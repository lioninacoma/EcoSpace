---
phase: 08-warp-motion-profile
verified: 2026-06-22T00:00:00Z
status: gaps_found
score: 6/8 must-haves verified
behavior_unverified: 0
overrides_applied: 0
gaps:
  - truth: "The ship decelerates smoothly into the target SOI boundary and does not tunnel past it on a single fast frame (WR-02)."
    status: failed
    reason: >
      _WarpProcess evaluates dist < target.SOIMeters at the start of the frame, then
      accumulates _warpElapsedSec += delta and derives warpSpeed = profile.Velocity(elapsed).
      ApplyMotion(delta) is called AFTER _WarpProcess returns (FlightController.cs line 475),
      applying CurrentSpeed * delta displacement with no clamp relative to remaining distance.
      During the cruise phase at intergalactic scale, VCruise ≈ 3e20 m/s; with a 0.05 s
      frame hitch the displacement is ~1.5e19 m — 4-10 orders of magnitude larger than
      PlanetSOI = 1e9 m or StarSOI = 1.5e15 m. The ship jumps from outside to far past the SOI
      in one frame. On the next frame dist is large again (ship receding), dist < SOIMeters
      is never true, warp runs until elapsed > TSel, and Velocity clamps to VTerminal.
      The ship drifts away from the target forever. The PLAN comment "Per-frame step never
      exceeds remaining distance ... because the decel ramp ends at ManualMaxSpeed at the
      boundary" only holds under steady small-delta integration; it is not robust to a single
      oversized frame. ROADMAP WR-02 explicitly prescribed "Clamp the per-frame step to the
      SOI arrival point." No such clamp exists in the delivered code.
    artifacts:
      - path: "Scripts/Flight/FlightController.cs"
        issue: >
          _WarpProcess (lines 902-970) has no per-frame displacement clamp. ApplyMotion
          is called unconditionally with CurrentSpeed * delta after _WarpProcess sets speed;
          the SOI arrival guard fires only on the NEXT frame's entry check. Lines 920-929:
          dist is sampled, disengage fires if dist<SOI, then elapsed accumulates and speed
          is written — but motion is not applied until after return, without capping
          the step to (dist - SOIMeters).
    missing:
      - >
        Add a per-frame displacement clamp in _WarpProcess before writing speed:
          double remainingToSoi = dist - target.SOIMeters;
          double step = warpSpeed * delta;
          if (step >= remainingToSoi) { DisengageWarp(); return; }
        Alternatively, clamp the ApplyMotion displacement for the Warping path so it
        cannot exceed remainingToSoi in one step. Also add an absolute timeout
        (_warpElapsedSec > TSel + margin → DisengageWarp) as a backstop against stranded-warp.
  - truth: "Warp arrival time equals the player-selected travel time T_sel exactly (for normal warp legs)."
    status: failed
    reason: >
      The area=D invariant breaks for small-D warps: when D < rampContribution
      (= f * T_sel * (VLaunch + VTerminal) / 2), Solve clamps vCruise to 0 but the ramps
      still run at VLaunch=1e6 and VTerminal=1e6. The integral of Velocity over [0, TSel]
      equals the ramp contribution (~f*T*vAvg), not D. Concretely: D=1000 m, vLaunch=vTerminal=1e6,
      f=1/3, T=120 s → rampContrib ≈ f*120*1e6 = 4e7 m; the ship travels ~4e7 m instead of
      1000 m — a 40,000x overshoot of the geometric target distance. This is exactly the
      configuration EngageWarp produces for a target just outside its SOI (D = d0 - SOIMeters
      is small while vLaunch = _easedSpeed ≈ ManualMaxSpeed = 1e6). The SmallD_AllOutputsFinite
      test only asserts finiteness, never Integrate(p) ≈ D for the small-D case; the regression
      is invisible to the test suite.
    artifacts:
      - path: "Scripts/Math/WarpMotionProfile.cs"
        issue: >
          Solve() lines 123-129: rampContribution = f * tSel * (vLaunch + vTerminal) * 0.5.
          When d < rampContribution, numerator < 0, vCruise is clamped to 0 (line 129).
          With vCruise=0, Velocity() returns VLaunch*(1-s) during accel (which is ≥ vLaunch*0.5)
          and VTerminal*s during decel. Integral ≈ rampContrib >> D.
      - path: "EcoSpace.Tests/WarpMotionProfileTests.cs"
        issue: >
          SmallD_AllOutputsFinite (lines 110-118) only checks double.IsFinite on outputs;
          it does NOT assert Integrate(p) ≈ D for d=1 m (the infeasible case). The
          area=D invariant test (Integral_EqualsD_WithinRelativeTolerance) only covers
          d=1e9 and d=1e23 — both large enough that vCruise >> 0.
    missing:
      - >
        Add a test: for d=1000 m, T=120 s, f=1/3, vLaunch=vTerminal=1e6, assert
        Integrate(p) ≈ D within 1%. This will FAIL with the current Solve, exposing
        the regression.
      - >
        Fix Solve to handle the infeasible case (D < rampContrib): either scale vLaunch/
        vTerminal down so rampContrib <= D, or shorten the ramp duration, or detect and
        return a special "near-arrival" profile whose integral is D by construction.
        At minimum, the existing test suite must cover this case to prevent silent regressions.
