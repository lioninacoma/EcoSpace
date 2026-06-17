---
type: tech-debt
status: pending
priority: P1
created: 2026-06-17
area: flight
origin: phase-03 UAT play-test (260617-j6b checkpoint, rejected)
tags: [tech-debt, flight, speed-envelope, soi, hud-target, design]
related: [thrust-zero-at-galaxy-soi-exit, hud-target-nearest-galaxy-space]
backlog_ref: 999.1
---

# Flight speed model: make max speed tier- and target-aware

## What

The arcade speed envelope does not scale across the in-system ↔ intergalactic range.
There is a SINGLE global `MaxSpeed` (`FlightController._maxSpeed = 2e20` m/s, an
intergalactic value, D-35) for every scale. The only thing keeping in-system speed
usable was the proximity clamp `targetMax = clamp(nearest * SpeedPerMeter, MinSpeed,
MaxSpeed)` — and the 260617-j6b "direction-aware" patch made that clamp
receding-exempt, which removed the in-system ceiling entirely.

**Observed (play-test 2026-06-17):** reasonable thrust only right next to the start
planet. Flying toward the home star, speed ramps to ly/s and the ship flies straight
past the star — in-system travel is unusable. Intergalactic flight feels fine with the
patch, but the model is now broken for the case Phase 1 shipped.

## Why

- `MaxSpeed` is not context-scaled. Receding from the nearest body should not jump to
  an intergalactic ceiling while still inside a star system.
- The envelope is keyed only to nearest-SURFACE distance with no notion of the
  ship's current scale tier or its intended destination.

## Design direction (from user, to refine)

> "Reconsider the flight system, maybe with 999.1 in mind. Thrust handled by the
> current target. If no target is set, thrust bounded by the current space [tier]."

Candidate model:
- **Per-tier speed ceiling** — each `UniObject.Space` (Planet / Star / Galaxy /
  Universe) has its own max speed so in-system stays slow and intergalactic stays fast,
  with smooth easing across SOI transitions (keep D-07 lerps).
- **Target-aware modulation (optional)** — when a target is set, scale the ceiling /
  ease-out by distance-to-target so you decelerate onto the target instead of onto the
  nearest incidental body. Requires a reliable "current target" (see
  [[hud-target-nearest-galaxy-space]] and [[hud-cycle-target-not-working]]).
- **No separate FTL mode** — keep it a tuning/envelope model (D-35/D-36 constraint).

## Dependencies

- A working target system is a prerequisite for the target-aware variant. The in-SOI
  target HUD now works ([[hud-cycle-target-not-working]] Tab fix; [[hud-target-nearest-galaxy-space]]
  flicker fix) — but it is scoped to the current SOI (D-12).
- Supersedes the rejected [[thrust-zero-at-galaxy-soi-exit]] quick fix (commit f343cc3)
  — the galaxy-SOI-exit dead zone must be solved inside this model, not by a global
  recede-exemption.

## Folded-in scope (cross-space targeting — the D-12 override / 999.1 slice)

These belong with the target-aware flight model and are pulled from backlog 999.1
(minimal slice, NOT the full nav-HUD):

- **Cross-SOI nearest / targeting** — let the player target a body in another SOI (e.g.
  another star while inside the home star's SOI). Overrides D-12 (current-SOI-only). This
  is the deferred half of [[hud-target-nearest-galaxy-space]] symptom 2 and the "fly to
  another star" intent.
- **World-pinned target outline (circle)** — a ring drawn around the active target that
  holds a minimum on-screen radius so a distant target is always findable (user request
  2026-06-17). Pairs with the existing off-screen edge marker. Already described in
  ROADMAP backlog 999.1.
- These also unblock the "thrust handled by current target" model — you must be able to
  pick and hold a meaningful target before thrust can ease onto it.

## What "done" looks like

- In-system travel is usable: approach a star/planet and stop near it without
  overshooting; speeds feel proportional to the scale you're in.
- Intergalactic travel still reaches FTL-equivalent speed and eases onto the
  destination galaxy (closes the original thrust-zero-at-exit symptom too).
- Transitions across SOI boundaries are smooth (no speed pop), in BOTH directions.
- No separate FTL mode; pull only the minimal "current target" slice of backlog 999.1,
  not the full nav-HUD.

## Notes

- This is phase-sized (design + discussion), not a quick task. Recommend `/gsd-phase`
  insert + `/gsd-discuss-phase`.
- Related decisions: D-06/07/08 (distance→speed curve), D-35 (MaxSpeed tuning), D-36
  (no-overshoot guard).
- Requires in-game Godot verification (not catchable by build/tests).
