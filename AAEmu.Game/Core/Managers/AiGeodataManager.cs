using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmu.Game.Models.CryEngine.Readers;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models;

using NLog;

using Point = AAEmu.Game.Models.Game.AI.AStar.Point;

namespace AAEmu.Game.Core.Managers;

// GeoData AiNavigation
public class AiGeoDataManager : Singleton<AiGeoDataManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<byte, Dictionary<uint, List<AiNavigation>>> _aiNavigation;
    private Dictionary<byte, Dictionary<uint, string>> _areasMission;
    private Dictionary<byte, Dictionary<uint, List<Point>>> _forbiddenArea;
    private Dictionary<byte, Dictionary<uint, List<Point>>> _aiPath;
    private Dictionary<byte, Dictionary<uint, List<Point>>> _aiNavigationModifier;

    public List<AiNavigation> GetAvailablePoints(uint zoneKey, uint point)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

        var ret = new List<AiNavigation>();
        _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
        aiNavigation?.TryGetValue(point, out ret);

        return ret;
    }

    #region A point in a polygon

    public bool CheckImpossibleWalk(uint zoneKey, Point point)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

        var res = new List<bool>();
        _forbiddenArea.TryGetValue((byte)worldId, out var forbiddenArea);

        if (forbiddenArea != null && forbiddenArea.Count <= 1)
        {
            return false; // consider that we are inside the zone (i.e. limitation outside)
        }

        if (forbiddenArea != null)
        {
            foreach (var fa in forbiddenArea.Values)
            {
                res.Add(IsInPolygon(point, fa));
            }
        }

        return res.Any(b => b);
    }

    private static bool IsInPolygon(Point point, List<Point> polygon)
    {
        var result = false;
        var a = polygon.Last();
        foreach (var b in polygon)
        {
            if (b.X.Equals(point.X) && b.Y.Equals(point.Y))
                return true;

            if (b.Y.Equals(a.Y) && point.Y.Equals(a.Y))
            {
                if (a.X <= point.X && point.X <= b.X)
                    return true;

                if (b.X <= point.X && point.X <= a.X)
                    return true;
            }

            if (b.Y < point.Y && a.Y >= point.Y || a.Y < point.Y && b.Y >= point.Y)
            {
                if (b.X + (point.Y - b.Y) / (a.Y - b.Y) * (a.X - b.X) <= point.X)
                    result = !result;
            }
            a = b;
        }
        return result;
    }

    /// <summary>
    /// получить центр треугольника (пересечение медиан)
    /// </summary>
    public static Vector3 TriangleCenter(Point point1, Point point2, Point point3)
    {
        var x = (point1.X + point2.X + point3.X) / 3;
        var y = (point1.Y + point2.Y + point3.Y) / 3;
        var z = (point1.Z + point2.Z + point3.Z) / 3;

        return new Vector3(x, y, z);
    }

    #endregion A point in a polygon

    #region Path smoothing

    // https://www.codeproject.com/Articles/18936/A-C-Implementation-of-Douglas-Peucker-Line-Appro
    public static List<Point> DouglasPeuckerReduction(List<Point> points, double tolerance)
    {
        if (points == null || points.Count < 3)
            return points;

        var firstPointIndex = 0;
        var lastPointIndex = points.Count - 1;
        var pointIndexsToKeep = new List<int>();

        //The first and the last point cannot be the same
        while (points[firstPointIndex].Equals(points[lastPointIndex]))
        {
            lastPointIndex--;
        }

        //Add the first and last index to the keepers
        pointIndexsToKeep.Add(firstPointIndex);
        pointIndexsToKeep.Add(lastPointIndex);

        DouglasPeuckerReduction(points, firstPointIndex, lastPointIndex, tolerance, ref pointIndexsToKeep);

        var returnPoints = new List<Point>();
        pointIndexsToKeep.Sort();
        foreach (var index in pointIndexsToKeep)
        {
            returnPoints.Add(points[index]);
        }

        return returnPoints;
    }

    private static void DouglasPeuckerReduction(List<Point> points, int firstPointIndex, int lastPointIndex, double tolerance, ref List<int> pointIndexsToKeep)
    {
        double maxDistance = 0;
        var indexFarthest = 0;

        if (lastPointIndex - firstPointIndex > 1) // ADDITION: need to have more than two points in the set we are looking through
        {
            for (var index = firstPointIndex; index < lastPointIndex; index++)
            {
                var distance = PerpendicularDistance(points[firstPointIndex], points[lastPointIndex], points[index]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    indexFarthest = index;
                }
            }

            if (maxDistance > tolerance && indexFarthest != firstPointIndex) // CHANGE: condition was wrong.
            {
                //Add the largest point that exceeds the tolerance
                pointIndexsToKeep.Add(indexFarthest);

                DouglasPeuckerReduction(points, firstPointIndex, indexFarthest, tolerance, ref pointIndexsToKeep);
                DouglasPeuckerReduction(points, indexFarthest, lastPointIndex, tolerance, ref pointIndexsToKeep);
            }
        }
    }

    private static double PerpendicularDistance(Point point1, Point point2, Point point)
    {
        //Area = |(1/2)(x1y2 + x2y3 + x3y1 - x2y1 - x3y2 - x1y3)|   *Area of triangle
        //Base = v((x1-x2)²+(x1-x2)²)                               *Base of Triangle*
        //Area = .5*Base*H                                          *Solve for height
        //Height = Area/.5/Base

        var area = Math.Abs(.5 * (point1.X * point2.Y + point2.X * point.Y + point.X * point1.Y - point2.X * point1.Y - point.X * point2.Y - point1.X * point.Y));
        var bottom = Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2));
        var height = area / bottom * 2;

        return height;
    }

    #endregion Path smoothing

    #region Finding the closest point

    public (uint, Point) FindСlosestToTheCurrent2(uint zoneKey, Vector3 pos)
    {
        var index = 0u;
        var point = new Point();

        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

        _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
        if (aiNavigation != null)
        {
            foreach (var closest in aiNavigation.Values.Select(lpf => lpf
                         .OrderBy(x => DistanceBetweenPoints(pos, x.Position))
                         .First()))
            {
                index = closest.StartPoint;
                point = closest.Position;
            }
        }

        Logger.Warn($"# Found near position index: {index}...");
        return (index, point);
    }

    public (uint, Point) FindСlosestToTheCurrent(uint zoneKey, Vector3 pos)
    {
        var posX = pos.X;
        var posY = pos.Y;

        var index = 0u;
        var point = new Point();
        var minDist = 99999.0f;
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

        _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
        if (aiNavigation != null)
        {
            foreach (var lpf in aiNavigation.Values)
            {
                foreach (var pf in lpf)
                {
                    var dx = posX - pf.Position.X;
                    var dy = posY - pf.Position.Y;

                    var distance = dx * dx + dy * dy;
                    if (!(distance < minDist)) { continue; }

                    index = pf.StartPoint;
                    point = pf.Position;
                    minDist = distance;
                }
            }
        }

        // Logger.Warn($"# Found near position index: {index}...");
        return (index, point);
    }

    public float GetHeight(uint zoneKey, Vector3 pos)
    {
        // A/B test toggle: if explicitly requested, use the original 3D-nearest behaviour.
        var spawnCfg = AppConfiguration.Instance.World?.SpawnHeight;
        if (spawnCfg != null && spawnCfg.UseLegacyBai3DLookup)
            return GetHeightLegacy3D(zoneKey, pos);

        var lookup = GetHeightDetailed(pos);

        // GetHeightDetailed always returns the closest 2D node, even if it is far.
        var maxXy = spawnCfg?.MaxBaiDistance2D ?? 6.0f;
        var maxBaiVsHmap = spawnCfg?.MaxBaiVsHeightmapDelta ?? 3.0f;
        var unmeshedThr = spawnCfg?.UnmeshedStructureThreshold ?? 1.5f;
        var preferHmap = spawnCfg?.PreferHeightmapForGroundZ ?? true;

        // V6 height fix: use the BILINEARLY INTERPOLATED heightmap
        // Compute once and reuse to avoid redundant WorldManager lookups.
        float hmapZ;
        bool hmapValid;
        try
        {
            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
            var world = WorldManager.Instance.GetWorld(worldId);
            hmapZ = world?.Template?.GetHeight(pos.X, pos.Y) ?? 0f;
            hmapValid = !float.IsNaN(hmapZ) && !float.IsInfinity(hmapZ) && hmapZ != 0f;
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "GetHeight heightmap lookup failed for zone {0} at ({1:F2},{2:F2})", zoneKey, pos.X, pos.Y);
            hmapZ = 0f;
            hmapValid = false;
        }

        if (!lookup.HasValue)
            return hmapValid ? hmapZ : 0f;

        if (lookup.Distance2D > maxXy)
            return hmapValid ? hmapZ : 0f;

        // .bai is close enough in XY. Use the already-computed heightmap value.
        if (hmapValid)
        {
            var hmapBaiAgree = MathF.Abs(lookup.BaiZ - hmapZ) <= maxBaiVsHmap;
            var highestFloor = MathF.Max(lookup.BaiZ, hmapZ);

            // Unmeshed structure (pier, dock, low platform)
            if (hmapBaiAgree && (pos.Z - highestFloor) > unmeshedThr)
                return pos.Z;

            // V6 anti-levitation
            if (preferHmap && hmapBaiAgree)
                return hmapZ;

            // bai and heightmap disagree - trust the .bai
            return lookup.BaiZ;
        }

        // No valid heightmap - fall back to the .bai node.
        return lookup.BaiZ;
    }

    /// <summary>
    /// Legacy 3D-nearest .bai height lookup for A/B testing.
    /// </summary>
    internal float GetHeightLegacy3D(uint zoneKey, Vector3 pos)
    {
        var rrr = 0f;
        try
        {
            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

            var posX = pos.X;
            var posY = pos.Y;

            var pointN = new Point();
            var pointFa = new Point();

            var minDistN = 99999.0f;
            var minDistFa = 99999.0f;

            _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
            if (aiNavigation != null)
            {
                foreach (var lpf in aiNavigation.Values)
                {
                    foreach (var pf in lpf)
                    {
                        var dx = posX - pf.Position.X;
                        var dy = posY - pf.Position.Y;

                        var distance = dx * dx + dy * dy;
                        if (!(distance < minDistN)) { continue; }

                        pointN = pf.Position;
                        minDistN = distance;
                    }
                }
            }

            _forbiddenArea.TryGetValue((byte)worldId, out var forbiddenArea);
            if (forbiddenArea != null)
            {
                foreach (var lfa in forbiddenArea.Values)
                {
                    foreach (var pf in lfa)
                    {
                        var dx = posX - pf.X;
                        var dy = posY - pf.Y;

                        var distance = dx * dx + dy * dy;
                        if (!(distance < minDistFa)) { continue; }

                        pointFa = pf;
                        minDistFa = distance;
                    }
                }
            }

            rrr = minDistFa < minDistN ? pointFa.Z : pointN.Z;
        }
        catch
        {
            rrr = 0f;
        }

        return rrr;
    }

    public float GetHeight(uint zoneKey, WorldSpawnPosition pos)
    {
        return GetHeight(zoneKey, pos.AsPositionVector());
    }

    private static float DistanceBetweenPoints(Vector3 point, Point compareTo)
    {
        return (compareTo.X - point.X) * (compareTo.X - point.X) +
               (compareTo.Y - point.Y) * (compareTo.Y - point.Y);
    }

    private static float DistanceBetweenPoints(Point point, Point compareTo)
    {
        return (compareTo.X - point.X) * (compareTo.X - point.X) +
               (compareTo.Y - point.Y) * (compareTo.Y - point.Y);
    }

    public static Point FindClosest(List<AiNavigation> searchIn, Point compareTo)
    {
        return searchIn
            .Select(p => new { point = p.Position, distance = DistanceBetweenPoints(p.Position, compareTo) })
            .OrderBy(distances => distances.distance)
            .First().point;
    }

    public static Point FindClosest(List<Point> searchIn, Point compareTo)
    {
        return searchIn
            .Select(p => new { point = p, distance = DistanceBetweenPoints(p, compareTo) })
            .OrderBy(distances => distances.distance)
            .First().point;
    }

    /// <summary>
    /// Find the nearest point
    /// </summary>
    public static uint FindClosestIndexPoint(List<Point> searchIn, Point compareTo)
    {
        var minDistance = 0f;
        var pointN = 0u;

        for (var i = 0; i < searchIn.Count; i++)
        {
            var distance = DistanceBetweenPoints(searchIn[i], compareTo);
            if (distance > minDistance)
                continue;

            pointN = (uint)i;
            minDistance = distance;
        }

        return pointN;
    }

    public float GetHeight2(uint zoneKey, Vector3 pos)
    {
        var rrr = 0f;
        var position = new Point(pos.X, pos.Y, pos.Z);
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        try
        {
            var point = new Point();
            var res = new List<Point>();

            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

            _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
            if (aiNavigation != null)
            {
                res.AddRange(aiNavigation.Values.Select(nav => FindClosest(nav, position)));
            }

            _forbiddenArea.TryGetValue((byte)worldId, out var forbiddenArea);
            if (forbiddenArea != null)
            {
                res.AddRange(forbiddenArea.Values.Select(fa => FindClosest(fa, position)));
            }

            point = res.OrderBy(p => DistanceBetweenPoints(pos, p)).First();
            rrr = point.Z;
        }
        catch
        {
            rrr = 0f;
        }

        stopWatch.Stop();
        Logger.Info($"GetHeight2 took {stopWatch.Elapsed}");
        return rrr;
    }

    #endregion Finding the closest point

    #region SQLite

    public void Load()
    {
        Logger.Info("Loading AI GeoData...");

        _aiNavigation = new Dictionary<byte, Dictionary<uint, List<AiNavigation>>>();
        _areasMission = new Dictionary<byte, Dictionary<uint, string>>();
        _forbiddenArea = new Dictionary<byte, Dictionary<uint, List<Point>>>();
        _aiPath = new Dictionary<byte, Dictionary<uint, List<Point>>>();
        _aiNavigationModifier = new Dictionary<byte, Dictionary<uint, List<Point>>>();

        var worlds = WorldManager.Instance.GetWorlds();
        foreach (var world in worlds)
        {
            // TODO добавить в worlds => Geodata
            var aiNavigation = new Dictionary<uint, List<AiNavigation>>();
            var areasMission = new Dictionary<uint, string>();
            var forbiddenArea = new Dictionary<uint, List<Point>>();
            var aiPath = new Dictionary<uint, List<Point>>();
            var aiNavigationModifier = new Dictionary<uint, List<Point>>();

            var worldPath = Path.Combine("Data", "AiGeoData", world.Name);
            var worldPathToFile = Path.Combine(worldPath, "server_ai_geo_data.sqlite3");
            if (!File.Exists(worldPathToFile))
            {
                Logger.Info($"World {world.Name} is missing {Path.GetFileName(worldPathToFile)}");
            }
            else
            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                using (var connection = SQLite.CreateConnection(worldPath, "server_ai_geo_data.sqlite3"))
                {
                    Logger.Info("Loading ai_navigation...");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM ai_navigation";
                        command.Prepare();
                        using (var sqliteDataReader = command.ExecuteReader())
                        using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                        {
                            while (reader.Read())
                            {
                                var template = new AiNavigation();
                                template.Id = reader.GetUInt32("id");
                                template.ZoneKey = reader.GetUInt32("zone_key");
                                template.StartPoint = reader.GetUInt32("start_point");
                                template.EndPoint = reader.GetUInt32("end_point");
                                template.Position = new Point();
                                template.Position.X = reader.GetFloat("x");
                                template.Position.Y = reader.GetFloat("y");
                                template.Position.Z = reader.GetFloat("z");

                                // convert coordinates from local to world, immediately when reading the path from the file
                                var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                                var vec = ZoneManager.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                                template.Position.X = vec.X;
                                template.Position.Y = vec.Y;
                                template.Position.Z = vec.Z;

                                if (aiNavigation.TryGetValue(template.StartPoint, out var value))
                                {
                                    value.Add(template);
                                }
                                else
                                {
                                    aiNavigation.Add(template.StartPoint, new List<AiNavigation> { template });
                                }
                            }
                        }
                    }

                    Logger.Info("Loading areas_mission...");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM areas_mission";
                        command.Prepare();
                        using (var sqliteDataReader = command.ExecuteReader())
                        using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                        {
                            while (reader.Read())
                            {
                                var template = new AreasMission();
                                template.Id = reader.GetUInt32("id");
                                template.ZoneKey = reader.GetUInt32("zone_key");
                                template.Name = reader.GetString("name");
                                template.Type = reader.GetString("type");
                                template.PointCount = reader.GetUInt32("point_count");

                                areasMission.Add(template.Id, template.Type);
                            }
                        }
                    }

                    Logger.Info("Loading areas_mission_points...");
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM areas_mission_points";
                        command.Prepare();
                        using (var sqliteDataReader = command.ExecuteReader())
                        using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                        {
                            while (reader.Read())
                            {
                                var template = new AreasMissionPoints();
                                template.Id = reader.GetUInt32("id");
                                template.ZoneKey = reader.GetUInt32("zone_key");
                                template.Position = new Point();
                                template.Position.X = reader.GetFloat("x");
                                template.Position.Y = reader.GetFloat("y");
                                template.Position.Z = reader.GetFloat("z");

                                // convert coordinates from local to world, immediately when reading the path from the file
                                var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                                var vec = ZoneManager.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                                template.Position.X = vec.X;
                                template.Position.Y = vec.Y;
                                template.Position.Z = vec.Z;

                                var type = areasMission[template.Id];
                                switch (type)
                                {
                                    case "ForbiddenArea":
                                        if (forbiddenArea.TryGetValue(template.Id, out var value))
                                        {
                                            value.Add(template.Position);
                                        }
                                        else
                                        {
                                            forbiddenArea.Add(template.Id, new List<Point> { template.Position });
                                        }
                                        break;
                                    case "AINavigationModifier":
                                        if (aiNavigationModifier.TryGetValue(template.Id, out var value1))
                                        {
                                            value1.Add(template.Position);
                                        }
                                        else
                                        {
                                            aiNavigationModifier.Add(template.Id, new List<Point> { template.Position });
                                        }
                                        break;
                                    case "AIPath":
                                        if (aiPath.TryGetValue(template.Id, out var value2))
                                        {
                                            value2.Add(template.Position);
                                        }
                                        else
                                        {
                                            aiPath.Add(template.Id, new List<Point> { template.Position });
                                        }
                                        break;
                                }
                            }
                        }
                    }
                }
