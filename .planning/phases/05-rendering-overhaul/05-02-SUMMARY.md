---
phase: 05-rendering-overhaul
plan: 02
subsystem: rendering
tags: [godot, csharp, skybox, gdshader, descriptor-feed, brightness-floor, galaxy-disc-weights, emission-floor]

# Dependency graph
requires:
  - phase: 05-rendering-overhaul
    plan: 01
    provides: "LuminousBodyDescriptor[], LuminousDescriptorBuilder Node (process_priority=-10), LuminousLod curves — the single classify+project source of truth consumed by all drawers"
provides:
  - "SkyboxRenderer refeed: reads LuminousDescriptorBuilder.Descriptors[] instead of running its own classify+project loop — D-07 reversed and confirmed in code"
  - "MinVisibleBrightness=0.05f display-floor constant in SkyboxRenderer — distant stars always findable (closes P1, D-09)"
  - "_galDiscWeights float[] populated from d.LodWeight in galaxy branch; SetShaderParameter galaxy_disc_weights pushed to sky shader (D-13 groundwork)"
  - "_skyDirs cache rebuilt from descriptor directions each frame (RND-07 preserved)"
  - "galaxy_disc_weights uniform in skybox.gdshader, multiplied into galaxy contribution"
  - "process_mode reset to 0 on SkyboxRenderer node in Main.tscn (was 4 = DISABLED — Pitfall 1 fix)"
  - "NearStarEmissionFloor=0.8f constant in WorldRenderer.RenderBodyAt — near star always bright mesh (D-12 missing-sun fix)"
  - "Play-test gate APPROVED 2026-06-19: sun bright up close, distant stars findable, skybox world-fixed and running cleanly"
affects: [05-03, 05-04]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Descriptor-feed pattern: SkyboxRenderer.SyncSkyPoints reads _builder.Descriptors[] read-only; no UniMath/TierClassifier calls inside — eliminates the double classify+project loop (Pitfall 5)"
    - "Display-only brightness floor (MinVisibleBrightness): applied to sky alpha only — StarRendering.ApparentBrightness physics untouched (Pattern 5)"
    - "Emissive floor pattern (NearStarEmissionFloor): Mathf.Max(brightness, floor) on the star mesh emissive multiplier — prevents black-sun at close range (Pitfall 4)"
    - "galaxy_disc_weights groundwork: uniform declared in shader, populated from LodWeight, default PackedFloat32Array in Main.tscn — ready for Plan 3 crossfade (D-13)"

key-files:
  created: []
  modified:
    - Scripts/Render/SkyboxRenderer.cs
    - Shaders/skybox.gdshader
    - Scripts/Render/WorldRenderer.cs
    - Main.tscn

key-decisions:
  - "D-07 REVERSED confirmed in code: SkyboxRenderer is KEPT and is the distant-body drawer; the post-process spatial quad (LuminousPassRenderer) is narrowed to glow/halo only (Plan 3)"
  - "D-09 P1 floor: MinVisibleBrightness=0.05f is an [ASSUMED] play-test calibration knob; APPROVED 2026-06-19 play-test"
  - "D-12 near-star fix: NearStarEmissionFloor=0.8f is an [ASSUMED] play-test calibration knob; APPROVED 2026-06-19 play-test"
  - "D-13 groundwork: galaxy_disc_weights uniform wired (shader + Main.tscn default) but crossfade logic deferred to Plan 3"
  - "SkyboxRenderer.SyncSkyPoints contains NO UniMath.RelativePosition, NO TierClassifier.Classify, NO GameObjects iteration after the refeed — descriptor is the single feed (Pitfall 5)"
  - "StarRendering.ApparentBrightness is unchanged — both MinVisibleBrightness (sky alpha) and NearStarEmissionFloor (mesh emissive) are display-only layers on top of the physics function"

patterns-established:
  - "Read-only descriptor consumer pattern: SkyboxRenderer resolves _builder via BuilderPath export or FindChild fallback in _Ready, reads Descriptors[] in SyncSkyPoints — mirrors LuminousPassRenderer pattern"
  - "Sky-type shader as distant-body drawer: renders at infinite depth behind all geometry automatically; post-process pass (luminous_pass.gdshader) narrowed to screen-space glow only"

