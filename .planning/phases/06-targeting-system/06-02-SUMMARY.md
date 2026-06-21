---
phase: 06-targeting-system
plan: "02"
subsystem: Hud
tags: [targeting, hud, panel, ui, input, cursor, tree, flight-gate]
dependency_graph:
  requires:
    - Hud.SetTargetIndex (06-01)
    - Hud.GetTargetCandidates (06-01)
    - Hud.ActiveTargetIndex (06-01)
    - UniMath.Distance (LCA path)
    - toggle_target_panel input action (06-01)
  provides:
    - TargetSelectorPanel (left-anchored cross-space hierarchy-tree selector, Tab toggle)
    - FlightController.IsPanelOpen (flight-input gate consumed by the panel)
  affects:
    - Scripts/Hud/TargetSelectorPanel.cs (new)
    - Scripts/Flight/FlightController.cs (IsPanelOpen gate added)
    - Main.tscn (CanvasLayer/TargetSelectorPanel node added + repositioned)
tech_stack:
  added: []
  patterns:
    - Navigable parent→child tree (galaxy→star→planet) rebuilt from UniObject.ParentIndex/ChildIndices/Space
    - Level-state model rebuilt on every level change (fixes empty-list lockup)
    - Cursor reconciliation: panel open → MouseMode Visible; close → Captured
    - FlightController gated via public IsPanelOpen flag (early-return in _Input/_Process/_UnhandledInput)
    - Read-only consumer pattern — all selection via Hud.SetTargetIndex only
key_files:
  created:
    - Scripts/Hud/TargetSelectorPanel.cs
  modified:
    - Scripts/Flight/FlightController.cs
    - Main.tscn
decisions:
  - "D-54 SUPERSEDED: flat tier-grouped (GALAXY/STAR/PLANET headers) replaced by a navigable parent→child hierarchy tree rooted at the galaxy level, with breadcrumb path"
  - "D-51 REINSTATED (galaxy-as-target): galaxies are SELECTABLE targets — Enter on a galaxy sets it as the active target (fly to galaxy center); d/Right still descends into its stars. User override 2026-06-21: 'make galaxies selectable, override every decision made'"
  - "D-12 overridden: targeting is cross-space full-hierarchy, not current-parent-space only"
  - "Tree navigation: a/Left = up a level, d/Right = down a level, w/Up & s/Down = move within level, Enter = select; both WASD and arrow keys bound"
  - "Flight fully gated while panel open (FlightController.IsPanelOpen) — WASD drives the menu, never the ship; play-test-driven scope addition beyond declared plan files"
  - "D-53 preserved: panel never writes GameObjects or LocalPos; all writes via Hud.SetTargetIndex"
  - "Panel re-anchored left and repositioned below the TGT label (manual HUD fix) to avoid overlapping other HUD elements"
  - "DEFERRED: time-slow / game-pause while menu open (user parked it 2026-06-21); flight-stop already covers the functional need"
metrics:
  duration: "~25m (3 play-test cycles)"
  completed: "2026-06-21"
  tasks_completed: 3
  files_changed: 3
status: complete
---

# Phase 6 Plan 2: Cross-Space Target Selector Panel Summary

A left-anchored, phosphor-green **hierarchy-tree** selector. It shows the real universe
parent→child structure (galaxies → their stars → their planets) with a breadcrumb path,
displays name + live distance per row, and lets the player browse and select **any** body
across spaces — galaxies, stars, and planets are all selectable targets. While the panel is
open, all flight input is suppressed so WASD drives the menu, not the ship.

Delivered over three play-test cycles: (1) initial tier-grouped right panel → rejected;
(2) reworked to a left-anchored navigable tree + flight gate; (3) fixed an empty-list
navigation lockup and made galaxies selectable (user override). Approved 2026-06-21.

## What Changed

### Scripts/Hud/TargetSelectorPanel.cs (NEW)

**Class:** `Hud.TargetSelectorPanel : Control` — read-only consumer of sim state (D-53).

**Tree model & navigation:**
- Displays the universe hierarchy as a navigable tree rooted at the galaxy level; a
  breadcrumb header shows the current path (e.g. `TARGETS — HOME GALAXY — STARS`).
- `a` / Left = up one level; `d` / Right = descend into the highlighted node's children;
  `w` / Up & `s` / Down = move the `>` highlight within the current level; `Enter` = select
  the highlighted body as the active target. Both WASD and arrow keys are bound.
- The visible-row list is rebuilt from the current container on **every** level change —
  the fix for the empty-list lockup (see Deviations).

**Selection (cross-space, all tiers):**
- Galaxies, stars, and planets are all `Enter`-selectable; selecting calls
  `Hud.SetTargetIndex(index)`, closes the panel, and resumes flight. A galaxy target eases
  the ship toward the galaxy center via the existing `FlightController.ActiveTargetIndex`
  (UniMath.Distance LCA path).
