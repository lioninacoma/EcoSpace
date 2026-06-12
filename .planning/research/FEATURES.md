# Feature Research

**Domain:** Retro first-person space sim (Wing Commander / Elite-style) — travel and rendering layer
**Researched:** 2026-06-12
**Confidence:** HIGH (genre conventions well-established; retro references have 30+ years of community documentation)

---

## Scope Boundary

This document covers the v1 travel-and-rendering milestone (flight, speed, skybox, HUD, celestial body presentation) and defers long-term features (trading, combat, procedural generation) explicitly. Every table entry is tagged **[V1]** or **[DEFERRED]**.

---

## Feature Landscape

### Table Stakes (Players Expect These)

Features that every retro space sim player assumes exist. Missing any of these breaks the "it feels like a space sim" impression.

| Feature | Why Expected | Complexity | v1? | Notes |
|---------|--------------|------------|-----|-------|
| Forward throttle with visible speed readout | Every space sim from Elite (1984) onward shows current speed; Privateer used dual SET/KPS gauges | LOW | [V1] | Show current speed in km/s or game units; dual display (set vs actual) preferred |
| Pitch and yaw on primary axes (mouse/stick) | Wing Commander, Freelancer, and every arcade space sim uses pitch/yaw as primary axes, not pitch/roll | LOW | [V1] | Roll should be a secondary axis; pitch/yaw on mouse X/Y is genre standard |
| Boost / afterburner with visible feedback | Privateer, Wing Commander, Elite Dangerous all include a boost mode with audio and visual feedback | LOW | [V1] | Short-duration burst, not sustained; audio cue + screen flash or engine glow |
| Crosshair / gun reticle on screen center | Every first-person cockpit sim from WC1 onward places a targeting reticle at screen center | LOW | [V1] | Even without combat, a fixed reticle orients the player |
| Speed displayed as a number | Universal convention; Privateer shows KPS, Elite shows m/s or Mm/s depending on mode | LOW | [V1] | Must change unit/display scaling with context-speed changes or player is confused |
| Target selection (cycle through nearby objects) | Freelancer, Wing Commander, Elite Dangerous all let you tab/cycle through targets | MEDIUM | [V1] | Minimal: target name + distance; combat lock is deferred |
| Target name and distance shown on HUD | Every space sim since WC1 shows what you are looking at and how far away it is | LOW | [V1] | Even a single text label satisfies this |
| Stars visible as bright point sources / spheres | Elite (1984), Elite Dangerous, No Man's Sky — stars are always the dominant background element | LOW | [V1] | EcoSpace renders them as light-emitting spheres; matches or exceeds convention |
| Planets visible as distinct spherical bodies | Every cockpit space sim shows planets as large visible orbs on approach | MEDIUM | [V1] | Dithered sphere mesh is EcoSpace's approach; must be visible from distance |
| Dynamic starfield / skybox that does not drift with player | Parallax-stable starfield is universal in space sims; a drifting sky breaks immersion instantly | MEDIUM | [V1] | Already designed as a dynamic spherical skybox; must not move with camera rotation |
| Sense of scale — distant bodies grow as you approach | Wing Commander, No Man's Sky, Elite Dangerous all convey scale by size change over approach | LOW | [V1] | Sphere scaling with distance already implied by the rendering architecture |
| Flight that feels "floaty" but responsive | Wing Commander arcade model: no Newtonian drift, damped roll, responsive pitch/yaw | MEDIUM | [V1] | 6DOF Newtonian is explicitly out of scope; must feel like flying a plane in space |
| Throttle-down / stop (or near-stop) control | Privateer: Backspace to halt; Wing Commander: zero throttle key; players expect to be able to stop | LOW | [V1] | Instant or near-instant deceleration in arcade model |
| Some indicator of current location / scale context | Pioneer Space Sim, Elite Dangerous: flight mode indicator, current system/body name visible | LOW | [V1] | A text label showing "Star System: Sol" or current space level satisfies this |

