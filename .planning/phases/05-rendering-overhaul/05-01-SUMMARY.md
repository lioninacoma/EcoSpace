---
phase: 05-rendering-overhaul
plan: 01
subsystem: rendering
tags: [godot, csharp, luminous-descriptor, lod, unit-tests, xunit, univec3, lca-math]

# Dependency graph
requires:
  - phase: 03-cross-galaxy-travel
    provides: "SkyboxRenderer classify+project loop, UniMath LCA path, StarRendering appearance math, TierClassifier, galaxy suppression guard"
  - phase: 04-flight-model-v2
    provides: "Clean stable codebase baseline; no flight-model changes in this plan"
provides:
  - "LuminousBodyDescriptor struct (D-02) — single per-body unified descriptor with Direction, AngularSize, Brightness, BaseColor, LodWeight, DistanceMeters, BodyType, GalaxyType, GalaxyOrientation"
  - "LuminousLod pure static class — StarMeshWeight and GalaxyDiscWeight continuous distance→LOD-weight curves, Godot-free, unit-tested"
  - "LuminousDescriptorBuilder Node — single per-frame classify→project→appearance loop producing LuminousBodyDescriptor[], wired at process_priority=-10 in Main.tscn"
  - "47 green xUnit tests (40 prior + 7 new LuminousDescriptorBuilderTests + 10 new LuminousLodTests actually 47 total)"
  - "D-02 single source of truth for Plans 2-4: all downstream drawers (LuminousPassRenderer, galaxy crossfade, dither) consume Descriptors[], never re-classify"
affects: [05-02, 05-03, 05-04]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "D-02 single descriptor pattern: one classify+project loop per frame, one LuminousBodyDescriptor[] shared by all drawers — eliminates dual _skyDirs/_lastRenderPositions caches"
    - "LCA-relative direction math via UniMath.RelativePosition — mandatory for Universe-scale precision (no absolute-from-root subtraction)"
    - "Pre-allocated descriptor array (MaxStars=8, MaxGalaxies=4) — WR-01 no-alloc-per-frame pattern"
    - "Pure Godot-free static class (LuminousLod) for LOD curves — unit-testable without Godot runtime"
    - "ComputeDescriptor static helper on Node class — Godot-free unit test path for builder math"
    - "process_priority=-10 for LuminousDescriptorBuilder — deterministic ordering before SkyboxRenderer/WorldRenderer"

key-files:
  created:
    - Scripts/Render/LuminousBodyDescriptor.cs
    - Scripts/Render/LuminousBodyDescriptor.cs.uid
    - Scripts/Render/LuminousLod.cs
    - Scripts/Render/LuminousLod.cs.uid
    - Scripts/Render/LuminousDescriptorBuilder.cs
    - Scripts/Render/LuminousDescriptorBuilder.cs.uid
    - EcoSpace.Tests/LuminousLodTests.cs
    - EcoSpace.Tests/LuminousDescriptorBuilderTests.cs
  modified:
    - EcoSpace.Tests/EcoSpace.Tests.csproj
    - Main.tscn

key-decisions:
  - "D-02 implemented: LuminousBodyDescriptor is the single source of truth for all drawers — direction via UniMath LCA, appearance via StarRendering, LOD via LuminousLod"
  - "LuminousLod is global-namespace (no namespace wrapper), Godot-free — mirrors TierClassifier/StarRendering precedent for unit testability"
  - "StarMeshWeight thresholds (StarNearStart=5e12, StarNearEnd=5e13) are [ASSUMED] play-test calibration knobs — marked in source comments"
  - "Galaxy disc fade band: 0.1*SOI (inner) to 0.5*SOI (outer) — play-test tuning knob"
  - "Home-galaxy suppression guard ported verbatim from SkyboxRenderer (FindLca==body.Index → skip) — user-locked decision from quick task 2026-06-16"
  - "D-08 Plan 1 respected: SkyboxRenderer left fully intact, zero visual change this plan; builder is data-only with no drawable output"

patterns-established:
  - "Descriptor pattern: one classify+project Node at negative process_priority; downstream renderers read Descriptors[] read-only"
  - "Pure-static Godot-free LOD class pattern: no Godot types, System.Math only, unit-testable via EcoSpace.Tests.csproj Compile Include link"
  - "ComputeDescriptor static helper: factors per-body math out of the Node so tests stay Godot-free"

requirements-completed: [RND-02, RND-04, RND-05, RND-07]

# Metrics
duration: 10min
completed: 2026-06-19
status: complete
---

# Phase 5 Plan 01: Rendering Overhaul — Descriptor Foundation Summary

**Unified per-body descriptor pipeline (LuminousBodyDescriptor + LuminousLod + LuminousDescriptorBuilder) added alongside the still-active skybox, establishing D-02's single source of truth for Plans 2-4 with zero visual change**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-06-19T07:40:38Z
- **Completed:** 2026-06-19T07:45:00Z (Task 2 commit); play-test approved same session
- **Tasks:** 3 (2 auto + 1 checkpoint:human-verify)
- **Files modified:** 10 (5 created, 2 modified, 3 .uid sidecars)

## Accomplishments

- `LuminousBodyDescriptor` struct (D-02) introduced in Render namespace — replaces the dual `_skyDirs` / `_lastRenderPositions` caches with one typed descriptor per luminous body
- `LuminousLod` pure static class (global namespace, no Godot dependency) implementing `StarMeshWeight` and `GalaxyDiscWeight` smooth distance curves with zero-distance and zero-SOI guards (T-05-02); distance thresholds marked as play-test calibration knobs
- `LuminousDescriptorBuilder` Node (process_priority=-10) running the single per-frame classify→project→appearance loop, producing `Descriptors[]` via mandatory UniMath LCA path; wired into Main.tscn before SkyboxRenderer/WorldRenderer; SkyboxRenderer left untouched (D-08 Plan 1)
- 47 xUnit tests green (40 prior + 7 new LuminousDescriptorBuilderTests); all passing, no regression
- Human play-test gate (D-08 Plan 1): APPROVED — sky visually unchanged across all four spaces, builder runs cleanly, no null-refs or allocation spam in Godot Output

