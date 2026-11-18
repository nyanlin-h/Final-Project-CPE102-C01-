# KMUTT CPE AR Navigation: Floor 10 & 11 map setup

## v2 update: map rebuilt from the floor plans
The first mesh (`kmutt_cpe_floors_10_11.obj`) was traced from the fire-evacuation plan, but it captured the
**room-number text and the evacuation arrows as walls**, while most real walls, stairs and lifts were missing.
`Data/kmutt_cpe_floors.json` is now traced directly from:
* **Floor 11:** the evacuation plan (CAD drawing, room numbers B11-1100 … B11-1115)
* **Floor 10:** the Computer Engineering department plan, which is schematic. It was fitted onto the Floor 11 drawing by matching the 4 staircases and 2 lift shafts, so the floors line up vertically.

Contents: 155 + 157 wall boxes · 1511 nodes, all connected · **Room 1100 – Room 1115** (Floor 11 destinations) ·
4 staircases (`StairNW`, `StairW`, `StairE`, `StairSE`) and 2 lift banks (`LiftW`, `LiftE`) linked between floors 10 and 11.
Floor 10 destinations, from the on-site video tour: Room 1052 (Teachers Lounge), Department Hall, 1017 (Senior Project Room), 1016 (Hardware House Office), 1014 (Hardware House Workshop), 1013 (ESIC Lab), 1015 (Planning and Administration Office), 1012 (A² Lab), 1002 and 1001 (G3MS & CI Labs). The other Floor 10 rooms are walkable but unnamed.

**Scale (measured):** on the 1:450 Floor 11 evacuation poster, the main building is 14.65 cm long, which is 66 m in reality.
That gives 1 plan pixel = 0.101 m, a classroom bay of about 10.8 m, and a corridor about 4.6 m wide. The whole floor is about 92 × 39 m.
Floor-to-floor height is assumed to be 3.5 m.

**New Editor flow:** AR Map Tools → **Build / Rebuild Building Map** → **Import Plan Waypoints** → **Validate Graph**.
There's no need for Generate Grid or Auto-Link. If you used them before, Import removes those old auto waypoints first.

---

Targets: **Unity 6 (6000.x) · URP · AR Foundation 6.x · ARCore XR Plugin 6.x · ARKit XR Plugin 6.x**.
(On AR Foundation 5.x, the only change is in `MapAligner`: use `trackedImagesChanged` instead of `trackablesChanged`.)

---

## 0. What's in the package

```
Assets/KMUTT_ARNav/
  Data/kmutt_cpe_floors.json     ← cleaned geometry from kmutt_cpe_floors_10_11.obj (Unity axes, meters)
  Shaders/ARDepthMask.shader     ← invisible depth-only occluder (URP)
  Scripts/Runtime/Navigation/    ← BuildingMap, FloorRoot, MapZone, NavNode, NavGraph, AStarPathfinder,
                                   PathSmoother, NavigationController
  Scripts/Runtime/AR/            ← MapAligner, ARPathVisualizer, AROcclusionConfigurator, ProceduralMarkers
  Scripts/Runtime/UI/            ← NavigationUI (TextMeshPro)
  Scripts/Editor/ARMapToolsWindow.cs  ← menu KMUTT > AR Map Tools
  Generated/                     ← created by the tools (meshes, materials, prefabs)
```

### What I changed in your mesh
* The OBJ is **Z-up**. Unity is **Y-up**, so the conversion is `unity = (X, Z, Y)`. This keeps the plan view the right way round, with no mirroring.
  Don't drag the raw OBJ into Unity for navigation: Unity's OBJ importer flips X, which would mirror the building.
* The OBJ has 344 boxes. **8 of them are solid room/corridor blocks**, not walls. Floor 11 even has one 26.6 × 13.8 m block covering the whole floor.
  Used as occluders, these would hide every arrow, because the user would be standing *inside* a solid block.
  They are now **zones**: trigger volumes, plus optional thin walls along their outline.
  * Floor 10: 65 walls + 7 zones (`Zone_04` is the long corridor band, about x 6.8–28 / z 6.8–10.5)
  * Floor 11: 271 walls + 1 zone (the floor outline)
* The STL is the same geometry in **centimeters** with no floor groups, so it isn't used.

---

## 1. Project settings (once)

| Where | Setting |
|---|---|
| Package Manager | AR Foundation, ARCore XR Plugin, ARKit XR Plugin, Universal RP, TextMeshPro (in uGUI) |
| Project Settings → XR Plug-in Management → **Android** tab | ☑ Google ARCore |
| Project Settings → XR Plug-in Management → **iOS** tab | ☑ Apple ARKit |
| XR Plug-in Management → ARCore | Requirement **Required**, Depth **Optional** (so non-depth phones can still install) |
| XR Plug-in Management → ARKit | Requirement **Required**, Face Tracking off |
| Player → Android | Graphics APIs: **OpenGLES3 first** (if the camera feed is black with Vulkan, remove Vulkan). Min API **24+**, Scripting Backend **IL2CPP**, Target Architectures **ARM64** |
| Player → iOS | Camera Usage Description: *"Camera is used for AR navigation"*, Target min iOS **15+**, Architecture ARM64 |
| URP Renderer asset (e.g. `PC_Renderer`/`Mobile_Renderer`) | **Add Renderer Feature → AR Background Renderer Feature** (required, otherwise the camera feed is black) |
| URP Asset | HDR off, MSAA 2× or off, Depth Texture off (not needed), Shadows off (markers are unlit) |

