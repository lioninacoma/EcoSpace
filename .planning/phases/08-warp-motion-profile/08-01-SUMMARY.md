---
phase: 08-warp-motion-profile
plan: "01"
subsystem: warp-drive-math
tags: [warp, motion-profile, math, flight-controller, closed-form, xunit]
dependency_graph:
  requires:
    - Scripts/Math/UniMath.cs
    - Scripts/Flight/FlightController.cs (existing warp state machine)
  provides:
    - Scripts/Math/WarpMotionProfile.cs (WarpMotionProfile struct)
    - EcoSpace.Tests/WarpMotionProfileTests.cs (18 new tests)
  affects:
    - Scripts/Hud/WarpConfirmationScreen.cs (v_c display, D-12)
    - Scripts/Flight/FlightController.cs (warp drive rewritten)
tech_stack:
  added:
    - WarpMotionProfile (global-namespace readonly struct, Scripts/Math/)
  patterns:
    - Closed-form symmetric smoothstep trapezoid velocity profile (D-01/D-04)
    - Numerically stable lerp (v0*(1-s) + v1*s) to prevent catastrophic cancellation
    - WR-03 bounds-safe gate pattern (abort before state mutation)
    - Elapsed-time-driven warp (D-10) vs. distance-based exponential decay (Phase-7)
key_files:
  created:
    - Scripts/Math/WarpMotionProfile.cs
    - EcoSpace.Tests/WarpMotionProfileTests.cs
  modified:
    - EcoSpace.Tests/EcoSpace.Tests.csproj
    - Scripts/Flight/FlightController.cs
    - Scripts/Hud/WarpConfirmationScreen.cs
decisions:
  - Exact area formula (v_c = (D - f*T*vAvg) / (T*(1-f))) accounts for non-zero
    launch/terminal speeds; simplified D-03 formula (v_c = D/(T*(1-f))) only holds
    for zero endpoints and would over-estimate v_c by the ramp contribution
  - Numerically stable lerp (v0*(1-s)+v1*s) required at intergalactic scale where
    v_c~2e20 m/s and vTerminal~1e6 m/s; naive (vCruise + delta*s) loses all low bits
  - WarpConfirmationScreen D-12 fix applied in this plan (not deferred to 08-02)
    because removing WarpMaxSpeed caused a compilation blocker (Rule 3 deviation)
metrics:
  duration: "8 minutes"
  completed: "2026-06-22T20:09:34Z"
  tasks: 2
  files_created: 2
  files_modified: 3
status: complete
---

# Phase 08 Plan 01: Warp Motion Profile (Core Math + Controller Rewrite) Summary

Closed-form symmetric smoothstep trapezoid warp profile with exact-time arrival invariant, eliminating the Phase-7 exponential approach timing.

## What Was Built

**Task 1: WarpMotionProfile struct + unit tests**

`Scripts/Math/WarpMotionProfile.cs` — pure `readonly struct` (global namespace, Godot-free) with:
- `Solve(d, tSel, f, vLaunch, vTerminal)` factory using the exact area formula:
  `v_c = (D − f·T·(vLaunch+vTerminal)/2) / (T·(1−f))`
- `Velocity(t)` evaluator using numerically stable lerp `v0*(1-s)+v1*s` (prevents catastrophic cancellation at intergalactic scale where v_c~2e20 >> vTerminal~1e6)
- Smoothstep `s(u)=3u²-2u³` hand-rolled (no Godot/Mathf dependency)
- Divisor guards (`System.Math.Max(EPSILON, ...)`) throughout (T-08-01)

`EcoSpace.Tests/WarpMotionProfileTests.cs` — 18 xUnit tests:
- Integral of Velocity over [0, T_sel] == D within 0.1% at in-system (1e9 m) and intergalactic (1e23 m) scales with f=1/3 and f≈0.5
- Endpoint speeds: Velocity(0)==vLaunch, Velocity(TSel)==vTerminal
- Cruise boundaries: Velocity(f*T)==VCruise, Velocity((1-f)*T)==VCruise
- Edge cases: small-D, T=1s floor, f=0 (pure cruise), f=0.5 (pure triangle)

**Task 2: FlightController rewrite**

