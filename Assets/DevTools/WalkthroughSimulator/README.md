# Walkthrough Simulator (Editor only)

Walk the KMUTT CPE map on your computer: no phone, posters or trip to the building needed.

## Use it

1. Open `Assets/Scenes/SampleScene` and press **Play**.
2. You start on Floor 10 at the east lift. The map is aligned automatically (no poster scan).
3. Pick a room in the left panel and press **Go**. Follow the blue arrows.

| Key | Action |
|---|---|
| Hold right mouse | Look around |
| W A S D | Walk (Shift = run) |
| E / Q | At stairs or a lift: go up / down a floor |
| F | Fly mode (Space / C = up / down); press F again to land |
| **TP** button | Teleport to a room, stairs or lift |
| Step-free toggle | Routes avoid stairs (applies on next **Go**) |

Green labels are rooms, purple are stairs/lifts; dimmed labels are behind a wall.

## What it uses

It drives the **real** `NavigationController`, `ARPathVisualizer` and `MapAligner.AlignStandingOnNode`.
Only AR tracking is replaced (by the WASD camera). Instructions, re-routing, floor changes and arrival
behave exactly as on the phone.

## Notes

* Turn it off: **KMUTT ▸ Dev ▸ Walkthrough Simulator (Play mode)** (per-computer setting).
* Nothing is saved into the scene. Walls are drawn solid and a checker floor is added only while playing.
* The whole script is wrapped in `#if UNITY_EDITOR`, so it is never included in phone builds.
