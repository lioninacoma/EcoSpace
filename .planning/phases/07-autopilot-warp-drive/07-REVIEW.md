---
phase: 07-autopilot-warp-drive
reviewed: 2026-06-22T19:12:04Z
depth: standard
files_reviewed: 4
files_reviewed_list:
  - Scripts/Flight/FlightController.cs
  - Scripts/Hud/WarpConfirmationScreen.cs
  - Scripts/Math/UniMath.cs
  - Scripts/Math/UniVec3.cs
findings:
  critical: 0
  warning: 4
  info: 4
  total: 8
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-06-22T19:12:04Z
**Depth:** standard
**Files Reviewed:** 4
**Status:** issues_found

## Summary

Reviewed the autopilot/warp-drive implementation: `FlightController` (state machine, speed
envelope, warp process), `WarpConfirmationScreen` (UI), and the precision math additions
`UniVec3.ToDouble3Units` and `UniMath.NormalizedDirection`.

The precision design is sound for the documented intergalactic case. `NormalizedDirection`
normalizes in unit-space from the LCA-relative `UniVec3`, where the `long`→`double` Units cast
is exact (Universe-scale separation of ~2.4e22 m yields ~2.4e6 units, well under 2^53). Scale
cancels in the ratio, so direction is correct and precision-preserving as claimed. Bounds
checks, finite-value guards, and division-by-zero guards (`travelTimeSec` floor of 1.0, slider
min 5 s, `lnRatio > 1e-9`) are present on the main paths.

The defects below are correctness/robustness issues rather than precision failures. The most
important is a **scale-dependent coincidence threshold** in `NormalizedDirection` (and the
mirrored `UniVec3.Normalized`) that is measured in *units* not metres, and a warp-step
**SOI overshoot/tunneling** risk at the uncapped 2e20 m/s warp speed. No security issues found
(no secrets, injection, eval, or unsafe deserialization in scope). No structural-findings block
was provided.

The known P3 tech-debt item (warp-timing inaccuracy under the `WarpMaxSpeed` cap, exponential
approach formula) is acknowledged and not re-filed here.

## Warnings

### WR-01: `NormalizedDirection` coincidence threshold is in unit-space, not metres — collapses to zero direction at huge separations

