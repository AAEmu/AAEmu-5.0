using System;
using System.Numerics;

using AAEmu.Game.Physics;
using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.NPChar;

public readonly record struct NpcSpawnHeightResolution(float Height, NpcHeightAnchorKind AnchorKind);

/// <summary>
/// Resolves the spawn Z for NPCs.
/// Mirrors the original ArcheAge zone server logic:
/// - Default: terrain height (server NPC Z must match heightmap to avoid visible floating vs players).
/// - Cave exception: if spawner Z is significantly below terrain, trust spawner Z.
/// - Interior exception: if spawner Z differs from terrain and static world geometry confirms a separate walkable layer,
///   keep that layer height to avoid dropping NPCs through buildings/caves.
/// We intentionally avoid expensive brush/triangle probing during mass spawns.
/// </summary>
public static class NpcSpawnHeightOverrides
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const float TerrainEpsilon = 0.0f;
    private const float CaveThreshold = 1.25f;
    private const float TerrainSnapThreshold = 0.75f;
    private const float InteriorFloorDeltaThreshold = 0.5f;
    private const float InteriorFloorNodeSearchRadius = 3.0f;
    private const float InteriorFloorNodeHeightTolerance = 0.9f;
    private const float BrushProbeStartOffsetUp = 3.0f;
    private const float BrushProbeMinElevation = 0.5f;
    private const float BrushAcceptZTolerance = 2.0f;
    private const float BrushElevationTrustThreshold = 2.0f;

    // Rate-limited brush probe diagnostic log counter.
    private static readonly int BrushProbeMaxDiagnosticLogs = 0;
    private static int _brushProbeDiagnosticCount;

    public static NpcSpawnHeightResolution GetSpawnHeight(
        Npc npc, float spawnerX, float spawnerY, float spawnerZ,
        InstanceWorld world, bool skipElevatedProbe = false)
    {
        if (npc == null || world == null)
            return new NpcSpawnHeightResolution(spawnerZ, NpcHeightAnchorKind.Unknown);

        // 1. Forced height from template/config.
        if (TryGetForcedSpawnHeight(npc, out var forcedHeight))
            return new NpcSpawnHeightResolution(forcedHeight, NpcHeightAnchorKind.Spawner);

        // 2. Water NPCs.
        if (TryGetWaterSurfaceSpawnHeight(npc, spawnerX, spawnerY, world, out var waterHeight))
        {
            var waterAnchor = NpcWaterSurfaceOverrides.IsWaterSurfaceNpc(npc)
                ? NpcHeightAnchorKind.WaterSurface
                : NpcHeightAnchorKind.WaterColumn;
            return new NpcSpawnHeightResolution(waterHeight, waterAnchor);
        }

        // 3. Flying NPCs.
        if (npc.CanFly)
        {
            // Use world.GetHeight (World.HeightMaps, loaded at startup) rather than
            // world.Template.GetHeight (per-cell WorldCell.HeightMap), which may return 0
            // when per-cell heightmap.dat files are absent from the client data bundle.
            var terrainH = world.GetHeight(spawnerX, spawnerY);
            if (terrainH <= 0f) terrainH = spawnerZ;
            return new NpcSpawnHeightResolution(MathF.Max(spawnerZ, terrainH), NpcHeightAnchorKind.Air);
        }

        var terrainHeight = world.GetHeight(spawnerX, spawnerY);
        if (terrainHeight <= 0f) terrainHeight = spawnerZ;

        // 4. Cave exception: spawner is clearly below terrain (underground layer).
        if (spawnerZ < terrainHeight - CaveThreshold)
        {
            if (world.Physics?.StaticGeometryIndex == null)
                return new NpcSpawnHeightResolution(spawnerZ, NpcHeightAnchorKind.InteriorFloor);

            if (TryResolveInteriorFloorSpawnHeight(world, spawnerX, spawnerY, spawnerZ, out var caveInteriorHeight))
                return new NpcSpawnHeightResolution(caveInteriorHeight, NpcHeightAnchorKind.InteriorFloor);

            return new NpcSpawnHeightResolution(terrainHeight + TerrainEpsilon, NpcHeightAnchorKind.Terrain);
        }

        // Fast path: spawner already matches terrain.
        if (skipElevatedProbe || MathF.Abs(spawnerZ - terrainHeight) <= TerrainSnapThreshold)
            return new NpcSpawnHeightResolution(terrainHeight + TerrainEpsilon, NpcHeightAnchorKind.Terrain);

        // 5. Interior/elevated layer check.
        var deltaToTerrain = spawnerZ - terrainHeight;
        if (MathF.Abs(deltaToTerrain) >= InteriorFloorDeltaThreshold &&
            TryResolveInteriorFloorSpawnHeight(world, spawnerX, spawnerY, spawnerZ, out var interiorHeight))
        {
            return new NpcSpawnHeightResolution(interiorHeight, NpcHeightAnchorKind.InteriorFloor);
        }

        // 5.5. Elevated open-air brush geometry (paved plazas, bridges, ramparts, stairs).
        if (!skipElevatedProbe && deltaToTerrain >= BrushProbeMinElevation)
        {
            var probeResult = TryResolveElevatedBrushSpawnHeightWithDiagAndAnchor(
                world, spawnerX, spawnerY, spawnerZ, terrainHeight,
                out var brushHeight, out var diagReason, out var isDesignTrust);

            if (BrushProbeMaxDiagnosticLogs > 0 && Logger.IsDebugEnabled)
            {
                var idx = System.Threading.Interlocked.Increment(ref _brushProbeDiagnosticCount);
                if (idx <= BrushProbeMaxDiagnosticLogs)
                {
                    var idxCount = world.Physics?.StaticGeometryIndex?.GetEntryCountAt(spawnerX, spawnerY) ?? -1;
                    var totalBrushes = world.Physics?.StaticGeometryIndex?.TotalBrushCount ?? -1;
                    var totalVoxels = world.Physics?.StaticGeometryIndex?.TotalVoxelCount ?? -1;
                    Logger.Debug(
                        $"[BrushProbe #{idx}] spawner=({spawnerX:F1},{spawnerY:F1},{spawnerZ:F2}) terrain={terrainHeight:F2} Δ={deltaToTerrain:F2}m [gridCellEntries={idxCount}, worldTotal={totalBrushes}+v{totalVoxels}] → {diagReason}");
                }
            }

            if (probeResult)
            {
                var anchorKind = isDesignTrust
                    ? NpcHeightAnchorKind.Spawner
                    : NpcHeightAnchorKind.InteriorFloor;
                return new NpcSpawnHeightResolution(brushHeight, anchorKind);
            }
        }

        // 6. Default: terrain.
        return new NpcSpawnHeightResolution(terrainHeight + TerrainEpsilon, NpcHeightAnchorKind.Terrain);
    }

    internal static string BuildElevatedProbeDiagnostic(
        InstanceWorld world,
        float x,
        float y,
        float spawnerZ,
        float terrainHeight)
    {
        if (world?.Physics?.StaticGeometryIndex == null)
            return "probe=index-null";

        var index = world.Physics.StaticGeometryIndex;
        var entryCount = index.GetEntryCountAt(x, y);
        var totalBrushes = index.TotalBrushCount;
        var totalVoxels = index.TotalVoxelCount;
        var probeResult = TryResolveElevatedBrushSpawnHeightWithDiag(
            world, x, y, spawnerZ, terrainHeight, out var brushHeight, out var diagReason);
        var describe = string.Empty;
        if (entryCount > 0)
        {
            describe = index.DescribeEntriesAt(x, y);
            if (!string.IsNullOrEmpty(describe))
                describe = " | " + describe;
        }

        return $"probe={probeResult} brushZ={brushHeight:F2} reason={diagReason} entries={entryCount} total={totalBrushes}+v{totalVoxels}{describe}";
    }

    private static bool TryResolveInteriorFloorSpawnHeight(
        InstanceWorld world,
        float x,
        float y,
        float spawnerZ,
        out float height)
    {
        height = 0f;
        if (world?.Physics?.StaticGeometryIndex == null)
            return false;

        var probe = new Vector3(x, y, spawnerZ);
        if (!StaticGeoDataService.TryResolveDistinctLayerHeight(
                world,
                probe,
                InteriorFloorNodeSearchRadius,
                spawnerZ,
                InteriorFloorNodeHeightTolerance,
                out var interiorHeight))
        {
            return false;
        }

        var zDelta = MathF.Abs(interiorHeight - spawnerZ);
        if (zDelta > InteriorFloorNodeHeightTolerance)
            return false;

        height = spawnerZ;
        return height > 0f;
    }

    private static bool TryResolveElevatedBrushSpawnHeight(
        InstanceWorld world,
        float x,
        float y,
        float spawnerZ,
        float terrainHeight,
        out float height)
    {
        return TryResolveElevatedBrushSpawnHeightWithDiag(world, x, y, spawnerZ, terrainHeight, out height, out _);
    }

    private static bool TryResolveElevatedBrushSpawnHeightWithDiag(
        InstanceWorld world,
        float x,
        float y,
        float spawnerZ,
        float terrainHeight,
        out float height,
        out string diagReason)
    {
        return TryResolveElevatedBrushSpawnHeightWithDiagAndAnchor(
            world, x, y, spawnerZ, terrainHeight, out height, out diagReason, out _);
    }

    private static bool TryResolveElevatedBrushSpawnHeightWithDiagAndAnchor(
        InstanceWorld world,
        float x,
        float y,
        float spawnerZ,
        float terrainHeight,
        out float height,
        out string diagReason,
        out bool isDesignTrust)
    {
        height = 0f;
        isDesignTrust = false;

        var index = world?.Physics?.StaticGeometryIndex;
        if (index == null)
        {
            diagReason = "StaticGeometryIndex NULL";
            return false;
        }

        var rayStart = spawnerZ + BrushProbeStartOffsetUp;
        var deltaToTerrain = spawnerZ - terrainHeight;
        if (!index.TryRaycastDown(x, y, rayStart, out var brushZ))
        {
            if (deltaToTerrain >= BrushElevationTrustThreshold)
            {
                height = spawnerZ;
                isDesignTrust = true;
                diagReason = $"index NO HIT — trust spawnerZ (design-elevated Δ={deltaToTerrain:F2}m ≥ {BrushElevationTrustThreshold}m)";
                return true;
            }
            diagReason = $"index NO HIT — Δ={deltaToTerrain:F2}m too small to trust spawnerZ — REJECT";
            return false;
        }

        if (brushZ <= terrainHeight + BrushProbeMinElevation)
        {
            diagReason = $"brush={brushZ:F2} too close to terrain (Δ={brushZ - terrainHeight:F2}m < {BrushProbeMinElevation}m) — REJECT";
            return false;
        }

        if (MathF.Abs(brushZ - spawnerZ) > BrushAcceptZTolerance)
        {
            diagReason = $"brush={brushZ:F2} far from spawnerZ ({MathF.Abs(brushZ - spawnerZ):F2}m > {BrushAcceptZTolerance}m) — REJECT";
            return false;
        }

        height = brushZ;
        diagReason = $"brush={brushZ:F2} ACCEPT";
        return true;
    }

    private static bool TryGetForcedSpawnHeight(Npc npc, out float height)
    {
        height = 0f;
        if (npc?.Template == null)
            return false;
        return false;
    }

    private static bool TryGetWaterSurfaceSpawnHeight(Npc npc, float x, float y, InstanceWorld world, out float height)
    {
        height = 0f;
        if (npc?.Template == null)
            return false;

        if (NpcWaterSurfaceOverrides.IsWaterSurfaceNpc(npc))
        {
            var ocean = world.Template.OceanLevel;
            var terrain = world.GetHeight(x, y);
            height = terrain < ocean ? ocean : terrain;
            return true;
        }

        if (NpcWaterSurfaceOverrides.IsWaterNpc(npc))
        {
            var terrain = world.GetHeight(x, y);
            height = MathF.Max(terrain, world.Template.OceanLevel);
            return true;
        }

        return false;
    }
}
