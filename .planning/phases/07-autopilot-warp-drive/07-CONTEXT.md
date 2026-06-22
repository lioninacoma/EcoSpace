# Phase 7: Autopilot & Warp Drive - Context

**Gathered:** 2026-06-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 7 delivers two coupled capabilities on top of the Phase-6 targeting system:

1. **Look-around camera** — decouple camera rotation from ship heading in both normal flight and warp (Left Alt hold; camera eases back to ship heading on release). Normal flight: ship holds current heading while player looks around.

2. **Manual speed cap** — reduce `MaxSpeed` from the current intergalactic 2e20 m/s to 1,000 km/s (1e6 m/s). Manual throttle is always ≤ ManualMaxSpeed. The Phase-4 tier ceiling logic (D-40) becomes autopilot-only.

3. **Warp drive (autopilot)** — press J to open a warp confirmation screen; set travel time via in-game slider/numeric input; press Enter to engage, Esc or J to cancel. Ship auto-orients toward target, flies at warp speed (= remaining distance ÷ selected travel time), auto-disengages when entering the target's SOI.

**Out of scope:** Body collision avoidance (deferred — see Deferred section). Future cost mechanic tied to travel time. Warp visual FX (Phase 999.4 backlog).

</domain>

<decisions>
## Implementation Decisions

### Warp Activation & Target Flow
- **D-01:** Autopilot reuses the Phase-6 tree selector target (`Hud.ActiveTargetIndex`) as the warp destination. No separate warp-destination UI — the existing tree selector IS the warp picker.
- **D-02:** J key (`warp_engage` InputMap action) opens the warp confirmation screen. Requires an active target (`ActiveTargetIndex >= 0`); no-op if no target is set.
- **D-03:** On warp engage (Enter on confirmation screen), the ship auto-orients smoothly toward the target direction. Auto-orientation uses the direction from `UniMath.RelativeMetres(ship, target, objs)` normalized, lerped into `_shipBasis` each frame.
- **D-04:** Warp confirmation screen sets `FlightController.IsPanelOpen = true` while open — reuses the existing gate in `_Input`, `_UnhandledInput`, and `_Process` to suppress all flight input. No new flag needed.

### Warp Speed & Travel Time
- **D-05:** Travel time is **in-game configurable**, not an editor export. The warp confirmation screen presents a slider or numeric input (minutes) that the player sets before engaging. Default shown on screen: 2 minutes.
- **D-06:** Warp speed is computed each frame as `warpSpeed = UniMath.Distance(ship, target, objs) / (selectedTravelTime)`. This naturally produces a deceleration curve as the ship closes in — no separate ease-out logic needed; remaining distance → 0 means speed → 0.
- **D-07:** A `WarpMaxSpeed` editor export (default 2e20 m/s, matching current `_maxSpeed`) caps the computed warp speed. Prevents an absurdly high speed when the target is very close and travel time is short. This is a technical safety knob, not a game mechanic.
- **D-08:** Warp auto-disengages when `UniMath.Distance(ship, target, objs) < target.SOIMeters`. Speed then eases down to `ManualMaxSpeed` using the existing `_speedEasing` lerp mechanism.

### Manual Speed Cap
- **D-09:** `ManualMaxSpeed` = 1e6 m/s (1,000 km/s). This is an editor export (tuning knob). All manual throttle is clamped to `ManualMaxSpeed` regardless of tier or SOI context.
- **D-10:** The Phase-4 tier ceiling (D-40: `tierCeiling = parent.SOIMeters × TierSpeedFactor`) and proximity damp (D-42) continue to apply for the autopilot speed envelope, but manual flight ignores them — manual speed is simply `min(throttle01 × contextMax, ManualMaxSpeed)` where `contextMax` is still eased for smooth SOI transitions.

