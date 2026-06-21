---
phase: 06-targeting-system
plan: "03"
subsystem: Render
tags: [targeting, hud, marker, shader, outline, 3d, tracking-label]
dependency_graph:
  requires:
    - Hud.ActiveTargetIndex (06-01) — cross-space active target index
    - UniMath.RelativePosition / RelativeMetres / Distance (LCA path)
    - Camera3D (Main.tscn)
  provides:
    - TargetMarkerRenderer (3D sphere-outline marker, camera-relative angular placement)
    - target_outline.gdshader (unlit fresnel silhouette, draws on top)
    - TrackingLabel (name + live distance, pinned to the marker)
  affects:
    - Scripts/Render/TargetMarkerRenderer.cs (new)
    - Shaders/target_outline.gdshader (new)
    - Main.tscn (TargetMarkerRenderer Node3D + TrackingLabel)
tech_stack:
  added: []
  patterns:
    - Camera-relative marker placement — sphere on a fixed-radius shell around the camera in the target's direction
    - Angular sizing — markerRadius = MarkerCameraDistance * tan(targetRadius/distance); scale-independent ratio
    - depth_test_disabled silhouette shader draws over all geometry (no occlusion, no far-plane culling)
    - Adaptive rim_width for constant pixel-thickness ring band (exact sphere-silhouette geometry)
    - Read-only consumer — reads Hud.ActiveTargetIndex + target UniObject only
key_files:
  created:
    - Scripts/Render/TargetMarkerRenderer.cs
    - Shaders/target_outline.gdshader
  modified:
    - Main.tscn
decisions:
  - "D-50 implemented: 3D sphere-outline marker (unit SphereMesh + unlit rim/fresnel silhouette shader, discards interior) — distorts perspective-correctly like a real sphere"
  - "D-51 implemented: placement/size purely from the target UniObject via UniMath LCA, NO render-set gate — works in every space incl. a NO-MESH GALAXY in Universe space (galaxies are selectable targets per 06-02 override)"
  - "D-52 implemented: minimum on-screen size floor (MinMarkerRadius) — a distant target is never a sub-pixel speck"
  - "D-53 preserved: TargetMarkerRenderer is read-only — no GameObjects/LocalPos writes, no TranslatePos"
  - "D-57 implemented: name + live distance (UniMath.Distance LCA) tracking label pinned to the marker; rides the off-screen DirMarker when the target leaves the screen; fixed-corner TGT readout remains as fallback"
  - "PLACEMENT TECHNIQUE (user direction 2026-06-21): marker rides a fixed-radius sphere AROUND THE CAMERA (camPos + dir*MarkerCameraDistance) sized to the target's ANGULAR size — gives constant depth (constant ring thickness), on-top rendering (depth_test_disabled), and immunity to far-plane culling. Supersedes the earlier true-world-position + observer-unit placement for the marker; direction still via UniMath LCA, angular size is a scale-independent ratio"
  - "CONSTANT RING THICKNESS (user direction 2026-06-21): rim_width driven per-frame from exact silhouette geometry T = R*(1-sqrt(1-rim_width^2)) → rim_width = sqrt(1-(1-T/R)^2), holding a constant ~2px band (TargetRingThicknessPx) regardless of marker diameter"
metrics:
  duration: "~30m (multi-cycle play-test)"
  completed: "2026-06-21"
  tasks_completed: 4
  files_changed: 3
status: complete
---

# Phase 6 Plan 3: 3D Sphere-Outline Target Marker + Tracking Label

A 3D sphere-outline marker that rings the active target across every space, holds a minimum
on-screen size, keeps a constant ring line thickness at any distance, never gets occluded by
other meshes or culled by the far plane, and carries a name+distance label that tracks the
body on screen. Replaces the flat 2D circle removed in 06-01.

Converged over several play-test cycles (see Journey below). Final human play-test
**APPROVED 2026-06-21**.

## What Changed

### Shaders/target_outline.gdshader (NEW)
Unlit rim/fresnel silhouette shader. `render_mode unshaded, cull_back, depth_draw_never,
depth_test_disabled`. The fragment discards interior fragments where
`|dot(VIEW, NORMAL)| > rim_width`, leaving only the silhouette ring; `outline_color` and
`rim_width` are pushed per-frame. `depth_test_disabled` makes the marker draw on top of all
scene geometry (no occlusion by intervening planets/stars).

### Scripts/Render/TargetMarkerRenderer.cs (NEW)
`Render.TargetMarkerRenderer : Node3D` — read-only consumer of sim state (D-53). Each frame
reads `Hud.ActiveTargetIndex` and drives one unit-sphere `MeshInstance3D` (outline shader as
`MaterialOverride`) plus the tracking label:

- **Direction (cross-space, D-51):** `UniMath.RelativePosition(ship, target)` → `ToDouble3()`
  on the SOI-bounded delta → normalized unit direction. No render-set gate, so a no-mesh
  galaxy still resolves a direction.
- **Placement (camera-relative):** `markerPos = cameraGlobalPosition + dir * MarkerCameraDistance`
  (default 75u) — the marker rides a fixed-radius shell around the camera. Constant depth ⇒
  no far-plane culling and a stable basis for ring thickness.
