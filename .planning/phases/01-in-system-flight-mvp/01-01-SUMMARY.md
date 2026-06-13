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
    - "Floating-origin: UniVec3.ToLocalDoubleUnits(ship.LocalPos) × per-space factor for render positions"
    - "Floating-origin (meters): UniVec3.ToLocalDouble(ship.LocalPos) for HUD speed calculation (unchanged)"
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
  - "Unit-space render via ToLocalDoubleUnits: observer-scale as unit basis + per-space RenderFactor; supersedes global RenderScale (D-15 reversed again)"

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
4. **Deviation: Global RenderScale (uniform k=1e-6)** - `50c6338` (feat, post-checkpoint owner direction)
5. **Deviation: Unshaded material + remove DirectionalLight3D** - `f6f1a35` (fix, post-checkpoint runtime fix)
6. **Continuation: Unit-space render via ToLocalDoubleUnits + per-space factors** - `f403b7a` (feat, replaces global RenderScale)
7. **Fix: Observer-scale unit basis + parent-at-frame-origin rendering** - `e9212da` (fix, pre-Godot verification rendering correctness)

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

### Unit-space render architecture (supersedes uniform RenderScale)

**[Architecture Decision] Replace global RenderScale with per-space RenderFactor via ToLocalDoubleUnits**
- **Decided after:** human-verify checkpoint, continuation pass (owner direction)
- **Supersedes:** The uniform `RenderScale = 1e-6f` approach added in commit `50c6338` (which itself superseded D-15)
- **Decision:** Add `UniVec3.ToLocalDoubleUnits(observer)` that expresses the meter delta in observer-cell units by dividing by `observer.Scale`. RenderBridge then multiplies by a per-space factor (`PlanetRenderFactor`, `StarRenderFactor`, etc.) instead of a global meter-based scale. The observer's scale is the unit basis for all bodies in the frame — this keeps bodies at different SOI depths consistent when the ship transitions spaces.
- **Rationale:** A global meter-based `RenderScale` picks a fixed meter-to-render-unit ratio that may be correct for one tier but wrong at another (Planet scale = 0.0001 m/unit vs. Star scale = 1 m/unit is 10,000× different). By normalizing to observer units first and then applying a small per-space factor, each space tier can be tuned independently from the Inspector without touching code.
- **New method:** `UniVec3.ToLocalDoubleUnits(in UniVec3 observer)` — returns `(this - observer).ToDouble3() * (1.0 / observer.Scale)` (corrected from initial `delta.ToDouble3Units()` — see rendering correctness fix below). Marked `[AggressiveInlining]`. Does NOT replace `ToLocalDouble` (which stays in meters; still used by `Hud.cs` for speed). Also added `ToDouble3Units()` returning `(Double3)Units + Offset / Scale` for absolute unit-space positions.
- **ToLocalDouble callers checked:** `Scripts/HUD/Hud.cs:72` (`ship.LocalPos.ToLocalDouble(_prevPos)`) — correctly left in meters for speed magnitude; no change needed.
- **New exports on RenderBridge:**
  - `PlanetRenderFactor = 1e-8f` (Planet scale 0.0001 m/unit: 1 render unit ≈ 1e12 m at this factor)
  - `StarRenderFactor = 1e-8f` (Star scale 1 m/unit: 1 render unit ≈ 1e8 m at this factor)
  - `GalaxyRenderFactor = 1e-8f` (placeholder, Galaxy not exercised by MVP scene)
  - `UniverseRenderFactor = 1e-8f` (placeholder, Universe not exercised by MVP scene)
- **Radius transform contract (plan 01-02 obligation):** `DefaultBodyRadius (meters) / ship.LocalPos.Scale (→ observer units) × factor (→ render units)`. Plan 01-02 MUST apply this exact same transform when it introduces true per-body `RadiusMeters` (RND-03/04). Do NOT hardcode radii in render units.
- **Changes made:**
  1. `UniVec3.ToLocalDoubleUnits` added after `ToLocalDouble` in the Conversion sector of `UniVec3.cs`.
  2. `RenderBridge.RenderScale` export removed; replaced with four per-space `[Export]` properties.
  3. `RenderFactorFor(Space)` switch helper added (private, returns matching export by ship space, defaults to `StarRenderFactor`).
  4. `SyncBodies`: computes `factor` once from `ship.CurrentSpace`; passes to `RenderBodyAt` / `GetOrCreateMesh`.
  5. `RenderBodyAt`: calls `ToLocalDoubleUnits` then multiplies each component by `factor`.
  6. `GetOrCreateMesh`: radius computed as `(float)(DefaultBodyRadius / ship.LocalPos.Scale * factor)`.
  7. `CameraFarPlane` default remains `1e6f`; unshaded material unchanged; no DirectionalLight3D re-added.
- **Files modified:** `Scripts/Universe/Math/UniVec3.cs`, `Scripts/Render/RenderBridge.cs`
- **Commit:** `f403b7a`
- **Build result:** 0 errors, 0 warnings (`dotnet build EcoSpace.csproj`)

### Rendering correctness fix: observer-scale unit basis in ToLocalDoubleUnits

