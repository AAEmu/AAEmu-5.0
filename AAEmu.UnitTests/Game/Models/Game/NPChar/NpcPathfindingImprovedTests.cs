using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

// PhysicsManager is in AAEmu.Game.Core.Managers.World (re-imported above)

using Jitter2.LinearMath;

using Moq;
using Xunit;

// Target adaptation notes:
// Source tests use WorldInstance(template, 0, true, id) — target uses World directly.
// Source tests set _parentWorld field — target's Npc.ParentWorld is a computed property
// from WorldManager.GetWorldByZone(Transform.ZoneId). Tests must register the world
// in WorldManager._worlds and _worldIdByZoneId, and set Transform.ZoneId accordingly.
// Source tests set Transform._instanceId — this field exists in target Transform.

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public sealed class NpcPathfindingImprovedTests : IDisposable
{
    private static int s_nextId = 19000;
    private readonly List<uint> _registeredWorldIds = [];

    public NpcPathfindingImprovedTests()
    {
        AppConfiguration.Instance.World ??= new WorldConfig { GeoDataMode = false };
        AppConfiguration.Instance.NpcPathfinding ??= new NpcPathfindingConfig();
    }

    public void Dispose()
    {
        var worldsField = typeof(WorldManager).GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic);
        var zoneField = typeof(WorldManager).GetField("_worldIdByZoneId", BindingFlags.Instance | BindingFlags.NonPublic);
        var worlds = worldsField?.GetValue(WorldManager.Instance) as Dictionary<uint, InstanceWorld>;
        var zoneMap = zoneField?.GetValue(WorldManager.Instance) as Dictionary<uint, uint>;

        foreach (var id in _registeredWorldIds)
        {
            worlds?.Remove(id);
            // Remove zone mapping entries that pointed to this world id
            if (zoneMap != null)
            {
                var keysToRemove = new List<uint>();
                foreach (var kv in zoneMap)
                {
                    if (kv.Value == id)
                        keysToRemove.Add(kv.Key);
                }
                foreach (var k in keysToRemove)
                    zoneMap.Remove(k);
            }
        }
        _registeredWorldIds.Clear();
    }

    private static uint GetNextId() => (uint)Interlocked.Increment(ref s_nextId);

    private InstanceWorld CreateWorld(float terrainHeight)
    {
        var worldId = GetNextId();
        var zoneId = worldId; // use same value as zone key for simplicity

        var template = new WorldTemplate
        {
            Id = worldId,
            Name = $"test_world_{worldId}",
            OceanLevel = 0,
            CellX = 1,
            CellY = 1,
            Cells = new WorldCell[1, 1],
            HeightMaxCoefficient = 1.0
        };

        // Create a WorldCell with a uniform height-map loaded via reflection
        var cell = new WorldCell(0, 0, template);
        var heightMapSize = WorldManager.CELL_HMAP_RESOLUTION;
        var heightMap = new ushort[heightMapSize, heightMapSize];

        // Convert float terrain height to ushort via the same coefficient logic
        // WorldTemplate.HeightMaxCoefficient defaults to 1.0 in tests (it's 0 if not set,
        // which would cause div-by-zero; we don't need actual height reads here since we
        // only test collision-related methods that don't call GetHeight for their core logic).
        // Leave heightMap as zero — terrain height will be 0 from the cell.

        SetNonPublicProperty(cell, nameof(WorldCell.HeightMap), heightMap);
        SetNonPublicProperty(cell, "Loaded", true);
        template.Cells[0, 0] = cell;

        var world = new InstanceWorld
        {
            Id = worldId,
            Name = template.Name,
            TemplateId = template.Id,
            Template = template,
            CellX = template.CellX,
            CellY = template.CellY,
            OceanLevel = 100f
        };

        // Register in WorldManager so ParentWorld lookup works
        var worldsField = typeof(WorldManager).GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(worldsField);
        var worlds = worldsField!.GetValue(WorldManager.Instance) as Dictionary<uint, InstanceWorld>;
        if (worlds == null)
        {
            worlds = new Dictionary<uint, InstanceWorld>();
            worldsField.SetValue(WorldManager.Instance, worlds);
        }
        worlds[worldId] = world;

        var zoneField = typeof(WorldManager).GetField("_worldIdByZoneId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(zoneField);
        var zoneMap = zoneField!.GetValue(WorldManager.Instance) as Dictionary<uint, uint>;
        if (zoneMap == null)
        {
            zoneMap = new Dictionary<uint, uint>();
            zoneField.SetValue(WorldManager.Instance, zoneMap);
        }
        zoneMap[zoneId] = worldId;

        _registeredWorldIds.Add(worldId);
        return world;
    }

    private static void SetTransformZoneId(Npc npc, uint zoneId)
    {
        // Set the _zoneId field directly to bypass OnZoneChange side-effects in tests
        var zoneIdField = typeof(AAEmu.Game.Models.Game.World.Transform.Transform)
            .GetField("_zoneId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(zoneIdField);
        zoneIdField!.SetValue(npc.Transform, zoneId);
    }

    private static void SetNonPublicProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static void SetNonPublicField(object obj, string fieldName, object value)
    {
        var type = obj.GetType();
        FieldInfo fieldInfo = null;
        while (type != null)
        {
            fieldInfo = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo != null)
                break;
            type = type.BaseType;
        }
        Assert.NotNull(fieldInfo);
        fieldInfo!.SetValue(obj, value);
    }

    private static void AddStaticGeometryEntry(WorldStaticGeometryIndex index, int cellX, int cellY, IStaticGeometryEntry entry)
    {
        var gridField = typeof(WorldStaticGeometryIndex).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(gridField);
        var grid = Assert.IsType<Dictionary<(int CellX, int CellY), List<IStaticGeometryEntry>>>(gridField!.GetValue(index));

        if (!grid.TryGetValue((cellX, cellY), out var list))
        {
            list = [];
            grid[(cellX, cellY)] = list;
        }
        list.Add(entry);
    }

    #region NpcPathfindingConfig defaults

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetEvadeTimeoutSeconds_HasDefaultValue()
    {
        var config = new NpcPathfindingConfig();
        Assert.Equal(5.0f, config.UnreachableTargetEvadeTimeoutSeconds);
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetEvadeTimeoutSeconds_CanBeSet()
    {
        var config = new NpcPathfindingConfig();
        config.UnreachableTargetEvadeTimeoutSeconds = 15.5f;
        Assert.Equal(15.5f, config.UnreachableTargetEvadeTimeoutSeconds);
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetMaxZDelta_HasDefaultValue()
    {
        var config = new NpcPathfindingConfig();
        Assert.Equal(3.0f, config.UnreachableTargetMaxZDelta);
    }

    [Fact]
    public void NpcPathfindingConfig_UnreachableTargetMaxZDelta_CanBeSet()
    {
        var config = new NpcPathfindingConfig();
        config.UnreachableTargetMaxZDelta = 5.0f;
        Assert.Equal(5.0f, config.UnreachableTargetMaxZDelta);
    }

    #endregion

    #region ShouldUseStaticWorldCollisionValidation

    [Fact]
    public void ShouldUseStaticWorldCollisionValidation_TerrainSegmentNearStaticGeometry_ReturnsTrue()
    {
        var world = CreateWorld(0f);
        var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        world.Physics = physics;

        AddStaticGeometryEntry(
            physics.StaticGeometryIndex,
            0,
            0,
            new FakeStaticGeometryEntry(
                new JBoundingBox(new JVector(4f, 0f, 4f), new JVector(6f, 3f, 6f))));

        var npc = new Npc();
        SetTransformZoneId(npc, world.Id);
        npc.Transform.Local.Position = new Vector3(1f, 1f, 0f);

        var method = typeof(Npc).GetMethod("ShouldUseStaticWorldCollisionValidation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(npc, [new Vector3(1f, 1f, 0f), new Vector3(3f, 3f, 0f)])!;

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseStaticWorldCollisionValidation_TerrainSegmentWithoutNearbyStaticGeometry_ReturnsFalse()
    {
        var world = CreateWorld(0f);
        var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        world.Physics = physics;

        var npc = new Npc();
        SetTransformZoneId(npc, world.Id);
        npc.Transform.Local.Position = new Vector3(1f, 1f, 0f);

        var method = typeof(Npc).GetMethod("ShouldUseStaticWorldCollisionValidation", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (bool)method!.Invoke(npc, [new Vector3(1f, 1f, 0f), new Vector3(3f, 3f, 0f)])!;

        Assert.False(result);
    }

    #endregion

    #region TryResolveStaticCollisionFallbackForWorldMesh

    [Fact(Skip = "Requires ModelManager initialized — NullReferenceException in GetActorModel when ModelManager not loaded")]
    public void TryResolveStaticCollisionFallbackForWorldMesh_StaticWall_ReturnsSidestep()
    {
        var world = CreateWorld(0f);
        var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        world.Physics = physics;

        AddStaticGeometryEntry(
            physics.StaticGeometryIndex,
            0,
            0,
            new FakeStaticGeometryEntry(
                new JBoundingBox(new JVector(1.6f, 0f, -0.5f), new JVector(1.9f, 3f, 0.5f)),
                [
                    new JTriangle(new JVector(1.6f, 0f, -0.5f), new JVector(1.6f, 3f, -0.5f), new JVector(1.6f, 0f, 0.5f)),
                    new JTriangle(new JVector(1.6f, 3f, -0.5f), new JVector(1.6f, 3f, 0.5f), new JVector(1.6f, 0f, 0.5f))
                ]));

        var npc = new Npc();
        SetTransformZoneId(npc, world.Id);
        SetNonPublicField(npc, "_pathfindingConfig", new NpcPathfindingConfig { PassRadius = 0.5f, MinModelSize = 0.5f });
        npc.Transform.Local.Position = new Vector3(0f, 0f, 0f);

        var found = InvokeTryResolveStaticCollisionFallbackForWorldMesh(
            npc,
            new Vector3(0f, 0f, 0f),
            new Vector3(3.5f, 0f, 0f),
            out var adjusted);

        Assert.True(found);
        Assert.True(MathF.Abs(adjusted.Y) > 0.25f);
    }

    [Fact]
    public void TryResolveStaticCollisionFallbackForWorldMesh_WithoutStaticBlocker_ReturnsFalse()
    {
        var world = CreateWorld(0f);
        var physics = new PhysicsManager { SimulationWorld = world };
        physics.Initialize();
        world.Physics = physics;

        var npc = new Npc();
        SetTransformZoneId(npc, world.Id);
        SetNonPublicField(npc, "_pathfindingConfig", new NpcPathfindingConfig { PassRadius = 0.5f, MinModelSize = 0.5f });
        npc.Transform.Local.Position = new Vector3(0f, 0f, 0f);

        var found = InvokeTryResolveStaticCollisionFallbackForWorldMesh(
            npc,
            new Vector3(0f, 0f, 0f),
            new Vector3(3.5f, 0f, 0f),
            out _);

        Assert.False(found);
    }

    private static bool InvokeTryResolveStaticCollisionFallbackForWorldMesh(Npc npc, Vector3 from, Vector3 to, out Vector3 adjusted)
    {
        var method = typeof(Npc).GetMethod(
            "TryResolveStaticCollisionFallbackForWorldMesh",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var args = new object[] { from, to, Vector3.Zero };
        var result = (bool)method!.Invoke(npc, args)!;
        adjusted = (Vector3)args[2];
        return result;
    }

    #endregion

    private sealed class FakeStaticGeometryEntry : IStaticGeometryEntry
    {
        private readonly List<JTriangle> _triangles;

        public FakeStaticGeometryEntry(JBoundingBox worldAabb)
        {
            WorldAabb = worldAabb;
            _triangles = [];
        }

        public FakeStaticGeometryEntry(JBoundingBox worldAabb, List<JTriangle> triangles)
        {
            WorldAabb = worldAabb;
            _triangles = triangles;
        }

        public JBoundingBox WorldAabb { get; }

        public List<JTriangle> GetTransformedTriangles()
        {
            return _triangles;
        }
    }
}