### Differentiators (What Makes EcoSpace Distinct)

Features unique to EcoSpace's architecture. These are what make the v1 travel experience genuinely novel rather than a retro clone.

| Feature | Value Proposition | Complexity | v1? | Notes |
|---------|-------------------|------------|-----|-------|
| Context-auto-scaling speed — no mode switching | Every other space sim requires explicit mode switches (Supercruise in ED, Pulse Engine in NMS, IDS throttle scale in Evochron). EcoSpace does it automatically via SOI-aware speed scaling. This is the defining UX differentiator. | HIGH | [V1] | Speed ceiling grows continuously as player moves away from bodies; shrinks on approach. Requires mapping UniVec3 distance-to-parent-body into a speed multiplier. Dependency: SOI transitions must be smooth. |
| Seamless SOI transitions with no loading screen | Elite Dangerous uses frame shift drive transitions with loading breaks; NMS has fade-cut loading. EcoSpace's SOI reparenting is frame-continuous. | HIGH | [V1] | Already implemented in GameWorld; the game layer must not introduce any break (no fade, no warp effect). |
| 1:1-scale universe traversable without abstraction | No other playable space sim is truly 1:1 scale without time compression or warp abstractions. SpaceEngine offers this as an exploration tool, not a game. | HIGH | [V1] | Depends on context-auto-scaling speed and SOI precision to make it feel traversable. |
| Dithered 8-bit indexed-color rendering of celestial bodies | 8-bit palette + ordered dithering on sphere meshes is aesthetically distinctive. Elite Dangerous and NMS target photorealism; WC used SVGA without dithering. EcoSpace occupies an unoccupied retro-aesthetic space. | MEDIUM | [V1] | Dithering shader already implemented. Must look intentional, not broken — needs a coherent palette choice. |
| Dynamic skybox that reflects actual scale context | When crossing from inside a star system to intergalactic space, the skybox updates to represent the correct frame of reference. This is novel — most sims use a static skybox. | MEDIUM | [V1] | Directly tied to SOI transitions. Scale transitions must trigger skybox re-projection. |
| Stars rendered as geometry (light-emitting spheres), not sprites | Most retro sims use point sprites or textured quads for stars. Rendering the nearest star as a glowing mesh creates a qualitatively different feeling of physical presence. | LOW | [V1] | Already in the design. Performance concern only arises if many stars are in the scene simultaneously. |

### Anti-Features (Deliberately Excluded from v1)

These are features players might expect or request that are explicitly out of scope for the travel-and-rendering milestone. Document the reasoning so scope doesn't creep.

| Feature | Why Requested | Why Excluded from v1 | What Instead |
|---------|---------------|----------------------|--------------|
| Full cockpit art / instrument panel | Wing Commander's signature look; players associate space sim with a cockpit bezel | Cockpit art is expensive to produce and blocks proving the flight + rendering loop; deferred per PROJECT.md | Minimal HUD overlay on transparent background; cockpit art added in a later phase |
| 6DOF Newtonian flight (drift, momentum) | Elite Dangerous Flight Assist Off, Evochron, and hardcore sim fans expect realistic momentum | Kills accessibility for casual play; complicates the "feel good" mandate for v1; explicitly rejected in PROJECT.md | Arcade model with light damping; can feel weighty without being Newtonian |
| Atmospheric flight / landing on planets | NMS, Elite Dangerous offer surface landing; players expect to land if they can approach a planet | Planet surface rendering requires additional LOD system and terrain mesh; out of scope for v1 | Planets are scenery / navigation targets for v1; approach and circle, not land |
| Combat / weapon systems | Wing Commander is combat-centric; players expect guns | Combat depends on a solid flight model first; must not front-load systems before the core feels right | Deferred; add after v1 flight is validated |
| Trading / economy UI | Elite/Privateer players expect a market screen | Economy requires persistent world state and balance work; unrelated to travel and rendering | Deferred long-term |
| Procedural system generation | Players expect infinite universe in a "real" space sim | Procedural generation is complex and blocks validating the coordinate/rendering mechanics cheaply | Hand-authored test data in v1; procedural added as a later milestone |
| Galaxy map / jump plotting UI | Elite Dangerous, NMS: players expect a map to plan routes | Map UI implies multiple systems and route planning; v1 proves mechanics in a single authored universe | Navigation in v1 is purely first-person; a galaxy map is a later feature |
| Voice acting / comms | Wing Commander's wingman chatter is iconic | Content; not a mechanic; irrelevant to validating flight and rendering | Not planned; could be added as polish |
| Multiplayer | Elite Dangerous is multiplayer-first | Massively increases architectural complexity; no current design requirement | Explicitly not in scope |
| Time compression | Frontier: Elite II, KSP — used to skip travel time | Defeats the purpose of context-auto-scaling speed as the primary travel mechanic | The auto-scaling speed is EcoSpace's answer to this |
| Shields / hull health bar | Wing Commander, Elite: players expect ship status gauges | No damage model exists yet; v1 doesn't need ship health | Deferred to when combat is added |

