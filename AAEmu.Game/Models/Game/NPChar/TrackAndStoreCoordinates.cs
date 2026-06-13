using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Json;

using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.NPChar;

public partial class Npc
{
    #region Height Caching System

    private const float NearbyCharactersSearchRadius = 4f;
    private const float NearbyNpcSearchRadius = 15f;
    private const float Tolerance = 0.5f; // порог допуска для корректировки высоты
    private const float FloorThreshold = 5.6f;

    #endregion

    #region Public API

    /// <summary>
    /// Returns the reference Z height for a NPC at a given XY. This is the function called by
    /// SCUnitStatePacket, Simulation, and Npc movement code to "snap" the NPC's Z value before
    /// sending it to clients and before applying movement updates.
    ///
    /// History / why this function looks the way it does:
    ///
    /// The legacy implementation only respected the spawn position for two specific behaviours
    /// (HoldPosition and Idle). Every other state — including the very common SpawningBehavior
    /// that runs immediately after spawn, and RunCommandSet which is the default afterwards —
    /// fell through to GetHeight(), which returns the .bai navmesh Z (or heightmap as fallback).
    /// For NPCs spawned on small unmeshed sub-structures (piers, low decks, blocks, stairs,
    /// stalls, roots) this completely undid the careful work of NpcSpawnerNpc.ResolveSpawnZ:
    /// the resolver could correctly keep spawnZ = 120.80 on a sub-structure, but the next
    /// SCUnitStatePacket would call GetReferenceHeight, hit the GetHeight() branch, get back
    /// the BAI floor (119.31), and overwrite the NPC's Z back down. The NPC visually sank
    /// into the structure and sometimes became un-clickable.
    ///
    /// V5 fix (near-home guard): if the NPC is geographically close to its spawn position
    /// (XY distance below SpawnHomeXyRadius, default 3 m), trust Spawner.Position.Z. This
    /// covers the moment-of-spawn case and any NPC standing on its spawn spot (most static
    /// village / dungeon NPCs). Patrolling or aggro'd NPCs that have moved away from spawn
    /// still fall through to terrain lookup as before.
    ///
    /// V6 fix (smooth blend): V5 used a hard binary cut at SpawnHomeXyRadius. Field data
    /// (NPC objId 16221) showed the failure mode: a near-stationary NPC whose XY jitters by
    /// ~4 m repeatedly crosses the 3 m boundary, so its Z snaps between the correct spawn Z
    /// (161.65) and an aberrant terrain/bai Z (166.69) tick to tick — visible as the NPC
    /// popping up and down by several metres while standing still. V6 keeps the spawner Z
    /// fully trusted inside SpawnHomeXyRadius, then BLENDS linearly to the terrain height
    /// across an additional SpawnHomeBlendBand metres. Beyond radius+band the terrain height
    /// is used exactly as before. Because the transition is continuous, small XY jitter no
    /// longer causes a discontinuous Z jump, and an NPC that genuinely walks away is handed
    /// off to terrain tracking smoothly.
    ///
    /// Behavior summary:
    ///   0. ai null            -> GetHeight (legacy)
    ///   1. CanFly             -> Spawner.Position.Z (legacy)
    ///   2. Hold/Idle          -> Spawner.Position.Z (legacy)
    ///   3. Near home in XY    -> Spawner.Position.Z (V5/V6)
    ///   4. Terrain lookup     -> GetHeight (legacy)
    ///   5. Terrain lookup = 0 -> Spawner.Position.Z fallback (legacy)
    /// </summary>
    public float GetReferenceHeight(float x, float y, float z, uint zoneId)
    {
        float finalHeight;

        // 0. If AI is null, fall through to terrain lookup
        if (Ai == null)
        {
            finalHeight = WorldManager.Instance.GetHeight(zoneId, x, y, z);
            if (finalHeight != 0f)
                return finalHeight;
            return Spawner?.Position.Z ?? Transform.World.Position.Z;
        }

        // 1. If NPC can fly, use spawner Z
        if (CanFly)
        {
            finalHeight = Spawner.Position.Z;
            return finalHeight;
        }

        // 2. For HoldPositionBehavior and IdleBehavior, use spawner Z
        switch (Ai.GetCurrentBehavior())
        {
            case HoldPositionBehavior:
            case IdleBehavior:
                finalHeight = Spawner.Position.Z;
                return finalHeight;
        }

        // 3. (V5/V6) Near-home guard, SMOOTHED.
        //
        // V5 used a hard binary cut at SpawnHomeXyRadius: inside -> spawner Z, outside ->
        // terrain Z. Field data (NPC objId 16221) showed the failure mode: a near-stationary
        // NPC whose XY jitters by ~4 m repeatedly crosses the 3 m boundary, so its Z snaps
        // between the correct spawn Z (161.65) and an aberrant terrain/bai Z (166.69) tick to
        // tick — visible as the NPC popping up and down by several metres while standing still.
        //
        // V6 keeps the spawner Z fully trusted inside SpawnHomeXyRadius, then BLENDS linearly
        // to the terrain height across an additional SpawnHomeBlendBand metres. Beyond
        // radius+band the terrain height is used exactly as before. Because the transition is
        // continuous, small XY jitter no longer causes a discontinuous Z jump, and an NPC that
        // genuinely walks away is handed off to terrain tracking smoothly.
        var spawner = Spawner;
        if (spawner != null)
        {
            var spawnCfg = AppConfiguration.Instance.World?.SpawnHeight;
            var homeRadius = spawnCfg?.SpawnHomeXyRadius ?? 3.0f;
            var blendBand = spawnCfg?.SpawnHomeBlendBand ?? 4.0f;
            if (homeRadius > 0f)
            {
                var dx = x - spawner.Position.X;
                var dy = y - spawner.Position.Y;
                var dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist <= homeRadius)
                {
                    // Fully home: trust the resolver-chosen spawn Z.
                    return spawner.Position.Z;
                }

                if (blendBand > 0f && dist <= homeRadius + blendBand)
                {
                    // Transition zone: blend spawn Z -> terrain Z by how far into the band we are.
                    var terrainZ = WorldManager.Instance.GetHeight(zoneId, x, y, z);
                    if (terrainZ == 0f)
                        terrainZ = spawner.Position.Z; // terrain lookup failed; stay on spawn Z
                    var t = (dist - homeRadius) / blendBand; // 0 at inner edge, 1 at outer edge
                    return spawner.Position.Z + (terrainZ - spawner.Position.Z) * t;
                }
            }
        }

