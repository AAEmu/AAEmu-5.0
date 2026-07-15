using System.Collections.Generic;
using AAEmu.Commons.Network;
// ReSharper disable ClassNeverInstantiated.Global

namespace AAEmu.Game.Models.Game;

public class Configurations : PacketMarshaler
{
    public string Key { get; set; }
    public string Value { get; set; }
}

public class WorldConfig
{
    public enum WindModelType
    {
        /// <summary>Retail-like: wind only along N↔S axis. 15 angle bonus for wind in the direction of the ship.</summary>
        Official,
        /// <summary>More realistic: wind direction rotates smoothly over the day.</summary>
        Realistic
    }

    /// <summary>
    /// Message of the Day that gets displayed in player's chat upon login
    /// </summary>
    public string MOTD { get; set; } = "";
    /// <summary>
    /// Message shown to the player when they exit the game
    /// </summary>
    public string LogoutMessage { get; set; } = "";
    /// <summary>
    /// Time in minutes between user data Save events
    /// </summary>
    public double AutoSaveInterval { get; set; } = 5.0;
    /// <summary>
    /// Server-side Exp multiplier (on top of buffs)
    /// </summary>
    public double ExpRate { get; set; } = 1.0;
    /// <summary>
    /// Server-side Honor Points multiplier (on top of buffs)
    /// </summary>
    public double HonorRate { get; set; } = 1.0;
    /// <summary>
    /// Server-side Vocation Badge multiplier (on top of buffs)
    /// </summary>
    public double VocationRate { get; set; } = 1.0;
    /// <summary>
    /// Multiplier for the loot dice (some loot types are not affected by this)
    /// </summary>
    public double LootRate { get; set; } = 1.0;
    /// <summary>
    /// Multiplier for gold that is obtained through loot drops
    /// </summary>
    public double GoldLootMultiplier { get; set; } = 1.0;
    /// <summary>
    /// Multiplier for growth rate of doodads, note that this only affects steps marked as growth and not those with a simple timer.
    /// </summary>
    public double GrowthRate { get; set; } = 1.0;
    /// <summary>
    /// Minimum character level required to use gathering/harvesting/mining skills (e.g. Mine Stone, Gather Herbs,
    /// Pick Fruit, Harvest Crops). Any gathering skill whose data-defined level requirement is higher than this is
    /// clamped down to this value at load time; lower requirements are left untouched. Set to 1 to let players
    /// gather from level 1. Detected via the skill's world-interaction type (Collect/Mine/Harvest/Pick), so it only
    /// affects gathering skills and not normal combat/class abilities.
    /// </summary>
    public int GatheringMinLevel { get; set; } = 1;

    /// <summary>
    /// Quest context ids refused at accept. For quests broken by unimplemented quest act types (e.g.
    /// EffectFire) that would soft-lock the client, and the on/off switch for seasonal/event quests.
    /// Rejected cleanly so the offer cutscene closes (no freeze). Remove ids as act types get implemented
    /// or the event system enables them. See project_archeage_broken_quests.
    /// </summary>
    public List<uint> DisabledQuestIds { get; set; } = new()
    {
        150, 1388, 1484, 5277, 5653, 5980, 6774, 6793, 8165, 8291, 8296, 8342,
        8693, 8761, 8770, 8791, 8822, 8952, 9002, 9003, 9004, 9008, 9065, 9075
    };
    /// <summary>
    /// Number of days 1 week worth of tax pays for, set this to 3640 would make 1 tax payment last for about 10 years.
    /// </summary>
    public uint DaysForTaxPayment { get; set; } = 7u; 
    /// <summary>
    /// Set a minimum access-level that a character must have to ignore falling damage (for devs)
    /// </summary>
    public int IgnoreFallDamageAccessLevel { get; set; } = 100;
    /// <summary>
    /// When enabled, players take no damage at all
    /// </summary>
    public bool GodMode { get; set; }
    /// <summary>
    /// Legacy BAI/nav toggle. Height resolution now comes from terrain + static client geometry.
    /// Left enabled by default so legacy nav/ship-barrier consumers keep working.
    /// </summary>
    public bool GeoDataMode { get; set; } = true;
    /// <summary>
    /// Enables save_geodata mode
    /// </summary>
    public bool SaveGeoDataMode { get; set; }

    /// <summary>
    /// Enables First Journey
    /// </summary>
    public bool FirstJourneyEnable { get; set; }

