using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    public enum EdgeKind { Walk, Stairs, Elevator }

    public struct NavEdge
    {
        public int to;
        public float cost;
        public EdgeKind kind;
    }

    /// <summary>
    /// Immutable runtime snapshot of all NavNodes under a BuildingMap, in MAP space
    /// (so it stays valid no matter where MapAligner puts the building in AR session space).
    /// </summary>
    public class NavGraph
    {
        public readonly List<NavNode> nodes = new List<NavNode>();
        public Vector3[] positions;     // map space
        public int[] floorOf;
        public List<NavEdge>[] adjacency;

        readonly Dictionary<NavNode, int> indexOf = new Dictionary<NavNode, int>();
        readonly Dictionary<string, int> indexOfId = new Dictionary<string, int>();

        public int Count => nodes.Count;

        public static NavGraph Build(BuildingMap map, float stairsPenalty = 15f, float elevatorPenalty = 25f)
        {
            var g = new NavGraph();
            map.GetComponentsInChildren(true, g.nodes);
            int n = g.nodes.Count;
            g.positions = new Vector3[n];
            g.floorOf = new int[n];
            g.adjacency = new List<NavEdge>[n];

            for (int i = 0; i < n; i++)
            {
                var node = g.nodes[i];
                g.indexOf[node] = i;
                g.positions[i] = map.transform.InverseTransformPoint(node.transform.position);
                g.floorOf[i] = node.floorLevel;
                g.adjacency[i] = new List<NavEdge>();
                if (!string.IsNullOrEmpty(node.nodeId))
                {
                    if (g.indexOfId.ContainsKey(node.nodeId))
                        Debug.LogWarning($"[NavGraph] Duplicate nodeId '{node.nodeId}' on {node.name}", node);
                    else g.indexOfId[node.nodeId] = i;
                }
            }

            // Horizontal edges (dedupe: links may be stored on both ends)
            var seen = new HashSet<long>();
            for (int i = 0; i < n; i++)
            {
                foreach (var other in g.nodes[i].AllLinks())
                {
                    if (!g.indexOf.TryGetValue(other, out int j) || j == i) continue;
                    long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                    if (!seen.Add(key)) continue;
                    float d = Vector3.Distance(g.positions[i], g.positions[j]);
                    g.AddUndirected(i, j, d, EdgeKind.Walk);
                }
            }

            // Vertical edges: connect every Stairs/Elevator node sharing a transitionGroup across floors
            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var node = g.nodes[i];
                if (!node.IsTransition) continue;
                if (!groups.TryGetValue(node.transitionGroup, out var list)) groups[node.transitionGroup] = list = new List<int>();
                list.Add(i);
            }
            foreach (var kv in groups)
            {
                var list = kv.Value;
                for (int a = 0; a < list.Count; a++)
                for (int b = a + 1; b < list.Count; b++)
                {
                    int i = list[a], j = list[b];
                    if (g.floorOf[i] == g.floorOf[j]) continue;
                    var kind = g.nodes[i].type == NavNodeType.Elevator ? EdgeKind.Elevator : EdgeKind.Stairs;
                    float penalty = kind == EdgeKind.Elevator ? elevatorPenalty : stairsPenalty * Mathf.Abs(g.floorOf[i] - g.floorOf[j]);
                    // cost >= straight-line distance keeps the Euclidean A* heuristic admissible
                    float cost = Mathf.Max(penalty, Vector3.Distance(g.positions[i], g.positions[j]));
                    g.AddUndirected(i, j, cost, kind);
                }
            }
            return g;
        }

        void AddUndirected(int a, int b, float cost, EdgeKind kind)
        {
            adjacency[a].Add(new NavEdge { to = b, cost = cost, kind = kind });
            adjacency[b].Add(new NavEdge { to = a, cost = cost, kind = kind });
        }

        public int IndexOf(NavNode node) => node != null && indexOf.TryGetValue(node, out int i) ? i : -1;
        public int IndexOf(string nodeId) => nodeId != null && indexOfId.TryGetValue(nodeId, out int i) ? i : -1;

        /// <summary>Closest node on a floor to a map-space point (ignores height). Linear scan: fine for a few thousand nodes.</summary>
        public int NearestNode(Vector3 mapPos, int floorLevel, bool requireEdges = true)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < positions.Length; i++)
            {
                if (floorOf[i] != floorLevel) continue;
                if (requireEdges && adjacency[i].Count == 0) continue;
                float dx = positions[i].x - mapPos.x, dz = positions[i].z - mapPos.z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        public List<int> Destinations()
        {
            var r = new List<int>();
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].isDestination) r.Add(i);
            return r;
        }

        /// <summary>Connected-component labels; useful for validating that every destination is reachable.</summary>
        public int[] Components(out int count)
        {
            var comp = new int[Count];
            for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            count = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < Count; s++)
            {
                if (comp[s] != -1) continue;
                comp[s] = count; stack.Push(s);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (var e in adjacency[u])
                        if (comp[e.to] == -1) { comp[e.to] = count; stack.Push(e.to); }
                }
                count++;
            }
            return comp;
        }
    }
}