---

## Feature Dependencies

```
[Context-auto-scaling speed]
    └──requires──> [SOI transition system] (GameWorld — ALREADY EXISTS)
    └──requires──> [Distance-to-parent-body query API] (GameWorld.TranslatePos — extend)
    └──requires──> [Speed multiplier curve] (design parameter)

[Seamless SOI transitions in-game]
    └──requires──> [SOI transition system] (ALREADY EXISTS)
    └──requires──> [Floating-origin player anchor] (new — player held at world origin)
    └──enables──> [Dynamic skybox update on scale transition]

[Dynamic skybox]
    └──requires──> [SOI transition events] (hook into TrySpaceTransition callback)
    └──requires──> [Skybox sphere mesh + shader]
    └──enhances──> [Sense of scale / context]

[Dithered planet rendering]
    └──requires──> [Sphere mesh in scene]
    └──requires──> [Dithering post-process shader] (ALREADY EXISTS — UniRenderer)
    └──requires──> [Distance-based LOD or scale for sphere] (new — keep sphere visible at range)

[Target selection UI]
    └──requires──> [List of UniObjects in current parent space] (GameWorld — query needed)
    └──requires──> [Cycle-target input binding]
    └──enhances──> [Scale context label on HUD]

[Speed readout on HUD]
    └──requires──> [Player velocity vector from flight controller]
    └──enhances──> [Context-auto-scaling speed] (player must see the speed change)

[Minimal HUD]
    └──requires──> [Player velocity] (for speed display)
    └──requires──> [Current space / scale] (for context label)
    └──requires──> [Target system] (for target name + distance)
    └──depends on: [Godot CanvasLayer] (ALREADY EXISTS in Main.tscn)

[Flight controller (arcade model)]
    └──requires──> [Floating-origin player anchor]
    └──requires──> [GameWorld.TranslatePos] (ALREADY EXISTS)
    └──enables──> [All HUD elements that read from player state]
```

### Dependency Notes

- **Context-auto-scaling speed requires SOI distance query:** GameWorld already tracks parent/child SOI radii; a query like `DistanceToParentBodyCenter()` needs to be surfaced as an API for the flight controller to read.
- **Floating-origin player anchor is the keystone:** Without it, all rendering breaks at scale. It must be implemented before anything else in the game layer. The player ship remains at Godot's world origin; `GameWorld.TranslatePos` moves the world around the player.
- **Dynamic skybox depends on SOI transition events:** Currently `TrySpaceTransition` prints debug logs only. The flight layer needs to hook into transition callbacks to trigger skybox updates.
- **Dithered planet rendering enhances, not requires, the dithering shader:** Planets would be visible without dithering; the dithering is the aesthetic differentiator and the shader is already wired up.
- **Target selection is independent of combat:** It reads from GameWorld's object list and is entirely UI-side; no targeting lock or damage model needed.

---

## MVP Definition

