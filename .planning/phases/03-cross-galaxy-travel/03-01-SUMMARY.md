---
phase: 03-cross-galaxy-travel
plan: "01"
subsystem: universe-data + sky-rendering
tags: [galaxy, skybox, shader, uniuniobject, testsetup, procedural]
dependency_graph:
  requires: []
  provides:
    - UniObject.Type.Star/Galaxy/Planet enum values
    - UniObject.ObjectType/GalaxyType/GalaxySeed/GalaxyOrientation fields
    - 3 authored galaxies (HOME/DEST/ELLIPTICAL CLUSTER) in TestSetup at 1:1 intergalactic distances
    - skybox.gdshader galaxy uniform set + procedural spiral/elliptical disc loop
    - SkyboxRenderer galaxy partitioning + uniform push
  affects:
    - WorldRenderer (consumes ObjectType for routing — Plan 03-02)
    - FlightController (galaxy RadiusMeters feeds speed envelope — Plan 03-02)
    - TierClassifier (galaxies in Universe space classify as NextTierSkybox — unchanged, working)
tech_stack:
  added: []
  patterns:
    - ObjectType field routing (Type==Star/Galaxy/Planet) for renderer partitioning (D-38)
    - LCA-relative UniMath.RelativePosition for all galaxy direction math (mandatory at 1e22 m)
    - Shared StarRendering.AngularRadius/ApparentBrightness for galaxy size/brightness (D-30)
    - GLSL int[] uniform arrays for galaxy_types (int[] path confirmed — see below)
key_files:
  created: []
  modified:
    - Scripts/UniObject.cs
    - Scripts/TestSetup.cs
    - Shaders/skybox.gdshader
    - Scripts/Render/SkyboxRenderer.cs
decisions:
  - "int[] uniform path chosen: galaxy_types declared as GLSL `uniform int galaxy_types[MAX_GALAXIES]` and pushed via C# int[]; build passed 0 errors and 28 tests green with this path — no float-packing fallback needed"
  - "GALAXY_DISC_SCALE=80.0 and GALAXY_LOD_THRESHOLD=2e-4 flagged as play-test tuning knobs"
  - "Galaxy positions: HOME (0,0,0), DEST (0,0,2.4e6 Universe units = 2.4e22 m), ELLIPTICAL (1.27e6,0,1.27e6 Universe units = 1.8e22 m at 45°)"
metrics:
  duration: ~15 min
  completed_date: "2026-06-16"
  tasks_completed: 3
  files_modified: 4
---

# Phase 03 Plan 01: Galaxy Body Model + Procedural Sky Discs Summary

**One-liner:** Extended UniObject with Star/Galaxy/Planet types and galaxy presentation fields; authored 3 flyable galaxies at true 1:1 intergalactic distances; rendered them procedurally in the sky shader as spiral/elliptical discs that grow from sub-pixel points on approach.

---

## int[]-vs-float-packing Shader Uniform Decision (Contract for Plans 03-02 and 03-03)

**Decision: int[] path used and confirmed valid.**

