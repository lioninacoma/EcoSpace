---
type: tech-debt
status: pending
priority: P4
created: 2026-06-17
area: hud
origin: phase-03 UAT play-test
tags: [tech-debt, hud, target, input, tab]
related: [hud-target-nearest-galaxy-space]
---

# Cannot cycle / set a target manually (Tab does nothing)

## What

The player cannot select or cycle the active target — pressing Tab (or anything) does
not change the `TGT` readout.

## Why (suspected)

The `cycle_target` action IS bound to Tab in `project.godot` (physical keycode
4194305 = KEY_TAB) and `Hud._Input` calls
`@event.IsActionPressed("cycle_target")` to advance `_targetIndex`. Candidate causes:
- **Tab is Godot's default `ui_focus_next`** — the GUI focus system may be consuming /
  competing with the Tab event, so the cycle either doesn't fire or fires inconsistently
  while the mouse is Captured.
- **Targetable list too small** — if `BuildTargetableList` only yields the parent +
  home star in Galaxy space (see [[hud-target-nearest-galaxy-space]]), cycling appears
  to "do nothing" because the readout barely changes.
- Event possibly marked handled elsewhere before `_Input` advances the index.

## What "done" looks like

- Tab (or a chosen key) reliably cycles the target through the full targetable set in
  every space, and the `TGT` readout + off-screen marker update accordingly.
- No interference from Godot's `ui_focus_next` (rebind the key, mark the event handled,
  or disable focus-next while flying).

## Notes

- Investigate together with [[hud-target-nearest-galaxy-space]] — likely the same
  target subsystem and possibly the same fix session.
- A reliable target is a prerequisite for the target-aware flight model
  ([[flight-speed-model-tier-and-target-aware]]).
- Requires in-game Godot verification.