**[Rule 1 - Bug] ToLocalDoubleUnits used body's scale instead of observer's scale as unit basis**
- **Found during:** post-plan continuation (pre-Godot verification, scale-mismatch analysis)
- **Root cause:** The previous implementation `delta.ToDouble3Units()` expands to `(Double3)delta.Units + delta.Offset / delta.Scale`. After `delta = this - observer`, `delta.Scale` is `this.Scale` (left-operand scale kept by operator `-`). When a rendered body lives in a different space than the ship (e.g. parent planet at Star scale 1 vs ship in Planet scale 1e-4), the unit basis is the BODY's scale, producing observer-unit values that are 1e4× off from the radius calculation (which always uses `ship.LocalPos.Scale`). This causes camera-inside-planet at any space boundary.
- **Fix:** Replace with `delta.ToDouble3() * (1.0 / observer.Scale)` — converts the meter delta to observer units using the observer's scale unconditionally. `ToDouble3Units()` is retained for absolute unit positions (caller `ship.LocalPos.ToDouble3Units()` in RenderBridge).
- **Contract preserved:** `ToLocalDouble` (meters, used by Hud.cs for speed) unchanged. `ToDouble3Units` (absolute units) unchanged.
- **Files modified:** `Scripts/Universe/Math/UniVec3.cs`
- **Commit:** `e9212da`
- **Build result:** 0 errors, 0 warnings (`dotnet build EcoSpace.csproj`)

### Rendering correctness fix: parent body rendered at -ship offset (frame-origin)

**[Rule 1 - Bug] Parent body positioned using parent.LocalPos (grandparent frame) diffed against ship.LocalPos (parent frame) — frame mismatch**
- **Found during:** post-plan continuation (scale-mismatch analysis; same commit)
- **Root cause:** `RenderBodyAt` used `body.LocalPos.ToLocalDoubleUnits(ship.LocalPos)` for all bodies including the parent. For siblings this is correct: both body and ship are children of the same parent, so they share a frame. But the PARENT's `LocalPos` is expressed relative to the GRANDPARENT (e.g. planet position relative to the star), while `ship.LocalPos` is expressed relative to the PARENT (planet). Differencing them mixes frames — the planet renders at roughly its distance from the star (~1 AU) instead of its distance from the ship (~7,000 km).
- **Fix:** Added `isParent` boolean parameter to `RenderBodyAt`. When `isParent=true`, position is computed as `ship.LocalPos.ToDouble3Units() * -1.0` (parent at frame origin → ship-relative position is negation of ship's own offset). When `isParent=false` (siblings), `ToLocalDoubleUnits` is used unchanged. All three render quantities — parent position, sibling position, radius — are on the `ship.LocalPos.Scale` observer-unit basis, all multiplied by `factor`.
- **Sanity check (Planet space, ship 7e6 m from planet):**
  - `ship.LocalPos.Scale = 1e-4` m/unit (Planet space)
  - Ship offset in observer units: `7e6 / 1e-4 = 7e10` units along Z
  - Parent render Z: `-(7e10) * 1e-8 = -700 render units` (correct)
  - Planet radius observer units: `6.371e6 / 1e-4 = 6.371e10` units
  - Planet render radius: `6.371e10 * 1e-8 = 637 render units` → surface 63 render units from ship (planet looms nearby)
- **Plan 01-02 contract:** per-body radii use the same `meters / ship.LocalPos.Scale * factor` transform — no change to radius path.
- **Files modified:** `Scripts/Render/RenderBridge.cs`
- **Commit:** `e9212da`
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
- **DefaultBodyRadius = 6.371e6f** in `RenderBridge.cs` — uniform Earth-radius sphere for all bodies (expressed in true meters; converted to observer units then multiplied by per-space factor at mesh creation); true 1:1 per-body radii and materials arrive in Plan 02 (RND-03/04) using the same meters→observer-units→×factor transform
- **Speed display: raw m/s only** in `Hud.cs` — adaptive unit ladder (m/s → km/s → AU/s → ly/s) deferred to Plan 04 (HUD-01 full)
- **Forward-only thrust (+Z)** in `TestSetup._Process` — attitude-oriented (Basis) motion arrives in Plan 02 (FLT-02)

## Threat Flags

No new threat surface introduced beyond what the plan's threat model covers.

## Self-Check: PASSED (post-checkpoint fix + unit-space render continuation + rendering correctness fixes included)

Files exist:
- Scripts/Universe/GameWorld.cs: FOUND
- Scripts/Render/RenderBridge.cs: FOUND
- Scripts/HUD/Hud.cs: FOUND
- Scripts/Universe/TestSetup.cs: FOUND
- Scripts/Universe/Math/UniVec3.cs: FOUND (ToLocalDoubleUnits + ToDouble3Units)
- Main.tscn: FOUND
- project.godot: FOUND

Commits verified:
- b31161c (Task 1 — feat: iterative SOI transition): FOUND
- f51ba3b (Task 2 — feat: RenderBridge + thrust): FOUND
- 0f0cd61 (Task 3 — feat: Hud.cs): FOUND
- 50c6338 (deviation — global RenderScale): FOUND
- f6f1a35 (deviation — unshaded material, remove DirectionalLight): FOUND
- f403b7a (continuation — unit-space render via ToLocalDoubleUnits + per-space factors): FOUND
- e9212da (fix — observer-scale unit basis + parent-at-frame-origin rendering): FOUND

Build: 0 errors, 0 warnings confirmed (all commits).