    /// <summary>Pre-load terrain heightmap data (for physics)</summary>
    public bool PreLoadTerrain { get; set; }

    /// <summary>Maximum number of instances that can be created</summary>
    public uint MaxInstances { get; set; } = 32;

    /// <summary>Target Ticks per Second to use for Physics threads</summary>
    public float TargetPhysicsTps { get; set; } = 25f;

    /// <summary>Ship wind model: Official (retail N↔S axis +15%) or Realistic (rotates with time of day)</summary>
    public WindModelType WindModel { get; set; } = WindModelType.Official;

    /// <summary>Server-side Actability Points multiplier (on top of buffs)</summary>
    public double ActabilityRate { get; set; } = 1.0;

    /// <summary>
    /// NPC spawn-Z resolution policy. Controls how the server reconciles the Z value declared in
    /// <c>npc_spawns.json</c> with the heightmap and .bai navmesh data when spawning a non-flying NPC.
    /// See <see cref="SpawnHeightConfig"/> for details.
    /// </summary>
    public SpawnHeightConfig SpawnHeight { get; set; } = new();

    /// <summary>
    /// NPC combat pathfinding policy. Controls how the A* pathfinder, repath cadence, waypoint
    /// consumption and path smoothing behave during NPC aggro and pursuit.
    /// See <see cref="PathingConfig"/> for details.
    /// </summary>
    public PathingConfig Pathing { get; set; } = new();
}

/// <summary>
/// Tunable thresholds that control the spawn-Z resolution algorithm in
/// <c>NpcSpawnerNpc.SpawnNpc</c>. Defaults are conservative on purpose: they fix the common
/// "NPC levitates above the ground" / "NPC under the floor" cases without re-snapping NPCs that
/// were placed deliberately on structures (interior floors, bridges, dungeon platforms, etc.).
/// </summary>
public class SpawnHeightConfig
{
    /// <summary>
    /// Master switch. When false the new resolution logic is bypassed and the original
    /// <c>abs(spawnZ - geoZ) &lt; 1f</c> behaviour is used. Useful for A/B testing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum 2D (XY) distance, in metres, between a spawn point and a .bai node for that
    /// node's Z to be considered representative of the local floor. Beyond this radius the
    /// .bai value is treated as untrusted and the heightmap is preferred.
    /// Default: 6m. Rejects the obvious "balcony 26 m away" lookup while still tolerating
    /// sparser navmesh in caves, dungeons and outdoor patches with low NPC density.
    /// </summary>
    public float MaxBaiDistance2D { get; set; } = 6.0f;

    /// <summary>
    /// Maximum vertical separation, in metres, between a candidate .bai Z and the local
    /// heightmap Z for the .bai value to be considered consistent with the open-air ground.
    /// If exceeded the .bai node is assumed to belong to a different vertical surface
    /// (interior floor, bridge, balcony) and the heightmap is preferred.
    /// </summary>
    public float MaxBaiVsHeightmapDelta { get; set; } = 3.0f;

    /// <summary>
    /// Maximum vertical correction, in metres, that the resolver is allowed to apply to the
    /// JSON-declared spawn Z. If the candidate floor Z is further than this from the spawn Z,
    /// the spawn Z is kept verbatim (the NPC is assumed to be intentionally placed on a
    /// structure that we cannot reason about — interior, dungeon, ship, doodad-mounted, etc.).
    /// Default: 5m.
    /// </summary>
    public float MaxSnapDistance { get; set; } = 5.0f;

    /// <summary>
    /// Threshold, in metres, above which a spawn Z that exceeds the highest of (heightmap, bai)
    /// — when those two agree with each other — is treated as belonging to an unmeshed
    /// structure (pier, dock, low balcony, wooden platform), in which case the JSON spawn Z
    /// is kept even though the snap budget would otherwise allow a snap.
    /// Default: 1.5m. Lower values risk catching legitimate small terrain rounding errors;
    /// higher values risk letting NPCs sink through low piers and decks.
    /// </summary>
    public float UnmeshedStructureThreshold { get; set; } = 1.5f;

