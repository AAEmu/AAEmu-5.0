using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

namespace AAEmu.UnitTests.Game.Physics;

/// <summary>
/// Tests for <see cref="StaticGeoDataService"/>:
/// TryResolveDistinctLayerHeight and TryGetClosestDistinctLayerPoint.
///
/// Uses reflection to inject FakeEntry instances directly into WorldStaticGeometryIndex._grid
/// (same technique as WorldInstanceStaticGeometrySegmentTests).
/// World is constructed with a flat heightmap (terrain height = 0) so that any geometry hit
/// at Z > 0.5 qualifies as a "distinct layer" (elevated) and at Z < -1.25 as subterranean.
/// </summary>
public class StaticGeoDataServiceTests
{
    // -----------------------------------------------------------------------
    // FakeEntry — implements IStaticGeometryEntry with programmable triangles
    // -----------------------------------------------------------------------

    private sealed class FakeEntry : IStaticGeometryEntry
    {
        private readonly List<JTriangle> _triangles;

        public FakeEntry(JBoundingBox aabb, List<JTriangle> triangles)
        {
            WorldAabb = aabb;
            _triangles = triangles;
        }

        public JBoundingBox WorldAabb { get; }

        public List<JTriangle> GetTransformedTriangles() => _triangles;
    }

    // -----------------------------------------------------------------------
    // Injection helpers
    // -----------------------------------------------------------------------

    private static readonly FieldInfo GridField =
        typeof(WorldStaticGeometryIndex).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo GridLockField =
        typeof(WorldStaticGeometryIndex).GetField("_gridLock", BindingFlags.NonPublic | BindingFlags.Instance);

    private const float InvCellSize = 1f / 100f;

    private static void InjectEntry(WorldStaticGeometryIndex index, FakeEntry entry)
    {
        Assert.NotNull(GridField);
        Assert.NotNull(GridLockField);

        var gridObj = GridField.GetValue(index);
        var lockObj = GridLockField.GetValue(index);
        var gridType = gridObj.GetType();
        var tryGetValue = gridType.GetMethod("TryGetValue");
        var itemSetter = gridType.GetProperty("Item");

        var aabb = entry.WorldAabb;
        var minCellX = (int)(aabb.Min.X * InvCellSize);
        var maxCellX = (int)(aabb.Max.X * InvCellSize);
        var minCellY = (int)(aabb.Min.Z * InvCellSize);
        var maxCellY = (int)(aabb.Max.Z * InvCellSize);

        lock (lockObj)
        {
            for (var cy = minCellY; cy <= maxCellY; cy++)
            {
                for (var cx = minCellX; cx <= maxCellX; cx++)
                {
                    var key = (cx, cy);
                    var getArgs = new object[] { key, null };
                    var found = (bool)tryGetValue.Invoke(gridObj, getArgs);

                    System.Collections.IList list;
                    if (found && getArgs[1] != null)
                    {
                        list = (System.Collections.IList)getArgs[1];
                    }
                    else
                    {
                        var listType = gridType.GetGenericArguments()[1];
                        list = (System.Collections.IList)System.Activator.CreateInstance(listType);
                        itemSetter.SetValue(gridObj, list, new object[] { key });
                    }

                    lock (list)
                        list.Add(entry);
                }
            }
        }
    }

    /// <summary>
    /// Builds a horizontal flat quad (two triangles) at the given jitter Y height.
    /// Jitter coords: X=worldX, Y=worldZ(up), Z=worldY.
    /// The quad covers worldX [x-r..x+r], worldY [y-r..y+r].
    /// </summary>
    private static FakeEntry BuildFlatFloorAt(float worldX, float worldY, float worldZ, float radius)
    {
        // Jitter: X=worldX, Y=worldZ(up), Z=worldY
        var jMinX = worldX - radius;
        var jMaxX = worldX + radius;
        var jY = worldZ;       // top face at worldZ
        var jMinZ = worldY - radius;
        var jMaxZ = worldY + radius;

        var aabb = new JBoundingBox(
            new JVector(jMinX, jY - 0.1f, jMinZ),
            new JVector(jMaxX, jY + 0.1f, jMaxZ));

        // Flat triangles at top face (Y = jY + 0.1f)
        var p0 = new JVector(jMinX, jY + 0.1f, jMinZ);
        var p1 = new JVector(jMaxX, jY + 0.1f, jMinZ);
        var p2 = new JVector(jMaxX, jY + 0.1f, jMaxZ);
        var p3 = new JVector(jMinX, jY + 0.1f, jMaxZ);

        return new FakeEntry(aabb, [
            new JTriangle(p0, p1, p2),
            new JTriangle(p0, p2, p3)
        ]);
    }

