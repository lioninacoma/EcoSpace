# EcoSpace

## What This Is

A first-person retro space sim set in a 1:1-scale universe. The player flies a low-poly spaceship through nested coordinate spaces — from inside a star system out to galaxy scale and across to other galaxies — rendered in an 8-bit color, dithered, CRT-styled aesthetic reminiscent of early-90s space sims (Wing Commander, Elite/Frontier). It is built on an existing high-precision universe engine (hierarchical sphere-of-influence transitions, unlimited-range `UniVec3` positions, SIMD math) that keeps relative distances small enough to render a real 1:1 cosmos without floating-point drift.

## Core Value

The player can fly seamlessly through a massive 1:1-scale universe — from a planet's surroundings out to interstellar and intergalactic distances — with rendering and flight that stay correct and feel good across every scale.

## Requirements

### Validated

<!-- Inferred from existing codebase (see .planning/codebase/). These are built and relied upon. -->

- ✓ Hierarchical multi-scale universe model (Root → Universe → Galaxy → Star → Planet) — existing
- ✓ Sphere-of-influence (SOI) transitions with automatic reparenting across scale boundaries — existing
- ✓ Unlimited-range, drift-free position math (`UniVec3` = Long3 units + Double3 offset + scale) — existing
- ✓ SIMD-accelerated double-precision vector math (`Double3`, AVX2 with scalar fallback) — existing
- ✓ Dithering post-process shader pipeline with inspector-tunable parameters (`UniRenderer`) — existing
- ✓ Test simulation of an object moving between bodies across SOI boundaries (`TestSetup`) — existing

### Active

<!-- v1 hypotheses — building toward these. See REQUIREMENTS.md for the scoped, ID'd list. -->

- [ ] First-person spaceship flight with arcade (Wing Commander-style) controls
- [ ] Context-auto-scaling speed (crawl near bodies, accelerate enormously in empty space) — no manual mode switching
- [ ] Player held at origin (floating origin) so distances stay relative to the player
- [ ] Planets rendered as sphere meshes with dithering and an 8-bit color palette
- [ ] Stars rendered as bright, light-emitting spheres (no cast shadows)
- [ ] Only objects inside the current parent space rendered dynamically
- [ ] Dynamic spherical skybox representing all stars/galaxies outside the current space, updated on scale transitions
- [ ] Minimal retro HUD (e.g. speed, current target/scale)
- [ ] In-system flight: fly around a single star system and approach dithered bodies (first sequenced goal)
- [ ] Cross-galaxy travel: fly between galaxies with correct SOI transitions and skybox updates (second sequenced goal)

### Out of Scope

<!-- For this milestone. Explicit boundaries with reasoning. -->

- Full Wing Commander-style cockpit interior art — deferred; v1 uses a minimal HUD to prove flight and rendering first
- Procedural universe generation — deferred; v1 uses hand-authored test data to validate mechanics cheaply
- Trading / economy systems — long-term goal, not part of the travel-and-rendering foundation
- Combat / dogfighting — long-term goal, depends on flight foundation being solid first
- 6DOF Newtonian flight physics — rejected in favor of arcade feel for v1
- 1-bit / monochrome DOS render mode — rejected for v1 in favor of 8-bit color (palette pipeline may make it cheap to add later)
- Survival / resource-sim mechanics — not selected as a target direction for now

## Context

- **Engine already in place.** This is a brownfield project. Godot 4.6.2 (Mono / C# 12, .NET 8.0) with the Universe simulation, math library, SOI transition logic, and dithering renderer already implemented and working. See `.planning/codebase/` for the full map.
- **The hard part is solved.** The multi-scale coordinate system and SOI transitions — the thing that makes a 1:1 universe renderable — exist and are tested via `TestSetup`. This project builds the *playable game layer* (flight, controls, rendering of bodies, skybox, HUD) on top of that foundation.
- **Rendering target.** 8-bit indexed-color look with dithering for shading, plus CRT-style scanline effects. A `crt.gdshader` already exists but is currently unused; `dithering.gdshader` is wired up.
- **Floating origin.** The player ship should remain at the coordinate origin, with the world translated around it, to keep rendered distances small and precise.
- **Skybox model.** Objects outside the current SOI/parent space are not rendered as geometry; they are projected onto a dynamic spherical skybox. On scale transitions (e.g. entering galaxy scale), these representations must be repositioned to reflect the new frame.
- **Reference feel.** Wing Commander for cockpit/flight feel and HUD; Elite/Frontier for the sense of a vast traversable cosmos.

## Constraints

- **Tech stack**: Godot 4.6.2 Mono, C# 12 / .NET 8.0 — existing engine; new work stays in this stack and the `Universe` namespace conventions
- **Architecture**: Must build on the existing `UniVec3` / SOI / `GameWorld` model — do not replace the precision/space system; flight and rendering consume it
- **Performance**: Real-time first-person rendering; SIMD math paths and floating-origin must keep per-frame cost stable across scales
- **Rendering**: Forward Plus renderer, DirectX 12 on Windows; 8-bit/dithered look achieved via post-process shaders
- **Scope discipline**: v1 uses hand-authored test data and a minimal HUD — no procedural generation, no cockpit art, no economy/combat yet

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| 8-bit color palette (not 1-bit mono) for v1 | Richer retro look; dithering pipeline can later support a 1-bit toggle | — Pending |
| Arcade flight (Wing Commander), not 6DOF Newtonian | Forgiving, cinematic, matches the retro reference; faster to make feel good | — Pending |
| Context-auto-scaling speed | Single seamless mechanic for a 1:1 universe; avoids manual mode juggling | — Pending |
| Minimal HUD in v1, full cockpit later | Prove flight + rendering before investing in cockpit art | — Pending |
| Hand-authored test data in v1, procedural later | Validate mechanics cheaply before building a generator | — Pending |
| Build on existing SOI/`UniVec3` engine | The precision/space foundation is already solved and tested | ✓ Good |
| Sequence v1: in-system flight + rendering, then cross-galaxy travel | Get look-and-feel right at one scale before proving multi-scale traversal | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-06-12 after initialization*
