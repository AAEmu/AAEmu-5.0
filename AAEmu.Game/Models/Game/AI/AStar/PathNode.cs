// https://lsreg.ru/realizaciya-algoritma-poiska-a-na-c/

#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.AI.AStar;

/// <summary>
/// Status of the last <see cref="PathNode.FindPath"/> call.
/// </summary>
public enum PathStatus
{
    None,
    Complete,
    Partial,
    Failed,
    DirectFallback,
}

/// <summary>
/// Reusable A* pathfinder.
/// </summary>
public class PathNode
{
    public uint ZoneKey { get; set; }
    public Vector3 Position { get; set; }
    public float PathLengthFromStart { get; set; }
    public PathNode? CameFrom { get; set; }
    public float PathLengthToEnd { get; set; }
    public float EstimateFullPathLength => PathLengthFromStart + PathLengthToEnd;

    public Vector3 StartPointPos { get; set; }
    public Vector3 EndPointPos { get; set; }
    public Queue<Vector3> FoundPath { get; set; } = new();
    public Vector3 CurrentTargetPos { get; set; }

    // Diagnostics
    public PathStatus LastStatus { get; set; } = PathStatus.None;
    public Vector3 LastStartReal { get; set; } = Vector3.Zero;
    public Vector3 LastEndReal { get; set; } = Vector3.Zero;
    public Vector3 LastStartNodePos { get; set; } = Vector3.Zero;
    public Vector3 LastEndNodePos { get; set; } = Vector3.Zero;
    public int LastRawNodeCount { get; set; }
    public float LastPathLength { get; set; }
    public float LastOptimalDistance { get; set; }
    public bool LastStartNodeSkipped { get; set; }
    public string LastRepathReason { get; set; } = string.Empty;
    public DateTime LastRepathTime { get; set; } = DateTime.MinValue;
    public uint LastTargetObjId { get; set; }
    public Vector3 LastTargetPosAtRepath { get; set; } = Vector3.Zero;