### Launch With (v1 — Travel and Rendering)

The v1 milestone proves that a player can fly through a 1:1-scale universe with retro aesthetics. Everything below is required for the concept to land.

- [ ] **Floating-origin player anchor** — Without this, all rendering breaks at scale. First thing to implement.
- [ ] **Arcade flight controller** — Pitch/yaw on primary axes, throttle up/down, boost, stop key. Wing Commander feel.
- [ ] **Context-auto-scaling speed** — Single continuous mechanic replacing manual mode switches. The core differentiator.
- [ ] **Dithered planet sphere rendering** — Sphere mesh visible at distance, post-processed through existing dithering shader.
- [ ] **Light-emitting star sphere** — Nearest star rendered as geometry, not a sprite; provides scene lighting reference.
- [ ] **Dynamic skybox** — Spherical background updated on SOI transitions; does not drift with camera rotation.
- [ ] **Minimal HUD** — Speed (number + unit), current space context label, target name + distance (cycle-able).
- [ ] **In-system flight test** — Fly around a single star system, approach planets, observe scale and rendering. First sequenced goal.
- [ ] **Cross-scale travel test** — Execute SOI transitions from planet orbit to interstellar and observe skybox updates. Second sequenced goal.

### Add After Validation (v1.x)

Features to add once v1 flight and rendering is solid, before moving to long-term goals.

- [ ] **CRT scanline post-process** — `crt.gdshader` already exists but is unused; wire it up after the core aesthetic is confirmed
- [ ] **Sound design** — Engine hum, boost SFX, SOI transition audio cue; none of these block v1 but they complete the feel
- [ ] **Autopilot assist** — Point at a target and hold heading; reduces tedium on long approaches; add once target system is solid
- [ ] **Multiple celestial bodies in test scene** — v1 starts with a minimal authored scene; add moons, gas giants, etc. once rendering scales

### Future Consideration (v2+)

Defer until the travel-and-rendering foundation is validated.

- [ ] **Galaxy map / route planner** — Requires multiple authored or procedural systems to be useful
- [ ] **Procedural universe generation** — Validate hand-authored mechanics first
- [ ] **Combat / weapons** — Requires flight model to feel solid; adds a full second design problem
- [ ] **Trading / economy** — Requires persistent world state and balance work
- [ ] **Full cockpit art** — Expensive; blocked until visual style is locked
- [ ] **Atmospheric entry / landing** — Additional LOD and terrain systems required
- [ ] **Multiplayer** — Major architectural scope

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Floating-origin player anchor | HIGH | MEDIUM | P1 |
| Arcade flight controller (pitch/yaw/throttle) | HIGH | MEDIUM | P1 |
| Context-auto-scaling speed | HIGH | HIGH | P1 |
| Speed readout on HUD | HIGH | LOW | P1 |
| Dithered planet sphere rendering | HIGH | MEDIUM | P1 |
| Light-emitting star sphere | HIGH | LOW | P1 |
| Dynamic skybox | HIGH | MEDIUM | P1 |
| Minimal HUD (speed + context + target) | HIGH | LOW | P1 |
| Boost / afterburner | MEDIUM | LOW | P1 |
| Target cycle (name + distance) | MEDIUM | LOW | P1 |
| CRT scanline shader (wire up existing) | MEDIUM | LOW | P2 |
| Sound design (engine hum, boost) | MEDIUM | MEDIUM | P2 |
| Autopilot assist (hold heading) | MEDIUM | MEDIUM | P2 |
| Multiple authored bodies in test scene | MEDIUM | LOW | P2 |
| Galaxy map | LOW | HIGH | P3 |
| Combat / weapons | LOW | HIGH | P3 |
| Trading / economy | LOW | HIGH | P3 |
| Full cockpit art | MEDIUM | HIGH | P3 |

**Priority key:**
- P1: Must have for v1 milestone to land
- P2: Add in v1.x after core is validated
- P3: Deferred to v2+ / long-term

---

## Competitor Feature Analysis

