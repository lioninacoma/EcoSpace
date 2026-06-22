# Phase 8: Warp Motion Profile - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-22
**Phase:** 8-warp-motion-profile
**Areas discussed:** Profile shape / acceleration, Time-vs-cap conflict, Arrival hand-off, Cleanup scope

---

## Reference source provided by user

Mid-discussion the user pasted a Unity `TrapezoidMotionProfile` class (jerk-limited
S-curve trapezoid: acceleration ramps linearly over `rampUpTime`, cruise at `maxSpeed`,
mirror decel; auto-detects trapezoid vs triangle; `Eval(t)` returns `(pos, speed)`; total
time is an *output*, and position is rescaled via `scale = distance / totalProfileDistance`).

User direction: *"get inspirations from this source file ... and improve it, i want
reproducible and exactly timed motion, so configured time of flight is exact."*

**Resolution:** adopt the trapezoid + jerk-limited ramp shape; **invert** the model so
`T_sel` is the exact input (not a derived output), and replace the position-rescale hack
with a closed-form solve. This redirected the originally-planned "profile shape" question
(fixed-a vs thirds vs distance-fraction) — see below.

---

## Profile shape / acceleration

Originally planned options (fixed-acceleration export, time-fraction ramp, distance-fraction
ramp) were superseded by the reference-driven design above. The chosen design:

- Closed-form **symmetric smoothstep trapezoid** solved for exact `T_sel`.
- Free knob = **accel-time fraction `f`** (default ⅓), exported.
- Cruise velocity derived per-warp `v_c = D / (T_sel·(1−f))` — scale-invariant.
- Smoothstep ramp (`3u²−2u³`) chosen over the reference's asymmetric cubic+linear ramp
  because its area is exactly `½·v_c·t_a`, preserving exact-time analytically while staying
  jerk-limited at the endpoints.

**User's choice:** Reference-inspired closed-form trapezoid with exact-time guarantee.
**Notes:** Exactness/reproducibility is the hard requirement; this drove every downstream choice.

---

## Time-vs-cap conflict

| Option | Description | Selected |
|--------|-------------|----------|
| Clamp selectable time | Confirmation screen floors the time input at `T_min = D/(WarpMaxSpeed·(1−f))`; any selectable `T_sel` is then exact. Keeps the cap. | |
| Cap speed, arrive late | Allow any `T_sel`; if it exceeds the cap, cruise at `WarpMaxSpeed` and arrive later — time NOT exact. | |
| Drop the speed cap | Treat `WarpMaxSpeed` as non-binding so `T_sel` is ALWAYS exact (fictional FTL). Loses the safety ceiling; needs a different pathological guard. | ✓ |

**User's choice:** Drop the speed cap.
**Notes:** Supersedes Phase-7 D-07. Retain `T_sel ≥ 1s` and `double.IsFinite` guards in place of the cap.

---

## Arrival hand-off

| Option | Description | Selected |
|--------|-------------|----------|
| Ease to ManualMaxSpeed | Decel ends at `ManualMaxSpeed` (1e6 m/s) at the SOI boundary; ship sails across still moving, seamless hand to manual. | ✓ |
| Ease to near-zero | Full 0→v_c→0 trapezoid; ship arrives essentially stopped at the SOI edge, manual from rest. | |
| Configurable terminal speed | Expose a terminal-velocity export (default ManualMaxSpeed); tune by play-test. | |

**User's choice:** Ease to ManualMaxSpeed.
**Notes:** Profile distance `D = d0 − SOIMeters`. Launch made symmetric too — ramp starts at the ship's current manual speed so warp-in and warp-out both blend cleanly (no speed pop).

---

## Cleanup scope

| Option | Description | Selected |
|--------|-------------|----------|
| WR-01 direction threshold | Fix `UniMath.NormalizedDirection` coincidence threshold `mag < 1e-3` — unit space, not metres (+ stale comment). Affects warp auto-orient. | ✓ |
| WR-03 gate EngageWarp | Gate `EngageWarp` on a valid resolved target (bounds-safe ship + target). | ✓ |
| WR-04 open-space speed | Fix the open-space sentinel halving manual speed at default `SpeedPerMeter=0.5`; ~doubles open-space manual top speed (re-tune expected). | ✓ |

**User's choice:** Fold all three.
**Notes:** WR-02 (SOI tunnelling) is resolved structurally by the decel ramp shrinking the per-frame step near arrival — not a separate folded item.

---

## Claude's Discretion

- Profile class structure / method names / elapsed-time state placement (consistent with `FlightController` conventions).
- Smoothstep evaluation (`Mathf.SmoothStep` vs hand-rolled) and numerical edge handling at small `D` / `T_sel` floor.
- Confirmation-screen layout for the new peak-`v_c` display (units / scientific-notation formatting per HUD convention).

## Deferred Ideas

- Travel-cost mechanic (speed↔cost) — future phase; `T_sel` / `v_c` are the attachment points.
- Body collision avoidance during warp — its own future phase.
- Warp visual FX (Phase 999.4) — depends on this profile shipping.
- Broader manual-flight speed-model re-tune after the WR-04 fix — noted, not scoped here beyond the bug fix.
