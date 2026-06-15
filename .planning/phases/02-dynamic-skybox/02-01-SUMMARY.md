---
phase: 02-dynamic-skybox
plan: "01"
subsystem: skybox
tags: [skybox, rendering, tier-classification, gdshader, sibling-stars]
dependency_graph:
  requires:
    - 01-in-system-flight-mvp (WorldRenderer, floating-origin, dithering pipeline)
  provides:
    - TierClassifier (pure C# enum + static Classify method)
    - SkyboxRenderer (read-only per-frame sky uniform push)
    - skybox.gdshader (shader_type sky, EYEDIR world-space disc rendering)
    - UniObject.Luminosity field (D-26)
    - 3 sibling star systems in TestSetup (D-23)
  affects:
    - 02-02 (magnitude model builds on SkyboxRenderer flat placeholder)
    - 02-03 (TierClassifier unit tests, Godot-free)
tech_stack:
  added:
    - "Shaders/skybox.gdshader — shader_type sky with EYEDIR world-space projection"
    - "Scripts/Render/SkyboxRenderer.cs — read-only Node renderer in namespace Render"
    - "Scripts/TierClassifier.cs — Godot-free pure C# tier classifier"
  patterns:
    - "Double-precision hierarchy walk (AbsolutePositionInRoot) for galaxy-scale direction vectors"
    - "Fixed-size GLSL array uniforms with star_count control variable"
    - "TierClassifier.Classify drives sky vs mesh partitioning deterministically"
key_files:
  created:
    - Scripts/TierClassifier.cs
    - Scripts/Render/SkyboxRenderer.cs
    - Shaders/skybox.gdshader
  modified:
    - Scripts/UniObject.cs (added Luminosity field, D-26)
    - Scripts/TestSetup.cs (added 3 sibling star systems, D-23; Luminosity on home star)
    - Main.tscn (background_mode 1→2 Sky; ShaderMaterial+Sky sub-resources; SkyboxRenderer node)
decisions:
  - "Flat placeholder brightness (alpha=1.0, size=0.002f) used for Plan 01; Plan 02 adds the real magnitude model"
  - "AbsolutePositionInRoot uses double precision throughout, cast to Vector3 only after normalizing"
  - "SkyboxRenderer uses Camera3D.Environment.Sky.SkyMaterial path to obtain ShaderMaterial"
  - "Main.tscn references SkyboxRenderer.cs and skybox.gdshader without UIDs (Godot path-only ref)"
  - "Sibling stars authored at true interstellar distances in Galaxy space (1 unit = 1e4 m)"
metrics:
  duration: "6m 29s"
  completed: "2026-06-15"
  tasks_completed: 2
  tasks_total: 3
  files_created: 3
  files_modified: 3
---

# Phase 02 Plan 01: Dynamic Skybox Vertical Slice Summary

**One-liner:** End-to-end dynamic skybox vertical slice — TierClassifier classifies sibling stars as NextTierSkybox; SkyboxRenderer pushes EYEDIR world-space directions to skybox.gdshader each frame; dithered via existing CanvasLayer post-process.

## What Was Built

This plan delivers the first end-to-end vertical slice of the dynamic skybox:

1. **`UniObject.Luminosity`** — new `public double Luminosity = 1.0` field added after `RadiusMeters` in `Scripts/UniObject.cs`, with solar-luminosity-units documentation (D-26).

2. **`TierClassifier` + `SkyTier`** — new `Scripts/TierClassifier.cs` in the global namespace with NO `using Godot`. Declares `enum SkyTier { Skip, CurrentTierMesh, NextTierSkybox, Beyond }` and `public static class TierClassifier` with `Classify(UniObject body, UniObject ship)`. Classification: null/self/Root → Skip; `body.CurrentSpace == ship.CurrentSpace` → CurrentTierMesh; `body.CurrentSpace == ParentSpace(ship.CurrentSpace)` → NextTierSkybox; else → Beyond.

3. **3 sibling star systems** — added to `Scripts/TestSetup.cs` under `_galaxy` in Galaxy space:
   - `ALPHA CEN`: 3.97e12 Galaxy units (+X), G-type warm white `Color(1.0, 0.92, 0.70)`, Luminosity=1.519
   - `BARNARD`: 5.63e12 Galaxy units (+X), M-dwarf red `Color(1.0, 0.30, 0.15)`, Luminosity=0.0035
   - `SIRIUS`: `(-6.0e12, 0, 5.6e12)` Galaxy units, A-type blue-white `Color(0.70, 0.85, 1.0)`, Luminosity=25.4
   
   All at realistic interstellar distances with varied colors/luminosities (D-23). Home STAR also given `Luminosity = 1.0`.

4. **`skybox.gdshader`** — `shader_type sky` (no render_mode), `const int MAX_STARS = 8`, fixed-size array uniforms `star_dirs[MAX_STARS]`/`star_colors[MAX_STARS] : source_color`/`star_sizes[MAX_STARS]`, `int star_count = 0`. `void sky()` loops up to `star_count`, accumulates `dot(EYEDIR, star_dirs[i])` smoothstep disc, writes to `COLOR`. No RADIANCE sampling (Godot 4.6 regression #115441 avoidance). Extensive header comment documents EYEDIR world-space, DO-NOT-RADIANCE warning, dither integration, and bloom.

5. **`SkyboxRenderer`** — `public partial class SkyboxRenderer : Node` in `namespace Render`. Export `NodePath WorldPath`. `_Ready`: resolves `_world` via WorldRenderer pattern; obtains `_skyMat` via `Camera3D.Environment.Sky.SkyMaterial`. `_Process`: calls `SyncSkyPoints()`. Per-frame: bounds-check ship; loop all bodies; classify via `TierClassifier.Classify`; for NextTierSkybox bodies, compute direction via `AbsolutePositionInRoot` double-precision hierarchy walk; push flat brightness placeholder (`alpha=1.0f`, `size=0.002f`); push `star_count`/`star_dirs`/`star_colors`/`star_sizes` via `SetShaderParameter`. Strictly read-only (T-02-01, T-02-02 mitigations applied).

6. **`Main.tscn`** wiring: `background_mode` changed `1` → `2` (Sky); added `ShaderMaterial_sky` sub-resource with `shader = skybox.gdshader`; added `Sky_main` sub-resource with `sky_material = ShaderMaterial_sky`; `Environment_r0du0` now has `sky = Sky_main`; `glow_enabled = true` preserved (D-20); added `SkyboxRenderer` node as child of `Main` with `WorldPath = NodePath("..")`.

## Ship Space and Visible Stars

When the ship is in **Planet space or Star space**, `TierClassifier.Classify` returns `NextTierSkybox` for all three sibling bodies (ALPHA CEN, BARNARD, SIRIUS) because their `CurrentSpace == Galaxy == ParentSpace(ship.CurrentSpace)` for both Star and Planet spaces.

The sky points should be visible from **Star space** and **Planet space** — both qualify. The sibling stars are NOT visible from Galaxy space (they'd be `CurrentTierMesh` there) or from Universe space (they'd be `Beyond`).

## Sibling Star Authored Data Summary

| Name      | Galaxy Coords (units)        | Color RGB         | Luminosity |
|-----------|------------------------------|-------------------|------------|
| ALPHA CEN | (3.97e12, 0, 0)              | (1.0, 0.92, 0.70) | 1.519      |
| BARNARD   | (5.63e12, 0, 0)              | (1.0, 0.30, 0.15) | 0.0035     |
| SIRIUS    | (-6.0e12, 0, 5.6e12)         | (0.70, 0.85, 1.0) | 25.4       |

Galaxy scale: 1 unit = 10 000 m.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| Task 1 | 8480f16 | feat(02-01): Luminosity field, sibling star test data, TierClassifier |
| Task 2 | f76b1ad | feat(02-01): skybox.gdshader, SkyboxRenderer, Main.tscn Sky wiring |

## Deviations from Plan

### Plan Instructions vs Implementation

**1. [Rule 2 - Enhancement] Flat brightness placeholder explicitly documented**

- **Found during:** Task 2 implementation
- **Issue:** Plan says "use a uniform placeholder brightness/size" for Plan 01, with the real magnitude model in Plan 02. Implemented exactly as specified — alpha=1.0, size=0.002f constant per body.
- **Impact:** All 3 sibling stars appear with equal brightness in Plan 01; Plan 02 adds the D-17 inverse-square model with D-19 min-floor.

None beyond the above — plan executed exactly as written. All acceptance criteria met for Tasks 1 and 2.

## Checkpoint: Task 3 (human-verify) — Awaiting Verification

**Status:** BLOCKED at checkpoint:human-verify (gate="blocking")

Tasks 1 and 2 complete. Task 3 requires runtime verification in Godot 4.6.2. This checkpoint also validates the three Wave-0 smoke risks from research:

- **Open Question 1 (A5):** Does `hint_screen_texture` capture sky output in DX12/Forward+? (dither integration D-27)
- **Open Question 2:** Does EYEDIR regression affect non-RADIANCE sky shaders? (world-fixed projection)
- **Open Question 3 (A7):** Does C# `Color[]` → `uniform vec4 : source_color` work in Godot 4.6.2 Mono?

## Known Stubs

- **SkyboxRenderer brightness model** (`Scripts/Render/SkyboxRenderer.cs`, SyncSkyPoints, lines with `_colors[count]` and `_sizes[count]`): flat placeholder `alpha=1.0f`, `size=0.002f` — Plan 02 replaces with the D-17/D-19 magnitude model. This is intentional; Plan 01's goal is to prove the end-to-end mechanism first.

## Threat Surface Scan

No new network endpoints, auth paths, or file access patterns introduced. This is a single-player offline rendering subsystem with no external trust boundaries. Threat register mitigations T-02-01 and T-02-02 applied:
- T-02-01: `star_count` clamped to `[0, MaxStars]` by loop guard in SyncSkyPoints; shader loops only `i < star_count`.
- T-02-02: `AbsolutePositionInRoot` loop guarded by `(uint)pIdx < (uint)objs.Count && objs[pIdx] != null`; caller guards coincident positions with `len < 1e-30` fallback.

## Self-Check: PASSED

| Item | Status |
|------|--------|
| Scripts/UniObject.cs | FOUND |
| Scripts/TierClassifier.cs | FOUND |
| Scripts/Render/SkyboxRenderer.cs | FOUND |
| Shaders/skybox.gdshader | FOUND |
| Main.tscn | FOUND |
| Scripts/TestSetup.cs | FOUND |
| Commit 8480f16 (Task 1) | FOUND |
| Commit f76b1ad (Task 2) | FOUND |
| Luminosity field in UniObject.cs | FOUND |
| SkyTier enum in TierClassifier.cs | FOUND |
| TierClassifier has no `using Godot` | PASS |
| ALPHA CEN / BARNARD / SIRIUS siblings | FOUND |
| shader_type sky in skybox.gdshader | FOUND |
| background_mode = 2 in Main.tscn | FOUND |