human_verification: []
---

# Phase 8: Warp Motion Profile — Verification Report

**Phase Goal:** Replace Phase-7's exponential warp-approach timing with a proper kinematic
motion profile so warp arrival time matches the user-selected travel time across all scales,
and the ship decelerates *into* the target SOI rather than tunnelling past it on a frame boundary.

**Verified:** 2026-06-22
**Status:** gaps_found — 2 BLOCKERs
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | Warp arrival time equals T_sel exactly (closed-form, not approximate). | FAILED (BLOCKER) | Area=D invariant breaks for small-D legs (target just outside SOI): vCruise clamps to 0 but ramps still run at VLaunch/VTerminal=1e6. Integral ≈ rampContrib >> D. 40,000x overshoot. Test suite does not cover this case. |
| 2 | The ship decelerates smoothly into the target SOI and does not tunnel past it on a single fast frame (WR-02). | FAILED (BLOCKER) | No per-frame displacement clamp exists. ApplyMotion uses CurrentSpeed*delta unclamped. At VCruise~3e20 m/s and a 0.05 s frame hitch, displacement is ~1.5e19 m vs PlanetSOI=1e9 m. Ship overshoots, warp never re-arrives. |
| 3 | Engaging warp at peak speed and arriving both blend cleanly into manual flight at ManualMaxSpeed (no speed pop). | VERIFIED | Profile terminal speed = _manualMaxSpeed (line 868 EngageWarp). VTerminal written at decel ramp end. DisengageWarp leaves _easedSpeed as-is; UpdateSpeedEnvelope ease-down is the safety net. |
| 4 | EngageWarp with an invalid/unresolved target is a safe no-op and stays in Manual state (WR-03). | VERIFIED | Lines 843-853: bounds-safe double check with (uint) cast pattern; null checks on shipObj/tgtObj; early return before state mutation; _warpState stays Manual on all failure paths. |
| 5 | The WarpMaxSpeed cap no longer influences warp speed; v_c is whatever the closed form requires. | VERIFIED | `grep WarpMaxSpeed FlightController.cs` → no match (only dead commented-out constant on line 43). WarpAccelFraction [Export] present at line 204. _WarpProcess uses profile.Velocity() exclusively. |
| 6 | UniMath.NormalizedDirection no longer rejects large real separations as coincident; its threshold is correct for integer-unit space and its comment is accurate (WR-01). | VERIFIED | UniMath.cs lines 231-238: threshold changed from `mag < 1e-3` to `mag < 0.5`; comment rewritten to say "INTEGER UNITS" and explains 0.5 is correct sub-unit coincidence cutoff. No `1e-3` appears in UniMath.cs. |
| 7 | Open-space manual top speed reaches the tier ceiling instead of half of it (WR-04). | VERIFIED | FlightController.cs line 662: `nearest = tierCeiling / System.Math.Max(_speedPerMeter, 1e-11)`. Then targetMax = Clamp(nearest * _speedPerMeter, ...) = tierCeiling. Old Max(..., 1.0) sentinel removed. |
| 8 | The warp confirmation screen shows the peak cruise speed v_c and applies no travel-time clamp. | VERIFIED | WarpConfirmationScreen.cs lines 233-239: computes profileDist, reads WarpAccelFraction, computes vc using exact area formula, displays via Hud.FormatSpeed. WarpMaxSpeed appears only in a comment. No travel-time clamp in the display path. |

