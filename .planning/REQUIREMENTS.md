# Requirements: EcoSpace

**Defined:** 2026-06-12
**Core Value:** The player can fly seamlessly through a massive 1:1-scale universe — from a planet's surroundings out to interstellar and intergalactic distances — with rendering and flight that stay correct and feel good across every scale.

## v1 Requirements

Requirements for the initial playable travel-and-rendering milestone. Each maps to roadmap phases. Built on the existing universe engine (SOI transitions, `UniVec3` precision, dithering shader) — see `.planning/codebase/`.

### Stability

- [x] **STAB-01**: SOI transition handling is iterative and null-safe — the ship can cross multiple SOI boundaries in a single frame without crashing or corrupting the object hierarchy

### Flight & Controls

- [x] **FLT-01**: Player can pitch and yaw the ship with the mouse (primary axes) and roll (secondary), with arcade-style auto-stabilization
- [x] **FLT-02**: Player can control forward throttle and slow or stop the ship
- [x] **FLT-03**: Ship speed auto-scales to its SOI surroundings — crawling near bodies and accelerating enormously in empty space — with no manual mode switching

### World

- [x] **WORLD-01**: A hand-authored multi-scale test universe (a star system with planets, plus multiple galaxies) exists to fly through and validate transitions

### Rendering

