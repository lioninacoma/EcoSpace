# Phase 7: Autopilot & Warp Drive - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-22
**Phase:** 07-autopilot-warp-drive
**Areas discussed:** Warp activation & target flow, Manual speed cap, Warp speed profile & arrival, In-flight autopilot behavior

---

## Warp Activation & Target Flow

| Option | Description | Selected |
|--------|-------------|----------|
| Reuse Phase-6 target | Tree selector IS the warp destination picker; no new UI | ✓ |
| Separate warp-destination panel | Distance-ranked second panel | |
| Auto-warp to current target | Hold full throttle → auto-engage | |

**User's choice:** Reuse Phase-6 ActiveTargetIndex — no separate panel needed.

| Option | Description | Selected |
|--------|-------------|----------|
| J (Jump) | Unbound in InputMap | ✓ |
| Enter / Return | Same as tree-selector confirm key | |
| You decide | Claude picks binding | |

**User's choice:** J key opens warp confirmation screen.

| Option | Description | Selected |
|--------|-------------|----------|
| Auto-orient toward target | Ship rotates to face target on engage | ✓ |
| Keep current heading | Player aims manually before engaging | |

**User's choice:** Auto-orient toward target on warp engage.

**Interruption — user added constraint:** Autopilot should navigate around solid bodies (planets, stars, black holes) to avoid collision; flight path curves around obstacles.

| Option | Description | Selected |
|--------|-------------|----------|
| Physical body radius (RadiusMeters) | Only solid surface is the obstacle | ✓ |
| SOI radius (SOIMeters) | Entire sphere of influence is keep-out | |
| Configurable safety multiplier | Obstacle = RadiusMeters × factor | |

| Option | Description | Selected |
|--------|-------------|----------|
| Single-arc detour | Perpendicular waypoint + smooth arc | ✓ |
| Multi-body A* pathfinding | Graph search for collision-free route | |
| Iterative deflection | Per-frame heading nudge | |

**Obstacle types selected:** Orb (planet), Star, Asteroid — NOT Galaxy (diffuse).
**Clearance:** body.RadiusMeters × SafetyClearance (configurable, ~1.2×).

**User correction:** Defer collision avoidance entirely to a future phase.

**Notes:** Collision avoidance captured in Deferred section of CONTEXT.md for the dedicated future phase.

---

## Manual Speed Cap

**User interruption before formal question:** Travel time should be configurable in-game (not an editor export knob). There will be a future game mechanic where cost scales with speed (lower speed = more cost-efficient). For now, just need to set travel time. 2-minute intergalactic transit should remain achievable as a configuration.

| Option | Description | Selected |
|--------|-------------|----------|
| ~1,000 km/s (1e6 m/s) | Fast in-system, punchy feel | ✓ |
| ~30 km/s (3e4 m/s) | Earth orbital speed — very precise | |
| ~10,000 km/s (1e7 m/s) | ~3% c — very fast in-system | |
| You decide + editor knob | Claude sets default, exposes export | |

**User's choice:** 1,000 km/s (1e6 m/s) as ManualMaxSpeed.

---

## Warp Speed Profile & Arrival

**User clarification:** Travel time is configurable in-game via a slider/numeric input on the warp confirmation screen — NOT an editor inspector knob.

| Option | Description | Selected |
|--------|-------------|----------|
| Slider / numeric input | Continuous or typed minutes value on warp screen | ✓ |
| Preset tiers (FAST/NORMAL/SLOW) | Named options with arrow key selection | |
| You decide | Claude picks format | |

**User's choice:** Slider/numeric input on the warp confirmation screen.

| Option | Description | Selected |
|--------|-------------|----------|
| Enter to engage, Esc or J to cancel | Consistent with Phase-6 panel (Esc closes) | ✓ |
| J to engage, Esc to cancel | Double-tap J: open then confirm | |

**User's choice:** Enter to engage, Esc or J to cancel.

| Option | Description | Selected |
|--------|-------------|----------|
| Enter target's SOI — auto-disengage | Warp cuts when ship crosses into destination SOI | ✓ |
| J to disengage manually | Warp runs until player presses J | |

**User's choice:** Auto-disengage when entering target's SOI radius.

---

## In-Flight Autopilot Behavior

**User addition:** Player can only look around during warp (ship on rails). The same look-around feature should also exist in normal flight mode (camera decoupled from ship heading, ship holds current heading).

| Option | Description | Selected |
|--------|-------------|----------|
| Hold a key (Alt / middle mouse) | Momentary look, snap back on release | ✓ |
| Toggle (L key) | Press once to enter/exit look-around | |
| Right mouse button hold | Hold RMB to look around | |

**User's choice:** Hold Left Alt to look around.

| Option | Description | Selected |
|--------|-------------|----------|
| Hold current heading — no steering | Ship flies straight; consistent with warp | ✓ |
| Steering still works via keyboard | Mouse = camera, keyboard = steering | |
| Full stop — ship pauses | Throttle freezes during look | |

**User's choice:** Ship holds current heading while looking around in normal flight.

| Option | Description | Selected |
|--------|-------------|----------|
| Left Alt (hold) | Common flight-sim look-around binding | ✓ |
| Middle mouse button | Scroll-wheel button | |
| You decide | Claude picks binding | |

**User's choice:** Left Alt.

| Option | Description | Selected |
|--------|-------------|----------|
| Ease back smoothly (~0.3s) | Camera lerps to ship heading on release | ✓ |
| Snap instantly | Immediate return to ship heading | |

**User's choice:** Ease back smoothly over ~0.3s.

| Option | Description | Selected |
|--------|-------------|----------|
| Ease down to ManualMaxSpeed | Speed lerps using _speedEasing on warp disengage | ✓ |
| Hard stop to zero | Ship stops instantly | |

**User's choice:** Ease down to ManualMaxSpeed on warp exit.

---

## Claude's Discretion

- Warp confirmation screen layout and slider UX (position, step size, visual style)
- `WarpMaxSpeed` editor export (technical safety cap, default 2e20 m/s) — prevents absurd computed speeds for very nearby targets
- `look_around` and `warp_engage` InputMap action names in project.godot

## Deferred Ideas

- **Body collision avoidance** — user deferred to its own future phase. Full design captured in CONTEXT.md Deferred section (obstacle types: Orb/Star/Asteroid; single-arc detour; RadiusMeters × SafetyClearance).
- **Travel cost mechanic** — future phase; lower warp speed = more cost-efficient. `selectedTravelTime` field is the attachment point.
- **Warp visual FX** — Phase 999.4 backlog; depends on Phase 7 autopilot state machine.
