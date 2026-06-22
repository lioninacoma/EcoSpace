---
phase: 07-autopilot-warp-drive
verified: 2026-06-22T20:30:00Z
status: verified_with_concerns
score: 7/8 must-haves verified
behavior_unverified: 0
overrides_applied: 0
re_verification:
human_verification:
  - test: "Intergalactic warp transit time vs selected travel time, when dist/T_int exceeds WarpMaxSpeed cap"
    expected: "Arrival within a few minutes for an intergalactic hop; selected travel time approximately honored (exact accuracy is the known P3 deferral)"
    why_human: "Real-time motion + exponential-approach timing under the speed cap is not unit-testable; covered by the approved play-test and the deferred Phase-8 trapezoidal-profile rework"
deferred:
  - truth: "Warp arrival time exactly matches the selected travel time when dist/T_int is clamped by WarpMaxSpeed"
    addressed_in: "Phase 8 (planned)"
    evidence: "P3 tech-debt warp-timing-inaccuracy-under-speed-cap.md filed (commit 02c4238); Phase 8 to replace exponential-approach timing with a trapezoidal/triangular motion profile"
concerns:
  - id: WR-02
    severity: warning
    summary: "Warp can tunnel through small (Planet/Star) target SOIs in a single frame at WarpMaxSpeed; the dist < SOIMeters disengage test can be skipped on a frame boundary. Does NOT affect the intergalactic/headline case (galaxy SOI >> per-frame step)."
  - id: WR-03
    severity: warning
    summary: "EngageWarp enters Warping even if the target was cleared between opening the panel and pressing Enter; _WarpProcess self-disengages next frame but one frame runs in Warping with stale CurrentSpeed."
  - id: IN-02
    severity: info
    summary: "Confirmation-screen warp-speed display uses _selectedTravelTimeSec while the controller flies at the calibrated _warpInternalTimeSec, so the shown speed differs from the actual initial warp speed (cosmetic)."
---

# Phase 7: Autopilot & Warp Drive Verification Report

**Phase Goal:** Distance-ranked (resolved to target-reuse per locked D-01), space-independent target selection + autopilot traversal ("warp drive") — manual flight capped at km/s, warp is autopilot-only and reaches FTL-equivalent for intergalactic transit in minutes.

**Verified:** 2026-06-22
**Status:** verified_with_concerns
**Re-verification:** No — initial verification

## Goal Achievement

The phase goal is **achieved**. The two coupled capabilities — a km/s-capped manual flight path and an autopilot-only warp drive that reuses the Phase-6 target, auto-orients, flies at distance/time speed, and auto-disengages at the target SOI — are all present, substantively implemented, and wired end-to-end (InputMap → confirmation screen → FlightController state machine → motion). The build is clean and the full 8-behavior play-test was approved by a human (commit `02c4238`). One concern (single-frame SOI tunneling at small SOIs) and the knowingly-deferred P3 timing inaccuracy keep this at `verified_with_concerns` rather than a clean pass.