**Score: 6/8 truths verified (2 BLOCKER gaps)**

---

## CR-01 Assessment (Frame-Boundary Anti-Tunnel)

This is a BLOCKER. The code evidence is unambiguous:

**Sequence of events per warp frame (FlightController.cs):**

```
_Process(delta) line 470-479:
  → _WarpProcess(delta)                      // lines 902-970
       dist = UniMath.Distance(ship, target)  // line 920 — start-of-frame sample
       if (dist < SOIMeters) DisengageWarp()  // line 923 — ONLY arrival guard
       _warpElapsedSec += delta               // line 928
       warpSpeed = _warpProfile.Velocity(..)  // line 929
       _easedSpeed = warpSpeed                // line 968
       CurrentSpeed = _easedSpeed             // line 969
  → ApplyMotion(delta)                        // line 475 — AFTER _WarpProcess returns
       displacement = CurrentSpeed * delta    // line 741 — NO clamp vs remaining distance
       world.TranslatePos(...)                // moves ship unrestricted
```

The PLAN's comment "The decel ramp ends at ManualMaxSpeed at the boundary (resolves WR-02 tunnelling)" is only true under steady small-delta. Under a single large delta (frame hitch, alt-tab, low FPS spike, editor breakpoint), the ship travels many multiples of the SOI radius in one `ApplyMotion` call, landing on the far side. The next `_WarpProcess` call sees a large positive `dist` again and warp continues indefinitely.

**ROADMAP WR-02** explicitly prescribed: "Clamp the per-frame step to the SOI arrival point." This clamp was not implemented. The implementation resolved WR-02 by claiming the decel ramp construction prevents tunneling — a claim that only holds under steady integration, not on a single fast frame.

**Verdict: FAILED (BLOCKER).** The phase goal's headline "tunnelling past it ON A FRAME BOUNDARY" guarantee is not met.

---

## WR-01 (Review) Assessment (Small-D Area=D Invariant)

This is a BLOCKER (maps to must-have truth 1: "exact arrival time across all scales").

**Math trace (WarpMotionProfile.cs Solve, lines 123-129):**

```
rampContrib = f * T_sel * (vLaunch + vTerminal) / 2
            = (1/3) * 120 * (1e6 + 1e6) / 2
            = 4e7 m   [for default config]

Case: D = d0 - SOIMeters, e.g. d0=1.001e9, SOI=1e9 → D=1000 m

numerator = 1000 - 4e7 = -39,999,000
vCruise   = Max(0, numerator/denominator) = 0     ← clamped to 0

Velocity integrates as:
  accel:  VLaunch=1e6 → VCruise=0  (ramp covers f*T=40s, area ≈ f*T*vLaunch/2 = 2e7 m)
  cruise: flat 0                    (area = 0)
  decel:  VCruise=0 → VTerminal=1e6 (ramp covers f*T=40s, area ≈ f*T*vTerminal/2 = 2e7 m)

Integral ≈ 4e7 m   vs   D = 1000 m   →  40,000× overshoot
```