- Mouse click on a row selects it (same as Enter). `d`/Right vs `Enter` are distinct on a
  galaxy row: `d` descends, `Enter` selects the galaxy itself.
- `OpenPanel()` syncs the highlight to the current active target; sets `MouseMode = Visible`
  and `FlightController.IsPanelOpen = true`. `ClosePanel()` restores `MouseMode = Captured`
  and clears the gate.

**Security invariant (D-53 / T-06-04):** zero direct writes to `GameObjects[...]` or
`.LocalPos =` — selection flows only through `Hud.SetTargetIndex`. All distances via
`UniMath.Distance` (LCA path — CLAUDE.md §Position Math).

### Scripts/Flight/FlightController.cs

Added `public bool IsPanelOpen { get; set; } = false` and `if (IsPanelOpen) return;` guards
at the top of `_Input`, `_Process`, and `_UnhandledInput` — suppresses throttle, attitude,
roll, mouse steering, and the T-key toggle while the panel owns input.

### Main.tscn

- Added the `TargetSelectorPanel` node under `CanvasLayer`, script ext_resource, and
  `HudPath`/`FlightPath` wiring.
- Left-anchored (`anchors_preset 9`) and repositioned `offset_top = 116` so it clears the
  top-left TGT/HUD readout (manual HUD fix by the user during play-test).

## Commits

| # | Hash | Message |
|---|------|---------|
| 1 | a598d34 | feat(06-02): implement TargetSelectorPanel — tier-grouped cross-space target selector |
| 2 | 4971359 | feat(06-02): wire TargetSelectorPanel into Main.tscn under CanvasLayer |
| 3 | 7b0bbbc | feat(06-02): add IsPanelOpen flight-gate to FlightController |
| 4 | edb2083 | feat(06-02): rework TargetSelectorPanel — left panel, hierarchy tree, flight gate |
| 5 | e0b6d8d | feat(06-02): re-anchor TargetSelectorPanel to left edge of screen |
| 6 | a506061 | fix(06-02): nav-bug + galaxy-selectable rework |
| 7 | 9a593d6 | chore(06-02): reposition TargetSelectorPanel below TGT label (manual HUD fix) |

## Build / Test Result

`dotnet build EcoSpace.csproj -c Debug` — **Build succeeded. 0 errors, 0 warnings.**
Human play-test **APPROVED 2026-06-21** (tree traversal with no empty-list lockup; galaxy
Enter-select vs d-descend; flight fully gated while open; WASD + arrows; left placement).

## Deviations from Plan

- **Tier-grouped design superseded by a hierarchy tree** (D-54) — play-test feedback; the
  flat GALAXY/STAR/PLANET header layout was rejected in favor of navigable parent→child levels.
- **Galaxies made selectable targets** — user override ("override every decision made");
  reinstates the original D-51 galaxy-as-target case (galaxy target = galaxy center). This
  re-enables the galaxy-marker requirement for Wave 3 (06-03).
- **FlightController.cs touched** (out of declared plan files) to add the `IsPanelOpen`
  flight-input gate — required by "flight controls turn off when in menu".
- **Empty-list navigation bug fixed** — root cause: `BuildCurrentLevelList()` skipped bodies
  in `Universe` space, but galaxies live in `Universe` space (children of Root), so the
  level-0 list was always empty after navigating up. Dropped the space filter; the
  `ObjectType == Galaxy` check is sufficient (Root container is `Type.None`).
- **Manual HUD reposition** by the user (left anchor + offset below TGT label).

## Deferred

- **Time-slow / game-pause while the menu is open** — user parked it for now
  (2026-06-21). Flight input already stops via `IsPanelOpen`, covering the functional need;
  a dramatic time-dilation effect can be a later polish item.

## Threat Flags

None. T-06-04 (panel mutating sim state) verified mitigated: selection only through
`Hud.SetTargetIndex`; no direct writes to `GameObjects`/`LocalPos`.

## Self-Check

- [x] `Scripts/Hud/TargetSelectorPanel.cs` exists — hierarchy tree, WASD/arrow nav, all-tier `Enter` select, `UniMath.Distance`
- [x] `Scripts/Flight/FlightController.cs` has `IsPanelOpen` gate in `_Input`/`_Process`/`_UnhandledInput`
- [x] `Main.tscn` has the `TargetSelectorPanel` node (left-anchored, repositioned) wired to Hud/Flight
- [x] grep: no `GameObjects[...]=` or `.LocalPos =` writes in TargetSelectorPanel.cs
- [x] Commits a598d34, 4971359, 7b0bbbc, edb2083, e0b6d8d, a506061, 9a593d6 on `main`
- [x] Build: 0 errors, 0 warnings; play-test approved

## Self-Check: PASSED
