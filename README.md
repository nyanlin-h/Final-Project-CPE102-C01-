# KMUTT CPE AR Navigation: Project Walkthrough (Map & Navigation)

> **Last updated:** 18 Nov 2026

---

## 1. Project Summary

An AR app (Unity + AR Foundation, Android + iOS) that guides people through **Floor 10 and Floor 11** of the CPE building.
The user scans a poster on the wall, picks a room, and follows 3D arrows drawn over the camera view. Routes work across floors
via stairs or lifts. My part is the **digital map of the building** and the **navigation system** that runs on top of it.

---

## 2. Current status

| Area | Status | Notes |
|---|---|---|
| Building geometry (walls), both floors | ✅ Done | Traced from the official plans |
| Real-world scale | ✅ Done | Measured on the 1:450 fire-evacuation poster (see §5) |
| Floors aligned on top of each other | ✅ Done | Matched via stair and lift positions |
| Walkable network (1,505 nodes) | ✅ Done | All nodes connected, nothing isolated |
| Stairs (4) and lift banks (2), linked between floors | ✅ Done | `StairNW`, `StairW`, `StairE`, `StairSE`, `LiftW`, `LiftE` |
| Destinations (20 rooms) | ✅ Done | 10 on Floor 11, 10 on Floor 10, all verified on site (see §6) |
| A* pathfinding, including multi-floor and step-free mode | ✅ Code done | Tested offline; needs a device test |
| Poster calibration, AR markers, occlusion | ✅ Code done | Needs posters printed and placed |
| Tested on a phone in the building | ⏳ Not yet | Next milestone |



---

## 3. How it works (data pipeline)

```
DONE ONCE (computer)                                         LIVE (phone, every frame)
──────────────────                                           ─────────────────────────
Floor plans + tour video + measurements                      AR tracking (ARCore / ARKit) → phone position
        │                                                            │
        ▼                                                            ▼
Tracing script ──► kmutt_cpe_floors.json                     Poster scan → map is pinned to the real building
        │                                                            │
        ▼                                                            ▼
Unity editor import ──► BuildingMap in the scene             A* finds the route (across floors if needed)
        │                                                            │
        ▼                                                            ▼
App build (map is baked into the APK/IPA, works offline)     Arrows + instructions shown, hidden behind walls
```

* The JSON is **only an editor input**. After import, the map lives in the Unity scene and is packed into the app at build time.
* Changing the map (new room name, moved door) means: update the data → regenerate the JSON → re-import → rebuild.

---

## 4. Where things are in the repo

```
Assets/KMUTT_ARNav/
├─ Data/
│  ├─ kmutt_cpe_floors.json        ← the map: walls, nodes, links, rooms, stairs, lifts
│  └─ map_preview.png              ← top-down picture of both floors (look at this first!)
├─ Shaders/ARDepthMask.shader      ← invisible walls that hide arrows behind real walls
├─ Scripts/Runtime/Navigation/     ← BuildingMap, NavNode, NavGraph, AStarPathfinder,
│                                    PathSmoother, NavigationController, MapData
├─ Scripts/Runtime/AR/             ← MapAligner (posters), ARPathVisualizer (arrows),
│                                    AROcclusionConfigurator, ProceduralMarkers
├─ Scripts/Runtime/UI/NavigationUI.cs   ← simple test UI (app team will replace it)
├─ Scripts/Editor/ARMapToolsWindow.cs   ← menu: KMUTT → AR Map Tools
├─ Generated/                      ← created by the tools (meshes, materials, prefabs) – COMMIT IT
└─ README_SETUP.md                 ← full Unity/XR setup guide (project settings, hierarchy, inspector)
```

> **Ownership:** `Assets/KMUTT_ARNav/` = AR / map team. Please don't edit files in here without telling me. App UI should live in `Assets/App/`.

---

## 5. Map data facts

