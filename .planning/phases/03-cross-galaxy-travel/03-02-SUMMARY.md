---
phase: 03-cross-galaxy-travel
plan: "02"
subsystem: flight-model
tags: [flight, speed, intergalactic, ftl, constraint-removal, security]
dependency_graph:
  requires:
    - 03-01 (galaxy RadiusMeters=5e20 must be authored for speed curve deceleration)
  provides:
    - FlightController.MaxSpeed setter with SpeedOfLight cap removed
    - FlightController._maxSpeed default raised to 2e20 m/s (intergalactic FTL-equivalent)
    - double.IsFinite guard in ApplyMotion blocking NaN/Infinity from TranslatePos
  affects:
    - FlightController speed envelope (now reaches FTL magnitudes in open intergalactic space)
    - FlightController deceleration on approach (still driven by galaxy RadiusMeters from 03-01)
tech_stack:
  added: []
  patterns:
    - System.Math.Max(0.0,value) guard idiom in MaxSpeed setter (consistent with SpeedPerMeter/MinSpeed/SpeedEasing)
    - double.IsFinite guard in ApplyMotion before TranslatePos (security mitigation T-03-04)
key_files:
  created: []
  modified:
    - Scripts/Flight/FlightController.cs
decisions:
  - "_maxSpeed default set to 2e20 m/s targeting ~2-minute full-throttle crossing at 2.4e22 m intergalactic separation (D-35); this value is a play-test tuning knob — lower to 4e19 for ~10 min crossing, raise for faster"
  - "SpeedOfLight const commented out (reference-only); no longer used as a cap anywhere in FlightController"
  - "double.IsFinite guard placed before the 1e-3 epsilon check so both gates protect TranslatePos"
  - "No FTL overshoot / sub-stepping / step-clamp guard added per D-36 — galaxy SOI (5e20 m) takes ~150 frames to cross at 2e20 m/s; deferred to play-test"
metrics:
  duration: ~5 min
  completed_date: "2026-06-16"
  tasks_completed: 1
  files_modified: 1
---

# Phase 03 Plan 02: Remove SpeedOfLight Cap + Raise MaxSpeed to FTL-Equivalent Summary

**One-liner:** Removed the 3e8 m/s SpeedOfLight clamp on MaxSpeed setter and raised the default to 2e20 m/s; added double.IsFinite guard in ApplyMotion; the existing distance→speed curve (galaxy RadiusMeters from 03-01) provides natural deceleration on galaxy approach.

---

## MaxSpeed Default and Crossing-Time Record (D-35 input)

| Parameter | Value | Notes |
|-----------|-------|-------|
| `_maxSpeed` default | `2e20` m/s | Targeting ~2-minute crossing at 2.4e22 m separation |
| Distance (home→DEST GALAXY) | 2.4e22 m | 2.4e6 Universe units × 1e16 m/unit |
| Theoretical crossing time | ~120 s | 2.4e22 / 2e20 = 120 s at full throttle in open space |
| Observed crossing time | DEFERRED | Requires manual play-test (human-verify at end of phase) |

**Tuning knob:** `_maxSpeed` in `Scripts/Flight/FlightController.cs` line 139. If crossing feels too fast, lower to `4e19` (~10 min). If too slow, raise toward `1e21`. This is the single knob for D-35 crossing-time tuning.

---

## Changes Made

### `Scripts/Flight/FlightController.cs`

1. **MaxSpeed setter** — `Mathf.Clamp(value, 0.0, SpeedOfLight)` replaced with `System.Math.Max(0.0, value)`. The SpeedOfLight (3e8 m/s) cap silently prevented intergalactic speeds; removing it allows the editor export to hold any non-negative value.

2. **`_maxSpeed` field default** — raised from `1e11` to `2e20` m/s. The comment documents the crossing-time rationale and flags the value as a play-test tuning knob.

3. **`SpeedOfLight` const** — commented out entirely. The doc comment clarifies it is retained as reference only and is no longer a cap.

4. **`ApplyMotion` finiteness guard** — `if (!double.IsFinite(CurrentSpeed)) return;` added as the first check, before the epsilon guard. With MaxSpeed uncapped, a pathological editor export (e.g. `double.PositiveInfinity`) could propagate through the easing into `TranslatePos`. This guard blocks that path (T-03-04 / RESEARCH §V5).

---

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Remove SpeedOfLight cap, raise MaxSpeed default, add IsFinite guard | e1c8104 | Scripts/Flight/FlightController.cs |

---

## Automated Verification Results

- `dotnet build EcoSpace.csproj`: 0 errors, 0 warnings ✓
- `dotnet test EcoSpace.Tests`: 30/30 passed ✓
- Grep gate: `grep -v '^[[:space:]]*//' FlightController.cs | grep -c 'Mathf.Clamp(value, 0.0, SpeedOfLight)'` → **0** ✓
- Source assertions:
  - `MaxSpeed` setter uses `System.Math.Max(0.0, value)` — no SpeedOfLight reference on non-comment lines ✓
  - `_maxSpeed = 2e20` (≥ 1e19) ✓
  - `double.IsFinite(CurrentSpeed)` guard in `ApplyMotion` ✓
  - No FTL overshoot / sub-stepping / step-clamp added (inspection confirms only finiteness guard added) ✓

---

## Pending Human Verification

The following check requires launching the Godot game and cannot be performed by the executor:

**End-of-phase play-test (03-02 human check):**

1. Launch the game. Ship starts in PlanetA orbit. Throttle up — confirm speed auto-scales correctly near bodies (unchanged from previous behavior).
2. Fly outward past the home system SOI, then past the home galaxy SOI into Universe space. Confirm speed climbs to FTL-equivalent magnitudes (HUD speed label should show ly/s-class units via the D-10 adaptive ladder).
3. Fly toward DEST GALAXY (+Z direction). Confirm:
   - Speed eases down automatically as the galaxy disc approaches (no manual braking needed).
   - The ship crosses into the DEST GALAXY SOI with no crash, no pop, no loading screen.
4. **Record the observed full-throttle home→destination crossing time.** Compare to the ~2-minute target. This is the D-35 tuning input — adjust `_maxSpeed` if the crossing feels too fast or too slow.
5. **Also confirm (from 260616-riw deferral):** exactly 2 galaxy discs visible in the sky from the home system (no HOME GALAXY disc — suppressed), with DEST GALAXY and ELLIPTICAL CLUSTER visible.

---

## Deviations from Plan

None — plan executed exactly as written. Single targeted change to FlightController.cs matching all acceptance criteria.

---

## Known Stubs

None — all flight math is live. `_maxSpeed = 2e20` is an authored default, not a placeholder. The crossing time number (D-35) is a play-test measurement, not a code stub.

---

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The `MaxSpeed` setter change reduces surface area (removes the SpeedOfLight cap that could have been confused for a security boundary). The IsFinite guard closes the T-03-04 threat vector. No new threat flags.

---

## Self-Check: PASSED

- `Scripts/Flight/FlightController.cs` exists on disk ✓
- Commit e1c8104 verified in git log ✓
- `dotnet build` 0 errors ✓
- 30/30 tests green ✓
