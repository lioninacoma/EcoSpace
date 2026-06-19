---
phase: 05-rendering-overhaul
plan: 03
subsystem: rendering
tags: [godot, csharp, gdshader, post-process, near-star-glow, psf, depth-occlusion, lod, galaxy-disc-weight, descriptor-feed]

# Dependency graph
requires:
  - phase: 05-rendering-overhaul
    plan: 01
    provides: "LuminousBodyDescriptor[], LuminousDescriptorBuilder (process_priority=-10), LuminousLod curves — single source of truth for all drawers"
  - phase: 05-rendering-overhaul
    plan: 02
    provides: "SkyboxRenderer descriptor-refeed, galaxy_disc_weights uniform groundwork, MinVisibleBrightness=0.05, NearStarEmissionFloor=0.8 — play-test approved 2026-06-19"
provides:
  - "luminous_pass.gdshader narrowed to near-star PSF glow/halo only — galaxy loop and galaxy uniforms removed (D-13); galaxies are sky-shader-only"
  - "Screen-space aperture-diffraction PSF (inverse-cube core + horizontal/vertical diffraction spikes, aspect-corrected) for near-star lens flare"
  - "PSF LOD ramp: intensity scales with LodWeight via smoothstep(PsfLodFloor, PsfLodFloor+PsfLodRange, lod) — eases in, no pop"
  - "Per-pixel analytic depth occlusion: star view-depth from descriptor RenderDistance (metres / ship.LocalPos.Scale * StarRenderFactor), compared against INV_PROJECTION_MATRIX-reconstructed scene depth — full silhouette occlusion at all angles"
  - "StarRenderFactor (1e-8) as single source of truth in LuminousDescriptorBuilder, aliased by WorldRenderer.StarRenderFactor"
  - "LuminousLod.GalaxyDiscWeight fade band extended to [0.5×SOI, 1.1×SOI] — gradual fade-in across SOI boundary, no pop (D-13)"
  - "Play-test-APPROVED PSF knobs (D-04 2026-06-19): PsfCoreScale=80, PsfSpikeLongScale=12, PsfSpikeShortScale=0.25, PsfIntensity=1.0, PsfLodFloor=0.02, PsfLodRange=0.6, PsfDepthEpsilon=50, caps star=128/galaxy=32"
  - "48/48 tests green; SkyboxRenderer BuilderPath corrected to sibling path ../LuminousDescriptorBuilder"
affects: [05-04]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Observer-unit depth conversion: star render depth = metres / ship.LocalPos.Scale * StarRenderFactor (NOT metres * factor). The ship's LocalPos.Scale is the per-frame observer-unit-to-metre ratio; skipping the division collapses scale entirely and produces wrong depth at every scale except the authored one."
    - "Descriptor RenderDistance field: metres→render-unit conversion pre-computed in LuminousDescriptorBuilder (which reliably has the ship reference) and stored on the descriptor; renderers read it off instead of attempting the conversion themselves (avoids the world-resolution failure mode that zeroed PSF)"
    - "PSF LOD ramp via smoothstep: intensity = smoothstep(PsfLodFloor, PsfLodFloor+PsfLodRange, lod_weight) — small non-zero PsfLodFloor makes the ramp start before LOD=0.0 so faint halo appears before the star is fully in near-star territory; PsfLodRange controls the ease-in band"
    - "StarRenderFactor single source of truth: defined once in LuminousDescriptorBuilder as const float StarRenderFactor = 1e-8f; WorldRenderer.StarRenderFactor aliases it — no magic number duplication across render paths"

key-files:
  created: []
  modified:
    - Shaders/luminous_pass.gdshader
    - Scripts/Render/LuminousPassRenderer.cs
    - Scripts/Render/LuminousDescriptorBuilder.cs
    - Scripts/Render/LuminousLod.cs
    - EcoSpace.Tests/LuminousLodTests.cs
    - Main.tscn

