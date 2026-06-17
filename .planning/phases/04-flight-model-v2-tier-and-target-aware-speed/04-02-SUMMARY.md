---
phase: 04-flight-model-v2-tier-and-target-aware-speed
plan: "02"
subsystem: hud-target-circle
tags: [hud, target-circle, draw, world-pinned, d-46]
dependency_graph:
  requires:
    - 04-01 (ActiveTargetIndex accessor)
  provides:
    - Hud._Draw override with DrawArc phosphor-green outline
    - Hud.UpdateTargetCircle helper (render-set gate + camera-projection guards)
    - Hud._worldRenderer reference (WorldRenderer.GetRenderPosition gate)
    - Hud._showTargetCircle / _targetCirclePos / _targetCircleRadius circle-state fields
  affects:
    - Scripts/Hud/Hud.cs
tech_stack:
  added: []
  patterns:
    - Control._Draw + QueueRedraw pattern for per-frame 2D overlay in a 3D scene
    - WorldRenderer.GetRenderPosition as render-set gate (only draw when body is a current-space mesh)
    - Camera3D.UnprojectPosition + camLocal.Z > 0 behind-camera guard (reused from UpdateDirectionMarker)
    - Minimum-radius floor clamp for D-46 findability guarantee
key_files:
  created: []
  modified:
    - Scripts/Hud/Hud.cs
decisions:
  - "D-46: world-pinned outline drawn only when target body is in WorldRenderer rendered set (GetRenderPosition gate)"
  - "MIN_CIRCLE_RADIUS=20f / MAX_CIRCLE_RADIUS=200f as private const play-test knobs (D-46 discretion)"
  - "_worldRenderer resolved via FindChild (no [Export] NodePath) — matches _steeringReticle no-export style"
  - "DrawArc not DrawCircle — unfilled outline matches retro aesthetic"
metrics:
  duration: "~2 min"
  completed: "2026-06-17T18:40:00Z"
  tasks: 2
  files_modified: 1
---

# Phase 04 Plan 02: World-Pinned Target Circle Summary

**One-liner:** Phosphor-green world-pinned outline circle in `Hud._Draw`, gated on `WorldRenderer.GetRenderPosition`, with a `20px` minimum on-screen radius and behind-camera/off-screen suppression — D-46 delivered.

## Status

Tasks 1 and 2 complete. Paused at Task 3 (checkpoint:human-verify — in-game play-test). Awaiting human verification before plan is marked complete.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add _worldRenderer reference + circle-state fields to Hud | 4a7f9e1 | Scripts/Hud/Hud.cs |
| 2 | Implement UpdateTargetCircle + _Draw + QueueRedraw wiring | 2f3e5b4 | Scripts/Hud/Hud.cs |

## What Was Built

### Task 1 — `_worldRenderer` reference + circle-state fields

Added to `Scripts/Hud/Hud.cs`:

- `private Render.WorldRenderer _worldRenderer;` in the private-references section (line 56)
- Three circle-state fields (lines 60–62):
  - `private bool _showTargetCircle;`
  - `private Vector2 _targetCirclePos;`
  - `private float _targetCircleRadius;`
- `_worldRenderer` resolved in `_Ready` via `GetTree().Root.FindChild("WorldRenderer", true, false) as Render.WorldRenderer` — mirrors the `_steeringReticle` no-export style; callers use `?.`

### Task 2 — `UpdateTargetCircle` + `_Draw` + `QueueRedraw`

Added to `Scripts/Hud/Hud.cs`:

**`UpdateTargetCircle(UniObject ship, List<UniObject> gameObjects)` helper (5-guard pattern):**

1. Sets `_showTargetCircle = false` at the top (mirrors `HideMarker()` discipline)
2. Guard 1: null-guard `_worldRenderer` and `_camera`
3. Guard 2: resolve active target via `BuildTargetableList` (current-tier reach, D-45/D-12) — returns early if list empty
4. Guard 3: render-set gate — `!_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos)` returns false when the body is not a current-space mesh; leaves circle off and edge marker handles findability (D-46)
5. Guard 4: behind-camera check — `camLocal.Z > 0` guard mirrors `UpdateDirectionMarker` verbatim (Pitfall 6 / T-04-04 mitigated)
6. Guard 5: off-screen bounds check against `viewport.GetVisibleRect().Size`
7. Clamps `_targetCircleRadius = Mathf.Clamp(MIN_CIRCLE_RADIUS, MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS)` — D-46 "never a sub-pixel speck" minimum floor
8. Sets `_showTargetCircle = true` only after all guards pass