### Look-Around Camera
- **D-11:** Left Alt hold activates look-around in **both** normal flight and warp. A new `look_around` InputMap action bound to `Key.Alt`.
- **D-12:** While look-around is active: mouse motion drives `_cameraOffset` (a separate basis accumulated over look-around duration), NOT `_shipBasis`/`_cursor`. Ship holds its current heading; throttle still works; steering accumulation is suspended.
- **D-13:** On Left Alt release, `_cameraOffset` lerps back to identity over ~0.3 seconds — camera eases back to ship heading. During warp, there is no ship heading to return to (ship is always rotating toward target), so the camera eases back to face the warp direction.
- **D-14:** During warp, look-around is always available (Alt hold). The ship remains on autopilot rails regardless.

### Warp Confirmation Screen
- **D-15:** Screen shows: target name, estimated distance, travel time input (slider + numeric minutes display), computed warp speed (for player awareness), Enter to engage / Esc or J to cancel.
- **D-16:** The screen is a new Godot `Control` node (similar to `TargetSelectorPanel`) parented to the CanvasLayer in `Main.tscn`. Read-only consumer of world state (D-53 from Phase 6 preserved).
- **D-17:** The selected travel time (in seconds) is stored as a runtime field on the warp component — not persisted between sessions. Defaults to 120s (2 minutes) each session.

### In-Flight Warp Behavior
- **D-18:** During warp the ship is **on rails** — ship auto-orients to target, throttle input is ignored, steering cursor accumulation is suspended. The only active player input is look-around (Alt hold).
- **D-19:** On warp disengage (SOI arrival OR player cancels with Esc/J), speed eases down to `ManualMaxSpeed` via `_speedEasing` lerp. No hard stop.

### Claude's Discretion
- Warp confirmation screen layout and exact slider UX (position, width, step size) — Claude picks a retro-consistent style matching the Phase-6 `TargetSelectorPanel` aesthetic (phosphor green, monospace font).
- The `look_around` InputMap action should be added to `project.godot` alongside the existing `warp_engage` action.
- Warp speed display on the confirmation screen — show in m/s with scientific notation (consistent with HUD speed display conventions).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Flight Model (Phase 7 modifies this directly)
- `Scripts/Flight/FlightController.cs` — full speed envelope (D-40/D-42/D-43 tier ceiling, proximity damp, target ease-out, `IsPanelOpen` gate, `_shipBasis`/`_camera`). Phase 7 adds `ManualMaxSpeed` cap, `_cameraOffset` look-around, warp state machine.
- `.planning/phases/04-flight-model-v2/04-CONTEXT.md` — locked D-40/D-42/D-43/D-44/D-45 decisions that Phase 7 builds on.

### Targeting API (Phase 7 consumes, must not break)
- `Scripts/Hud/Hud.cs` — `ActiveTargetIndex`, `SetTargetIndex`, `GetTargetCandidates`, `IsPanelOpen` setter.
- `Scripts/Hud/TargetSelectorPanel.cs` — Phase-6 panel pattern to follow for the warp confirmation screen.
- `.planning/phases/06-targeting-system/06-CONTEXT.md` — D-50–D-57 decisions; D-53 read-only constraint must be preserved.

### Universe Math (warp navigation uses cross-frame distances)
- `Scripts/Math/UniMath.cs` — `Distance`, `RelativeMetres`, `RelativePosition` (LCA path). All warp distance computations MUST use UniMath, not raw `ToDouble3()`.
- `CLAUDE.md` §"Position Math (UniVec3 / UniMath)" — the canonical cross-frame math rule.

### Phase Roadmap
- `.planning/ROADMAP.md` — Phase 7 section (promoted from backlog 999.3); Phase 999.4 (warp VFX, depends on Phase 7 autopilot state).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `FlightController.IsPanelOpen` (bool) — already suppresses all flight input in `_Input`, `_UnhandledInput`, `_Process`. Warp confirmation screen sets this true while open; no new gate needed.
- `FlightController._speedEasing` + `Mathf.Lerp` pattern — reuse for warp-exit ease-down to `ManualMaxSpeed` and for camera look-around ease-back.
- `Hud.ActiveTargetIndex` — read-only, clamp-safe, always returns a valid game-object index or -1. The warp screen reads this to know what to fly to.
- `TargetSelectorPanel.cs` — established pattern for a `Control` that sets `IsPanelOpen`, handles WASD/arrow key input, and is wired into Main.tscn on a CanvasLayer. Follow the same structure for `WarpConfirmationScreen`.
- `UniMath.Distance(ship, target, objs)` — cross-frame distance via LCA path. This is the ONLY safe way to compute remaining warp distance; direct `UniVec3.Distance` or `ToDouble3()` will catastrophically cancel at intergalactic scale.