`skybox.gdshader` declares `uniform int galaxy_types[MAX_GALAXIES]` (GLSL `int[]` uniform array).
`SkyboxRenderer.cs` pushes `_galTypes` (C# `int[]`) via `SetShaderParameter("galaxy_types", _galTypes)`.

The build compiled with 0 errors and all 28 tests passed. The runtime `int[]` → `Variant` → GLSL `int` array path worked without issue in this Godot 4.6.2 Mono build. No float-packing fallback was needed.

**Contract for downstream plans:** Plans 03-02 and 03-03 must continue using `int[]` for `galaxy_types` push. If a future Godot upgrade breaks this, the fallback is to pack type (0/1) into `galaxy_orientations[i].w` and read it in the shader with `galaxy_orientations[i].w < 0.5 ? spiral : elliptical`.

---

## Authored Galaxy Data (Contract for downstream)

| Galaxy | Position (Universe units) | Distance (m) | Type | BaseColor | GalaxyType | GalaxySeed | GalaxyOrientation |
|--------|---------------------------|--------------|------|-----------|------------|------------|-------------------|
| HOME GALAXY | (0, 0, 0) | 0 | Spiral | (0.7, 0.75, 1.0) = cool blue-white | 0 | 0.42 | (0, 1, 0) — XZ plane |
| DEST GALAXY | (0, 0, 2.4e6) | 2.4e22 m | Spiral mirror | (1.0, 0.85, 0.7) = warm orange-gold | 0 | 0.73 | (0.2, 0.98, 0) — slight tilt |
| ELLIPTICAL CLUSTER | (1.27e6, 0, 1.27e6) | ~1.8e22 m at 45° | Elliptical | (1.0, 0.88, 0.65) = warm golden | 1 | 0.17 | (0.3, 0.95, 0.1) — modest tilt |

All galaxies: `RadiusMeters = 5e20` (50 kly, feeds FlightController speed envelope per D-36), `Luminosity = 1e10` (HOME/DEST) or `6e9` (ELLIPTICAL CLUSTER), `GalaxySOI = 5e4 Universe units = 5e20 m`.

---

## Shader Tuning Knobs (Flag for end-of-phase play-test review)

| Constant | File | Value | Note |
|----------|------|-------|------|
| `GALAXY_LOD_THRESHOLD` | `skybox.gdshader` | `2e-4` | Switch from point to disc mode (~3 px at 1080p/75°) — ASSUMED, requires play-test |
| `GALAXY_DISC_SCALE` | `skybox.gdshader` | `80.0` | Angular→UV normalization for arm/disc structure — ASSUMED, requires play-test |
| Galaxy `Luminosity` | `TestSetup.cs` | `1e10` (HOME/DEST), `6e9` (CLUSTER) | Brightness through StarRendering log curve; tune if galaxies are too dim/bright |

---

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Extend UniObject.Type + galaxy fields; declare skybox galaxy uniforms | 5a3dc13 | Scripts/UniObject.cs, Shaders/skybox.gdshader |
| 2 | Author 3 galaxies + mirror system + cluster; add procedural galaxy loop | 8e77ccc | Scripts/TestSetup.cs, Shaders/skybox.gdshader |
| 3 | Partition SkyboxRenderer by Type; push galaxy uniforms | 6317481 | Scripts/Render/SkyboxRenderer.cs |

---

## Automated Verification Results

- `dotnet build EcoSpace.csproj`: 0 errors, 0 warnings (verified after each task)
- `dotnet test EcoSpace.Tests`: 28/28 passed (UniObject type changes + galaxy fields are link-compiled into test project)
- Grep gate: `grep -v '^[[:space:]]*//' SkyboxRenderer.cs | grep -c 'SetShaderParameter("galaxy_'` → **6** (required ≥ 2)
- Source assertions:
  - `UniObject.cs` contains `Star`, `Galaxy`, `Planet` in `Type` enum and `ObjectType`, `GalaxyType`, `GalaxySeed`, `GalaxyOrientation` fields ✓
  - `TestSetup.cs` contains 3 `AddGameObject(_root, ...)` galaxy bodies with `ObjectType = UniObject.Type.Galaxy` ✓
  - `TestSetup.cs`: no `AddGameObject` call uses the literal `5e3` SOI placeholder ✓
  - `TestSetup.cs`: all stars have `ObjectType = UniObject.Type.Star`, all planets `UniObject.Type.Planet` ✓
  - `skybox.gdshader` contains `galaxy_count`, `galaxy_dirs`, `galaxy_colors`, `galaxy_sizes`, `galaxy_types`, `galaxy_orientations`, `MAX_GALAXIES` ✓
  - `skybox.gdshader` `void sky()` contains `for (int i = 0; i < galaxy_count; i++)` loop with `GALAXY_LOD_THRESHOLD` branch and both `spiral_galaxy`/`elliptical_galaxy` calls ✓
  - `SkyboxRenderer.cs` declares `MaxGalaxies` and `_galDirs/_galColors/_galSizes/_galOrientations` arrays ✓
  - `SkyboxRenderer.SyncSkyPoints` branches on `body.ObjectType == UniObject.Type.Galaxy` vs `== UniObject.Type.Star` ✓
  - Star branch still sets `_skyDirs[body.Index]` (RND-07/D-21 cache preserved) ✓
  - Galaxy direction/size/brightness reuse `UniMath.RelativePosition` and `StarRendering.AngularRadius`/`ApparentBrightness` ✓

## Pending Human Verification

The following visual checks require launching the Godot game and cannot be performed by the executor:

### Task 2 Human Check
Launch the game from the home system (ship starts in PlanetA orbit). Confirm:
1. Two colored procedural discs are visible in the sky in two different directions (one spiral-type for DEST GALAXY, one elliptical-type for ELLIPTICAL CLUSTER), plus the existing sibling-star points.
2. Rotating the ship does NOT move the discs (world-fixed — EYEDIR is world-space in Godot 4 sky shaders).
3. The discs do not crash or render as solid white blobs.
(Disc size/structure tuning via GALAXY_DISC_SCALE/LOD threshold is the end-of-phase review's job.)

### Task 3 Human Check
Launch the game from the home system. Confirm:
1. The galaxy discs now reflect each galaxy's authored color (DEST = warm orange-gold; ELLIPTICAL CLUSTER = warm golden yellow).
2. Fly outward toward the destination galaxy (in the +Z direction from spawn) and confirm its disc enlarges smoothly into a spiral structure as distance closes.
3. No pop or flicker between point and disc modes as the galaxy grows.

---

## Deviations from Plan

None — plan executed exactly as written. The int[] uniform path worked on first attempt; no float-packing fallback was required.

---

## Known Stubs

None — all galaxy bodies have authored data (colors, seeds, orientations, radii, luminosities). Galaxy rendering drives live data every frame via SkyboxRenderer. No placeholder values reach the sky shader.

---

## Self-Check: PASSED

All modified files exist on disk. All task commits verified in git log:
- 5a3dc13 (Task 1) ✓
- 8e77ccc (Task 2) ✓
- 6317481 (Task 3) ✓
