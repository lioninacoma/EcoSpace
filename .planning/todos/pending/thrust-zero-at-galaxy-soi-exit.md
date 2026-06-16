---
type: tech-debt
status: pending
created: 2026-06-16
area: flight
origin: phase-03 play-test (cross-galaxy travel)
tags: [tech-debt, flight, speed-envelope, soi]
---

# Thrust drops to zero when exiting a galaxy's SOI

## What

When the ship crosses **out** of a galaxy's SOI (at the galaxy edge), throttle/
thrust effectively goes to zero — the ship will not accelerate away as expected.

## Why (suspected)

The distance→speed curve in `Scripts/Flight/FlightController.cs` adjusts max
speed automatically based on proximity to the nearest body (the D-06/07/08
envelope, now fed by galaxy `RadiusMeters`). At the galaxy SOI boundary the ship
is "close" to the galaxy, so the envelope clamps the allowed speed down to near
zero — which is the intended deceleration-on-approach behaviour, but it also
fires on the way **out**, stranding the player at the edge.

## What "done" looks like

- Leaving a galaxy SOI lets the player throttle back up to intergalactic speeds
  smoothly — no dead zone at the boundary.
- Approaching a galaxy still decelerates smoothly (don't regress the ease-in that
  Plan 03-02 relies on).
- Behaviour is symmetric and direction-aware: the speed envelope should consider
  whether the ship is moving toward or away from the body (e.g. gate the clamp on
  closing velocity / radial direction), or use a hysteresis / outbound-exempt rule.

## Notes

- Related decisions: D-06/07/08 (distance→speed curve), D-35 (MaxSpeed tuning),
  D-36 (no overshoot guard). Plan 03-02 deliberately kept this a tuning/constraint
  change and did not add a separate FTL mode — keep that constraint.
- Reproduce: fly from the home system to DEST GALAXY, enter its SOI, then try to
  fly back out — observe thrust dying at the boundary.
- Godot runtime check required; not catchable by build/tests.