key-decisions:
  - "D-11 three-stage star handoff retained: FAR sky-point → MID point-fades/glow-grows → NEAR sphere mesh + glow; PSF driven by descriptor LodWeight — pop-free by construction"
  - "D-13 galaxies sky-only CONFIRMED in code: galaxy loop and all galaxy uniforms removed from luminous_pass.gdshader; LuminousPassRenderer pushes star arrays only"
  - "Depth-occlusion root cause (5-iteration journey): units mismatch — star render depth MUST be metres / ship.LocalPos.Scale * factor (NOT metres * factor). Secondary regression: LuminousPassRenderer tried to resolve the world itself (Camera3D child, no WorldPath) → zeroed star_count; fixed by pre-computing RenderDistance in the builder."
  - "PSF tuning knobs APPROVED at play-test 2026-06-19: PsfCoreScale=80, PsfSpikeLongScale=12, PsfSpikeShortScale=0.25, PsfIntensity=1.0, PsfLodFloor=0.02, PsfLodRange=0.6, PsfDepthEpsilon=50 (render units), star cap=128, galaxy cap=32"
  - "SkyboxRenderer BuilderPath corrected to ../LuminousDescriptorBuilder (sibling path) — user-found bug masked by FindChild fallback"
  - "GalaxyDiscWeight fade band [0.5×SOI, 1.1×SOI]: weight is low at exactly 1.0×SOI, reaches ~1.0 only past the boundary — removes the single-frame pop at SOI exit"

patterns-established:
  - "Observer-unit render conversion pattern: all metres→render conversions in this engine must go through / ship.LocalPos.Scale * factor; using metres * factor alone is always wrong at non-unity scale"
  - "Build descriptor fields in the builder, not in the renderer: LuminousDescriptorBuilder has reliable ship access; renderers (especially Camera3D children) should read pre-computed values off the descriptor rather than re-deriving them from world state"

requirements-completed: [RND-04, RND-05, RND-07]

# Metrics
duration: ~90min (5-iteration play-test loop)
completed: 2026-06-19
status: complete
---

# Phase 5 Plan 03: Rendering Overhaul — Near-star PSF Glow/Halo + Galaxy Disc Crossfade Summary

**Screen-space aperture-diffraction PSF for near-star glow (per-pixel depth occlusion via observer-unit render depth) + galaxy disc fade-band extended past SOI; 5-iteration depth-gate debug journey; 48/48 tests green; play-test APPROVED 2026-06-19**

## Performance

- **Duration:** ~90 min (5 fix iterations after initial implementation)
- **Started:** 2026-06-19
- **Completed:** 2026-06-19; play-test APPROVED same session
- **Tasks:** 3 plan tasks (2 auto + 1 checkpoint:human-verify) + 5 play-test fix iterations
- **Files modified:** 6 (luminous_pass.gdshader, LuminousPassRenderer.cs, LuminousDescriptorBuilder.cs, LuminousLod.cs, LuminousLodTests.cs, Main.tscn)

## Accomplishments