    /// <summary>
    /// Basic method of route calculation.
    /// </summary>
    public List<Vector3> FindPath(AAEmu.Game.Models.Game.World.World world, Vector3 start, Vector3 goal)
    {
        LastStartReal = start;
        LastEndReal = goal;
        LastStatus = PathStatus.Failed;
        LastRawNodeCount = 0;
        LastStartNodeSkipped = false;

        // Guard against null world, template, or geodata — prevents NPE when the
        // world is still loading or the navmesh has not been initialised yet.
        if (world?.Template?.GeoData == null)
            return [];

        var pathCfg = AppConfiguration.Instance.World?.Pathing;
        var enabled = pathCfg?.Enabled ?? true;
        var maxNode2D = pathCfg?.MaxStartEndNode2D ?? 0f;
        var useCumulative = pathCfg?.UseCumulativeAStarCost ?? true;
        var loopMul = pathCfg?.LoopBudgetDistanceMultiplier ?? 15;
        var loopBase = pathCfg?.LoopBudgetBase ?? 100;
        var useOptimized = pathCfg?.UseOptimizedAStar ?? true;

        // Find the nearest .bai node to the start point
        var posStart = enabled
            ? world.Template.GeoData.FindClosestPathNode2D(ZoneKey, start, maxNode2D)
            : null;

        if (posStart.HasValue)
        {
            start = posStart.Value;
            LastStartNodePos = posStart.Value;
        }
        else
        {
            LastStartNodePos = start;
        }

        var posEnd = enabled
            ? world.Template.GeoData.FindClosestPathNode2D(ZoneKey, goal, maxNode2D)
            : null;

        if (posEnd.HasValue)
        {
            goal = posEnd.Value;
            LastEndNodePos = posEnd.Value;
        }
        else
        {
            LastEndNodePos = goal;
        }

        EndPointPos = goal;
        var rawDistance = Vector3.Distance(start, goal);

        // Loop budget
        var maxLoops = (int)MathF.Ceiling(rawDistance * loopMul) + loopBase;
        const float goalReachedTolerance = 2.0f;

        if (useOptimized)
        {
            return FindPathOptimized(world, start, goal, useCumulative, maxLoops, goalReachedTolerance);
        }

        // Legacy V4 loop — deliberately unoptimized (O(n log n) open-set scan via
        // OrderBy) to maintain exact behavioural parity with the pre-v6 A* for A/B
        // testing. The v6 optimized path uses a binary min-heap instead.
        var closedSet = new Collection<PathNode>();
        var openSet = new Collection<PathNode>();

        var startNode = new PathNode
        {
            Position = start,
            CameFrom = null,
            PathLengthFromStart = 0,
            PathLengthToEnd = GetHeuristicPathLength(start, goal)
        };
        openSet.Add(startNode);

        var maxLoopsLeft = maxLoops;

        while (openSet.Count > 0)
        {
            maxLoopsLeft--;

            var currentNode = openSet.OrderBy(node => node.EstimateFullPathLength).First();

            var goalReached = Vector3.Distance(currentNode.Position, goal) <= goalReachedTolerance;
            if (goalReached || maxLoopsLeft <= 0)
            {
                var result = GetPathForNode(currentNode);

                LastOptimalDistance = Vector3.Distance(start, goal);
                var pathLen = 0f;
                for (var i = 1; i < result.Count; i++)
                    pathLen += Vector3.Distance(result[i - 1], result[i]);
                LastPathLength = pathLen;

                result.Add(EndPointPos);
                var reducedQueue = AiGeoDataManager.Instance.ReducePath(result, 4);
                result = reducedQueue.ToList();
                Position = result[0];
                CurrentTargetPos = Vector3.Zero;

                LastRawNodeCount = result.Count;
                LastStatus = goalReached ? PathStatus.Complete : PathStatus.Partial;
                return result;
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            foreach (var neighbourNode in GetNeighbours(world, currentNode, useCumulative))
            {
                if (closedSet.Any(node => node.Position.Equals(neighbourNode.Position)))
                    continue;

                var openNode = openSet.FirstOrDefault(node => node.Position.Equals(neighbourNode.Position));
                if (openNode == null)
                {
                    openSet.Add(neighbourNode);
                }
                else if (openNode.PathLengthFromStart > neighbourNode.PathLengthFromStart)
                {
                    openNode.CameFrom = currentNode;
                    openNode.PathLengthFromStart = neighbourNode.PathLengthFromStart;
                    openNode.PathLengthToEnd = neighbourNode.PathLengthToEnd;
                }
            }
        }

        LastStatus = PathStatus.Failed;
        return [];
    }

    /// <summary>
    /// Optimized A* (v6) with binary heap.
    /// </summary>
    private List<Vector3> FindPathOptimized(AAEmu.Game.Models.Game.World.World world, Vector3 start, Vector3 goal,
        bool useCumulative, int maxLoops, float goalReachedTolerance)
    {
        var heap = AStarScratchPool.RentHeap();
        var bestG = AStarScratchPool.RentDictionary();
        var cameFrom = AStarScratchPool.RentDictionaryVec();
        var closed = AStarScratchPool.RentHashSet();
        var seq = 0L;

        var startH = GetHeuristicPathLength(start, goal);
        bestG[start] = 0f;
        heap.Push(new AStarEntry(start, 0f, startH, seq++));

        var maxLoopsLeft = maxLoops;

        while (heap.Count > 0)
        {
            var cur = heap.Pop();

            if (closed.Contains(cur.Pos))
                continue;
            if (bestG.TryGetValue(cur.Pos, out var knownG) && cur.G > knownG + 1e-4f)
                continue;

            maxLoopsLeft--;

            var goalReached = Vector3.Distance(cur.Pos, goal) <= goalReachedTolerance;
            if (goalReached || maxLoopsLeft <= 0)
            {
                var result = ReconstructPath(cameFrom, cur.Pos, start);

                LastOptimalDistance = Vector3.Distance(start, goal);
                var pathLen = 0f;
                for (var i = 1; i < result.Count; i++)
                    pathLen += Vector3.Distance(result[i - 1], result[i]);
                LastPathLength = pathLen;

                result.Add(EndPointPos);
                var reducedQueue = AiGeoDataManager.Instance.ReducePath(result, 4);
                result = reducedQueue.ToList();
                Position = result[0];
                CurrentTargetPos = Vector3.Zero;

                LastRawNodeCount = result.Count;
                LastStatus = goalReached ? PathStatus.Complete : PathStatus.Partial;

                // Return scratch collections to pool
                AStarScratchPool.ReturnHeap(heap);
                AStarScratchPool.ReturnDictionary(bestG);
                AStarScratchPool.ReturnDictionaryVec(cameFrom);
                AStarScratchPool.ReturnHashSet(closed);

                return result;
            }

            closed.Add(cur.Pos);

            var curNode = new PathNode
            {
                CurrentTargetPos = cur.Pos,
                Position = cur.Pos,
                EndPointPos = goal,
                PathLengthFromStart = cur.G,
                PathLengthToEnd = cur.H
            };

            foreach (var nb in GetNeighbours(world, curNode, useCumulative))
            {
                var nbPos = nb.Position;
                if (closed.Contains(nbPos))
                    continue;

                var tentativeG = nb.PathLengthFromStart;
                if (bestG.TryGetValue(nbPos, out var prevG) && tentativeG >= prevG - 1e-4f)
                    continue;

                bestG[nbPos] = tentativeG;
                cameFrom[nbPos] = cur.Pos;
                var hh = Vector3.Distance(nbPos, goal);
                heap.Push(new AStarEntry(nbPos, tentativeG, hh, seq++));
            }
        }

        LastStatus = PathStatus.Failed;

        // Return scratch collections to pool
        AStarScratchPool.ReturnHeap(heap);
        AStarScratchPool.ReturnDictionary(bestG);
        AStarScratchPool.ReturnDictionaryVec(cameFrom);
        AStarScratchPool.ReturnHashSet(closed);

        return [];
    }

    private static List<Vector3> ReconstructPath(Dictionary<Vector3, Vector3> cameFrom, Vector3 goal, Vector3 start)
    {
        var result = new List<Vector3> { goal };
        var cur = goal;
        var guard = 0;
        while (!cur.Equals(start) && cameFrom.TryGetValue(cur, out var prev) && guard++ < 4096)
        {
            cur = prev;
            result.Add(cur);
        }
        result.Reverse();
        return result;
    }

    private static float GetHeuristicPathLength(Vector3 from, Vector3 to)
    {
        return Vector3.Distance(from, to);
    }

    /// <summary>
    /// Obtaining a list of neighbours (V6 pathfinding fix).
    ///
    /// Pathfinding fix (bidirectional graph — v4):
    /// We now use AiGeoDataManager.GetNeighbourNodes instead of GetAvailablePoints.
    /// The legacy GetAvailablePoints only returned outgoing edges, treating the
    /// undirected .bai navmesh graph as if it were directed. This made A* unable to see
    /// roughly half of the reachable neighbours and caused 86% of paths to time out as
    /// 'Partial' on live servers. GetNeighbourNodes returns both outgoing
    /// (SourceNode == this) and incoming (TargetNode == this) edges, so A* now sees the
    /// full bidirectional graph as CryEngine intends.
    ///
    /// Pathfinding fix (G cost — v1):
    /// The legacy code set PathLengthFromStart to (sourceNode - goal).Length(),
    /// which is not a cumulative G cost. The corrected formula is
    /// parent.PathLengthFromStart + step, where step is the distance from the parent
    /// node to this neighbour.
    ///
    /// v6: when UseEdgeLengthCost is enabled, the per-edge step cost uses the
    /// link length from GetNeighbourNodes instead of the straight-line distance.
    /// </summary>
    private Collection<PathNode> GetNeighbours(AAEmu.Game.Models.Game.World.World world, PathNode pathNode, bool useCumulativeCost)
    {
        var result = new Collection<PathNode>();

        if (world?.Template?.GeoData == null)
            return result;

        var geoData = world.Template.GeoData;
        var pathCfg = AppConfiguration.Instance.World?.Pathing;
        var useEdgeLen = pathCfg?.UseEdgeLengthCost ?? true;

        // Get bidirectional neighbours using position-based lookup
        var neighbours = AiGeoDataManager.Instance.GetNeighbourNodesByPosition(ZoneKey, pathNode.Position);

        foreach (var (linkLength, neighbourPos) in neighbours)
        {
            // Check if the neighbour position is in a forbidden area
            if (geoData.CheckImpossibleWalk(neighbourPos))
                continue;

            // Calculate G cost (cumulative from start)
            float gCost;
            if (useCumulativeCost)
            {
                // v6: prefer the true on-mesh edge length when available
                float stepCost;
                if (useEdgeLen && linkLength > 0.01f)
                    stepCost = linkLength;
                else
                    stepCost = Vector3.Distance(pathNode.Position, neighbourPos);
                gCost = pathNode.PathLengthFromStart + stepCost;
            }
            else
            {
                // Legacy formula (kept for A/B testing only)
                gCost = Vector3.Distance(neighbourPos, pathNode.EndPointPos);
            }

            // H cost (heuristic to goal)
            var hCost = Vector3.Distance(neighbourPos, pathNode.EndPointPos);

            var neighbourNode = new PathNode
            {
                Position = neighbourPos,
                EndPointPos = pathNode.EndPointPos,
                CameFrom = pathNode,
                PathLengthFromStart = gCost,
                PathLengthToEnd = hCost
            };

            result.Add(neighbourNode);
        }

        return result;
    }

    private static List<Vector3> GetPathForNode(PathNode pathNode)
    {
        var result = new List<Vector3>();
        var currentNode = pathNode;
        while (currentNode != null)
        {
            result.Add(currentNode.Position);
            currentNode = currentNode.CameFrom;
        }
        result.Reverse();
        return result;
    }
}

/// <summary>
/// A single open-set entry for the v6 optimized A*.
/// </summary>
public readonly struct AStarEntry
{
    public readonly Vector3 Pos;
    public readonly float G;
    public readonly float H;
    public readonly float F;
    public readonly long Seq;

    public AStarEntry(Vector3 pos, float g, float h, long seq)
    {
        Pos = pos;
        G = g;
        H = h;
        F = g + h;
        Seq = seq;
    }

    public int CompareTo(in AStarEntry other)
    {
        var c = F.CompareTo(other.F);
        if (c != 0) return c;
        c = H.CompareTo(other.H);
        if (c != 0) return c;
        return Seq.CompareTo(other.Seq);
    }
}

/// <summary>
/// Minimal array-backed binary min-heap for A*.
/// </summary>
public sealed class AStarHeap
{
    private AStarEntry[] _items = new AStarEntry[64];
    public int Count { get; private set; }

