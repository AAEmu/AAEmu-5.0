using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using AAEmu.Game.Physics;

using Jitter2.LinearMath;

using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.World;

/// <summary>
/// Tests for <see cref="WorldStaticGeometryIndex"/>:
/// TryRaycastDown, TryCollectRaycastDownHits, IsSegmentWallBlocked,
/// GetEntryCountAt, DescribeEntriesAt, Clear.
///
/// Because WorldCell requires WorldManager/WorldTemplate, test entries are injected
/// directly into the index's private _grid via reflection using a <see cref="FakeEntry"/>
/// that implements the same triangle/AABB contract as production entries.
/// </summary>
public class WorldInstanceStaticGeometrySegmentTests
{
    // -----------------------------------------------------------------------
    // FakeEntry — mirrors StaticBrushEntry AABB-box triangle layout
    // -----------------------------------------------------------------------

    /// <summary>
    /// Implements the same IStaticGeometryEntry contract as production entries.
    /// Triangles are built from 12 AABB face triangles (same as StaticBrushEntry).
    /// Jitter coordinate convention: X=worldX, Y=worldZ(up), Z=worldY.
    /// </summary>
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
            var min = b.Min;
            var max = b.Max;
            var c000 = new JVector(min.X, min.Y, min.Z);
            var c100 = new JVector(max.X, min.Y, min.Z);
            var c010 = new JVector(min.X, max.Y, min.Z);
            var c110 = new JVector(max.X, max.Y, min.Z);
            var c001 = new JVector(min.X, min.Y, max.Z);
            var c101 = new JVector(max.X, min.Y, max.Z);
            var c011 = new JVector(min.X, max.Y, max.Z);
            var c111 = new JVector(max.X, max.Y, max.Z);
            return
            [
                // Top face (Y = max.Y)
                new JTriangle(c010, c110, c111),
                new JTriangle(c010, c111, c011),
                // Bottom face
                new JTriangle(c000, c101, c100),
                new JTriangle(c000, c001, c101),
                // Front face
                new JTriangle(c001, c111, c101),
                new JTriangle(c001, c011, c111),
                // Back face
                new JTriangle(c000, c100, c110),
                new JTriangle(c000, c110, c010),
                // Left face
                new JTriangle(c000, c010, c011),
                new JTriangle(c000, c011, c001),
                // Right face
                new JTriangle(c100, c101, c111),
                new JTriangle(c100, c111, c110),
            ];
        }

        /// <summary>Satisfies the IStaticGeometryEntry interface used by the index internals.</summary>
        public List<JTriangle> GetTransformedTriangles() => _triangles;
    }

    // -----------------------------------------------------------------------
    // Reflection-based injection helpers
    // -----------------------------------------------------------------------

    private static readonly FieldInfo GridField =
        typeof(WorldStaticGeometryIndex).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo GridLockField =
        typeof(WorldStaticGeometryIndex).GetField("_gridLock", BindingFlags.NonPublic | BindingFlags.Instance);

    private const float InvCellSize = 1f / 100f;

    /// <summary>
    /// Injects a <see cref="FakeEntry"/> into the index's internal grid without requiring a real WorldCell.
    /// </summary>
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
    /// Creates a fresh index containing a single AABB-box entry.
    /// Coordinates are in Jitter space: X=worldX, Y=worldZ(up), Z=worldY.
    /// </summary>
    private static WorldStaticGeometryIndex BuildIndexWithJitterBox(
        float jMinX, float jMinY, float jMinZ,
        float jMaxX, float jMaxY, float jMaxZ)
    {
        var index = new WorldStaticGeometryIndex();
        InjectEntry(index, new FakeEntry(new JBoundingBox(
            new JVector(jMinX, jMinY, jMinZ),
            new JVector(jMaxX, jMaxY, jMaxZ))));
        return index;
    }

    // -----------------------------------------------------------------------
    // 1. Initial state
    // -----------------------------------------------------------------------

    [Fact]
    public void Clear_OnFreshIndex_CountsRemainZero()
    {
        var index = new WorldStaticGeometryIndex();
        index.Clear();
        Assert.Equal(0, index.TotalBrushCount);
        Assert.Equal(0, index.TotalVoxelCount);
    }

    [Fact]
    public void GetEntryCountAt_EmptyIndex_ReturnsZero()
    {
        var index = new WorldStaticGeometryIndex();
        Assert.Equal(0, index.GetEntryCountAt(50f, 50f));
    }

    // -----------------------------------------------------------------------
    // 2. TryRaycastDown — empty index
    // -----------------------------------------------------------------------

    [Fact]
    public void TryRaycastDown_EmptyIndex_ReturnsFalse()
    {
        var index = new WorldStaticGeometryIndex();
        var hit = index.TryRaycastDown(100f, 100f, 500f, out var hitZ);
        Assert.False(hit);
        Assert.Equal(0f, hitZ);
    }

    // -----------------------------------------------------------------------
    // 3. TryRaycastDown — ray hits top face of box
    // -----------------------------------------------------------------------

    [Fact]
    public void TryRaycastDown_RayAboveBox_HitsTopFace()
    {
        // Jitter: jX[10..20], jY[5..20](height), jZ[10..20]
        // worldX [10..20], worldZ(up) [5..20], worldY [10..20]
        // Top face of box at worldZ=20 (jitter Y=20).
        var index = BuildIndexWithJitterBox(10f, 5f, 10f, 20f, 20f, 20f);

        // Ray from worldX=15, worldY=15, worldZ=100 (above box top at 20)
        var hit = index.TryRaycastDown(15f, 15f, 100f, out var hitZ);

        Assert.True(hit, "Expected ray to hit top face of box.");
        Assert.InRange(hitZ, 19.5f, 20.5f);
    }

    [Fact]
    public void TryRaycastDown_RayBelowBox_ReturnsFalse()
    {
        // Box worldZ bottom at 10, top at 20 (jitter Y [10..20])
        var index = BuildIndexWithJitterBox(10f, 10f, 10f, 20f, 20f, 20f);

        // Ray starts at worldZ=5 — below the box floor
        var hit = index.TryRaycastDown(15f, 15f, 5f, out _);

        Assert.False(hit, "Ray starting below box should not hit box.");
    }

    [Fact]
    public void TryRaycastDown_RayMissesBoxHorizontally_ReturnsFalse()
    {
        var index = BuildIndexWithJitterBox(10f, 5f, 10f, 20f, 20f, 20f);

        // worldX=50, worldY=50 — outside box footprint
        var hit = index.TryRaycastDown(50f, 50f, 100f, out _);

        Assert.False(hit, "Ray outside box footprint should miss.");
    }

    // -----------------------------------------------------------------------
    // 4. TryRaycastDown — two stacked boxes, highest surface returned
    // -----------------------------------------------------------------------

    [Fact]
    public void TryRaycastDown_TwoStackedBoxes_ReturnsHigherSurface()
    {
        // Lower box: jitter Y [0..10], upper box: jitter Y [20..30]
        var index = BuildIndexWithJitterBox(10f, 0f, 10f, 20f, 10f, 20f);
        InjectEntry(index, new FakeEntry(new JBoundingBox(
            new JVector(10f, 20f, 10f),
            new JVector(20f, 30f, 20f))));

        var hit = index.TryRaycastDown(15f, 15f, 100f, out var hitZ);

        Assert.True(hit);
        // Top of upper box is at jitter Y=30 (worldZ=30)
        Assert.InRange(hitZ, 28f, 32f);
    }

    // -----------------------------------------------------------------------
    // 5. TryCollectRaycastDownHits
    // -----------------------------------------------------------------------

    [Fact]
    public void TryCollectRaycastDownHits_EmptyIndex_ReturnsFalseAndEmptyList()
    {
        var index = new WorldStaticGeometryIndex();
        var hits = new List<float>();
        var result = index.TryCollectRaycastDownHits(100f, 100f, 500f, hits);
        Assert.False(result);
        Assert.Empty(hits);
    }

    [Fact]
    public void TryCollectRaycastDownHits_RayAboveBox_ReturnsHitsSortedDescending()
    {
        var index = BuildIndexWithJitterBox(10f, 5f, 10f, 20f, 20f, 20f);
        var hits = new List<float>();

        var result = index.TryCollectRaycastDownHits(15f, 15f, 100f, hits);

        Assert.True(result, "Expected at least one hit.");
        Assert.NotEmpty(hits);
        for (var i = 0; i < hits.Count - 1; i++)
            Assert.True(hits[i] >= hits[i + 1], "Hits must be sorted in descending order.");
    }

    [Fact]
    public void TryCollectRaycastDownHits_NullList_ThrowsArgumentNullException()
    {
        var index = new WorldStaticGeometryIndex();
        Assert.Throws<System.ArgumentNullException>(
            () => index.TryCollectRaycastDownHits(0f, 0f, 100f, null));
    }

    // -----------------------------------------------------------------------
    // 6. IsSegmentWallBlocked — empty / far from geometry
    // -----------------------------------------------------------------------

    [Fact]
    public void IsSegmentWallBlocked_EmptyIndex_ReturnsFalse()
    {
        var index = new WorldStaticGeometryIndex();
        var result = index.IsSegmentWallBlocked(
            new Vector3(0f, 0f, 10f),
            new Vector3(10f, 0f, 10f),
            2f);
        Assert.False(result);
    }

    [Fact]
    public void IsSegmentWallBlocked_SegmentFarFromBox_ReturnsFalse()
    {
        // Box at jX[10..20], jY[0..10], jZ[10..20]
        var index = BuildIndexWithJitterBox(10f, 0f, 10f, 20f, 10f, 20f);

        var blocked = index.IsSegmentWallBlocked(
            new Vector3(200f, 200f, 5f),
            new Vector3(210f, 200f, 5f),
            2f);

        Assert.False(blocked, "Segment far from box should not be blocked.");
    }

    // -----------------------------------------------------------------------
    // 7. IsSegmentWallBlocked — wall detection
    // -----------------------------------------------------------------------

    [Fact]
    public void IsSegmentWallBlocked_SegmentCrossesVerticalWall_ReturnsTrue()
    {
        // Thin vertical wall at worldX ≈ 50, worldY [0..10], worldZ [0..20]
        // Jitter: jX[49..51], jY[0..20](up), jZ[0..10]
        // Wall face normals ≈ (±1, 0, 0) → |normal.Y| = 0 < 0.55 → wall-like.
        var index = BuildIndexWithJitterBox(49f, 0f, 0f, 51f, 20f, 10f);

        // Segment from worldX=45 to worldX=55, at worldY=5, worldZ=10 — crosses wall
        var blocked = index.IsSegmentWallBlocked(
            new Vector3(45f, 5f, 10f),
            new Vector3(55f, 5f, 10f),
            2f);

        Assert.True(blocked, "Horizontal segment crossing a vertical wall should be blocked.");
    }

    [Fact]
    public void IsSegmentWallBlocked_SegmentDoesNotCrossWall_ReturnsFalse()
    {
        // Same wall at jX[49..51]
        var index = BuildIndexWithJitterBox(49f, 0f, 0f, 51f, 20f, 10f);

        // Segment runs at worldX=40 — does not reach wall (worldX=49)
        var blocked = index.IsSegmentWallBlocked(
            new Vector3(40f, 0f, 10f),
            new Vector3(40f, 10f, 10f),
            2f);

        Assert.False(blocked, "Segment not crossing the wall should not be blocked.");
    }

    // -----------------------------------------------------------------------
    // 8. GetEntryCountAt and DescribeEntriesAt
    // -----------------------------------------------------------------------

    [Fact]
    public void GetEntryCountAt_WithInjectedEntry_ReturnsNonZero()
    {
        // Box at jitter [0..50, 0..50, 0..50] — within first 100m grid cell
        var index = BuildIndexWithJitterBox(0f, 0f, 0f, 50f, 50f, 50f);

        var count = index.GetEntryCountAt(25f, 25f);
        Assert.True(count > 0, $"Expected entry count > 0 at (25, 25), got {count}.");
    }

    [Fact]
    public void DescribeEntriesAt_EmptyIndex_ReturnsEmptyString()
    {
        var index = new WorldStaticGeometryIndex();
        Assert.Equal(string.Empty, index.DescribeEntriesAt(50f, 50f));
    }

    [Fact]
    public void DescribeEntriesAt_WithEntry_ReturnsNonEmptyString()
    {
        var index = BuildIndexWithJitterBox(0f, 0f, 0f, 50f, 50f, 50f);
        var desc = index.DescribeEntriesAt(25f, 25f);
        Assert.NotEmpty(desc);
    }
}