requirements-completed: [RND-02, RND-04, RND-05, RND-07]

# Metrics
duration: ~15min
completed: 2026-06-19
status: complete
---

# Phase 5 Plan 02: Rendering Overhaul — Descriptor-fed Skybox + Near-star Brightness Fix Summary

**Descriptor-fed SkyboxRenderer (D-07 reversed) with MinVisibleBrightness=0.05 distant-star floor, galaxy_disc_weights groundwork, and NearStarEmissionFloor=0.8 missing-sun fix — play-test approved 2026-06-19**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-06-19 (continuation after replan)
- **Completed:** 2026-06-19; play-test approved same session
- **Tasks:** 3 (2 auto + 1 checkpoint:human-verify)
- **Files modified:** 4 (SkyboxRenderer.cs, skybox.gdshader, WorldRenderer.cs, Main.tscn)

## Accomplishments

- `SkyboxRenderer.SyncSkyPoints` refeed: replaced the internal classify+project loop (UniMath, TierClassifier, GameObjects) with a read-only loop over `_builder.Descriptors[]` — eliminates the double classify+project, resolves Pitfall 5; `_builder` resolved via `BuilderPath` export or `FindChild` fallback in `_Ready`
- Display-only brightness floor (`MinVisibleBrightness = 0.05f`) on sky-point alpha — distant stars at 1:1 distances are always findable light points regardless of inverse-square flux (closes P1, D-09); `StarRendering.ApparentBrightness` physics unchanged
- `galaxy_disc_weights` groundwork: new `float[MaxGalaxies] _galDiscWeights` in SkyboxRenderer populated from `d.LodWeight`; `SetShaderParameter` push added; `skybox.gdshader` declares `uniform float galaxy_disc_weights[MAX_GALAXIES]` and multiplies it into the galaxy contribution; `ShaderMaterial_sky` `shader_parameter/galaxy_disc_weights` default added to Main.tscn — ready for Plan 3 crossfade (D-13)
- `_skyDirs` cache rebuilt from descriptor directions in the refeed loop (RND-07 preserved; `GetSkyDirection` unchanged)
- `process_mode = 4` removed from `SkyboxRenderer` node in Main.tscn — renderer now runs each frame (was DISABLED; Pitfall 1 fix)
- `WorldRenderer.RenderBodyAt` missing-sun fix: `NearStarEmissionFloor = 0.8f` constant; star emissive block replaced `EmissionEnergyMultiplier = StarRendering.ApparentBrightness(...)` with `Mathf.Max(brightness, NearStarEmissionFloor)` — sun stays visibly bright at close range / inside the SOI where `ApparentBrightness` returns 0 via its `distMeters <= 1e-30` guard; `StarRendering.ApparentBrightness` and `SyncBodies` topology unchanged
- Play-test APPROVED 2026-06-19: sun bright up close, distant stars findable, skybox world-fixed and running cleanly, no regression

## Task Commits

Each task was committed atomically:

1. **Task 1: Refeed SkyboxRenderer from descriptor + galaxy_disc_weights + process_mode reset** - `a8cc821` (feat)
2. **Task 2: NearStarEmissionFloor clamp in WorldRenderer.RenderBodyAt (D-12 missing-sun fix)** - `821c484` (fix)
3. **Task 3: Play-test gate** — human-verify checkpoint APPROVED; no commit (no code written)

## Files Created/Modified

- `Scripts/Render/SkyboxRenderer.cs` — Refeed from `_builder.Descriptors[]`; new `BuilderPath` export + `_builder` field; `MinVisibleBrightness = 0.05f` display floor on star alpha; `_galDiscWeights float[]` field populated from `d.LodWeight`; `SetShaderParameter galaxy_disc_weights` push; `_skyDirs` rebuilt from descriptor directions; no `UniMath.RelativePosition` / `TierClassifier.Classify` / `GameObjects` in `SyncSkyPoints`
- `Shaders/skybox.gdshader` — `uniform float galaxy_disc_weights[MAX_GALAXIES]` declared; galaxy loop final contribution multiplied by `galaxy_disc_weights[i]`
- `Scripts/Render/WorldRenderer.cs` — `NearStarEmissionFloor = 0.8f` constant; star emissive block uses `Mathf.Max(StarRendering.ApparentBrightness(...), NearStarEmissionFloor)`; `SyncBodies` topology and galaxy skip guards unchanged; `StarRendering.ApparentBrightness` unchanged
- `Main.tscn` — `process_mode = 4` removed from `SkyboxRenderer` node; `shader_parameter/galaxy_disc_weights` PackedFloat32Array default added to `ShaderMaterial_sky`; `SkyboxRenderer.BuilderPath` wired to `LuminousDescriptorBuilder` node path