    public void Clear()
    {
        Count = 0;
    }

    public void Push(in AStarEntry e)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        _items[Count] = e;
        SiftUp(Count);
        Count++;
    }

    public AStarEntry Pop()
    {
        var root = _items[0];
        Count--;
        if (Count > 0)
        {
            _items[0] = _items[Count];
            SiftDown(0);
        }
        _items[Count] = default;
        return root;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            var parent = (i - 1) >> 1;
            if (_items[i].CompareTo(_items[parent]) >= 0)
                break;
            (_items[i], _items[parent]) = (_items[parent], _items[i]);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            var left = (i << 1) + 1;
            var right = left + 1;
            var smallest = i;
            if (left < Count && _items[left].CompareTo(_items[smallest]) < 0)
                smallest = left;
            if (right < Count && _items[right].CompareTo(_items[smallest]) < 0)
                smallest = right;
            if (smallest == i)
                break;
            (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
            i = smallest;
        }
    }
}

/// <summary>
/// Lightweight object pool for A* scratch collections.
/// Reduces GC pressure from per-call dictionary/hashset allocations in FindPathOptimized.
///
/// Thread-affinity requirement:
/// Uses [ThreadStatic] to avoid synchronization overhead — each thread gets its own pool.
/// The Rent/Return calls MUST happen on the same thread. This is currently guaranteed because
/// FindPath/FindPathOptimized are synchronous and called from the main game tick thread.
/// If you ever move FindPath to a Task.Run or thread-pool callback, either:
///   (a) disable this pool (replace Rent with 'new' and Return with no-op), or
///   (b) switch to a ConcurrentBag-based pool with proper synchronization.
/// </summary>
internal static class AStarScratchPool
{
    [ThreadStatic]
    private static Dictionary<Vector3, float>? _dictBestG;
    [ThreadStatic]
    private static Dictionary<Vector3, Vector3>? _dictCameFrom;
    [ThreadStatic]
    private static HashSet<Vector3>? _hashClosed;

