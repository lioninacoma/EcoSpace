---
type: tech-debt
status: pending
created: 2026-06-16
area: rendering
origin: phase-03 debug session galaxy-sky-disc-antipode
tags: [tech-debt, rendering, shader, galaxy]
---

# Galaxy disc tilt / foreshortening (face-on only)

## What

Galaxy sky discs in `Shaders/skybox.gdshader` currently render **face-on**. The
per-galaxy `disc_normal` (authored in `GalaxyOrientation`, pushed via
`galaxy_orientations[i].xyz`) is no longer used for projection — the disc tangent
basis is built from the galaxy *direction* instead.

## Why this exists

During the phase-03 debug session, building the tangent basis from `disc_normal`
collapsed the disc into a 1-D band (a sky-spanning ring) whenever the normal was
≈ world-up — which is exactly how the galaxies are authored (`GalaxyOrientation`
≈ `(0.2, 0.98, 0)`). Switching the basis to the galaxy direction made it robust
and fixed the ring, at the cost of dropping the disc tilt. See commit `fef1d91`
and `.planning/debug/galaxy-sky-disc-antipode.md`.

## What "done" looks like

- Galaxy discs render with a believable 3D tilt (elliptical foreshortening +
  in-plane roll) derived from `disc_normal`, **without** the degenerate collapse
  when `disc_normal` ≈ the galaxy direction or world-up.
- No antipode ghost (keep the `dot(EYEDIR, galaxy_dir) > 0` front-hemisphere gate).
- Verified in-game on approach to a galaxy (the disc should look like a tilted
  disc, not a flat circle and not a band).

## Approach sketch

Build a stable basis from `galaxy_dir`, then foreshorten: project `disc_normal`
into the view plane to get the disc minor-axis direction and compress UV along it
by `clamp(|dot(disc_normal, galaxy_dir)|, floor, 1)`. Guard the near-parallel
case with the floor so it never divides toward zero. Requires visual iteration
(Godot compiles the shader at runtime — `dotnet build` cannot catch it).

## Notes

`galaxy_orientations[i].xyz` is currently pushed but unused by the shader; `.w`
(seed) is still used. Re-wire `.xyz` when implementing tilt.
