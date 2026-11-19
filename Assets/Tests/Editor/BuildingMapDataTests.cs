using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KMUTT.ARNav.Tests
{
    /// <summary>
    /// Sanity checks on the real imported map in SampleScene. Run after every map re-import:
    /// they catch disconnected nodes, missing rooms, broken stairs/lift pairing and step-free dead ends.
    /// </summary>
    public class BuildingMapDataTests
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";

        static readonly string[] ExpectedDestinations =
        {
            "F10_ROOM_1052", "F10_DEPARTMENT_HALL", "F10_ROOM_1017", "F10_ROOM_1016", "F10_ROOM_1014",
            "F10_ROOM_1013", "F10_ROOM_1015", "F10_ROOM_1012", "F10_ROOM_1002", "F10_ROOM_1001",
            "F11_ROOM_1112", "F11_ROOM_1113", "F11_ROOM_1114", "F11_ROOM_1115", "F11_ROOM_1116",
            "F11_ROOM_1117", "F11_ROOM_1118", "F11_ROOM_1119", "F11_ROOM_1120", "F11_ROOM_1121",
        };

        static readonly string[] StairGroups = { "StairNW", "StairW", "StairE", "StairSE" };
        static readonly string[] LiftGroups = { "LiftW", "LiftE" };

        Scene scene;
        bool openedByTest;
        NavGraph graph;
        AStarPathfinder pathfinder;
        List<int> destinations;

        [OneTimeSetUp]
        public void LoadScene()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedByTest = true;
            }
            var map = scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<BuildingMap>(true))
                .FirstOrDefault(m => m != null);
            Assert.IsNotNull(map, $"No BuildingMap in {ScenePath}. Run KMUTT → AR Map Tools → Build + Import first.");

            graph = NavGraph.Build(map);
            pathfinder = new AStarPathfinder(graph);
            destinations = graph.Destinations();
        }

        [OneTimeTearDown]
        public void UnloadScene()
        {
            if (openedByTest && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }

        [Test]
        public void Graph_HasNodes_AndIsOneConnectedComponent()
        {
            Assert.Greater(graph.Count, 100);
            graph.Components(out int count);
            Assert.AreEqual(1, count, "Some nodes are unreachable — run AR Map Tools → Validate Graph");
        }

        [Test]
        public void NodeIds_AreUnique()
        {
            var dupes = graph.nodes.Where(n => !string.IsNullOrEmpty(n.nodeId))
                .GroupBy(n => n.nodeId).Where(grp => grp.Count() > 1).Select(grp => grp.Key).ToList();
            CollectionAssert.IsEmpty(dupes);
        }

        [Test]
        public void AllExpectedDestinations_ExistOnCorrectFloor()
        {
            var actual = destinations.Select(i => graph.nodes[i].nodeId).ToList();
            CollectionAssert.AreEquivalent(ExpectedDestinations, actual,
                "Destination list changed. If intentional, update ExpectedDestinations and README §6.");
            foreach (int i in destinations)
            {
                var n = graph.nodes[i];
                int floorFromId = int.Parse(n.nodeId.Substring(1, 2));
                Assert.AreEqual(floorFromId, n.floorLevel, $"{n.nodeId} is on floor {n.floorLevel}");
                Assert.IsNotEmpty(n.displayName, $"{n.nodeId} has no display name");
            }
        }

        [Test]
        public void EveryTransitionGroup_LinksFloor10And11_WithCorrectKind()
        {
            foreach (var (group, kind) in StairGroups.Select(s => (s, EdgeKind.Stairs))
                         .Concat(LiftGroups.Select(l => (l, EdgeKind.Elevator))))
            {
                int f10 = graph.IndexOf($"F10_{group.ToUpperInvariant()}");
                int f11 = graph.IndexOf($"F11_{group.ToUpperInvariant()}");
                Assert.GreaterOrEqual(f10, 0, $"F10 node for {group} missing");
                Assert.GreaterOrEqual(f11, 0, $"F11 node for {group} missing");
                Assert.IsTrue(graph.adjacency[f10].Any(e => e.to == f11 && e.kind == kind),
                    $"{group}: no {kind} edge between floors");
            }
        }

        [Test]
        public void EveryDestinationPair_IsRoutable()
        {
            AssertAllPairs(default, (from, to, r) => { });
        }

        [Test]
        public void StepFree_EveryDestinationPair_IsRoutable_WithoutStairs()
        {
            AssertAllPairs(new PathOptions { avoidStairs = true }, (from, to, r) =>
            {
                foreach (var seg in r.segments)
                    Assert.AreNotEqual(EdgeKind.Stairs, seg.exitTransition, $"{from} → {to} uses stairs");
            });
        }

        [Test]
        public void CrossFloorRoutes_HaveOneTransition_AndSegmentsMatchFloors()
        {
            AssertAllPairs(default, (from, to, r) =>
            {
                var a = graph.nodes[r.nodeIndices[0]];
                var b = graph.nodes[r.nodeIndices[r.nodeIndices.Count - 1]];
                int expected = a.floorLevel == b.floorLevel ? 1 : 2;
                Assert.AreEqual(expected, r.segments.Count, $"{from} → {to}: {r.segments.Count} segments");
                Assert.AreEqual(a.floorLevel, r.segments[0].floorLevel);
                Assert.AreEqual(b.floorLevel, r.segments[r.segments.Count - 1].floorLevel);
                if (expected == 2)
                {
                    Assert.AreNotEqual(EdgeKind.Walk, r.segments[0].exitTransition);
                    Assert.AreEqual(b.floorLevel, r.segments[0].nextFloorLevel);
                }
                foreach (var seg in r.segments)
                    foreach (int idx in seg.nodeIndices)
                        Assert.AreEqual(seg.floorLevel, graph.floorOf[idx], $"{from} → {to}: node on wrong floor segment");
            });
        }

        void AssertAllPairs(PathOptions opt, System.Action<string, string, PathResult> check)
        {
            var failures = new List<string>();
            foreach (int s in destinations)
            foreach (int t in destinations)
            {
                if (s == t) continue;
                var r = pathfinder.FindPath(s, t, opt);
                string from = graph.nodes[s].nodeId, to = graph.nodes[t].nodeId;
                if (!r.found) { failures.Add($"{from} → {to}"); continue; }
                check(from, to, r);
            }
            CollectionAssert.IsEmpty(failures, "No route for: " + string.Join(", ", failures));
        }
    }
}