    /// <summary>
    /// Maximum DOWNWARD snap distance, in metres. When the resolver decides to lower the
    /// NPC (spawnZ &gt; candidateFloor), it will only do so if the gap is at most this value.
    /// Larger downward gaps are treated as the NPC standing on an unmeshed sub-structure
    /// (low platform, pier, dock, stone block, root, stair) that is invisible to both the
    /// heightmap and the .bai navmesh — the JSON spawn Z is then kept verbatim.
    ///
    /// This rule is ASYMMETRIC: upward corrections (raising an NPC that JSON placed below
    /// the floor, e.g. clipping through the ground) are still allowed up to MaxSnapDistance.
    /// The rationale is that an NPC under the floor is always a bug, but an NPC above the
    /// floor can legitimately be standing on something we cannot reason about. When in
    /// doubt, trust the JSON.
    ///
    /// This complements <see cref="UnmeshedStructureThreshold"/>: the unmeshed-structure
    /// detection requires hmap and bai to agree, which fails on rocky / uneven terrain
    /// where bai sits a metre or so above hmap. The downward snap cap catches those cases
    /// too, regardless of hmap/bai agreement.
    ///
    /// Default: 0.5m. Empirically tuned against /heightmob captures showing that NPCs
    /// snapped downward by more than ~0.7m are visibly floating above the natural ground
    /// (i.e. they were on a low structure the resolver cannot see). Set to a very large
    /// value (e.g. 99) to disable this guard and restore the previous symmetric snap.
    /// </summary>
    public float MaxDownwardSnapDistance { get; set; } = 0.5f;

    /// <summary>
    /// (V5) XY radius, in metres, around a NPC's spawn position within which
    /// <see cref="AAEmu.Game.Core.Managers.World.WorldManager.GetReferenceHeight"/> trusts
    /// the spawner Z instead of running a fresh terrain lookup.
    ///
    /// Why this exists: the spawn-Z resolver (V3.5+V4) carefully picks the correct Z for the
    /// NPC at spawn time, but legacy code in SCUnitStatePacket, Simulation, and Npc movement
    /// calls GetReferenceHeight() on every visibility update, which re-runs a terrain lookup
    /// and overwrites the NPC's Z back down to the .bai / heightmap floor. That defeats every
    /// "keep" decision the resolver makes (keep-unmeshed-structure, keep-downward-cap, etc.)
    /// for NPCs that are on a low sub-structure (pier, deck, stone block, stair).
    ///
    /// Inside this radius, GetReferenceHeight returns Spawner.Position.Z directly, preserving
    /// the resolver's decision. Outside this radius the NPC has clearly wandered away (combat,
    /// patrol path, returning) and a fresh lookup is appropriate.
    ///
    /// Default: 3.0 m. Most static NPCs (vendors, guards, dialogue NPCs) never leave this
    /// radius. Patrol NPCs with waypoints further than 3 m will use the terrain lookup as
    /// before. Set to 0 to disable the guard entirely and restore the previous behaviour.
    /// </summary>
    public float SpawnHomeXyRadius { get; set; } = 3.0f;

    /// <summary>
    /// V6. Width, in metres, of the smooth transition band just outside
    /// <see cref="SpawnHomeXyRadius"/>. Inside the radius the spawn Z is trusted fully; across
    /// the next SpawnHomeBlendBand metres GetReferenceHeight linearly blends from the spawn Z
    /// to the terrain height; beyond that the terrain height is used as before.
    ///
    /// This replaces V5's hard binary cut, which made a near-stationary NPC whose XY jitters
    /// across the radius boundary pop up and down by several metres tick-to-tick (observed on
    /// NPC objId 16221: Z oscillating 161.65 ↔ 166.69 while standing still). A continuous blend
    /// removes the discontinuity. Default: 4.0 m. Set to 0 to restore V5's hard cut.
    /// </summary>
    public float SpawnHomeBlendBand { get; set; } = 4.0f;

    /// <summary>
    /// Max metres an NPC's resolved ground height may rise above its current reference Z in one
    /// movement step. If the height lookup returns more than this above the NPC, it's treated as
    /// the surface terrain above a cave/overhang (no cave-floor collision is loaded there) rather
    /// than a walkable floor, and the NPC keeps its current Z instead of climbing up through the
    /// cave roof toward you. Gentle slopes step up far less than this and are unaffected. 0 = off.
    /// </summary>
    public float MaxGroundRise { get; set; } = 8.0f;

