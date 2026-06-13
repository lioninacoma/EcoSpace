# Roadmap: EcoSpace

## Overview

Three phases build the playable game layer on top of EcoSpace's existing multi-scale universe engine. Phase 1 delivers the first sequenced acceptance goal — in-system flight with dithered body rendering and a minimal HUD — as a single large vertical slice that also fixes the known pre-flight crash. Phase 2 adds the dynamic skybox, the only subsystem flagged for shallow phase research. Phase 3 delivers the second sequenced goal: cross-galaxy travel with full SOI transitions at intergalactic scale.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: In-System Flight MVP** - Fix pre-flight crash, floating-origin rendering, arcade flight, HUD — player can fly a star system and approach dithered bodies
- [ ] **Phase 2: Dynamic Skybox** - Shader-type sky with SOI-aware updates; out-of-space objects projected onto a stable spherical skybox
- [ ] **Phase 3: Cross-Galaxy Travel** - Extend hand-authored data to galaxy/universe scale; full SOI chain validated at intergalactic distances

## Phase Details

### Phase 1: In-System Flight MVP
**Goal**: Player can fly around a single star system, approach dithered planets and stars, and read their speed and context on a minimal HUD
**Mode:** mvp
**Depends on**: Nothing (first phase); consumes existing engine (GameWorld, UniVec3, SOI, dithering shader)
**Requirements**: STAB-01, WORLD-01, RND-01, RND-02, RND-03, RND-04, FLT-01, FLT-02, FLT-03, HUD-01, HUD-02, HUD-03, HUD-04, TRV-01
**Success Criteria** (what must be TRUE):
  1. Player can pitch, yaw, and roll the ship with mouse input and arcade auto-stabilization; the ship does not drift or spin uncontrolled
  2. Ship speed auto-scales to its SOI context — noticeably slower near bodies, faster in open space — with no manual mode switch
  3. Planets and stars appear as dithered sphere meshes with an 8-bit color palette; bodies grow visibly as the player approaches
  4. A minimal HUD shows current speed (with scale-adaptive units), a context/location label, a crosshair, and a cycle-able target readout
  5. Player can cross SOI boundaries (enter and exit planet/star SOI) at high speed without a crash or hierarchy corruption
**Plans**: 4 plans
Plans:
- [ ] 01-01-PLAN.md — Walking Skeleton: STAB-01 iterative transition + floating-origin render + thrust-driven ship + live speed HUD
- [ ] 01-02-PLAN.md — Body rendering: per-body hues + 1:1 radii + emissive star/OmniLight + color-palette dither
- [ ] 01-03-PLAN.md — Flight feel: virtual-joystick steering + roll + persistent throttle + distance-scaled speed + crosshair/reticle
- [ ] 01-04-PLAN.md — Minimal HUD: adaptive speed units + context label + target cycle + off-screen marker + phosphor-green (TRV-01)
**UI hint**: yes

### Phase 2: Dynamic Skybox
**Goal**: A stable spherical skybox represents all stars and galaxies outside the current SOI, updates on SOI transitions, and never drifts with camera rotation
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: RND-05
**Success Criteria** (what must be TRUE):
  1. The skybox visibly changes (star/galaxy representations reposition) when the player transitions across an SOI boundary
  2. The skybox does not rotate or drift as the player rotates the ship — it remains fixed to world space
  3. Out-of-SOI bodies (stars, galaxies) are not rendered as geometry; they appear only on the skybox
**Plans**: TBD

### Phase 3: Cross-Galaxy Travel
**Goal**: Player can fly from one galaxy to another, with SOI transitions and the skybox updating correctly across the full Root → Universe → Galaxy hierarchy
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: TRV-02
**Success Criteria** (what must be TRUE):
  1. The hand-authored test universe contains at least two distinct galaxies the player can navigate between
  2. Player can fly from inside one galaxy's SOI to intergalactic space and into a second galaxy's SOI with no crash, pop, or loading screen
  3. The skybox updates correctly at each scale transition (galaxy-to-universe and universe-to-galaxy) during cross-galaxy flight
  4. Ship speed at intergalactic scale reaches FTL-equivalent magnitudes via auto-scaling — the journey is completable in a reasonable play session
**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. In-System Flight MVP | 0/4 | Not started | - |
| 2. Dynamic Skybox | 0/TBD | Not started | - |
| 3. Cross-Galaxy Travel | 0/TBD | Not started | - |
