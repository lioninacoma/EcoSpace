---
type: tech-debt
status: pending
priority: P2
created: 2026-06-17
area: render
origin: phase-03 UAT play-test (intergalactic transit)
tags: [tech-debt, render, skybox, galaxy, universe-space, design, needs-discussion]
related: [flight-speed-model-tier-and-target-aware, galaxy-space-star-meshes-invisible]
confirmed: 2026-06-18 (phase-03 UAT Test 5 re-run — "in universe space I cannot see galaxies and they are not circled by the target marker")
---

# Galaxies disappear in Universe space (no distance cue to target galaxy)

## What

While in **Galaxy space**, the other galaxies are visible as skybox disc entities.
On transitioning **out into Universe space**, the galaxies **disappear entirely** —
there is no visual of the destination galaxy and therefore no cue for how far away it
is. This blocks confirming intergalactic flight (UAT items 5–7): the player cannot
judge approach to the target galaxy.

## Why

`WorldRenderer` explicitly **never renders galaxies as meshes** (D-28 / T-03-06 —
`Scripts/Render/WorldRenderer.cs` lines ~239 and ~255 skip any `ObjectType ==
Type.Galaxy` in both the parent and sibling render paths). The procedural galaxy discs
come from `SkyboxRenderer`, which only projects the "next tier out." In Universe space
the galaxies are the CURRENT tier, so the skybox no longer carries them — and because
meshes are skipped, nothing renders them at all.

## Status (2026-06-19)

**Standalone debt** — un-bundled from the deleted old Phase 5. Carries a design fork
(see below); to be promoted to its own phase **after** the Phase 5 *Rendering Overhaul*,
which is the right place for the unified tier-render path this builds on.

## Open design question (needs discussion)

In Universe space, how should galaxies be drawn?
1. **As meshes** — reverse D-28 for the Universe tier so galaxies become emissive
   disc/sphere meshes you fly toward (mirrors how stars become meshes in Galaxy space).
   Need to decide on a sensible render factor and disc/billboard representation at
   true 1:1 intergalactic distances.
2. **Enhanced skybox + HUD distance** — keep galaxies as skybox discs that grow with
   proximity, and lean on a target-distance HUD readout for the "how far" cue.
3. **Hybrid** — skybox disc beyond a LOD threshold, promote to mesh on close approach
   (the same point↔mesh handoff pattern as Star↔Galaxy, RND-07).

Is it even reasonable to draw galaxy meshes at these distances? That's the core of the
discussion. Whatever we pick, the Star↔Galaxy / Galaxy↔Universe handoffs must stay
visually continuous (RND-07).

## What "done" looks like

- Flying out of a galaxy into Universe space, the destination galaxy remains visible
  with a clear sense of distance/approach (visual and/or HUD).
- The handoff across the Galaxy↔Universe boundary is pop-free.
- D-28 is revisited and the new decision recorded (supersede or scope it to a tier).

## Notes

- Blocks UAT items 5, 6, 7 (intergalactic transit / far-end integration / crossing time).
- Pairs naturally with a target-distance HUD readout (see
  [[flight-speed-model-tier-and-target-aware]]).
- Requires in-game Godot verification.