The ship reaches a target just outside its SOI from 1.001×SOI away, then flies 4e7 m deeper into the body instead of stopping at the SOI boundary. The dist < SOIMeters disengage fires early once the ship enters the SOI, so the behavior doesn't cause a crash — but the exact-timing invariant is violated, which is the headline correctness requirement ("arrival time matches T_sel exactly across all scales").

The test `SmallD_AllOutputsFinite` only checks `double.IsFinite` — it does not catch this. The failing case is not covered by any test.

---

## Required Artifacts

| Artifact | Expected | Status | Details |
|----------|---------|--------|---------|
| `Scripts/Math/WarpMotionProfile.cs` | Pure closed-form struct Solve+Velocity | VERIFIED | Exists, 198 lines, Godot-free, global namespace, correct struct shape. |
| `EcoSpace.Tests/WarpMotionProfileTests.cs` | xUnit assertions including area=D invariant | PARTIAL / STUB for small-D | 18 tests exist. area=D covered for d=1e9 and d=1e23 only. Small-D case only asserts finiteness. Integral_EqualsD tests DO NOT cover the infeasible (D < rampContrib) regime. |
| `Scripts/Flight/FlightController.cs` | Elapsed-time driven warp, WarpAccelFraction, WarpMaxSpeed removed, WR-03 gate | VERIFIED for 4 of 5 sub-items; FAILED for anti-tunnel | WarpAccelFraction present line 212; WarpMaxSpeed absent; _warpElapsedSec present line 294; WR-03 gate present lines 843-853; BUT no per-frame displacement clamp. |

## Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Scripts/Flight/FlightController.cs` | `Scripts/Math/WarpMotionProfile.cs` | `_WarpProcess` evaluates `_warpProfile.Velocity(_warpElapsedSec)` | VERIFIED | Lines 929, 300, 866-868. |
| `Scripts/Flight/FlightController.cs` | `Scripts/Math/UniMath.cs` | `EngageWarp` captures d0 via `UniMath.Distance` | VERIFIED | Line 857. |
| `Scripts/Hud/WarpConfirmationScreen.cs` | `Scripts/Flight/FlightController.cs` | reads `WarpAccelFraction` to compute v_c | VERIFIED | Lines 234-237. |
| `Scripts/Flight/FlightController.cs` | `Scripts/Math/UniMath.cs` | `UpdateSpeedEnvelope` open-space sentinel resolves to tierCeiling | VERIFIED | Line 662: `tierCeiling / Max(_speedPerMeter, 1e-11)`. |

## Requirements Coverage

Phase 8 requirement IDs are internal to the phase (P3-TIMING, WR-01..04, D-01..D-15); they are
not listed in REQUIREMENTS.md (which tracks FLT-xx, RND-xx, etc.). The mapping is:

