using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

// Target adaptation notes:
// Source AIUtilsTests tests IsRaisedIndoorRoamingSegmentBlocked, which does not exist
// in the target's AIUtils class. Those tests cannot be ported.
// This file tests WorldStaticGeometryIndex integration that underpins the static
// segment blocker functionality referenced in the source tests, using the same
// FakeStaticGeometryEntry injection pattern as other geodata tests.

namespace AAEmu.UnitTests.Game.Models.Game.AI.Utils;

public class AIUtilsTests
{
    /// <summary>
    /// Smoke test: WorldStaticGeometryIndex correctly reports a wall between two points.
    /// This mirrors the intent of the source IsRaisedIndoorRoamingSegmentBlocked tests,
    /// applied to the layer available in the target architecture.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_LocalPassStaticWall_IsSegmentBlocked_ReturnsTrue()
    {
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0, CreateBoxWallEntry(2.6f, -0.5f, 0f, 3.4f, 0.5f, 3f));

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(0f, 0f, 1f),
            new Vector3(6f, 0f, 1f),
            verticalTolerance: 8f);

        Assert.True(blocked);
    }

    /// <summary>
    /// Verifies that a segment that does not pass through the wall is not reported as blocked.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_WithoutBlocker_IsSegmentBlocked_ReturnsFalse()
    {
        var index = new WorldStaticGeometryIndex();

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(0f, 0f, 1f),
            new Vector3(6f, 0f, 1f),
            verticalTolerance: 8f);

        Assert.False(blocked);
    }

    /// <summary>
    /// Verifies that GetEntryCountAt returns zero when no entries are in the cell.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_EmptyIndex_GetEntryCountAt_ReturnsZero()
    {
        var index = new WorldStaticGeometryIndex();

        var count = index.GetEntryCountAt(3f, 0f);

        Assert.Equal(0, count);
    }

    /// <summary>
    /// Verifies that GetEntryCountAt returns non-zero after an entry is injected at that position.
    /// </summary>
    [Fact]
    public void StaticGeometryIndex_AfterInjection_GetEntryCountAt_ReturnsNonZero()
    {
        var index = new WorldStaticGeometryIndex();
        AddStaticGeometryEntry(index, 0, 0, CreateBoxWallEntry(2.6f, -0.5f, 0f, 3.4f, 0.5f, 3f));

        var count = index.GetEntryCountAt(3f, 0f);

        Assert.True(count > 0);
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
