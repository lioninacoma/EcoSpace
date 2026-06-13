---
phase: 01-in-system-flight-mvp
plan: "03"
subsystem: flight-controller
tags: [godot, csharp, flight, input, virtual-joystick, speed-scaling, hud, reticle]

requires:
  - "01-01: Walking Skeleton (ShipIndex, TranslatePos, GameWorld foundation)"
  - "01-02: Body Rendering (UniObject.RadiusMeters for surface-distance speed scaling)"

provides:
  - "FlightController: virtual-joystick steering + roll + persistent throttle [-1,1] + distance-scaled speed"
  - "Fixed center crosshair (HUD-03) and separate moving steering reticle (D-05) in Main.tscn"
  - "InputMap actions: roll_left (Q), roll_right (E), throttle_up (W/scroll), throttle_down (S/scroll), full_stop (X), cycle_target (Tab)"
  - "FlightController read-only accessors: CurrentSpeed, Throttle01, SteerCursor, ShipBasis"
  - "Mouse capture toggle: T key switches between Confined and Captured modes"

affects:
  - "04-hud-polish (FlightController.CurrentSpeed / Throttle01 / SteerCursor / ShipBasis for HUD display)"

tech-stack:
  added:
    - "Scripts/Flight/FlightController.cs (new Node with [Export] tuning knobs)"
    - "Main.tscn: Crosshair (fixed center Control) + SteeringReticle (moving Control) + FlightController node"
    - "project.godot: roll_left/roll_right/throttle_up/throttle_down/full_stop/cycle_target InputMap actions"
  patterns:
    - "Virtual-joystick: accumulated _cursor in _Input (not _UnhandledInput) with LimitLength clamp; deadzone → hold-attitude"
    - "Attitude: Basis multiply + Orthonormalized() every frame (drift-free, T-03-01)"
    - "Distance-scaled speed: ship.LocalPos.Magnitude() - parent.RadiusMeters → Lerp-eased contextMax → throttle × contextMax"
    - "Reticle: Control.Position = viewportCenter + cursor - halfSize (top-left placement correction)"
    - "Mouse mode: T key toggles Confined ↔ Captured; steering uses software cursor accumulation regardless of mode"
    - "Throttle range [-1,1]: W=forward, S=reverse, X eases to zero; enables reverse thrust without a separate input"
    - "SteeringReticle mouse_filter=Ignore (2): prevents Control from consuming MouseMotion events before _Input accumulation"

key-files:
  created:
    - "Scripts/Flight/FlightController.cs"
  modified:
    - "Scripts/Universe/TestSetup.cs — removed skeleton thrust _Process motion; FlightController owns motion"
    - "project.godot — added 6 new InputMap actions"
    - "Main.tscn — added Crosshair, SteeringReticle Controls + FlightController node; set mouse_filter=Ignore on reticle Controls"

key-decisions:
  - "Distance-scaled speed envelope implemented inline in Task 1 (Tasks 1+2 share FlightController.cs; no separate commit needed)"
  - "Mouse mode toggle: T key cycles Confined/Captured — Confined keeps OS cursor visible for debugging, Captured hides it for clean flight"
  - "Basis roll uses Vector3.Back (+Z) as roll axis: local roll around forward (-Z) = rotation about +Z"
  - "SteeringReticle position = viewportCenter + cursor - halfSize to center 16x16 control on cursor"
  - "cycle_target (Tab) defined here as Tab physical keycode (4194305) for Plan 04 use"
  - "DESIGN REFINEMENT D-03: Throttle range extended to [-1,1] for reverse thrust (W=forward, S=reverse, X eases to 0). Approved in play-test session."
  - "Speed envelope root-cause: distance measured to PARENT body (ship.LocalPos.Magnitude() - parent.RadiusMeters), not sibling scan — parent is always the body dominating the ship's space"
  - "Mouse input accumulation moved to _Input (not _UnhandledInput) so Hud/Crosshair/SteeringReticle Controls with mouse_filter=Stop cannot swallow MouseMotion events before FlightController sees them"

