# Requirements: EcoSpace

**Defined:** 2026-06-12
**Core Value:** The player can fly seamlessly through a massive 1:1-scale universe — from a planet's surroundings out to interstellar and intergalactic distances — with rendering and flight that stay correct and feel good across every scale.

## v1 Requirements

Requirements for the initial playable travel-and-rendering milestone. Each maps to roadmap phases. Built on the existing universe engine (SOI transitions, `UniVec3` precision, dithering shader) — see `.planning/codebase/`.

### Stability

- [ ] **STAB-01**: SOI transition handling is iterative and null-safe — the ship can cross multiple SOI boundaries in a single frame without crashing or corrupting the object hierarchy

### Flight & Controls

- [ ] **FLT-01**: Player can pitch and yaw the ship with the mouse (primary axes) and roll (secondary), with arcade-style auto-stabilization
- [ ] **FLT-02**: Player can control forward throttle and slow or stop the ship
- [ ] **FLT-03**: Ship speed auto-scales to its SOI surroundings — crawling near bodies and accelerating enormously in empty space — with no manual mode switching

### World

- [ ] **WORLD-01**: A hand-authored multi-scale test universe (a star system with planets, plus multiple galaxies) exists to fly through and validate transitions

### Rendering

- [ ] **RND-01**: Player is held at the coordinate origin while the world is translated around them (floating origin); object `UniVec3` positions are synced to Godot `Node3D` transforms each frame without precision jitter
- [ ] **RND-02**: Bodies belonging to the current render tier are rendered dynamically as sphere-mesh geometry; bodies one tier further out are deferred to the skybox (RND-05). Concretely: **inside a star system** (both Star space and Planet space, not just the immediate parent) the system's planet(s) *and* star(s)/sun(s) — including every sun of a multi-star system — are meshes; **in Galaxy space** the stars/suns of the current galaxy are meshes. Bodies beyond the current tier (other systems' stars while in-system, other galaxies while in-galaxy) are not rendered as meshes
- [ ] **RND-03**: Planets are rendered as sphere meshes with dithering and an 8-bit color palette
- [ ] **RND-04**: The current system's star(s)/sun(s) — including each sun in a multi-star system — are rendered as bright, light-emitting sphere meshes (no cast shadows), each casting light that affects the system's planets
- [ ] **RND-05**: A dynamic spherical skybox represents the bodies just *outside* the current render tier as distant light points — the stars of other star systems (and other galaxies) while inside a system, and *only* other galaxies while in Galaxy space. It updates when the player crosses a scale boundary and never drifts with camera rotation
- [ ] **RND-06**: Distances are kept 1:1 in both calculation and rendering — the universe simulation (SOI, `UniVec3`, distances, speed envelopes) runs at true 1:1 meters, and rendering reproduces those distances in a uniformly scaled **unit-space** (observer-scale unit basis × per-space render factor) so 1:1 proportions and perspective are preserved while the camera far plane stays bounded (≤ 1e6 render units). Body radii are likewise rendered at true 1:1 scale. (Reframes the earlier "honest 1:1 render distances" idea: uniform unit-space scaling *is* the 1:1 model, not a violation of it.)
- [ ] **RND-07**: The skybox↔mesh handoff is visually continuous. When the player crosses a scale boundary and a star switches between a skybox light-point and a rendered mesh (e.g. Star→Galaxy space, where in-galaxy stars become meshes), the change in its apparent position, brightness, and color is barely perceptible — no pop, jump, or flicker

### HUD

- [ ] **HUD-01**: Player sees a current speed readout whose unit adapts to the scale of travel
- [ ] **HUD-02**: Player sees a context/location label showing the current space level or nearest body
- [ ] **HUD-03**: A crosshair / reticle is shown at screen center
- [ ] **HUD-04**: Player can view (and cycle) a target readout showing a body's name and distance

### Travel (integration milestones)

- [ ] **TRV-01**: Player can fly around a single star system and approach dithered planets and stars (first sequenced milestone — in-system flight + look-and-feel)
- [ ] **TRV-02**: Player can fly from one galaxy to another, with SOI transitions and the skybox updating correctly along the way (second sequenced milestone — cross-galaxy travel)

## v2 Requirements

Deferred to a future release. Tracked but not in the current roadmap.

### Flight & Controls

- **FLT-04**: Boost / afterburner — a short burst of extra speed with audio and visual feedback

### Presentation

- **PRES-01**: CRT scanline post-process overlay (wire up the existing unused `crt.gdshader`)
- **PRES-02**: Engine and boost audio feedback
- **PRES-03**: Selectable 1-bit / monochrome render mode (palette toggle on the dithering pipeline)

## Out of Scope

Explicitly excluded for this milestone. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Full Wing Commander-style cockpit interior art | Deferred; v1 uses a minimal HUD to prove flight and rendering first |
| Procedural universe generation | Deferred; v1 uses hand-authored test data to validate mechanics cheaply |
| Trading / economy systems | Long-term goal; not part of the travel-and-rendering foundation |
| Combat / dogfighting | Long-term goal; depends on flight foundation being solid first |
| 6DOF Newtonian flight physics | Rejected for v1 in favor of arcade feel |
| Survival / resource-sim mechanics | Not selected as a target direction for now |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| STAB-01 | Phase 1 | Pending |
| FLT-01 | Phase 1 | Pending |
| FLT-02 | Phase 1 | Pending |
| FLT-03 | Phase 1 | Pending |
| WORLD-01 | Phase 1 | Pending |
| RND-01 | Phase 1 | Pending |
| RND-02 | Phase 1 | Pending |
| RND-03 | Phase 1 | Pending |
| RND-04 | Phase 1 | Pending |
| RND-05 | Phase 2 | Pending |
| RND-06 | Phase 1 | Pending |
| RND-07 | Phase 2 | Pending |
| HUD-01 | Phase 1 | Pending |
| HUD-02 | Phase 1 | Pending |
| HUD-03 | Phase 1 | Pending |
| HUD-04 | Phase 1 | Pending |
| TRV-01 | Phase 1 | Pending |
| TRV-02 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0 ✓

---
*Requirements defined: 2026-06-12*
*Last updated: 2026-06-13 — clarified the tiered mesh/skybox model (RND-02/04/05): in-system → planets + sun(s) as meshes; in-galaxy → that galaxy's stars as meshes; skybox carries only the next tier out (other systems' stars, then only galaxies). Added RND-07 (visually continuous skybox↔mesh handoff).*
