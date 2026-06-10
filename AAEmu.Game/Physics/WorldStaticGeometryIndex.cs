using System.Diagnostics;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine;
using AAEmu.Game.Models.CryEngine.Objects;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

using NLog;

namespace AAEmu.Game.Physics;

/// <summary>
/// Common interface for static geometry entries in <see cref="WorldStaticGeometryIndex"/>.
/// </summary>
internal interface IStaticGeometryEntry
{
    JBoundingBox WorldAabb { get; }
    List<JTriangle> GetTransformedTriangles();
}

/// <summary>
/// Represents a single static brush instance in world space.
/// Triangles are lazily loaded from the CGF model on first raycast.
/// </summary>
internal sealed class StaticBrushEntry : IStaticGeometryEntry
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public required ObjectDataType1Brush Brush { get; init; }
    public required string ModelPath { get; init; }
    public required string MaterialPath { get; init; }
    public required JBoundingBox WorldAabb { get; init; }
    public required JVector CellOffset { get; init; }
    public required int SourceCellX { get; init; }
    public required int SourceCellY { get; init; }
    public required int MinGridCellX { get; init; }
    public required int MaxGridCellX { get; init; }
    public required int MinGridCellY { get; init; }
    public required int MaxGridCellY { get; init; }

    // Lazy-loaded transformed triangles for exact raycasting.
    private List<JTriangle> _transformedTriangles;
    private readonly object _meshLock = new();

    /// <summary>
    /// Gets or builds the transformed triangles for this brush instance.
    /// </summary>
    public List<JTriangle> GetTransformedTriangles()
    {
        if (_transformedTriangles != null)
            return _transformedTriangles;

        lock (_meshLock)
        {
            if (_transformedTriangles != null)
                return _transformedTriangles;

            try
            {
                var localTriangles = CryEngineModelHelper.MakeModel(ModelPath, MaterialPath);
                if (localTriangles == null || localTriangles.Count == 0)
                {
                    _transformedTriangles = [];
                    return _transformedTriangles;
                }

                // Build transform matrix from brush data.
                // This mirrors PhysicsManager.AddBrushObject transform logic.
                var m3X3 = new JMatrix(
                    Brush.Matrix3X4.M11, Brush.Matrix3X4.M31, Brush.Matrix3X4.M21,
                    Brush.Matrix3X4.M13, Brush.Matrix3X4.M33, Brush.Matrix3X4.M23,
                    Brush.Matrix3X4.M12, Brush.Matrix3X4.M32, Brush.Matrix3X4.M22);

                var translation = CellOffset + new JVector(
                    Brush.Matrix3X4.M14,
                    Brush.Matrix3X4.M34,
                    Brush.Matrix3X4.M24);

                var transformed = new List<JTriangle>(localTriangles.Count);
                foreach (var tri in localTriangles)
                {
                    var v0 = JVector.Transform(tri.V0, m3X3) + translation;
                    var v1 = JVector.Transform(tri.V1, m3X3) + translation;
                    var v2 = JVector.Transform(tri.V2, m3X3) + translation;
                    transformed.Add(new JTriangle(v0, v1, v2));
                }

                _transformedTriangles = transformed;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load transformed triangles for brush {0} (model: {1})", Brush.Name, ModelPath);
                _transformedTriangles = [];
            }

            return _transformedTriangles;
        }
    }
}

/// <summary>
/// Represents a single static voxel object in world space.
/// Triangles are lazily built from VoxelMeshReader on first raycast.
/// </summary>
internal sealed class StaticVoxelEntry : IStaticGeometryEntry
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public required ObjectDataType6Voxel Voxel { get; init; }
    public required JMatrix M3X3 { get; init; }
    public required JVector Translation { get; init; }
    public required JBoundingBox WorldAabb { get; init; }

    private List<JTriangle> _triangles;
    private readonly object _meshLock = new();

    public List<JTriangle> GetTransformedTriangles()
    {
        if (_triangles != null)
            return _triangles;

        lock (_meshLock)
        {
            if (_triangles != null)
                return _triangles;

            try
            {
                var verts = Voxel.MeshReader?.Vertices;
                var indices = Voxel.MeshReader?.Indices;
                if (verts == null || indices == null || verts.Count == 0 || indices.Count < 3)
                {
                    _triangles = [];
                    return _triangles;
                }

                var triCount = indices.Count / 3;
                var built = new List<JTriangle>(triCount);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var i1 = indices[i];
                    var i2 = indices[i + 1];
                    var i3 = indices[i + 2];
                    if (i1 < 0 || i1 >= verts.Count || i2 < 0 || i2 >= verts.Count || i3 < 0 || i3 >= verts.Count)
                        continue;

                    var localJ1 = new JVector(verts[i1].X, verts[i1].Z, verts[i1].Y);
                    var localJ2 = new JVector(verts[i2].X, verts[i2].Z, verts[i2].Y);
                    var localJ3 = new JVector(verts[i3].X, verts[i3].Z, verts[i3].Y);

                    var v0 = JVector.Transform(localJ1, M3X3) + Translation;
                    var v1 = JVector.Transform(localJ2, M3X3) + Translation;
                    var v2 = JVector.Transform(localJ3, M3X3) + Translation;
                    built.Add(new JTriangle(v0, v1, v2));
                }

                _triangles = built;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to lazy-build voxel triangles");
                _triangles = [];
            }

            return _triangles;
        }
    }
}