        // 4. Terrain height retrieval
        // Try NavMesh first (most accurate for NPCs on navmesh)
        var navMeshHeight = WorldManager.Instance.GetCorrectNpcHeight(zoneId, x, y, z);
        if (!float.IsNaN(navMeshHeight))
        {
            return navMeshHeight;
        }

        // Fall back to world height (heightmap + static geometry)
        finalHeight = WorldManager.Instance.GetHeight(zoneId, x, y, z);
        if (finalHeight != 0f)
        {
            return finalHeight;
        }

        // 5. Take the default height
        return Spawner?.Position.Z ?? Transform.World.Position.Z;
    }

    #endregion

    #region Private Implementation

    internal float AdjustNpcFloor(float candidate, float? minZ = null, float? maxZ = null)
    {
        var actualMinZ = minZ ?? Math.Min(Spawner.Position.Z, candidate);
        var actualMaxZ = maxZ ?? Math.Max(Spawner.Position.Z, candidate);

        if (actualMaxZ - actualMinZ >= FloorThreshold)
        {
            return Building.GetFloorHeight(actualMinZ, actualMaxZ, Spawner.Position.Z);
        }
        return candidate;
    }

    /// <summary>
    /// Загружает данные спавна NPC из файла
    /// </summary>
    public static List<JsonNpcSpawns> LoadSpawnFile()
    {
        var spawnsFilePath = Path.Combine(FileManager.AppPath, "Data", "Worlds", "main_world", "npc_spawns_main.json");
        if (!File.Exists(spawnsFilePath))
        {
            Logger.Warn($"Spawn file not found at {spawnsFilePath}");
            return null;
        }

        try
        {
            var spawnsJson = File.ReadAllText(spawnsFilePath);
            return JsonConvert.DeserializeObject<List<JsonNpcSpawns>>(spawnsJson);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load spawn file");
            return null;
        }
    }

    /// <summary>
    /// Находит и обновляет высоту для указанного NPC
    /// </summary>
    public static bool UpdateSpawnHeight(List<JsonNpcSpawns> spawns, uint unitId, float x, float y, float newZ)
    {
        if (spawns == null)
            return false;

        var spawnToUpdate = spawns.FirstOrDefault(s =>
            Math.Abs(s.Position.X - x) < 0.1f &&
            Math.Abs(s.Position.Y - y) < 0.1f &&
            s.UnitId == unitId);

        if (spawnToUpdate == null)
        {
            Logger.Warn($"No matching spawn found for NPC {unitId} at ({x}, {y})");
            return false;
        }

        spawnToUpdate.Position.Z = newZ;
        return true;
    }

    /// <summary>
    /// Сохраняет обновленные данные спавна обратно в файл
    /// </summary>
    public static void SaveSpawnFile(List<JsonNpcSpawns> spawns)
    {
        if (spawns == null)
            return;

        var spawnsFilePath = Path.Combine(FileManager.AppPath, "Data", "Worlds", "main_world", "npc_spawns_main.json");

        try
        {
            var updatedJson = JsonConvert.SerializeObject(spawns, Formatting.Indented);
            File.WriteAllText(spawnsFilePath, updatedJson);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save spawn file");
        }
    }

    /// <summary>
    /// Основной метод для обновления высоты в файле спавна
    /// </summary>
    public static bool UpdateSpawnFileHeight(uint unitId, float x, float y, float newZ)
    {
        var spawns = LoadSpawnFile();
        if (spawns == null)
            return false;

        if (UpdateSpawnHeight(spawns, unitId, x, y, newZ))
        {
            SaveSpawnFile(spawns);
            Logger.Info($"Updated spawn height for NPC {unitId} at ({x}, {y}) to Z={newZ}");
            return true;
        }

        return false;
    }
    #endregion
}