## Decisions Made

- **D-07 REVERSED confirmed in code:** The skybox is the correct tool for distant bodies (renders at infinite depth behind all geometry); `LuminousPassRenderer` (post-process) narrowed to glow/halo only in Plan 3
- **D-09 / P1 floor calibration:** `MinVisibleBrightness = 0.05f` was the assumed play-test knob — play-test gate APPROVED this value 2026-06-19; no change required
- **D-12 emissive floor calibration:** `NearStarEmissionFloor = 0.8f` was the assumed play-test knob — play-test gate APPROVED this value 2026-06-19; no change required
- **D-13 groundwork only:** `galaxy_disc_weights` uniform and push are in place; the actual distance-driven crossfade logic (`LuminousLod.GalaxyDiscWeight` driving the weight per frame) is Plan 3 scope — no premature implementation
- **No second home-galaxy suppression added:** `LuminousDescriptorBuilder` already suppresses ancestor galaxies; adding a duplicate check in `SkyboxRenderer` would be the anti-pattern called out in RESEARCH

## Deviations from Plan

None — plan executed exactly as written. All must-have truths and artifact assertions satisfied:

- `SkyboxRenderer.cs` references `_builder.Descriptors` and `DescriptorCount`, contains `MinVisibleBrightness`, `_galDiscWeights`, and populates `_skyDirs[d.BodyIndex]` from descriptor directions
- `SyncSkyPoints` contains NO `TierClassifier.Classify` and NO `UniMath.RelativePosition` (Pitfall 5 resolved)
- `skybox.gdshader` declares `galaxy_disc_weights` as a uniform float array and the galaxy loop multiplies by `galaxy_disc_weights[i]`
- `Main.tscn` does NOT contain `process_mode = 4` on the SkyboxRenderer node and `ShaderMaterial_sky` contains `shader_parameter/galaxy_disc_weights`
- `WorldRenderer.cs` contains `NearStarEmissionFloor` and the star emissive assignment uses `Mathf.Max(`
- `StarRendering.cs` is unchanged (no edit to `ApparentBrightness`)
- `SyncBodies` galaxy skip guards (`UniObject.Type.Galaxy`) still present
- `dotnet build`: 0 errors, 0 warnings; `dotnet test`: 47/47 green, 0 regression

## Issues Encountered

None — both commits built and tested cleanly. Play-test gate approved without requiring any code changes.

## User Setup Required

None — no external service configuration required.

## Known Stubs

None — no placeholder data paths or wired-but-empty fields. `galaxy_disc_weights` defaults to a 4-element zero array in Main.tscn intentionally (the crossfade logic that drives per-frame values is Plan 3 scope; the zero default means the galaxy contribution is not yet modulated, which is the correct interim state).

## Next Phase Readiness

**Ready for Plan 3 (05-03):** The sky-shader + post-process split is established. The skybox is re-enabled and descriptor-fed. The `galaxy_disc_weights` uniform groundwork is in place for Plan 3 to wire `LuminousLod.GalaxyDiscWeight` per-frame values into the shader push. `LuminousPassRenderer` (committed from the pre-replan 05-02 work) is ready to be narrowed to glow/halo only.

**Play-test calibration knobs confirmed:** `MinVisibleBrightness=0.05f` and `NearStarEmissionFloor=0.8f` passed the play-test gate and do not need tuning before Plan 3.

**Tuning note for Plan 3:** `LuminousLod.GalaxyDiscWeight` thresholds (fade band 0.1*SOI–0.5*SOI) are still `[ASSUMED]` from Plan 1 — Plan 3 play-test will calibrate the galaxy disc crossfade distance.

---
*Phase: 05-rendering-overhaul*
*Completed: 2026-06-19*
