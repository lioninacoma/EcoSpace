# Phase 8: Warp Motion Profile - Context

**Gathered:** 2026-06-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 8 replaces the current **exponential warp-approach timing** with a **kinematic trapezoidal motion profile** so that warp arrival time matches the player-selected travel time *exactly* and the ship decelerates *into* the target SOI instead of tunnelling past it.

The Phase-7 warp drive currently drives speed as `warpSpeed = dist / _warpInternalTimeSec` (an exponential-decay curve), with `_warpInternalTimeSec` calibrated at `EngageWarp` so arrival *approximately* hits `T_sel` — but the `WarpMaxSpeed` cap (D-07) breaks that calibration, and a single fast frame can tunnel past the SOI boundary (WR-02). Phase 8 rewrites the per-frame warp speed as a closed-form, deterministic trapezoid driven by elapsed warp time.

**In scope:**
- New closed-form symmetric **smoothstep trapezoidal** velocity profile (accel → cruise → decel) solved for exact `T_sel`.
- Replace the `_warpInternalTimeSec` / `dist / T_int` decay logic in `_WarpProcess` and the calibration block in `EngageWarp`.
- Remove the `WarpMaxSpeed` cap (D-07) — supersedes that Phase-7 decision.
- Seamless arrival hand-off to manual flight at the SOI boundary.
- Fold the Phase-7 review cleanups WR-01, WR-03, WR-04.
- Update the `WarpConfirmationScreen` speed display to reflect the varying-speed profile.

**Out of scope (unchanged from Phase 7 — stay deferred):**
- Body collision avoidance (own future phase).
- Travel-cost mechanic tied to travel time (future phase).
- Warp visual FX (Phase 999.4 backlog).
- Manual-flight speed model beyond the WR-04 fix (Phase-4 tier ceiling / proximity damp stay as-is).

</domain>

<decisions>
## Implementation Decisions

### Motion Profile Shape (Area 1)
- **D-01:** Warp uses a **closed-form symmetric trapezoidal velocity profile** — accel → cruise at `v_c` → decel — driven analytically, not an exponential decay. Inspired by the user-provided `TrapezoidMotionProfile` reference, but **inverted**: `T_sel` is the *input* and is honored exactly, whereas the reference treats total time as an output and rescales position to fit distance (which distorts effective accel/speed). Do not replicate the reference's `scale = distance / totalProfileDistance` position-rescale hack.
- **D-02:** The free parameter is the **accel-time fraction `f`**: accel phase = decel phase = `f · T_sel`, cruise = `(1 − 2f) · T_sel`. Default `f = 1/3`, exposed as an editor `[Export]` tuning knob.
- **D-03:** Cruise velocity is derived per-warp: `v_c = D / (T_sel · (1 − f))` where `D` is the warp distance (see D-08). This is **scale-invariant** — the same `f` produces good feel at in-system (~1e9 m) and intergalactic (~1e23 m) distances with no per-tier acceleration constant.
- **D-04:** Accel/decel ramps use a **symmetric smoothstep curve** (`s(u) = 3u² − 2u³`), not the reference's asymmetric cubic-ramp-then-linear shape. Smoothstep is jerk-limited (zero acceleration at both endpoints) *and* its area is exactly `½·v_c·t_a`, so the exact-time relationship `D = v_c·(T_sel − f·T_sel)` holds analytically. A single smoothstep over the whole accel/decel phase makes a separate `rampUpTime`/jerk knob (as in the reference) unnecessary.

### Speed Cap / Time-vs-Cap (Area 3)
- **D-05:** The `WarpMaxSpeed` cap (Phase-7 D-07) is **dropped** — the selected travel time is *always* honored exactly, and `v_c` is whatever the closed form requires (warp is fictional FTL; an unbounded `v_c` is acceptable). **This supersedes Phase-7 D-07.** Remove or neutralize the `WarpMaxSpeed` export.
- **D-06:** Retain the two non-cap safety guards: clamp `T_sel ≥ 1.0 s` (division-by-zero, the existing T-07-01 mitigation) and the `double.IsFinite` check before writing speed (existing T-07-02 mitigation, → safe disengage on non-finite).

### Arrival Hand-off (Area 2)
- **D-07:** The decel ramp ends at **`ManualMaxSpeed`** (1e6 m/s) exactly at the SOI boundary — the ship sails across the boundary still moving and the player flies the final in-SOI approach. No dead stop. (Chosen over ease-to-zero and a configurable terminal-speed knob.)
- **D-08:** The profile distance is `D = d0 − target.SOIMeters` (decel completes *at* the SOI boundary, where D-08-from-Phase-7 disengages warp). `d0` = initial `UniMath.Distance(ship, target, objs)` at `EngageWarp`.
- **D-09:** The profile is **symmetric at both endpoints**: launch starts the velocity ramp at the ship's *current manual speed* (≈ ManualMaxSpeed) rather than 0, and arrival ends at ManualMaxSpeed — so both the warp-in and warp-out transitions blend cleanly into manual flight with no speed pop. (`SOI arrival = disengage` from Phase-7 D-08 is preserved; `_speedEasing` hand-down from D-19 still applies as the safety net.)

