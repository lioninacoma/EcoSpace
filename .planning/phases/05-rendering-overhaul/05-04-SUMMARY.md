---
phase: 05-rendering-overhaul
plan: 04
subsystem: rendering
tags: [godot, csharp, gdshader, post-process, hdr-composition, dither, dead-code-removal, descriptor-feed, phase-final]

# Dependency graph
requires:
  - phase: 05-rendering-overhaul
    plan: 01
    provides: "LuminousBodyDescriptor[] + LuminousDescriptorBuilder + LuminousLod — single source of truth (Direction) for all drawers"
  - phase: 05-rendering-overhaul
    plan: 02
    provides: "SkyboxRenderer descriptor-refeed, MinVisibleBrightness floor, NearStarEmissionFloor=0.8"
  - phase: 05-rendering-overhaul
    plan: 03
    provides: "luminous_pass.gdshader narrowed to near-star PSF (galaxy loop/uniforms removed, D-13); observer-unit depth occlusion; play-test-approved PSF knobs (D-04)"
provides:
  - "D-05 composition ordering verified/locked: sky shader (behind geometry) → 3D opaque meshes → luminous_pass blend_add transparent quad → WorldEnvironment glow → CanvasLayer dither LAST — one unified 8-bit palette edge-to-edge, no banding seam"
  - "Dead WorldRenderer._lastRenderPositions cache removed: field + Clear() + end-of-frame copy loop gone; descriptor.Direction is the single source (D-02 cleanup)"
  - "GetRenderPosition rewritten as a thin cache-free accessor reading the live mesh transform (_meshes[bodyIdx].Position, gated on mesh.Visible); GetRenderRadius unchanged — Hud._Draw target circle (D-46) preserved, no regression"
  - "Confirmed no stale galaxy uniforms/push remain in LuminousPassRenderer (Plan-3 cleanup verified) — galaxies are sky-shader-only (D-13)"
  - "RenderFactorFor, NearStarEmissionFloor, galaxy skip guard, floating-origin sync, 1:1 unit-space render preserved (RND-06, RND-03, D-28) — no regression"
  - "Final per-tier parity/improvement play-test PASSED across all four spaces (Planet/Star/Galaxy/Universe); CRT confirmed OUT (D-06)"
  - "Phase 5 Rendering Overhaul COMPLETE — 4/4 plans done"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Composition ordering for the 8-bit look: additive near-star glow composes in the 3D transparent pass (Camera3D-child blend_add quad) so the canvas_item dither quantizes the fully-composited HDR frame once, edge-to-edge — the dither shader stays a pure canvas_item quantizer with no depth hint (Godot bug #74464)"
    - "Cache-free read-only accessor: GetRenderPosition reads the live Godot mesh transform instead of a parallel per-frame cache — removes a dual-source-of-truth remnant now that the descriptor's Direction is authoritative (D-02)"

key-files:
  created:
    - .planning/phases/05-rendering-overhaul/05-04-SUMMARY.md
  modified:
    - Scripts/Render/WorldRenderer.cs

key-decisions:
  - "D-05 ordering was already structurally correct (luminous quad is a Camera3D child → naturally renders before the CanvasLayer dither). Task 1 was verify-only; no shader/scene edit needed — the documented HDR SubViewport/CompositorEffect fallback (RESEARCH A5/A6) was NOT required."
  - "Dead _lastRenderPositions cache removed (D-02 cleanup); GetRenderPosition kept as a thin cache-free accessor reading the live mesh transform (RESEARCH/PATTERNS option (a)) so the Hud target circle (D-46) keeps working with no parallel cache."
  - "No stale galaxy uniforms in LuminousPassRenderer — confirmed already removed in Plan 3 (D-13). Verify-only, no change."
  - "CRT scanlines remain OUT (D-06) — RND-01 CRT portion stays deferred; this plan was dither composition + cleanup only."

patterns-established:
  - "Verify-then-lock for composition ordering: when render-pass order is already correct by construction (Camera3D-child transparent quad before CanvasLayer post), prefer source/scene-graph assertions + a play-test over speculative rework."

requirements-completed: [RND-03, RND-06]

# Metrics
duration: ~4min (2 auto tasks; Task 1 verify-only, Task 2 cache removal) + final play-test
completed: 2026-06-20
status: complete
---

# Phase 5 Plan 04: Rendering Overhaul — Finalization (HDR-before-Dither Lock + Dead-Cache Removal) Summary

**D-05 glow-before-dither composition verified/locked (one unified 8-bit palette edge-to-edge); dead WorldRenderer._lastRenderPositions cache removed with Hud accessors preserved; no stale galaxy uniforms; final all-tier parity play-test APPROVED 2026-06-20. Phase 5 Rendering Overhaul COMPLETE (4/4 plans).**

## Performance

- **Duration:** ~4 min code work (Task 1 verify-only, Task 2 atomic cache removal) + final human play-test
- **Started:** 2026-06-20
- **Completed:** 2026-06-20; play-test APPROVED same session
- **Tasks:** 3 plan tasks (2 auto + 1 checkpoint:human-verify)
- **Files modified:** 1 (WorldRenderer.cs); verify-only across dithering.gdshader / luminous_pass.gdshader / LuminousPassRenderer.cs / Main.tscn

## Accomplishments

- **D-05 composition ordering verified/locked.** Confirmed the render-pass chain matches the design: the sky shader renders behind all geometry; 3D opaque meshes (planets + near-star spheres) write depth; the `LuminousPassRenderer` Camera3D-child `blend_add` quad composes the near-star glow/halo additively into the HDR 3D buffer; WorldEnvironment glow processes that buffer; the CanvasLayer dither (`dithering.gdshader`, `canvas_item`, `hint_screen_texture`) runs LAST and quantizes the fully-composited frame so the 8-bit palette applies once, edge-to-edge. No banding seam between the glow region and the rest of the scene.
- **Source assertions passed (Task 1, no code change required):**
  - `dithering.gdshader` is `shader_type canvas_item` with NO `hint_depth_texture` (Godot bug #74464 avoided).
  - `luminous_pass.gdshader` render_mode still contains `blend_add`.
  - `LuminousPassRenderer.cs` pushes only star uniforms — no `galaxy_disc_weights` / galaxy uniform arrays / galaxy push remain (Plan-3 cleanup confirmed, D-13).
  - `Main.tscn`: the dither CanvasLayer draws after the Camera3D-child `LuminousPassRenderer` quad — D-05 ordering correct by construction.
- **Dead `_lastRenderPositions` cache removed** (`WorldRenderer.cs`): the dictionary field, the `_lastRenderPositions.Clear()` at the top of `SyncBodies`, and the end-of-frame copy loop persisting `renderPositions` into it are all gone — the descriptor's `Direction` (Plan 1) is now the single source of truth (D-02 cleanup).
- **`GetRenderPosition` rewritten** as a thin cache-free accessor: reads `_meshes[bodyIdx].Position` directly, returning `true` only when the mesh exists and `mesh.Visible`. `GetRenderRadius` unchanged (already read `_meshes` directly). The Hud._Draw target circle (D-46) — which calls both — keeps working with no parallel cache.
- **No regression:** `RenderFactorFor`, `NearStarEmissionFloor` (D-12), the galaxy skip guard (`UniObject.Type.Galaxy`, D-28/T-03-06), floating-origin `SyncBodies`/`RenderBodyAt`, and the 1:1 unit-space render (RND-06) all preserved verbatim.
- **Final all-tier parity/improvement play-test APPROVED (D-04):** Planet / Star / Galaxy / Universe spaces all meet-or-beat the pre-overhaul look; unified palette edge-to-edge; HUD target circle tracks correctly after the cache removal; CRT correctly absent (D-06).
- **Build: 0/0 warnings/errors. Tests: 48/48 green** (no regression).

## Task Commits

Each task was committed atomically:

1. **Task 1: Verify/lock D-05 glow-before-dither composition ordering; confirm no stale galaxy push** — verify-only (no source change required; all assertions passed against existing Plan-3 code)
2. **Task 2: Remove dead _lastRenderPositions cache; GetRenderPosition reads live mesh (D-02)** — `02c24c9` (refactor)
3. **Task 3: Final play-test gate — dither composition + all-tier parity** — checkpoint:human-verify, APPROVED 2026-06-20 (no source change)

Session checkpoint state recorded at `c9f3340` during the Task 3 pause.

## Files Created/Modified

- `Scripts/Render/WorldRenderer.cs` — Removed `_lastRenderPositions` dictionary field + its `Clear()` + the end-of-`SyncBodies` copy loop; rewrote `GetRenderPosition` as a thin cache-free accessor reading the live `_meshes[bodyIdx].Position` (gated on `mesh.Visible`); `GetRenderRadius`, `RenderFactorFor`, `NearStarEmissionFloor`, galaxy skip guards, and floating-origin sync preserved. (−40 / +27 lines)
- `.planning/phases/05-rendering-overhaul/05-04-SUMMARY.md` — this summary.
- Verify-only (no edit): `Shaders/dithering.gdshader`, `Shaders/luminous_pass.gdshader`, `Scripts/Render/LuminousPassRenderer.cs`, `Main.tscn`.

## Decisions Made

- **D-05 ordering correct by construction:** The luminous glow quad is a Camera3D child, so it renders in the 3D transparent pass *before* the CanvasLayer dither — the design was already structurally sound. Task 1 verified rather than rebuilt; the HDR SubViewport / CompositorEffect fallback (RESEARCH A5/A6) was explicitly NOT needed.
- **GetRenderPosition kept as a public cache-free accessor (RESEARCH/PATTERNS option (a)):** reading the live mesh transform preserves the Hud target circle (D-46) without resurrecting a parallel cache — consistent with the single-descriptor principle (D-02).
- **CRT stays OUT (D-06):** confirmed absent in the final play-test; RND-01's CRT portion remains deferred.

## Deviations from Plan

None. Task 1 was planned as verify-only-by-default and required no edit; Task 2 matched the plan exactly; Task 3 (play-test) approved on the first pass with no fix iterations.

## Issues Encountered

None. Build clean and tests green throughout; the final play-test passed without any fix-and-retest cycle.

## User Setup Required

None — no external service configuration required.

## Known Stubs

None.

## Phase 5 Completion

This is the **final plan of Phase 5 — Rendering Overhaul (4/4 plans complete).** The overhaul unified the renderer into a coherent two-layer drawer split, all fed by the single Plan-1 `LuminousBodyDescriptor` pipeline:

- **Sky shader (`skybox.gdshader` / SkyboxRenderer)** — distant stars + galaxies, rendered behind all geometry (D-07 reversed; skybox kept).
- **Post-process (`luminous_pass.gdshader`)** — near-star aperture-diffraction PSF glow/halo only, depth-occluded per-pixel (D-13: galaxies are sky-shader-only).
- **Meshes (WorldRenderer)** — planets + near stars, body_lit terminator, floating-origin, 1:1 unit-space.
- **Composition** — additive glow composes in HDR before the canvas_item dither quantizes the whole frame to the 8-bit palette, edge-to-edge (D-05).

## Next Phase Readiness

Phase 5 is complete and ready for verification (`/gsd-verify-work` / phase transition). The render-debt todos un-bundled from the old Phase 5 remain OPEN as standalone follow-ups to be promoted to their own later phases **after** the overhaul:

- **P1** `galaxy-space-star-meshes-invisible` — galaxy-space star findability (do NOT reuse the abandoned StarPointRenderer billboard).
- **P2** `galaxy-visibility-in-universe-space` — Universe-space galaxy visibility (design fork).
- `galaxy-disc-tilt-foreshortening` — RESOLVED-PENDING-VERIFY (re-confirm post-overhaul).

Phase 3 UAT (3/7 pass) is gated on these render debts (post-overhaul), not on Phase 5 directly.

---
*Phase: 05-rendering-overhaul*
*Completed: 2026-06-20*