### Established Patterns
- **Read-only HUD/renderer contract (D-53):** `WarpConfirmationScreen` and any warp-related Hud additions must never mutate `GameObjects`, `LocalPos`, or `ChildIndices`. Selection flows only through `Hud.SetTargetIndex`.
- **IsPanelOpen gate:** Set true on open, false on close. Already implemented in `_Input` (early return), `_UnhandledInput` (early return), and `_Process` (early return). Warp screen and look-around mode both hook into this.
- **Observer-unit render conversion (Phase-5 lesson):** Any metres→render conversion elsewhere must use `metres / ship.LocalPos.Scale * factor`, never `metres * factor`. The warp confirmation screen displays raw metres (distance) — no render conversion needed there.
- **Camera basis:** `_camera.Basis = _shipBasis` is set every frame in `UpdateAttitude`. Look-around overrides this by writing a `_lookBasis` instead when Alt is held.

### Integration Points
- `FlightController._Process` → `UpdateAttitude` → `_camera.Basis = _shipBasis`: look-around replaces this assignment with `_camera.Basis = _lookBasis` when Alt is held.
- `FlightController.UpdateSpeedEnvelope`: add `ManualMaxSpeed` clamp after the existing `targetMax` computation; the clamp only applies to manual flight (not autopilot path).
- `Main.tscn` CanvasLayer: add `WarpConfirmationScreen` node (same level as `TargetSelectorPanel`).
- `project.godot` InputMap: add `warp_engage` (J key) and `look_around` (Left Alt) actions.

</code_context>

<specifics>
## Specific Ideas

- **Travel time default:** 2 minutes (120 s) shown pre-filled in the warp confirmation screen each session.
- **Warp screen aesthetic:** Retro phosphor-green style matching `TargetSelectorPanel` — monospace font, minimal chrome, shows target name + distance + time input + computed speed.
- **Look-around ease-back:** ~0.3 s lerp from `_lookBasis` back to `_shipBasis` (or warp heading) on Left Alt release.
- **Future cost mechanic hook (noted for next phase):** The `selectedTravelTime` field on the warp component is the natural attachment point. Lower travel time = higher speed = higher cost. Phase 7 stores and uses the value; a future phase reads it and computes cost.

</specifics>

<deferred>
## Deferred Ideas

### Body Collision Avoidance (deferred to its own phase)
User direction 2026-06-22: defer collision avoidance entirely. Originally scoped as part of Phase 7 autopilot, but parked for a dedicated phase. When that phase arrives, relevant decisions already captured:
- Obstacle types: `Type.Orb`, `Type.Star`, `Type.Asteroid` (not `Type.Galaxy` — diffuse, non-solid)
- Bypass shape: single-arc detour (compute perpendicular waypoint just outside obstacle radius, fly source → waypoint → destination)
- Obstacle radius: `body.RadiusMeters × SafetyClearance` (configurable multiplier, e.g. 1.2×)

### Travel Cost Mechanic (future phase)
Speed ↔ cost relationship: lower warp speed (longer travel time) = more cost-efficient. Phase 7 lays the groundwork (in-game configurable travel time). A future phase adds the cost computation and display on the warp confirmation screen.

### Warp Visual FX (Phase 999.4 backlog)
Starfield streaking, speed-line dithered post-process for warp transit. Read-only consumer of autopilot state. Depends on Phase 7 shipping the warp state machine first.

</deferred>

---

*Phase: 7-Autopilot & Warp Drive*
*Context gathered: 2026-06-22*