> Note on scope: the original 999.3 "distance-ranked candidate set" was superseded during discuss-phase by locked decision **D-01** (warp reuses `Hud.ActiveTargetIndex` from the Phase-6 tree selector). The roadmap goal line still reads "distance-ranked"; the contract that Phase 7 actually shipped is target-reuse. Verified against the as-locked WARP-01 requirement, not the superseded framing.

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Manual throttle never exceeds ManualMaxSpeed (1e6 m/s) regardless of tier or target | ✓ VERIFIED | `FlightController.cs:683-684` clamps `targetMax = Min(targetMax, _manualMaxSpeed)` only when `_warpState == Manual`; `_manualMaxSpeed = 1e6` (line 191). Tier ceiling/proximity damp still computed but capped. |
| 2 | Warp is autopilot-only; large (FTL-equiv) speeds live ONLY in the warp path | ✓ VERIFIED | Large speed is produced solely in `_WarpProcess` (`warpSpeed = Min(dist/_warpInternalTimeSec, _warpMaxSpeed)`, line 913) which writes `CurrentSpeed` directly; the manual `UpdateSpeedEnvelope` cannot exceed 1e6 (truth 1). `WarpMaxSpeed = 2e20` (line 204). |
| 3 | Pressing J with an active target opens confirmation; Enter engages warp; ship auto-orients toward target and flies at distance/time speed | ✓ VERIFIED | `WarpConfirmationScreen._UnhandledInput` opens on `warp_engage` only when `ActiveTargetIndex >= 0` (line 136-141); Enter → `ClosePanel()` then `EngageWarp` (161-162); `EngageWarp` sets `WarpState.Warping` (line 861); `_WarpProcess` Slerps `_shipBasis` toward `NormalizedDirection` (921-947) and flies at dist/time. |
| 4 | Warp auto-disengages when ship enters target SOI, easing speed down to ManualMaxSpeed (no hard stop) | ✓ VERIFIED | `_WarpProcess:909` `if (dist < target.SOIMeters) { DisengageWarp(); return; }`; `DisengageWarp` only sets state to Manual and does NOT zero `CurrentSpeed` (870-873), so `UpdateSpeedEnvelope`'s lerp eases down to the manual cap (D-19). |
| 5 | During warp, throttle and steering are ignored; only look-around responds | ✓ VERIFIED | `_Process` Warping case (465-474) calls only `UpdateLookAround` + `_WarpProcess` + `ApplyMotion` — no `HandleThrottleInput`/`UpdateAttitude`. `EngageWarp` zeroes `_cursor` (860). |
| 6 | Left Alt look-around decouples camera from heading in manual & warp; eases back on release | ✓ VERIFIED | `look_around` InputMap = physical_keycode 4194328 (Left Alt). `_rawMouseDelta` accumulates into `_cameraOffset` while held (785-805); ease-back via Quaternion Slerp over ~0.3 s on release (808-815). Called in both manual (`UpdateAttitude`) and warp (`_Process`). Dual-field `_rawMouseDelta`/`_cursor` prevents the cursor-bleed bug. |
| 7 | Warp navigation uses safe cross-frame math (UniMath, never raw ToDouble3 across frames) | ✓ VERIFIED | `_WarpProcess` uses `UniMath.Distance` (906) and `UniMath.NormalizedDirection` (921); `EngageWarp` uses `UniMath.Distance` (846); confirmation screen uses `UniMath.Distance` (222). `NormalizedDirection` normalizes in unit-space (Long3 ratios, scale cancels) — precision-correct at intergalactic scale. |
| 8 | Warp reaches FTL-equivalent and completes an intergalactic transit in minutes | ⚠️ PARTIAL (play-test approved; exact timing deferred P3) | Mechanism present: 2e20 m/s cap × ~2.4e22 m separation ⇒ ~2 min order-of-magnitude; play-test of all 8 behaviors APPROVED (02c4238). Exact arrival-time accuracy under the WarpMaxSpeed cap is the knowingly-deferred P3 (Phase 8 trapezoidal profile). Headline "minutes" outcome confirmed by play-test; treated as deferred, not a goal failure. |

**Score:** 7/8 truths verified (truth 8 partial/play-test-confirmed with deferred exact-timing).

### Deferred Items

