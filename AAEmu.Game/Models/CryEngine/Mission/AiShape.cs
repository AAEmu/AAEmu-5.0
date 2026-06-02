using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AAEmu.Game.Models.CryEngine.Mission;

public class AiShape(uint zoneId) : Mission(zoneId)
{
    public List<Vector3> Points { get; set; } = [];

    // Cached AABB for fast segment rejection in LinePassesThroughForbiddenArea.
    private float _minX, _maxX, _minY, _maxY;
    private bool _bboxCached;
    private readonly object _bboxLock = new();

    private void EnsureBoundingBox()
    {
        if (_bboxCached) return;
        lock (_bboxLock)
        {
            if (_bboxCached) return;
            if (Points == null || Points.Count == 0)
            {
                _minX = _maxX = _minY = _maxY = 0f;
                _bboxCached = true;
                return;
            }
            var minX = Points[0].X; var maxX = minX;
            var minY = Points[0].Y; var maxY = minY;
            for (var i = 1; i < Points.Count; i++)
            {
                var p = Points[i];
                if (p.X < minX) minX = p.X; else if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; else if (p.Y > maxY) maxY = p.Y;
            }
            _minX = minX; _maxX = maxX; _minY = minY; _maxY = maxY;
            _bboxCached = true;
        }
    }

    /// <summary>
    /// Cheap AABB intersection test with a 2D segment (X/Y).
    /// Used as a pre-filter before the expensive per-edge intersection check.
    /// </summary>
    public bool BBoxIntersectsSegment2D(Vector3 a, Vector3 b)
    {
        EnsureBoundingBox();
        if (Points == null || Points.Count == 0) return false;
        var segMinX = MathF.Min(a.X, b.X);
        var segMaxX = MathF.Max(a.X, b.X);
        if (segMaxX < _minX || segMinX > _maxX) return false;
        var segMinY = MathF.Min(a.Y, b.Y);
        var segMaxY = MathF.Max(a.Y, b.Y);
        if (segMaxY < _minY || segMinY > _maxY) return false;
        return true;
    }

    public override bool Equals(Mission other)
    {
        if (this == other)
            return true;

        if (other is not AiShape aiShape)
            return false;

        return ZoneId.Equals(aiShape.ZoneId) &&
               Name.Equals(aiShape.Name) &&
               Points.SequenceEqual(aiShape.Points);
    }

    //
}
