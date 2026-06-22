---
phase: 08-warp-motion-profile
plan: 03
subsystem: flight
tags: [warp, motion-profile, soi, anti-tunnel, closed-form, csharp, xunit, godot]

requires:
  - phase: 08-warp-motion-profile
    provides: "Closed-form WarpMotionProfile struct, elapsed-time warp driver (_WarpProcess), EngageWarp"
provides:
  - "Area=D invariant restored for small-D warp legs (target just outside SOI) via Solve endpoint-scaling"
  - "Per-frame SOI-arrival displacement clamp in _WarpProcess (WR-02 anti-tunnel, robust to oversized frames)"
  - "Absolute warp timeout backstop (WarpTimeoutMarginSec) against stranded warp"
  - "SmallD_IntegralEqualsD regression test guarding the area=D invariant in the infeasible regime"
affects: [warp, flight, motion-profile]

tech-stack:
  added: []
  patterns:
    - "Endpoint-scaling for infeasible closed-form profiles: scale ramp endpoints by k=D/rampContribution so ramp area equals D by construction"
    - "Frame-boundary anti-tunnel: clamp per-frame displacement to remaining distance and disengage before applying motion"

key-files:
  created: []
  modified:
    - "EcoSpace.Tests/WarpMotionProfileTests.cs"
    - "Scripts/Math/WarpMotionProfile.cs"
    - "Scripts/Flight/FlightController.cs"

key-decisions:
  - "Solve infeasible small-D fix: scale both vLaunch and vTerminal by k=D/rampContribution and set vCruise=0 (chosen over shortening ramp duration or a special near-arrival profile) — keeps the smoothstep ramp shape intact and the velocity integral = D by construction, with no Solve signature change."
  - "Anti-tunnel implemented as a disengage-before-motion guard in _WarpProcess (step >= remainingToSoi → DisengageWarp; return) rather than clamping the ApplyMotion displacement, keeping the warp/manual motion paths uniform."
  - "Stranded-warp backstop margin WarpTimeoutMarginSec = 2.0 s — bounded, disengages shortly after planned T_sel."

patterns-established:
  - "Red-then-green gap closure: failing regression test committed first (67bc78c) proving the bug, then the fix (c27f566) flips it green."

requirements-completed: [P3-TIMING, WR-02, D-03, D-08, D-10]

duration: 8 min
completed: 2026-06-22
status: complete
---

# Phase 8 Plan 3: Warp Gap Closure Summary

**Closed both Phase-8 BLOCKER gaps: restored the area=D / exact-arrival invariant for small-D warp legs via Solve endpoint-scaling, and added a per-frame SOI-arrival displacement clamp plus a timeout backstop so the ship cannot tunnel past the target SOI on an oversized frame.**

## Performance

- **Duration:** ~8 min
- **Started:** 2026-06-22T20:37:49Z
- **Completed:** 2026-06-22T20:46:00Z
- **Tasks:** 3
- **Files modified:** 3

## Accomplishments
- BLOCKER 2 (P3-TIMING): `WarpMotionProfile.Solve()` now preserves the area=D invariant in the infeasible small-D regime (D < ramp contribution). Previously the velocity integral was ~4e7 m for D=1000 m (a ~40,000× overshoot); now it equals D within 1%.
- BLOCKER 1 (WR-02): `_WarpProcess` clamps the per-frame warp step to the SOI arrival point — if `warpSpeed * delta >= remainingToSoi` it disengages BEFORE `ApplyMotion` runs, so a single oversized frame (hitch / alt-tab / breakpoint) at cruise speed (~3e20 m/s) can no longer tunnel the ship past the SOI.
- Added a stranded-warp absolute timeout backstop (`_warpElapsedSec > TSel + WarpTimeoutMarginSec → DisengageWarp`).
- Added `SmallD_IntegralEqualsD` regression test (red-then-green) so the area=D invariant is now verified in the small-D regime, not just finiteness.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add failing small-D area=D regression test (RED)** - `67bc78c` (test) — failed with integral 4.000E+007 vs D 1.000E+003, proving the regression.
2. **Task 2: Fix Solve infeasible small-D case (GREEN)** - `c27f566` (fix) — endpoint scaling; full suite 67/67 green.
3. **Task 3: Per-frame SOI-arrival clamp + warp timeout backstop (WR-02)** - `ae054dd` (fix) — `EcoSpace.csproj` build succeeds.

## Files Created/Modified
- `EcoSpace.Tests/WarpMotionProfileTests.cs` - Added `SmallD_IntegralEqualsD` [Fact] asserting `Integrate(Solve(1000, 120, 1/3, 1e6, 1e6)) ≈ D` within 1%.
- `Scripts/Math/WarpMotionProfile.cs` - `Solve()` infeasible-case branch: when `rampContribution > d`, scale `vLaunch`/`vTerminal` by `k = d/rampContribution` and set `vCruise = 0`; feasible branch unchanged. Signature unchanged; struct stays Godot-free / global namespace.
- `Scripts/Flight/FlightController.cs` - New private const `WarpTimeoutMarginSec = 2.0`; `_WarpProcess` adds the timeout backstop and the `remainingToSoi = dist - target.SOIMeters` per-frame clamp (disengage-before-motion). Existing `dist < target.SOIMeters` early-disengage retained.

## Decisions Made
- See key-decisions in frontmatter. Endpoint-scaling was chosen over ramp-shortening / special profile because it keeps the smoothstep shape and the Solve signature intact while making the integral = D by construction.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None. (Pre-existing nullability warnings CS8765/CS8618/etc. in unrelated Math/Render files surfaced during the test build; they predate this plan and were not introduced or touched here.)

## User Setup Required
None - no external service configuration required.

## Verification Results
- `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj` — 67/67 passed (66 prior + new `SmallD_IntegralEqualsD`).
- `SmallD_IntegralEqualsD` confirmed RED at commit 67bc78c (integral 4e7 vs 1000), GREEN after c27f566.
- `dotnet build EcoSpace.csproj` — Build succeeded with the FlightController clamp + timeout additions.

## Self-Check: PASSED
- All 3 tasks' acceptance criteria verified (test red→green, Solve signature unchanged, build green, clamp + timeout + early-disengage all present).
- Both must-have truths now positively observable: small-D integral ≈ D within 1%; per-frame step clamped to SOI boundary + absolute timeout backstop.

## Next Phase Readiness
- Both BLOCKERs closed. Ready for re-verification (`/gsd-verify-work 08`).
- The WARNING-level WarpConfirmationScreen v_c display inaccuracy (uses ManualMaxSpeed for both endpoints while EngageWarp uses _easedSpeed as vLaunch) was intentionally left out of this gap-closure scope; it remains a non-blocking cosmetic item if the user wants it addressed later.

---
*Phase: 08-warp-motion-profile*
*Completed: 2026-06-22*
