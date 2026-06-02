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
    /// <param name="point1"></param>
    /// <param name="point2"></param>
    /// <param name="point3"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Douglases the peucker reduction.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="firstPointIndex">The first point.</param>
    /// <param name="lastPointIndex">The last point.</param>
    /// <param name="tolerance">The tolerance.</param>
    /// <param name="pointIndexsToKeep">The point index to keep.</param>
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

    /// <summary>
    /// The distance of a point from a line made from point1 and point2.
    /// </summary>
    /// <param name="point1">The point1.</param>
    /// <param name="point2">The point2.</param>
    /// <param name="point">The point.</param>
    /// <returns></returns>
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
        var rrr = 0f;
        //var stopWatch = new Stopwatch();
        //stopWatch.Start();
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

            //Logger.Warn($"# Found near position aiNavigation, Z: {pointN.Z}...");
            rrr = minDistFa < minDistN ? pointFa.Z : pointN.Z;
        }
        catch
        {
            rrr = 0f;
        }
        //stopWatch.Stop();
        //Logger.Info($"GetHeight took {stopWatch.Elapsed}");

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
    /// <param name="searchIn"></param>
    /// <param name="compareTo"></param>
    /// <returns>returns the index of the found point</returns>
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
            //Logger.Warn($"# Found near position aiNavigation, Z: {pointN.Z}...");
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
            _aiNavigation = new Dictionary<byte, Dictionary<uint, List<AiNavigation>>>();
            _areasMission = new Dictionary<byte, Dictionary<uint, string>>();
            _forbiddenArea = new Dictionary<byte, Dictionary<uint, List<Point>>>();
            _aiPath = new Dictionary<byte, Dictionary<uint, List<Point>>>();
            _aiNavigationModifier = new Dictionary<byte, Dictionary<uint, List<Point>>>();
        }

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
    /// Mirrors source AiGeoDataManager.IsSegmentBlocked adapted for Singleton architecture.
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
    /// Adapted for Singleton architecture: looks up BAI data from WorldManager.
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
                    // Allow raised indoor points that are on a distinct elevated floor layer.
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
    /// </summary>
    public Queue<Vector3> ReducePath(List<Vector3> foundPath, int maxNodeSkipCount)
    {
        var res = new Queue<Vector3>();
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
}
