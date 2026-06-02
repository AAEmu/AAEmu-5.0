using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Tests for <see cref="InstanceWorld.GetHeight(float, float, float)"/>:
/// the фаза-1 overload that consults <see cref="WorldStaticGeometryIndex"/> before
/// falling back to the heightmap.
/// </summary>
public class WorldGetHeightStaticGeoTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a PhysicsManager without running its constructor (avoids AppConfiguration.Instance NPE).
    /// Only StaticGeometryIndex is populated.
    /// </summary>
    private static PhysicsManager CreatePhysicsStub(WorldStaticGeometryIndex index)
    {
        var physics = (PhysicsManager)RuntimeHelpers.GetUninitializedObject(typeof(PhysicsManager));
        var backingField = typeof(PhysicsManager).GetField(
            $"<{nameof(PhysicsManager.StaticGeometryIndex)}>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        backingField?.SetValue(physics, index);
        return physics;
    }

    private static InstanceWorld BuildWorldWithIndex(WorldStaticGeometryIndex index)
        => new InstanceWorld { Physics = CreatePhysicsStub(index) };

    private static InstanceWorld BuildWorldWithPlatform(
        float jMinX, float jMinY, float jMinZ,
        float jMaxX, float jMaxY, float jMaxZ)
    {
        var index = new WorldStaticGeometryIndex();
        InjectEntry(index, new FakeEntry(new JBoundingBox(
            new JVector(jMinX, jMinY, jMinZ),
            new JVector(jMaxX, jMaxY, jMaxZ))));
        return BuildWorldWithIndex(index);
    }

    private static void InjectEntry(WorldStaticGeometryIndex index, IStaticGeometryEntry entry)
    {
        var gridField = typeof(WorldStaticGeometryIndex).GetField(
            "_grid", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var grid = (Dictionary<(int, int), List<IStaticGeometryEntry>>)gridField.GetValue(index)!;

        var aabb = entry.WorldAabb;
        const float CellSize = 64f;
        int cellMinX = (int)System.MathF.Floor(aabb.Min.X / CellSize);
        int cellMinZ = (int)System.MathF.Floor(aabb.Min.Z / CellSize);
        int cellMaxX = (int)System.MathF.Floor(aabb.Max.X / CellSize);
        int cellMaxZ = (int)System.MathF.Floor(aabb.Max.Z / CellSize);

        for (var cx = cellMinX; cx <= cellMaxX; cx++)
        for (var cz = cellMinZ; cz <= cellMaxZ; cz++)
        {
            var key = (cx, cz);
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = new List<IStaticGeometryEntry>();
            lock (list)
                list.Add(entry);
        }
    }

    // -----------------------------------------------------------------------
    // FakeEntry
    // -----------------------------------------------------------------------

    private sealed class FakeEntry : IStaticGeometryEntry
    {
        public JBoundingBox WorldAabb { get; }
        private readonly List<JTriangle> _triangles;

        public FakeEntry(JBoundingBox aabb)
        {
            WorldAabb = aabb;
            _triangles = BuildBoxTriangles(aabb);
        }

        private static List<JTriangle> BuildBoxTriangles(JBoundingBox b)
        {
            var mn = b.Min; var mx = b.Max;
            var c000 = new JVector(mn.X, mn.Y, mn.Z); var c100 = new JVector(mx.X, mn.Y, mn.Z);
            var c010 = new JVector(mn.X, mx.Y, mn.Z); var c110 = new JVector(mx.X, mx.Y, mn.Z);
            var c001 = new JVector(mn.X, mn.Y, mx.Z); var c101 = new JVector(mx.X, mn.Y, mx.Z);
            var c011 = new JVector(mn.X, mx.Y, mx.Z); var c111 = new JVector(mx.X, mx.Y, mx.Z);
            return
            [
                new JTriangle(c000, c100, c010), new JTriangle(c100, c110, c010),
                new JTriangle(c001, c011, c101), new JTriangle(c101, c011, c111),
                new JTriangle(c000, c001, c100), new JTriangle(c100, c001, c101),
                new JTriangle(c010, c110, c011), new JTriangle(c110, c111, c011),
                new JTriangle(c000, c010, c001), new JTriangle(c010, c011, c001),
                new JTriangle(c100, c101, c110), new JTriangle(c110, c101, c111),
            ];
        }

        public List<JTriangle> GetTransformedTriangles() => _triangles;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void GetHeight_NullPhysics_FallsBackToHeightmap()
    {
        // World with no Physics — should not throw, returns 0 (unloaded heightmap).
        var world = new InstanceWorld();
        var result = world.GetHeight(10f, 10f, 5f);
        Assert.Equal(0f, result);
    }

    [Fact]
    public void GetHeight_EmptyIndex_FallsBackToHeightmap()
    {
        var world = BuildWorldWithIndex(new WorldStaticGeometryIndex());
        var result = world.GetHeight(10f, 10f, 5f);
        Assert.Equal(0f, result);
    }

    [Fact]
    public void GetHeight_RaisedPlatformAboveOrigin_ReturnsPlatformTopSurface()
    {
        // Jitter Y∈[10,20] → world-Z top = 20; referenceZ = 25 (above platform).
        var world = BuildWorldWithPlatform(0f, 10f, 0f, 50f, 20f, 50f);

        var result = world.GetHeight(25f, 25f, 25f);

        Assert.InRange(result, 19f, 21f);
    }

    [Fact]
    public void GetHeight_ReferenceZBelowPlatform_FallsBackToHeightmap()
    {
        // Platform top = 20, but referenceZ = 5 — probe starts at 7, below top face → misses.
        var world = BuildWorldWithPlatform(0f, 10f, 0f, 50f, 20f, 50f);

        var result = world.GetHeight(25f, 25f, 5f);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void GetHeight_OutsidePlatformXY_FallsBackToHeightmap()
    {
        var world = BuildWorldWithPlatform(0f, 10f, 0f, 50f, 20f, 50f);

        var result = world.GetHeight(100f, 25f, 25f);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void GetHeight_TwoPlatforms_ReturnsUpperClosestFromAbove()
    {
        // Lower top = 10, upper top = 20; referenceZ = 22 → expect upper (20).
        var index = new WorldStaticGeometryIndex();
        InjectEntry(index, new FakeEntry(new JBoundingBox(
            new JVector(0f, 5f, 0f), new JVector(50f, 10f, 50f))));
        InjectEntry(index, new FakeEntry(new JBoundingBox(
            new JVector(0f, 15f, 0f), new JVector(50f, 20f, 50f))));
        var world = BuildWorldWithIndex(index);

        var result = world.GetHeight(25f, 25f, 22f);

        Assert.InRange(result, 19f, 21f);
    }
}