| # | Item | Addressed In | Evidence |
|---|------|-------------|----------|
| 1 | Exact warp arrival time = selected travel time when `dist/T_int` exceeds WarpMaxSpeed | Phase 8 (planned) | `warp-timing-inaccuracy-under-speed-cap.md` P3 tech-debt (commit 02c4238); exponential-approach formula to be replaced by trapezoidal/triangular profile |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `project.godot` | warp_engage (J=74) + look_around (Left Alt=4194328) InputMap actions | ✓ VERIFIED | Both present (lines 76-85); keycodes correct |
| `Scripts/Flight/FlightController.cs` | WarpState machine, Manual/Warp speed caps, look-around offset, EngageWarp/DisengageWarp/_WarpProcess | ✓ VERIFIED | All symbols present and substantive (956 lines); state switch in `_Process`; build clean |
| `Scripts/Hud/WarpConfirmationScreen.cs` | Phosphor-green confirmation panel, travel-time slider, Enter→EngageWarp, read-only | ✓ VERIFIED | Mirrors TargetSelectorPanel; HSlider FocusMode=None (Enter-capture fix); zero sim mutation (D-53) |
| `Scripts/Math/UniMath.cs` `NormalizedDirection` | Precise warp direction at any scale | ✓ VERIFIED | Unit-space normalize (231-233); precision design sound per code review |
| `Scripts/Math/UniVec3.cs` `ToDouble3Units` | Unit-space position for direction math | ✓ VERIFIED | `(Double3)Units + Offset/Scale` (138-141) |
| `Main.tscn` | WarpConfirmationScreen node under CanvasLayer, wired | ✓ VERIFIED | Node at line 198 with `WorldPath`/`HudPath`/`FlightPath` all set (213-215); ext_resource UID resolved (commit 12dedd2) |

### Key Link Verification

| From | To | Via | Status |
|------|----|----|--------|
| `WarpConfirmationScreen` Enter | `FlightController.EngageWarp` | `_flight?.EngageWarp(...)` after ClosePanel (line 161-162) | ✓ WIRED |
| `_WarpProcess` | `UniMath.Distance` / `NormalizedDirection` | cross-frame distance + orient direction (906, 921) | ✓ WIRED |
| `EngageWarp` | `WarpState.Warping` | sets `_warpState = WarpState.Warping` (861) | ✓ WIRED |
| `WarpConfirmationScreen` | `Hud.ActiveTargetIndex` | gate on `>= 0` + RefreshDisplay (140, 213) | ✓ WIRED |
| `_Process` Warping case | `ApplyMotion` → `TranslatePos` | SOI traversal via existing GameWorld (470, 736) | ✓ WIRED |

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| (none) | No TODO/FIXME/XXX/TBD/HACK/PLACEHOLDER in any modified file | — | Clean; no unauditable debt markers |
| `FlightController.cs:43` | Commented-out `SpeedOfLight` const (IN-03) | ℹ️ Info | Dead commented code; cosmetic only |

### Human Verification Required

1. **Intergalactic transit timing** — fly a warp from the home galaxy to the destination galaxy with a selected travel time (e.g. 2 min).
   - Expected: arrival in roughly the selected time / a few minutes; FTL-equivalent speed reached mid-transit. Exact-time accuracy under the WarpMaxSpeed cap is the known P3 deferral, not a pass/fail gate here.
   - Status: Already exercised — full 8-behavior play-test APPROVED (commit 02c4238). Listed for completeness.

### Gaps Summary

No blocking gaps. All eight required behaviors are implemented, wired, and play-test-approved with a clean build. The phase ships at `verified_with_concerns` for three reasons, none of which defeat the goal:

1. **Deferred (not a gap):** exact warp arrival-time accuracy under the `WarpMaxSpeed` cap — knowingly parked as P3, to be replaced by a trapezoidal/triangular profile in Phase 8. The headline "intergalactic transit in minutes" outcome is met.
2. **WR-02 (warning):** single-frame SOI tunneling can occur for *small* (Planet/Star) target SOIs at 2e20 m/s, because the `dist < SOIMeters` disengage is only sampled at frame boundaries. The intergalactic/headline case is unaffected (galaxy SOI ~5e20 m is ~150× a per-frame step). Worth clamping the per-frame step to the SOI arrival point in a follow-up.
3. **WR-03 (warning):** `EngageWarp` enters Warping even with a target cleared after the panel opened; `_WarpProcess` self-recovers on the next frame, but one frame runs in Warping with stale `CurrentSpeed`. Low impact; recommend gating the state transition on a valid target.

Code review (07-REVIEW.md) reported 0 critical, 4 warning, 4 info; the precision-critical design (`NormalizedDirection` unit-space normalization) was reviewed as sound. The warnings are robustness/cosmetic, consistent with this verdict.

---

_Verified: 2026-06-22T20:30:00Z_
_Verifier: Claude (gsd-verifier)_