#pragma warning restore CA2000 // Dispose objects before losing scope
            }
            _aiNavigation[(byte)world.Id] = aiNavigation;
            _areasMission[(byte)world.Id] = areasMission;
            _forbiddenArea[(byte)world.Id] = forbiddenArea;
            _aiNavigationModifier[(byte)world.Id] = aiNavigationModifier;
            _aiPath[(byte)world.Id] = aiPath;
        }
    }

    #endregion

    #region Static Collision Segment Check

    /// <summary>
    /// Hybrid blocker: checks if a segment between two world positions is blocked by either
    /// a forbidden area or by runtime static world geometry (walls, buildings, caves).
    /// </summary>
    public bool IsSegmentBlocked(Vector3 start, Vector3 end, float extraRadius = 0f)
    {
        // Check static geometry via WorldManager — find a world that has collision loaded
        var worlds = WorldManager.Instance.GetWorlds();
        foreach (var world in worlds)
        {
            if (world?.Physics?.StaticGeometryIndex == null)
                continue;

            if (world.Physics.StaticGeometryIndex.GetEntryCountAt(start.X, start.Y) <= 0 &&
                world.Physics.StaticGeometryIndex.GetEntryCountAt(end.X, end.Y) <= 0)
                continue;

            if (world.IsStaticCollisionSegmentBlocked(start, end, 8f + extraRadius, out _))
                return true;

            break;
        }

        return false;
    }

    #endregion

    #region Forbidden Area and Path Reduction

    /// <summary>
    /// Checks if a point is inside any forbidden zone polygon.
    /// </summary>
    public bool CheckImpossibleWalk(Vector3 point)
    {
        return CheckImpossibleWalk(point, null, out _);
    }

    /// <summary>
    /// Checks if a point is inside a forbidden zone area, with an optional narrow override for
    /// raised indoor floors where 2D forbidden polygons overlap several vertical layers.
    /// </summary>
    public bool CheckImpossibleWalk(Vector3 point, float? raisedLayerAnchorHeight, out bool allowedByRaisedIndoorLayer)
    {
        allowedByRaisedIndoorLayer = false;

        var bai = GetBaiByPos(point);
        if (bai == null)
            return false;

        foreach (var areaMission in bai.AreasMissionReaders)
        {
            foreach (var shape in EnumerateForbiddenShapesFromReader(areaMission))
            {
                if (!IsInPolygon(point, shape.Points))
                    continue;

                if (raisedLayerAnchorHeight.HasValue)
                {
                    allowedByRaisedIndoorLayer = true;
                    break;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a line passes through any forbidden area shape near the start position.
    /// </summary>
    public bool LinePassesThroughForbiddenArea(
        Vector3 startNode,
        Vector3 endNode,
        List<string> ignoredShapeNames,
        bool ignoreCorners,
        out AAEmu.Game.Models.CryEngine.Mission.AiShape intersectionShape,
        out Vector3 intersectionPoint,
        out int intersectionLinePointStartIndex,
        float maxHeightOffset = 8f)
    {
        intersectionPoint = Vector3.Zero;
        intersectionLinePointStartIndex = -1;
        intersectionShape = null;
        var intersectionPointDistanceSq = float.PositiveInfinity;

        var bai = GetBaiByPos(startNode);
        if (bai == null)
            return false;

        foreach (var areaMission in bai.AreasMissionReaders)
        {
            foreach (var aiShape in EnumerateForbiddenShapesFromReader(areaMission))
            {
                if (ignoredShapeNames != null && ignoredShapeNames.Contains(aiShape.Name))
                    continue;
                if (!aiShape.BBoxIntersectsSegment2D(startNode, endNode))
                    continue;

                if (!LinePassesThroughAiShape(startNode, endNode, aiShape, true, maxHeightOffset, ignoreCorners, out var hitTest, out var hitIndex))
                    continue;

                var hitDistanceSq = Vector3.DistanceSquared(startNode, hitTest);
                if (intersectionLinePointStartIndex < 0 || hitDistanceSq < intersectionPointDistanceSq)
                {
                    intersectionShape = aiShape;
                    intersectionPoint = hitTest;
                    intersectionLinePointStartIndex = hitIndex;
                    intersectionPointDistanceSq = hitDistanceSq;
                }
            }
        }

        return intersectionLinePointStartIndex >= 0;
    }

    /// <summary>
    /// Finds a detour waypoint around a forbidden area obstacle.
    /// </summary>
    public bool TryFindLocalDetourWaypoint(
        Vector3 start,
        Vector3 goal,
        Vector3 obstaclePoint,
        AAEmu.Game.Models.CryEngine.Mission.AiShape obstacleShape,
        out Vector3 detourWaypoint,
        float maxDetourDistance = 20f)
    {
        detourWaypoint = Vector3.Zero;

        if (obstacleShape?.Points == null || obstacleShape.Points.Count < 3)
            return false;

        var obstacleCenter = new Vector3(
            (obstacleShape.Points[0].X + obstacleShape.Points[1].X + obstacleShape.Points[2].X) / 3,
            (obstacleShape.Points[0].Y + obstacleShape.Points[1].Y + obstacleShape.Points[2].Y) / 3,
            (obstacleShape.Points[0].Z + obstacleShape.Points[1].Z + obstacleShape.Points[2].Z) / 3);

        var directionFromObstacle = Vector3.Subtract(obstaclePoint, obstacleCenter);
        directionFromObstacle.Z = 0;

        if (directionFromObstacle.Length() < 0.1f)
            directionFromObstacle = new Vector3(1, 0, 0);

        directionFromObstacle = Vector3.Normalize(directionFromObstacle);

        var perpendicularRight = new Vector3(-directionFromObstacle.Y, directionFromObstacle.X, 0);
        var perpendicularLeft = new Vector3(directionFromObstacle.Y, -directionFromObstacle.X, 0);
        var detourDistances = new[] { 3f, 6f, 10f, 15f };

        foreach (var dist in detourDistances)
        {
            if (dist > maxDetourDistance)
                break;

            var rightWaypoint = obstaclePoint + perpendicularRight * dist;
            rightWaypoint.Z = obstaclePoint.Z;
            if (!CheckImpossibleWalk(rightWaypoint) &&
                !IsSegmentBlocked(start, rightWaypoint) &&
                !IsSegmentBlocked(rightWaypoint, goal))
            {
                detourWaypoint = rightWaypoint;
                return true;
            }

            var leftWaypoint = obstaclePoint + perpendicularLeft * dist;
            leftWaypoint.Z = obstaclePoint.Z;
            if (!CheckImpossibleWalk(leftWaypoint) &&
                !IsSegmentBlocked(start, leftWaypoint) &&
                !IsSegmentBlocked(leftWaypoint, goal))
            {
                detourWaypoint = leftWaypoint;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reduces a path by skipping intermediate nodes when direct line-of-sight is clear.
    /// Pathfinding tunable: passing <c>maxNodeSkipCount <= 0</c> disables the smoothing
    /// entirely and returns the raw path as a queue.
    /// </summary>
    public Queue<Vector3> ReducePath(List<Vector3> foundPath, int maxNodeSkipCount)
    {
        var res = new Queue<Vector3>();

        // Smoothing disabled by configuration -> raw path.
        if (maxNodeSkipCount <= 0)
        {
            foreach (var p in foundPath)
                res.Enqueue(p);
            return res;
        }

        for (var startNodeIndex = 0; startNodeIndex < foundPath.Count; startNodeIndex++)
        {
            var startNode = foundPath[startNodeIndex];
            res.Enqueue(startNode);

            for (var endNodeIndex = startNodeIndex + maxNodeSkipCount; endNodeIndex > startNodeIndex; endNodeIndex--)
            {
                if (endNodeIndex >= foundPath.Count)
                    continue;
                var endNode = foundPath[endNodeIndex];
                if (!IsSegmentBlocked(startNode, endNode))
                {
                    res.Enqueue(endNode);
                    startNodeIndex = endNodeIndex;
                    break;
                }
            }
        }
        return res;
    }

    // ---- Private helpers for ForbiddenArea methods ----

    private static Models.CryEngine.Loaders.BaseBaiLoader GetBaiByPos(Vector3 pos)
    {
        var worlds = WorldManager.Instance.GetWorlds();
        foreach (var world in worlds)
        {
            var bai = world?.Template?.GetBaiByPos(pos);
            if (bai != null)
                return bai;
        }
        return null;
    }

    private static IEnumerable<AAEmu.Game.Models.CryEngine.Mission.AiShape> EnumerateForbiddenShapesFromReader(
        AAEmu.Game.Models.CryEngine.Readers.AreasMissionReader areaMission)
    {
        if (areaMission == null)
            yield break;

        foreach (var shape in areaMission.ForbiddenAreasList)
            yield return shape;

        foreach (var shape in areaMission.ForbiddenBoundariesList)
            yield return shape;

        foreach (var shape in areaMission.DesignerForbiddenAreasList)
            yield return shape;
    }

    private static bool IsInPolygon(Vector3 point, List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return false;
        var result = false;
        var a = polygon[^1];
        foreach (var b in polygon)
        {
            if (b.X.Equals(point.X) && b.Y.Equals(point.Y))
                return true;

            if (b.Y.Equals(a.Y) && point.Y.Equals(a.Y))
            {
                if (a.X <= point.X && point.X <= b.X) return true;
                if (b.X <= point.X && point.X <= a.X) return true;
            }

            if ((b.Y < point.Y && a.Y >= point.Y) || (a.Y < point.Y && b.Y >= point.Y))
            {
                if (b.X + (point.Y - b.Y) / (a.Y - b.Y) * (a.X - b.X) <= point.X)
                    result = !result;
            }
            a = b;
        }
        return result;
    }

    private static bool LinePassesThroughAiShape(
        Vector3 startPos,
        Vector3 endPos,
        AAEmu.Game.Models.CryEngine.Mission.AiShape shape,
        bool closedLoop,
        float maxHeightOffset,
        bool ignoreCorners,
        out Vector3 intersectionPoint,
        out int intersectionPointStartIndex)
    {
        intersectionPoint = Vector3.Zero;
        intersectionPointStartIndex = -1;
        var maxIndexToStart = shape.Points.Count + (closedLoop ? 0 : -1);
        const float CornerEpsSq = 0.0001f;

        for (var index = 0; index < maxIndexToStart; index++)
        {
            var lineStart = shape.Points[index];
            var lineEnd = index < maxIndexToStart - 1 ? shape.Points[index + 1] : shape.Points[0];
            if (ignoreCorners && (
                    Vector3.DistanceSquared(startPos, lineStart) <= CornerEpsSq ||
                    Vector3.DistanceSquared(startPos, lineEnd) <= CornerEpsSq ||
                    Vector3.DistanceSquared(endPos, lineStart) <= CornerEpsSq ||
                    Vector3.DistanceSquared(endPos, lineEnd) <= CornerEpsSq))
                continue;

            var iPoint = FindLineIntersection(startPos, endPos, lineStart, lineEnd);
            if (iPoint == Vector3.Zero)
                continue;

            if (maxHeightOffset == 0f ||
                MathF.Abs(iPoint.Z - startPos.Z) <= maxHeightOffset ||
                MathF.Abs(iPoint.Z - endPos.Z) <= maxHeightOffset)
            {
                var newDistSq = Vector3.DistanceSquared(startPos, iPoint);
                if (intersectionPointStartIndex < 0 ||
                    newDistSq < Vector3.DistanceSquared(startPos, intersectionPoint))
                {
                    intersectionPoint = iPoint;
                    intersectionPointStartIndex = index;
                }
            }
        }

        return intersectionPointStartIndex >= 0;
    }

    private static Vector3 FindLineIntersection(Vector3 lineAStart, Vector3 lineAEnd, Vector3 lineBStart, Vector3 lineBEnd)
    {
        // 2D intersection (XY plane), Z from lineA.
        var d1 = lineAEnd - lineAStart;
        var d2 = lineBEnd - lineBStart;
        var cross = d1.X * d2.Y - d1.Y * d2.X;
        if (MathF.Abs(cross) < 1e-6f)
            return Vector3.Zero; // parallel

        var diff = lineBStart - lineAStart;
        var t = (diff.X * d2.Y - diff.Y * d2.X) / cross;
        var u = (diff.X * d1.Y - diff.Y * d1.X) / cross;

        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return Vector3.Zero; // outside segment bounds

        return lineAStart + d1 * t;
    }

    #endregion

    #region V6 Height Diagnostics

    /// <summary>
    /// Result of a detailed .bai height lookup.
    /// </summary>
    public readonly struct BaiHeightLookup
    {
        public bool HasValue { get; init; }
        public float BaiZ { get; init; }
        public float Distance2D { get; init; }
        public float DeltaZ { get; init; }
        public byte NodeType { get; init; }
        public string Source { get; init; }
    }

    /// <summary>
    /// XY-based nearest-node lookup that also returns context.
    /// Uses the legacy _aiNavigation data since NetMissionReader is not ported.
    /// </summary>
    public BaiHeightLookup GetHeightDetailed(Vector3 pos)
    {
        try
        {
            var bestZ = 0f;
            var bestDist2DSq = float.MaxValue;
            var bestDeltaZ = 0f;
            var bestSource = string.Empty;
            const float SameFootprintSlack = 1.5f;
            const float SameFootprintSlackSq = SameFootprintSlack * SameFootprintSlack;
            var found = false;

            // Search through all loaded AI navigation points.
            // Uses squared distance for comparisons to avoid expensive MathF.Sqrt in the hot loop.
            foreach (var kvp in _aiNavigation)
            {
                foreach (var navList in kvp.Value.Values)
                {
                    foreach (var nav in navList)
                    {
                        var dx = nav.Position.X - pos.X;
                        var dy = nav.Position.Y - pos.Y;
                        var d2Sq = dx * dx + dy * dy;
                        var dz = MathF.Abs(nav.Position.Z - pos.Z);

                        var wins = d2Sq < bestDist2DSq - 1e-3f
                                   || (d2Sq <= bestDist2DSq + SameFootprintSlackSq
                                       && bestDist2DSq <= d2Sq + SameFootprintSlackSq
                                       && dz < bestDeltaZ);

                        if (wins)
                        {
                            bestDist2DSq = d2Sq;
                            bestZ = nav.Position.Z;
                            bestDeltaZ = dz;
                            bestSource = "AiNavigation";
                            found = true;
                        }
                    }
                }
            }

            if (!found)
                return default;

            return new BaiHeightLookup
            {
                HasValue = true,
                BaiZ = bestZ,
                Distance2D = MathF.Sqrt(bestDist2DSq),
                DeltaZ = bestZ - pos.Z,
                NodeType = 0,
                Source = bestSource,
            };
        }
        catch (Exception ex)
        {
            Logger.Trace(ex, "GetHeightDetailed failed at {0}", pos);
            return default;
        }
    }

    /// <summary>
    /// Pathfinding-grade closest-node lookup (used by A* start/end node selection).
    /// Returns the position of the closest nav point, or null if none found.
    /// </summary>
    public Vector3? FindClosestPathNode2D(uint zoneKey, Vector3 pos, float maxDistance2D)
    {
        var spawnCfg = AppConfiguration.Instance.World?.SpawnHeight;
        if (spawnCfg != null && spawnCfg.UseLegacyBai3DLookup)
        {
            var legacy = FindClosestNodeLegacy(zoneKey, pos);
            return legacy;
        }

        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        Vector3 bestPos = Vector3.Zero;
        var bestD2DSq = float.MaxValue;
        var bestDeltaZ = float.MaxValue;
        var found = false;

        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav != null)
        {
            foreach (var navList in aiNav.Values)
            {
                foreach (var nav in navList)
                {
                    var dx = nav.Position.X - pos.X;
                    var dy = nav.Position.Y - pos.Y;
                    var d2DSq = dx * dx + dy * dy;
                    var dz = MathF.Abs(nav.Position.Z - pos.Z);

                    const float SameFootprintSlack = 1.5f;
                    const float SameFootprintSlackSq = SameFootprintSlack * SameFootprintSlack;
                    var wins = d2DSq < bestD2DSq - 1e-3f
                               || (d2DSq <= bestD2DSq + SameFootprintSlackSq
                                   && bestD2DSq <= d2DSq + SameFootprintSlackSq
                                   && dz < bestDeltaZ);

                    if (wins)
                    {
                        bestPos = new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
                        bestD2DSq = d2DSq;
                        bestDeltaZ = dz;
                        found = true;
                    }
                }
            }
        }

        if (!found)
            return null;

        if (maxDistance2D > 0f && bestD2DSq > maxDistance2D * maxDistance2D)
            return null;

        return bestPos;
    }

    /// <summary>
    /// Legacy 3D-nearest node lookup for A/B testing.
    /// </summary>
    private Vector3? FindClosestNodeLegacy(uint zoneKey, Vector3 pos)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        Vector3 bestPos = Vector3.Zero;
        var bestDist3D = float.MaxValue;
        var found = false;

        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav != null)
        {
            foreach (var navList in aiNav.Values)
            {
                foreach (var nav in navList)
                {
                    var dist3D = Vector3.DistanceSquared(new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z), pos);
                    if (dist3D < bestDist3D)
                    {
                        bestDist3D = dist3D;
                        bestPos = new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
                        found = true;
                    }
                }
            }
        }

        return found ? bestPos : null;
    }

    /// <summary>
    /// Bidirectional neighbour lookup (V6 pathfinding fix).
    ///
    /// Background: the .bai ai_navigation table stores each edge ONCE, with a
    /// (StartPoint, EndPoint) pair. The legacy GetAvailablePoints only returned edges
    /// where StartPoint == pointId, treating the undirected graph as directed.
    /// This made A* unable to see roughly half of the reachable neighbours.
    ///
    /// This method returns the full set of neighbours: nodes reachable via outgoing
    /// edges (StartPoint == pointId) AND nodes reachable via incoming edges
    /// (EndPoint == pointId).
    ///
    /// Since we don't have NetMissionReader's LinkDescriptor.Length, we compute
    /// the Euclidean distance as the link length.
    /// </summary>
    /// <param name="zoneKey">Zone ID to search in.</param>
    /// <param name="pointId">The node ID to find neighbours for.</param>
    /// <returns>List of (linkLength, neighbourPosition) pairs.</returns>
    public List<(float LinkLength, Vector3 NeighbourPos)> GetNeighbourNodes(uint zoneKey, uint pointId)
    {
        var result = new List<(float, Vector3)>();
        var seen = new HashSet<uint>();

        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav == null)
            return result;

        // First, find the position of the current node
        var currentPos = GetNodePosition(zoneKey, pointId);
        if (!currentPos.HasValue)
            return result;

        var currentVector = currentPos.Value;

        foreach (var navList in aiNav.Values)
        {
            foreach (var nav in navList)
            {
                var navPos = new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);

                // Outgoing edge: this node is the source (StartPoint)
                // nav.Position is the END point position
                if (nav.StartPoint == pointId && !seen.Contains(nav.EndPoint))
                {
                    seen.Add(nav.EndPoint);
                    var length = Vector3.Distance(currentVector, navPos);
                    result.Add((length, navPos));
                }
                // Incoming edge: this node is the target (EndPoint)
                // We need to find the START point position
                else if (nav.EndPoint == pointId && !seen.Contains(nav.StartPoint))
                {
                    seen.Add(nav.StartPoint);
                    // For incoming edges, the neighbour is at a different position
                    // We need to find the position of the StartPoint node
                    var startNodePos = FindNodePositionByPointId(zoneKey, nav.StartPoint, nav);
                    var length = Vector3.Distance(currentVector, startNodePos);
                    result.Add((length, startNodePos));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the position of a node by its point ID.
    /// </summary>
    private Vector3 FindNodePositionByPointId(uint zoneKey, uint pointId, AiNavigation fallbackNav)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav == null)
            return new Vector3(fallbackNav.Position.X, fallbackNav.Position.Y, fallbackNav.Position.Z);

        foreach (var navList in aiNav.Values)
        {
            foreach (var nav in navList)
            {
                if (nav.StartPoint == pointId)
                    return new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
            }
        }

        return new Vector3(fallbackNav.Position.X, fallbackNav.Position.Y, fallbackNav.Position.Z);
    }

    /// <summary>
    /// Gets the position of a node by its ID within a zone.
    /// </summary>
    private Vector3? GetNodePosition(uint zoneKey, uint pointId)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav == null)
            return null;

        foreach (var navList in aiNav.Values)
        {
            foreach (var nav in navList)
            {
                if (nav.StartPoint == pointId)
                    return new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the end point position for a given start point.
    /// </summary>
    private Vector3? GetEndPointPosition(uint zoneKey, uint startPointId)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav == null)
            return null;

        foreach (var navList in aiNav.Values)
        {
            foreach (var nav in navList)
            {
                if (nav.StartPoint == startPointId)
                    return new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets neighbours by position — finds the closest node and returns its neighbours.
    /// This is the main entry point for pathfinding.
    /// </summary>
    public List<(float LinkLength, Vector3 NeighbourPos)> GetNeighbourNodesByPosition(uint zoneKey, Vector3 pos)
    {
        // Find the closest node to this position
        var closestNode = FindClosestNodeByPosition(zoneKey, pos);
        if (closestNode == null)
            return [];

        return GetNeighbourNodes(zoneKey, closestNode.Value.nodeId);
    }

    /// <summary>
    /// Legacy wrapper for backward compatibility.
    /// </summary>
    public List<(float LinkLength, Vector3 NeighbourPos)> GetNeighbourNodes(Vector3 pos)
    {
        // Find the closest node to this position and get its neighbours
        return GetNeighbourNodesByPosition(0, pos);
    }

    /// <summary>
    /// Finds the closest node to a position and returns its ID and position.
    /// </summary>
    private (uint nodeId, Vector3 position)? FindClosestNodeByPosition(uint zoneKey, Vector3 pos)
    {
        var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
        Vector3 bestPos = Vector3.Zero;
        var bestDist = float.MaxValue;
        var bestNodeId = 0u;
        var found = false;

        _aiNavigation.TryGetValue((byte)worldId, out var aiNav);
        if (aiNav != null)
        {
            foreach (var navList in aiNav.Values)
            {
                foreach (var nav in navList)
                {
                    var dx = nav.Position.X - pos.X;
                    var dy = nav.Position.Y - pos.Y;
                    var dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestPos = new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
                        bestNodeId = nav.StartPoint;
                        found = true;
                    }
                }
            }
        }

        return found ? (bestNodeId, bestPos) : null;
    }

    #endregion
}
