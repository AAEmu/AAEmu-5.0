using System;
using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Physics;
using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Centralizes NPC height resolution for spawn, idle, roaming, combat and return.
/// Simple rule: water/air are special; terrain uses heightmap; everything else uses
/// the world raycast (WorldStaticGeometryIndex fallback).
/// </summary>
public static class NpcHeightResolver
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const float InteriorFloorNodeSearchRadius = 3.0f;
    private const float InteriorFloorNodeHeightTolerance = 0.9f;
    private const int VisibilityReconcileDiagnosticLimit = 200;
    private const int VisibilityProbeDiagnosticLimit = 60;
    private const int VisibilityGlobalTerrainProbeDiagnosticLimit = 200;
    private const float VisibilityGlobalTerrainProbeMinDelta = 2.0f;
    private const float HaranyaImperialSquareMinX = 19180f;
    private const float HaranyaImperialSquareMaxX = 19390f;
    private const float HaranyaImperialSquareMinY = 8430f;
    private const float HaranyaImperialSquareMaxY = 8620f;
    private static int _visibilityReconcileDiagnosticCount;
    private static int _visibilityProbeDiagnosticCount;
    private static int _visibilityGlobalTerrainProbeDiagnosticCount;
    private static ConcurrentDictionary<VisibilityTerrainProbeKey, byte> VisibilityGlobalTerrainProbeSeen { get; } = [];

    private readonly record struct VisibilityTerrainProbeKey(
        uint WorldTemplateId,
        uint NpcTemplateId,
        int XDecimeter,
        int YDecimeter,
        int SpawnerZDecimeter);

    public static float ResolveSpawnHeight(Npc npc, Vector3 spawnPosition, InstanceWorld world, bool skipElevatedProbe = false)
    {
        return ResolveSpawnHeightResolution(npc, spawnPosition, world, skipElevatedProbe).Height;
    }

    public static NpcSpawnHeightResolution ResolveSpawnHeightResolution(
        Npc npc, Vector3 spawnPosition, InstanceWorld world, bool skipElevatedProbe = false)
    {
        if (npc == null || world == null)
            return new NpcSpawnHeightResolution(spawnPosition.Z, NpcHeightAnchorKind.Unknown);

        var resolved = NpcSpawnHeightOverrides.GetSpawnHeight(
            npc, spawnPosition.X, spawnPosition.Y, spawnPosition.Z, world, skipElevatedProbe);

        CaptureAnchor(npc, world, spawnPosition, resolved.Height, resolved.AnchorKind, NpcHeightResolutionContext.Spawn);
        return resolved;
    }

    public static float ResolveHeight(NpcAi ai, Vector3 pos, InstanceWorld world, NpcHeightResolutionContext context)
    {
        if (world == null)
            return pos.Z;

        if (ai?.Owner == null)
            return world.GetHeight(pos.X, pos.Y);

        var npc = ai.Owner;
        var state = ai.HeightState;

        // Water / air — dedicated paths.
        if (TryResolveWaterHeight(npc, world, pos, out var waterHeight))
            return waterHeight;

        if (npc.CanFly && npc.CurrentAggroTarget == null)
        {
            var spawnerHeight = npc.Spawner?.Position.Z ?? (ai.HomePosition != default ? ai.HomePosition.Z : pos.Z);
            var anchorHeight = state.IsInitialized ? state.AnchorZ : MathF.Max(pos.Z, spawnerHeight);
            return MathF.Max(anchorHeight, world.Template?.GetHeight(pos.X, pos.Y) ?? pos.Z);
        }

        // Hot path: MovementStep must be cheap and stall-free.
        if (context == NpcHeightResolutionContext.MovementStep)
        {
            var terrain = world.GetHeight(pos.X, pos.Y);
            if (terrain < 0f)
                return state.IsInitialized ? state.LastResolvedZ : pos.Z;

            if (state is { IsInitialized: true })
            {
                if (state.AnchorKind == NpcHeightAnchorKind.Terrain)
                {
                    state.UpdateLastResolved(terrain);
                    return terrain;
                }

                // Locked layers (interior floors, water, air).
                if (state.LayerLocked)
                {
                    if (state.AnchorKind == NpcHeightAnchorKind.InteriorFloor)
                    {
                        var candidateZ = pos.Z;
                        if (!(candidateZ > 0f) || float.IsNaN(candidateZ) || float.IsInfinity(candidateZ))
                            return state.LastResolvedZ;

                        const float InteriorMismatchUnlockDeltaToTerrain = 1.25f;
                        const int InteriorEvidenceProbeCooldownTicksWhenConfirmed = 200;
                        var absDeltaToTerrain = MathF.Abs(state.LastResolvedZ - terrain);
                        if (absDeltaToTerrain > InteriorMismatchUnlockDeltaToTerrain)
                        {
                            if (state.ShouldProbeInteriorEvidence())
                            {
                                if (TryGetInteriorFloorEvidenceNearAnchor(world, pos, state.LastResolvedZ, out var hasEvidence))
                                {
                                    if (!hasEvidence)
                                    {
                                        state.SwitchToTerrain(terrain);
                                        return terrain;
                                    }

                                    state.SetInteriorEvidenceProbeCooldown(InteriorEvidenceProbeCooldownTicksWhenConfirmed);
                                }
                                else
                                {
                                    state.SetInteriorEvidenceProbeCooldown(50);
                                }
                            }
                        }

                        const float InteriorUnlockNearTerrainEps = 0.35f;
                        const int InteriorUnlockNearTerrainTicks = 20;
                        var layerNearTerrain = MathF.Abs(state.LastResolvedZ - terrain) <= InteriorUnlockNearTerrainEps;
                        var candidateNearTerrain = MathF.Abs(candidateZ - terrain) <= InteriorUnlockNearTerrainEps;
                        state.TrackNearTerrain(layerNearTerrain && candidateNearTerrain);
                        if (state.NearTerrainTicks >= InteriorUnlockNearTerrainTicks)
                        {
                            if (TryGetInteriorFloorEvidenceNearAnchor(world, pos, state.LastResolvedZ, out var hasEvidence) && !hasEvidence)
                            {
                                state.SwitchToTerrain(terrain);
                                return terrain;
                            }

                            state.TrackNearTerrain(false);
                        }

                        const float TerrainSnapThreshold = 0.75f;
                        if (state.LastResolvedZ > terrain + 0.1f && candidateZ <= terrain + TerrainSnapThreshold)
                            return state.LastResolvedZ;
                        if (state.LastResolvedZ < terrain - 0.1f && candidateZ >= terrain - TerrainSnapThreshold)
                            return state.LastResolvedZ;

                        const float MaxPerStepDelta = 0.75f;
                        var dz = candidateZ - state.LastResolvedZ;
                        if (MathF.Abs(dz) <= MaxPerStepDelta)
                        {
                            state.UpdateLastResolved(candidateZ);
                            return candidateZ;
                        }

                        var adjusted = state.LastResolvedZ + MathF.CopySign(MaxPerStepDelta, dz);
                        state.UpdateLastResolved(adjusted);
                        return adjusted;
                    }

                    return state.LastResolvedZ;
                }

                if (MathF.Abs(state.LastResolvedZ - terrain) <= 1.0f)
                {
                    state.UpdateLastResolved(terrain);
                    return terrain;
                }

                return state.LastResolvedZ;
            }

            return terrain;
        }

        // Terrain-anchored NPCs: fast heightmap path.
        if (state is { IsInitialized: true, AnchorKind: NpcHeightAnchorKind.Terrain })
        {
            var hmap = world.GetHeight(pos.X, pos.Y);
            if (hmap >= 0f)
                return hmap;
        }

        // Interior/cave layer: prefer static-geometry nodes.
        if (state is { IsInitialized: true, AnchorKind: NpcHeightAnchorKind.InteriorFloor, LayerLocked: true })
        {
            if (TryResolveInteriorFloorHeight(world, pos, state.AnchorZ, out var interiorHeight))
            {
                state.UpdateLastResolved(interiorHeight);
                return interiorHeight;
            }

            return state.LastResolvedZ > 0f ? state.LastResolvedZ : state.AnchorZ;
        }

        // Everything else: use heightmap.
        var solidFloor = world.GetHeight(pos.X, pos.Y);
        return solidFloor >= 0f ? solidFloor : pos.Z;
    }

    public static Vector3 ResolveAnchoredPosition(NpcAi ai, Vector3 pos, InstanceWorld world, NpcHeightResolutionContext context)
    {
        return pos with { Z = ResolveHeight(ai, pos, world, context) };
    }

    internal static bool TryPromoteTerrainNpcToElevatedFloorForVisibility(Npc npc)
    {
        return TryPromoteTerrainNpcToElevatedFloorForVisibility(npc, npc?.ParentWorld, ResolveVisibilitySpawnHeight);
    }

    public static void EnsureAnchorInitialized(Npc npc, InstanceWorld world)
    {
        if (npc?.Ai == null || world == null || npc.Ai.HeightState.IsInitialized)
            return;

        var pos = npc.Transform.World.Position;
        CaptureAnchor(npc, world, pos, pos.Z, NpcHeightAnchorKind.Unknown, NpcHeightResolutionContext.IdleAnchor);
    }

    private static void CaptureAnchor(Npc npc, InstanceWorld world, Vector3 referencePosition, float resolvedHeight, NpcHeightAnchorKind anchorKind, NpcHeightResolutionContext context)
    {
        if (npc?.Ai == null || world == null)
            return;

        if (anchorKind == NpcHeightAnchorKind.Unknown)
            anchorKind = DetermineAnchorKind(world, referencePosition with { Z = resolvedHeight }, resolvedHeight);

        var layerLocked = anchorKind is NpcHeightAnchorKind.InteriorFloor or NpcHeightAnchorKind.WaterSurface
            or NpcHeightAnchorKind.WaterColumn or NpcHeightAnchorKind.Air;
        npc.Ai.HeightState.Capture(resolvedHeight, anchorKind, layerLocked);
    }

    private static NpcHeightAnchorKind DetermineAnchorKind(InstanceWorld world, Vector3 pos, float resolvedHeight)
    {
        if (world == null)
            return NpcHeightAnchorKind.Unknown;

        var mapHeight = world.Template?.GetHeight(pos.X, pos.Y) ?? resolvedHeight;
        if (MathF.Abs(resolvedHeight - mapHeight) <= 0.75f)
            return NpcHeightAnchorKind.Terrain;

        return NpcHeightAnchorKind.Spawner;
    }

    private static bool TryResolveWaterHeight(Npc npc, InstanceWorld world, Vector3 pos, out float waterHeight)
    {
        waterHeight = 0f;
        if (npc == null || world == null)
            return false;

        if (NpcWaterSurfaceOverrides.IsWaterSurfaceNpc(npc))
        {
            waterHeight = world.Template.OceanLevel;
            return true;
        }

        if (NpcWaterSurfaceOverrides.IsWaterNpc(npc))
        {
            var terrain = world.GetHeight(pos.X, pos.Y);
            waterHeight = MathF.Max(terrain, world.Template.OceanLevel);
            return true;
        }

        return false;
    }

    internal static bool TryPromoteTerrainNpcToElevatedFloorForVisibility(
        Npc npc,
        InstanceWorld world,
        Func<Npc, Vector3, InstanceWorld, NpcSpawnHeightResolution> resolver)
    {
        if (npc?.Ai == null || world?.Template == null || resolver == null)
            return false;

        if (npc.CanFly || NpcWaterSurfaceOverrides.IsWaterNpc(npc))
            return false;

        var state = npc.Ai.HeightState;
        if (!state.IsInitialized || state.AnchorKind != NpcHeightAnchorKind.Terrain)
            return false;

        var spawnerPosition = npc.Spawner?.Position;
        if (spawnerPosition == null)
            return false;

        var currentPosition = npc.Transform.World.Position;
        var distanceToSpawnerSq = Vector2.DistanceSquared(
            new Vector2(currentPosition.X, currentPosition.Y),
            new Vector2(spawnerPosition.X, spawnerPosition.Y));
        const float MaxVisibilityReconcileDistanceFromSpawner = 5f;
        var terrainHeight = world.Template.GetHeight(currentPosition.X, currentPosition.Y);
        const float MinimumElevatedDeltaToTerrain = 0.5f;
        var shouldLogCandidate = spawnerPosition.Z > terrainHeight + MinimumElevatedDeltaToTerrain;

        if (distanceToSpawnerSq > MaxVisibilityReconcileDistanceFromSpawner * MaxVisibilityReconcileDistanceFromSpawner)
        {
            if (shouldLogCandidate)
            {
                TryLogVisibilityReconcileDiagnostic(
                    npc, world, currentPosition, spawnerPosition, terrainHeight,
                    $"skip distance_from_spawner={MathF.Sqrt(distanceToSpawnerSq):F2}m");
            }
            return false;
        }

        if (spawnerPosition.Z <= terrainHeight + MinimumElevatedDeltaToTerrain)
            return false;

        const float TerrainSnapThreshold = 0.75f;
        if (currentPosition.Z > terrainHeight + TerrainSnapThreshold)
        {
            TryLogVisibilityReconcileDiagnostic(
                npc, world, currentPosition, spawnerPosition, terrainHeight,
                $"skip current_z_above_terrain currentZ={currentPosition.Z:F2}");
            return false;
        }

        var cellX = (int)(currentPosition.X / WorldManager.CELL_SIZE);
        var cellY = (int)(currentPosition.Y / WorldManager.CELL_SIZE);
        var cell = world.Template.GetCell(cellX, cellY);
        if (cell == null || !cell.Loaded)
        {
            TryLogVisibilityReconcileDiagnostic(
                npc, world, currentPosition, spawnerPosition, terrainHeight,
                $"skip cell_missing_or_not_loaded cell=({cellX},{cellY}) loaded={cell?.Loaded.ToString() ?? "null"}");
            return false;
        }

        // In the target codebase, terrain collision readiness is not tracked per-cell.
        // We proceed assuming collision is ready if the cell is loaded.

        var resolution = resolver(npc, currentPosition, world);
        if (resolution.AnchorKind != NpcHeightAnchorKind.InteriorFloor)
        {
            TryLogVisibilityReconcileDiagnostic(
                npc, world, currentPosition, spawnerPosition, terrainHeight,
                $"skip resolver_anchor={resolution.AnchorKind} resolverZ={resolution.Height:F2}",
                resolution);
            return false;
        }

        if (resolution.Height <= terrainHeight + MinimumElevatedDeltaToTerrain)
        {
            TryLogVisibilityReconcileDiagnostic(
                npc, world, currentPosition, spawnerPosition, terrainHeight,
                $"skip resolver_height_near_terrain resolverZ={resolution.Height:F2}",
                resolution);
            return false;
        }

        if (resolution.Height <= currentPosition.Z + 0.25f)
        {
            TryLogVisibilityReconcileDiagnostic(
                npc, world, currentPosition, spawnerPosition, terrainHeight,
                $"skip resolver_height_not_above_current resolverZ={resolution.Height:F2}",
                resolution);
            return false;
        }

        var rotation = npc.Transform.Local.Rotation;
        npc.SetPosition(currentPosition.X, currentPosition.Y, resolution.Height, rotation.X, rotation.Y, rotation.Z);
        npc.Ai.HeightState.SwitchToInteriorFloor(resolution.Height, resolution.Height);

        const float AnchorPositionUpdateRadius = 2f;
        var anchorUpdateRadiusSq = AnchorPositionUpdateRadius * AnchorPositionUpdateRadius;
        var homePosition = npc.Ai.HomePosition;
        if (Vector2.DistanceSquared(
                new Vector2(homePosition.X, homePosition.Y),
                new Vector2(currentPosition.X, currentPosition.Y)) <= anchorUpdateRadiusSq)
        {
            npc.Ai.HomePosition = new Vector3(homePosition.X, homePosition.Y, resolution.Height);
        }

        var idlePosition = npc.Ai.IdlePosition;
        if (Vector2.DistanceSquared(
                new Vector2(idlePosition.X, idlePosition.Y),
                new Vector2(currentPosition.X, currentPosition.Y)) <= anchorUpdateRadiusSq)
        {
            npc.Ai.IdlePosition = new Vector3(idlePosition.X, idlePosition.Y, resolution.Height);
        }

        TryLogVisibilityReconcileDiagnostic(
            npc, world, currentPosition, spawnerPosition, terrainHeight,
            $"PROMOTED newZ={resolution.Height:F2}",
            resolution);

        return true;
    }

    private static void TryLogVisibilityReconcileDiagnostic(
        Npc npc,
        InstanceWorld world,
        Vector3 currentPosition,
        WorldSpawnPosition spawnerPosition,
        float terrainHeight,
        string reason,
        NpcSpawnHeightResolution? resolution = null)
    {
        if (!Logger.IsDebugEnabled)
            return;

        var idx = System.Threading.Interlocked.Increment(ref _visibilityReconcileDiagnosticCount);
        if (idx > VisibilityReconcileDiagnosticLimit)
            return;

        var entryCount = world?.Physics?.StaticGeometryIndex?.GetEntryCountAt(currentPosition.X, currentPosition.Y) ?? -1;
        var totalBrushes = world?.Physics?.StaticGeometryIndex?.TotalBrushCount ?? -1;
        var totalVoxels = world?.Physics?.StaticGeometryIndex?.TotalVoxelCount ?? -1;
        var state = npc?.Ai?.HeightState;
        var resolutionText = resolution.HasValue
            ? $" resolver=({resolution.Value.AnchorKind},{resolution.Value.Height:F2})"
            : string.Empty;
        var probeText = TryBuildTargetedProbeDiagnostic(world, currentPosition, spawnerPosition, terrainHeight, reason);

        Logger.Debug(
            $"[VisibilityReconcile #{idx}] npc={npc?.ObjId ?? 0}/{npc?.TemplateId ?? 0} " +
            $"pos=({currentPosition.X:F1},{currentPosition.Y:F1},{currentPosition.Z:F2}) " +
            $"terrain={terrainHeight:F2} spawnerZ={spawnerPosition?.Z ?? 0f:F2} " +
            $"state={state?.AnchorKind.ToString() ?? "null"} " +
            $"entries={entryCount} total={totalBrushes}+v{totalVoxels} -> {reason}{resolutionText}{probeText}");

        TryLogGlobalSuspiciousTerrainProbeDiagnostic(
            npc, world, currentPosition, spawnerPosition, terrainHeight, reason, resolution);
    }

    private static void TryLogGlobalSuspiciousTerrainProbeDiagnostic(
        Npc npc,
        InstanceWorld world,
        Vector3 currentPosition,
        WorldSpawnPosition spawnerPosition,
        float terrainHeight,
        string reason,
        NpcSpawnHeightResolution? resolution)
    {
        if (!Logger.IsDebugEnabled)
            return;

        if (npc == null || world?.Template == null || spawnerPosition == null)
            return;

        if (!reason.StartsWith("skip resolver_anchor=Terrain", StringComparison.Ordinal))
            return;

        var deltaToTerrain = spawnerPosition.Z - terrainHeight;
        if (deltaToTerrain < VisibilityGlobalTerrainProbeMinDelta)
            return;

        var key = new VisibilityTerrainProbeKey(
            world.TemplateId,
            (uint)npc.TemplateId,
            QuantizeToDecimeter(currentPosition.X),
            QuantizeToDecimeter(currentPosition.Y),
            QuantizeToDecimeter(spawnerPosition.Z));
        if (!VisibilityGlobalTerrainProbeSeen.TryAdd(key, 0))
            return;

        var idx = System.Threading.Interlocked.Increment(ref _visibilityGlobalTerrainProbeDiagnosticCount);
        if (idx > VisibilityGlobalTerrainProbeDiagnosticLimit)
            return;

        var probeDiagnostic = NpcSpawnHeightOverrides.BuildElevatedProbeDiagnostic(
            world, currentPosition.X, currentPosition.Y, spawnerPosition.Z, terrainHeight);
        var resolutionText = resolution.HasValue
            ? $" resolver=({resolution.Value.AnchorKind},{resolution.Value.Height:F2})"
            : string.Empty;
        var region = BuildKilometerRegionTag(currentPosition.X, currentPosition.Y);

        Logger.Debug(
            $"[TerrainProbeGlobal #{idx}] world={world} region={region} npc={npc.ObjId}/{npc.TemplateId} " +
            $"pos=({currentPosition.X:F1},{currentPosition.Y:F1},{currentPosition.Z:F2}) " +
            $"terrain={terrainHeight:F2} spawnerZ={spawnerPosition.Z:F2} delta={deltaToTerrain:F2} " +
            $"reason={reason}{resolutionText} | {probeDiagnostic}");
    }

    private static string TryBuildTargetedProbeDiagnostic(
        InstanceWorld world,
        Vector3 currentPosition,
        WorldSpawnPosition spawnerPosition,
        float terrainHeight,
        string reason)
    {
        if (world?.Template == null || spawnerPosition == null)
            return string.Empty;

        if (!string.Equals(world.Template.Name, "main_world", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        if (currentPosition.X < HaranyaImperialSquareMinX || currentPosition.X > HaranyaImperialSquareMaxX)
            return string.Empty;

        if (currentPosition.Y < HaranyaImperialSquareMinY || currentPosition.Y > HaranyaImperialSquareMaxY)
            return string.Empty;

        if (spawnerPosition.Z <= terrainHeight + 1.5f)
            return string.Empty;

        if (!reason.StartsWith("skip resolver_anchor=", StringComparison.Ordinal))
            return string.Empty;

        var probeIdx = System.Threading.Interlocked.Increment(ref _visibilityProbeDiagnosticCount);
        if (probeIdx > VisibilityProbeDiagnosticLimit)
            return string.Empty;

        var probeDiagnostic = NpcSpawnHeightOverrides.BuildElevatedProbeDiagnostic(
            world, currentPosition.X, currentPosition.Y, spawnerPosition.Z, terrainHeight);
        if (string.IsNullOrEmpty(probeDiagnostic))
            return string.Empty;

        return $" | probe#{probeIdx}: {probeDiagnostic}";
    }

    private static int QuantizeToDecimeter(float value)
    {
        return (int)MathF.Round(value * 10.0f);
    }

    private static string BuildKilometerRegionTag(float x, float y)
    {
        var regionX = (int)MathF.Floor(x / 1000.0f);
        var regionY = (int)MathF.Floor(y / 1000.0f);
        return $"{regionX:D2}k_{regionY:D2}k";
    }

    private static NpcSpawnHeightResolution ResolveVisibilitySpawnHeight(Npc npc, Vector3 currentPosition, InstanceWorld world)
    {
        var spawnerZ = npc?.Spawner?.Position?.Z ?? currentPosition.Z;
        return NpcSpawnHeightOverrides.GetSpawnHeight(npc, currentPosition.X, currentPosition.Y, spawnerZ, world, skipElevatedProbe: false);
    }

    private static bool TryGetInteriorFloorEvidenceNearAnchor(InstanceWorld world, Vector3 pos, float anchorZ, out bool hasEvidence)
    {
        hasEvidence = false;
        if (world?.Template == null || world.Physics?.StaticGeometryIndex == null)
            return false;

        var probe = pos with { Z = anchorZ };
        if (!StaticGeoDataService.TryResolveDistinctLayerHeight(
                world, probe, InteriorFloorNodeSearchRadius, anchorZ, InteriorFloorNodeHeightTolerance, out var interiorHeight))
        {
            hasEvidence = false;
            return true;
        }

        hasEvidence = MathF.Abs(interiorHeight - anchorZ) <= InteriorFloorNodeHeightTolerance;
        return true;
    }

    private static bool TryResolveInteriorFloorHeight(InstanceWorld world, Vector3 pos, float anchorZ, out float height)
    {
        height = 0f;
        if (world?.Template == null || world.Physics?.StaticGeometryIndex == null)
            return false;

        var probe = pos with { Z = anchorZ };
        if (!StaticGeoDataService.TryResolveDistinctLayerHeight(
                world, probe, InteriorFloorNodeSearchRadius, anchorZ, InteriorFloorNodeHeightTolerance, out var interiorHeight))
        {
            return false;
        }

        var layerDelta = MathF.Abs(interiorHeight - anchorZ);
        if (layerDelta > InteriorFloorNodeHeightTolerance)
        {
            height = anchorZ;
            return true;
        }

        height = interiorHeight;
        return height > 0f;
    }
}