    /// <summary>
    /// V6. When true (default), the ground Z used for NPCs prefers the bilinearly-interpolated
    /// terrain heightmap over the .bai navmesh node whenever the two AGREE (within
    /// <see cref="MaxBaiVsHeightmapDelta"/>). The .bai navmesh is built at agent height and
    /// floats systematically above the terrain (field data: +1.89 m on average), so using it as
    /// the ground reference leaves NPCs hovering. With this on, the .bai is demoted to a
    /// structure/cave guard: it is still trusted where it DISAGREES with the heightmap (real
    /// elevated walkable surfaces — bridges, cave floors, dungeon platforms), but on open
    /// ground the heightmap wins. Affects both spawn (<c>ResolveSpawnZ</c>) and per-tick
    /// (<c>GetHeight</c>/<c>GetReferenceHeight</c>) resolution. Set to false to restore the
    /// V5 bai-first behaviour.
    /// </summary>
    public bool PreferHeightmapForGroundZ { get; set; } = true;

    /// <summary>
    /// When true, every non-flying NPC spawn logs (at Debug level) the three candidate Z
    /// values, the .bai node distance / type, and the final decision. Disable on production
    /// servers — main_world has tens of thousands of spawns.
    /// </summary>
    public bool LogResolution { get; set; } = false;

    /// <summary>
    /// A/B test toggle. When true, restores the original 3D-nearest .bai lookup in
    /// <see cref="AAEmu.Game.Core.Managers.AiGeoDataManager.GetHeight(System.Numerics.Vector3)"/>
    /// AND forces <see cref="Enabled"/>=false semantics in the spawn-Z resolver, regardless of
    /// the <see cref="Enabled"/> setting. Use this only to reproduce the broken legacy
    /// behaviour for diagnostics or comparison; never on production.
    ///
    /// Important: this flag has GLOBAL effect. It changes the behaviour of
    /// <c>AiGeoDataManager.GetHeight()</c> for every consumer (AI, pathing, GM commands), not
    /// only for NPC spawns.
    /// </summary>
    public bool UseLegacyBai3DLookup { get; set; } = false;

    /// <summary>
    /// Optional, OFF by default. When true, the resolver also trusts a .bai node that is
    /// XY-close AND vertically close to the JSON spawn Z, even if it diverges strongly from
    /// the heightmap. Intended for spawns deliberately placed on bridges / interior floors /
    /// elevated structures where the heightmap is not the right reference.
    ///
    /// Risk: if the JSON spawn Z is itself wrong, this rule will lock onto a wrong .bai node
    /// near it and propagate the error. Enable only after analysing logs and confirming that
    /// rejected .bai values were actually correct for structure spawns.
    /// </summary>
    public bool TrustBaiNearSpawnZ { get; set; } = false;
}

/// <summary>
/// Tunables for the NPC combat pathfinding pipeline (aggro -> FindPath -> A* -> ReducePath -> MoveInRange).
///
/// The defaults are conservative on purpose: they fix the most visible "NPC runs back to spawn before
/// chasing the player" / "NPC follows a stale path" symptoms reported with v3.5 spawn-Z without
/// disabling the existing smoothing and leash logic. See HANDOFF for the full list of bugs addressed.
///
/// All values can be hot-tuned via <c>AAEmu.Game/Configurations/World.json</c> under
/// <c>World.Pathing</c>.
/// </summary>
public class PathingConfig
{
    /// <summary>
    /// Master switch. When false the new pathfinding behaviour is bypassed and the original
    /// (broken) <c>BaseCombatBehavior.MoveInRange()</c> / <c>Npc.FindPath()</c> code paths run.
    /// Useful for A/B testing the patch in isolation. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Repath when the target has moved more than this many metres away from the position used
    /// when the current path was computed. The previous code repathed only when the cached
    /// endpoint was *close* to the target (the condition was inverted), which left NPCs chasing
    /// stale routes. Typical retail-feel value: 2.0–4.0 m.
    /// </summary>
    public float RepathTargetMovedDistance { get; set; } = 2.5f;

    /// <summary>
    /// Minimum delay between two A* recomputations for the same NPC, in milliseconds.
    /// Prevents per-tick repath storms when a player kites an aggressive NPC. Lower values
    /// feel more reactive but cost more CPU; higher values give smoother routes. Default: 350 ms.
    /// </summary>
    public int RepathCooldownMs { get; set; } = 350;

    /// <summary>
    /// When > 0, the first node returned by A* (the "start technical node" closest to the NPC)
    /// is dropped from the followable path if the NPC is already within this distance from it
    /// AND the node is further from the target than the NPC currently is. This is the core fix
    /// for the "NPC turns back toward spawn before chasing" bug. Default: 4.0 m.
    /// Set to 0 to keep the legacy behaviour (always follow the start node).
    /// </summary>
    public float SkipStartNodeIfBehindRadius { get; set; } = 4.0f;