    /// <summary>
    /// Creates a World with flat zero-height terrain and an attached StaticGeometryIndex.
    /// Uses FormatterServices to create PhysicsManager without invoking its constructor
    /// (which would require AppConfiguration.Instance).
    /// WorldTemplate has CellX=1, CellY=1. Coordinates above 2*1024=2048 fall outside bounds,
    /// causing GetRawHeightMapHeight to return 0 without accessing Cells[].
    /// </summary>
    private static World CreateWorldWithIndex(out WorldStaticGeometryIndex index)
    {
        var world = new World
        {
            Id = 9900,
            Name = "test-static-geo-service",
            Template = new WorldTemplate
            {
                Id = 9900,
                Name = "test-template",
                CellX = 1,
                CellY = 1,
                Cells = new WorldCell[1, 1],
                ZoneKeyByRegions = new uint[1, 1] { { 9900 } }
            }
        };

        index = new WorldStaticGeometryIndex();

        // Build a PhysicsManager bypassing its constructor (avoids AppConfiguration access).
        var physics = (PhysicsManager)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(PhysicsManager));

        // Inject the index into PhysicsManager's backing field (auto-property {get;} = new())
        var backingField = typeof(PhysicsManager).GetField(
            "<StaticGeometryIndex>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(backingField);
        backingField!.SetValue(physics, index);

        world.Physics = physics;
        return world;
    }

    // Use coordinates outside the single-cell template (CellX=1, CELL_SIZE=1024)
    // so that GetRawHeightMapHeight returns 0f without accessing Cells[].
    // x/1024 = 2500/1024 = 2 > CellX=1 → out of bounds → return 0f.
    private const float TestX = 2500f;
    private const float TestY = 2500f;

    // -----------------------------------------------------------------------
    // 1. Null / empty guards
    // -----------------------------------------------------------------------

    [Fact]
    public void TryResolveDistinctLayerHeight_NullWorld_ReturnsFalse()
    {
        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            null,
            new Vector3(10f, 10f, 5f),
            3f,
            5f,
            0.9f,
            out var height);

        Assert.False(result);
        Assert.Equal(0f, height);
    }

    [Fact]
    public void TryResolveDistinctLayerHeight_WorldWithoutIndex_ReturnsFalse()
    {
        var world = new World { Id = 1, Template = new WorldTemplate { Id = 1 } };
        // Physics is null — no StaticGeometryIndex

        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            world,
            new Vector3(10f, 10f, 5f),
            3f,
            5f,
            0.9f,
            out var height);

        Assert.False(result);
        Assert.Equal(0f, height);
    }

    [Fact]
    public void TryResolveDistinctLayerHeight_EmptyIndex_ReturnsFalse()
    {
        var world = CreateWorldWithIndex(out _);

        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            world,
            new Vector3(TestX, TestY, 5f),
            3f,
            5f,
            0.9f,
            out var height);

        Assert.False(result);
        Assert.Equal(0f, height);
    }

    // -----------------------------------------------------------------------
    // 2. Elevated floor above terrain (terrain = 0 at out-of-bounds coords, floor at Z = 10)
    // -----------------------------------------------------------------------

    [Fact]
    public void TryResolveDistinctLayerHeight_ElevatedFloor_ReturnsHit()
    {
        var world = CreateWorldWithIndex(out var index);
        // Flat floor at worldZ = 10 (terrain = 0 out of bounds, delta = 10 >= 0.5 → distinct)
        InjectEntry(index, BuildFlatFloorAt(TestX, TestY, 10f, 5f));

        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            world,
            new Vector3(TestX, TestY, 10f),
            3f,
            10f,
            1.5f,
            out var height);

        Assert.True(result, "Expected distinct layer hit at elevated floor.");
        Assert.InRange(height, 9.5f, 11f);
    }

    // -----------------------------------------------------------------------
    // 3. Floor at terrain level — should NOT be considered distinct
    // -----------------------------------------------------------------------

    [Fact]
    public void TryResolveDistinctLayerHeight_FloorAtTerrainLevel_ReturnsFalse()
    {
        var world = CreateWorldWithIndex(out var index);
        // Flat floor at worldZ = 0.2 — too close to terrain (0) to be distinct (< 0.5 threshold)
        InjectEntry(index, BuildFlatFloorAt(TestX, TestY, 0.2f, 5f));

        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            world,
            new Vector3(TestX, TestY, 0.2f),
            3f,
            0.2f,
            1.5f,
            out _);

        Assert.False(result, "Floor within terrain epsilon should not be a distinct layer.");
    }

    // -----------------------------------------------------------------------
    // 4. Preferred height tolerance filters out far-Z hits
    // -----------------------------------------------------------------------

    [Fact]
    public void TryResolveDistinctLayerHeight_HitFarFromPreferredZ_ReturnsFalse()
    {
        var world = CreateWorldWithIndex(out var index);
        // Floor at worldZ = 10, but preferredHeight = 5 with tight tolerance 0.5
        InjectEntry(index, BuildFlatFloorAt(TestX, TestY, 10f, 5f));

        var result = StaticGeoDataService.TryResolveDistinctLayerHeight(
            world,
            new Vector3(TestX, TestY, 5f),
            3f,
            5f,    // preferred
            0.5f,  // tolerance — hit at Z=10 is 5m away, > 0.5
            out _);

        Assert.False(result, "Hit too far from preferredHeight should be rejected.");
    }
}