requirements-completed: [FLT-01, FLT-02, FLT-03, HUD-03]

metrics:
  duration: "~35 min (including 3 play-test rounds)"
  completed: "2026-06-13"
  tasks_completed: 4
  tasks_total: 4
  files_modified: 4
---

# Phase 01 Plan 03: Flight Controller Summary

**Arcade flight controller: virtual-joystick mouse steering, Q/E roll, persistent [-1,1] throttle with reverse, distance-to-parent-surface eased speed envelope, and a fixed crosshair + moving reticle — all human-verified across three play-test rounds.**

## Performance

- **Duration:** ~35 min (including 3 play-test rounds of iteration)
- **Completed:** 2026-06-13
- **Tasks:** 4 of 4 complete (including human-verify checkpoint)
- **Files modified:** 4

## Accomplishments

### Task 1: Virtual-joystick steering + roll + persistent throttle (FLT-01, FLT-02)

- Created `Scripts/Flight/FlightController.cs` as `public partial class FlightController : Node` in namespace `Universe`
- Software cursor `_cursor` accumulates `InputEventMouseMotion.Relative * Sensitivity` in `_Input` and is clamped with `LimitLength(MaxCursorRadius)` — no absolute mouse query (Pitfall 8 avoided)
- Attitude: steer = `_cursor / MaxCursorRadius`; deadzone check → zero rotation in deadzone (hold-attitude D-02); outside deadzone: yaw/pitch applied as local `Basis` multiply; `Orthonormalized()` every frame (T-03-01 mitigation)
- Roll: `(Input.GetActionStrength("roll_left") - roll_right) * RollRate * delta` composed into Basis
- Throttle: persistent `double _throttle01` in [-1,1] — W increases (forward), S decreases (reverse), X eases to 0; clamped to [-1,1] (design refinement from play-test round 1)
- Camera basis aligned to `_shipBasis` each frame — player viewpoint tracks ship attitude
- T key toggles mouse mode between Confined and Captured
- Removed skeleton thrust motion from `TestSetup._Process` — `FlightController` now calls `TranslatePos`
- Added 6 InputMap actions to `project.godot`

### Task 2: Distance-scaled speed envelope (FLT-03, D-06/D-07/D-08)

- Implemented inline in Task 1's `FlightController.cs` `UpdateSpeedEnvelope()` method
- Root-cause fix from initial implementation: distance now measured to PARENT body directly (`ship.LocalPos.Magnitude() - parent.RadiusMeters`) rather than scanning siblings — parent body always dominates in its own space; sibling scan gave huge speed in planet space
- `targetMax = Clamp(nearest * SpeedPerMeter, MinSpeed, MaxSpeed)` (shape is tuning discretion)
- `_contextMax` lerped toward `targetMax` each frame: `Mathf.Lerp(_contextMax, targetMax, SpeedEasing * delta)` — absorbs SOI-boundary discontinuities (D-07, Pitfall 9)
- `actualSpeed = _throttle01 * _contextMax` — one control, auto-scaled (D-08); negative throttle yields negative actualSpeed for reverse
- `CurrentSpeed` exposed read-only for HUD
- Speed magnitude capped at SpeedOfLight = 3e8 m/s absolute upper bound (T-03-02)

### Task 3: Fixed crosshair + moving steering reticle (HUD-03, D-05)

- `Main.tscn`: Added `Crosshair` Control anchored at viewport center (anchors 0.5/0.5) with phosphor-green `CrossH` and `CrossV` ColorRect arms — fixed nose-forward marker (HUD-03)
- Added `SteeringReticle` Control; `FlightController._steeringReticle` found by name in `_Ready`; positioned each frame at `viewportCenter + _cursor - halfSize` to center the 16x16 cross on the cursor offset
- Both reticles phosphor-green `Color(0.1, 1, 0.3, 1)` matching existing HUD aesthetic (D-09)
- Added `FlightController` node to scene with `WorldPath = NodePath("..")` and `CameraPath = NodePath("../Camera3D")`
- Set `mouse_filter = Ignore (2)` on both Crosshair and SteeringReticle Controls (play-test fix round 3)