Make sure the **mobile** URP asset is active for Android/iOS in *Project Settings → Quality*.

---

## 2. Build the map (Editor)

1. Copy `Assets/KMUTT_ARNav` into your project (keep the folder name).
2. Open your AR scene → **KMUTT → AR Map Tools**.
3. **Build / Rebuild Building Map**. This creates the `ARNavWalls` layer, the materials and:

```
BuildingMap                (BuildingMap)         pos 0,0,0  rot 0  scale 1  ← NEVER static
├─ Floor_10                (FloorRoot level 10)  y = 0.0
│  ├─ Walls                (MeshFilter, MeshRenderer[M_ARDepthMask], N × BoxCollider)  layer ARNavWalls
│  ├─ Zones                (child per zone: BoxCollider isTrigger + MapZone)          layer Ignore Raycast
│  ├─ Waypoints            (NavNodes go here)
│  └─ Calibration          (poster markers go here)
└─ Floor_11                (FloorRoot level 11)  y = 3.5
   └─ … same …
```
   Re-running it rebuilds walls/zones only. Your waypoints and destinations are kept.

4. **Generate Grid** (1.0 m spacing, 0.35 m clearance) → **Auto-Link (LOS)**.
   Expected: about 180 nodes on F10 and 225 on F11 (see `map_preview.png`).
5. **Rename zones** to real room names (select `Floor_10/Zones/Floor_10_Zone_05` → MapZone → Display Name).

### 2a. Doors (important on Floor 10)
The zone outline walls close rooms off. For every real door:
* Select one grid node on the corridor side and one on the room side (Ctrl/Cmd-click) → **Link Selected**. The yellow gizmo line is a manual link, and Auto-Link never removes it.

### 2b. Destinations
Move the Scene view pivot to a room door (double-click near it, or press F on an object) → set the name/floor → **Add Destination**.
Then put it exactly at the door and **Auto-Link** again. `nodeId` is generated as `F10_ROOM_1001`, which you can use from code/QR codes.

### 2c. Stairs & elevators (Floor 10 ⇄ 11)
The model has **no stair or elevator geometry I could identify with confidence**. Candidates worth checking on site:
F10 boxes around x 9.3–11.1 / 12.4–14.2, z 3.1–6.1 (two ~1.8 × 2.9 m blocks, which is lift-shaft sized), and F11 x 7.4–8.0 / 22.1–22.7, z 2.1–5.8.

* Set *Transition type* = Stairs, *group* = `StairA` → **Add Stairs/Elevator Pair**. One node is created per floor.
* Move **each** node to the stair landing/door **on its own floor**. They can differ in X/Z (for example, stairs that switch back).
* Repeat for each staircase (`StairB`…) and lift (`Lift1`, type Elevator). Nodes with the same group are connected vertically. Costs are set on NavigationController (stairs 15, elevator 25).
* Give each transition node's **blue Z axis** the direction you face when stepping off.

### 2d. Validate
**Validate Graph** reports islands, unreachable destinations, and stairs groups that exist on only one floor. Aim for: *Connected components: 1* (or only tiny leftover islands).

---

## 3. AR scene hierarchy & inspector

Start from *GameObject → XR → AR Session* and *XR → XR Origin (Mobile AR)*, then:

```
AR Session                 ARSession (Attempt Update ☑, Match Frame Rate ☑), ARInputManager
XR Origin (Mobile AR)      XROrigin (Camera Y Offset 0, Tracking Origin Mode: Not Specified)
│                          ARPlaneManager        Detection Mode: Horizontal, Plane Prefab: none (or a debug one)
│                          ARAnchorManager       Anchor Prefab: none
│                          ARTrackedImageManager Serialized Library: KMUTT_RefLib, Max Moving Images: 0,
│                                                Tracked Image Prefab: (optional) an XYZ axes gizmo, used to verify orientation
│                          ARRaycastManager      (optional)
└─ Camera Offset
   └─ Main Camera          tag MainCamera, Camera(Clear Flags: Solid Color black, Near 0.1, Far 60),
                           ARCameraManager (Focus Auto, Light Estimation None),
                           ARCameraBackground (Use Custom Material ☐),
                           AROcclusionManager, AROcclusionConfigurator(map = BuildingMap),
                           TrackedPoseDriver (Input System)
BuildingMap                (from step 2. Keep at the scene root.)
NavigationSystem           MapAligner, ARPathVisualizer, NavigationController
Canvas (Screen Space - Overlay, Canvas Scaler: Scale With Screen Size 1080×1920, match 0.5)
└─ NavigationUI + TMP_Dropdown, Buttons (Go, Stop, Align Here), Toggles (Step-free, Show walls), TMP texts
EventSystem                (Input System UI Input Module)
```

