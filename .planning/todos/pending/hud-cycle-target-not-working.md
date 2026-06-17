---
type: tech-debt
status: resolved
priority: P4
created: 2026-06-17
resolved: 2026-06-17
area: hud
origin: phase-03 UAT play-test
tags: [tech-debt, hud, target, input, tab]
related: [hud-target-nearest-galaxy-space]
debug-session: .planning/debug/hud-target-galaxy-space.md
---

> **RESOLVED 2026-06-17** (debug session `hud-target-galaxy-space`, user-verified in-game).
> Root cause was a **keybinding off-by-one**: `cycle_target` in project.godot was bound to
> `physical_keycode 4194305 = KEY_ESCAPE`, not Tab (`KEY_TAB = 4194306`). Pressing Tab matched
> no binding, so nothing happened. (An earlier guess — ui_focus_next stealing Tab, "fix" by
> moving to `_UnhandledInput` — was wrong and backwards: `_Input` runs BEFORE the GUI focus
> system, so it is the correct handler for Tab.)
> **Fix:** rebind `cycle_target` 4194305 → 4194306 (Tab) in project.godot; keep the handler in
> `Hud._Input` and call `GetViewport().SetInputAsHandled()` after cycling so focus-nav does not
> double-fire. Verified: Tab cycles STAR → PLANET A → PLANET B → STAR in Star space. Build 0/0,
> tests 30/30.

# Cannot cycle / set a target manually (Tab does nothing)

## What

The player cannot select or cycle the active target — pressing Tab (or anything) does
not change the `TGT` readout.

## Why

`cycle_target` was bound to **Escape (4194305)** instead of **Tab (4194306)** — an
off-by-one in the keycode. The binding was simply wrong; the handler was fine.

## Resolution

- project.godot: `cycle_target` physical_keycode 4194305 → 4194306 (Tab).
- Hud.cs: handler stays in `_Input` (correct — runs before `ui_focus_next`); added
  `GetViewport().SetInputAsHandled()` so the focus system does not also act on Tab.

## Notes

- Cycling is scoped to the current SOI parent + its children (D-12). Targeting bodies in
  OTHER SOIs (e.g. other stars from inside the home star's SOI) is the cross-space-targeting
  feature — see [[flight-speed-model-tier-and-target-aware]] (D-12 override / backlog 999.1).
