#if UNITY_EDITOR   // Editor-only dev tool: never compiled into phone builds.
using System.Collections.Generic;
using KMUTT.ARNav;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KMUTT.DevTools
{
    /// <summary>
    /// Walk the digital twin in Play mode without a phone or the building.
    /// Replaces AR tracking with a first-person camera, skips poster scanning (uses MapAligner's manual
    /// "stand on a node" alignment), and drives the real NavigationController + ARPathVisualizer.
    ///
    /// Spawned automatically when entering Play mode (toggle: KMUTT ▸ Dev ▸ Walkthrough Simulator).
    /// Nothing is saved into the scene.
    /// </summary>
    public class WalkthroughSimulator : MonoBehaviour
    {
        public const string PrefKey = "KMUTT.DevTools.WalkthroughSimulator";
        const string DefaultStartNode = "F10_LIFTE";

        [Header("Movement")]
        public float walkSpeed = 1.4f;
        public float runSpeed = 4f;
        public float flySpeed = 8f;
        public float lookSensitivity = 0.15f;
        public float bodyRadius = 0.25f;

        [Header("Labels")]
        public float labelRange = 30f;

        NavigationController nav;
        MapAligner aligner;
        BuildingMap map;
        Camera cam;
        CharacterController body;

        float yaw, pitch;
        bool fly;
        int floor = 10;

        string instruction = "Pick a destination and press Go";
        NavState state = NavState.Idle;
        float distance = -1f;
        int selected = -1;
        bool stepFree;
        bool showTransitions;
        Vector2 scroll;

        List<NavNode> destinations;
        readonly List<NavNode> transitions = new List<NavNode>();
        NavNode nearTransition;
        GUIStyle labelStyle, promptStyle, wrapStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!UnityEditor.EditorPrefs.GetBool(PrefKey, true)) return;
            if (FindFirstObjectByType<NavigationController>() == null) return;
            new GameObject("[WalkthroughSimulator]").AddComponent<WalkthroughSimulator>();
        }

        void Start()
        {
            nav = FindFirstObjectByType<NavigationController>();
            aligner = nav.aligner;
            map = nav.map;
            cam = nav.arCamera != null ? nav.arCamera : Camera.main;

            DisableARCameraDrivers();
            SetUpVisuals();

            body = cam.gameObject.AddComponent<CharacterController>();
            body.radius = bodyRadius;
            body.height = EyeHeight + 0.2f;
            body.center = new Vector3(0f, -EyeHeight / 2f + 0.1f, 0f);
            body.stepOffset = 0f;
            body.skinWidth = 0.02f;

            destinations = nav.GetDestinations();
            foreach (var n in nav.Graph.nodes) if (n.IsTransition) transitions.Add(n);
            transitions.Sort((a, b) => a.floorLevel != b.floorLevel ? a.floorLevel.CompareTo(b.floorLevel) : string.CompareOrdinal(a.Label, b.Label));

            nav.onInstruction.AddListener(s => instruction = s);
            nav.onStateChanged.AddListener(s => { state = s; if (s != NavState.Navigating && s != NavState.FloorTransition) distance = -1f; });
            nav.onDistanceRemaining.AddListener(d => distance = d);

            var start = nav.Graph.nodes.Find(n => n.nodeId == DefaultStartNode) ?? destinations[0];
            TeleportTo(start);
            // Same call the "Align Here" debug button uses on the phone: map space == world space afterwards.
            if (aligner != null) aligner.AlignStandingOnNode(start);
            Debug.Log("[WalkthroughSimulator] Ready. Hold right mouse to look, WASD to walk, E/Q at stairs or lifts.");
        }

        float EyeHeight => nav != null ? nav.eyeHeight : 1.4f;

        void DisableARCameraDrivers()
        {
            // TrackedPoseDriver would overwrite our pose; ARCameraBackground has no camera feed in the Editor.
            foreach (var b in cam.GetComponents<Behaviour>())
            {
                string t = b.GetType().Name;
                if (t == "TrackedPoseDriver" || t == "ARCameraBackground") b.enabled = false;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            cam.nearClipPlane = 0.05f;
        }

        void SetUpVisuals()
        {
            // The real walls use an invisible depth-mask material (they only hide arrows on the phone).
            // Give them a solid, lit look for this session only; nothing is saved to the scene.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var wallMat = new Material(lit) { color = new Color(0.82f, 0.80f, 0.76f) };
                foreach (var f in map.floors)
                    if (f != null && f.wallRenderer != null) f.wallRenderer.sharedMaterial = wallMat;
            }
            else map.ShowDebugWalls = true;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;
            var checker = new Texture2D(2, 2) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            Color a = new Color(0.30f, 0.32f, 0.36f), b = new Color(0.26f, 0.28f, 0.32f);
            checker.SetPixels(new[] { a, b, b, a });
            checker.Apply();

            foreach (var f in map.floors)
            {
                if (f == null || f.wallRenderer == null) continue;
                var bounds = f.wallRenderer.bounds;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ground.name = "[Sim] Ground";
                Destroy(ground.GetComponent<Collider>());
                ground.transform.SetParent(f.transform, true);
                ground.transform.position = new Vector3(bounds.center.x, map.transform.TransformPoint(0, f.baseY, 0).y - 0.01f, bounds.center.z);
                ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localScale = new Vector3(bounds.size.x + 4f, bounds.size.z + 4f, 1f);
                var mat = new Material(shader) { mainTexture = checker };
                mat.mainTextureScale = new Vector2((bounds.size.x + 4f) / 2f, (bounds.size.z + 4f) / 2f);   // 1 m squares
                ground.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        // ───────────────────────── movement ─────────────────────────

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            }
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (kb.fKey.wasPressedThisFrame) ToggleFly();

            Vector3 input = Vector3.zero;
            if (kb.wKey.isPressed) input.z += 1;
            if (kb.sKey.isPressed) input.z -= 1;
            if (kb.dKey.isPressed) input.x += 1;
            if (kb.aKey.isPressed) input.x -= 1;
            input = Vector3.ClampMagnitude(input, 1f);

            if (fly)
            {
                if (kb.spaceKey.isPressed) input.y += 1;
                if (kb.leftCtrlKey.isPressed || kb.cKey.isPressed) input.y -= 1;
                cam.transform.position += cam.transform.rotation * new Vector3(input.x, 0, input.z) * (flySpeed * Time.deltaTime)
                                          + Vector3.up * (input.y * flySpeed * Time.deltaTime);
                return;
            }

            float speed = kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
            Vector3 move = Quaternion.Euler(0f, yaw, 0f) * input * (speed * Time.deltaTime);
            body.Move(move);
            SnapToFloor(floor);

            nearTransition = FindNearTransition();
            if (nearTransition != null)
            {
                if (kb.eKey.wasPressedThisFrame) TakeTransition(+1);
                if (kb.qKey.wasPressedThisFrame) TakeTransition(-1);
            }
        }

        void ToggleFly()
        {
            fly = !fly;
            body.enabled = !fly;
            if (!fly)
            {
                // Land on the floor closest below the camera
                var f = map.FloorAtHeight(map.WorldToMap(cam.transform.position).y - EyeHeight + 0.4f);
                floor = f != null ? f.level : floor;
                SnapToFloor(floor);
            }
        }

        void SnapToFloor(int level)
        {
            var f = map.GetFloor(level);
            if (f == null) return;
            Vector3 mapPos = map.WorldToMap(cam.transform.position);
            mapPos.y = f.baseY + EyeHeight;
            SetCameraPosition(map.MapToWorld(mapPos));
        }

        void SetCameraPosition(Vector3 world)
        {
            bool was = body != null && body.enabled;
            if (was) body.enabled = false;   // CharacterController ignores direct transform writes while enabled
            cam.transform.position = world;
            if (was) body.enabled = true;
        }

        NavNode FindNearTransition()
        {
            Vector3 p = cam.transform.position;
            NavNode best = null; float bestD = 2.0f;
            foreach (var t in transitions)
            {
                if (t.floorLevel != floor) continue;
                Vector3 q = t.transform.position;
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        NavNode Counterpart(NavNode t, int dir)
            => transitions.Find(o => o.transitionGroup == t.transitionGroup && o.floorLevel == t.floorLevel + dir);

        void TakeTransition(int dir)
        {
            var target = Counterpart(nearTransition, dir);
            if (target == null) return;
            TeleportTo(target, keepYaw: true);
        }

        void TeleportTo(NavNode node, bool keepYaw = false)
        {
            if (fly) ToggleFly();
            floor = node.floorLevel;
            SetCameraPosition(node.transform.position + Vector3.up * EyeHeight);
            if (!keepYaw)
            {
                Vector3 f = node.transform.forward; f.y = 0;
                yaw = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f).eulerAngles.y : 0f;
                pitch = 10f;
                cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        // ───────────────────────── UI ─────────────────────────

        void OnGUI()
        {
            if (nav == null) return;
            EnsureStyles();
            DrawLabels();
            DrawPanel();
            DrawPrompt();
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(6, 6, 3, 3) };
            labelStyle.normal.background = Texture2D.whiteTexture;   // tinted per label via GUI.backgroundColor
            labelStyle.normal.textColor = Color.white;
            promptStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 8, 8) };
            promptStyle.normal.textColor = Color.white;
            wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13 };
        }

        void DrawLabels()
        {
            Vector3 eye = cam.transform.position;
            DrawLabelSet(destinations, eye, new Color(0.2f, 0.85f, 0.4f));
            DrawLabelSet(transitions, eye, new Color(0.85f, 0.4f, 0.95f));
        }

        void DrawLabelSet(List<NavNode> nodes, Vector3 eye, Color tint)
        {
            foreach (var n in nodes)
            {
                if (!fly && n.floorLevel != floor) continue;
                Vector3 p = n.transform.position + Vector3.up * 2.1f;
                float dist = Vector3.Distance(eye, p);
                if (dist > labelRange && !fly) continue;
                Vector3 s = cam.WorldToScreenPoint(p);
                if (s.z <= 0) continue;
                bool hidden = Physics.Linecast(eye, p, nav.wallMask);
                string text = n == nav.Destination ? "★ " + n.Label : n.Label;
                Vector2 size = labelStyle.CalcSize(new GUIContent(text));
                var rect = new Rect(s.x - size.x / 2, Screen.height - s.y - size.y / 2, size.x, size.y);
                // Solid tinted plate + white text; rooms behind walls are dimmed but still readable.
                var oldBg = GUI.backgroundColor; var oldContent = GUI.contentColor;
                GUI.backgroundColor = new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f, hidden ? 0.55f : 0.9f);
                GUI.contentColor = new Color(1f, 1f, 1f, hidden ? 0.75f : 1f);
                GUI.Box(rect, text, labelStyle);
                GUI.backgroundColor = oldBg; GUI.contentColor = oldContent;
            }
        }

        void DrawPanel()
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("<b>Walkthrough Simulator</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 });
            GUILayout.Label($"Floor {floor}{(fly ? " (flying)" : "")} · {state}" + (distance >= 0 ? $" · {distance:0} m left" : ""));
            GUILayout.Label(instruction, wrapStyle);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            stepFree = GUILayout.Toggle(stepFree, " Step-free (no stairs)");
            if (GUILayout.Button("Stop", GUILayout.Width(60))) { nav.Cancel(); selected = -1; }
            GUILayout.EndHorizontal();

            GUILayout.Label("Destinations   (Go = navigate, TP = teleport)");
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < destinations.Count; i++)
            {
                var d = destinations[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{(i == selected ? "▶ " : "")}F{d.floorLevel} · {d.Label}", GUILayout.Width(280));
                if (GUILayout.Button("Go", GUILayout.Width(36)))
                {
                    selected = i;
                    nav.options.avoidStairs = stepFree;
                    nav.StartNavigation(d);
                }
                if (GUILayout.Button("TP", GUILayout.Width(36))) TeleportTo(d);
                GUILayout.EndHorizontal();
            }
            showTransitions = GUILayout.Toggle(showTransitions, " Stairs & lifts (teleport)");
            if (showTransitions)
                foreach (var t in transitions)
                    if (GUILayout.Button($"F{t.floorLevel} · {t.Label}")) TeleportTo(t);
            GUILayout.EndScrollView();

            GUILayout.Label("Right mouse: look · WASD: walk · Shift: run\nE / Q: up / down at stairs or lift\nF: fly mode (Space / C: up / down)", wrapStyle);
            GUILayout.EndArea();
        }

        void DrawPrompt()
        {
            if (fly || nearTransition == null) return;
            var up = Counterpart(nearTransition, +1);
            var down = Counterpart(nearTransition, -1);
            if (up == null && down == null) return;
            string verb = nearTransition.type == NavNodeType.Elevator ? "elevator" : "stairs";
            string text = $"{nearTransition.Label}:  " +
                          (up != null ? $"[E] {verb} up to floor {up.floorLevel}   " : "") +
                          (down != null ? $"[Q] {verb} down to floor {down.floorLevel}" : "");
            Vector2 size = promptStyle.CalcSize(new GUIContent(text));
            GUI.Box(new Rect((Screen.width - size.x) / 2, Screen.height - size.y - 30, size.x, size.y), text, promptStyle);
        }
    }
}
#endif
