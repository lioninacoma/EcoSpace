---
phase: 08-warp-motion-profile
reviewed: 2026-06-22T00:00:00Z
depth: standard
files_reviewed: 6
files_reviewed_list:
  - Scripts/Math/WarpMotionProfile.cs
  - Scripts/Flight/FlightController.cs
  - Scripts/Math/UniMath.cs
  - Scripts/Hud/WarpConfirmationScreen.cs
  - EcoSpace.Tests/WarpMotionProfileTests.cs
  - EcoSpace.Tests/EcoSpace.Tests.csproj
findings:
  critical: 1
  warning: 4
  info: 3
  total: 8
status: issues_found
---

# Phase 8: Code Review Report

**Reviewed:** 2026-06-22
**Depth:** standard
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Phase 8 replaces the prior exponential-approach warp model (`dist / T_int`) with a
closed-form symmetric-smoothstep trapezoidal velocity profile (`WarpMotionProfile`). The
math is clean, dependency-free, and well-documented; the 18 unit tests all pass and the
LCA/UniMath precision contract is respected (all cross-frame distance math routes through
`UniMath.Distance` / `UniMath.NormalizedDirection`, and `UniMath` remains read-only with an
unchanged `NormalizedDirection` signature).

However, the design relies on a per-frame `dist < target.SOIMeters` check as its *only*
arrival/anti-tunnel guard, with no clamp on per-frame warp displacement. Because cruise
speed can reach ~3e20 m/s while target SOIs are as small as 1e9 m (planet) or 1e15 m
(star), a single large-`delta` frame (frame hitch, alt-tab, breakpoint) steps the ship far
past the target SOI and warp never re-arrives. This is the primary correctness defect
(CR-01). A secondary cluster of warnings concerns the small-D clamp breaking the
area=D invariant, a HUD cruise-speed formula that disagrees with the actual solver, and a
scale-dependent coincidence threshold in `NormalizedDirection`.

The structural-findings pre-pass was not provided for this review, so all findings below
are narrative.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: Warp can tunnel past the target SOI on a single large-delta frame and never re-arrive

**File:** `Scripts/Flight/FlightController.cs:920-933` (and `:474` ApplyMotion ordering)

**Issue:** `_WarpProcess` advances the ship by `warpSpeed * delta` each frame (via
`ApplyMotion`), and the *only* arrival test is `if (dist < target.SOIMeters)` evaluated at
the **start** of the frame, before motion is applied. There is no clamp tying the per-frame
displacement to the remaining distance, and no "overshot the target" detection.

During the cruise phase, `VCruise` for an intergalactic leg is on the order of
`2.4e22 m / (120 s · (1−f)) ≈ 3e20 m/s`. Target SOIs are tiny by comparison:
`PlanetSOI = 1e9 m`, `StarSOI = 1.5e15 m` (`Scripts/TestSetup.cs:54-55`). A single frame
with a modest `delta` (a hitch, alt-tab, editor breakpoint, or just a low-FPS spike) of
even 0.05 s produces a step of `~1.5e19 m` — 4 to 10 orders of magnitude larger than the
target SOI. The ship jumps from outside the SOI to far past it in one frame. The next
frame, `dist` is large again (ship is now on the far side and receding), so
`dist < target.SOIMeters` never becomes true, warp continues until `_warpElapsedSec`
exceeds `TSel`, `Velocity` clamps to `VTerminal`, and the ship drifts away from the target
forever. The "WR-02 anti-tunnel" comment claims the decel ramp prevents tunneling, but that
guarantee only holds under steady small-`delta` integration — it is not robust to a single
oversized step.

**Fix:** Clamp the per-frame warp displacement to never overshoot the remaining distance
into the SOI, and treat an overshoot as arrival. For example, compute the intended step and
cap it:

```csharp
_warpElapsedSec += delta;
double warpSpeed = _warpProfile.Velocity(_warpElapsedSec);
if (!double.IsFinite(warpSpeed)) { DisengageWarp(); return; }

// Anti-tunnel: never step past the SOI boundary in one frame.
double remainingToSoi = dist - target.SOIMeters;          // dist is start-of-frame
double step = warpSpeed * delta;
if (step >= remainingToSoi) { DisengageWarp(); return; }   // would reach/overshoot SOI
```