### Per-Frame Drive / Determinism (derived, locked)
- **D-10:** Warp is driven by **elapsed warp time** `t`, not by re-deriving speed from remaining distance each frame. Accumulate `t` since `EngageWarp` and set `CurrentSpeed = v(t)` from the closed-form profile. This keeps timing exact and reproducible (the user's core requirement). Keep `dist < target.SOIMeters` as the terminal/safety disengage so floating-origin integration drift can't overshoot.
- **D-11:** Auto-orientation (Phase-7 D-03 slerp toward target) is retained unchanged in structure; only the speed source changes. The WR-01 fix (below) corrects the direction-coincidence threshold it depends on.

### Confirmation Screen (derived, locked)
- **D-12:** Replace the old single "computed warp speed" line (Phase-7 D-15) with the **peak cruise speed `v_c`** (the max speed the ship reaches), shown alongside target name, distance, and the travel-time input. Since the cap is dropped, the travel-time input is **not** clamped/floored. Keep the retro phosphor-green aesthetic (Phase-7 D-16).

### Review Cleanups Folded In (Area 4)
- **D-13 (WR-01):** Fix `UniMath.NormalizedDirection` coincidence threshold — `mag < 1e-3` is evaluated in **unit space, not metres**; correct the threshold and the stale comment. Directly affects warp auto-orient (D-11).
- **D-14 (WR-03):** Gate `EngageWarp` on a **valid resolved target** — bounds-safe ship + target lookup must succeed before starting the profile; no-op / safe abort otherwise.
- **D-15 (WR-04):** Fix the open-space sentinel in `UpdateSpeedEnvelope` (`FlightController.cs:653`): at default `SpeedPerMeter = 0.5`, `nearest = tierCeiling / max(0.5, 1.0)` then `targetMax = nearest × 0.5` halves the open-space manual target to `tierCeiling × 0.5`. Fixing it ~doubles open-space manual top speed — **a manual-flight feel change**; expect to re-tune `SpeedPerMeter` / tier factors during play-test.

### Claude's Discretion
- Exact internal structure of the profile class (standalone helper struct/class vs inline in `FlightController`), method names, and where elapsed-time state lives — Claude picks, consistent with existing `FlightController` conventions.
- Exact smoothstep evaluation (Godot `Mathf.SmoothStep` vs hand-rolled `3u²−2u³`) and numerical edge handling (very small `D`, `T_sel` at the 1 s floor).
- Confirmation-screen layout details for the new `v_c` display (units/scientific-notation formatting consistent with the HUD speed convention).

### Folded Todos
None folded from the todo system — the Phase-8 seeds (P3 timing debt + WR-01/02/03/04) arrived via the Phase-7 review/verify hand-off and the ROADMAP entry, already captured above.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Warp Drive Implementation (Phase 8 rewrites this directly)
- `Scripts/Flight/FlightController.cs` — `EngageWarp` (calibration block to replace, lines ~827–862), `_WarpProcess` (per-frame warp speed/orient/disengage, lines ~888–954), `UpdateSpeedEnvelope` open-space sentinel (WR-04, line ~653), `ManualMaxSpeed`/`WarpMaxSpeed` exports (~198–224), `WarpState` machine. This is the primary file changed.
- `.planning/phases/07-autopilot-warp-drive/07-CONTEXT.md` — locked Phase-7 warp decisions D-01..D-19. Phase 8 **supersedes D-07** (cap dropped) and **rewrites the speed source behind D-06** (was `dist / T_int`); preserves D-08 (SOI arrival = disengage), D-18 (on-rails), D-19 (ease-down hand-off).

### Universe Math (warp navigation — all distances LCA-safe)
- `Scripts/Math/UniMath.cs` — `Distance`, `RelativeMetres`, `NormalizedDirection` (WR-01 fix lives here). All warp distance/direction math MUST use `UniMath`, never raw `UniVec3.Distance` / `ToDouble3()` across frames.
- `CLAUDE.md` §"Position Math (UniVec3 / UniMath)" — the canonical cross-frame math rule.

### Warp Confirmation Screen (speed display update — D-12)
- `Scripts/Hud/WarpConfirmationScreen.cs` (and its `Main.tscn` wiring) — read-only consumer; update the computed-speed line to peak `v_c`, remove any time clamp. Follow the Phase-7 `TargetSelectorPanel` pattern.
- `Scripts/Hud/Hud.cs` — `ActiveTargetIndex` (warp destination source); read-only contract (Phase-6 D-53) preserved.

### Reference Source (user-provided, external inspiration)
- `TrapezoidMotionProfile` (Unity reference, pasted in discussion 2026-06-22) — jerk-limited S-curve trapezoid. **Adopt:** trapezoid + jerk-limited ramps + triangle/trapezoid awareness. **Reject/invert:** total-time-as-output and the `scale = distance / totalProfileDistance` position-rescale (we make `T_sel` the exact input instead). Not committed to the repo; summarized in DISCUSSION-LOG.md.

### Phase Roadmap & State
- `.planning/ROADMAP.md` — Phase 8 entry (seeds: P3 timing debt + WR-01/02/03/04).
- `.planning/STATE.md` — "Phase 08 carried inputs" block (the five WR seeds + open design Q, now resolved by D-05).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `FlightController._easedSpeed` / `CurrentSpeed` — `_WarpProcess` already writes warp speed here and `ApplyMotion` consumes `CurrentSpeed`. The new profile sets `CurrentSpeed = v(t)`; the plumbing is unchanged.
- `FlightController._speedEasing` lerp — the existing warp-exit ease-down (Phase-7 D-19) stays as the safety net beyond the profile's own ManualMaxSpeed terminal (D-07/D-09).
- `WarpState` enum + `_warpState` — the Manual/Confirming/Warping machine is reused; only the Warping per-frame body changes. Add an elapsed-warp-time accumulator field (D-10).
- `UniMath.Distance` / `UniMath.NormalizedDirection` — the only LCA-safe distance/direction source; `NormalizedDirection` also gets the WR-01 threshold fix.
- Phase-7 NaN/Inf guard + `T_sel ≥ 1s` clamp — keep both (D-06).

### Established Patterns
- **On-rails warp (Phase-7 D-18):** throttle/steering suspended while Warping; only look-around (Alt) active. Unchanged.
- **Read-only HUD contract (Phase-6 D-53):** `WarpConfirmationScreen` must not mutate `GameObjects`/`LocalPos`/`ChildIndices`; it only reads distance/target and calls `EngageWarp`.
- **Bounds-safe lookup:** `(uint)idx < (uint)count` pattern used in `_WarpProcess`/`EngageWarp` — extend it for the WR-03 valid-target gate (D-14).
- **`[Export]` tuning knobs:** add accel-fraction `f` (default 1/3); remove/neutralize `WarpMaxSpeed`.

### Integration Points
- `EngageWarp(travelTimeSec)` — replace the `_warpInternalTimeSec` calibration with: capture `d0` (via `UniMath.Distance`), compute `D = d0 − target.SOIMeters`, store `T_sel`, reset elapsed-warp-time to 0, validate target (WR-03). Solve `v_c`, accel/decel/cruise durations.
- `_WarpProcess(delta)` — replace `warpSpeed = min(dist/T_int, WarpMaxSpeed)` with `v(t)` evaluation from the profile (accumulate `t += delta`); keep the auto-orient slerp and the `dist < SOIMeters` disengage.
- `FlightController.cs:653` (`UpdateSpeedEnvelope` open-space branch) — WR-04 fix (D-15).
- `WarpConfirmationScreen` — D-12 speed-display change.

</code_context>

<specifics>
## Specific Ideas

- **Exact, reproducible timing is the hard requirement** (user, 2026-06-22): "I want reproducible and exactly timed motion, so configured time of flight is exact." This is why the profile is closed-form, time-parameterized (D-10), and the speed cap is dropped (D-05) rather than allowed to distort arrival time.
- **Reference feel:** the user's `TrapezoidMotionProfile` — jerk-limited S-curve ramps, accel→cruise→decel, trapezoid/triangle awareness — is the inspiration; the improvement is making `T_sel` the exact input and using a symmetric smoothstep ramp so timing is analytically exact.
- **Default accel fraction `f = 1/3`** (thirds: ⅓ accel, ⅓ cruise, ⅓ decel) — a clean, scale-invariant starting point; exposed for play-test tuning.

</specifics>

<deferred>
## Deferred Ideas

- **Travel-cost mechanic** (lower travel time = higher peak speed = higher cost) — still future; `T_sel` and the now-exposed `v_c` are the natural attachment points. Out of scope here.
- **Body collision avoidance during warp** — still its own future phase (decisions parked in Phase-7 context: obstacle types, single-arc detour, `RadiusMeters × SafetyClearance`).
- **Warp visual FX** (Phase 999.4) — starfield streaking / speed-line post-process; depends on this profile shipping.
- **Manual-flight speed-model re-tune after WR-04** — fixing the open-space sentinel (D-15) ~doubles open-space manual top speed; a broader re-tune of `SpeedPerMeter` / tier factors is noted but not scoped into Phase 8 beyond the bug fix itself.

None of the discussion drifted outside the phase domain.

</deferred>

---

*Phase: 8-Warp Motion Profile*
*Context gathered: 2026-06-22*