- **Angular size:** `markerWorldRadius = MarkerCameraDistance * tan(atan(rawRadius·padding / distMetres))`
  so the marker subtends the target's true angular size and lands at the correct screen
  position/size. `distMetres = UniMath.Distance` (LCA path).
- **Min-size floor (D-52):** the marker's on-screen pixel radius is clamped to
  `[MinMarkerRadius, MaxMarkerRadius]` and back-projected to a world radius at the fixed depth.
- **Constant ring thickness:** `rim_width = sqrt(1 - (1 - t)^2)` with `t = clamp(TargetRingThicknessPx / R, 0, 1)`
  (R = clamped pixel radius), from the exact sphere-silhouette band relation
  `T = R·(1 - sqrt(1 - rim_width^2))` — a ~2px band at all distances.
- **Tracking label (D-57):** `{name}  {distance}` pinned beside the on-screen marker; rides the
  off-screen `DirMarker` edge position when the target leaves the screen.

**Read-only invariant (D-53):** no writes to `GameObjects[...]` / `.LocalPos`, no `TranslatePos`.

**Key [Export] knobs:** `MarkerCameraDistance` (75), `MinMarkerRadius` (20 default; user-set 8),
`MaxMarkerRadius` (300 default; user-set 200), `MarkerPadding` (1.15), `TargetRingThicknessPx`
(2.0), `BaseRimWidth` (fallback only).

### Main.tscn
`TargetMarkerRenderer` Node3D under `Main` (WorldPath/HudPath/CameraPath/TrackingLabelPath
wired); `TrackingLabel` Label under `CanvasLayer` (phosphor-green, mouse_filter Ignore, hidden
until a target is active). NodePaths + script uid were corrected by the user in-editor during
play-test; marker radius/thickness inspector tuning preserved.

## Implementation Journey (play-test convergence)

1. **e70213e / e2c5ab2 / 9d6aedc** — initial 3D sphere outline at the target's true world
   position (observer-unit `metres / scale * factor`), min-radius floor, tracking label.
2. **5a29caf** — first thickness fix (adaptive rim_width from floored radius) — partial.
3. **1b7b451** — user editor fixes: corrected NodePaths (`../` prefixes), reconciled the script
   uid (resolved a stale `ext_resource` reference), marker radius tuning.
4. **ce0c3b1** — placement reworked to a **fixed-camera-distance sphere with angular sizing**
   (user direction): constant depth, `depth_test_disabled` on-top draw, no far-plane culling.
5. **cf8849d** — **constant pixel-thickness ring**: rim_width derived from exact silhouette
   geometry so the band reads ~2px at every distance while the diameter frames the body.

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | e70213e | feat(06-03): add target_outline.gdshader — unlit rim/fresnel sphere-silhouette outline |
| 2 | e2c5ab2 | feat(06-03): TargetMarkerRenderer — 3D sphere-outline marker from UniObject |
| 3 | 9d6aedc | feat(06-03): wire TargetMarkerRenderer + TrackingLabel into Main.tscn |
| 4 | 5a29caf | fix(06-03): adaptive rim_width guarantees minimum ring pixel thickness at distance |
| 5 | 1b7b451 | fix(06-03): correct TargetMarkerRenderer NodePaths + uid; tune marker radius (manual play-test fixes) |
| 6 | ce0c3b1 | feat(06-03): rework marker to fixed-camera-distance sphere with angular sizing |
| 7 | cf8849d | fix(06-03): adaptive rim_width for constant pixel-thickness ring band |

## Build / Test Result
`dotnet build EcoSpace.csproj -c Debug` — **Build succeeded. 0 errors, 0 warnings.**
Human play-test **APPROVED 2026-06-21**: constant ring thickness near↔far; not occluded by
meshes; no far-plane vanish; correct angular position/size incl. no-mesh galaxy; label tracks.

## Deviations from Plan
- **Placement technique superseded** — the plan's true-world-position + observer-unit marker was
  replaced (user direction) by a camera-relative fixed-distance shell with angular sizing. Same
  decisions (D-50/D-51/D-52) satisfied; direction still via UniMath LCA; better robustness
  (constant thickness, on-top, no far-plane culling).
- **Constant-thickness ring** added (`TargetRingThicknessPx`) beyond the plan's static rim_width.
- **FlightController/Hud untouched here**; the marker reads `Hud.ActiveTargetIndex` only.

## Self-Check
- [x] `Scripts/Render/TargetMarkerRenderer.cs` — camera-relative angular placement, UniMath LCA direction, D-52 floor, adaptive rim_width, read-only
- [x] `Shaders/target_outline.gdshader` — unlit silhouette, `depth_test_disabled`
- [x] `Main.tscn` — TargetMarkerRenderer + TrackingLabel wired
- [x] grep: no `GameObjects[...]=` / `.LocalPos =` / `TranslatePos` in TargetMarkerRenderer.cs
- [x] Commits e70213e, e2c5ab2, 9d6aedc, 5a29caf, 1b7b451, ce0c3b1, cf8849d on `main`
- [x] Build 0/0; play-test approved

## Self-Check: PASSED
