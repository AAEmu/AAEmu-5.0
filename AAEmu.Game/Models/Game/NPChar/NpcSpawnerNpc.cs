using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawnerNpc : Spawner<Npc>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public uint NpcSpawnerTemplateId { get; set; } // spawner template id
    public uint MemberId { get; set; } // npc template id
    public string MemberType { get; set; } // 'Npc'
    public float Weight { get; set; }

    public NpcSpawnerNpc()
    {
    }

    /// <summary>
    /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
    /// </summary>
    /// <param name="spawnerTemplateId"></param>
    public NpcSpawnerNpc(uint spawnerTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
    }

    public NpcSpawnerNpc(uint spawnerTemplateId, uint memberId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
        MemberId = memberId;
        MemberType = "Npc";
    }

    public List<Npc> Spawn(NpcSpawner npcSpawner, uint ownerID = 0)
    {
        switch (MemberType)
        {
            case "Npc":
                return SpawnNpc(npcSpawner, ownerID);
            case "NpcGroup":
                return SpawnNpc(npcSpawner, ownerID);

            default:
                throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
        }
    }

    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint ownerID = 0)
    {
        var npcs = new List<Npc>();
        var npc = NpcManager.Instance.Create(0, MemberId);
        if (npc == null)
        {
            Logger.Warn($"Npc {MemberId}, from spawner Id {npcSpawner.Id} not exist at db. Spawner Position: {npcSpawner.Position}");
            return null;
        }

        npc.OwnerId = ownerID;
        npc.RegisterNpcEvents();

        Logger.Trace($"Spawn npc templateId {MemberId} objId {npc.ObjId} from spawnerId {NpcSpawnerTemplateId} at Position: {npcSpawner.Position}");

        // Resolve the spawn Z against the heightmap and the .bai navmesh.
        // See ResolveSpawnZ() below for the decision rules and rationale.
        // npc.ObjId is already assigned by NpcManager.Create, so we can record diagnostics
        // keyed by it.
        if (!npc.CanFly)
        {
            var spawnWorldObj = WorldManager.Instance.GetWorldByZone(npcSpawner.Position.ZoneId);
            npcSpawner.Position.Z = ResolveSpawnZ(
                spawnWorldObj,
                npcSpawner.Position.AsPositionVector(),
                npc.TemplateId,
                NpcSpawnerTemplateId,
                npc.ObjId);
        }

        if (npc.Transform == null)
        {
            Logger.Error($"Can't spawn npc {MemberId} from spawnerId {NpcSpawnerTemplateId}. Transform is null.");
            return null;
        }
        npc.Transform.ApplyWorldSpawnPosition(npcSpawner.Position);

        npc.Transform.InstanceId = npc.Transform.WorldId;
        npc.InstanceId = npc.Transform.WorldId;

        // Resolve spawn height and initialize HeightState anchor BEFORE setting HomePosition/IdlePosition.
        // Without this, the anchor defaults to Terrain and elevated-floor NPCs fall through raised geometry.
        var spawnWorld = WorldManager.Instance.GetWorld(npc.Transform.WorldId);
        if (spawnWorld != null && npc.Ai != null)
        {
            var spawnPos = npc.Transform.World.Position;
            var resolution = NpcHeightResolver.ResolveSpawnHeightResolution(npc, spawnPos, spawnWorld);
            var finalSpawnZ = resolution.Height;

            // Recorded-navmesh override (ground truth): if a player physically walked a floor at
            // this XY (/sg recording), trust it over the conservative spawn-snap guards, which
            // otherwise keep cave NPCs floating at a surface-resolved spawn Z. Only snaps DOWN, so
            // legitimately-elevated NPCs (no recorded floor beneath them) are untouched. This also
            // grounds HomePosition/IdlePosition below, fixing idle floaters.
            var navFloorZ = WorldManager.Instance.GetNearestNavFloor(npc.Transform.ZoneId, spawnPos.X, spawnPos.Y);
            if (!float.IsNaN(navFloorZ) && navFloorZ < spawnPos.Z - 1f)
                finalSpawnZ = navFloorZ;

            if (finalSpawnZ > 0f && MathF.Abs(finalSpawnZ - spawnPos.Z) > 0.01f)
                npc.Transform.Local.SetPosition(spawnPos.X, spawnPos.Y, finalSpawnZ);
        }

        if (npc.Ai != null)
        {
            npc.Ai.HomePosition = npc.Transform.World.Position;
            npc.Ai.IdlePosition = npc.Ai.HomePosition;
            npc.Ai.GoToSpawn();
        }

        npc.Spawner = npcSpawner;
        npc.Spawner.RespawnTime = (int)Rand.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
        npc.Spawn();

        var world = WorldManager.Instance.GetWorld(npc.Transform.WorldId);
        world.Events.OnUnitSpawn(world, new OnUnitSpawnArgs { Npc = npc });
        npc.Simulation = new Simulation(npc);

        if (npc.Ai != null && !string.IsNullOrWhiteSpace(npcSpawner.FollowPath))
        {
            if (!npc.Ai.LoadAiPathPoints(npcSpawner.FollowPath, false))
                Logger.Warn($"Failed to load {npcSpawner.FollowPath} for NPC {npc.TemplateId} ({npc.ObjId})");
        }

        npcs.Add(npc);
        return npcs;
    }

    private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner)
    {
        return SpawnNpc(npcSpawner);
    }

    /// <summary>
    /// Result of the spawn-Z candidate floor selection.
    /// </summary>
    private class SpawnZCandidate
    {
        public float Z;
        public string Source = string.Empty;
        public bool FromBai;
    }

    /// <summary>
    /// Resolves the final spawn Z of a non-flying NPC by reconciling three independent sources:
    /// the JSON-declared spawn Z, the terrain heightmap, and the .bai navmesh.
    ///
    /// Decision logic (intentionally conservative — the goal is to fix open-world spawns
    /// without re-snapping NPCs that are deliberately placed on structures):
    ///
    ///   1. Read heightmapZ from the bilinearly-interpolated terrain heightmap.
    ///   2. Ask the .bai navmesh for the nearest node in 2D (XY); record baiZ + distance + type.
    ///   3. The .bai value is "trusted" only if it is XY-close AND vertically close to heightmap.
    ///   4. Pick candidateFloorZ = trustedBaiZ ?? heightmapZ.
    ///   5. Guard: unmeshed structure detection (pier, dock, balcony).
    ///   6. Guard: asymmetric downward-snap cap.
    ///   7. Guard: snap budget. If exceeded, keep spawnZ.
    ///   8. Apply snap to candidateFloorZ.
    ///
    /// All thresholds are configurable via World.SpawnHeight in Configurations/World.json.
    /// </summary>
    private static float ResolveSpawnZ(AAEmu.Game.Models.Game.World.World world, Vector3 spawnPos, uint npcTemplateId, uint spawnerTemplateId, uint npcObjId)
    {
        if (world?.Template?.GeoData == null)
        {
            RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
                heightmapZ: float.NaN, hasHmap: false, bai: default,
                finalZ: spawnPos.Z, decision: "geo-data-unavailable");
            return spawnPos.Z;
        }

        var cfg = AppConfiguration.Instance.World?.SpawnHeight ?? new SpawnHeightConfig();

        // Legacy A/B test path.
        if (!cfg.Enabled || cfg.UseLegacyBai3DLookup)
            return ResolveSpawnZLegacy(world, spawnPos, npcTemplateId, spawnerTemplateId, npcObjId);

        var template = world.Template;

        // 1. Sample heightmap and .bai navmesh.
        var (heightmapZ, hasHeightmap) = SampleHeightmap(template, spawnPos, npcTemplateId);
        var bai = SampleBai(template, spawnPos, npcTemplateId);

        // 2. Select candidate floor (bai-trusted, heightmap, or none).
        var candidate = SelectCandidateFloor(cfg, spawnPos, heightmapZ, hasHeightmap, bai,
            npcTemplateId, spawnerTemplateId);
        if (candidate == null)
            return spawnPos.Z;

        // 3. Guard: unmeshed structure (pier, dock, balcony).
        if (IsUnmeshedStructure(cfg, spawnPos, heightmapZ, hasHeightmap, bai, npcTemplateId, spawnerTemplateId))
        {
            RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
                heightmapZ, hasHeightmap, bai, finalZ: spawnPos.Z, decision: "keep-unmeshed-structure");
            return spawnPos.Z;
        }

        // 4. Guard: asymmetric downward-snap cap.
        var downwardGap = spawnPos.Z - candidate.Z;
        if (downwardGap > cfg.MaxDownwardSnapDistance)
        {
            if (cfg.LogResolution)
                LogDownwardCap(cfg, spawnPos, npcTemplateId, spawnerTemplateId, downwardGap, heightmapZ, bai, candidate);
            RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
                heightmapZ, hasHeightmap, bai, finalZ: spawnPos.Z, decision: "keep-downward-cap");
            return spawnPos.Z;
        }

        // 5. Guard: snap budget.
        var absDelta = MathF.Abs(candidate.Z - spawnPos.Z);
        if (absDelta > cfg.MaxSnapDistance)
        {
            if (cfg.LogResolution)
                LogSnapBudgetExceeded(cfg, spawnPos, npcTemplateId, spawnerTemplateId, candidate, heightmapZ, bai);
            RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
                heightmapZ, hasHeightmap, bai, finalZ: spawnPos.Z, decision: "keep-spawn-budget");
            return spawnPos.Z;
        }

        // 6. Accept the snap.
        if (cfg.LogResolution)
            LogSnapAccepted(cfg, spawnPos, npcTemplateId, spawnerTemplateId, candidate, heightmapZ, bai);
        RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
            heightmapZ, hasHeightmap, bai, finalZ: candidate.Z,
            decision: candidate.FromBai ? "snap-bai" : "snap-heightmap");
        return candidate.Z;
    }

    private static float ResolveSpawnZLegacy(AAEmu.Game.Models.Game.World.World world, Vector3 spawnPos,
        uint npcTemplateId, uint spawnerTemplateId, uint npcObjId)
    {
        var zoneKey = world.ZoneKeys.FirstOrDefault();
        var legacyZ = world.Template.GeoData.GetHeightLegacy3D(zoneKey, spawnPos);
        var snapped = Math.Abs(spawnPos.Z - legacyZ) < 1f;
        var finalZ = snapped ? legacyZ : spawnPos.Z;
        RecordDiagnostic(npcObjId, npcTemplateId, spawnerTemplateId, spawnPos,
            heightmapZ: float.NaN, hasHmap: false, bai: default,
            finalZ: finalZ, decision: snapped ? "legacy-snap" : "legacy-keep");
        return finalZ;
    }

    private static (float z, bool valid) SampleHeightmap(WorldTemplate template, Vector3 spawnPos, uint npcTemplateId)
    {
        try
        {
            var z = template.GetHeight(spawnPos.X, spawnPos.Y);
            // Treat NaN, Infinity, and exactly 0 as invalid — 0 is the sentinel value returned by
            // WorldTemplate.GetHeight when the heightmap sample is outside the loaded terrain bounds.
            // This aligns with the validity check in AiGeoDataManager.GetHeight.
            return (z, !float.IsNaN(z) && !float.IsInfinity(z) && z != 0f);
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "Heightmap GetHeight failed at ({0:F2},{1:F2}) for NPC {2}", spawnPos.X, spawnPos.Y, npcTemplateId);
            return (float.NaN, false);
        }
    }

    private static AiGeoDataManager.BaiHeightLookup SampleBai(WorldTemplate template, Vector3 spawnPos, uint npcTemplateId)
    {
        try
        {
            return template.GeoData != null
                ? template.GeoData.GetHeightDetailed(spawnPos)
                : default;
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "GetHeightDetailed failed at ({0:F2},{1:F2}) for NPC {2}", spawnPos.X, spawnPos.Y, npcTemplateId);
            return default;
        }
    }

    private static SpawnZCandidate? SelectCandidateFloor(SpawnHeightConfig cfg, Vector3 spawnPos,
        float heightmapZ, bool hasHeightmap, AiGeoDataManager.BaiHeightLookup bai,
        uint npcTemplateId, uint spawnerTemplateId)
    {
        var baiTrusted = IsBaiTrusted(cfg, spawnPos, heightmapZ, hasHeightmap, bai);

        if (baiTrusted)
        {
            var candidate = new SpawnZCandidate
            {
                Z = bai.BaiZ,
                Source = $"bai({bai.Source},type={bai.NodeType})",
                FromBai = true
            };

            var preferHmap = AppConfiguration.Instance.World?.SpawnHeight?.PreferHeightmapForGroundZ ?? true;
            if (preferHmap && hasHeightmap)
            {
                var agree = MathF.Abs(bai.BaiZ - heightmapZ) <= cfg.MaxBaiVsHeightmapDelta;
                if (agree && heightmapZ < bai.BaiZ)
                {
                    candidate.Z = heightmapZ;
                    candidate.Source = "heightmap(anti-lev)";
                    candidate.FromBai = false;
                }
            }
            return candidate;
        }

        if (hasHeightmap)
            return new SpawnZCandidate { Z = heightmapZ, Source = "heightmap", FromBai = false };

        if (cfg.LogResolution)
            Logger.Debug("[SpawnZ] NPC {0} spawner {1} at ({2:F1},{3:F1}) — no floor data, keeping spawnZ={4:F2}",
                npcTemplateId, spawnerTemplateId, spawnPos.X, spawnPos.Y, spawnPos.Z);
        return null;
    }

    private static bool IsBaiTrusted(SpawnHeightConfig cfg, Vector3 spawnPos,
        float heightmapZ, bool hasHeightmap, AiGeoDataManager.BaiHeightLookup bai)
    {
        if (!bai.HasValue)
            return false;

        var withinXy = bai.Distance2D <= cfg.MaxBaiDistance2D;
        var consistentWithHmap = !hasHeightmap
                                 || MathF.Abs(bai.BaiZ - heightmapZ) <= cfg.MaxBaiVsHeightmapDelta;
        var consistentWithSpawnZ = cfg.TrustBaiNearSpawnZ
                                   && MathF.Abs(bai.BaiZ - spawnPos.Z) <= cfg.MaxSnapDistance;
        return withinXy && (consistentWithHmap || consistentWithSpawnZ);
    }

    private static bool IsUnmeshedStructure(SpawnHeightConfig cfg, Vector3 spawnPos,
        float heightmapZ, bool hasHeightmap, AiGeoDataManager.BaiHeightLookup bai,
        uint npcTemplateId, uint spawnerTemplateId)
    {
        if (!hasHeightmap || !bai.HasValue)
            return false;

        var hmapBaiAgree = MathF.Abs(bai.BaiZ - heightmapZ) <= cfg.MaxBaiVsHeightmapDelta;
        var highestFloor = MathF.Max(heightmapZ, bai.BaiZ);
        var spawnAboveFloor = spawnPos.Z - highestFloor;

        if (hmapBaiAgree && spawnAboveFloor > cfg.UnmeshedStructureThreshold)
        {
            if (cfg.LogResolution)
                Logger.Debug(
                    "[SpawnZ] NPC {0} spawner {1} at ({2:F1},{3:F1}) — unmeshed structure detected (spawnZ={4:F2} above max(hmap={5:F2}, bai={6:F2}) by {7:F2}m). Keeping spawnZ.",
                    npcTemplateId, spawnerTemplateId, spawnPos.X, spawnPos.Y,
                    spawnPos.Z, heightmapZ, bai.BaiZ, spawnAboveFloor);
            return true;
        }
        return false;
    }

    private static void LogDownwardCap(SpawnHeightConfig cfg, Vector3 spawnPos,
        uint npcTemplateId, uint spawnerTemplateId, float downwardGap,
        float heightmapZ, AiGeoDataManager.BaiHeightLookup bai, SpawnZCandidate candidate)
    {
        Logger.Debug(
            "[SpawnZ] NPC {0} spawner {1} at ({2:F1},{3:F1}) — downward gap {4:F2}m exceeds MaxDownwardSnapDistance {5:F2}m. Keeping spawnZ={6:F2} (presumed unmeshed sub-structure; heightmap={7:F2}, bai={8:F2} d2D={9:F2} type={10}, source={11}).",
            npcTemplateId, spawnerTemplateId, spawnPos.X, spawnPos.Y, downwardGap, cfg.MaxDownwardSnapDistance,
            spawnPos.Z, heightmapZ, bai.HasValue ? bai.BaiZ : 0f,
            bai.HasValue ? bai.Distance2D : 0f, bai.HasValue ? bai.NodeType : (byte)255, candidate.Source);
    }

    private static void LogSnapBudgetExceeded(SpawnHeightConfig cfg, Vector3 spawnPos,
        uint npcTemplateId, uint spawnerTemplateId, SpawnZCandidate candidate,
        float heightmapZ, AiGeoDataManager.BaiHeightLookup bai)
    {
        var delta = candidate.Z - spawnPos.Z;
        Logger.Debug(
            "[SpawnZ] NPC {0} spawner {1} at ({2:F1},{3:F1}) — delta {4:F2}m exceeds budget {5:F1}m. Keeping spawnZ={6:F2} (heightmap={7:F2}, bai={8:F2} d2D={9:F2} type={10}, source={11}).",
            npcTemplateId, spawnerTemplateId, spawnPos.X, spawnPos.Y, delta, cfg.MaxSnapDistance,
            spawnPos.Z, heightmapZ, bai.HasValue ? bai.BaiZ : 0f,
            bai.HasValue ? bai.Distance2D : 0f, bai.HasValue ? bai.NodeType : (byte)255, candidate.Source);
    }

    private static void LogSnapAccepted(SpawnHeightConfig cfg, Vector3 spawnPos,
        uint npcTemplateId, uint spawnerTemplateId, SpawnZCandidate candidate,
        float heightmapZ, AiGeoDataManager.BaiHeightLookup bai)
    {
        var delta = candidate.Z - spawnPos.Z;
        Logger.Debug(
            "[SpawnZ] NPC {0} spawner {1} at ({2:F1},{3:F1}) — snapped {4:F2} -> {5:F2} (delta {6:F2}m, source={7}, heightmap={8:F2}, bai={9:F2} d2D={10:F2} type={11}).",
            npcTemplateId, spawnerTemplateId, spawnPos.X, spawnPos.Y,
            spawnPos.Z, candidate.Z, delta, candidate.Source,
            heightmapZ, bai.HasValue ? bai.BaiZ : 0f,
            bai.HasValue ? bai.Distance2D : 0f, bai.HasValue ? bai.NodeType : (byte)255);
    }

    /// <summary>
    /// Pushes a diagnostic entry into the SpawnHeightDiagnostics singleton so that
    /// /heightmob target can later read what was decided for this exact NPC at spawn time,
    /// independently of where the NPC has moved since.
    /// </summary>
    private static void RecordDiagnostic(uint npcObjId, uint npcTemplateId, uint spawnerTemplateId,
        Vector3 spawnPos, float heightmapZ, bool hasHmap,
        AiGeoDataManager.BaiHeightLookup bai,
        float finalZ, string decision)
    {
        if (npcObjId == 0)
            return;

        SpawnHeightDiagnostics.Instance.Record(npcObjId, new SpawnHeightDiagnostics.Entry
        {
            NpcTemplateId = npcTemplateId,
            SpawnerTemplateId = spawnerTemplateId,
            SpawnPos = spawnPos,
            OriginalSpawnZ = spawnPos.Z,
            HeightmapZ = heightmapZ,
            HasHeightmap = hasHmap,
            BaiHasValue = bai.HasValue,
            BaiZ = bai.HasValue ? bai.BaiZ : 0f,
            BaiDistance2D = bai.HasValue ? bai.Distance2D : 0f,
            BaiNodeType = bai.HasValue ? bai.NodeType : (byte)255,
            BaiSource = bai.HasValue ? (bai.Source ?? string.Empty) : string.Empty,
            FinalZ = finalZ,
            Decision = decision ?? string.Empty,
        });
    }
}
