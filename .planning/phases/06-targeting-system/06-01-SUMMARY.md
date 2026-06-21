---
phase: 06-targeting-system
plan: "01"
subsystem: Hud
tags: [targeting, hud, cross-space, input, cleanup]
dependency_graph:
  requires: []
  provides:
    - Hud.BuildFullHierarchyTargetList (Galaxy→Star→Planet candidate set)
    - Hud.SetTargetIndex (panel write API)
    - Hud.GetTargetCandidates (panel read API)
    - project.godot toggle_target_panel input action
  affects:
    - Scripts/Flight/FlightController.cs (reads ActiveTargetIndex — contract unchanged)
    - Scripts/Hud/ (06-02 TargetSelectorPanel will call SetTargetIndex/GetTargetCandidates)
    - Scripts/Render/ (06-03 TargetMarkerRenderer will read ActiveTargetIndex)
tech_stack:
  added: []
  patterns:
    - Full-hierarchy GameObjects walk with tier bucketing (Galaxy/Star/Planet)
    - Panel-API ownership pattern: Hud owns _targetIndex; panel calls SetTargetIndex
key_files:
  created: []
  modified:
    - Scripts/Hud/Hud.cs
    - project.godot
decisions:
  - D-55 implemented: full cross-space candidate set (Galaxy→Star→Planet) replaces parent+siblings-only list
  - D-56 implemented: Tab cycle_target retired; toggle_target_panel bound to Tab (physical key 4194306)
  - D-50 implemented: 2D DrawArc circle path fully removed (superseded by 3D sphere-outline in 06-03)
  - D-53 preserved: SetTargetIndex/GetTargetCandidates write only internal _targetIndex, never sim state
  - ActiveTargetIndex public signature unchanged (FlightController D-43 ease-out contract preserved)
metrics:
  duration: "4m"
  completed: "2026-06-21"
  tasks_completed: 3
  files_changed: 2
status: complete
---

# Phase 6 Plan 1: Cross-Space Target Foundation Summary

Cross-space targeting foundation for the panel-driven selector: replaces the current-tier-only targetable set with a full-hierarchy candidate list (Galaxy→Star→Planet), adds the SetTargetIndex/GetTargetCandidates panel API, retires the Tab cycle_target input action, and removes the superseded 2D DrawArc circle path.

## What Changed

### Scripts/Hud/Hud.cs

**Added:**
- `BuildFullHierarchyTargetList(int shipIndex, List<UniObject> gameObjects)` — private method that walks all GameObjects and returns tier-ordered `TargetEntry` list: Galaxies first, then Stars, then Planets, skipping the ship, null entries, Root/Universe-space containers, and Ship/None-typed objects.
- `SetTargetIndex(int candidateIndex)` — public; clamps and stores into `_targetIndex`; writes only the internal HUD index, never sim state (D-53).
- `GetTargetCandidates()` — public `IReadOnlyList<int>`; returns GameObjects indices for every targetable body in tier order; empty when not ready.

**Modified:**
- `ActiveTargetIndex` property: sources candidate list from `BuildFullHierarchyTargetList` instead of `BuildTargetableList`. Public signature, return type, and Clamp behavior unchanged — FlightController D-43 ease-out reads it identically.
- `UpdateContextLabel`: sources nearest-body scan from `BuildFullHierarchyTargetList`; Galaxy exclusion from the "nearest" display label preserved (same behavior, wider candidate pool).
- `UpdateTargetReadout`: sources from `BuildFullHierarchyTargetList`.
- `_Ready`: removed `_worldRenderer` resolution block and `FindNodeByType` helper call.
- `_Process`: removed `UpdateTargetCircle()` call and `QueueRedraw()`.
- Class header doc updated to reflect D-55/D-56 changes.

**Removed:**
- `BuildTargetableList(parentIdx, shipIndex, gameObjects)` — old parent+siblings-only method
- `_Input(InputEvent)` override — cycle_target Tab handler (D-56: selection moves to panel)
- `UpdateTargetCircle(...)` method
- `_Draw()` override (DrawArc 2D circle)
- `_showTargetCircle`, `_targetCirclePos`, `_targetCircleRadius` fields
- `MIN_CIRCLE_RADIUS`, `MAX_CIRCLE_RADIUS`, `CIRCLE_BODY_PADDING` constants
- `_worldRenderer` field
- `FindNodeByType<T>` helper

### project.godot

- Removed: `cycle_target` input action (Tab, physical_keycode 4194306)
- Added: `toggle_target_panel` input action bound to same physical key (4194306 / Tab)

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | a222d64 | feat(06-01): replace targetable set with cross-space full-hierarchy candidate list |
| 2 | add04d4 | feat(06-01): add SetTargetIndex/GetTargetCandidates API; retire Tab cycle_target; bind toggle_target_panel |

## Build / Test Result

`dotnet build EcoSpace.csproj -c Debug` — **Build succeeded. 0 errors, 0 warnings.**

No unit test file was modified. The existing `HudFormatTests.Run()` smoke-test in `_Ready` is unaffected (only FormatSpeed/FormatDistance are tested there; both methods are unchanged). The plan states existing 48/48 unit tests should still be green — no test infrastructure was changed, and the build is clean.

## Deviations from Plan

**Tasks 1, 2, and 3 were implemented together in a single Hud.cs edit pass**, then committed in two logically grouped commits (T1 = candidate list + all removals; T2 = project.godot). The plan described three sequential tasks, but the removals in Task 3 (2D circle fields, _Draw, QueueRedraw, _worldRenderer) were interdependent with Task 1 changes (removing BuildTargetableList callers in UpdateTargetCircle required removing UpdateTargetCircle itself). Collapsing them avoided an intermediate broken state that would have produced a build error (removed field still referenced). This is a sequencing deviation, not a scope deviation — all three tasks' acceptance criteria are satisfied.

No other deviations. No architectural changes, no new packages, no threat surface additions.

## Known Stubs

None. The candidate API (`SetTargetIndex`/`GetTargetCandidates`) returns live data from GameObjects. No hardcoded empty values flow to the UI — the target readout and context label continue to display real body names and distances.

## Threat Flags

None. No new network endpoints, auth paths, file access patterns, or schema changes. The read-only consumer contract (D-53) is preserved: all new methods only read GameObjects and write the internal `_targetIndex`.

## Self-Check

- [x] `Scripts/Hud/Hud.cs` exists and contains `BuildFullHierarchyTargetList`, `SetTargetIndex`, `GetTargetCandidates`
- [x] `project.godot` contains `toggle_target_panel`; `cycle_target` is absent
- [x] Commits a222d64 and add04d4 exist on `main`
- [x] Build: 0 errors, 0 warnings
- [x] No `_Draw`, `DrawArc`, `UpdateTargetCircle`, `_showTargetCircle` in Hud.cs
- [x] No `cycle_target` anywhere in Scripts/ or project.godot
- [x] `UpdateDirectionMarker` and `HideMarker` unchanged

## Self-Check: PASSED
