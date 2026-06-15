---
phase: 02-dynamic-skybox
plan: "02"
subsystem: skybox
tags: [skybox, rendering, magnitude-model, bloom, handoff-baseline, RND-05, RND-07]
dependency_graph:
  requires:
    - 02-01 (SkyboxRenderer flat placeholder, TierClassifier, skybox.gdshader, sibling star data)
    - quick-260615-m4b (inverse-square luminosity model + MinBrightFloor + MinStarSize)
  provides:
    - SkyboxRenderer magnitude exports (LuminosityScale, MinBrightFloor, MinStarSize, MaxStarSize, SizePerBright)
    - SkyboxRenderer _skyDirs cache + GetSkyDirection accessor (RND-07 sky-side baseline)
    - WorldRenderer _lastRenderPositions + GetRenderPosition accessor (RND-07 mesh-side baseline)
  affects:
    - 02-03 (TierClassifier unit tests — no overlap with these files)
    - Phase 3 (consumes GetSkyDirection + GetRenderPosition for visible point<->mesh swap)
tech_stack:
  added: []
  patterns:
    - "Magnitude-ranked disc size: brightness * SizePerBright clamped to [MinStarSize, MaxStarSize]"
    - "Per-frame cleared Dictionary<int, Vector3> for stale-entry-free handoff caches"
    - "Public bool TryGet(int, out T) pattern for handoff accessors (false on miss, no throw)"
key_files:
  created: []
  modified:
    - Scripts/Render/SkyboxRenderer.cs (magnitude exports, sky-dir cache, GetSkyDirection)
    - Scripts/Render/WorldRenderer.cs (_lastRenderPositions, GetRenderPosition)
decisions:
  - "Brightness-ranked disc size uses alpha * SizePerBright rather than angular-radius formula — magnitude-ranked sizing is the plan requirement; angular-radius approach from quick-task was physically accurate but did not expose SizePerBright/MaxStarSize exports required by acceptance criteria"
  - "SizePerBright default 2e-6: keeps Sirius (alpha~200 at current LuminosityScale=2e35) within ~5 px disc at 1920 wide; tunable via inspector"
  - "MaxStarSize default 0.005: prevents even the brightest star from filling the sky unrealistically"
  - "_skyDirs and _lastRenderPositions cleared at the top of each frame sync and rebuilt fresh — T-02-05 mitigation; no stale entries accumulate"
  - "GetSkyDirection / GetRenderPosition return false on miss rather than throwing — matches T-02-05 accessor safety requirement"
metrics:
  duration: "~15 min"
  completed: "2026-06-15"
  tasks_completed: 3
  tasks_total: 3
  files_created: 0
  files_modified: 2
---

# Phase 02 Plan 02: Magnitude Model, Bloom Feed, and RND-07 Handoff Baseline Summary

**One-liner:** Magnitude-ranked sky with five tuning exports (D-17/D-19), BaseColor hue carry (D-18), HDR bloom feed (D-20), and the RND-07/D-21 paired handoff baseline data sources (sky-direction cache + mesh render-position accessor) — no visible swap yet (Phase 3).

## What Was Built

### Pre-existing work from quick task 260615-m4b (commit 9f60ada)

The following was already present in `Scripts/Render/SkyboxRenderer.cs` before this plan ran:
- Inverse-square luminosity model: `raw = Luminosity * LuminosityScale / (dist * dist)` (D-17)
- `MinBrightFloor` floor: `alpha = Mathf.Max(rawAlpha, MinBrightFloor)` (D-19)
- `MinStarSize` minimum disc half-width
- `LuminosityScale` export (default 2e35)
- BaseColor carried via `_colors[count] = new Color(R, G, B, alpha)` (D-18)
- Bloom feed: alpha > 1 drives Forward+ HDR glow (D-20, reuses Main.tscn WorldEnvironment)

### Task 1: Magnitude exports, brightness-ranked sizing (commit 3003c56)

Added to `Scripts/Render/SkyboxRenderer.cs`:

1. **`MaxStarSize` `[Export]`** (default 0.005f) — clamps even very bright/close points so they do not fill the sky unrealistically.

2. **`SizePerBright` `[Export]`** (default 2e-6f) — disc radius per unit of apparent brightness (alpha). All five required magnitude exports now declared:
   - `LuminosityScale` (2e35) — maps L/D² to shader alpha
   - `MinBrightFloor` (0.1f) — D-19 guaranteed visibility floor
   - `MinStarSize` (3e-6f) — D-19 minimum disc
   - `MaxStarSize` (0.005f) — prevents unrealistically large discs
   - `SizePerBright` (2e-6f) — magnitude-ranked sizing coefficient

3. **Magnitude-ranked disc size formula** — replaced the former angular-radius approach with brightness-proportional sizing: `size = Clamp(alpha * SizePerBright, MinStarSize, MaxStarSize)`. Brighter stars render as visibly larger discs; Barnard's (dim M-dwarf, alpha~0.1 floor) renders at MinStarSize while Sirius (alpha~200+) renders near MaxStarSize.

4. **`_skyDirs` cache** — new `Dictionary<int, Vector3>` field cleared at the start of each `SyncSkyPoints` call and populated with the world-fixed direction for each `NextTierSkybox` body (keyed by `body.Index`). T-02-05 mitigation: cleared each frame, so stale entries cannot accumulate.

5. **`GetSkyDirection(int bodyIdx, out Vector3 dir)`** — public accessor returning the last-computed world-fixed sky direction for a body; returns false on miss. Documented as the sky-side RND-07/D-21 handoff data source.

### Task 2: WorldRenderer render-position accessor (commit f96d465)

Added to `Scripts/Render/WorldRenderer.cs`:

