using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

// Target adaptation notes:
// Source PathNode.FindPath(WorldInstance, Vector3, Vector3, out ...) does not exist in
// this codebase. The target PathNode.FindPath(Point, Point) uses the old point-based API.
// The source test covering static geometry runtime wall blocking in A* cannot be ported
// directly. This file covers WorldStaticGeometryIndex injection patterns used by A* and
// serves as a regression anchor for future A* world-mesh integration work.

namespace AAEmu.UnitTests.Game.Models.Game.AI.AStar;

public class PathNodeTests
{
    /// <summary>
    /// Smoke test: PathNode can be created and its ZoneKey property round-trips correctly.
    /// </summary>
    [Fact]
    public void PathNode_ZoneKey_RoundTrips()
    {
        var pathNode = new PathNode { ZoneKey = 42 };
        Assert.Equal(42u, pathNode.ZoneKey);
    }

    /// <summary>
    /// Verifies that WorldStaticGeometryIndex can have entries injected via reflection,
    /// and that GetEntryCountAt returns the expected count after injection.
    /// This underpins the static geometry check used in hybrid path validation.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_AfterInjection_EntryCountIsCorrect()
    {
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0, CreateBoxWallEntry(4.7f, 1.7f, 0f, 5.3f, 2.3f, 3f));

        // Entry should be queryable at the injected cell position.
        var count = index.GetEntryCountAt(5.0f, 2.0f);
        Assert.True(count > 0, "Expected at least one entry at the wall position.");
    }

    /// <summary>
    /// Verifies that IsSegmentWallBlocked returns true for a line through the injected wall.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_IsSegmentWallBlocked_WallInPath_ReturnsTrue()
    {
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0,
            new FakeStaticGeometryEntry(
                new JBoundingBox(new JVector(4.7f, 0f, 1.7f), new JVector(5.3f, 3f, 2.3f)),
                [
                    new JTriangle(new JVector(5f, 0f, 1.7f), new JVector(5f, 3f, 1.7f), new JVector(5f, 0f, 2.3f)),
                    new JTriangle(new JVector(5f, 3f, 1.7f), new JVector(5f, 3f, 2.3f), new JVector(5f, 0f, 2.3f))
                ]));

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(4.0f, 2.0f, 1.5f),
            new Vector3(6.0f, 2.0f, 1.5f),
            verticalTolerance: 8f);

        Assert.True(blocked);
    }

    /// <summary>
    /// Verifies that IsSegmentWallBlocked returns false for a segment that misses the wall.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_IsSegmentWallBlocked_SegmentMissesWall_ReturnsFalse()
    {
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0,
            new FakeStaticGeometryEntry(
                new JBoundingBox(new JVector(4.7f, 0f, 1.7f), new JVector(5.3f, 3f, 2.3f)),
                [
                    new JTriangle(new JVector(5f, 0f, 1.7f), new JVector(5f, 3f, 1.7f), new JVector(5f, 0f, 2.3f)),
                    new JTriangle(new JVector(5f, 3f, 1.7f), new JVector(5f, 3f, 2.3f), new JVector(5f, 0f, 2.3f))
                ]));

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(1.0f, 2.0f, 1.5f),
            new Vector3(3.0f, 2.0f, 1.5f),
            verticalTolerance: 8f);

        Assert.False(blocked);
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

    private static FakeStaticGeometryEntry CreateBoxWallEntry(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        return new FakeStaticGeometryEntry(
            new JBoundingBox(new JVector(minX, minZ, minY), new JVector(maxX, maxZ, maxY)),
            [
                new JTriangle(new JVector(minX, minZ, minY), new JVector(minX, maxZ, minY), new JVector(minX, minZ, maxY)),
                new JTriangle(new JVector(minX, maxZ, minY), new JVector(minX, maxZ, maxY), new JVector(minX, minZ, maxY)),
                new JTriangle(new JVector(maxX, minZ, minY), new JVector(maxX, minZ, maxY), new JVector(maxX, maxZ, minY)),
                new JTriangle(new JVector(maxX, maxZ, minY), new JVector(maxX, minZ, maxY), new JVector(maxX, maxZ, maxY)),
                new JTriangle(new JVector(minX, minZ, minY), new JVector(maxX, minZ, minY), new JVector(minX, maxZ, minY)),
                new JTriangle(new JVector(maxX, minZ, minY), new JVector(maxX, maxZ, minY), new JVector(minX, maxZ, minY)),
                new JTriangle(new JVector(minX, minZ, maxY), new JVector(minX, maxZ, maxY), new JVector(maxX, minZ, maxY)),
                new JTriangle(new JVector(maxX, minZ, maxY), new JVector(minX, maxZ, maxY), new JVector(maxX, maxZ, maxY))
            ]);
    }

    private sealed class FakeStaticGeometryEntry : IStaticGeometryEntry
    {
        private readonly List<JTriangle> _triangles;

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
