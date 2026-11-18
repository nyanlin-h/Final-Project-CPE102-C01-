using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    [System.Serializable]
    public struct PathOptions
    {
        /// <summary>Wheelchair / trolley mode: never use stairs.</summary>
        public bool avoidStairs;
        /// <summary>Never use elevators (e.g. during fire drills).</summary>
        public bool avoidElevators;
    }

    /// <summary>A run of the route that stays on one floor. Ends at the destination or at a stairs/elevator node.</summary>
    public class PathSegment
    {
        public int floorLevel;
        public readonly List<int> nodeIndices = new List<int>();
        public readonly List<Vector3> mapPoints = new List<Vector3>();
        /// <summary>Kind of vertical move that follows this segment; Walk if this is the last segment.</summary>
        public EdgeKind exitTransition = EdgeKind.Walk;
        public int nextFloorLevel;
    }

    public class PathResult
    {
        public bool found;
        public float totalCost;
        public readonly List<int> nodeIndices = new List<int>();
        public readonly List<PathSegment> segments = new List<PathSegment>();
    }

    /// <summary>
    /// A* over NavGraph. Heuristic = 3D straight-line distance (admissible: every edge cost >= its length).
    /// Multi-floor routes come out of the same search because stairs/elevators are ordinary weighted edges.
    /// </summary>
    public class AStarPathfinder
    {
        readonly NavGraph graph;

        // Reused buffers (no per-query GC beyond the result)
        float[] g;
        int[] parent;
        EdgeKind[] parentEdge;
        int[] closedStamp;
        int[] openStamp;
        int stamp;
        readonly MinHeap open = new MinHeap(256);

        public AStarPathfinder(NavGraph graph)
        {
            this.graph = graph;
            int n = graph.Count;
            g = new float[n];
            parent = new int[n];
            parentEdge = new EdgeKind[n];
            closedStamp = new int[n];
            openStamp = new int[n];
        }

        public PathResult FindPath(int start, int goal, PathOptions options = default)
        {
            var result = new PathResult();
            if (start < 0 || goal < 0 || start >= graph.Count || goal >= graph.Count) return result;

            stamp++;
            open.Clear();
            var pos = graph.positions;

            g[start] = 0f;
            parent[start] = -1;
            openStamp[start] = stamp;
            open.Push(start, Vector3.Distance(pos[start], pos[goal]));

            while (open.Count > 0)
            {
                int u = open.Pop();
                if (closedStamp[u] == stamp) continue;          // stale heap entry
                closedStamp[u] = stamp;
                if (u == goal) break;

                var edges = graph.adjacency[u];
                for (int k = 0; k < edges.Count; k++)
                {
                    var e = edges[k];
                    if (options.avoidStairs && e.kind == EdgeKind.Stairs) continue;
                    if (options.avoidElevators && e.kind == EdgeKind.Elevator) continue;
                    int v = e.to;
                    if (closedStamp[v] == stamp) continue;
                    float tentative = g[u] + e.cost;
                    if (openStamp[v] == stamp && tentative >= g[v]) continue;
                    openStamp[v] = stamp;
                    g[v] = tentative;
                    parent[v] = u;
                    parentEdge[v] = e.kind;
                    open.Push(v, tentative + Vector3.Distance(pos[v], pos[goal]));
                }
            }

            if (closedStamp[goal] != stamp) return result;

            // Reconstruct
            for (int v = goal; v != -1; v = parent[v]) result.nodeIndices.Add(v);
            result.nodeIndices.Reverse();
            result.found = true;
            result.totalCost = g[goal];

            // Split into per-floor segments
            PathSegment seg = null;
            for (int i = 0; i < result.nodeIndices.Count; i++)
            {
                int idx = result.nodeIndices[i];
                if (seg == null || graph.floorOf[idx] != seg.floorLevel)
                {
                    if (seg != null)
                    {
                        seg.exitTransition = parentEdge[idx];
                        seg.nextFloorLevel = graph.floorOf[idx];
                    }
                    seg = new PathSegment { floorLevel = graph.floorOf[idx] };
                    result.segments.Add(seg);
                }
                seg.nodeIndices.Add(idx);
                seg.mapPoints.Add(pos[idx]);
            }
            return result;
        }

        /// <summary>Binary min-heap with lazy deletion (duplicates allowed, filtered by closed set).</summary>
        class MinHeap
        {
            int[] items;
            float[] keys;
            public int Count { get; private set; }

            public MinHeap(int capacity) { items = new int[capacity]; keys = new float[capacity]; }
            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == items.Length)
                {
                    System.Array.Resize(ref items, Count * 2);
                    System.Array.Resize(ref keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (keys[p] <= key) break;
                    items[i] = items[p]; keys[i] = keys[p]; i = p;
                }
                items[i] = item; keys[i] = key;
            }

            public int Pop()
            {
                int top = items[0];
                int lastItem = items[--Count];
                float lastKey = keys[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count && keys[c + 1] < keys[c]) c++;
                    if (keys[c] >= lastKey) break;
                    items[i] = items[c]; keys[i] = keys[c]; i = c;
                }
                if (Count > 0) { items[i] = lastItem; keys[i] = lastKey; }
                return top;
            }
        }
    }
}