| Phase Req | Description | Status | Evidence |
|-----------|-------------|--------|---------|
| P3-TIMING | Exact closed-form timing (replace exponential decay) | PARTIAL | Profile struct exists and integral=D holds for normal legs. Small-D case breaks timing invariant. |
| WR-01 | NormalizedDirection threshold fix | VERIFIED | 1e-3 → 0.5, comment corrected. |
| WR-02 | Anti-tunnel: clamp per-frame step to SOI arrival point | FAILED | No per-frame displacement clamp. BLOCKER. |
| WR-03 | EngageWarp safe no-op on invalid target | VERIFIED | Lines 843-853 gate present. |
| WR-04 | Open-space manual top speed reaches tier ceiling | VERIFIED | Line 662 sentinel corrected. |
| D-01 | Closed-form profile, T_sel is exact input (no position rescale) | VERIFIED | TSel stored as-is; test Solve_DoesNotRescalePosition passes. |
| D-02 | WarpAccelFraction [Export] with [0,0.5] clamp | VERIFIED | Line 204-215. |
| D-03 | v_c = (D - rampContrib) / (T_sel*(1-f)) | VERIFIED (formula correct) but breaks for small D | |
| D-04 | Smoothstep s(u)=3u²-2u³ hand-rolled | VERIFIED | Lines 192-197. |
| D-05 | WarpMaxSpeed removed | VERIFIED | No occurrence in FlightController.cs. |
| D-06 | T_sel ≥ 1.0 s clamp retained; IsFinite guard retained | VERIFIED | Lines 839, 932. |
| D-07 | Terminal speed = ManualMaxSpeed | VERIFIED | Line 868. |
| D-08 | D = d0 - SOIMeters | VERIFIED | Line 858. |
| D-09 | Launch speed = _easedSpeed | VERIFIED | Line 867. |
| D-10 | Elapsed-time driven; dist<SOI disengage kept | VERIFIED | Lines 928-929, 923. |
| D-11 | Auto-orient slerp retained | VERIFIED | Lines 937-964. |
| D-12 | Confirmation screen shows v_c, no WarpMaxSpeed | VERIFIED | Lines 233-239. |
| D-13 | NormalizedDirection comment accuracy (WR-01) | VERIFIED | Lines 231-235. |
| D-14 | WR-03 gate | VERIFIED | Lines 843-853. |
| D-15 | Open-space sentinel fix (WR-04) | VERIFIED | Line 662. |

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|---------|--------|
| `Scripts/Flight/FlightController.cs` | 43 | Commented-out dead code `// private const double SpeedOfLight = 3e8;` | INFO | Cosmetic; no functional impact. |
| `Scripts/Hud/WarpConfirmationScreen.cs` | 236 | v_c formula uses `ManualMaxSpeed` for both ramp endpoints (assumes vLaunch=ManualMaxSpeed), but EngageWarp uses _easedSpeed as vLaunch, which may differ | WARNING | Displayed cruise speed is inaccurate when ship is not already at ManualMaxSpeed when panel opens. |
| `EcoSpace.Tests/WarpMotionProfileTests.cs` | 110-118 | SmallD_AllOutputsFinite does not assert Integrate(p)≈D | BLOCKER | Area=D invariant is not verified for the infeasible regime; regression is invisible. |

## Behavioral Spot-Checks

Step 7b: The project requires Godot runtime; static analysis only.

Unit test suite: `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj` — summary claims 66/66 pass.
The passing tests prove the formula is correct for large-D inputs (d=1e9, d=1e23). They do NOT
cover the small-D infeasible regime or per-frame tunneling protection (which is a runtime invariant,
not testable as a unit test without a mocked delta).

## Gaps Summary

Two BLOCKERs prevent the phase goal from being fully achieved:

**BLOCKER 1 — WR-02 frame-boundary anti-tunnel (must-have truth 2):** The phase goal's explicit
promise "decelerates into the target SOI rather than tunnelling past it on a frame boundary" is
not met. `_WarpProcess` evaluates the arrival guard at frame start, then sets speed and returns.
`ApplyMotion` applies `CurrentSpeed * delta` with no per-frame clamp relative to remaining
distance to the SOI. A single large frame (hitch, alt-tab, breakpoint) can displace the ship
past the entire SOI in one step; the next frame's arrival guard fires in the wrong direction
and warp never terminates. ROADMAP WR-02 prescribed a per-frame step clamp; the implementation
resolved it by design argument (decel ramp) that only holds under steady small-delta integration.

**BLOCKER 2 — Small-D area=D invariant break (must-have truth 1):** For targets just outside
their SOI (D = d0 - SOIMeters is small), the vCruise clamp to 0 leaves full-strength ramps
intact, producing a profile whose integral is ~40,000× larger than D. Arrival time is not exact
in this regime. The test suite does not catch this — SmallD_AllOutputsFinite only checks finiteness.

Both gaps require code changes (displacement clamp in FlightController._WarpProcess; Solve
infeasible-case handling in WarpMotionProfile; at minimum a failing test to surface the regression).

---

_Verified: 2026-06-22_
_Verifier: Claude (gsd-verifier)_