    /// <summary>
    /// Distance, in metres, at which a waypoint is considered reached and dequeued. Replaces
    /// the legacy use of the NPC's attack range as waypoint tolerance, which made melee NPCs
    /// hop over waypoints too quickly and ranged NPCs miss them entirely. Default: 0.9 m.
    /// </summary>
    public float WaypointReachTolerance { get; set; } = 0.9f;

    /// <summary>
    /// When true, if A* returns no path AND the target is currently visible to the NPC, the
    /// NPC moves directly toward the target as a fallback (no waypoints, single-segment path).
    /// Prevents NPCs from standing still when the navmesh is sparse near the spawn or target.
    /// Default: true.
    /// </summary>
    public bool DirectFallbackWhenVisible { get; set; } = true;

    /// <summary>
    /// <c>maxNodeSkipCount</c> passed to <see cref="AAEmu.Game.Core.Managers.AiGeoDataManager.ReducePath"/>.
    /// Setting this to 0 disables the smoothing pass entirely (useful when diagnosing whether
    /// detours come from A* itself or from over-aggressive smoothing). Legacy hardcoded value
    /// was 10. Default: 4 — smooths obvious zig-zags but does not skip more than 4 navmesh
    /// nodes at once.
    /// </summary>
    public int ReducePathMaxSkip { get; set; } = 4;

    /// <summary>
    /// When true, the A* pathfinder uses cumulative G cost (sum of step lengths from the start)
    /// instead of the legacy bug where G was computed as <c>distance(sourceNode, goal)</c> —
    /// effectively making G look like a second H. The legacy formula remains available for A/B
    /// testing by setting this to false. Default: true.
    /// </summary>
    public bool UseCumulativeAStarCost { get; set; } = true;

    /// <summary>
    /// Maximum 2D (XY) distance, in metres, between the NPC / target position and a candidate
    /// .bai node when picking the start/end node for A*. The legacy code used 3D distance,
    /// which suffered from the same vertical-cliff bug as v3.5 spawn-Z (a balcony 8 m XY but
    /// 1 m above outranked a ground node 5 m XY but 5 m below). XY is now the primary metric.
    /// 0 disables the cap. Default: 8.0 m. Note: v3.5 uses 6.0 m for spawn-Z, but path nodes
    /// can legitimately be a bit further for sparse outdoor navmesh, hence 8 m here.
    /// </summary>
    public float MaxStartEndNode2D { get; set; } = 8.0f;

    /// <summary>
    /// When true, every NPC FindPath / repath logs (at Info level via NLog and into
    /// <c>SpawnHeightDiagnostics</c>'s sibling <c>PathfindDiagnostics</c> if present) the
    /// start real, start node, end real, end node, deltas, raw path length, reduced path
    /// length, first 5 waypoints and the chosen repath reason. Disable on production servers.
    /// </summary>
    public bool LogPathfinding { get; set; } = false;

    // ---------------------------------------------------------------------
    // v6 — shortest-path A* (heap + tie-break + edge-length cost).
    // ---------------------------------------------------------------------

    /// <summary>
    /// v6. Use the optimized A* (binary heap + deterministic F→H→FIFO tie-break + correct
    /// relaxation) instead of the legacy V4 <c>OrderBy(F).First()</c> open-set scan. The
    /// optimized loop is provably optimal with the admissible straight-line heuristic, finds
    /// shorter routes on flat terrain (no sideways wandering on equal-F plateaus), and converges
    /// in far fewer iterations so it rarely times out as Partial. Set to <c>false</c> to fall
    /// back to the exact V4 behaviour for A/B testing. Default: true.
    /// </summary>
    public bool UseOptimizedAStar { get; set; } = true;

    /// <summary>
    /// v6. When true (and <see cref="UseCumulativeAStarCost"/> is also true), the per-edge step
    /// cost uses the .bai <c>LinkDescriptor.Length</c> (true on-mesh traversal length) instead
    /// of the straight-line distance between node centres. This lets A* prefer routes that are
    /// genuinely shorter to walk. Falls back to straight-line distance for edges with no stored
    /// length. Default: true.
    /// </summary>
    public bool UseEdgeLengthCost { get; set; } = true;

    /// <summary>
    /// v6. A* loop-budget multiplier applied to the start→goal straight-line distance. Total
    /// budget = ceil(distance * this) + <see cref="LoopBudgetBase"/>. Only *valid* node
    /// expansions consume budget (stale heap pops are free). Default: 15.
    /// </summary>
    public int LoopBudgetDistanceMultiplier { get; set; } = 15;

