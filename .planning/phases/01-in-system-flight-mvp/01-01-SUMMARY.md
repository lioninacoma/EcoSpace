---
phase: 01-in-system-flight-mvp
plan: "01"
subsystem: simulation-render-hud
tags: [godot, csharp, floating-origin, soi, walking-skeleton, gameworld, renderbridge, hud]

requires: []
provides:
  - "Iterative null-safe SOI transition loop in GameWorld (STAB-01)"
  - "Floating-origin RenderBridge: ship-relative per-frame mesh positioning"
  - "Keyboard thrust via InputMap (thrust_forward W, thrust_back S)"
  - "Phosphor-green live-speed HUD label reading ship speed each frame"
affects:
  - "02-flight-feel"
  - "03-body-rendering"
  - "04-hud-polish"

tech-stack:
  added:
    - "Scripts/Render/RenderBridge.cs (new Node3D component)"
    - "Scripts/HUD/Hud.cs (new Control component)"
  patterns:
    - "Floating-origin: UniVec3.ToLocalDouble(ship.LocalPos) for all body render positions"
    - "Null-safe GameObjects accessor: Get(int) with unsigned-cast bounds check"
    - "Iterative SOI transition: bounded while loop with lastExitedIndex anti-oscillation"
    - "Mesh lifecycle: create once per body, reposition each frame, toggle Visible"
    - "Read-only HUD: per-frame speed delta magnitude divided by delta time"

key-files:
  created:
    - "Scripts/Render/RenderBridge.cs"
    - "Scripts/HUD/Hud.cs"
  modified:
    - "Scripts/Universe/GameWorld.cs"
    - "Scripts/Universe/TestSetup.cs"
    - "Main.tscn"
    - "project.godot"

key-decisions:
  - "Iterative SOI transition (MaxIterations=32) replaces recursive form — prevents stack overflow and enables capped anomaly logging"
  - "Floating origin anchored on ship.LocalPos in RenderBridge (not parent body) — prevents render jitter at large distances"
  - "Skeleton speed as [Export] double SkeletonSpeed=1e8 m/s placeholder — true context-scaled speed deferred to Plan 02"
  - "RenderBridge snapshots ChildIndices before foreach iteration — prevents InvalidOperationException on mutation"
  - "HUD computes speed from prev-frame position delta rather than internal sim state — keeps it a clean read-only consumer"

patterns-established:
  - "Pattern 1 (STAB-01): Bounded iterative TrySpaceTransition with lastExitedIndex anti-oscillation and MaxIterations cap"
  - "Pattern 2 (RND-01): Floating-origin sync via ToLocalDouble(ship.LocalPos) — anchor on ship, never parent body"
  - "Pattern 3 (RND-02): Render only parent.ChildIndices bodies (current-space-only rendering)"
  - "Pattern 4 (HUD): Per-frame speed label as read-only consumer; prev-pos delta / delta pattern"

requirements-completed: [STAB-01, WORLD-01, RND-01, RND-02, HUD-01, TRV-01]

duration: 4min
completed: "2026-06-13"
---

# Phase 01 Plan 01: Walking Skeleton Summary

**Iterative null-safe SOI transition + floating-origin RenderBridge + keyboard thrust + phosphor-green speed HUD establishing the full input→sim→render→HUD loop**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-06-13T01:40:37Z
- **Completed:** 2026-06-13T01:44:38Z
- **Tasks:** 3 of 3 auto tasks complete (checkpoint:human-verify pending)
- **Files modified:** 6

## Accomplishments

- Replaced recursive `TrySpaceTransition` with a bounded iterative while loop; added null-safe `Get(int)` accessor protecting all index dereferences in the transition family
- Created `RenderBridge.cs`: per-frame floating-origin body mesh sync using `UniVec3.ToLocalDouble(ship.LocalPos)`, iterating only `parent.ChildIndices` (current-space bodies only)
- Removed autopilot from `TestSetup`; added keyboard thrust via `Input.GetActionStrength("thrust_forward"/"thrust_back")` and added `ShipIndex` property for downstream consumers
- Added `thrust_forward` (W) and `thrust_back` (S) InputMap actions to `project.godot`
- Created `Hud.cs`: phosphor-green per-frame speed label (prev-frame delta magnitude / delta) as a read-only sim consumer
- Updated `Main.tscn`: RenderBridge node, Hud + SpeedLabel nodes, Camera3D far=1e12, removed static MeshInstance3D; DirectionalLight3D placeholder subsequently removed (post-checkpoint fix — see Deviations)

## Task Commits

1. **Task 1: Iterative null-safe SOI transition (STAB-01)** - `b31161c` (feat)
2. **Task 2: Floating-origin RenderBridge + thrust-driven ship** - `f51ba3b` (feat)
3. **Task 3: Phosphor-green live-speed HUD label** - `0f0cd61` (feat)

## Files Created/Modified

- `Scripts/Universe/GameWorld.cs` - Iterative TrySpaceTransition, Get(int) null-safe accessor, MaxIterations cap, null checks in transition family and PrintPositions, RunTransitionSmokeCheck()
- `Scripts/Render/RenderBridge.cs` - NEW: floating-origin body mesh sync via ToLocalDouble, ChildIndices iteration, lazy mesh lifecycle
- `Scripts/Universe/TestSetup.cs` - Removed autopilot; added SkeletonSpeed export, thrust _Process, ShipIndex property
- `Scripts/HUD/Hud.cs` - NEW: phosphor-green speed label, prev-frame delta speed calculation, read-only consumer
- `Main.tscn` - Added RenderBridge and Hud nodes, removed static MeshInstance3D, Camera3D far=1e12
- `project.godot` - Added [input] section with thrust_forward (W) and thrust_back (S)

