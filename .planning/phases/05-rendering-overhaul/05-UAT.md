---
status: complete
phase: 05-rendering-overhaul
source:
  - 05-01-SUMMARY.md
  - 05-02-SUMMARY.md
  - 05-03-SUMMARY.md
  - 05-04-SUMMARY.md
started: 2026-06-20T21:12:59Z
updated: 2026-06-20T21:18:00Z
resolution: "6/8 pass; 2 issues both deferred to render-debt P2 galaxy-visibility-in-universe-space (out of Phase 5 overhaul scope, user decision)"
---

## Current Test
<!-- OVERWRITE each test - shows where we are -->

[testing complete]

## Tests

### 1. Distant star findability (Star space)
expected: Inside the home star system, sibling stars appear as visible sky light points regardless of distance — a brightness floor (MinVisibleBrightness=0.05) keeps even very distant stars findable, never fully fading out at 1:1 distances.
result: pass

### 2. Near-sun stays bright up close (Star space)
expected: The current system's sun renders as a bright light-emitting sphere up close and inside its SOI — no "black sun" at close range (NearStarEmissionFloor keeps the mesh emissive even where inverse-square flux would read ~0).
result: pass

### 3. Near-star PSF glow + diffraction spikes (Star space)
expected: A near star shows an aperture-diffraction lens flare — a bright inverse-cube core glow with horizontal and vertical diffraction spikes around the sun mesh, intensity easing in smoothly (no hard pop) as you approach.
result: pass

### 4. Depth occlusion of the glow (Planet/Star space)
expected: When a planet (or other opaque body) passes in front of the sun, the planet's full silhouette cleanly occludes the star's glow/halo at all viewing distances and angles — the flare does not bleed through the foreground body, and the occlusion is correct close-up as well as far away.
result: pass

### 5. Continuous point→glow→mesh handoff (no pop)
expected: As a star transitions between scale tiers (far sky-point → mid point-fade/glow-grow → near sphere mesh), the change is barely perceptible — no sudden jump in position, brightness, or color at any threshold.
result: pass

### 6. Galaxy visibility + disc crossfade (Universe / Galaxy space)
expected: In Universe space the other galaxies render as sky discs (with tilt preserved, no antipodal ghost). The home galaxy is suppressed while inside its SOI. Crossing a galaxy SOI boundary, the disc fades in/out gradually across the boundary rather than popping to full opacity in one frame.
result: issue
reported: "The disc fades out really slow — when flying to a galaxy you are surrounded by the galaxy placeholder until it pops away and the stars become visible only when entering the galaxy. When flying away from a galaxy, a plane blocks half of the galaxy from rendering in the back, but if you turn there is a huge 'half' galaxy representation. Galaxy rendering on approach/leave needs reevaluation."
severity: major
deferred: true
deferred_to: "render debt P2 galaxy-visibility-in-universe-space (later phase / tech debt — user decision)"

### 7. Unified 8-bit dither palette + CRT absent (all spaces)
expected: The 8-bit dithered palette is consistent edge-to-edge — the near-star glow/halo is dithered with the same palette as planet meshes and the sky background, with no banding seam between the glow region and the rest of the frame (glow composes in HDR before the dither quantizes the whole frame). CRT scanlines are correctly absent (D-06).
result: pass

### 8. HUD target circle tracking after cache removal
expected: Cycling targets shows the phosphor-green world-pinned target circle following each body mesh on screen, tracking correctly across all spaces — unchanged behavior after the internal _lastRenderPositions cache removal (GetRenderPosition now reads the live mesh transform).
result: issue
reported: "Galaxies cannot be targeted in Universe space — root is a valid target and gets a target circle, but galaxies are not selectable. (Circle tracking for targetable bodies works; the gap is that galaxies are not targetable in Universe space.)"
severity: major
deferred: true
deferred_to: "render debt P2 galaxy-visibility-in-universe-space (galaxies have no mesh in Universe space → not findable/targetable; later phase / tech debt)"

## Summary

total: 8
passed: 6
issues: 2
pending: 0
skipped: 0

## Gaps

- truth: "Crossing a galaxy SOI boundary, the disc fades in/out gradually and the home galaxy is correctly culled approaching/leaving (no surrounding placeholder, no half-galaxy plane clipping)."
  status: failed
  reason: "User reported: disc fade-out is too slow (galaxy placeholder surrounds you on approach until it pops away; stars only appear after entering). On departure a plane clips half the galaxy in back, and turning reveals a huge 'half' galaxy. Galaxy render on approach/leave needs reevaluation."
  severity: major
  test: 6
  deferred: true
  deferred_to: "render-debt P2 galaxy-visibility-in-universe-space"
  artifacts: []
  missing: []

- truth: "In Universe space, galaxies are selectable/targetable like other bodies (target cycle includes galaxies, target circle pins to them)."
  status: failed
  reason: "User reported: galaxies cannot be targeted in Universe space — root is a valid target and gets a circle, but galaxies are not selectable. Root cause family: galaxies have no render mesh in Universe space (D-28 skips galaxy meshes; sky-shader only), so there is no mesh position for targeting/circle to pin to."
  severity: major
  test: 8
  deferred: true
  deferred_to: "render-debt P2 galaxy-visibility-in-universe-space"
  artifacts: []
  missing: []