    public static Dictionary<Vector3, float> RentDictionary()
    {
        var d = _dictBestG;
        _dictBestG = null;
        if (d == null)
            return new Dictionary<Vector3, float>(128);
        d.Clear();
        return d;
    }

    public static void ReturnDictionary(Dictionary<Vector3, float> d)
    {
        _dictBestG = d;
    }

    public static Dictionary<Vector3, Vector3> RentDictionaryVec()
    {
        var d = _dictCameFrom;
        _dictCameFrom = null;
        if (d == null)
            return new Dictionary<Vector3, Vector3>(128);
        d.Clear();
        return d;
    }

    public static void ReturnDictionaryVec(Dictionary<Vector3, Vector3> d)
    {
        _dictCameFrom = d;
    }

    public static HashSet<Vector3> RentHashSet()
    {
        var h = _hashClosed;
        _hashClosed = null;
        if (h == null)
            return new HashSet<Vector3>(128);
        h.Clear();
        return h;
    }

    public static void ReturnHashSet(HashSet<Vector3> h)
    {
        _hashClosed = h;
    }

    [ThreadStatic]
    private static AStarHeap? _heap;

    public static AStarHeap RentHeap()
    {
        var h = _heap;
        _heap = null;
        if (h == null)
            return new AStarHeap();
        h.Clear();
        return h;
    }

    public static void ReturnHeap(AStarHeap h)
    {
        _heap = h;
    }
}