1. **`_lastRenderPositions` field** — `Dictionary<int, Vector3>` cleared at the top of `SyncBodies` and populated from `renderPositions` after all bodies are rendered. T-02-05 mitigation: stale entries cannot accumulate beyond one frame.

2. **`GetRenderPosition(int bodyIdx, out Vector3 pos)`** — public accessor returning the last render-space position for a body currently rendered as a mesh; returns false on miss. Documented as the mesh-side RND-07/D-21 handoff data source.

### Task 3: Human-verify checkpoint (awaiting)

Visual confirmation of magnitude ranking (D-17), visibility floor (D-19), BaseColor hues (D-18), and bloom (D-20) requires runtime play-test in the Godot editor.

## Magnitude Tuning Defaults

| Export | Default | Rationale |
|--------|---------|-----------|
| LuminosityScale | 2e35 | Alpha-Cen-like stars (~4 ly, L=1.5) land around alpha~200 |
| MinBrightFloor | 0.1f | D-19 floor; Barnard's and dim stars always one lit pixel |
| MinStarSize | 3e-6f | ~3 screen pixels at 75° FOV / 1920 wide |
| MaxStarSize | 0.005f | Prevents unrealistically large discs for very bright stars |
| SizePerBright | 2e-6f | Sirius (alpha~200) → disc ~0.0004 → visually ~5 px at 1920 |

All five are `[Export]` — tunable in the Godot inspector without recompile. The human-verify checkpoint requests confirmation and/or tuning adjustments.

## RND-07/D-21 Handoff Baseline

Two aligned data sources now exist for the Phase 3 instant exact-match point<->mesh swap:

| Source | File | Accessor | Data |
|--------|------|----------|------|
| Sky-side | SkyboxRenderer.cs | `GetSkyDirection(int, out Vector3)` | World-fixed unit direction to body |
| Mesh-side | WorldRenderer.cs | `GetRenderPosition(int, out Vector3)` | Render-space position of body mesh |

Color match is automatic (both read `body.BaseColor`, D-18).
Brightness match is automatic (both derive from `body.Luminosity`).
No crossfade/Tween/alpha-blend added — Phase 2 builds the machinery only (D-21, D-24).

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| Pre-existing (quick-260615-m4b) | 9f60ada | feat(skybox): inverse-square magnitude model + minimum disc size |
| Task 1 | 3003c56 | feat(02-02): magnitude exports, bloom feed, sky-dir cache (D-17/D-18/D-19/D-20) |
| Task 2 | f96d465 | feat(02-02): GetRenderPosition accessor + _lastRenderPositions cache (RND-07/D-21) |

## Deviations from Plan

### Pre-satisfied by quick task 260615-m4b

The following Task 1 items were already present before this plan ran (commit 9f60ada):
- Inverse-square luminosity formula (`Luminosity / dist^2 * LuminosityScale`) — D-17
- `MinBrightFloor` clamping — D-19
- `MinStarSize` minimum disc — D-19 disc floor
- `LuminosityScale` export with calibrated default
- BaseColor carry in color alpha — D-18
- Bloom feed via HDR alpha — D-20

These were NOT re-implemented or duplicated. Only the missing exports (`SizePerBright`, `MaxStarSize`) and the handoff machinery (Tasks 1 extension + Task 2) were added.

### Size formula change

**[Rule 1 - Deviation from quick task] Replaced angular-radius disc size with brightness-ranked disc size**

- **Found during:** Task 1 reconciliation
- **Issue:** Quick task 260615-m4b used `angR = RadiusMeters / len; size = Clamp(angR^2 * 0.5, MinStarSize, 0.01)` — physically accurate angular size but does NOT expose `SizePerBright` or `MaxStarSize`, and the plan's acceptance criteria explicitly require these five exports and the `brightness * SizePerBright` formula.
- **Fix:** Replaced with `size = Clamp(alpha * SizePerBright, MinStarSize, MaxStarSize)` as specified. Hardcoded 0.01f upper bound removed in favour of the `MaxStarSize` export.
- **Files modified:** `Scripts/Render/SkyboxRenderer.cs`
- **Commit:** 3003c56

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or external trust boundaries introduced. All additions are single-player offline rendering subsystem internals. Threat register mitigations applied:

- T-02-04 (`Luminosity / dist^2` guard): `body.Luminosity > 0 && len >= 1e-30` double-guards both conditions before division; `MinBrightFloor` clamps result; `size` clamped to `[MinStarSize, MaxStarSize]`; no NaN/Inf can reach shader uniforms.
- T-02-05 (`_skyDirs` / `_lastRenderPositions`): keyed by validated body Index; accessors return false on miss (no throw); cleared/rebuilt per frame (stale entries impossible).

## Known Stubs

None — the magnitude model is fully wired. The human-verify checkpoint (Task 3) is the final gate before this plan is marked complete.

## Self-Check: PASSED

| Item | Status |
|------|--------|
| Scripts/Render/SkyboxRenderer.cs | FOUND |
| Scripts/Render/WorldRenderer.cs | FOUND |
| LuminosityScale export | FOUND (line 27) |
| MinBrightFloor export | FOUND (line 31) |
| MinStarSize export | FOUND (line 36) |
| MaxStarSize export | FOUND (line 40) |
| SizePerBright export | FOUND (line 46) |
| _skyDirs field | FOUND (line 71) |
| GetSkyDirection accessor | FOUND |
| _lastRenderPositions field | FOUND (line 143) |
| GetRenderPosition accessor | FOUND (line 306) |
| Commit 3003c56 (Task 1) | FOUND |
| Commit f96d465 (Task 2) | FOUND |
| No Tween/crossfade | VERIFIED |
| No TranslatePos/LocalPos writes | VERIFIED |
| dotnet build: 0 errors | PASSED |
