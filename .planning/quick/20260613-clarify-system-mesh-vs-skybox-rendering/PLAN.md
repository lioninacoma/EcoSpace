---
type: quick
slug: clarify-system-mesh-vs-skybox-rendering
quick_id: 260613-kor
created: 2026-06-13
---

# Quick Task: Clarify system-mesh vs skybox rendering model

Documentation/requirements-only change. Clarify the rendering split so it is
unambiguous which bodies are meshes and which are skybox light points.

## Decision being captured

**Tiered mesh/skybox rendering.** Each scale tier renders its own bodies as
meshes and defers the next tier out to the skybox.

- **Inside a star system** (both Star space and Planet space): the system's
  **planet(s)** and its **star(s)/sun(s)** — including every sun in a multi-star
  system — are sphere meshes. The sun stays a mesh even while flying close to a
  planet (its immediate parent space). Skybox: other systems' stars + galaxies.
- **In Galaxy space**: the stars/suns of the current galaxy are rendered as
  meshes. Skybox: *only* other galaxies.
- **Visual continuity (RND-07):** the skybox↔mesh handoff at a scale transition
  (e.g. Star→Galaxy, where in-galaxy stars promote from skybox points to meshes)
  must be barely perceptible — no pop in position, brightness, or color.

This sharpens the earlier "only the current parent space renders as geometry"
framing (RND-02). The render scope is the **current scale tier** (star system,
then galaxy), not the immediate SOI parent.

## Files changed

- `.planning/REQUIREMENTS.md` — RND-02 (tiered render scope), RND-04 (system's
  sun(s), multi-star), RND-05 (skybox = next tier out), new RND-07 (continuous
  handoff); traceability + coverage (17→18); footer note.
- `.planning/PROJECT.md` — Active rendering bullets + tiered Context "Skybox
  model" bullet; footer note.
- `.planning/phases/01-in-system-flight-mvp/01-CONTEXT.md` — Phase boundary
  RND-01/02 line, Body-renderer integration note, out-of-scope RND-07.

## Out of scope

No code changes. Phase 1's authored world is single-star and in-system only;
the multi-star and galaxy-tier wording (RND-07) is forward-looking (Phase 2/3)
and does not change Phase 1 implementation.