### Task 4: Human-Verify (approved after 3 play-test rounds)

- Three rounds of iteration followed by user approval
- Round 1: Reverse thrust design refinement — throttle [-1,1] approved
- Round 2: Speed envelope root-cause fix — parent-distance replaces sibling scan
- Round 3: Steering input fix — mouse_filter=Ignore + _Input accumulation
- Final state: steering, hold-attitude, roll, reverse throttle, full-stop, smooth distance-scaled speed, and both reticles all verified working

## Task Commits

1. **Task 1+2: Virtual-joystick steering + speed envelope** - `b0599d2` (feat)
2. **Task 3: Crosshair + reticle + FlightController node** - `23e1f2a` (feat)
3. **Fix: Reticle centering (subtract halfSize)** - `0f32d3e` (fix)
4. **Fix: Mouse capture toggle (T key), speed envelope direction, speed easing** - `1cd880d` (fix)
5. **Fix: Steering input path (mouse_filter Ignore + _Input accumulation) + reverse thrust** - `7f5b882` (fix)

## Files Created/Modified

- `Scripts/Flight/FlightController.cs` — NEW: full arcade flight model with [Export] tuning knobs, [-1,1] throttle, T-key mouse toggle, _Input accumulation
- `Scripts/Universe/TestSetup.cs` — removed SkeletonSpeed export + skeleton thrust _Process motion
- `project.godot` — 6 new InputMap actions added
- `Main.tscn` — Crosshair + SteeringReticle Controls (mouse_filter=Ignore) + FlightController node

## Deviations from Plan

### Tasks 1 and 2 implemented in a single file/commit

**[Deviation - Planning] Distance-scaled speed envelope (Task 2) implemented inline with Task 1**
- **Found during:** Task 1 implementation
- **Reason:** Both tasks modify `Scripts/Flight/FlightController.cs` exclusively. Implementing the complete final form in one pass avoids a trivial intermediate state commit with incorrect behavior.
- **Impact:** No separate commit for Task 2; all Task 2 acceptance criteria met in `b0599d2`.
- **Build result:** 0 errors, 0 warnings.

### Reticle centering fix (Rule 1 - Bug)

**[Rule 1 - Bug] SteeringReticle positioned at (center + cursor) instead of centering the control on cursor**
- **Found during:** Task 3 post-implementation review
- **Issue:** `Control.Position` in Godot 4 sets the top-left corner of the control, not its center. Setting `Position = viewportCenter + cursor` offsets the visual by the control's half-size.
- **Fix:** `Position = viewportCenter + cursor - halfSize` where `halfSize = _steeringReticle.Size / 2f`
- **Files modified:** `Scripts/Flight/FlightController.cs`
- **Commit:** `0f32d3e`

### Play-test round 1: Reverse thrust design refinement (D-03 extension)

**[Design Refinement - D-03] Throttle range extended from [0,1] to [-1,1] for reverse thrust**
- **Found during:** Human-verify play-test round 1
- **Issue:** Plan D-03 specified persistent throttle in [0,1] (forward only). Play-testing revealed that backing away from a body requires reversing without turning, which was impossible with forward-only throttle. S key was already bound to `throttle_down` but only reduced from max to 0, never negative.
- **Fix:** Clamped range changed to [-1,1]; negative throttle drives the ship backward (speed = throttle01 * contextMax where negative throttle gives negative speed along forward axis); X eases to 0 smoothly.
- **Approved by user in play-test session.** This is a deliberate design change, not a bug fix.
- **Files modified:** `Scripts/Flight/FlightController.cs`
- **Commit:** `1cd880d`

### Play-test round 2: Speed envelope root-cause fix (Rule 1 - Bug)