| Feature | Wing Commander (1990-97) | Elite / Frontier (1984-1993) | Elite Dangerous (2014) | Evochron Mercenary (2011) | EcoSpace Approach |
|---------|--------------------------|------------------------------|------------------------|---------------------------|-------------------|
| **Flight model** | Arcade: pitch/yaw, damped, capped speed | Arcade: pitch/roll primary; Newtonian optional in Frontier | 6DOF Newtonian with Flight Assist damping | Full Newtonian + IDS damping | Arcade (Wing Commander style), no 6DOF |
| **Speed in large space** | Single speed range (space is small arenas) | Time compression to skip travel | Supercruise mode (explicit activation, logarithmic speed) | IDS throttle scale multiplier (manual X1-X5) | Context-auto-scaling (automatic, continuous) |
| **Planet rendering** | Flat sprite or polygon backdrop | Wireframe / flat-shaded polygon | Photorealistic sphere with PBR | Procedural sphere with terrain | Dithered 8-bit indexed-color sphere mesh |
| **Skybox** | Static prerendered image | Static starfield bitmap | Static cubemap + nebula textures | Static starfield with dynamic nebulae | Dynamic: updates per SOI scale transition |
| **HUD elements** | SET/KPS speed, radar (2.5D), target box, shield/armor bars, afterburner status | Speed, heading, altitude, target, fuel | 6-panel cockpit: target, radar, nav, systems, fuel; full HUD | Speed, fuel, IDS scale, radar, target lock | Minimal: speed, context label, target name/distance |
| **Target system** | Tab-cycle ships/nav; red/blue color coding | Tab-cycle; autopilot to target | Full target panel: subsystems, shields | Full target lock with distance/speed | Minimal: cycle, name, distance |
| **Autopilot** | Jump to nav point (explicit); enters warp | Autopilot to bodies (explicit); docking autopilot | Supercruise Assist (optional module) | Nav autopilot with waypoints | Not in v1; heading-hold assist in v1.x |
| **Scale transitions** | Not applicable (small arena maps) | System exit via witchspace jump (explicit) | Frame Shift Drive jump with loading break | IDS scale change (manual) | Automatic SOI reparenting, no breaks |

---

## Genre Conventions Reference

### Flight Control Conventions (from WC, Elite, and descendants)

**Axis convention:** Pitch/yaw on primary axes (mouse X = yaw, mouse Y = pitch) is the genre standard since X-Wing/TIE Fighter and Wing Commander. Elite 1/2 used pitch/roll instead — this is the minority and considered dated by post-1993 convention. Use pitch/yaw.

**Roll:** Secondary axis, keyboard or stick twist. Not the primary turn. Players expect it to exist but not to be needed for basic flight.

**Throttle:** Persistent velocity model (set-speed, not hold-to-accelerate). Privateer's SET gauge (cruise control target) + KPS gauge (actual) is the clearest template. Players set a cruise speed with +/-, actual speed catches up. Alternative: direct analog throttle axis.

**Boost/Afterburner:** Short-duration, fuel-limited, large speed multiplier. Press-and-hold (Privateer: Tab). Audio spike + visual indicator. Universally expected even in stripped-down sims.

**Stop key:** Instant or near-instant deceleration (Privateer: Backspace). Players will be frustrated if they cannot stop cleanly. Required even in arcade model.

### Speed/Scale UX Conventions

**Elite Dangerous pattern (manual):** Player explicitly engages Supercruise via Frame Shift Drive. Speed is logarithmic, gravity wells create approach drag. Players must throttle back to avoid overshooting. Complex but understood by experienced players.

**Evochron pattern (manual):** Player manually sets IDS multiplier (X1 to X5). Speed range multiplied per setting. Simple but still a mode the player manages.

**EcoSpace pattern (automatic):** Speed ceiling scales continuously with distance to nearest body. No player action required. Closest analogy is No Man's Sky's Pulse Engine (automatic slow-down near hazards) but more granular. This is the differentiating mechanic — no prior space sim does exactly this.

