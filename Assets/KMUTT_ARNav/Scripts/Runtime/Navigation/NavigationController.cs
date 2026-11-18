using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace KMUTT.ARNav
{
    public enum NavState { Idle, NeedsCalibration, Navigating, FloorTransition, Arrived, NoRoute }

    /// <summary>
    /// Glue between tracking, pathfinding and visuals:
    /// AR camera pose → map space → current floor → A* route → per-floor segment → ARPathVisualizer.
    /// Handles arrival, off-route re-planning and stairs/elevator hand-over between Floor 10 and 11.
    /// </summary>
    public class NavigationController : MonoBehaviour
    {
        [Header("References")]
        public BuildingMap map;
        public MapAligner aligner;
        public ARPathVisualizer visualizer;
        public Camera arCamera;
        [Tooltip("Layer of the BuildingMap wall colliders (ARNavWalls).")]
        public LayerMask wallMask;

        [Header("Graph costs")]
        public float stairsPenalty = 15f;
        public float elevatorPenalty = 25f;
        public PathOptions options;

        [Header("Behaviour")]
        public float arriveRadius = 1.5f;
        public float transitionReachRadius = 1.8f;
        public float offRouteDistance = 2.5f;
        public float offRouteSeconds = 2f;
        [Tooltip("Approx. camera height above the floor while walking; used to decide which floor you're on.")]
        public float eyeHeight = 1.4f;
        public bool smoothPath = true;

        [Header("Events (hook UI here)")]
        public UnityEvent<string> onInstruction;
        public UnityEvent<NavState> onStateChanged;
        public UnityEvent<float> onDistanceRemaining;

        public NavState State { get; private set; } = NavState.Idle;
        public NavGraph Graph { get; private set; }
        public int CurrentFloor { get; private set; } = 10;
        public NavNode Destination { get; private set; }

        AStarPathfinder pathfinder;
        PathResult route;
        int segmentIndex;
        List<Vector3> activePolyline;
        float offRouteTimer;
        int destIndex = -1;

        void Reset()
        {
            map = FindFirstObjectByType<BuildingMap>();
            aligner = FindFirstObjectByType<MapAligner>();
            visualizer = FindFirstObjectByType<ARPathVisualizer>();
            arCamera = Camera.main;
            wallMask = LayerMask.GetMask("ARNavWalls");
        }

        void Awake()
        {
            Graph = NavGraph.Build(map, stairsPenalty, elevatorPenalty);
            pathfinder = new AStarPathfinder(Graph);
            Debug.Log($"[Nav] Graph: {Graph.Count} nodes, {Graph.Destinations().Count} destinations");
        }

        void OnEnable() { if (aligner != null) aligner.Aligned += OnAligned; }
        void OnDisable() { if (aligner != null) aligner.Aligned -= OnAligned; }

        public List<NavNode> GetDestinations()
        {
            var list = new List<NavNode>();
            foreach (int i in Graph.Destinations()) list.Add(Graph.nodes[i]);
            list.Sort((a, b) => a.floorLevel != b.floorLevel ? a.floorLevel.CompareTo(b.floorLevel)
                                                              : string.CompareOrdinal(a.Label, b.Label));
            return list;
        }

        public void StartNavigation(string nodeId) => StartNavigation(Graph.nodes.Find(n => n.nodeId == nodeId));

        public void StartNavigation(NavNode destination)
        {
            if (destination == null) { Debug.LogWarning("[Nav] Unknown destination"); return; }
            Destination = destination;
            destIndex = Graph.IndexOf(destination);
            if (aligner != null && !aligner.IsAligned) { SetState(NavState.NeedsCalibration); Say("Scan the nearest KMUTT AR poster to start"); return; }
            Replan();
        }

        public void Cancel()
        {
            Destination = null; destIndex = -1; route = null;
            visualizer.Hide();
            SetState(NavState.Idle);
        }

        void OnAligned(int floor)
        {
            CurrentFloor = floor;
            if (Destination != null && State != NavState.Arrived) Replan();
        }

        Vector3 UserMap() => map.WorldToMap(arCamera.transform.position);

        int DetectFloor(Vector3 userMap)
        {
            var f = map.FloorAtHeight(userMap.y - eyeHeight + 0.4f);
            return f != null ? f.level : CurrentFloor;
        }

        void Replan()
        {
            if (destIndex < 0) return;
            Vector3 user = UserMap();
            CurrentFloor = DetectFloor(user);
            int start = Graph.NearestNode(user, CurrentFloor);
            if (start < 0) { SetState(NavState.NoRoute); Say($"No walkable nodes on floor {CurrentFloor}"); return; }

            route = pathfinder.FindPath(start, destIndex, options);
            if (!route.found)
            {
                visualizer.Hide();
                SetState(NavState.NoRoute);
                Say(options.avoidStairs ? "No step-free route found" : "No route found");
                return;
            }
            segmentIndex = 0;
            ShowSegment(user);
            SetState(NavState.Navigating);
        }

        void ShowSegment(Vector3 userMap)
        {
            var seg = route.segments[segmentIndex];
            var pts = new List<Vector3>(seg.mapPoints.Count + 1);
            // Start the line at the user's feet when they're on this segment's floor
            if (seg.floorLevel == CurrentFloor)
            {
                var floor = map.GetFloor(seg.floorLevel);
                float baseY = floor != null ? floor.baseY : seg.mapPoints[0].y;
                pts.Add(new Vector3(userMap.x, baseY, userMap.z));
            }
            pts.AddRange(seg.mapPoints);
            activePolyline = smoothPath ? PathSmoother.Smooth(pts, map.transform, wallMask) : pts;

            bool last = segmentIndex == route.segments.Count - 1;
            visualizer.ShowRoute(activePolyline, endIsTransition: !last);

            if (last) Say($"Head to {Destination.Label}");
            else Say($"Walk to the {(seg.exitTransition == EdgeKind.Elevator ? "elevator" : "stairs")}, then go to floor {seg.nextFloorLevel}");
        }

        void Update()
        {
            if (route == null || !route.found || arCamera == null) return;
            if (State != NavState.Navigating && State != NavState.FloorTransition) return;

            Vector3 user = UserMap();
            int floorNow = DetectFloor(user);
            var seg = route.segments[segmentIndex];
            bool last = segmentIndex == route.segments.Count - 1;

            // --- Floor change detection ---
            if (floorNow != CurrentFloor)
            {
                CurrentFloor = floorNow;
                if (!last && floorNow == seg.nextFloorLevel)
                {
                    // Re-plan from where we actually are on the new floor (robust to taking a different staircase)
                    Replan();
                    if (aligner != null && aligner.targets.Exists(t => t.floorLevel == floorNow))
                        Say($"Welcome to floor {floorNow}. Scan the floor {floorNow} poster to refine accuracy, or keep following the arrows");
                    return;
                }
                Replan();
                return;
            }

            visualizer.Tick(user);

            float s = Polyline.Project(activePolyline, user, out float offDist, out _);
            float remaining = Polyline.Length(activePolyline) - s;
            for (int i = segmentIndex + 1; i < route.segments.Count; i++) remaining += Polyline.Length(route.segments[i].mapPoints);
            onDistanceRemaining?.Invoke(remaining);

            Vector3 end = activePolyline[activePolyline.Count - 1];
            float toEnd = Vector2.Distance(new Vector2(user.x, user.z), new Vector2(end.x, end.z));

            if (last && toEnd < arriveRadius)
            {
                SetState(NavState.Arrived);
                Say($"You have arrived at {Destination.Label}");
                visualizer.Hide();
                return;
            }

            if (!last && toEnd < transitionReachRadius && State != NavState.FloorTransition)
            {
                SetState(NavState.FloorTransition);
                string how = seg.exitTransition == EdgeKind.Elevator ? "Take the elevator" : "Take the stairs";
                string dir = seg.nextFloorLevel > seg.floorLevel ? "up" : "down";
                Say($"{how} {dir} to floor {seg.nextFloorLevel}");
            }

            // --- Off-route re-planning ---
            if (State == NavState.Navigating && offDist > offRouteDistance)
            {
                offRouteTimer += Time.deltaTime;
                if (offRouteTimer > offRouteSeconds) { offRouteTimer = 0; Say("Re-routing…"); Replan(); }
            }
            else offRouteTimer = 0;
        }

        void SetState(NavState s)
        {
            if (State == s) return;
            State = s;
            onStateChanged?.Invoke(s);
        }

        void Say(string msg)
        {
            onInstruction?.Invoke(msg);
            Debug.Log($"[Nav] {msg}");
        }
    }
}
