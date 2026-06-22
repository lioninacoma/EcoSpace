---
phase: 07-autopilot-warp-drive
plan: "01"
subsystem: flight
tags: [warp, state-machine, look-around, input-map, speed-cap]
dependency_graph:
  requires: []
  provides:
    - FlightController.EngageWarp / DisengageWarp (consumed by Plan 02 WarpConfirmationScreen)
    - FlightController.IsWarping (consumed by Plan 02 HUD cosmetic)
    - WarpState enum (Manual / Confirming / Warping)
    - ManualMaxSpeed / WarpMaxSpeed / WarpOrientRate exports
    - warp_engage (J) and look_around (Left Alt) InputMap actions
  affects:
    - FlightController._Process (state switch replaces linear call chain)
    - FlightController.UpdateAttitude (look-around branch + new camera write)
    - FlightController.UpdateSpeedEnvelope (ManualMaxSpeed clamp inserted)
tech_stack:
  added: []
  patterns:
    - WarpState enum switch in _Process (three-state: Manual/Confirming/Warping)
    - _cameraOffset Basis field + UpdateLookAround for look-around decoupling
    - UniMath.Distance / UniMath.RelativeMetres for cross-frame warp navigation
    - Quaternion.Slerp for smooth warp auto-orient
    - System.Math.Max(0.0, value) setter guard on all new exports (T-07-05)
key_files:
  modified:
    - project.godot
    - Scripts/Flight/FlightController.cs
  created: []
decisions:
  - "enum WarpState { Manual, Confirming, Warping } — three states route _Process; Confirming is an empty pass-through while WarpConfirmationScreen owns input (D-04)"
  - "ManualMaxSpeed = 1e6 m/s default applied only when _warpState == Manual; tier ceiling / proximity damp still run for autopilot (D-09/D-10)"
  - "DisengageWarp does NOT zero CurrentSpeed — leaves _easedSpeed for UpdateSpeedEnvelope lerp to ease down to ManualMaxSpeed (D-19 invariant)"
  - "_selectedTravelTimeSec is NEVER decremented — warp speed = dist / constant, deceleration from shrinking dist (D-06, Pitfall 1)"
  - "UpdateLookAround handles both accumulate (Alt held) and ease-back (Alt released) paths; called from UpdateAttitude (manual) and _Process/Warping (warp)"
  - "Left Alt physical_keycode 4194326 used as specified in RESEARCH.md A2; requires editor verification before play-test"
metrics:
  duration_minutes: 7
  completed_date: "2026-06-22"
  tasks_completed: 3
  tasks_total: 3
  files_modified: 2
---

# Phase 07 Plan 01: FlightController Warp State Machine + Look-Around Summary

**One-liner:** Added WarpState enum (Manual/Confirming/Warping), ManualMaxSpeed cap (1e6 m/s), warp auto-navigate via UniMath.Distance + Quaternion.Slerp, look-around camera decoupling via `_cameraOffset`, and two InputMap actions (J / Left Alt).

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add warp_engage (J) and look_around (Left Alt) InputMap actions | 35b35e2 | project.godot |
| 2 | Add WarpState enum, speed-cap + warp exports, ManualMaxSpeed clamp | 3771be0 | Scripts/Flight/FlightController.cs |
| 3 | Add _Process state switch, EngageWarp/DisengageWarp, _WarpProcess, look-around camera | 8c056c3 | Scripts/Flight/FlightController.cs |

## What Was Built

### project.godot (Task 1)

Two new InputMap actions appended to the `[input]` section using the exact `Object(InputEventKey,...)` serialization format of the existing `toggle_target_panel` entry:

- `warp_engage`: physical_keycode 74 (J key), unicode 106 — opens warp confirmation screen
- `look_around`: physical_keycode 4194326 (Left Alt), unicode 0 — activates look-around camera

### FlightController.cs (Tasks 2 + 3)

**New enum and state field:**
- `private enum WarpState { Manual, Confirming, Warping }` and `_warpState = WarpState.Manual`

**New `[Export]` properties (all with `System.Math.Max(0.0, value)` setter guard per T-07-05):**
- `ManualMaxSpeed` (backing `_manualMaxSpeed = 1e6`) — manual flight speed cap
- `WarpMaxSpeed` (backing `_warpMaxSpeed = 2e20`) — technical warp safety cap
- `WarpOrientRate` (backing `_warpOrientRate = 1.5`) — Slerp weight rate for auto-orient

**New private fields:**
- `_cameraOffset = Basis.Identity` — look-around offset accumulated while Alt held
- `_lookEaseRate = 1f / 0.3f` — ease-back rate (~0.3 s, D-13)
- `_selectedTravelTimeSec = 120.0` — default 2-minute travel time per session (D-17)

**Modified `_Process`:** Replaced linear call chain with `switch (_warpState)`. Panel gate updated to `if (IsPanelOpen && _warpState == WarpState.Manual) return;` (Pitfall 3 fix — warp must keep running when panel could be open transiently).

