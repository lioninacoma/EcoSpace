# Roadmap: EcoSpace

## Overview

Three phases build the playable game layer on top of EcoSpace's existing multi-scale universe engine. Phase 1 delivers the first sequenced acceptance goal — in-system flight with dithered body rendering and a minimal HUD — as a single large vertical slice that also fixes the known pre-flight crash. Phase 2 adds the dynamic skybox, the only subsystem flagged for shallow phase research. Phase 3 delivers the second sequenced goal: cross-galaxy travel with full SOI transitions at intergalactic scale.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: In-System Flight MVP** - Fix pre-flight crash, floating-origin rendering, arcade flight, HUD — player can fly a star system and approach dithered bodies (COMPLETE 2026-06-14)
- [ ] **Phase 2: Dynamic Skybox** - Shader-type sky updated on scale-tier transitions; only the next tier out (other systems' stars, then only galaxies) is projected onto a stable spherical skybox, with a visually continuous skybox↔mesh handoff
- [ ] **Phase 3: Cross-Galaxy Travel** - Extend hand-authored data to galaxy/universe scale; full SOI chain validated at intergalactic distances

## Phase Details

### Phase 1: In-System Flight MVP

**Goal**: Player can fly around a single star system, approach dithered planets and stars, and read their speed and context on a minimal HUD
**Mode:** mvp
**Depends on**: Nothing (first phase); consumes existing engine (GameWorld, UniVec3, SOI, dithering shader)
**Requirements**: STAB-01, WORLD-01, RND-01, RND-02, RND-03, RND-04, RND-06, FLT-01, FLT-02, FLT-03, HUD-01, HUD-02, HUD-03, HUD-04, TRV-01
**Success Criteria** (what must be TRUE):

  1. Player can pitch, yaw, and roll the ship with mouse input and arcade auto-stabilization; the ship does not drift or spin uncontrolled
  2. Ship speed auto-scales to its SOI context — noticeably slower near bodies, faster in open space — with no manual mode switch
  3. Planets and stars appear as dithered sphere meshes with an 8-bit color palette; bodies grow visibly as the player approaches
  4. A minimal HUD shows current speed (with scale-adaptive units), a context/location label, a crosshair, and a cycle-able target readout
  5. Player can cross SOI boundaries (enter and exit planet/star SOI) at high speed without a crash or hierarchy corruption

**Plans**: 4 plans
Plans:

- [x] 01-01-PLAN.md — Walking Skeleton: STAB-01 iterative transition + floating-origin render + thrust-driven ship + live speed HUD
- [x] 01-02-PLAN.md — Body rendering: per-body hues + 1:1 radii + emissive star/OmniLight + color-palette dither
- [x] 01-03-PLAN.md — Flight feel: virtual-joystick steering + roll + persistent throttle [-1,1] (reverse) + distance-scaled speed + crosshair/reticle (APPROVED)
- [x] 01-04-PLAN.md — Minimal HUD: adaptive speed units + context label + target cycle + off-screen marker + phosphor-green (TRV-01) (APPROVED)

**UI hint**: yes

### Phase 2: Dynamic Skybox

**Goal**: A stable spherical skybox represents only the next scale tier out as distant light points — other systems' stars (and galaxies) while in-system, and *only* other galaxies while in Galaxy space — updates when the player crosses a scale boundary, never drifts with camera rotation, and hands off seamlessly to/from meshes. Render scope is the current scale **tier**, not the immediate SOI parent: in Galaxy space the current galaxy's stars/suns become meshes
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: RND-05, RND-07
**Success Criteria** (what must be TRUE):

  1. The skybox visibly changes (its light points reposition / re-tier) when the player crosses a scale boundary
  2. The skybox does not rotate or drift as the player rotates the ship — it remains fixed to world space
  3. Only the next tier out is on the skybox; bodies of the current tier are meshes (in Galaxy space the galaxy's stars are meshes, not skybox points), and bodies beyond the next tier are neither
  4. The skybox↔mesh handoff is visually continuous (RND-07): a star promoted from a skybox point to a mesh, or demoted back, on a scale transition shows no perceptible pop in position, brightness, or color

**Plans**: TBD

### Phase 3: Cross-Galaxy Travel

**Goal**: Player can fly from one galaxy to another, with SOI transitions and the skybox updating correctly across the full Root → Universe → Galaxy hierarchy
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: TRV-02; galaxy-tier portions of RND-02, RND-04, RND-05, RND-07 (in Galaxy space the current galaxy's stars/suns become emissive meshes; the skybox carries only other galaxies; the Star↔Galaxy skybox↔mesh handoff stays seamless)
**Success Criteria** (what must be TRUE):

  1. The hand-authored test universe contains at least two distinct galaxies the player can navigate between
  2. Player can fly from inside one galaxy's SOI to intergalactic space and into a second galaxy's SOI with no crash, pop, or loading screen
  3. The skybox updates correctly at each scale transition (galaxy-to-universe and universe-to-galaxy) during cross-galaxy flight
  4. Ship speed at intergalactic scale reaches FTL-equivalent magnitudes via auto-scaling — the journey is completable in a reasonable play session
  5. In Galaxy space the current galaxy's stars/suns render as emissive sphere meshes (promoted from skybox light-points) and the skybox there carries only other galaxies; the promotion/demotion handoff across the Star→Galaxy boundary is visually continuous (RND-07) — no perceptible pop in position, brightness, or color

**Plans**: TBD

## Backlog

### Phase 999.1: Targeting & navigation HUD — hierarchy tree selector + world-pinned target outline (BACKLOG)

**Goal:** A target/selection system beyond the Phase-1 minimal HUD, deferred from plan 01-04:
  1. A folder/tree-structure menu to select **any** object in the universe hierarchy (across spaces) — overrides locked decision **D-12** (current-parent-space targeting only).
  2. A **world-pinned target outline** drawn around the selected body that holds a **minimum on-screen radius**, so a distant star/planet is always visible even as a sub-pixel speck.
  3. A **name + distance label pinned to the outline** that tracks the object on screen (moves with the body), augmenting the fixed-corner target readout + off-screen edge marker shipped in 01-04.

  Cross-space constraint to resolve in planning: `WorldRenderer` only renders bodies in the ship's current space, so a cross-space target can show direction + distance (edge marker) but the 3D outline can only be drawn once that body enters the rendered set (i.e. once the ship is in its space).
**Requirements:** TBD (extends HUD-04 + findability; revisits D-12)
**Plans:** 0 plans

Plans:
- [ ] TBD (promote with /gsd-review-backlog when ready)

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. In-System Flight MVP | 4/4 (TRV-01 milestone) | ✓ Complete | 2026-06-14 |
| 2. Dynamic Skybox | 0/TBD | Not started | - |
| 3. Cross-Galaxy Travel | 0/TBD | Not started | - |
