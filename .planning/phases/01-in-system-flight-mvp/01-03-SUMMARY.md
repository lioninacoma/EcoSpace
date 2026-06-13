---
phase: 01-in-system-flight-mvp
plan: "03"
subsystem: flight-controller
tags: [godot, csharp, flight, input, virtual-joystick, speed-scaling, hud, reticle]

requires:
  - "01-01: Walking Skeleton (ShipIndex, TranslatePos, GameWorld foundation)"
  - "01-02: Body Rendering (UniObject.RadiusMeters for surface-distance speed scaling)"

provides:
  - "FlightController: virtual-joystick steering + roll + persistent throttle + distance-scaled speed"
  - "Fixed center crosshair (HUD-03) and separate moving steering reticle (D-05) in Main.tscn"
  - "InputMap actions: roll_left (Q), roll_right (E), throttle_up (W/scroll), throttle_down (S/scroll), full_stop (X), cycle_target (Tab)"
  - "FlightController read-only accessors: CurrentSpeed, Throttle01, SteerCursor, ShipBasis"

affects:
  - "04-hud-polish (FlightController.CurrentSpeed / Throttle01 / SteerCursor / ShipBasis for HUD display)"

tech-stack:
  added:
    - "Scripts/Flight/FlightController.cs (new Node with [Export] tuning knobs)"
    - "Main.tscn: Crosshair (fixed center Control) + SteeringReticle (moving Control) + FlightController node"
    - "project.godot: roll_left/roll_right/throttle_up/throttle_down/full_stop/cycle_target InputMap actions"
  patterns:
    - "Virtual-joystick: accumulated _cursor with LimitLength clamp; deadzone → hold-attitude"
    - "Attitude: Basis multiply + Orthonormalized() every frame (drift-free, T-03-01)"
    - "Distance-scaled speed: nearest-surface Distance-RadiusMeters → Lerp-eased contextMax → throttle × contextMax"
    - "Reticle: Control.Position = viewportCenter + cursor - halfSize (top-left placement correction)"
    - "Mouse mode: Confined (keeps OS cursor in window; steering uses software cursor only, Pitfall 8)"

key-files:
  created:
    - "Scripts/Flight/FlightController.cs"
  modified:
    - "Scripts/Universe/TestSetup.cs — removed skeleton thrust _Process motion; FlightController owns motion"
    - "project.godot — added 6 new InputMap actions"
    - "Main.tscn — added Crosshair, SteeringReticle Controls + FlightController node"

key-decisions:
  - "Distance-scaled speed envelope implemented inline in Task 1 (Tasks 1+2 share FlightController.cs; no separate commit needed)"
  - "Mouse mode = Confined (not Captured): OS cursor stays in window; software cursor drives steering (Pitfall 8)"
  - "Basis roll uses Vector3.Back (+Z) as roll axis: local roll around forward (-Z) = rotation about +Z"
  - "SteeringReticle position = viewportCenter + cursor - halfSize to center 16×16 control on cursor"
  - "cycle_target (Tab) defined here as Tab physical keycode (4194305) for Plan 04 use"

metrics:
  duration: "~4 min"
  completed: "2026-06-13"
  tasks_completed: 3
  tasks_total: 4
  files_modified: 4
---

# Phase 01 Plan 03: Flight Controller Summary

**Virtual-joystick mouse steering + persistent throttle + Q/E roll + distance-scaled speed envelope (crawl near bodies, huge in open space, no SOI snap) + fixed crosshair and moving reticle HUD**

## Performance

- **Duration:** ~4 min
- **Completed:** 2026-06-13
- **Tasks:** 3 of 4 auto tasks complete (Task 4 = checkpoint:human-verify, pending)
- **Files modified:** 4

## Accomplishments

### Task 1: Virtual-joystick steering + roll + persistent throttle (FLT-01, FLT-02)

- Created `Scripts/Flight/FlightController.cs` as `public partial class FlightController : Node` in namespace `Universe`
- Software cursor `_cursor` accumulates `InputEventMouseMotion.Relative * Sensitivity` and is clamped with `LimitLength(MaxCursorRadius)` — no absolute mouse query (Pitfall 8 avoided)
- Attitude: steer = `_cursor / MaxCursorRadius`; deadzone check → zero rotation in deadzone (hold-attitude D-02); outside deadzone: yaw/pitch applied as local `Basis` multiply; `Orthonormalized()` every frame (T-03-01 mitigation)
- Roll: `Input.GetActionStrength("roll_left") - roll_right) * RollRate * delta` composed into Basis
- Throttle: persistent `_throttle01` [0,1] incremented/decremented on `throttle_up`/`throttle_down` just-pressed; zeroed on `full_stop`; clamped to [0,1] (T-03-04)
- Camera basis aligned to `_shipBasis` each frame — player viewpoint tracks ship attitude
- Mouse mode set to `Confined` — OS cursor stays in window; steering uses software cursor only
- Removed skeleton thrust motion from `TestSetup._Process` — `FlightController.ApplyMotion` now calls `TranslatePos`
- Added 6 InputMap actions to `project.godot`

### Task 2: Distance-scaled speed envelope (FLT-03, D-06/D-07/D-08)

