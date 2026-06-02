namespace AAEmu.Game.Models.Game.NPChar;

public enum NpcHeightAnchorKind
{
    Unknown,
    Terrain,
    Spawner,
    InteriorFloor,
    WaterSurface,
    WaterColumn,
    Air
}

public enum NpcHeightResolutionContext
{
    Spawn,
    ReferenceHeight,
    RoamingTarget,
    MovementStep,
    ReturnSnap,
    IdleAnchor
}

public sealed class NpcHeightState
{
    public bool IsInitialized { get; private set; }
    public float AnchorZ { get; private set; }
    public float LastResolvedZ { get; private set; }
    public NpcHeightAnchorKind AnchorKind { get; private set; } = NpcHeightAnchorKind.Unknown;
    public bool LayerLocked { get; private set; }

    /// <summary>
    /// Internal counter used to decide when an interior/cave-anchored NPC has effectively returned to terrain.
    /// Kept per-NPC to avoid global contention and to keep MovementStep deterministic/stall-free.
    /// </summary>
    public int NearTerrainTicks { get; private set; }

    /// <summary>
    /// Cooldown for expensive interior evidence probes in MovementStep (GeoData lookups).
    /// Stored per-NPC to avoid global contention and to keep the movement hot path stable.
    /// </summary>
    public int InteriorEvidenceProbeCooldownTicks { get; private set; }

    public void Capture(float anchorZ, NpcHeightAnchorKind anchorKind, bool layerLocked)
    {
        AnchorZ = anchorZ;
        LastResolvedZ = anchorZ;
        AnchorKind = anchorKind;
        LayerLocked = layerLocked;
        IsInitialized = true;
        NearTerrainTicks = 0;
        InteriorEvidenceProbeCooldownTicks = 0;
    }

    public void UpdateLastResolved(float resolvedZ)
    {
        LastResolvedZ = resolvedZ;
    }

    public void TrackNearTerrain(bool nearTerrain)
    {
        if (nearTerrain)
        {
            if (NearTerrainTicks < int.MaxValue)
                NearTerrainTicks++;
        }
        else
        {
            NearTerrainTicks = 0;
        }
    }

    public bool ShouldProbeInteriorEvidence()
    {
        if (InteriorEvidenceProbeCooldownTicks > 0)
        {
            InteriorEvidenceProbeCooldownTicks--;
            return false;
        }

        return true;
    }

    public void SetInteriorEvidenceProbeCooldown(int ticks)
    {
        InteriorEvidenceProbeCooldownTicks = ticks < 0 ? 0 : ticks;
    }

    public void SwitchToTerrain(float terrainZ)
    {
        Capture(terrainZ, NpcHeightAnchorKind.Terrain, layerLocked: false);
    }

    public void SwitchToInteriorFloor(float anchorZ, float currentZ)
    {
        AnchorZ = anchorZ;
        LastResolvedZ = currentZ;
        AnchorKind = NpcHeightAnchorKind.InteriorFloor;
        LayerLocked = true;
        IsInitialized = true;
        NearTerrainTicks = 0;
        InteriorEvidenceProbeCooldownTicks = 0;
    }
}
