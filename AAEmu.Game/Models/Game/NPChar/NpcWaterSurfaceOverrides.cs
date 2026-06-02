using System;
using System.Numerics;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// Provides override logic for water surface NPCs.
/// Handles special movement and height calculations for NPCs that should stay on water surface.
/// </summary>
public static class NpcWaterSurfaceOverrides
{
    private const float WaterSurfaceProbeOffset = 2f;
    private const float MinimumWaterDepth = 0.15f;

    /// <summary>
    /// Checks if an NPC is a water surface NPC (should float on water).
    /// Not implemented in this codebase version — always returns false.
    /// </summary>
    public static bool IsWaterSurfaceNpc(Npc npc)
    {
        return false;
    }

    /// <summary>
    /// Checks if an NPC is a water NPC (can be underwater or swimming).
    /// Not implemented in this codebase version — always returns false.
    /// </summary>
    public static bool IsWaterNpc(Npc npc)
    {
        return false;
    }

    public static bool ShouldUseDirectMovement(Npc npc)
    {
        if (npc == null)
            return false;

        if (IsWaterSurfaceNpc(npc))
            return true;

        if (IsWaterNpc(npc))
            return true;

        return false;
    }

    public static bool HasWaterColumnAt(InstanceWorld world, Vector3 point, out float waterSurface)
    {
        return HasWaterColumnAt(world, point, out waterSurface, out _);
    }

    public static bool HasWaterColumnAt(InstanceWorld world, Vector3 point, out float waterSurface, out float terrainHeight)
    {
        waterSurface = 0f;
        terrainHeight = 0f;

        if (!TryGetWaterSurfaceAt(world, point, out waterSurface))
            return false;

        terrainHeight = world.Template.GetHeight(point.X, point.Y);
        return terrainHeight + MinimumWaterDepth < waterSurface;
    }

    public static bool TryGetWaterSurfaceAt(InstanceWorld world, Vector3 point, out float waterSurface)
    {
        waterSurface = 0f;

        if (world?.Template == null)
            return false;

        waterSurface = world.Template.OceanLevel;

        if (world.Water == null)
            return true;

        var probeZ = MathF.Max(point.Z, waterSurface) + WaterSurfaceProbeOffset;
        var probedSurface = world.Water.GetWaterSurface(point with { Z = probeZ }, out _);
        if (!float.IsNaN(probedSurface) && !float.IsInfinity(probedSurface))
            waterSurface = probedSurface;

        return true;
    }
}
