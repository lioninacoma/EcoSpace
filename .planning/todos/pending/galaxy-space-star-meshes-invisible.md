---
type: tech-debt
status: pending
priority: P1
created: 2026-06-18
area: render
origin: phase-03 UAT play-test (Test 2, re-run after Phase 04)
tags: [tech-debt, render, mesh, star, galaxy-space, findability, scale, RND-02, RND-07]
related: [galaxy-visibility-in-universe-space, galaxy-disc-tilt-foreshortening]
---

# Galaxy-space star meshes are invisible (sub-pixel + too dim)

## What

Flying out of the home star SOI into **Galaxy space**, the home galaxy's stars
(STAR, ALPHA CEN, BARNARD, SIRIUS) are supposed to render as visible emissive sphere
meshes (RND-02/04, the 03-03 deliverable). In play-test they are **not visible at
all** — the sky is black. The bodies ARE in the render set: the player can cycle-target
them and the D-46 target circle appears, but it **circles empty black space** because no
visible mesh is drawn there.

## Why (root cause analysis)

Two compounding effects, both from rendering at true 1:1 scale in Galaxy space:

1. **Mesh radius collapses to sub-pixel.** In `WorldRenderer.RenderBodyAt`
   (`Scripts/Render/WorldRenderer.cs:452`) the render radius is
   `r = (RadiusMeters / ship.LocalPos.Scale) * factor`. In Galaxy space the scale is
   the galaxy unit-scale (very large m/unit) and `GalaxyRenderFactor = 1e-8`
   (`WorldRenderer.cs:64`, flagged "Placeholder — Galaxy tier not exercised by MVP scene;
   tune when reached"). A star radius ~7e8 m at light-year separations yields an angular
   size far below one pixel — the mesh is physically present but invisibly tiny.

2. **Emissive brightness floors to ~0.** The star mesh's `EmissionEnergyMultiplier` is
   set each frame from `StarRendering.ApparentBrightness(Luminosity, distMeters)`
   (`WorldRenderer.cs:487`) — inverse-square flux through a magnitude curve. At
   light-year distance the apparent brightness is essentially zero, so even the sub-pixel
   mesh emits no visible light.

This is the long-standing "findability of tiny specks at 1:1 scale" issue noted in the
01-render decisions (deferred to "HUD marker + Phase 3 tiered renderer") — it finally
bites now that the flight model (Phase 04) lets the player reach Galaxy space.

## What "done" looks like

- In Galaxy space the current galaxy's stars are **clearly visible** as emissive
  bodies you can fly toward — not black space behind a target circle.
- A **minimum on-screen size / brightness floor** for tier-member star meshes (mirror
  the skybox point treatment: discs floored at ~1 screen pixel, brightness clamped into
  a visible band) so distant stars stay findable without breaking 1:1 proportions when
  close.
- The Star↔Galaxy point↔mesh handoff (RND-07) stays visually continuous — unblocks
  UAT Tests 4 & 6, which are currently blocked on this.

## Approach options (to settle in planning)

1. **Minimum-angular-size mesh + emission floor** — clamp `r` to a minimum render-unit
   size and `EmissionEnergyMultiplier` to a visible floor for tier-member stars (cheapest;
   matches the skybox 1-pixel disc floor already in `StarRendering`).
2. **Billboard/point promotion** — render distant tier stars as camera-facing billboards
   sized to a screen-pixel floor, swapping to the true sphere mesh on close approach
   (the RND-07 handoff pattern).
3. **Tune `GalaxyRenderFactor`** — likely insufficient alone (a single factor cannot make
   both close and light-year-distant stars sane), but may be part of the fix.

## Notes

- Blocks UAT Test 2 (issue), and Tests 4 & 6 (blocked).
- Shares the conceptual fix ("findability floor for distant tier bodies") with
  [[galaxy-visibility-in-universe-space]] — plan them together.
- Requires in-game Godot verification (GDShader/visual; build + unit tests can't catch it).
- File under review: `Scripts/Render/WorldRenderer.cs` (RenderBodyAt, RenderFactorFor,
  StarRendering brightness curve).