- `luminous_pass.gdshader` narrowed to near-star PSF glow/halo only: galaxy loop and all galaxy uniforms (`galaxy_dirs`, `galaxy_colors`, `galaxy_sizes`, `galaxy_disc_weights`, `galaxy_types`, `galaxy_orientations`, `galaxy_count`) removed — galaxies are sky-shader-only (D-13). `LuminousPassRenderer` pushes only star arrays.
- Screen-space aperture-diffraction PSF modelled after ShaderToy reference: inverse-cube core glow + horizontal and vertical diffraction spikes, aspect-corrected. Per-channel blowout clamp (8.0) prevents bloom-bleed.
- PSF LOD ramp via `smoothstep(PsfLodFloor, PsfLodFloor+PsfLodRange, lod_weight)` — intensity eases in from zero, no pop at any LOD threshold. `PsfLodRange=0.6` controls the ease-in band width.
- Per-pixel analytic depth occlusion: star render depth computed from `descriptor.RenderDistance` (= `DistanceMetres / ship.LocalPos.Scale * StarRenderFactor`, the observer-unit conversion identical to WorldRenderer's render-position math). Compared per-pixel against the `INV_PROJECTION_MATRIX`-reconstructed scene depth so a foreground planet's full silhouette occludes the flare at all viewing distances and angles.
- `StarRenderFactor = 1e-8f` declared once in `LuminousDescriptorBuilder` and aliased by `WorldRenderer.StarRenderFactor` — single source of truth, no duplication.
- `LuminousLod.GalaxyDiscWeight` fade band extended to `[0.5×SOI, 1.1×SOI]`: weight is low at exactly the SOI boundary, reaches ~1.0 only outside — eliminates the single-frame full-opacity pop when the ship exits a galaxy SOI (D-13, RESEARCH Pitfall 2).
- One new test: `GalaxyDiscWeight_AtSoiBoundary_IsLow` — asserts weight < 0.5 at exactly 1.0×SOI. 48/48 tests green.
- Play-test-approved PSF [Export] knobs on `LuminousPassRenderer`: `PsfCoreScale=80`, `PsfSpikeLongScale=12`, `PsfSpikeShortScale=0.25`, `PsfIntensity=1.0`, `PsfLodFloor=0.02`, `PsfLodRange=0.6`, `PsfDepthEpsilon=50` (render units).
- SkyboxRenderer `BuilderPath` corrected to `"../LuminousDescriptorBuilder"` (sibling node path) — user-found bug that was silently masked by the `FindChild` fallback.

## Task Commits

Each task was committed atomically:

1. **Task 1: Narrow luminous_pass.gdshader to near-star glow/halo; remove galaxy loop** - `0e95df6` (feat)
2. **Task 2: Extend GalaxyDiscWeight fade band past SOI boundary (D-13)** - `3655ee7` (fix)
3. **Task 3: Play-test gate — iteration 1 (raise caps 128/32 + BuilderPath fix)** - `a10306c` (fix)
4. **Task 3: Play-test gate — iteration 2 (depth-gated near-star PSF with aperture diffraction)** - `71c592a` (fix)
5. **Task 3: Play-test gate — iteration 3 (widen PSF LOD ramp + depth-texture star-depth attempt)** - `aea5746` (fix)
6. **Task 3: Play-test gate — iteration 4 (per-pixel analytic star-depth occlusion)** - `b07e01e` (fix)
7. **Task 3: Play-test gate — iteration 5 (apply observer-unit scale divisor to star_view_dists)** - `7dca6c8` (fix)
8. **Task 3: Play-test gate — iteration 6 (source star render-distance from descriptor)** - `636aaa5` (fix)

**Play-test checkpoint:** APPROVED 2026-06-19 — no code commit (no source change written)

## Files Created/Modified

- `Shaders/luminous_pass.gdshader` — Galaxy loop + galaxy uniforms removed; near-star PSF (inverse-cube core + diffraction spikes, aspect-corrected); PSF LOD ramp via `smoothstep`; per-pixel analytic depth occlusion using `star_view_dists[]` compared against `INV_PROJECTION_MATRIX`-reconstructed scene depth; per-channel blowout clamp; `world_view_dir` via `INV_VIEW_MATRIX` preserved; `blend_add` render mode preserved
- `Scripts/Render/LuminousPassRenderer.cs` — Galaxy arrays and push block removed; star-only descriptor push; `[Export]` PSF knobs (`PsfCoreScale`, `PsfSpikeLongScale`, `PsfSpikeShortScale`, `PsfIntensity`, `PsfLodFloor`, `PsfLodRange`, `PsfDepthEpsilon`); `star_view_dists[]` populated from `descriptor.RenderDistance`; caps raised to 128 stars / 32 galaxies
- `Scripts/Render/LuminousDescriptorBuilder.cs` — `StarRenderFactor = 1e-8f` const declared; `RenderDistance` field pre-computed per star descriptor (= `DistanceMetres / ship.LocalPos.Scale * StarRenderFactor`); `WorldRenderer.StarRenderFactor` aliased to this const
- `Scripts/Render/LuminousLod.cs` — `GalaxyDiscWeight` fade band changed from `[0.1×SOI, 0.5×SOI]` to `[0.5×SOI, 1.1×SOI]`; band constants marked as `[ASSUMED] play-test knobs` (RESEARCH Assumptions Log A4)
- `EcoSpace.Tests/LuminousLodTests.cs` — Existing `GalaxyDiscWeight` band facts updated; new `GalaxyDiscWeight_AtSoiBoundary_IsLow` fact added asserting weight < 0.5 at exactly 1.0×SOI
- `Main.tscn` — PSF [Export] defaults wired on `LuminousPassRenderer` node; `SkyboxRenderer.BuilderPath` corrected to `../LuminousDescriptorBuilder`

## Decisions Made

- **D-11 three-stage star handoff confirmed:** PSF glow driven by `LodWeight` — `lod_fade = smoothstep(PsfLodFloor, PsfLodFloor+PsfLodRange, lod_weight)` eases in before `LodWeight` reaches 1.0; reaches effective zero at `LodWeight=1` so handoff to the mesh is pop-free by construction.
- **D-13 galaxies sky-only finalized:** Galaxy loop and all galaxy uniforms removed from both shader and renderer. This is confirmed, non-reversible architecture.
- **Depth occlusion approved:** Per-pixel analytic approach (comparing star view-depth from descriptor against INV_PROJECTION_MATRIX scene depth per pixel) APPROVED over single-point depth sample — full-silhouette occlusion at all viewing angles.
- **PSF knobs play-test approved (D-04):** `PsfCoreScale=80`, `PsfSpikeLongScale=12`, `PsfSpikeShortScale=0.25`, `PsfIntensity=1.0`, `PsfLodFloor=0.02`, `PsfLodRange=0.6`, `PsfDepthEpsilon=50`, star cap=128, galaxy cap=32. These are the canonical starting values for Plan 4.
- **GalaxyDiscWeight band [0.5×SOI, 1.1×SOI] approved at play-test:** Galaxy disc fades in gradually across the SOI boundary — no pop observed.

## Deviations from Plan

### Auto-fixed Issues (Play-test Iterations)

**1. [Rule 1 - Bug] 5-iteration depth-gate debugging journey**
- **Found during:** Task 3 play-test (iteration 1 through 6)
- **Root cause A — units mismatch:** Star render depth was computed as `distMetres * StarRenderFactor`. This is wrong. The correct conversion is `distMetres / ship.LocalPos.Scale * StarRenderFactor` — identical to the WorldRenderer `renderPos` computation. `ship.LocalPos.Scale` is the observer-unit-to-metre ratio; omitting the division collapses scale entirely, producing depth values orders of magnitude too large, so the flare was never occluded.
- **Root cause B — world resolution failure:** `LuminousPassRenderer` (a Camera3D child) attempted to resolve the world node itself via a world path. Camera3D children have no `WorldPath`; the resolution returned null, `star_count` was set to 0, and PSF never drew. Fixed by pre-computing `RenderDistance` in `LuminousDescriptorBuilder` (which reliably holds a ship reference) and reading it off the descriptor.
- **Fix progression:** iteration 1 raised caps+fixed BuilderPath (regression masked), iteration 2 added depth gate + PSF, iteration 3 widened LOD ramp + attempted depth-texture sample, iteration 4 switched to per-pixel analytic depth, iteration 5 added observer-unit scale divisor, iteration 6 moved RenderDistance computation to the builder.
- **Files modified:** `Shaders/luminous_pass.gdshader`, `Scripts/Render/LuminousPassRenderer.cs`, `Scripts/Render/LuminousDescriptorBuilder.cs`
- **Commits:** `71c592a`, `aea5746`, `b07e01e`, `7dca6c8`, `636aaa5`

**2. [Rule 2 - Missing Critical] StarRenderFactor single source of truth**
- **Found during:** Depth-occlusion fix work — `StarRenderFactor` was duplicated as a magic number in multiple places
- **Fix:** Declared once as `const float StarRenderFactor = 1e-8f` in `LuminousDescriptorBuilder`; `WorldRenderer.StarRenderFactor` aliased to it
- **Files modified:** `Scripts/Render/LuminousDescriptorBuilder.cs`, `Scripts/Render/WorldRenderer.cs`
- **Committed in:** `7dca6c8` / `636aaa5`

**3. [Rule 1 - Bug] SkyboxRenderer BuilderPath incorrect (user-found)**
- **Found during:** Task 3 play-test (iteration 1) by the user
- **Issue:** `BuilderPath` was set to `"LuminousDescriptorBuilder"` (child-search syntax) but `SkyboxRenderer` and `LuminousDescriptorBuilder` are siblings in the scene tree, not parent/child. `FindChild` fallback silently masked the misconfiguration.
- **Fix:** Corrected to `"../LuminousDescriptorBuilder"` (sibling path)
- **Files modified:** `Main.tscn`
- **Committed in:** `a10306c`

---

**Total deviations:** 3 auto-fixed (2 Rule 1 bugs, 1 Rule 2 missing-critical)
**Impact on plan:** All fixes necessary for correctness. Root causes were units-mismatch in depth conversion (systematic engine rule) and a world-resolution architectural mismatch (Camera3D child cannot resolve world). No scope creep.

## Key Lesson: Observer-Unit Render Conversion

**Any metres→render conversion in this engine MUST go through `/ ship.LocalPos.Scale * factor`; never `metres * factor` alone.**

`ship.LocalPos.Scale` is the observer-unit-to-metre ratio for the current frame. Omitting the division always produces wrong render depths/distances at non-unity scale. This is the same conversion `WorldRenderer.ComputeStarRenderPosFromHierarchy` uses for render positions — the render depth must use the identical formula to be comparable with the rendered geometry.

Rule of thumb: if your render conversion disagrees with `WorldRenderer`, it is wrong.

## Issues Encountered

The depth-occlusion implementation required 5 fix iterations over the play-test loop. Both root causes are systematic:
1. **Units mismatch** — `metres * factor` vs `metres / scale * factor`: both return a `float`, neither raises a compile error; the bug is invisible until tested at non-authored scale.
2. **World-resolution failure** — `LuminousPassRenderer` is a Camera3D child; resolving the world from that context returns null; `star_count` silently becomes 0. The architectural fix (pre-compute in builder, read off descriptor) is the correct long-term pattern and has been established for Plan 4.

## User Setup Required

None — no external service configuration required.

## Known Stubs

None — PSF knobs are play-test-approved values, not placeholders. `RenderDistance` is computed per-frame from live descriptor data.

## Next Phase Readiness

**Ready for Plan 4 (05-04):** The near-star PSF is implemented and play-test approved with calibrated knobs. The galaxy disc crossfade is in place (LodWeight-driven fade band). `LuminousPassRenderer` pushes star arrays only. The observer-unit depth conversion is established as a named pattern.

**Plan 4 scope reminder (from 05-03-PLAN.md deferred items):** HDR-before-dither composition ordering; `WorldRenderer._lastRenderPositions` dead-code removal.

**Calibration state:** All PSF knobs are APPROVED for Plan 4. No re-tuning needed before 05-04 unless play-test of Plan 4 visual changes conflicts.

---
*Phase: 05-rendering-overhaul*
*Completed: 2026-06-19*