**Modified `UpdateSpeedEnvelope`:** Inserted `if (_warpState == WarpState.Manual) targetMax = System.Math.Min(targetMax, _manualMaxSpeed);` after the targetEaseMax clamp, before the easing lerps. Tier ceiling (D-40) and proximity damp (D-42) computation is unchanged; manual flight is simply capped before the lerp target (D-10).

**Modified `UpdateAttitude`:** Branched on `Input.IsActionPressed("look_around")`:
- Held: calls `UpdateLookAround(delta)` which accumulates `_cursor` into `_cameraOffset`; roll suspended (Pitfall 6); `_shipBasis` orthonormalized but not mutated (D-12)
- Not held: normal steering into `_shipBasis` + calls `UpdateLookAround(delta)` for ease-back
- Camera write changed from `_camera.Basis = _shipBasis` to `_camera.Basis = (_shipBasis * _cameraOffset).Orthonormalized()` (T-07-04)

**New `UpdateLookAround(double delta)`:** Handles both look-around paths:
- Alt held: accumulates `_cursor` steer into `_cameraOffset` via `(* pitchBasis * yawBasis).Orthonormalized()` (T-07-04 mitigation)
- Alt released: `Quaternion.Slerp` ease-back toward `Quaternion.Identity`, `Orthonormalized()` on result (D-13)

**New `public void EngageWarp(double travelTimeSec)`:** Applies `System.Math.Max(1.0, travelTimeSec)` (T-07-01 divisor guard), zeroes `_cursor` (prevents stale steering delta on warp entry), sets `_warpState = WarpState.Warping`.

**New `public void DisengageWarp()`:** Sets `_warpState = WarpState.Manual` only — does NOT zero `CurrentSpeed` (D-19: speed eases to ManualMaxSpeed naturally via UpdateSpeedEnvelope lerp).

**New `private void _WarpProcess(double delta)`:**
- Bounds-safe ship/target lookup with `(uint)idx < (uint)gameObjects.Count` pattern
- `double dist = UniMath.Distance(ship, target, gameObjects)` — only safe cross-frame distance (CLAUDE.md §Position Math)
- SOI disengage: `if (dist < target.SOIMeters) { DisengageWarp(); return; }` (D-08)
- Speed: `System.Math.Min(dist / _selectedTravelTimeSec, _warpMaxSpeed)` — `_selectedTravelTimeSec` NEVER decremented (Pitfall 1 / D-06)
- IsFinite guard: `if (!double.IsFinite(warpSpeed)) { DisengageWarp(); return; }` (T-07-02)
- Auto-orient: `UniMath.RelativeMetres` direction → `right.LengthSquared() < 1e-6f` degenerate guard (Pitfall 4 / T-07-03) → `Quaternion.Slerp` into `_shipBasis.Orthonormalized()` (D-03)
- Writes `_easedSpeed = warpSpeed; CurrentSpeed = _easedSpeed;` for `ApplyMotion` to consume

**New `public bool IsWarping`:** Read accessor `=> _warpState == WarpState.Warping` for HUD/Plan-02 cosmetic display.

## Verification Results

- `dotnet build EcoSpace.csproj`: **0 errors, 0 warnings** (all three tasks)
- `grep -c 'warp_engage\|look_around' project.godot`: **2** (both actions present)
- `_selectedTravelTimeSec` decrement check: **0** occurrences (Pitfall 1 satisfied)
- Manual behavior unchanged except: manual speed capped at ManualMaxSpeed (1e6 m/s) and Alt activates look-around camera

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Enhancement] Unified UpdateLookAround to handle both accumulate and ease-back paths**

- **Found during:** Task 3
- **Issue:** Plan Task 3A describes `UpdateLookAround` handling both paths (accumulate when held, ease-back when released), but Task 3B's phrasing "call the ease-back branch of UpdateLookAround" implied the method only did ease-back. Implementing only ease-back would have required duplicate accumulation logic in both UpdateAttitude (manual) and the Warping case of _Process.
- **Fix:** Implemented `UpdateLookAround` to check `Input.IsActionPressed("look_around")` internally and handle both branches. Both callers (UpdateAttitude manual-else and _Process Warping case) call `UpdateLookAround(delta)` unconditionally. This matches Plan Task 3A's intent exactly and avoids duplicate code.
- **Files modified:** `Scripts/Flight/FlightController.cs`
- **Commit:** 8c056c3

No other deviations. All threat model mitigations (T-07-01 through T-07-05) implemented as specified.

## Known Stubs

None. All implemented symbols are functional. No placeholder values or TODO markers exist in modified files.

## Threat Flags

None. No new network endpoints, auth paths, file access patterns, or schema changes introduced. The only trust boundaries in this plan (editor exports to flight math, warp screen to FlightController, world state to warp math) are fully mitigated per the plan's threat register.

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `.planning/phases/07-autopilot-warp-drive/07-01-SUMMARY.md` exists | FOUND |
| `project.godot` exists | FOUND |
| `Scripts/Flight/FlightController.cs` exists | FOUND |
| Commit 35b35e2 (Task 1) exists | FOUND |
| Commit 3771be0 (Task 2) exists | FOUND |
| Commit 8c056c3 (Task 3) exists | FOUND |
