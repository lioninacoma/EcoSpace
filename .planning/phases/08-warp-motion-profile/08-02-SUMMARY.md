---
phase: 08-warp-motion-profile
plan: "02"
subsystem: warp-drive-math
tags: [warp, flight-controller, unidirection, speed-envelope, hud]
dependency_graph:
  requires:
    - Scripts/Math/UniMath.cs
    - Scripts/Flight/FlightController.cs
    - Scripts/Hud/WarpConfirmationScreen.cs
    - 08-01 (WarpAccelFraction export, WarpMaxSpeed removal)
  provides:
    - UniMath.NormalizedDirection with correct unit-space coincidence threshold (WR-01)
    - UpdateSpeedEnvelope open-space sentinel reaching tierCeiling (WR-04)
    - WarpConfirmationScreen v_c display via prior wave forced fix (D-12)
  affects:
    - Warp auto-orient accuracy (NormalizedDirection fix)
    - Open-space manual top speed (doubled at default SpeedPerMeter=0.5)
tech_stack:
  added: []
  patterns:
    - Unit-space coincidence threshold (0.5 integer units) vs metric epsilon (WR-01)
    - Open-space speed sentinel: nearest=tierCeiling/_speedPerMeter (WR-04)
    - Peak cruise v_c display with exact area formula (D-12, prior wave)
key_files:
  created: []
  modified:
    - Scripts/Math/UniMath.cs
    - Scripts/Flight/FlightController.cs
    - Scripts/Hud/WarpConfirmationScreen.cs (prior wave ab45a77)
decisions:
  - NormalizedDirection threshold 0.5 integer units: below minimum non-zero magnitude (1.0) so
    any physically distinct body is correctly passed through; coincident objects (same unit cell)
    correctly return Zero
  - Open-space sentinel uses Max(_speedPerMeter, 1e-11) floor instead of Max(_speedPerMeter, 1.0)
    so nearest*_speedPerMeter == tierCeiling exactly; SpeedPerMeter/tier-factor re-tune expected
    during play-test (D-15 note)
  - WarpConfirmationScreen D-12 applied in 08-01 (forced by Rule 3 — removing WarpMaxSpeed caused
    compilation blocker); uses richer exact-area formula accounting for non-zero launch/terminal
    speeds (not the simplified D-03 form) for consistency with WarpMotionProfile.Solve
metrics:
  duration: "3 minutes"
  completed: "2026-06-22T20:15:47Z"
  tasks: 2
  files_created: 0
  files_modified: 2
status: complete
---

# Phase 08 Plan 02: WR-01, WR-04, and D-12 Confirmation Screen Cleanup Summary

Corrects NormalizedDirection's unit-space coincidence threshold (WR-01), fixes the open-space speed sentinel so manual top speed reaches tierCeiling (WR-04), and reconciles D-12 WarpConfirmationScreen v_c display (already applied in 08-01).

## What Was Built

**Task 1: WR-01 NormalizedDirection threshold + WR-04 open-space sentinel**

`Scripts/Math/UniMath.cs`:
- Changed `NormalizedDirection` coincidence threshold from `mag < 1e-3` to `mag < 0.5` in integer-unit space
- `ToDouble3Units()` returns values in integer Units where the minimum non-zero magnitude is 1.0; the old `1e-3` threshold would have rejected sub-unit separations (a corner case only), but the new `0.5` threshold is the correct cutoff just below the minimum distinguishable distance (1.0 unit)
- Rewrote the stale comment that described the fallback as "in metres" — it is in INTEGER UNITS; added explanation of why 0.5 is the correct coincidence cutoff (WR-01 / D-13)
- Signature unchanged; all callers (`_WarpProcess` auto-orient, `LuminousDescriptorBuilder`) unaffected