(Implement the clamp where speed is consumed, or override the motion magnitude for the warp
path so `ApplyMotion` cannot exceed `remainingToSoi`.) Additionally, since `_WarpProcess`
no longer mutates `_warpElapsedSec` toward a known cap, consider an absolute timeout
(`_warpElapsedSec > TSel + margin → DisengageWarp`) as a backstop so a ship that has flown
past can never be stranded mid-warp.

## Warnings

### WR-01: Small-distance warps silently break the area = D invariant (massive overshoot for near targets)

**File:** `Scripts/Math/WarpMotionProfile.cs:123-129`

**Issue:** `Solve` computes
`vCruise = (D − f·T_sel·(vLaunch+vTerminal)/2) / (T_sel·(1−f))` then clamps `vCruise` to
`≥ 0`. When `D` is smaller than the ramp contribution (`f·T_sel·(vLaunch+vTerminal)/2`),
`vCruise` goes negative and is clamped to 0 — but the launch/terminal ramps still
contribute their full areas. The integral of `Velocity` over `[0, T_sel]` then equals
roughly `f·T_sel·(vLaunch+vTerminal)/2`, **not** `D`. Concretely, with `D = 1000 m`,
`vLaunch = vTerminal = 1e6`, `f = 1/3`, `T_sel = 120 s`, the profile travels
`≈ 4e7 m` instead of 1000 m — a 40,000× overshoot of the intended geometric distance. This
is exactly the configuration `EngageWarp` produces for a target just outside its SOI
(`d = max(0, d0 − SOIMeters)` is small while `vLaunch=_easedSpeed` and
`vTerminal=ManualMaxSpeed` are ~1e6). The existing `SmallD_AllOutputsFinite` test masks
this because it only asserts finiteness, never the integral, for the small-D case.

**Fix:** Detect the infeasible case (`D < ramp contribution`) and degrade gracefully —
e.g. scale `vLaunch`/`vTerminal` down so the ramp area cannot exceed `D`, or shorten the
ramp duration, or return a profile whose integral is `D` by construction. At minimum, add a
test that asserts `Integrate(p) ≈ D` for the small-D case so the regression is visible.

### WR-02: HUD-displayed cruise speed disagrees with the actual solved cruise speed

**File:** `Scripts/Hud/WarpConfirmationScreen.cs:233-239`

**Issue:** The confirmation screen computes the displayed warp speed as
`rampContrib = f * tSel * 2.0 * ManualMaxSpeed * 0.5` (i.e. `f·tSel·ManualMaxSpeed`),
assuming `vLaunch == vTerminal == ManualMaxSpeed`. But `EngageWarp`
(`FlightController.cs:866-868`) solves with `vLaunch = _easedSpeed` (the ship's *current*
eased speed, frequently far below `ManualMaxSpeed`) and only `vTerminal = ManualMaxSpeed`.
Whenever the ship is not already at `ManualMaxSpeed` when the panel is opened, the displayed
"WARP <speed>" is wrong — it under- or over-states the actual peak cruise speed by
`f·tSel·(ManualMaxSpeed − _easedSpeed)/(T_sel·(1−f))`. This is a player-facing accuracy bug,
not a crash.

**Fix:** Either expose the real launch speed from `FlightController` (a read-only
`CurrentSpeed`/`_easedSpeed` accessor already exists — use `CurrentSpeed`) and mirror the
exact `Solve` formula, or better, add a static `WarpMotionProfile.PreviewCruiseSpeed(d,
tSel, f, vLaunch, vTerminal)` helper and call it from both the HUD and `Solve` so the
displayed and applied numbers cannot diverge.

### WR-03: `NormalizedDirection` coincidence threshold is scale-dependent (auto-orient dead zone up to ~5e15 m)

**File:** `Scripts/Math/UniMath.cs:236-238`

