using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KMUTT.ARNav.Tests
{
    /// <summary>
    /// A* / NavGraph behaviour on a tiny hand-built two-floor map, independent of the real building data.
    ///
    ///   Floor 11 (y=3.5):   S11 ─── M11 ─── L11 ── R11(dest)
    ///                        │stairs          │lift
    ///   Floor 10 (y=0):     S10 ─── M10 ─── L10 ── A10(dest)
    ///                       (x=0)  (x=10)  (x=20)  (x=25)
    /// </summary>
    public class PathfindingSyntheticTests
    {
        GameObject root;
        BuildingMap map;
        readonly Dictionary<string, NavNode> nodes = new Dictionary<string, NavNode>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TestBuildingMap");
            map = root.AddComponent<BuildingMap>();

            Add("S10", 10, new Vector3(0, 0, 0), NavNodeType.Stairs, "StairA");
            Add("M10", 10, new Vector3(10, 0, 0));
            Add("L10", 10, new Vector3(20, 0, 0), NavNodeType.Elevator, "LiftA");
            Add("A10", 10, new Vector3(25, 0, 0), NavNodeType.Room, dest: true);
            Add("S11", 11, new Vector3(0, 3.5f, 0), NavNodeType.Stairs, "StairA");
            Add("M11", 11, new Vector3(10, 3.5f, 0));
            Add("L11", 11, new Vector3(20, 3.5f, 0), NavNodeType.Elevator, "LiftA");
            Add("R11", 11, new Vector3(25, 3.5f, 0), NavNodeType.Room, dest: true);

            Link("S10", "M10"); Link("M10", "L10"); Link("L10", "A10");
            Link("S11", "M11"); Link("M11", "L11"); Link("L11", "R11");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            nodes.Clear();
        }

        void Add(string id, int floor, Vector3 pos, NavNodeType type = NavNodeType.Waypoint, string group = "", bool dest = false)
        {
            var go = new GameObject(id);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            var n = go.AddComponent<NavNode>();
            n.nodeId = id; n.floorLevel = floor; n.type = type; n.transitionGroup = group; n.isDestination = dest;
            nodes[id] = n;
        }

        // Stored on one side only: NavGraph must still treat links as bidirectional.
        void Link(string a, string b) => nodes[a].links.Add(nodes[b]);

        PathResult Find(NavGraph g, string from, string to, PathOptions opt = default)
            => new AStarPathfinder(g).FindPath(g.IndexOf(from), g.IndexOf(to), opt);

        static List<EdgeKind> EdgeKinds(NavGraph g, PathResult r)
        {
            var kinds = new List<EdgeKind>();
            for (int i = 1; i < r.nodeIndices.Count; i++)
            {
                int a = r.nodeIndices[i - 1], b = r.nodeIndices[i];
                kinds.Add(g.adjacency[a].Find(e => e.to == b).kind);
            }
            return kinds;
        }

        [Test]
        public void Graph_IsSingleComponent_AndLinksAreBidirectional()
        {
            var g = NavGraph.Build(map);
            g.Components(out int count);
            Assert.AreEqual(1, count);
            Assert.IsTrue(Find(g, "A10", "S10").found, "one-sided link should be walkable in reverse");
        }

        [Test]
        public void SameFloor_RouteIsSingleSegment_WithStraightLineCost()
        {
            var g = NavGraph.Build(map);
            var r = Find(g, "S10", "A10");
            Assert.IsTrue(r.found);
            Assert.AreEqual(1, r.segments.Count);
            Assert.AreEqual(25f, r.totalCost, 1e-3f);
        }

        [Test]
        public void CrossFloor_PrefersCheaperStairs_AndSplitsIntoTwoSegments()
        {
            // Stairs penalty tiny, lift penalty huge → must use stairs even though it's a detour.
            var g = NavGraph.Build(map, stairsPenalty: 1f, elevatorPenalty: 1000f);
            var r = Find(g, "A10", "R11");

            Assert.IsTrue(r.found);
            CollectionAssert.Contains(EdgeKinds(g, r), EdgeKind.Stairs);
            CollectionAssert.DoesNotContain(EdgeKinds(g, r), EdgeKind.Elevator);
            Assert.AreEqual(2, r.segments.Count);
            Assert.AreEqual(10, r.segments[0].floorLevel);
            Assert.AreEqual(EdgeKind.Stairs, r.segments[0].exitTransition);
            Assert.AreEqual(11, r.segments[0].nextFloorLevel);
            Assert.AreEqual(11, r.segments[1].floorLevel);
            Assert.AreEqual(EdgeKind.Walk, r.segments[1].exitTransition, "last segment has no exit transition");
        }

        [Test]
        public void CrossFloor_PrefersCheaperLift()
        {
            var g = NavGraph.Build(map, stairsPenalty: 1000f, elevatorPenalty: 1f);
            var r = Find(g, "A10", "R11");
            Assert.IsTrue(r.found);
            Assert.AreEqual(EdgeKind.Elevator, r.segments[0].exitTransition);
        }

        [Test]
        public void StepFree_NeverUsesStairs()
        {
            var g = NavGraph.Build(map, stairsPenalty: 1f, elevatorPenalty: 1000f);
            var r = Find(g, "S10", "S11", new PathOptions { avoidStairs = true });
            Assert.IsTrue(r.found);
            CollectionAssert.DoesNotContain(EdgeKinds(g, r), EdgeKind.Stairs);
        }

        [Test]
        public void AvoidingStairsAndLifts_BetweenFloors_HasNoRoute()
        {
            var g = NavGraph.Build(map);
            var r = Find(g, "A10", "R11", new PathOptions { avoidStairs = true, avoidElevators = true });
            Assert.IsFalse(r.found);
            Assert.AreEqual(0, r.segments.Count);
        }

        [Test]
        public void InvalidIndices_ReturnNotFound()
        {
            var pf = new AStarPathfinder(NavGraph.Build(map));
            Assert.IsFalse(pf.FindPath(-1, 0).found);
            Assert.IsFalse(pf.FindPath(0, 999).found);
        }

        [Test]
        public void StartEqualsGoal_IsTrivialRoute()
        {
            var g = NavGraph.Build(map);
            var r = Find(g, "M10", "M10");
            Assert.IsTrue(r.found);
            Assert.AreEqual(1, r.nodeIndices.Count);
            Assert.AreEqual(0f, r.totalCost);
        }

        [Test]
        public void RepeatedQueries_OnSamePathfinder_AreConsistent()
        {
            // Guards the stamp-based buffer reuse in AStarPathfinder.
            var g = NavGraph.Build(map);
            var pf = new AStarPathfinder(g);
            var first = pf.FindPath(g.IndexOf("A10"), g.IndexOf("R11"));
            pf.FindPath(g.IndexOf("S11"), g.IndexOf("M10"));
            var again = pf.FindPath(g.IndexOf("A10"), g.IndexOf("R11"));
            Assert.AreEqual(first.totalCost, again.totalCost, 1e-4f);
            CollectionAssert.AreEqual(first.nodeIndices, again.nodeIndices);
        }

        [Test]
        public void EveryEdgeCost_IsAtLeastItsLength_SoHeuristicIsAdmissible()
        {
            var g = NavGraph.Build(map, stairsPenalty: 0.1f, elevatorPenalty: 0.1f);
            for (int i = 0; i < g.Count; i++)
                foreach (var e in g.adjacency[i])
                    Assert.GreaterOrEqual(e.cost + 1e-4f, Vector3.Distance(g.positions[i], g.positions[e.to]));
        }

        [Test]
        public void NearestNode_RespectsFloor()
        {
            var g = NavGraph.Build(map);
            Assert.AreEqual(g.IndexOf("M11"), g.NearestNode(new Vector3(9, 0, 0), 11));
            Assert.AreEqual(g.IndexOf("M10"), g.NearestNode(new Vector3(9, 0, 0), 10));
        }
    }
}