| Item | Value |
|---|---|
| Units | metres; Unity axes: `x` along the building, `z` across, `y` up |
| Scale source | 1:450 poster; main building measured **14.65 cm** → $14.65 \times 450 = 6592\text{ cm} \approx 66\text{ m}$ |
| Floor size | about 92 × 39 m including the side wings |
| Floor heights | Floor 10 at $y = 0$; Floor 11 at $y = 3.5$ m (**assumed, please confirm**) |
| Floor 11 source | Official fire-evacuation plan (CAD, accurate) |
| Floor 10 source | Department plan (simplified drawing). Fitted onto Floor 11 using stairs/lifts; may be off by about 1–2 m in places |

---

## 6. Destinations

**Floor 11** (verified from the on-site walkthrough video; the fire plan's `B11-11xx` codes are **not** the real numbers):

| nodeId | Name | Side of main corridor |
|---|---|---|
| `F11_ROOM_1116` | Room 1116 – Classroom | south, east end |
| `F11_ROOM_1115` | Room 1115 – Classroom | south |
| `F11_ROOM_1114` | Room 1114 – Classroom | south |
| `F11_ROOM_1113` | Room 1113 – Computer Laboratory | south |
| `F11_ROOM_1112` | Room 1112 – Computer Laboratory | south |
| `F11_ROOM_1121` | Room 1121 – Classroom | north |
| `F11_ROOM_1120` | Room 1120 – Network Laboratory | north |
| `F11_ROOM_1119` | Room 1119 – Electronics and Circuits Laboratory | north |
| `F11_ROOM_1118` | Room 1118 – Embedded Systems Laboratory | north |
| `F11_ROOM_1117` | Room 1117 – Graduate Common Room | north, west end |

Not yet verified (walkable, no destination): the narrow rooms at the far west of the south row and far east of the north row, and the rooms in the east/west wings.

**Floor 10** (from the on-site video tour):

| nodeId | Name |
|---|---|
| `F10_ROOM_1052` | Room 1052 – Teachers Lounge |
| `F10_DEPARTMENT_HALL` | Department Hall |
| `F10_ROOM_1017` | Room 1017 – Senior Project Room |
| `F10_ROOM_1016` | Room 1016 – Hardware House Office |
| `F10_ROOM_1014` | Room 1014 – Hardware House Workshop |
| `F10_ROOM_1013` | Room 1013 – ESIC Lab |
| `F10_ROOM_1015` | Room 1015 – Planning and Administration Office |
| `F10_ROOM_1012` | Room 1012 – A² Lab |
| `F10_ROOM_1002` | Room 1002 – G3MS & CI Lab |
| `F10_ROOM_1001` | Room 1001 – G3MS & CI Lab |

Floor 11 IDs follow the same pattern: `F11_ROOM_1114` etc.

---

## 7. The JSON format (short version)

```json
{
  "units": "meters",
  "metersPerPixel": 0.101,
  "floors": [
    { "name": "Floor_11", "level": 11, "baseY": 3.5, "height": 3.0,
      "walls": [ { "name": "Floor_11_Wall_000",
                   "center": { "x": 26.614, "y": 5.0, "z": 43.127 },
                   "size":   { "x": 22.725, "y": 3.0, "z": 0.404 } } ] }
  ],
  "nodes": [
    { "nodeId": "F11_ROOM_1114", "displayName": "Room 1114", "type": "Room", "floor": 11,
      "position": { "x": 63.327, "y": 3.5, "z": 11.615 },
      "isDestination": true, "transitionGroup": "", "auto": false },
    { "nodeId": "F10_STAIRW", "displayName": "Stairs W", "type": "Stairs", "floor": 10,
      "position": { "x": 5.05, "y": 0.0, "z": 18.988 },
      "isDestination": false, "transitionGroup": "StairW", "auto": false }
  ],
  "links": [ { "a": 739, "b": 39 } ]
}
```

* `type`: `Waypoint` (plain walking point), `Room`, `Stairs`, `Elevator`
* `links` use **indexes** into `nodes`
* Stairs/lift nodes with the **same `transitionGroup`** on different floors are connected automatically

---

## 8. How pathfinding works (A*)

* Both floors form **one graph**. Corridor links cost their length in metres.
* Stairs/lift pairs are vertical links: stairs cost **15**, lift **25** (tunable on `NavigationController`).
* A* expands the node with the lowest $f(n) = g(n) + h(n)$, where $g$ is the cost so far and $h$ is the straight-line 3D distance to the target.
  This always finds the cheapest route.
* **Step-free mode** skips all stairs links, so routes use lifts.
* The route is split per floor. The app shows only the current floor's arrows and says *"Take the stairs up to floor 11"* at the change.
* While walking: if you go off the route for more than 2 s, or your height says you're on a new floor, the route is recalculated.

---

## 9. For the app team: how to connect

Everything you need is on `NavigationController` and `MapAligner`. Don't touch the internals.

| You call | You listen to |
|---|---|
| `nav.GetDestinations()` → list of `NavNode` (`.Label`, `.nodeId`, `.floorLevel`) | `nav.onInstruction` (string): text for the banner |
| `nav.StartNavigation("F11_ROOM_1114")` | `nav.onDistanceRemaining` (float, metres) |
| `nav.Cancel()` | `nav.onStateChanged` (`NeedsCalibration`, `Navigating`, `FloorTransition`, `Arrived`, `NoRoute`) |
| `nav.options.avoidStairs = true` | `aligner.CalibrationProgress` (0–1), `aligner.Aligned` (floor number) |

```csharp
nav.onInstruction.AddListener(t => banner.text = t);
nav.onStateChanged.AddListener(s => { if (s == NavState.Arrived) ShowArrivedScreen(); });
nav.StartNavigation("F11_ROOM_1114");
```

Example of one trip, as events:
`NeedsCalibration` → (poster scanned) `Aligned(10)` → `Navigating` → `FloorTransition` *"Take the stairs up to floor 11"* → `Navigating` → `Arrived`.

---

## 10. Open questions (please help answer)

1. **Floor height:** is Floor 10 → 11 really about 3.5 m?
2. **Lifts:** do both lift banks (west = 2 lifts, east = 3 lifts) stop at floors 10 **and** 11? Do all 4 staircases connect them?
3. **Floor 10 rooms:** confirm the front/back pairs: 1016 in front of 1014, and 1013 in front of 1015.
4. **Tour direction:** the tour matches the plan only if it starts at the 3-lift lobby. Confirm that's the "northwest" entrance.
5. **Unverified rooms:** Floor 11 wing rooms and the two narrow end rooms (south-west, north-east) have no confirmed numbers yet.
6. **More Floor 10 names:** the other Floor 10 rooms (top rows) are walkable but unnamed. Add names if needed.
7. **Better Floor 10 plan:** a CAD/evacuation plan for Floor 10 would remove the 1–2 m error.

---

## 11. Running it yourself (Unity)

Requires **Unity 6000.0.85f1**. Other versions may cause errors.

1. Pull the repo, open the project in Unity Hub, and open `Assets/Scenes/SampleScene`.
2. **KMUTT → AR Map Tools:**
   1. **Build / Rebuild Building Map**
   2. **Import Plan Waypoints**
   3. **Validate Graph**. It should say *Connected components: 1*.
3. To look around: switch to the **Scene** tab, select `BuildingMap`, press **F**. Hold the right mouse button and use **WASD** to fly through.
   Use the eye icons on `Floor_10` / `Floor_11` in the Hierarchy to view one floor at a time.
4. Phone builds: see `README_SETUP.md` §1 and §3 (XR plug-ins, AR Background Renderer Feature, scene hierarchy).

**Git rules:** commit `.meta` files and the `Generated/` folder; never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`;
don't edit the same `.unity` scene as someone else at the same time.

---

## 12. Next steps

- [ ] Answer the open questions in §10
- [ ] Print calibration posters (A4), add them to the Reference Image Library, mark their positions in the scene
- [ ] First phone build and an on-site test: check walls line up and routes work on both floors
- [ ] App team: build screens against the contract in §9
- [ ] Optional: in-editor walk-through / XR Simulation preview for testing without going to the building
- [ ] Add the map-tracing script to the repo so the map can be regenerated by anyone