- [x] **RND-01**: Player is held at the coordinate origin while the world is translated around them (floating origin); object `UniVec3` positions are synced to Godot `Node3D` transforms each frame without precision jitter
- [~] **RND-02**: Bodies belonging to the current render tier are rendered dynamically as sphere-mesh geometry; bodies one tier further out are deferred to the skybox (RND-05). Concretely: **inside a star system** (both Star space and Planet space, not just the immediate parent) the system's planet(s) *and* star(s)/sun(s) — including every sun of a multi-star system — are meshes; **in Galaxy space** the stars/suns of the current galaxy are meshes. Bodies beyond the current tier (other systems' stars while in-system, other galaxies while in-galaxy) are not rendered as meshes  _(Phase 1 PARTIAL: in Star space the system's planets + star render as meshes ✓. In Planet space only the orbited planet renders; rendering the star + sibling planet while in Planet space is bounded by the far-plane/1:1-scale limit — the sun at 1 AU is ~15× beyond the far plane — and is deferred with D-16 to the Phase 2/3 tiered/skybox renderer. Accepted at 2026-06-14 Phase 1 verification.)_
- [x] **RND-03**: Planets are rendered as sphere meshes with dithering and an 8-bit color palette
- [x] **RND-04**: The current system's star(s)/sun(s) — including each sun in a multi-star system — are rendered as bright, light-emitting sphere meshes (no cast shadows), each casting light that affects the system's planets  _(Phase 1: Star-space emissive sun mesh + shader-based Lambert lighting on planets ✓; galaxy-tier suns-as-meshes → Phase 3.)_
- [ ] **RND-05**: A dynamic spherical skybox represents the bodies just *outside* the current render tier as distant light points — the stars of other star systems (and other galaxies) while inside a system, and *only* other galaxies while in Galaxy space. It updates when the player crosses a scale boundary and never drifts with camera rotation
- [x] **RND-06**: Distances are kept 1:1 in both calculation and rendering — the universe simulation (SOI, `UniVec3`, distances, speed envelopes) runs at true 1:1 meters, and rendering reproduces those distances in a uniformly scaled **unit-space** (observer-scale unit basis × per-space render factor) so 1:1 proportions and perspective are preserved while the camera far plane stays bounded (≤ 1e6 render units). Body radii are likewise rendered at true 1:1 scale. (Reframes the earlier "honest 1:1 render distances" idea: uniform unit-space scaling *is* the 1:1 model, not a violation of it.)
- [ ] **RND-07**: The skybox↔mesh handoff is visually continuous. When the player crosses a scale boundary and a star switches between a skybox light-point and a rendered mesh (e.g. Star→Galaxy space, where in-galaxy stars become meshes), the change in its apparent position, brightness, and color is barely perceptible — no pop, jump, or flicker

### HUD

- [x] **HUD-01**: Player sees a current speed readout whose unit adapts to the scale of travel
- [x] **HUD-02**: Player sees a context/location label showing the current space level or nearest body
- [x] **HUD-03**: A crosshair / reticle is shown at screen center
- [x] **HUD-04**: Player can view (and cycle) a target readout showing a body's name and distance  _(Phase 1: current-space target cycle + fixed readout + off-screen edge marker per D-12. Extended targeting — whole-hierarchy tree selector + world-pinned outline + tracking label — deferred to Backlog 999.1.)_

### Travel (integration milestones)

- [x] **TRV-01**: Player can fly around a single star system and approach dithered planets and stars (first sequenced milestone — in-system flight + look-and-feel)
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
| STAB-01 | Phase 1 | Done (Phase 1 ✓) |
| FLT-01 | Phase 1 | Done (Phase 1 ✓) |
| FLT-02 | Phase 1 | Done (Phase 1 ✓) |
| FLT-03 | Phase 1 | Done (Phase 1 ✓) |
| WORLD-01 | Phase 1 | Done (Phase 1 ✓) |
| RND-01 | Phase 1 | Done (Phase 1 ✓) |
| RND-02 | Phase 1 (Star-space in-system meshes) · Phase 2/3 (Planet-space cross-body + galaxy-tier meshes) | Partial — Star-space ✓ (Phase 1); Planet-space cross-body deferred (D-16) → Phase 2/3 |
| RND-03 | Phase 1 | Done (Phase 1 ✓) |
| RND-04 | Phase 1 (Star-space system sun mesh + lighting) · Phase 3 (galaxy stars as meshes) | Done (Phase 1 portion ✓); galaxy-tier → Phase 3 |
| RND-05 | Phase 2 (in-system skybox) · Phase 3 (in-galaxy skybox = only galaxies) | Pending |
| RND-06 | Phase 1 | Done (Phase 1 ✓) |
| RND-07 | Phase 2 (handoff baseline) · Phase 3 (Star→Galaxy-scale handoff) | Pending |
| HUD-01 | Phase 1 | Done (Phase 1 ✓) |
| HUD-02 | Phase 1 | Done (Phase 1 ✓) |
| HUD-03 | Phase 1 | Done (Phase 1 ✓) |
| HUD-04 | Phase 1 (current-space cycle + edge marker) · Backlog 999.1 (whole-hierarchy tree + world-pinned outline) | Done (Phase 1 scope ✓); extended targeting → Backlog 999.1 |
| TRV-01 | Phase 1 | Done (Phase 1 ✓) |
| TRV-02 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0 ✓

> **Tiered rendering requirements are delivered incrementally.** RND-02, RND-04,
> RND-05, and RND-07 each describe a single tier *rule* that applies at multiple
> scales. The in-system portion lands in Phase 1/2; the galaxy-tier portion
> (in-galaxy stars become meshes, the skybox carries only other galaxies, and the
> Star→Galaxy skybox↔mesh handoff stays seamless) is completed in Phase 3. Each
> requirement is counted once above even though its delivery spans phases — see
> the Phase column for the per-phase split.

---
*Requirements defined: 2026-06-12*
*Last updated: 2026-06-13 — clarified the tiered mesh/skybox model (RND-02/04/05): in-system → planets + sun(s) as meshes; in-galaxy → that galaxy's stars as meshes; skybox carries only the next tier out (other systems' stars, then only galaxies). Added RND-07 (visually continuous skybox↔mesh handoff).*
*Last updated: 2026-06-13 — marked RND-02/04/05/07 as phase-spanning in traceability (in-system portion early, galaxy-tier portion in Phase 3) so Phase 3 carries explicit rendering coverage rather than TRV-02 alone.*
*Last updated: 2026-06-14 — Phase 1 verified & closed. STAB-01, WORLD-01, RND-01/03/06, FLT-01/02/03, HUD-01/02/03/04, TRV-01 delivered; RND-04 Phase-1 portion (Star-space sun mesh + lighting) delivered. RND-02 accepted as PARTIAL: Star-space in-system meshes delivered; Planet-space cross-body rendering (star + sibling planet visible while orbiting a planet) deferred to the Phase 2/3 tiered/skybox renderer per D-16 (far-plane/1:1-scale bound). Extended targeting HUD (whole-hierarchy tree selector + world-pinned outline + tracking label) deferred to Backlog Phase 999.1 (overrides D-12).*