**Unit display:** At planetary scale, show m/s or km/s. At interstellar scale, show AU/hr or c (fraction of light speed). Unit must change with scale or the number becomes meaningless.

### HUD Conventions (Minimum Viable Set)

Based on cross-reference across WC1, Elite, Pioneer Space Sim, and Elite Dangerous:

1. **Speed number** (current velocity, with unit) — universal, non-negotiable
2. **Crosshair / reticle** — screen-center orientation anchor — universal
3. **Target label** (name + distance) — universal from WC1 onward
4. **Context/location label** (current system, body name, or space level) — expected in any sim with scale
5. **Radar or proximity indicator** — near-universal; can be minimal (dot plot) rather than full 2.5D scope

Items 6+ (shields, armor, fuel, weapon heat, hull) belong to combat / ship-damage systems and are deferred for v1.

### Celestial Body Presentation Conventions

**Planets:** Rendered as visible spheres even at great distance. Size grows on approach (sense of scale). Detail increases on close approach. Dithered flat-shading is a valid aesthetic choice — early Elite and WC used flat polygons. The EcoSpace dithered sphere occupies a well-precedented retro aesthetic space.

**Stars:** Rendered as bright, distinct, oversized point sources or small spheres relative to their distance. Never rendered true-scale (they'd be invisible at typical in-game distances). Wings Commander used a bright blob; Elite Dangerous uses lens flare. EcoSpace's light-emitting sphere is a stronger presence than any sprite-based approach.

**Skybox:** Static in virtually all space sims. EcoSpace's dynamic skybox (updated on SOI transitions) is a genuine differentiator. The key convention to match: the skybox must not rotate or drift when the camera rotates — it must feel infinitely far away.

**Scale perception cues:** Players perceive scale through (1) angular size change of bodies on approach, (2) HUD distance readout, (3) parallax between nearby and distant objects, (4) the speed at which the skybox does NOT move. All four should be present in v1.

---

## Sources

- Wing Commander / Privateer manual (1993): dual SET/KPS speed gauge, cockpit layout, radar conventions
  - [Wing Commander CIC: Game Control Schemes](https://www.wcnews.com/controls.shtml)
  - [Privateer Manual (PDF)](https://download.wcnews.com/files/manuals/Wing%20Commander%20-%20Privateer%20-%20Manual.pdf)
- [Elite Dangerous Wiki: Flight Assist](https://elite-dangerous.fandom.com/wiki/Flight_Assist) — flight-assist damping model
- [Elite Dangerous Wiki: Supercruise](https://elite-dangerous.fandom.com/wiki/Supercruise) — speed/gravity scaling, logarithmic approach
- [Evochron Mercenary Instructions](https://starwraith.com/evochronmercenary/instructions/readme.htm) — IDS throttle scale X1-X5 manual mode
- [Pioneer Space Sim: Flight UI](https://wiki.pioneerspacesim.net/wiki/Flight_UI) — comprehensive HUD element catalog
- [Frontier: Elite II — Wikipedia](https://en.wikipedia.org/wiki/Frontier:_Elite_II) — autopilot, time compression, witchspace
- [No Man's Sky: Travel](https://nomanssky.fandom.com/wiki/Travel) — Pulse Engine speed approach, seamless planet transition design
- [House of the Dying Sun: Steam](https://store.steampowered.com/app/283160/House_of_the_Dying_Sun/) — minimalist HUD, no lead indicator, audio-based feedback
- [GameDev.net: Floating Origin Issues](https://gamedev.net/forums/topic/711954-issues-with-floating-origin/) — floating origin pattern and camera anchoring
- Star Citizen: [Flight Model and Input Controls](https://robertsspaceindustries.com/comm-link/transmission/13951-flight-model-and-input-controls) — pitch/yaw vs pitch/roll axis convention discussion

---

*Feature research for: EcoSpace — retro first-person space sim, travel and rendering layer*
*Researched: 2026-06-12*