/// <summary>
/// Static spatial index over all brush geometry in the world.
/// Provides approximate AABB-based floor height queries for NPC spawn height resolution.
/// Note: without CGF mesh loading, brush triangles are approximated from AABB corners.
/// </summary>
public sealed class WorldStaticGeometryIndex
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Uniform grid cell size in meters. 100m balances memory vs query speed.
    private const float CellSize = 100f;
    private const float InvCellSize = 1f / CellSize;

    private readonly Dictionary<(int CellX, int CellY), List<IStaticGeometryEntry>> _grid = new();
    private readonly Dictionary<(int CellX, int CellY), List<StaticBrushEntry>> _entriesBySourceCell = new();
    private readonly object _gridLock = new();

    /// <summary>
    /// Diagnostic: total brushes registered across the world.
    /// </summary>
    public int TotalBrushCount { get; private set; }

    /// <summary>
    /// Diagnostic: total voxel objects registered across the world.
    /// </summary>
    public int TotalVoxelCount { get; private set; }

    /// <summary>
    /// Diagnostic: number of brush entries in the spatial grid cell that covers (x, y).
    /// </summary>
    public int GetEntryCountAt(float x, float y)
    {
        var cx = (int)(x * InvCellSize);
        var cy = (int)(y * InvCellSize);
        lock (_gridLock)
        {
            if (_grid.TryGetValue((cx, cy), out var list))
                return list.Count;
            return 0;
        }
    }

    /// <summary>
    /// Diagnostic: builds a single-line summary of the AABB envelope of all entries covering (x, y).
    /// </summary>
    public string DescribeEntriesAt(float x, float y, int maxEntries = 5)
    {
        var cx = (int)(x * InvCellSize);
        var cy = (int)(y * InvCellSize);
        lock (_gridLock)
        {
            if (!_grid.TryGetValue((cx, cy), out var list) || list.Count == 0)
                return string.Empty;

            var envMin = list[0].WorldAabb.Min;
            var envMax = list[0].WorldAabb.Max;
            var xyHitCount = 0;
            var xyHitZMin = float.PositiveInfinity;
            var xyHitZMax = float.NegativeInfinity;
            lock (list)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var aabb = list[i].WorldAabb;
                    envMin = JVector.Min(envMin, aabb.Min);
                    envMax = JVector.Max(envMax, aabb.Max);
                    if (x >= aabb.Min.X && x <= aabb.Max.X && y >= aabb.Min.Z && y <= aabb.Max.Z)
                    {
                        xyHitCount++;
                        if (aabb.Min.Y < xyHitZMin) xyHitZMin = aabb.Min.Y;
                        if (aabb.Max.Y > xyHitZMax) xyHitZMax = aabb.Max.Y;
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("envelope X[").Append(envMin.X.ToString("F1")).Append(",").Append(envMax.X.ToString("F1"))
              .Append("] Y[").Append(envMin.Z.ToString("F1")).Append(",").Append(envMax.Z.ToString("F1"))
              .Append("] Z[").Append(envMin.Y.ToString("F1")).Append(",").Append(envMax.Y.ToString("F1")).Append("]");

            sb.Append(" XYcover=").Append(xyHitCount);
            if (xyHitCount > 0)
                sb.Append(" XYcoverZ[").Append(xyHitZMin.ToString("F1")).Append(",").Append(xyHitZMax.ToString("F1")).Append("]");

            lock (list)
            {
                sb.Append(" sample=[");
                var printed = 0;
                for (var i = 0; i < list.Count && printed < maxEntries; i++)
                {
                    var aabb = list[i].WorldAabb;
                    if (!(x >= aabb.Min.X && x <= aabb.Max.X && y >= aabb.Min.Z && y <= aabb.Max.Z))
                        continue;
                    if (printed > 0) sb.Append(";");
                    sb.Append("*X(").Append(aabb.Min.X.ToString("F0")).Append("-").Append(aabb.Max.X.ToString("F0")).Append(")");
                    sb.Append("Y(").Append(aabb.Min.Z.ToString("F0")).Append("-").Append(aabb.Max.Z.ToString("F0")).Append(")");
                    sb.Append("Z(").Append(aabb.Min.Y.ToString("F0")).Append("-").Append(aabb.Max.Y.ToString("F0")).Append(")");
                    printed++;
                }
                for (var i = 0; i < list.Count && printed < maxEntries; i++)
                {
                    var aabb = list[i].WorldAabb;
                    if (x >= aabb.Min.X && x <= aabb.Max.X && y >= aabb.Min.Z && y <= aabb.Max.Z)
                        continue;
                    if (printed > 0) sb.Append(";");
                    sb.Append("X(").Append(aabb.Min.X.ToString("F0")).Append("-").Append(aabb.Max.X.ToString("F0")).Append(")");
                    sb.Append("Y(").Append(aabb.Min.Z.ToString("F0")).Append("-").Append(aabb.Max.Z.ToString("F0")).Append(")");
                    sb.Append("Z(").Append(aabb.Min.Y.ToString("F0")).Append("-").Append(aabb.Max.Y.ToString("F0")).Append(")");
                    printed++;
                }
                sb.Append("]");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Registers a brush object into the spatial index.
    /// The world AABB is derived from StartPos/EndPos of the brush data.
    /// </summary>
    public void AddBrush(WorldCell cell, ObjectDataType1Brush brush, string modelPath, string materialPath)
    {
        if (brush == null)
            return;

        var cellOffset = cell.GetCellWorldOffset().ToJVector();

        var m3X3 = new JMatrix(
            brush.Matrix3X4.M11, brush.Matrix3X4.M31, brush.Matrix3X4.M21,
            brush.Matrix3X4.M13, brush.Matrix3X4.M33, brush.Matrix3X4.M23,
            brush.Matrix3X4.M12, brush.Matrix3X4.M32, brush.Matrix3X4.M22);

        var translation = cellOffset + new JVector(
            brush.Matrix3X4.M14,
            brush.Matrix3X4.M34,
            brush.Matrix3X4.M24);

        // Derive local bounds from StartPos/EndPos brush extents (Jitter Y↔Z swap applied).
        var localMin = new JVector(brush.StartPos.X, brush.StartPos.Z, brush.StartPos.Y);
        var localMax = new JVector(brush.EndPos.X, brush.EndPos.Z, brush.EndPos.Y);

        // Ensure min <= max after coordinate swap.
        var swappedMin = JVector.Min(localMin, localMax);
        var swappedMax = JVector.Max(localMin, localMax);

        // Transform 8 corners of local AABB to world space.
        var corners = new JVector[8];
        corners[0] = JVector.Transform(new JVector(swappedMin.X, swappedMin.Y, swappedMin.Z), m3X3) + translation;
        corners[1] = JVector.Transform(new JVector(swappedMin.X, swappedMin.Y, swappedMax.Z), m3X3) + translation;
        corners[2] = JVector.Transform(new JVector(swappedMin.X, swappedMax.Y, swappedMin.Z), m3X3) + translation;
        corners[3] = JVector.Transform(new JVector(swappedMin.X, swappedMax.Y, swappedMax.Z), m3X3) + translation;
        corners[4] = JVector.Transform(new JVector(swappedMax.X, swappedMin.Y, swappedMin.Z), m3X3) + translation;
        corners[5] = JVector.Transform(new JVector(swappedMax.X, swappedMin.Y, swappedMax.Z), m3X3) + translation;
        corners[6] = JVector.Transform(new JVector(swappedMax.X, swappedMax.Y, swappedMin.Z), m3X3) + translation;
        corners[7] = JVector.Transform(new JVector(swappedMax.X, swappedMax.Y, swappedMax.Z), m3X3) + translation;

        var worldMin = corners[0];
        var worldMax = corners[0];
        for (var i = 1; i < 8; i++)
        {
            worldMin = JVector.Min(worldMin, corners[i]);
            worldMax = JVector.Max(worldMax, corners[i]);
        }

        var worldAabb = new JBoundingBox(worldMin, worldMax);
        var minCellX = (int)(worldMin.X * InvCellSize);
        var maxCellX = (int)(worldMax.X * InvCellSize);
        var minCellY = (int)(worldMin.Z * InvCellSize);
        var maxCellY = (int)(worldMax.Z * InvCellSize);

        var entry = new StaticBrushEntry
        {
            Brush = brush,
            ModelPath = modelPath ?? string.Empty,
            MaterialPath = materialPath ?? string.Empty,
            WorldAabb = worldAabb,
            CellOffset = cellOffset,
            SourceCellX = cell.CellX,
            SourceCellY = cell.CellY,
            MinGridCellX = minCellX,
            MaxGridCellX = maxCellX,
            MinGridCellY = minCellY,
            MaxGridCellY = maxCellY
        };

        lock (_gridLock)
        {
            var sourceKey = (cell.CellX, cell.CellY);
            if (!_entriesBySourceCell.TryGetValue(sourceKey, out var sourceEntries))
            {
                sourceEntries = [];
                _entriesBySourceCell[sourceKey] = sourceEntries;
            }
            sourceEntries.Add(entry);

            for (var cy = minCellY; cy <= maxCellY; cy++)
            {
                for (var cx = minCellX; cx <= maxCellX; cx++)
                {
                    var key = (cx, cy);
                    if (!_grid.TryGetValue(key, out var list))
                    {
                        list = [];
                        _grid[key] = list;
                    }

                    lock (list)
                    {
                        list.Add(entry);
                    }
                }
            }
            TotalBrushCount++;
        }
    }

    /// <summary>
    /// Registers a voxel terrain object into the spatial index.
    /// Triangles are built lazily from VoxelMeshReader on first raycast.
    /// </summary>
    public void AddVoxel(WorldCell cell, ObjectDataType6Voxel voxel)
    {
        if (voxel?.MeshReader == null) return;
        var verts = voxel.MeshReader.Vertices;
        if (verts.Count == 0) return;

        var cellOffset = cell.GetCellWorldOffset().ToJVector();

        var m3X3 = new JMatrix(
            voxel.Matrix3X4.M11, voxel.Matrix3X4.M31, voxel.Matrix3X4.M21,
            voxel.Matrix3X4.M13, voxel.Matrix3X4.M33, voxel.Matrix3X4.M23,
            voxel.Matrix3X4.M12, voxel.Matrix3X4.M32, voxel.Matrix3X4.M22);

        var translation = cellOffset + new JVector(
            voxel.Matrix3X4.M14,
            voxel.Matrix3X4.M34,
            voxel.Matrix3X4.M24);

        var worldMin = new JVector(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var worldMax = new JVector(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var anyVert = false;
        for (var i = 0; i < verts.Count; i++)
        {
            var local = new JVector(verts[i].X, verts[i].Z, verts[i].Y);
            var world = JVector.Transform(local, m3X3) + translation;
            worldMin = JVector.Min(worldMin, world);
            worldMax = JVector.Max(worldMax, world);
            anyVert = true;
        }

        if (!anyVert) return;

        var worldAabb = new JBoundingBox(worldMin, worldMax);
        var entry = new StaticVoxelEntry
        {
            Voxel = voxel,
            M3X3 = m3X3,
            Translation = translation,
            WorldAabb = worldAabb
        };

        var minCellX = (int)(worldMin.X * InvCellSize);
        var maxCellX = (int)(worldMax.X * InvCellSize);
        var minCellY = (int)(worldMin.Z * InvCellSize);
        var maxCellY = (int)(worldMax.Z * InvCellSize);

        lock (_gridLock)
        {
            for (var cy = minCellY; cy <= maxCellY; cy++)
            {
                for (var cx = minCellX; cx <= maxCellX; cx++)
                {
                    var key = (cx, cy);
                    if (!_grid.TryGetValue(key, out var list))
                    {
                        list = [];
                        _grid[key] = list;
                    }

                    lock (list)
                    {
                        list.Add(entry);
                    }
                }
            }
            TotalVoxelCount++;
        }
    }

    /// <summary>
    /// Removes all indexed brush entries that originated from the given world cell.
    /// </summary>
    public void RemoveCell(WorldCell cell)
    {
        if (cell == null)
            return;

        lock (_gridLock)
        {
            var sourceKey = (cell.CellX, cell.CellY);
            if (!_entriesBySourceCell.TryGetValue(sourceKey, out var sourceEntries) || sourceEntries.Count == 0)
                return;

            _entriesBySourceCell.Remove(sourceKey);

            for (var entryIndex = 0; entryIndex < sourceEntries.Count; entryIndex++)
            {
                var entry = sourceEntries[entryIndex];
                for (var cy = entry.MinGridCellY; cy <= entry.MaxGridCellY; cy++)
                {
                    for (var cx = entry.MinGridCellX; cx <= entry.MaxGridCellX; cx++)
                    {
                        var key = (cx, cy);
                        if (!_grid.TryGetValue(key, out var list))
                            continue;

                        lock (list)
                        {
                            for (var i = list.Count - 1; i >= 0; i--)
                            {
                                if (ReferenceEquals(list[i], entry))
                                    list.RemoveAt(i);
                            }

                            if (list.Count == 0)
                                _grid.Remove(key);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Casts a vertical ray downward from (x, y, zStart) against all indexed geometry.
    /// Returns the highest intersection Z that is &lt;= zStart.
    /// </summary>
    public bool TryRaycastDown(float x, float y, float zStart, out float hitZ)
    {
        hitZ = 0f;
        var origin = new JVector(x, zStart, y);
        var direction = -JVector.UnitY;

        var cx = (int)(x * InvCellSize);
        var cy = (int)(y * InvCellSize);

        List<IStaticGeometryEntry> entries = null;
        var entriesLockTaken = false;

        lock (_gridLock)
        {
            if (!_grid.TryGetValue((cx, cy), out entries) || entries.Count == 0)
                return false;

            System.Threading.Monitor.Enter(entries);
            entriesLockTaken = true;
        }

        var closestLambda = float.PositiveInfinity;
        var found = false;
        var traceTimer = Logger.IsDebugEnabled ? Stopwatch.StartNew() : null;
        var meshesBuilt = 0;
        var trianglesTested = 0;

        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (x < entry.WorldAabb.Min.X || x > entry.WorldAabb.Max.X ||
                    y < entry.WorldAabb.Min.Z || y > entry.WorldAabb.Max.Z)
                {
                    continue;
                }

                if (zStart < entry.WorldAabb.Min.Y)
                    continue;

                var triangles = entry.GetTransformedTriangles();
                if (triangles.Count == 0)
                    continue;

                meshesBuilt++;

                foreach (var tri in triangles)
                {
                    trianglesTested++;
                    if (RayIntersectsTriangle(origin, direction, tri.V0, tri.V1, tri.V2, out var lambda))
                    {
                        if (lambda < closestLambda)
                        {
                            closestLambda = lambda;
                            hitZ = origin.Y - lambda;
                            found = true;
                        }
                    }
                }
            }
        }
        finally
        {
            if (entriesLockTaken)
                System.Threading.Monitor.Exit(entries);
        }

        traceTimer?.Stop();
        if (traceTimer != null && traceTimer.ElapsedMilliseconds > 10)
        {
            Logger.Debug(
                "WorldStaticGeometryIndex.TryRaycastDown slow: {0} ms, pos=({1},{2},{3}), entries={4}, meshes={5}, triangles={6}, found={7}, hitZ={8}",
                traceTimer.ElapsedMilliseconds, x, y, zStart, entries.Count, meshesBuilt, trianglesTested, found, hitZ);
        }

        return found;
    }

    /// <summary>
    /// Casts a vertical ray downward from (x, y, zStart) and collects distinct surface hits
    /// ordered from highest to lowest. Used for layered floors in caves, bridges and buildings.
    /// </summary>
    public bool TryCollectRaycastDownHits(float x, float y, float zStart, List<float> hitHeights, int maxHits = 8)
    {
        ArgumentNullException.ThrowIfNull(hitHeights);

        hitHeights.Clear();
        var origin = new JVector(x, zStart, y);
        var direction = -JVector.UnitY;

        var cx = (int)(x * InvCellSize);
        var cy = (int)(y * InvCellSize);

        List<IStaticGeometryEntry> entries = null;
        var entriesLockTaken = false;

        lock (_gridLock)
        {
            if (!_grid.TryGetValue((cx, cy), out entries) || entries.Count == 0)
                return false;

            System.Threading.Monitor.Enter(entries);
            entriesLockTaken = true;
        }

        var traceTimer = Logger.IsDebugEnabled ? Stopwatch.StartNew() : null;
        var meshesBuilt = 0;
        var trianglesTested = 0;

        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                if (x < entry.WorldAabb.Min.X || x > entry.WorldAabb.Max.X ||
                    y < entry.WorldAabb.Min.Z || y > entry.WorldAabb.Max.Z)
                {
                    continue;
                }

                if (zStart < entry.WorldAabb.Min.Y)
                    continue;

                var triangles = entry.GetTransformedTriangles();
                if (triangles.Count == 0)
                    continue;

                meshesBuilt++;

                foreach (var tri in triangles)
                {
                    trianglesTested++;
                    if (!RayIntersectsTriangle(origin, direction, tri.V0, tri.V1, tri.V2, out var lambda))
                        continue;

                    var hitHeight = origin.Y - lambda;
                    if (hitHeight > zStart + 0.001f)
                        continue;

                    hitHeights.Add(hitHeight);
                }
            }
        }
        finally
        {
            if (entriesLockTaken)
                System.Threading.Monitor.Exit(entries);
        }

        if (hitHeights.Count == 0)
        {
            traceTimer?.Stop();
            return false;
        }

        hitHeights.Sort(static (left, right) => right.CompareTo(left));

        const float HeightDedupEpsilon = 0.05f;
        var writeIndex = 0;
        for (var readIndex = 0; readIndex < hitHeights.Count; readIndex++)
        {
            var hit = hitHeights[readIndex];
            if (writeIndex > 0 && MathF.Abs(hitHeights[writeIndex - 1] - hit) <= HeightDedupEpsilon)
                continue;

            hitHeights[writeIndex] = hit;
            writeIndex++;
            if (writeIndex >= maxHits)
                break;
        }

        if (writeIndex < hitHeights.Count)
            hitHeights.RemoveRange(writeIndex, hitHeights.Count - writeIndex);

        traceTimer?.Stop();
        if (traceTimer != null && traceTimer.ElapsedMilliseconds > 10)
        {
            Logger.Debug(
                "WorldStaticGeometryIndex.TryCollectRaycastDownHits slow: {0} ms, pos=({1},{2},{3}), entries={4}, meshes={5}, triangles={6}, hits={7}",
                traceTimer.ElapsedMilliseconds, x, y, zStart, entries.Count, meshesBuilt, trianglesTested, hitHeights.Count);
        }

        return true;
    }

    /// <summary>
    /// Checks whether a horizontal movement segment intersects wall-like triangles from static geometry.
    /// Used as a narrow fallback for NPC movement validation.
    /// </summary>
    public bool IsSegmentWallBlocked(Vector3 start, Vector3 end, float verticalTolerance)
    {
        const float ProbeLift = 0.35f;
        const float HorizontalMargin = 1.25f;

        var traceStart = start with { Z = start.Z + ProbeLift };
        var traceEnd = end with { Z = end.Z + ProbeLift };

        var traceOrigin = traceStart.ToJVector();
        var traceDelta = traceEnd.ToJVector() - traceOrigin;
        var maxDistance = traceDelta.Length();
        if (maxDistance <= 0.05f)
            return false;

        var traceDirection = JVector.Normalize(traceDelta);
        var minAllowedHeight = MathF.Min(traceStart.Z, traceEnd.Z) - verticalTolerance;
        var maxAllowedHeight = MathF.Max(traceStart.Z, traceEnd.Z) + verticalTolerance;

        var roughArea = new JBoundingBox(
            new JVector(
                MathF.Min(traceStart.X, traceEnd.X) - HorizontalMargin,
                minAllowedHeight,
                MathF.Min(traceStart.Y, traceEnd.Y) - HorizontalMargin),
            new JVector(
                MathF.Max(traceStart.X, traceEnd.X) + HorizontalMargin,
                maxAllowedHeight,
                MathF.Max(traceStart.Y, traceEnd.Y) + HorizontalMargin));

        var nearbyEntries = new HashSet<IStaticGeometryEntry>();

        lock (_gridLock)
        {
            var minCellX = (int)(roughArea.Min.X * InvCellSize);
            var maxCellX = (int)(roughArea.Max.X * InvCellSize);
            var minCellY = (int)(roughArea.Min.Z * InvCellSize);
            var maxCellY = (int)(roughArea.Max.Z * InvCellSize);

            for (var cy = minCellY; cy <= maxCellY; cy++)
            {
                for (var cx = minCellX; cx <= maxCellX; cx++)
                {
                    if (!_grid.TryGetValue((cx, cy), out var list) || list.Count == 0)
                        continue;

                    lock (list)
                    {
                        for (var i = 0; i < list.Count; i++)
                        {
                            nearbyEntries.Add(list[i]);
                        }
                    }
                }
            }
        }

        foreach (var entry in nearbyEntries)
        {
            var worldAabb = entry.WorldAabb;
            if (!JBoundingBoxIntersects2D(roughArea, worldAabb))
                continue;

            if (worldAabb.Max.Y < minAllowedHeight || worldAabb.Min.Y > maxAllowedHeight)
                continue;

            var triangles = entry.GetTransformedTriangles();
            if (triangles.Count == 0)
                continue;

            for (var i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                if (!TriangleRoughAreaOverlaps(roughArea, tri))
                    continue;

                if (!RayIntersectsTriangle(traceOrigin, traceDirection, tri.V0, tri.V1, tri.V2, out var lambda))
                    continue;

                if (lambda <= 0.05f || lambda > maxDistance)
                    continue;

                var edge1 = tri.V1 - tri.V0;
                var edge2 = tri.V2 - tri.V0;
                JVector.Cross(edge1, edge2, out var normal);
                var normalLengthSquared = normal.LengthSquared();
                if (normalLengthSquared <= 1e-6f)
                    continue;

                normal = JVector.Normalize(normal);
                if (!IsWallLikeSurface(normal))
                    continue;

                var hitPos = traceOrigin + lambda * traceDirection;
                if (hitPos.Y < minAllowedHeight || hitPos.Y > maxAllowedHeight)
                    continue;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moller–Trumbore ray-triangle intersection using Jitter vectors.
    /// </summary>
    private static bool RayIntersectsTriangle(JVector origin, JVector direction, JVector v0, JVector v1, JVector v2, out float t)
    {
        t = 0f;
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        JVector.Cross(direction, edge2, out var h);
        var a = JVector.Dot(edge1, h);
        if (a > -1e-6f && a < 1e-6f)
            return false;
        var f = 1.0f / a;
        var s = origin - v0;
        var u = f * JVector.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return false;
        JVector.Cross(s, edge1, out var q);
        var v = f * JVector.Dot(direction, q);
        if (v < 0.0f || u + v > 1.0f)
            return false;
        t = f * JVector.Dot(edge2, q);
        return t > 1e-6f;
    }

    private static bool IsWallLikeSurface(in JVector normal)
    {
        return MathF.Abs(normal.Y) < 0.55f;
    }

    private static bool JBoundingBoxIntersects2D(in JBoundingBox a, in JBoundingBox b)
    {
        return !(a.Max.X < b.Min.X ||
                 a.Min.X > b.Max.X ||
                 a.Max.Z < b.Min.Z ||
                 a.Min.Z > b.Max.Z);
    }

    private static bool TriangleRoughAreaOverlaps(in JBoundingBox roughArea, in JTriangle tri)
    {
        var triMinX = MathF.Min(tri.V0.X, MathF.Min(tri.V1.X, tri.V2.X));
        var triMaxX = MathF.Max(tri.V0.X, MathF.Max(tri.V1.X, tri.V2.X));
        if (triMaxX < roughArea.Min.X || triMinX > roughArea.Max.X)
            return false;

        var triMinZ = MathF.Min(tri.V0.Z, MathF.Min(tri.V1.Z, tri.V2.Z));
        var triMaxZ = MathF.Max(tri.V0.Z, MathF.Max(tri.V1.Z, tri.V2.Z));
        if (triMaxZ < roughArea.Min.Z || triMinZ > roughArea.Max.Z)
            return false;

        var triMinY = MathF.Min(tri.V0.Y, MathF.Min(tri.V1.Y, tri.V2.Y));
        var triMaxY = MathF.Max(tri.V0.Y, MathF.Max(tri.V1.Y, tri.V2.Y));
        return !(triMaxY < roughArea.Min.Y || triMinY > roughArea.Max.Y);
    }

    /// <summary>
    /// Clears all entries. Called on world shutdown or cell unload if needed.
    /// </summary>
    public void Clear()
    {
        lock (_gridLock)
        {
            _grid.Clear();
            _entriesBySourceCell.Clear();
        }
    }
}

/// <summary>
/// Service layer providing layered-height and distinct-floor queries on top of
/// <see cref="WorldStaticGeometryIndex"/>.
/// This is the authoritative layered-height path for server NPC placement.
/// Adapted from source 3.5.0.3: WorldInstance replaced with World (same type alias in target).
/// </summary>
public static class StaticGeoDataService
{
    private const float ProbeStartOffsetUp = 3.0f;
    private const float DistinctLayerDeltaThreshold = 0.5f;
    private const float SubterraneanLayerDeltaThreshold = 1.25f;
    private const int MaxCollectedHits = 8;

    private static readonly Vector2[] s_sampleOffsets =
    [
        new Vector2(0f, 0f),
        new Vector2(0.5f, 0f),
        new Vector2(-0.5f, 0f),
        new Vector2(0f, 0.5f),
        new Vector2(0f, -0.5f),
        new Vector2(1f, 0f),
        new Vector2(-1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(0f, -1f),
        new Vector2(0.70710677f, 0.70710677f),
        new Vector2(-0.70710677f, 0.70710677f),
        new Vector2(0.70710677f, -0.70710677f),
        new Vector2(-0.70710677f, -0.70710677f)
    ];

    [System.ThreadStatic]
    private static List<float> s_heightHits;

    /// <summary>
    /// Resolves the best distinct-layer height (interior floor, cave, elevated platform)
    /// at a given world position by downward raycasting through static geometry.
    /// </summary>
    public static bool TryResolveDistinctLayerHeight(
        World world,
        Vector3 pos,
        float maxDistanceAllowed,
        float preferredHeight,
        float preferredHeightTolerance,
        out float height)
    {
        height = 0f;
        if (!TryGetClosestDistinctLayerPoint(world, pos, maxDistanceAllowed, preferredHeight, preferredHeightTolerance, out var closestPoint))
            return false;

        height = closestPoint.Z;
        return true;
    }

    /// <summary>
    /// Finds the closest point on a distinct geometry layer (not terrain surface)
    /// near the given position, within <paramref name="maxDistanceAllowed"/> metres.
    /// </summary>
    public static bool TryGetClosestDistinctLayerPoint(
        World world,
        Vector3 pos,
        float maxDistanceAllowed,
        float? preferredHeight,
        float preferredHeightTolerance,
        out Vector3 closestPoint)
    {
        closestPoint = Vector3.Zero;
        if (world?.Physics?.StaticGeometryIndex == null)
            return false;

        var radius = maxDistanceAllowed;
        if (float.IsNaN(radius) || radius < 0f)
            radius = 0f;

        var usePreferredHeight = preferredHeight.HasValue && preferredHeightTolerance > 0f;
        var bestZDistance = float.PositiveInfinity;
        var bestDistanceSq = float.PositiveInfinity;
        var found = false;
        var hitHeights = GetScratchHeights();

        for (var i = 0; i < s_sampleOffsets.Length; i++)
        {
            var offset = s_sampleOffsets[i];
            if (i > 0 && radius <= 0f)
                break;

            var sampleX = pos.X + offset.X * radius;
            var sampleY = pos.Y + offset.Y * radius;

            var referenceHeight = preferredHeight ?? pos.Z;
            if (!TryCollectLayerHeights(world, sampleX, sampleY, referenceHeight, hitHeights))
                continue;

            var terrainHeight = world.GetHeight(sampleX, sampleY);
            var dx = sampleX - pos.X;
            var dy = sampleY - pos.Y;
            var distanceSq = dx * dx + dy * dy;
            if (distanceSq > radius * radius + 0.001f)
                continue;

            for (var hitIndex = 0; hitIndex < hitHeights.Count; hitIndex++)
            {
                var hitHeight = hitHeights[hitIndex];
                if (!IsDistinctFromTerrain(hitHeight, terrainHeight))
                    continue;

                var zDistance = usePreferredHeight ? MathF.Abs(hitHeight - preferredHeight.Value) : MathF.Abs(hitHeight - pos.Z);
                if (usePreferredHeight && zDistance > preferredHeightTolerance)
                    continue;

                if (!found ||
                    zDistance < bestZDistance ||
                    (MathF.Abs(zDistance - bestZDistance) <= 0.001f && distanceSq < bestDistanceSq))
                {
                    closestPoint = new Vector3(sampleX, sampleY, hitHeight);
                    bestZDistance = zDistance;
                    bestDistanceSq = distanceSq;
                    found = true;
                }
            }
        }

        return found;
    }

    private static List<float> GetScratchHeights()
    {
        s_heightHits ??= new List<float>(MaxCollectedHits);
        s_heightHits.Clear();
        return s_heightHits;
    }

    private static bool TryCollectLayerHeights(World world, float x, float y, float referenceHeight, List<float> hitHeights)
    {
        if (world?.Physics?.StaticGeometryIndex == null)
            return false;

        var terrainHeight = world.GetHeight(x, y);
        var probeStart = MathF.Max(referenceHeight, terrainHeight) + ProbeStartOffsetUp;
        return world.Physics.StaticGeometryIndex.TryCollectRaycastDownHits(x, y, probeStart, hitHeights, MaxCollectedHits);
    }

    private static bool IsDistinctFromTerrain(float candidateHeight, float terrainHeight)
    {
        var elevatedDelta = candidateHeight - terrainHeight;
        if (elevatedDelta >= DistinctLayerDeltaThreshold)
            return true;

        var subterraneanDelta = terrainHeight - candidateHeight;
        return subterraneanDelta >= SubterraneanLayerDeltaThreshold;
    }
}