**Issue:** The coincidence cutoff `mag < 0.5` is measured in **integer Units of the LCA
child-frame**, whose scale varies by hierarchy level. For an intergalactic leg the LCA is
the Universe and the child-frame is Star scale (~1e16 m/unit), so `0.5 units ≈ 5e15 m` —
larger than an entire star system. Inside that radius `NormalizedDirection` returns
`Double3.Zero` and the warp auto-orient block (`FlightController.cs:938`) is skipped. The
comment justifies 0.5 as "just below 1.0 unit," but a fixed unit-count threshold maps to a
wildly different metre radius at each scale. In practice the ship is usually still pointed
correctly and decelerating by then, but the orientation logic silently disables itself over
a scale-dependent and surprisingly large region.

**Fix:** Decide the coincidence test in metres, not units — e.g. fall through to the
`Offset`-based path (as the doc comment for the method still advertises) or compare
`rel.ToDouble3().Magnitude()` against a small absolute epsilon, so the dead zone is the same
physical size regardless of LCA scale. If a unit-based cutoff is intentional, document the
exact metre radius it implies at each LCA level.

### WR-04: `EPSILON` denominator guards mask infeasible inputs instead of rejecting them

**File:** `Scripts/Math/WarpMotionProfile.cs:116,125,164,178`

**Issue:** `Solve` and `Velocity` guard every division with `Math.Max(EPSILON, …)`
(`tSel`, `T_sel·(1−f)`, `TAccel`, `TDecel`). This guarantees finite output (good for the
finiteness tests) but converts genuinely-degenerate inputs into arbitrary large finite
values rather than a clearly-handled case. For example a `tSel` that arrives as 0 (the
caller clamps to 1.0, but `Solve` is `public` and unit-tested directly) yields
`vCruise = numerator / 1e-11` — an astronomically large speed that then flows into motion.
Combined with CR-01 (no displacement clamp) this is a path to instant tunneling.

**Fix:** Keep the epsilon guards as a last resort, but validate at the boundary: reject or
explicitly clamp non-physical `tSel`/`f` (or return a documented "no-op" profile with
`VCruise = 0` and `TSel` finite) so the epsilon path is never the load-bearing behavior.
Document that `Solve` assumes `tSel ≥ 1.0` (currently only the comment at
`FlightController.cs:106` states this; `Solve` itself does not enforce it).

## Info

### IN-01: `Velocity(0)` and `Velocity(T_sel)` equal launch/terminal only when ramps are non-degenerate

**File:** `Scripts/Math/WarpMotionProfile.cs:160-181`

**Issue:** At `f = 0` (`TAccel = TCruise-bounded`), `Velocity(0)` returns `VLaunch` but
`Velocity(0+ε)` jumps to `VCruise` — a velocity discontinuity at warp start. This is an
inherent property of `f = 0` (no ramp), and `WarpAccelFraction` defaults to 1/3 so it is not
hit in normal play, but the `FAtZero_AllOutputsFinite` test does not assert the (expected)
discontinuity, so the behavior is undocumented.

**Fix:** Document that `f = 0` produces a step at `t = 0`/`t = T_sel`, or clamp the
exported `WarpAccelFraction` minimum above 0 if a smooth start is required.

### IN-02: Dead commented-out constant left in source

**File:** `Scripts/Flight/FlightController.cs:41-43`

**Issue:** `// private const double SpeedOfLight = 3e8;` is fully commented-out dead code
retained only for narrative. Per the codebase's own anti-pattern guidance, commented-out
code should be removed (history preserves it).

**Fix:** Delete the commented constant; the surrounding doc comment already explains the cap
was removed.

### IN-03: `WarpConfirmationScreen` duplicates the `Solve` cruise formula by hand

**File:** `Scripts/Hud/WarpConfirmationScreen.cs:233-237`

**Issue:** The cruise-speed preview re-implements the area equation inline rather than
reusing `WarpMotionProfile`. Beyond the value mismatch in WR-02, this is a maintainability
hazard: any future change to the `Solve` formula must be mirrored here manually or the HUD
silently drifts.

**Fix:** Extract a shared static preview helper on `WarpMotionProfile` (see WR-02 fix) and
call it from both sites so there is a single source of truth.

---

_Reviewed: 2026-06-22_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