**`_Draw()` override:**

```csharp
public override void _Draw()
{
    if (!_showTargetCircle) return;
    DrawArc(_targetCirclePos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
}
```

Unfilled arc (outline only) using existing `PhosphorGreen` export — no new color. `DrawArc` API confirmed correct in the build (A1/A2 assumptions verified by 0-error build).

**`_Process` wiring:**
- `UpdateTargetCircle(ship, gameObjects)` called after `UpdateTargetReadout` each frame
- `QueueRedraw()` called immediately after (Pitfall 4 — `_Draw` is on-demand, not per-frame)

**Read-only contract honored:** `UpdateTargetCircle` never assigns to `_targetIndex` or any `GameObjects` element. `_targetIndex` is only read (via `Mathf.Clamp` local expression) inside the helper.

**Play-test knobs:**
- `private const float MIN_CIRCLE_RADIUS = 20f` — minimum on-screen radius (D-46 floor)
- `private const float MAX_CIRCLE_RADIUS = 200f` — maximum cap

## Verification Results (structural)

```
dotnet build: 0 errors, 0 warnings ✓
private Render.WorldRenderer _worldRenderer: Hud.cs:56 ✓
_showTargetCircle field: Hud.cs:60 ✓
_targetCirclePos field: Hud.cs:61 ✓
_targetCircleRadius field: Hud.cs:62 ✓
FindChild("WorldRenderer"): Hud.cs:138 ✓
public override void _Draw: Hud.cs:398 ✓
DrawArc(: Hud.cs:403 ✓
GetRenderPosition(: Hud.cs:367 ✓
UnprojectPosition(: Hud.cs:382 ✓
camLocal.Z > 0: Hud.cs:376 ✓
QueueRedraw(): Hud.cs:177 ✓
_showTargetCircle = false: Hud.cs:354 ✓
_targetIndex not mutated in UpdateTargetCircle: confirmed ✓
```

Behavioral verification (on-screen circle, min-radius findability, edge-marker fallback) gated to Task 3 in-game play-test checkpoint below.

## Deviations from Plan

None — plan executed exactly as written. `DrawArc` API (A1/A2 medium-confidence assumption) confirmed correct by 0-error build; no fallback to `DrawPolyline` was needed. `const float` used for MIN_R/MAX_R rather than `[Export]` — simpler and sufficient for the minimal D-46 slice; can be promoted to `[Export]` during the play-test tuning session if the inspector adjustment is preferred.

## Known Stubs

None — all new symbols are wired to live data sources. `_worldRenderer` resolves to the scene's `WorldRenderer` node; `GetRenderPosition` is authoritative each frame; `_camera` was already wired in prior plans. The circle is a live read of real frame data, not placeholder logic.

## Threat Flags

No new security-relevant surface beyond the plan's threat model:
- T-04-04 (behind-camera mirroring): mitigated by `camLocal.Z > 0` guard (Hud.cs:376)
- T-04-05 (stale/missing render position): mitigated by `GetRenderPosition` returning false, leaving `_showTargetCircle = false`
- T-04-06 (per-frame QueueRedraw cost): accepted (negligible, per plan)

## Awaiting: Task 3 Play-test Checkpoint

Task 3 is a `checkpoint:human-verify` gate — the complete Phase 04 behavior must be verified in-game. See checkpoint message below.

## Self-Check: PASSED (Tasks 1–2)

- `Scripts/Hud/Hud.cs` — exists with all new symbols confirmed
- Commit 4a7f9e1 — Task 1 (_worldRenderer + circle-state fields)
- Commit 2f3e5b4 — Task 2 (UpdateTargetCircle + _Draw + QueueRedraw)
- Both commits on main branch, build 0/0
