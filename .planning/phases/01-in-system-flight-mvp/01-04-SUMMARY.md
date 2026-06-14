---
phase: 01-in-system-flight-mvp
plan: "04"
subsystem: hud
status: checkpoint-failure-fixed-re-presenting-human-verify
tags: [godot, csharp, hud, ui, phosphor-green, adaptive-units, target-cycle, direction-marker, tdd]

requires:
  - "01-01: Walking Skeleton (GameWorld, ShipIndex, TestSetup)"
  - "01-02: Body Rendering (UniObject.Name, RadiusMeters, BaseColor for nearest-body scan)"
  - "01-03: FlightController (CurrentSpeed accessor; cycle_target InputMap action)"

provides:
  - "Hud.FormatSpeed(double): adaptive m/s→km/s→AU/s→ly/s unit ladder (D-10, HUD-01)"
  - "Hud.FormatDistance(double): same ladder without /s suffix"
  - "Context label: SpaceTierName + nearest-body scan per frame (D-11, HUD-02)"
  - "Target cycle: _targetIndex over current-space siblings on Tab/cycle_target (D-12, HUD-04)"
  - "Off-screen edge direction marker pointing toward active target (findability resolution)"
  - "Phosphor-green CRT aesthetic throughout; magenta removed from FPSLabel (D-09)"
  - "HudFormatTests: runtime smoke-test assertions for FormatSpeed/FormatDistance bands"

affects:
  - "TRV-01: integration milestone — all five slices composed"

key-files:
  created:
    - Scripts/Hud/HudFormatTests.cs
  modified:
    - Scripts/Hud/Hud.cs
    - Main.tscn

decisions:
  - "DirMarker placed as direct child of CanvasLayer (not Hud) so position math is in full viewport coords"
  - "_Input used for cycle_target to ensure mouse_filter=Ignore cannot swallow the key event"
  - "Task 3 phosphor-green recolor was subsumed in Task 1/2 commits; no separate files to change"

metrics:
  duration: "~4 minutes (Tasks 1-3)"
  completed_date: "2026-06-14"
  tasks_attempted: 4
  tasks_completed: 3
  files_created: 1
  files_modified: 2
---

# Phase 01 Plan 04: Minimal HUD Summary

**One-liner:** Phosphor-green HUD with adaptive-unit speed (m/s→ly/s), space-tier context label, cycle-able target readout with off-screen edge direction marker — completing the TRV-01 in-system-flight milestone. AWAITING HUMAN VERIFY.

## Status

**Tasks 1–3 complete** (3 commits). Task 4 is a `checkpoint:human-verify` gate — the player must play-test the full HUD loop before this plan is marked complete.

## Tasks Completed

| Task | Name | Commit | Status |
|------|------|--------|--------|
| 1 | Adaptive speed units + context label (TDD) | `0566018` (RED), `ed731ee` (GREEN) | Done |
| 2 | Target cycle + off-screen direction marker | `eac7d11` | Done |
| 3 | Phosphor-green CRT recolor | subsumed in Task 1/2 | Done |
| 4 | Human-verify checkpoint | — | **RE-PRESENTING** |
| 4a | Fix: parent body in target/nearest set | `55c70ad` | Done |

## What Was Built

### Task 1 — Adaptive Units + Context Label (HUD-01/02, D-10/D-11)

**TDD executed correctly:**
- RED: `HudFormatTests.cs` added with 9 assertions referencing `Hud.FormatSpeed` and `Hud.FormatDistance` — build failed with CS0117 (confirmed RED gate)
- GREEN: `FormatSpeed`/`FormatDistance` implemented; `HudFormatTests.Run()` called in `_Ready()` — all 9 assertions pass

`FormatSpeed` follows the RESEARCH Code Examples exactly:
```
< 1 km/s  → "NNN.N m/s"
< 1 AU/s  → "NNN.N km/s"
< 1 ly/s  → "N.NNN AU/s"
≥ 1 ly/s  → "N.NNN ly/s"
```
Zero is handled by the m/s band — no division-by-zero (T-04-03 mitigation).

Context label built per frame: `SpaceTierName(ship.CurrentSpace) + " · nearest: " + nearestBodyName`, where nearest is found by scanning `parent.ChildIndices` for minimum `UniVec3.Distance` (null-skipped, ship-skipped, T-04-01 mitigation).

`Main.tscn` additions: `ContextLabel`, `TargetLabel` (both phosphor-green), `DirMarker` (initially hidden).

### Task 2 — Target Cycle + Off-Screen Direction Marker (HUD-04/D-12)

- `cycle_target` (Tab) action handled in `_Input` (not `_UnhandledInput`) to bypass any Control mouse_filter swallowing
- `_targetIndex` advances modulo sibling count on Tab; clamped per frame so it stays valid across SOI transitions (T-04-02 mitigation)
- `TargetLabel` shows `"TGT  <name> · <FormatDistance(dist)>"` each frame
- `BuildSiblingList` null-skips all `GameObjects` slots (T-04-01 mitigation)
- `DirMarker` placed as direct child of `CanvasLayer` (not a child of `Hud`) so position math is in full viewport coordinates without parent-offset compensation
- Off-screen detection: camera-local Z > 0 (behind camera) OR projected screen pos outside viewport bounds
- When off-screen: marker pinned to viewport edge in the direction of the target, rotated to point; hides when on-screen

### Task 3 — Phosphor-Green CRT Recolor (D-09)