**File:** `Scripts/Math/UniMath.cs:231-233`
**Issue:** `NormalizedDirection` computes `u = rel.ToDouble3Units()` (a vector in the LCA
child-frame's *unit* space) and then returns `Double3.Zero` when `u.Magnitude() < 1e-3`. The
threshold `1e-3` is dimensionless *units*, but the doc comment treats it like a small-distance
guard. When the LCA child-scale is large, this rejects very large *metre* distances. At Galaxy
child-scale (`Scale = 1e16` when LCA is Universe), `1e-3` units = `1e13` m — two bodies up to
~10 trillion metres apart would be reported as coincident and yield a zero direction, silently
disabling warp auto-orient. The comment "Falls back to Offset (sub-unit range) when Units are
all zero" also does not match the code: there is no Offset fallback branch; the method only
either normalizes `u` or returns zero.

In the current warp flow this is masked because warp disengages once `dist < target.SOIMeters`
(SOIs are far larger than 1e13 m at galaxy scale), but the helper is general-purpose and the
threshold is incorrect for any caller operating at finer scales.

**Fix:** Make the guard distance-based and scale-independent. Either normalize from metres
(`rel.ToDouble3()`) using a small *metre* epsilon, or scale the unit-space epsilon by `rel.Scale`:
```csharp
Double3 u = rel.ToDouble3Units();
double magUnits = u.Magnitude();
// coincident if the metre-space magnitude is below ~1e-3 m
if (magUnits * rel.Scale < 1e-3) return Double3.Zero;
return new Double3(u.X / magUnits, u.Y / magUnits, u.Z / magUnits);
```
Also correct or remove the inaccurate "Falls back to Offset" comment.

### WR-02: Warp can overshoot / tunnel through the target SOI in a single frame

**File:** `Scripts/Flight/FlightController.cs:888-953`
**Issue:** `_WarpProcess` disengages only when `dist < target.SOIMeters` is observed *at a frame
boundary*. `warpSpeed` is clamped to `WarpMaxSpeed` (default 2e20 m/s). At ~60 fps
(`delta ≈ 0.016 s`) a single frame advances up to ~3.2e18 m. If `target.SOIMeters` is smaller
than the per-frame step (e.g. a Planet/Star SOI), the ship can jump from outside the SOI to
well past the target centre in one frame, so the `dist < SOIMeters` check never fires on a frame
boundary — the warp continues, or the ship ends up on the far side. This is the classic
discrete-integration tunneling problem and is distinct from the known timing-accuracy tech-debt.

**Fix:** Clamp the per-frame motion so it cannot overshoot the SOI arrival point, and/or detect
arrival by remaining distance rather than only the SOI test. For example cap the step:
```csharp
double maxStep = Math.Max(0.0, dist - target.SOIMeters);
double step = Math.Min(warpSpeed * delta, maxStep);
// then disengage when step == maxStep (arrived) — derive an effective speed = step / delta
```
This guarantees the ship lands on the SOI boundary instead of tunneling through it.

### WR-03: `EngageWarp` recomputes references separately from `_WarpProcess`, allowing a stale/empty warp

**File:** `Scripts/Flight/FlightController.cs:827-862`
**Issue:** `EngageWarp` transitions to `WarpState.Warping` even when it cannot resolve a valid
ship/target pair: the `hasRefs`/null branches simply fall back to
`_warpInternalTimeSec = _selectedTravelTimeSec` and then unconditionally set
`_warpState = WarpState.Warping` (line 861). If `ActiveTargetIndex` is `-1` or out of range at
engage time (e.g. the target was cleared between opening the panel and pressing Enter), warp is
entered with no valid target. `_WarpProcess` then immediately calls `DisengageWarp()` on its own
null check — but for the first frame the ship is in Warping state with `CurrentSpeed` left at its
prior value, and look-around/camera handling runs. The entry should be gated on a valid target.

**Fix:** Refuse to enter Warping without a valid target:
```csharp
if (!hasRefs || gameObjects[shipIdx] == null || gameObjects[tgtIdx] == null)
    return;   // stay in Manual; nothing to warp toward
...
_warpState = WarpState.Warping;
```

### WR-04: `UpdateSpeedEnvelope` reads `_hud.ActiveTargetIndex` for a target that may be in a different frame, but only the LCA path guards it — sentinel-distance path can mis-scale open space

**File:** `Scripts/Flight/FlightController.cs:652-653`
**Issue:** When no body is found (`nearest == double.MaxValue`), `nearest` is set to
`tierCeiling / Math.Max(_speedPerMeter, 1.0)`. If `_speedPerMeter < 1.0` (default is `0.5`),
`Math.Max(_speedPerMeter, 1.0)` forces the divisor to `1.0`, so `nearest = tierCeiling`, and the
subsequent `Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, tierCeiling)` yields
`tierCeiling * 0.5` — i.e. open-space speed is silently halved relative to the documented intent
of "use tierCeiling as the speed target" (comment at line 650-651). The `Math.Max(..., 1.0)`
guard prevents a divide blow-up but also distorts the result for the common `_speedPerMeter < 1`
configuration, so the open-space target never actually reaches `tierCeiling`.

**Fix:** Compute the sentinel `nearest` so that `nearest * _speedPerMeter == tierCeiling`,
guarding only the true zero case:
```csharp
if (nearest == double.MaxValue)
    nearest = _speedPerMeter > 0.0 ? tierCeiling / _speedPerMeter : tierCeiling;
```
(The later `Clamp(..., _minSpeed, tierCeiling)` already bounds the result, so no Infinity risk.)

## Info

### IN-01: `UniVec3.Normalized()` is dead code with a latent scale-confusion bug

**File:** `Scripts/Math/UniVec3.cs:192-198`
**Issue:** `Normalized()` is never called anywhere in the codebase (verified: all `.Normalized()`
call sites are Godot `Quaternion`/`Vector3`). It computes a unit vector from `ToDouble3Units()`
(unit space) then feeds the components into `FromDouble(u.X/mag, u.Y/mag, u.Z/mag, Scale)`, which
interprets them as *metres* at `Scale` m/unit — mixing unit-space and metre-space semantics. It
also shares WR-01's scale-dependent `1e-3` threshold. Because it is unused, this is not a live
bug, but it is a trap for future callers.
**Fix:** Remove the method, or document/repair the unit-vs-metre semantics and the threshold.

### IN-02: Duplicated warp-speed formula and `WarpMaxSpeed` default across two files

**File:** `Scripts/Hud/WarpConfirmationScreen.cs:232` and `Scripts/Flight/FlightController.cs:913`
**Issue:** The warp-speed formula and the `2e20` fallback are duplicated. The HUD computes
`Min(dist / _selectedTravelTimeSec, _flight?.WarpMaxSpeed ?? 2e20)` while the controller computes
`Min(dist / _warpInternalTimeSec, _warpMaxSpeed)`. The two use *different* time constants
(`_selectedTravelTimeSec` vs the calibrated `_warpInternalTimeSec`), so the speed shown in the
confirmation screen does not match the actual initial warp speed. This is cosmetic but
misleading, and the magic `2e20` fallback is repeated.
**Fix:** Expose a single helper on `FlightController` (e.g. `ComputeInitialWarpSpeed(dist,
travelTimeSec)`) that the HUD calls, and centralize the `2e20` default as a named constant.

### IN-03: Dead/commented constant `SpeedOfLight`

**File:** `Scripts/Flight/FlightController.cs:41-43`
**Issue:** `private const double SpeedOfLight = 3e8;` is fully commented out with a note that it
was removed in Plan 03-02. Commented-out code should not be retained; the rationale already
lives in the surrounding doc comment.
**Fix:** Delete the commented declaration.

### IN-04: `FormatTravelTime` doc comment example is inconsistent with the hint label

**File:** `Scripts/Hud/WarpConfirmationScreen.cs:124-125, 251-267`
**Issue:** The hint row says `"5s–60m"` (line 125) but the slider `MaxValue` is `3600.0` (= 60 m)
and `FormatTravelTime` supports an `h` branch with the example `"1h 5m 0s" (for >= 3600 s)`. The
slider can only reach exactly 3600 s (1 h 0 m 0 s), so the `hours > 0` branch is effectively
unreachable for any value the slider produces except the exact max. Minor: the doc example
implies values above the slider range. No functional impact.
**Fix:** Align the comment/example with the actual `[5, 3600]` slider range, or remove the `h`
example.

---

_Reviewed: 2026-06-22T19:12:04Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