`Scripts/Flight/FlightController.cs`:
- Changed `UpdateSpeedEnvelope` open-space sentinel from `tierCeiling / System.Math.Max(_speedPerMeter, 1.0)` to `tierCeiling / System.Math.Max(_speedPerMeter, 1e-11)`
- At default `_speedPerMeter = 0.5`: old code produced `nearest = tierCeiling / 1.0 = tierCeiling`, then `targetMax = nearest * 0.5 = tierCeiling * 0.5` (half the ceiling); new code produces `nearest = tierCeiling / 0.5 = 2 * tierCeiling`, then `targetMax = Clamp(2*tierCeiling * 0.5, ...) = tierCeiling` (the full ceiling) (WR-04 / D-15)
- Only the open-space sentinel branch changed; proximity-damp, target ease-out, and all other math untouched
- `_speedPerMeter ≤ 0` guarded via `System.Math.Max(_speedPerMeter, 1e-11)` (T-08-05 mitigated)

**Task 2: WarpConfirmationScreen peak cruise-speed v_c display (D-12)**

Already satisfied by the prior wave's forced fix (commit ab45a77, 08-01 Task 2). All acceptance criteria verified:
- `WarpConfirmationScreen.cs` does not reference `WarpMaxSpeed` (comment only: "WarpMaxSpeed cap removed (D-05)")
- Reads `_flight?.WarpAccelFraction ?? (1.0 / 3.0)` for `f`
- Computes `vc` using the exact area formula: `profileDist = Max(0, dist - target.SOIMeters)`, `rampContrib = f * tSel * 2 * ManualMaxSpeed * 0.5`, `vc = Max(0, profileDist - rampContrib) / Max(1e-11, tSel * (1-f))`
- No travel-time clamp added (D-12 honored)
- Divisor `(1-f)` guarded via `Max(1e-11, ...)` (T-08-04 mitigated)
- `Hud.FormatSpeed(vc)` used for display
- Read-only contract (D-53) and phosphor-green `AddRow` styling (D-16) preserved

## Deviations from Plan

### Task 2 Satisfied by Prior Wave (Expected)

**[Rule 3 - Blocking, 08-01] WarpConfirmationScreen D-12 fix applied in plan 08-01**
- **Background:** Plan 08-01 removed `WarpMaxSpeed` from `FlightController.cs` (D-05). This directly caused a compilation blocker in `WarpConfirmationScreen.cs` which referenced `_flight?.WarpMaxSpeed`.
- **Resolution:** The 08-01 executor applied the D-12 v_c display fix early (deviation noted in 08-01-SUMMARY.md). The implementation uses the richer exact area formula (`v_c = (D - rampContrib) / (T_sel*(1-f))`) rather than the plan's simplified form (`v_c = (dist-SOIMeters) / (T_sel*(1-f))`), which is strictly more correct for non-zero launch/terminal speeds.
- **Acceptance criteria gap check:** All Task 2 acceptance criteria are met by the prior fix. No gap found; no re-write performed.
- **Commit:** ab45a77

## Verification Results

- `dotnet build EcoSpace.csproj` → 0 errors, 0 warnings
- `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj` → 66/66 pass (12 UniMath + 18 WarpMotionProfile + 36 other)
- `grep "1e-3" Scripts/Math/UniMath.cs` → no match (old threshold removed)
- `grep "Max(_speedPerMeter, 1.0)" Scripts/Flight/FlightController.cs` → no match (old sentinel removed)
- `grep "WarpMaxSpeed" Scripts/Hud/WarpConfirmationScreen.cs` → only comment reference (no code reference)
- `grep "WarpAccelFraction" Scripts/Hud/WarpConfirmationScreen.cs` → line 234 present
- `grep "FormatSpeed" Scripts/Hud/WarpConfirmationScreen.cs` → line 239 present

## Known Stubs

None — all display paths wire to live computed values.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes introduced. T-08-04 (WarpConfirmationScreen divisor) and T-08-05 (UpdateSpeedEnvelope divisor) both mitigated as planned.

## Self-Check: PASSED

- Scripts/Math/UniMath.cs: FOUND (modified in this plan)
- Scripts/Flight/FlightController.cs: FOUND (modified in this plan)
- Scripts/Hud/WarpConfirmationScreen.cs: FOUND (modified in 08-01, verified in this plan)
- Commit 0203711 (Task 1): FOUND in git log
- Commit ab45a77 (Task 2, prior wave): FOUND in git log