**[Rule 1 - Bug] Speed envelope used sibling scan; gave huge speed in planet space**
- **Found during:** Human-verify play-test round 2
- **Issue:** Initial `UpdateSpeedEnvelope()` scanned the ship's siblings (parent's ChildIndices, skipping ship) for the nearest body. In planet space, the ship's parent IS the planet, so no sibling was close — the scan returned a large distance and yielded a huge contextMax even while very close to the planet surface.
- **Fix:** Measure distance to the PARENT body directly: `surfaceDist = ship.LocalPos.Magnitude() - parent.RadiusMeters`, clamped at 0. The parent always dominates in its own coordinate space, giving the correct crawl speed near its surface.
- **Files modified:** `Scripts/Flight/FlightController.cs`
- **Commit:** `1cd880d`

### Play-test round 3: Steering input swallowed by HUD Controls (Rule 1 - Bug)

**[Rule 1 - Bug] MouseMotion events consumed by Crosshair/SteeringReticle Controls before FlightController._Input**
- **Found during:** Human-verify play-test round 3 (steering non-responsive after reticle rendering was added)
- **Issue:** Both `Crosshair` and `SteeringReticle` Controls in `Main.tscn` had `mouse_filter = Stop` (default). Godot's event routing sends `InputEventMouseMotion` to the topmost Control under the cursor and stops propagation if its `mouse_filter` is `Stop`. The HUD Controls covered the viewport and consumed all mouse motion before `FlightController._Input` could accumulate it — steering appeared dead.
- **Fix 1:** Set `mouse_filter = Ignore (2)` on both Crosshair and SteeringReticle Controls in `Main.tscn` — events pass through.
- **Fix 2:** Moved mouse accumulation from `_UnhandledInput` to `_Input` in `FlightController.cs` — `_Input` receives events before UI filtering, ensuring accumulation even if a Control somehow still intercepts.
- **Files modified:** `Main.tscn`, `Scripts/Flight/FlightController.cs`
- **Commit:** `7f5b882`

## Known Stubs

- **Throttle_up / throttle_down both bound to W/S**: These actions share physical keycodes with the old `thrust_forward`/`thrust_back` actions. Old actions remain in `project.godot` for backwards compatibility but are no longer read. Plan 04 may clean this up.
- **cycle_target (Tab)**: Defined in InputMap but not read by any script yet — reserved for Plan 04 HUD target cycling (HUD-04).
- **Crosshair and SteeringReticle**: Plain cross shapes drawn via ColorRect arms. More elaborate reticle art deferred to Plan 04 or beyond.

## Threat Flags

No new threat surface beyond the plan's threat model. All STRIDE threats mitigated:
- T-03-01: Orthonormalized() every frame
- T-03-02: MaxSpeed clamped to SpeedOfLight; Lerp-eased contextMax
- T-03-03: LimitLength(MaxCursorRadius) every input event
- T-03-04: Throttle Mathf.Clamp to [-1,1]

## Self-Check: PASSED

Files exist:
- Scripts/Flight/FlightController.cs: FOUND
- Scripts/Universe/TestSetup.cs: FOUND (skeleton thrust removed)
- project.godot: FOUND (roll_left, full_stop, throttle_up verified)
- Main.tscn: FOUND (Crosshair, SteeringReticle, FlightController nodes; mouse_filter=Ignore)

Commits verified:
- b0599d2 (feat(01-03): virtual-joystick steering + roll + persistent throttle): FOUND
- 23e1f2a (feat(01-03): fixed center crosshair + moving steering reticle): FOUND
- 0f32d3e (fix(01-03): center reticle on cursor by subtracting half-size): FOUND
- 1cd880d (fix(01-03): mouse capture toggle, speed envelope direction, easing): FOUND
- 7f5b882 (fix(01-03): fix steering input path and enable reverse thrust): FOUND

Build: 0 errors, 0 warnings confirmed. Human-verify: APPROVED (3 play-test rounds).
