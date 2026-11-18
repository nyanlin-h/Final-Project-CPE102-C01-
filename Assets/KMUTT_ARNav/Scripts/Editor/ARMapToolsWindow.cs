using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KMUTT.ARNav.EditorTools
{
    /// <summary>
    /// KMUTT > AR Map Tools. One window for the whole map pipeline:
    ///   1. Build Building Map      – walls/zones from Data/kmutt_cpe_floors.json (keeps your waypoints)
    ///   2. Generate Waypoint Grid  – auto nodes in free space inside rooms/corridors
    ///   3. Auto-Link               – line-of-sight edges between nearby nodes
    ///   4. Add destinations / stairs / elevators, manual door links
    ///   5. Validate                – reachability report
    ///   6. Create Marker Prefabs / Navigation System
    /// </summary>
    public class ARMapToolsWindow : EditorWindow
    {
        const string Root = "Assets/KMUTT_ARNav";
        const string GenDir = Root + "/Generated";
        public const string WallLayerName = "ARNavWalls";

        TextAsset mapJson;
        bool perimeterWalls = true;
        float perimeterThickness = 0.1f;
        float gridSpacing = 1.0f;
        float clearance = 0.35f;
        float autoLinkFactor = 1.5f;
        float handNodeLinkRadius = 3.0f;

        string newName = "Room 1001";
        int newFloor = 10;
        NavNodeType newTransitionType = NavNodeType.Stairs;
        string newGroup = "StairA";
        Vector2 scroll;

        [MenuItem("KMUTT/AR Map Tools", priority = 0)]
        static void Open() => GetWindow<ARMapToolsWindow>("KMUTT AR Map");

        void OnEnable()
        {
            if (mapJson == null) mapJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Data/kmutt_cpe_floors.json");
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var map = FindFirstObjectByType<BuildingMap>(FindObjectsInactive.Include);

            Header("1 · Building geometry");
            mapJson = (TextAsset)EditorGUILayout.ObjectField("Map JSON", mapJson, typeof(TextAsset), false);
            perimeterWalls = EditorGUILayout.Toggle(new GUIContent("Zone perimeter walls", "Room/corridor blocks in the source mesh become thin walls along their outline"), perimeterWalls);
            perimeterThickness = EditorGUILayout.Slider("Perimeter thickness", perimeterThickness, 0.05f, 0.3f);
            if (GUILayout.Button("Build / Rebuild Building Map", GUILayout.Height(28)) && mapJson) BuildMap(mapJson, perimeterWalls, perimeterThickness);

            using (new EditorGUI.DisabledScope(map == null))
            {
                Header("2 · Waypoints from floor plans (recommended)");
                if (GUILayout.Button("Import Plan Waypoints (rooms, stairs, lifts)", GUILayout.Height(28)) && mapJson) ImportPlanWaypoints(map, mapJson);

                Header("2b · Waypoints by grid (only if the JSON has no nodes)");
                gridSpacing = EditorGUILayout.Slider("Grid spacing (m)", gridSpacing, 0.5f, 2f);
                clearance = EditorGUILayout.Slider("Wall clearance (m)", clearance, 0.15f, 0.8f);
                autoLinkFactor = EditorGUILayout.Slider("Auto link radius × spacing", autoLinkFactor, 1.0f, 2.5f);
                handNodeLinkRadius = EditorGUILayout.Slider("Hand-placed node link radius", handNodeLinkRadius, 1f, 8f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Generate Grid")) GenerateGrid(map, gridSpacing, clearance);
                if (GUILayout.Button("Auto-Link (LOS)")) AutoLink(map, gridSpacing * autoLinkFactor, handNodeLinkRadius);
                if (GUILayout.Button("Clear Auto Nodes")) ClearAuto(map);
                EditorGUILayout.EndHorizontal();

                Header("3 · Destinations & floor transitions (placed at Scene view pivot)");
                newFloor = EditorGUILayout.IntPopup("Floor", newFloor, new[] { "Floor 10", "Floor 11" }, new[] { 10, 11 });
                newName = EditorGUILayout.TextField("Destination name", newName);
                if (GUILayout.Button("Add Destination")) AddDestination(map, newName, newFloor);
                EditorGUILayout.Space(4);
                newTransitionType = (NavNodeType)EditorGUILayout.EnumPopup("Transition type", newTransitionType);
                newGroup = EditorGUILayout.TextField("Transition group", newGroup);
                if (GUILayout.Button("Add Stairs/Elevator Pair (F10 + F11)")) AddTransitionPair(map, newTransitionType, newGroup);
                EditorGUILayout.HelpBox("Select two NavNodes (e.g. either side of a door) and use the buttons below. Manual links survive Auto-Link.", MessageType.None);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Link Selected")) LinkSelected(true);
                if (GUILayout.Button("Unlink Selected")) LinkSelected(false);
                EditorGUILayout.EndHorizontal();

                Header("4 · Check");
                if (GUILayout.Button("Validate Graph")) Validate(map);

                Header("5 · Scene setup");
                if (GUILayout.Button("Create Marker Prefabs")) CreateMarkerPrefabs();
                if (GUILayout.Button("Create Navigation System Object")) CreateNavSystem(map);
                if (map != null)
                {
                    bool dbg = EditorGUILayout.Toggle("Show walls (debug material)", map.ShowDebugWalls);
                    if (dbg != map.ShowDebugWalls) { Undo.RecordObject(map, "Toggle walls"); map.ShowDebugWalls = dbg; }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void Header(string t) { EditorGUILayout.Space(8); EditorGUILayout.LabelField(t, EditorStyles.boldLabel); }

        // ------------------------------------------------------------------ 1. Build map
        static void BuildMap(TextAsset json, bool perimeter, float thickness)
        {
            var data = MapData.FromJson(json.text);
            int layer = EnsureLayer(WallLayerName);
            EnsureFolder(GenDir);

            var occMat = GetOrCreateMaterial(GenDir + "/M_ARDepthMask.mat", Shader.Find("KMUTT/AR Depth Mask"), null);
            var dbgMat = GetOrCreateMaterial(GenDir + "/M_MapDebug.mat", Shader.Find("Universal Render Pipeline/Unlit"),
                                             m => MakeTransparent(m, new Color(1f, 0.55f, 0.1f, 0.35f)));

            var map = FindFirstObjectByType<BuildingMap>(FindObjectsInactive.Include);
            if (map == null)
            {
                var go = new GameObject("BuildingMap");
                Undo.RegisterCreatedObjectUndo(go, "Create BuildingMap");
                map = go.AddComponent<BuildingMap>();
            }
            Undo.RecordObject(map, "Build BuildingMap");
            map.occluderMaterial = occMat;
            map.debugMaterial = dbgMat;
            map.floors.Clear();

            foreach (var fd in data.floors)
            {
                Transform ft = map.transform.Find(fd.name);
                if (ft == null)
                {
                    ft = new GameObject(fd.name).transform;
                    Undo.RegisterCreatedObjectUndo(ft.gameObject, "Create floor");
                    ft.SetParent(map.transform, false);
                }
                ft.localPosition = new Vector3(0, fd.baseY, 0);
                ft.localRotation = Quaternion.identity;
                ft.localScale = Vector3.one;
                if (!ft.TryGetComponent(out FloorRoot floor)) floor = Undo.AddComponent<FloorRoot>(ft.gameObject);
                floor.level = fd.level; floor.baseY = fd.baseY; floor.storeyHeight = fd.height;

                DestroyChild(ft, "Walls");
                DestroyChild(ft, "Zones");

                // ---- Walls: one combined render mesh + one BoxCollider per box
                var boxes = new List<(Vector3 c, Vector3 s)>();
                foreach (var w in fd.walls) boxes.Add((w.center - Vector3.up * fd.baseY, w.size));
                if (perimeter)
                    foreach (var z in fd.zones) boxes.AddRange(Perimeter(z.center - Vector3.up * fd.baseY, z.size, thickness));

                var walls = new GameObject("Walls") { layer = layer };
                Undo.RegisterCreatedObjectUndo(walls, "Walls");
                walls.transform.SetParent(ft, false);
                GameObjectUtility.SetStaticEditorFlags(walls, 0); // moves with the AR anchor → must NOT be static
                foreach (var b in boxes)
                {
                    var bc = walls.AddComponent<BoxCollider>();
                    bc.center = b.c; bc.size = b.s;
                }
                var mesh = BuildBoxMesh(boxes, $"{fd.name}_Walls");
                mesh = SaveMesh(mesh, $"{GenDir}/{fd.name}_Walls.asset");
                walls.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = walls.AddComponent<MeshRenderer>();
                mr.sharedMaterial = occMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                floor.wallRenderer = mr;

                // ---- Zones: trigger volumes, named so you can rename them to real room names
                var zones = new GameObject("Zones") { layer = 2 /* Ignore Raycast */ };
                Undo.RegisterCreatedObjectUndo(zones, "Zones");
                zones.transform.SetParent(ft, false);
                foreach (var z in fd.zones)
                {
                    var zg = new GameObject(z.name) { layer = 2 };
                    zg.transform.SetParent(zones.transform, false);
                    zg.transform.localPosition = z.center - Vector3.up * fd.baseY;
                    var bc = zg.AddComponent<BoxCollider>();
                    bc.isTrigger = true; bc.size = z.size;
                    var mz = zg.AddComponent<MapZone>();
                    mz.displayName = z.name; mz.floorLevel = fd.level;
                }
                floor.zonesRoot = zones.transform;

                var wp = ft.Find("Waypoints");
                if (wp == null)
                {
                    wp = new GameObject("Waypoints").transform;
                    Undo.RegisterCreatedObjectUndo(wp.gameObject, "Waypoints");
                    wp.SetParent(ft, false);
                }
                floor.waypointsRoot = wp;

                // Calibration marker holder (put poster markers here)
                if (ft.Find("Calibration") == null)
                {
                    var cal = new GameObject("Calibration");
                    Undo.RegisterCreatedObjectUndo(cal, "Calibration");
                    cal.transform.SetParent(ft, false);
                }

                EditorUtility.SetDirty(floor);
                map.floors.Add(floor);
                Debug.Log($"[KMUTT] {fd.name}: {fd.walls.Count} wall boxes, {fd.zones.Count} zones" + (perimeter ? $" (+{fd.zones.Count * 4} perimeter walls)" : ""));
            }
            map.floors.Sort((a, b) => a.level.CompareTo(b.level));
            map.ShowDebugWalls = true; // visible while authoring; turn off for device builds
            EditorUtility.SetDirty(map);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Selection.activeGameObject = map.gameObject;
        }

        static IEnumerable<(Vector3, Vector3)> Perimeter(Vector3 c, Vector3 s, float t)
        {
            float hx = s.x / 2, hz = s.z / 2;
            yield return (c + new Vector3(0, 0, -hz + t / 2), new Vector3(s.x, s.y, t));
            yield return (c + new Vector3(0, 0, hz - t / 2), new Vector3(s.x, s.y, t));
            yield return (c + new Vector3(-hx + t / 2, 0, 0), new Vector3(t, s.y, s.z));
            yield return (c + new Vector3(hx - t / 2, 0, 0), new Vector3(t, s.y, s.z));
        }

        // Faces: (normal, u, v) with Cross(v,u) == normal → triangles (0,1,2),(0,2,3) are clockwise from outside (Unity front face)
        static readonly Vector3[,] Faces =
        {
            { Vector3.right,   Vector3.forward, Vector3.up },
            { Vector3.left,    Vector3.up,      Vector3.forward },
            { Vector3.up,      Vector3.right,   Vector3.forward },
            { Vector3.down,    Vector3.forward, Vector3.right },
            { Vector3.forward, Vector3.up,      Vector3.right },
            { Vector3.back,    Vector3.right,   Vector3.up },
        };

        static Mesh BuildBoxMesh(List<(Vector3 c, Vector3 s)> boxes, string name)
        {
            var v = new List<Vector3>(boxes.Count * 24);
            var nrm = new List<Vector3>(boxes.Count * 24);
            var tri = new List<int>(boxes.Count * 36);
            foreach (var (c, s) in boxes)
            {
                Vector3 h = s * 0.5f;
                for (int f = 0; f < 6; f++)
                {
                    Vector3 n = Faces[f, 0], u = Vector3.Scale(Faces[f, 1], h), w = Vector3.Scale(Faces[f, 2], h);
                    Vector3 o = c + Vector3.Scale(n, h);
                    int k = v.Count;
                    v.Add(o - u - w); v.Add(o - u + w); v.Add(o + u + w); v.Add(o + u - w);
                    for (int i = 0; i < 4; i++) nrm.Add(n);
                    tri.Add(k); tri.Add(k + 1); tri.Add(k + 2);
                    tri.Add(k); tri.Add(k + 2); tri.Add(k + 3);
                }
            }
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ 2. Waypoints
        static void GenerateGrid(BuildingMap map, float spacing, float clearance)
        {
            if (!CheckMapPose(map)) return;
            int mask = LayerMask.GetMask(WallLayerName);
            Physics.SyncTransforms();
            int total = 0;
            foreach (var floor in map.floors)
            {
                ClearAuto(floor);
                var zones = floor.zonesRoot != null ? floor.zonesRoot.GetComponentsInChildren<BoxCollider>() : new BoxCollider[0];
                if (zones.Length == 0) { Debug.LogWarning($"[KMUTT] {floor.name} has no zones; skipping grid."); continue; }

                Bounds all = zones[0].bounds;
                foreach (var z in zones) all.Encapsulate(z.bounds);
                Vector3 up = floor.transform.up;
                int count = 0;
                for (float x = all.min.x + spacing * 0.5f; x < all.max.x; x += spacing)
                for (float z = all.min.z + spacing * 0.5f; z < all.max.z; z += spacing)
                {
                    Vector3 p = new Vector3(x, floor.transform.position.y, z);
                    bool inside = false;
                    foreach (var zc in zones) if (zc.bounds.Contains(p + up * 1f)) { inside = true; break; }
                    if (!inside) continue;
                    if (Physics.CheckCapsule(p + up * 0.3f, p + up * 1.7f, clearance, mask, QueryTriggerInteraction.Ignore)) continue;

                    var go = new GameObject($"wp_{floor.level}_{x:0.0}_{z:0.0}");
                    go.transform.SetParent(floor.waypointsRoot, false);
                    go.transform.position = p;
                    var n = go.AddComponent<NavNode>();
                    n.autoGenerated = true; n.floorLevel = floor.level;
                    Undo.RegisterCreatedObjectUndo(go, "Generate waypoint");
                    count++;
                }
                total += count;
                Debug.Log($"[KMUTT] {floor.name}: {count} auto waypoints");
            }
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            if (total > 0) Debug.Log("[KMUTT] Now press Auto-Link (LOS).");
        }

        /// <summary>
        /// Creates NavNodes + links traced from the floor plans (JSON "nodes"/"links").
        /// Removes old auto waypoints and any existing node with the same nodeId, so it can be re-run safely.
        /// Imported links are stored as manualLinks so Auto-Link never deletes them.
        /// </summary>
        static void ImportPlanWaypoints(BuildingMap map, TextAsset json)
        {
            if (!CheckMapPose(map)) return;
            var data = MapData.FromJson(json.text);
            if (data.nodes == null || data.nodes.Count == 0) { EditorUtility.DisplayDialog("No nodes", "This JSON has no plan waypoints. Use Generate Grid instead.", "OK"); return; }

            var ids = new HashSet<string>();
            foreach (var n in data.nodes) if (!string.IsNullOrEmpty(n.nodeId)) ids.Add(n.nodeId);
            foreach (var f in map.floors) ClearAuto(f);
            foreach (var existing in map.GetComponentsInChildren<NavNode>(true))
                if (!string.IsNullOrEmpty(existing.nodeId) && ids.Contains(existing.nodeId)) Undo.DestroyObjectImmediate(existing.gameObject);

            var created = new NavNode[data.nodes.Count];
            for (int i = 0; i < data.nodes.Count; i++)
            {
                var d = data.nodes[i];
                var floor = map.GetFloor(d.floor);
                if (floor == null) { Debug.LogWarning($"[KMUTT] Floor {d.floor} missing for node {i}"); continue; }
                string goName = d.auto ? $"wp_{d.floor}_{i}" : (!string.IsNullOrEmpty(d.nodeId) ? d.nodeId : $"node_{i}");
                var go = new GameObject(goName);
                Undo.RegisterCreatedObjectUndo(go, "Import waypoint");
                go.transform.SetParent(floor.waypointsRoot, false);
                go.transform.position = map.transform.TransformPoint(d.position);
                var n = go.AddComponent<NavNode>();
                n.floorLevel = d.floor;
                n.autoGenerated = d.auto;
                n.nodeId = d.nodeId;
                n.displayName = d.displayName;
                n.isDestination = d.isDestination;
                n.transitionGroup = d.transitionGroup;
                n.type = System.Enum.TryParse(d.type, out NavNodeType t) ? t : NavNodeType.Waypoint;
                created[i] = n;
            }
            int linkCount = 0;
            foreach (var l in data.links)
            {
                if (l.a < 0 || l.b < 0 || l.a >= created.Length || l.b >= created.Length) continue;
                NavNode a = created[l.a], b = created[l.b];
                if (a == null || b == null || a == b) continue;
                if (!a.manualLinks.Contains(b)) a.manualLinks.Add(b);
                if (!b.manualLinks.Contains(a)) b.manualLinks.Add(a);
                linkCount++;
            }
            foreach (var n in created) if (n) EditorUtility.SetDirty(n);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log($"[KMUTT] Imported {created.Length} plan nodes and {linkCount} links. Run Validate Graph next (no Auto-Link needed).");
        }

        static void AutoLink(BuildingMap map, float autoRadius, float handRadius)
        {
            if (!CheckMapPose(map)) return;
            int mask = LayerMask.GetMask(WallLayerName);
            Physics.SyncTransforms();
            var nodes = map.GetComponentsInChildren<NavNode>(true);
            foreach (var n in nodes) { Undo.RecordObject(n, "Auto-link"); n.links.Clear(); }

            int edges = 0;
            for (int i = 0; i < nodes.Length; i++)
            for (int j = i + 1; j < nodes.Length; j++)
            {
                NavNode a = nodes[i], b = nodes[j];
                if (a.floorLevel != b.floorLevel) continue;
                float r = (a.autoGenerated && b.autoGenerated) ? autoRadius : handRadius;
                Vector3 pa = a.transform.position, pb = b.transform.position;
                if (Mathf.Abs(pa.y - pb.y) > 0.5f || Vector3.Distance(pa, pb) > r) continue;
                if (!LineOfSight(pa, pb, mask)) continue;
                a.links.Add(b); b.links.Add(a); edges++;
            }
            foreach (var n in nodes) EditorUtility.SetDirty(n);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log($"[KMUTT] Auto-linked {edges} edges across {nodes.Length} nodes. Run Validate Graph next.");
        }

        static bool LineOfSight(Vector3 a, Vector3 b, int mask, float probeRadius = 0.12f, float height = 1.0f)
        {
            Vector3 d = b - a; float len = d.magnitude;
            if (len < 1e-3f) return true;
            return !Physics.SphereCast(a + Vector3.up * height, probeRadius, d / len, out _, len, mask, QueryTriggerInteraction.Ignore);
        }

        static void ClearAuto(BuildingMap map) { foreach (var f in map.floors) ClearAuto(f); EditorSceneManager.MarkSceneDirty(map.gameObject.scene); }

        static void ClearAuto(FloorRoot floor)
        {
            if (floor.waypointsRoot == null) return;
            var autos = new List<NavNode>();
            foreach (var n in floor.waypointsRoot.GetComponentsInChildren<NavNode>(true)) if (n.autoGenerated) autos.Add(n);
            // drop dangling references held by hand-placed nodes
            foreach (var n in floor.waypointsRoot.GetComponentsInChildren<NavNode>(true))
                if (!n.autoGenerated) { Undo.RecordObject(n, "Clear auto"); n.links.RemoveAll(x => x == null || x.autoGenerated); n.manualLinks.RemoveAll(x => x == null || x.autoGenerated); }
            foreach (var n in autos) Undo.DestroyObjectImmediate(n.gameObject);
        }

        // ------------------------------------------------------------------ 3. Authoring helpers
        static Vector3 PivotOnFloor(FloorRoot floor)
        {
            var sv = SceneView.lastActiveSceneView;
            Vector3 p = sv != null ? sv.pivot : Vector3.zero;
            p.y = floor.transform.position.y;
            return p;
        }

        static void AddDestination(BuildingMap map, string name, int level)
        {
            var floor = map.GetFloor(level);
            if (floor == null) { Debug.LogError($"Floor {level} not found"); return; }
            var go = new GameObject($"DEST_{name}");
            Undo.RegisterCreatedObjectUndo(go, "Add destination");
            go.transform.SetParent(floor.waypointsRoot, false);
            go.transform.position = PivotOnFloor(floor);
            var n = go.AddComponent<NavNode>();
            n.type = NavNodeType.Room; n.isDestination = true; n.floorLevel = level;
            n.displayName = name; n.nodeId = $"F{level}_{Sanitize(name)}";
            Selection.activeGameObject = go;
            Debug.Log($"[KMUTT] Added destination {n.nodeId}. Move it to the room door, then Auto-Link.");
        }

        static void AddTransitionPair(BuildingMap map, NavNodeType type, string group)
        {
            if (type != NavNodeType.Stairs && type != NavNodeType.Elevator) { Debug.LogError("Type must be Stairs or Elevator"); return; }
            var created = new List<Object>();
            foreach (var floor in map.floors)
            {
                var go = new GameObject($"{type}_{group}_F{floor.level}");
                Undo.RegisterCreatedObjectUndo(go, "Add transition");
                go.transform.SetParent(floor.waypointsRoot, false);
                go.transform.position = PivotOnFloor(floor);
                var n = go.AddComponent<NavNode>();
                n.type = type; n.transitionGroup = group; n.floorLevel = floor.level;
                n.displayName = $"{type} {group}"; n.nodeId = $"F{floor.level}_{Sanitize(type + "_" + group)}";
                created.Add(go);
            }
            Selection.objects = created.ToArray();
            Debug.Log($"[KMUTT] Added {type} pair '{group}'. Move each one onto the real landing/door on its floor (they may differ in X/Z), then Auto-Link.");
        }

        static void LinkSelected(bool link)
        {
            var sel = new List<NavNode>();
            foreach (var go in Selection.gameObjects) if (go.TryGetComponent(out NavNode n)) sel.Add(n);
            if (sel.Count < 2) { Debug.LogWarning("Select at least two NavNodes"); return; }
            for (int i = 0; i < sel.Count - 1; i++)
            {
                NavNode a = sel[i], b = sel[i + 1];
                Undo.RecordObject(a, "Link"); Undo.RecordObject(b, "Link");
                if (link)
                {
                    if (!a.manualLinks.Contains(b)) a.manualLinks.Add(b);
                    if (!b.manualLinks.Contains(a)) b.manualLinks.Add(a);
                }
                else
                {
                    a.manualLinks.Remove(b); b.manualLinks.Remove(a);
                    a.links.Remove(b); b.links.Remove(a);
                }
                EditorUtility.SetDirty(a); EditorUtility.SetDirty(b);
            }
        }

        // ------------------------------------------------------------------ 4. Validate
        static void Validate(BuildingMap map)
        {
            var g = NavGraph.Build(map);
            var comp = g.Components(out int count);
            var sizes = new int[count];
            foreach (var c in comp) sizes[c]++;
            int main = 0; for (int i = 1; i < count; i++) if (sizes[i] > sizes[main]) main = i;

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Nodes: {g.Count}   Connected components: {count}   Largest: {sizes[main]}");
            int isolated = 0;
            for (int i = 0; i < g.Count; i++) if (g.adjacency[i].Count == 0) isolated++;
            report.AppendLine($"Isolated nodes: {isolated}");

            foreach (int d in g.Destinations())
                if (comp[d] != main) report.AppendLine($"  ✗ Destination '{g.nodes[d].Label}' is NOT reachable from the main network (add a door link)");

            var groups = new Dictionary<string, HashSet<int>>();
            for (int i = 0; i < g.Count; i++)
                if (g.nodes[i].IsTransition)
                {
                    if (!groups.TryGetValue(g.nodes[i].transitionGroup, out var set)) groups[g.nodes[i].transitionGroup] = set = new HashSet<int>();
                    set.Add(g.floorOf[i]);
                }
            if (groups.Count == 0) report.AppendLine("  ✗ No stairs/elevator pairs: Floor 10 and 11 are disconnected");
            foreach (var kv in groups)
                if (kv.Value.Count < 2) report.AppendLine($"  ✗ Transition group '{kv.Key}' exists on only one floor");

            for (int c = 0; c < count; c++)
                if (c != main && sizes[c] > 3)
                {
                    int sample = System.Array.IndexOf(comp, c);
                    report.AppendLine($"  • Island of {sizes[c]} nodes near '{g.nodes[sample].name}' (floor {g.floorOf[sample]}) – probably a room needing a door link");
                }
            Debug.Log("[KMUTT] Graph validation\n" + report);
            EditorUtility.DisplayDialog("Graph validation", report.ToString(), "OK");
        }

        // ------------------------------------------------------------------ 5. Scene setup
        static void CreateMarkerPrefabs()
        {
            string dir = GenDir + "/Markers";
            EnsureFolder(dir);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var arrowMat = GetOrCreateMaterial(dir + "/M_Arrow.mat", unlit, m => MakeOpaque(m, new Color(0f, 0.85f, 1f)));
            var pinMat = GetOrCreateMaterial(dir + "/M_Destination.mat", unlit, m => MakeOpaque(m, new Color(1f, 0.25f, 0.2f)));
            var transMat = GetOrCreateMaterial(dir + "/M_Transition.mat", unlit, m => MakeOpaque(m, new Color(0.85f, 0.2f, 1f)));

            var arrowMesh = SaveMesh(ProceduralMarkers.CreateChevron(), dir + "/Mesh_Chevron.asset");
            var pinMesh = SaveMesh(ProceduralMarkers.CreatePin(), dir + "/Mesh_Pin.asset");
            var ringMesh = SaveMesh(ProceduralMarkers.CreateRing(), dir + "/Mesh_Ring.asset");

            var arrow = MakePrefab(dir + "/NavArrow.prefab", ("Chevron", arrowMesh, arrowMat, Vector3.zero, 1f));
            var dest = MakePrefab(dir + "/NavDestination.prefab", ("Pin", pinMesh, pinMat, Vector3.zero, 1f), ("Ring", ringMesh, pinMat, Vector3.up * 0.01f, 1f));
            var trans = MakePrefab(dir + "/NavTransition.prefab", ("Pin", pinMesh, transMat, Vector3.zero, 1.3f), ("Ring", ringMesh, transMat, Vector3.up * 0.01f, 1.6f));

            var vis = FindFirstObjectByType<ARPathVisualizer>(FindObjectsInactive.Include);
            if (vis != null)
            {
                Undo.RecordObject(vis, "Assign markers");
                vis.arrowPrefab = arrow; vis.destinationPrefab = dest; vis.transitionPrefab = trans;
                EditorUtility.SetDirty(vis);
            }
            Debug.Log($"[KMUTT] Marker prefabs created in {dir}" + (vis ? " and assigned to ARPathVisualizer." : "."));
        }

        static GameObject MakePrefab(string path, params (string name, Mesh mesh, Material mat, Vector3 offset, float scale)[] parts)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            foreach (var p in parts)
            {
                var child = new GameObject(p.name);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = p.offset;
                child.transform.localScale = Vector3.one * p.scale;
                child.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterial = p.mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            return prefab;
        }

        static void CreateNavSystem(BuildingMap map)
        {
            var go = new GameObject("NavigationSystem");
            Undo.RegisterCreatedObjectUndo(go, "Create NavigationSystem");
            var aligner = go.AddComponent<MapAligner>();
            var vis = go.AddComponent<ARPathVisualizer>();
            var nav = go.AddComponent<NavigationController>();

            aligner.map = map;
            aligner.imageManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARTrackedImageManager>(FindObjectsInactive.Include);
            aligner.planeManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARPlaneManager>(FindObjectsInactive.Include);
            aligner.arCamera = Camera.main;
            vis.map = map;
            nav.map = map; nav.aligner = aligner; nav.visualizer = vis; nav.arCamera = Camera.main;
            nav.wallMask = LayerMask.GetMask(WallLayerName);

            // One calibration target per floor, pre-wired to an empty marker you then move onto the poster position
            foreach (var floor in map.floors)
            {
                var cal = floor.transform.Find("Calibration");
                var marker = new GameObject($"Poster_F{floor.level}_A").transform;
                Undo.RegisterCreatedObjectUndo(marker.gameObject, "Poster marker");
                marker.SetParent(cal != null ? cal : floor.transform, false);
                marker.localPosition = new Vector3(0, 1.5f, 0);
                // Default: poster on a wall facing -Z (normal +Y of the marker points toward the viewer)
                marker.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                aligner.targets.Add(new CalibrationTarget { referenceImageName = $"KMUTT_F{floor.level}_A", markerInMap = marker, floorLevel = floor.level });
            }

            string dir = GenDir + "/Markers";
            vis.arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/NavArrow.prefab");
            vis.destinationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/NavDestination.prefab");
            vis.transitionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/NavTransition.prefab");

            if (aligner.imageManager == null) Debug.LogWarning("[KMUTT] No ARTrackedImageManager found. Add it to XR Origin, then assign it on MapAligner.");
            if (Camera.main == null) Debug.LogWarning("[KMUTT] No camera tagged MainCamera. Tag the AR Camera under XR Origin as MainCamera.");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // ------------------------------------------------------------------ utilities
        static bool CheckMapPose(BuildingMap map)
        {
            var t = map.transform;
            if (t.position != Vector3.zero || t.rotation != Quaternion.identity || t.lossyScale != Vector3.one)
            {
                if (!EditorUtility.DisplayDialog("BuildingMap is moved", "Author waypoints with BuildingMap at position (0,0,0), rotation 0, scale 1. Reset it now?", "Reset", "Cancel")) return false;
                Undo.RecordObject(t, "Reset map");
                t.position = Vector3.zero; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
            }
            return true;
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToUpperInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        static void DestroyChild(Transform parent, string name)
        {
            var c = parent.Find(name);
            if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
        }

        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue))
                {
                    p.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"[KMUTT] Created layer '{name}' at index {i}");
                    return i;
                }
            }
            Debug.LogError("[KMUTT] No free user layer for " + name);
            return 0;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material GetOrCreateMaterial(string path, Shader shader, System.Action<Material> setup)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                if (shader == null) { Debug.LogError($"[KMUTT] Shader missing for {path}. Is URP installed and active?"); return null; }
                m = new Material(shader);
                setup?.Invoke(m);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static void MakeOpaque(Material m, Color c)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Cull", (float)CullMode.Off);   // render both faces
            m.enableInstancing = true;
        }

        static void MakeTransparent(Material m, Color c)
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