- Implemented inline in Task 1's `FlightController.cs` `UpdateSpeedEnvelope()` method
- Scans ship's parent's `ChildIndices` for nearest surface distance: `UniVec3.Distance - body.RadiusMeters`, clamped at 0
- `targetMax = Clamp(nearest * SpeedPerMeter, MinSpeed, MaxSpeed)` (shape is tuning discretion)
- `_contextMax` lerped toward `targetMax` each frame: `Mathf.Lerp(_contextMax, targetMax, SpeedEasing * delta)` — absorbs SOI-boundary discontinuities (D-07, Pitfall 9)
- `actualSpeed = _throttle01 * _contextMax` — one control, auto-scaled (D-08)
- `CurrentSpeed` exposed read-only for HUD
- Speed capped at SpeedOfLight = 3e8 m/s absolute upper bound (T-03-02)

### Task 3: Fixed crosshair + moving steering reticle (HUD-03, D-05)

- `Main.tscn`: Added `Crosshair` Control anchored at viewport center (anchors 0.5/0.5) with phosphor-green `CrossH` and `CrossV` ColorRect arms — fixed nose-forward marker (HUD-03)
- Added `SteeringReticle` Control; `FlightController._steeringReticle` found by name in `_Ready`; positioned each frame at `viewportCenter + _cursor - halfSize` to center the 16×16 cross on the cursor offset
- Both reticles phosphor-green `Color(0.1, 1, 0.3, 1)` matching existing HUD aesthetic (D-09)
- Added `FlightController` node to scene with `WorldPath = NodePath("..")` and `CameraPath = NodePath("../Camera3D")`

## Task Commits

1. **Task 1+2: Virtual-joystick steering + speed envelope** - `b0599d2` (feat)
2. **Task 3: Crosshair + reticle + FlightController node** - `23e1f2a` (feat)
3. **Fix: Reticle centering (subtract halfSize)** - `0f32d3e` (fix)

## Files Created/Modified

- `Scripts/Flight/FlightController.cs` — NEW: full arcade flight model with [Export] tuning knobs
- `Scripts/Universe/TestSetup.cs` — removed SkeletonSpeed export + skeleton thrust _Process motion
- `project.godot` — 6 new InputMap actions added
- `Main.tscn` — Crosshair, SteeringReticle Controls + FlightController node added

## Deviations from Plan

### Tasks 1 and 2 implemented in a single file/commit

**[Deviation - Planning] Distance-scaled speed envelope (Task 2) implemented inline with Task 1**
- **Found during:** Task 1 implementation
- **Reason:** Both tasks modify `Scripts/Flight/FlightController.cs` exclusively. Since the Task 1 plan includes a placeholder "temporary constant max" that Task 2 immediately replaces, implementing the complete final form in one pass avoids a trivial intermediate state commit with incorrect behavior.
- **Impact:** No separate commit for Task 2's changes; all Task 2 acceptance criteria are met and verified in `b0599d2`.
- **Build result:** 0 errors, 0 warnings on both the Task 1 and Task 2 verification checks.

### Reticle centering fix (Rule 1 — Bug)

**[Rule 1 - Bug] SteeringReticle positioned at (center + cursor) instead of centering the control on cursor**
- **Found during:** Task 3 post-implementation review
- **Issue:** `Control.Position` in Godot 4 sets the top-left corner of the control, not its center. Setting `Position = viewportCenter + cursor` places the top-left at center+cursor, offset by the control's half-size from the intended cursor position.
- **Fix:** `Position = viewportCenter + cursor - halfSize` where `halfSize = _steeringReticle.Size / 2f`
- **Files modified:** `Scripts/Flight/FlightController.cs`
- **Commit:** `0f32d3e`

## Known Stubs

- **Throttle_up / throttle_down both bound to W/S**: These actions currently share physical keycodes with the old `thrust_forward`/`thrust_back` actions. The old actions remain in `project.godot` for backwards compatibility but are no longer read by any active script. Plan 04 may clean this up.
- **cycle_target (Tab)**: Defined in InputMap now but not read by any script yet — reserved for Plan 04 HUD target cycling (HUD-04).
- **Crosshair and SteeringReticle**: Currently plain cross shapes drawn via ColorRect arms. More elaborate reticle art (target brackets, etc.) deferred to Plan 04 or beyond.

## Threat Flags

No new threat surface beyond the plan's threat model. All STRIDE threats mitigated:
- T-03-01: Orthonormalized() every frame
- T-03-02: MaxSpeed clamped to SpeedOfLight; Lerp-eased contextMax
- T-03-03: LimitLength(MaxCursorRadius) every input event
- T-03-04: Throttle Mathf.Clamp to [0,1]

## Self-Check: PASSED

Files exist:
- Scripts/Flight/FlightController.cs: FOUND
- Scripts/Universe/TestSetup.cs: FOUND (skeleton thrust removed)
- project.godot: FOUND (roll_left, full_stop, throttle_up verified)
- Main.tscn: FOUND (Crosshair, SteeringReticle, FlightController nodes)

Commits verified:
- b0599d2 (feat(01-03): virtual-joystick steering + roll + persistent throttle): FOUND
- 23e1f2a (feat(01-03): fixed center crosshair + moving steering reticle): FOUND
- 0f32d3e (fix(01-03): center reticle on cursor by subtracting half-size): FOUND

Build: 0 errors, 0 warnings confirmed.