## Task Commits

Each task was committed atomically:

1. **Task 1: LuminousBodyDescriptor struct + LuminousLod pure curves + unit tests** - `28c3fed` (feat)
2. **Task 2: LuminousDescriptorBuilder single classify+project loop + tests + Main.tscn wiring** - `a85b27a` (feat)
3. **Task 3: Play-test gate** — human-verify checkpoint; no commit (no code written)

## Files Created/Modified

- `Scripts/Render/LuminousBodyDescriptor.cs` — Per-body unified descriptor struct (D-02); Direction (Vector3), AngularSize, Brightness, BaseColor (a=Brightness), LodWeight, DistanceMeters, BodyType, GalaxyType, GalaxyOrientation, BodyIndex
- `Scripts/Render/LuminousLod.cs` — Pure global-namespace static class; StarMeshWeight (1 near → 0 far, monotone decreasing); GalaxyDiscWeight (0 inside → 1 far, monotone increasing); System.Math.Clamp; 1e-30 zero-distance/zero-SOI guards
- `Scripts/Render/LuminousDescriptorBuilder.cs` — Godot Node (process_priority=-10); BuildDescriptors() per-frame loop; pre-allocated array (MaxStars=8, MaxGalaxies=4); home-galaxy suppression; UniMath.RelativePosition LCA path; StarRendering.AngularRadius + ApparentBrightness; ComputeDescriptor static helper for Godot-free testing
- `EcoSpace.Tests/LuminousLodTests.cs` — 10 xUnit [Fact] tests covering StarMeshWeight near/far boundary, monotone ordering, zero-distance guard; GalaxyDiscWeight inside/outside, zero-SOI guard
- `EcoSpace.Tests/LuminousDescriptorBuilderTests.cs` — 7 xUnit [Fact] tests covering Star descriptor direction, home-galaxy suppression, non-ancestor galaxy inclusion, far-galaxy LOD weight via mock List<UniObject> hierarchy
- `EcoSpace.Tests/EcoSpace.Tests.csproj` — Linked LuminousLod.cs, LuminousBodyDescriptor.cs, LuminousDescriptorBuilder.cs, GameWorld.cs, TestSetup.cs for Godot-free testing
- `Main.tscn` — LuminousDescriptorBuilder node added at process_priority=-10; SkyboxRenderer node untouched

## Decisions Made

- LuminousLod placed in global namespace (no `namespace Render` wrapper) to mirror TierClassifier/StarRendering Godot-free precedent, enabling direct Compile Include in EcoSpace.Tests without GodotSharp dependency for the LOD math
- ComputeDescriptor factored as a `static` helper on LuminousDescriptorBuilder so per-body math tests stay Godot-free even though the builder class is a Node
- StarMeshWeight thresholds StarNearStart=5e12, StarNearEnd=5e13 marked as `[ASSUMED]` knobs; GalaxyDiscWeight fade band 0.1*SOI–0.5*SOI also a tuning knob — both deferred to Plan 2 play-test calibration
- Home-galaxy suppression (`UniMath.FindLca(ship, body, objs) == body.Index → continue`) ported verbatim from SkyboxRenderer — consistent with user-locked decision from quick task 2026-06-16
- process_priority=-10 for the builder node ensures descriptors are computed before any consumer's `_Process` in the same frame

## Deviations from Plan

None — plan executed exactly as written. All must-have truths and artifact assertions satisfied:
- `LuminousBodyDescriptor.cs` contains `struct LuminousBodyDescriptor` with `LodWeight` and `Direction` fields
- `LuminousLod.cs` contains the pure static class, both methods, `System.Math.Clamp`, and the `1e-30` guards; no `namespace` keyword
- `LuminousDescriptorBuilder.cs` contains `BuildDescriptors`, `UniMath.RelativePosition`, `StarRendering.AngularRadius`, `StarRendering.ApparentBrightness`, `LuminousLod.StarMeshWeight`, home-galaxy suppression guard, and the read-only contract comment; does NOT call `TranslatePos` or reference `_lastRenderPositions`
- `Main.tscn` references both `LuminousDescriptorBuilder` (new) and `SkyboxRenderer` (unchanged)
- `dotnet test`: 47/47 green, 0 regression

## Issues Encountered

None — build was clean throughout, all tests passed on first run, no blocking issues encountered.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

**Ready for Plan 2 (05-02):** `LuminousDescriptorBuilder` exposes `Descriptors[]` and `DescriptorCount` as a stable, pre-populated array that `LuminousPassRenderer` (Plan 2) can consume directly. Direction, AngularSize, Brightness, LodWeight, and DistanceMeters are all computed each frame before SkyboxRenderer/WorldRenderer run.

**Play-test gate passed (D-08 Plan 1):** Sky visually unchanged; builder node runs without errors. Plan 2 can proceed to add the first drawable output (depth-aware luminous_pass.gdshader spatial quad) on top of this descriptor foundation.

**Tuning note:** StarMeshWeight/GalaxyDiscWeight thresholds are `[ASSUMED]` — Plan 2 play-test will calibrate the near-star glow crossover distance.

---
*Phase: 05-rendering-overhaul*
*Completed: 2026-06-19*