## Deviations from Plan

### Architecture decision: uniform render-scale (supersedes D-15)

**[Architecture Decision] Render in uniformly scaled-down space; camera far 1e6**
- **Decided after:** human-verify checkpoint (post-plan, owner decision)
- **Supersedes:** D-15 ("honest 1:1 render distances, specks growing on approach") — that decision has been reversed
- **Decision:** Universe/SOI math remains 1:1 (UniVec3, TranslatePos, all SOI distances and speed envelopes are unchanged). Rendering is done in a uniformly scaled-down render space via a single factor `k = RenderScale = 1e-6` (1 render unit = 1,000,000 m). Because the scale is uniform and applied to both positions and radii, perspective is invariant — visually identical to 1:1. This keeps the camera far plane at a reasonable 1e6 render units (covers ~1e12 m ≥ 1 AU for every MVP body).
- **Rationale:** "Only universe calculations are done in 1:1 scale. Rendering uses a scaled-down render space with relative, small distances and a reasonable camera far plane (max 1e6)."
- **Changes made:**
  1. Added `[Export] public float RenderScale { get; set; } = 1e-6f` to `RenderBridge` with full doc comment.
  2. `RenderBodyAt`: position scaled via `Double3 rel = body.LocalPos.ToLocalDouble(ship.LocalPos) * RenderScale` (uses AVX2-accelerated `Double3.operator*(Double3, double)`).
  3. `GetOrCreateMesh`: `SphereMesh.Radius = DefaultBodyRadius * RenderScale`, `Height = DefaultBodyRadius * 2f * RenderScale`. `DefaultBodyRadius` stays in true meters.
  4. `CameraFarPlane` default changed 1e12 → 1e6.
  5. `Main.tscn` `Camera3D far` updated 1e+12 → 1e+06 (consistent with runtime override in `_Ready`).
- **Note for Plan 01-02:** Plan 01-02 MUST reuse `RenderBridge.RenderScale` when applying true 1:1 body radii (RND-03/04). Do NOT hardcode radii directly in render units — always express radii in true meters and multiply by `RenderScale` at mesh creation, exactly as this pattern establishes.
- **Files modified:** `Scripts/Render/RenderBridge.cs`, `Main.tscn`
- **Commit:** `50c6338`
- **Build result:** 0 errors, 0 warnings (`dotnet build EcoSpace.csproj`)

### Post-checkpoint runtime fix

**[Rule 1 - Bug] Removed placeholder DirectionalLight3D; added unshaded skeleton material**
- **Found during:** human-verify checkpoint — Godot logged `prepare_camera: Condition "!res" is true. Returning: false` every frame at startup
- **Root cause:** `Camera3D.far = 1e12` (needed for AU-scale visibility) combined with the placeholder `DirectionalLight3D` caused the rendering light culler to fail building a valid frustum. With no light in the scene no frustum is attempted and the error disappears.
- **Fix:**
  1. Removed `DirectionalLight3D` node entirely from `Main.tscn`. The node was a placeholder scheduled for replacement by an `OmniLight3D` in Plan 02; removing it early has no functional downside for the skeleton.
  2. Added `StandardMaterial3D` with `ShadingMode = Unshaded` and a light gray-green albedo via `MaterialOverride` in `RenderBridge.GetOrCreateMesh` so skeleton sphere bodies remain visible without any scene light. Per-body materials/radii elaborated in Plan 02 (RND-03/04).
  3. Camera `far = 1e12` left unchanged — AU-scale bodies must stay visible post SOI transition.
- **Files modified:** `Main.tscn`, `Scripts/Render/RenderBridge.cs`
- **Commit:** `f6f1a35`
- **Build result:** 0 errors, 0 warnings (`dotnet build EcoSpace.csproj`)

## Known Stubs

- **SkeletonSpeed = 1e8 m/s** in `TestSetup.cs` — placeholder forward speed; true context-auto-scaled speed arrives in Plan 02 (FLT-01/02/03)
- **DefaultBodyRadius = 6.371e6f** in `RenderBridge.cs` — uniform Earth-radius sphere for all bodies (expressed in true meters, scaled by RenderScale at mesh creation); true 1:1 per-body radii and materials arrive in Plan 02 (RND-03/04)
- **Speed display: raw m/s only** in `Hud.cs` — adaptive unit ladder (m/s → km/s → AU/s → ly/s) deferred to Plan 04 (HUD-01 full)
- **Forward-only thrust (+Z)** in `TestSetup._Process` — attitude-oriented (Basis) motion arrives in Plan 02 (FLT-02)

## Threat Flags

No new threat surface introduced beyond what the plan's threat model covers.

## Self-Check: PASSED (post-checkpoint fix included)

Files exist:
- Scripts/Universe/GameWorld.cs: FOUND
- Scripts/Render/RenderBridge.cs: FOUND
- Scripts/HUD/Hud.cs: FOUND
- Scripts/Universe/TestSetup.cs: FOUND
- Main.tscn: FOUND
- project.godot: FOUND

Commits verified:
- b31161c (Task 1 — feat: iterative SOI transition): FOUND
- f51ba3b (Task 2 — feat: RenderBridge + thrust): FOUND
- 0f0cd61 (Task 3 — feat: Hud.cs): FOUND

Build: 0 errors, 0 warnings confirmed.