public class Building
{
    private const float FloorThreshold = 5.6f;

    private static int ComputeFloorCount(float minZ, float maxZ)
    {
        var verticalDiff = maxZ - minZ;
        if (verticalDiff < FloorThreshold)
            return 1;
        return (int)(verticalDiff / FloorThreshold) + 1;
    }

    public static float GetFloorHeight(float minZ, float maxZ, int floorIndex)
    {
        var totalFloors = ComputeFloorCount(minZ, maxZ);
        if (floorIndex < 0 || floorIndex >= totalFloors)
            throw new ArgumentOutOfRangeException(nameof(floorIndex), "Номер этажа вне диапазона");

        if (totalFloors == 1)
            return (minZ + maxZ) / 2;

        var delta = (maxZ - minZ) / (totalFloors - 1);
        return minZ + floorIndex * delta;
    }

    public static float GetFloorHeight(float minZ, float maxZ, float referenceZ)
    {
        var totalFloors = ComputeFloorCount(minZ, maxZ);
        if (totalFloors == 1)
            return (minZ + maxZ) / 2;

        var delta = (maxZ - minZ) / (totalFloors - 1);
        var floorIndex = (int)Math.Round((referenceZ - minZ) / delta);

        floorIndex = Math.Max(0, Math.Min(floorIndex, totalFloors - 1));
        return minZ + floorIndex * delta;
    }
}