Subsumed in Tasks 1 and 2:
- `FPSLabel` magenta `Color(1, 0, 1, 1)` → phosphor-green `Color(0.1, 1, 0.3, 1)` in Task 1's Main.tscn write
- All new labels (`SpeedLabel`, `ContextLabel`, `TargetLabel`) created phosphor-green
- `DirMarker`/`Arrow` created phosphor-green
- `PhosphorGreen` export on `Hud` applies programmatically via `ApplyPhosphorGreen()` in `_Ready()`
- Zero magenta `Color(1, 0, 1, 1)` remains in `Main.tscn` (verified by grep)

## Checkpoint Failure and Fix (2026-06-14)

Human-verify checkpoint FAILED on items 3 and 4:
- Item 3 (context nearest body): showed `nearest: ---` while orbiting Planet A
- Item 4 (target readout + Tab cycle): showed `TGT ---` and Tab cycled nothing

**Root cause:** `BuildSiblingList` returned the ship's siblings within Planet A's `ChildIndices` — which only contained the ship itself, yielding an empty list. The parent body (Planet A) was never in the targetable set. `WorldRenderer` correctly renders the PARENT body at the frame origin, but `Hud` was only scanning siblings.

**Fix (commit `55c70ad`):**
- `BuildSiblingList` replaced by `BuildTargetableList` which adds the parent body first (with `IsParent=true`), then iterates siblings as before. Count is now ≥ 1 whenever the ship has a valid parent.
- New `TargetEntry` struct (Index + IsParent flag) drives the correct math path.
- New `GetRelativeMeters(ship, body, isParent)` helper: for the parent, returns `ship.LocalPos.ToDouble3() * -1.0` (parent sits at SOI origin — its LocalPos is in the grandparent frame, not the ship's frame); for siblings, returns `body.LocalPos.ToLocalDouble(ship.LocalPos)` as before.
- `UpdateContextLabel`, `UpdateTargetReadout`, `_Input` cycle handler all updated to use `BuildTargetableList` and `GetRelativeMeters`.
- `UpdateDirectionMarker` now accepts a precomputed `Double3 relD` (no longer calls `targetObj.LocalPos.ToLocalDouble` which was wrong for the parent).

Expected behaviour after fix:
- Orbiting Planet A: `PLANET SPACE · nearest: PLANET A` and `TGT  PLANET A · <dist>`; Tab cycles to Planet A only (only one targetable body in Planet SOI).
- After exiting to Star space: `STAR SPACE · nearest: <closest of Star/Planet A/Planet B>` and Tab cycles Star → Planet A → Planet B.

## Deviations from Plan

### Path Corrections (Pre-Approved)

**Files referenced in plan → actual files used:**
- `Scripts/HUD/Hud.cs` → `Scripts/Hud/Hud.cs` (folder case; namespace `Hud`)
- `Scripts/Universe/UniObject.cs` → `Scripts/UniObject.cs` (global namespace)
- `Scripts/Universe/GameWorld.cs` → `Scripts/GameWorld.cs` (global namespace)
- `Scripts/Universe/Math/UniVec3.cs` → `Scripts/Math/UniVec3.cs` (global namespace)

These were documented as a pre-approved deviation in the execution instructions.

### Auto-fixed Issues

**[Rule 2 - Missing Critical Functionality] DirMarker moved out of Hud to CanvasLayer**
- **Found during:** Task 2 implementation review
- **Issue:** `DirMarker` as a child of `Hud` (which has offset_left=5, offset_top=30) would require subtracting the Hud parent offset from all viewport-edge position calculations, making the math fragile
- **Fix:** Moved `DirMarker` to be a direct child of `CanvasLayer` so position coordinates are full viewport coordinates
- **Files modified:** `Main.tscn`, `Hud.cs` (`GetParent()?.GetNodeOrNull<Control>("DirMarker")`)

**[Rule 1 - Architecture Choice] FormatSpeed reads FlightController.CurrentSpeed**
- **Found during:** Task 1 implementation
- **Issue:** The skeleton `Hud.cs` computed speed from a prev-frame position delta — correct but unnecessary now that `FlightController.CurrentSpeed` is the eased, authoritative speed
- **Fix:** `Hud._Process` reads `_flight.CurrentSpeed` directly (null-safe fallback to 0.0) instead of recomputing
- **Files modified:** `Hud.cs`

## Known Stubs

None. All data is wired from live sim state:
- Speed: `FlightController.CurrentSpeed`
- Context tier: `ship.CurrentSpace` (live per frame)
- Nearest body: real scan of `parent.ChildIndices`
- Target: real `_targetIndex` over actual `ChildIndices`
- Distance: real `UniVec3.Distance`

## Threat Surface Scan

No new security surface introduced. HUD is read-only. All three threat register mitigations applied:
- T-04-01: null-safe `(uint)idx < (uint)count` guard + null-skip in all `GameObjects` scans
- T-04-02: `_targetIndex = Mathf.Clamp(_targetIndex, 0, siblings.Count - 1)` per frame
- T-04-03: `FormatSpeed`/`FormatDistance` branch on magnitude bands before dividing; zero stays in m/s band

## Self-Check

### Files exist:
- `Scripts/Hud/Hud.cs`: YES
- `Scripts/Hud/HudFormatTests.cs`: YES

### Commits exist:
- `0566018` (test RED): YES
- `ed731ee` (feat GREEN + context): YES
- `eac7d11` (feat target+marker): YES

## Self-Check: PASSED

All 3 auto tasks committed. Build: 0 errors. Grep verifications: all pass. Awaiting Task 4 human-verify.