`Scripts/Flight/FlightController.cs`:
- Removed: `WarpMaxSpeed` [Export] + `_warpMaxSpeed` (D-05)
- Removed: `_warpInternalTimeSec` + exponential calibration block (D-01)
- Added: `WarpAccelFraction` [Export] (f, [0,0.5], default 1/3, D-02)
- Added: `_warpElapsedSec` accumulator (D-10), `_warpProfile` WarpMotionProfile value
- `EngageWarp` rewritten: WR-03 gate (null/OOB → return before Warping), `d0=UniMath.Distance` (LCA-safe), `D=d0-SOIMeters` (D-08), Solve with current `_easedSpeed` as launch (D-09)
- `_WarpProcess` speed line: `_warpElapsedSec += delta; warpSpeed = _warpProfile.Velocity(_warpElapsedSec)` (no WarpMaxSpeed min(), no dist/T_int)
- All retained: bounds-safe lookups, dist<SOIMeters disengage (D-10), IsFinite guard (D-06/T-07-02), auto-orient slerp (D-11), _easedSpeed write

`Scripts/Hud/WarpConfirmationScreen.cs`:
- Applied D-12: replaced `dist/tSel` capped by `WarpMaxSpeed` with peak cruise speed `v_c` display (using same exact formula as Solve)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Exact area formula corrects for non-zero launch/terminal speeds**
- **Found during:** Task 1 test writing
- **Issue:** D-03 formula `v_c = D/(T*(1-f))` only holds when vLaunch=vTerminal=0. With both endpoints at ManualMaxSpeed (1e6 m/s), the smoothstep ramps contribute area `f*T*(vLaunch+vTerminal)/2`, making the naive formula over-estimate v_c and the integral exceed D
- **Fix:** Corrected to `v_c = (D - f*T*(vLaunch+vTerminal)/2) / (T*(1-f))`
- **Files modified:** `Scripts/Math/WarpMotionProfile.cs`, `EcoSpace.Tests/WarpMotionProfileTests.cs`
- **Commit:** 98b7a0c (Task 1)

**2. [Rule 1 - Bug] Numerically stable lerp prevents catastrophic cancellation**
- **Found during:** Task 1 test run (Velocity_AtTSel_IsTerminalSpeed returning 1015808 instead of 1e6)
- **Issue:** At intergalactic scale, `VCruise~2.22e20` >> `VTerminal~1e6`. The naive form `VCruise + (VTerminal-VCruise)*s` suffers catastrophic cancellation — when `s=1.0`, `(1e6-2.22e20)*1.0 = -2.22e20`, and `2.22e20 + (-2.22e20) = 15808` (lost 14 orders of magnitude of precision)
- **Fix:** Changed to `VCruise*(1-s) + VTerminal*s` (numerically stable)
- **Files modified:** `Scripts/Math/WarpMotionProfile.cs`
- **Commit:** 98b7a0c (Task 1)

**3. [Rule 3 - Blocking] WarpConfirmationScreen D-12 fix pulled into this plan**
- **Found during:** Task 2 build verification
- **Issue:** Plan 08-01 removes WarpMaxSpeed from FlightController.cs, but WarpConfirmationScreen.cs referenced `_flight?.WarpMaxSpeed` — causing a compilation error (Task 2 change directly caused this)
- **Fix:** Applied the D-12 v_c display update (replacing `dist/tSel` capped by WarpMaxSpeed with peak cruise speed) — this is the planned 08-02 change but was required immediately for compilation
- **Files modified:** `Scripts/Hud/WarpConfirmationScreen.cs`
- **Commit:** ab45a77 (Task 2)

## Verification Results

- `dotnet build EcoSpace.csproj` → 0 errors, 0 warnings
- `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj` → 66/66 pass (18 new WarpMotionProfile + 48 existing)
- `WarpMaxSpeed`, `_warpMaxSpeed`, `_warpInternalTimeSec` absent from FlightController.cs
- `WarpAccelFraction`, `_warpElapsedSec`, `_warpProfile` present in FlightController.cs
- EngageWarp returns before entering Warping on null/OOB target (WR-03)
- EngageWarp captures `d0` via `UniMath.Distance` (LCA-safe, CLAUDE.md §Position Math)
- `_WarpProcess` computes speed from `_warpProfile.Velocity(_warpElapsedSec)`
- No new raw `UniVec3.Distance`/`ToDouble3()` cross-frame calls introduced

## Self-Check: PASSED

- Scripts/Math/WarpMotionProfile.cs: FOUND
- EcoSpace.Tests/WarpMotionProfileTests.cs: FOUND
- Commit 98b7a0c (Task 1): FOUND in git log
- Commit ab45a77 (Task 2): FOUND in git log