    /// <summary>
    /// v6. Constant headroom added to the A* loop budget. See
    /// <see cref="LoopBudgetDistanceMultiplier"/>. Default: 100.
    /// </summary>
    public int LoopBudgetBase { get; set; } = 100;

    /// <summary>
    /// v6. Master switch for the detour-rejection heuristics in <c>Npc.FindPath</c> and
    /// <c>BaseCombatBehavior.MoveInRange</c> (the guards that throw away the whole path and fall
    /// back to a direct chase when a waypoint looks like a big detour). These were a band-aid for
    /// the legacy A* producing bogus 50 m backward paths. The v6 optimized A* produces genuinely
    /// shortest paths, so there is nothing to reject — the guards are <b>disabled by default</b>.
    /// They remain available as an emergency safety net: set to <c>true</c> (with
    /// <see cref="DetourRejectThreshold"/>) only if you observe a regression. Default: false.
    /// </summary>
    public bool RejectDetourPaths { get; set; } = false;

    /// <summary>
    /// v6. Distance, in metres, by which the first followable waypoint must be *further* from the
    /// target than the NPC currently is (and the NPC must be at least this far from that
    /// waypoint) before the path is treated as a detour and rejected. Higher = more tolerant.
    /// Only used when <see cref="RejectDetourPaths"/> is true. Legacy hard-coded value was 5.0.
    /// Default: 5.0.
    /// </summary>
    public float DetourRejectThreshold { get; set; } = 5.0f;
}

public class AccountDeleteDelayTiming
{
    /// <summary>
    /// Minimum Level this timing applies to
    /// </summary>
    public int Level { get; set; }
    /// <summary>
    /// Delay in minutes that needs to be used if this character is at least this level
    /// </summary>
    public int Delay { get; set; }
}

public class AccountConfig
{
    /// <summary>
    /// Allowed Regex for account names
    /// </summary>
    public string NameRegex { get; set; } = "^[a-zA-Z0-9]{1,18}$";
    /// <summary>
    /// Marks if a deleted character's name can be re-used for a new character
    /// </summary>
    public bool DeleteReleaseName { get; set; } = false;
    // ReSharper disable once CollectionNeverUpdated.Global
    // Populated by JSON reader
    /// <summary>
    /// Delete character settings
    /// </summary>
    public List<AccountDeleteDelayTiming> DeleteTimings { get; set; } = [];
    /// <summary>
    /// Default access-level for new accounts
    /// </summary>
    public int AccessLevelDefault { get; set; } = 0;
    /// <summary>
    /// Access-Level that should be used for the first created account on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstAccount { get; set; } = 100;
    /// <summary>
    /// Access-Level that should be used for the first created character on the server regardless of other settings
    /// </summary>
    public int AccessLevelFirstCharacter { get; set; } = 100;
}

public class CurrencyValuesConfig
{
    public int Default { get; set; } = 0;
    public int DailyLogin { get; set; } = 0;
    public int TickMinutes { get; set; } = 5;
    public int TickAmount { get; set; } = 0;
    public int TickAmountPremium { get; set; } = 0;

    public int GetTickAmount(bool isPremium)
    {
        return isPremium ? TickAmountPremium : TickAmount;
    }
}

public class SpecialtyConfig
{
    /// <summary>
    /// Maximum rate for speciality packs
    /// </summary>
    public int MaxSpecialtyRatio { get; set; } = 130;
    /// <summary>
    /// Minimum rate for speciality packs
    /// </summary>
    public int MinSpecialtyRatio { get; set; } = 70;
    /// <summary>
    /// Amount the trade in rate lowers for each traded pack
    /// </summary>
    public double RatioDecreasePerPack { get; set; } = 0.5f;
    /// <summary>
    /// Number of % a trade recovers every X time
    /// </summary>
    public double RatioIncreasePerTick { get; set; } = 5.0;
    /// <summary>
    /// Number of minutes between trade rate updates when selling packs
    /// </summary>
    public double RatioDecreaseTickMinutes { get; set; } = 1f;
    /// <summary>
    /// Time in minutes before a traded pack is no longer counted towards the trade rate calculation
    /// </summary>
    public double RatioRegenTickMinutes { get; set; } = 60f;
}

public class ScriptsConfig
{
    public LoadStrategyType LoadStrategy { get; set; } = LoadStrategyType.Reflection;

    public enum LoadStrategyType
    {
        Compilation,
        Reflection
    }
}