Then in AR Map Tools:
* **Create Marker Prefabs** → `Generated/Markers/NavArrow`, `NavDestination`, `NavTransition`
* **Create Navigation System Object** → wires MapAligner/Visualizer/Controller and adds one poster marker per floor.

### Recommended inspector values

**ARPathVisualizer:** Arrow Spacing 1.0 · Arrow Height 0.25 (floor-ish; 0.9 for "floating" at hip height) · Visible Ahead 12 · Max Arrows 24.
Optional: add a LineRenderer (width 0.08, URP Unlit material, Alignment *View*) and assign it to *Path Line*. It is re-parented under BuildingMap automatically.

**NavigationController:** Wall Mask **ARNavWalls** · Arrive Radius 1.5 · Transition Reach Radius 1.8 · Off Route Distance 2.5 · Off Route Seconds 2 · Eye Height 1.4 · Smooth Path ☑.
Wire *On Instruction (String)* to `NavigationUI`, or just add NavigationUI, which subscribes from code.

**MapAligner:** Yaw Only ☑ · Samples To Lock 20 · Max Scan Distance 2.5 · Targets: one per poster (below).

**AROcclusionConfigurator:** Depth Mode *Fastest* · Temporal Smoothing ☑ · Use Model Occlusion ☑.

---

## 4. Calibration posters (how the map is aligned to the real hallway)

1. Make 2–4 posters per floor: high-contrast, non-repetitive images (logos and text collages work well, plain QR codes are weak). Print at a known size, e.g. **A4 = 0.21 m wide**.
2. *Create → XR → Reference Image Library* → `KMUTT_RefLib`. Add each image, **Specify Size ☑**, Physical Size X = printed width (m), name e.g. `KMUTT_F10_A`.
   (ARKit *requires* the physical size. ARCore uses it for faster detection.)
3. Hang the posters flat on walls at about 1.5 m, at spots you can **measure** on the model (e.g. "corridor wall, 2.0 m from the lift door").
4. In the scene, move each `Floor_xx/Calibration/Poster_Fxx_A` to that spot. Orientation:
   * **+Y (green)** points **out of the wall** toward the viewer.
   * **+Z (blue)** points to the **top** of the poster.
   * Turn on the debug walls and check that the marker sits on the wall face.
5. On device, add an axes prefab as *Tracked Image Prefab* once, scan a poster, and confirm green comes out of the image and blue points to its top. If your device shows otherwise, rotate the markers to match. This is a one-time check.

The user opens the app, points at the nearest poster for about 1 s (20 frames), and the map locks and gets an `ARAnchor`. Scanning another poster later corrects drift, which is recommended after taking the stairs.

**Testing without posters:** assign *Debug Start Node* in NavigationUI (a node with its blue axis pointing down the corridor). Stand on that spot facing that way and press **Align Here**.

---

## 5. Occlusion (how markers hide behind walls)

| Layer | Works on | Hides markers behind |
|---|---|---|
| **Model occlusion**: `M_ARDepthMask` on `Walls` (queue Geometry-10, ZWrite on, ColorMask 0) | **every** ARCore/ARKit device | modelled walls, any distance |
| **Sensor occlusion**: `AROcclusionManager` env depth | ARCore Depth API phones; iPhone/iPad **with LiDAR** | people, furniture, pillars not in the model (≈ 0.5–8 m) |
| People occlusion (fallback) | iOS A12+ without LiDAR | people only |

`AROcclusionConfigurator` picks the best available mode at runtime, so no iOS-only feature is assumed.
Markers use the opaque URP Unlit material (queue 2000), so they render *after* the depth mask and get clipped correctly.
If you make markers transparent (queue 3000), occlusion still works because transparent objects still depth-*test*.

---

## 6. Device test checklist

1. Build to device. *Show walls* toggle on → scan a poster → orange walls should line up with the real walls within ~10–20 cm. If they're rotated, the poster marker orientation is wrong. If they're shifted, the marker position is wrong.
2. Toggle walls off → choose a destination on the same floor → arrows appear, and are hidden when you look through a wall at the next corridor.
3. Walk off route for 3 s → "Re-routing…".
4. Cross-floor: pick an F11 room from F10 → arrows lead to the stairs → transition pin appears → climb → app detects floor 11 by height (≈ 2.6 m climbed) → new arrows. Scan the F11 poster to remove stair drift.
5. Step-free toggle → route must use the elevator.

### Common problems
* **Black camera** → AR Background Renderer Feature missing on the active URP renderer.
* **Arrows visible through every wall** → BuildingMap walls use `M_MapDebug` (debug on), or the URP renderer uses a depth-priming mode that skips SRPDefaultUnlit. Turn *Depth Priming* off.
* **Arrows never visible** → you are standing inside a wall box. Check the zone conversion, or that `Generated/Floor_xx_Walls.asset` was rebuilt.
* **Floor never switches** → `Eye Height` is wrong for how the phone is held. Log `UserMap().y` on each floor.
