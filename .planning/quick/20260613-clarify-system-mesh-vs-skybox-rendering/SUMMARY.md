---
type: quick
slug: clarify-system-mesh-vs-skybox-rendering
quick_id: 260613-kor
status: complete
subsystem: docs/requirements
tags: [rendering, skybox, star-system, requirements, RND-02, RND-04, RND-05]
provides:
  - Tiered mesh/skybox model — in-system: planets + sun(s) as meshes; in-galaxy: that galaxy's stars as meshes
  - Skybox carries only the next tier out (other systems' stars while in-system, only galaxies while in-galaxy)
  - New RND-07 requirement: visually continuous skybox<->mesh handoff across scale transitions
affects: [01-in-system-flight-mvp, phase-2-skybox, phase-3-cross-galaxy]
key-files:
  modified:
    - .planning/REQUIREMENTS.md
    - .planning/PROJECT.md
    - .planning/phases/01-in-system-flight-mvp/01-CONTEXT.md
key-decisions:
  - "Render scope is the current scale tier, not the immediate SOI parent: system sun(s) stay meshes in Planet space; a galaxy's stars become meshes in Galaxy space"
  - "Multi-star systems render every sun as a mesh + light"
  - "Skybox = only the next tier out (other systems' stars in-system; only galaxies in-galaxy), as distant light points"
  - "RND-07 (new, Phase 2): the skybox<->mesh handoff at scale transitions must be barely perceptible"
duration: 1min
completed: 2026-06-13
---

# Quick Task: Clarify system-mesh vs skybox rendering model

**Docs-only: the current star system (planets + sun(s), incl. multi-star) renders as sphere meshes in both Star and Planet space; only other systems' stars and galaxies appear on the dynamic skybox.**

## Accomplishments
- Reframed **RND-02** into a tiered rule: bodies of the *current render tier* are meshes, the next tier out is deferred to the skybox. Inside a system (Star + Planet space) the planets + sun(s) are meshes; in Galaxy space the current galaxy's stars/suns are meshes.
- Updated **RND-04** to cover multi-star systems (every sun renders as an emissive mesh + light).
- Updated **RND-05** so the skybox holds *only the next tier out* — other systems' stars (and galaxies) while in-system; only other galaxies while in-galaxy.
- Added **RND-07** (Phase 2): the skybox↔mesh handoff at scale transitions must be visually continuous — no perceptible pop in position, brightness, or color when a star is promoted/demoted.
- Propagated the framing into PROJECT.md (Active rendering bullets + tiered "Skybox model") and the Phase 01 CONTEXT (phase boundary, body-renderer note, out-of-scope RND-07).

## Files Created/Modified
- `.planning/REQUIREMENTS.md` — RND-02 / RND-04 / RND-05 reworded, new RND-07, traceability + coverage (17→18), footer
- `.planning/PROJECT.md` — Active rendering bullets, tiered Context "Skybox model" bullet, footer
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — phase boundary RND-01/02 line, body-renderer note, out-of-scope RND-07
- `.planning/quick/20260613-clarify-system-mesh-vs-skybox-rendering/PLAN.md` — task plan

## Notes
- No code changes. Phase 1's authored world is single-star and in-system only, so the multi-star and galaxy-tier wording is forward-looking (Phase 2/3) and does not alter current Phase 1 implementation. Implementer takeaways: (1) a Planet-space body renderer must include the parent star and sibling planets, not only the current parent's children; (2) Galaxy-space rendering promotes the galaxy's stars from skybox points to meshes, and that handoff (RND-07) must be seamless.
