---
phase: 07-autopilot-warp-drive
plan: "02"
subsystem: hud
tags: [warp, confirmation-screen, hud-panel, main-tscn]
dependency_graph:
  requires:
    - FlightController.EngageWarp / IsPanelOpen / WarpMaxSpeed (07-01)
    - Hud.ActiveTargetIndex / FormatSpeed / FormatDistance (06-02)
    - UniMath.Distance (LCA path)
  provides:
    - WarpConfirmationScreen (J opens warp panel, Enter engages warp)
    - Main.tscn WarpConfirmationScreen node wired under CanvasLayer
  affects:
    - Main.tscn (new node + ext_resource added)
tech_stack:
  added: []
  patterns:
    - TargetSelectorPanel structure mirrored exactly (OpenPanel/ClosePanel/IsPanelOpen gate)
    - HSlider runtime travel-time input (not an Export — configured in _Ready)
    - ClosePanel-before-EngageWarp order (T-07-09 / Pitfall 3)
    - UniMath.Distance cross-frame distance for display (CLAUDE.md §Position Math)
key_files:
  created:
    - Scripts/Hud/WarpConfirmationScreen.cs
  modified:
    - Main.tscn
decisions:
  - "WarpConfirmationScreen mirrors TargetSelectorPanel exactly: NodePath exports with FindChild fallback, IsPanelOpen gate, AddRow phosphor-green helper, GetViewport().SetInputAsHandled() on every handled input"
  - "ClosePanel() called BEFORE EngageWarp() on Enter (T-07-09): IsPanelOpen=false before warp _Process begins so warp is never gated"
  - "WarpConfirmationScreen node positioned centered in CanvasLayer (anchors_preset=8, 400x240) — retro confirmation dialog style distinct from left-aligned TargetSelectorPanel"
  - "WorldPath not set in Main.tscn for WarpConfirmationScreen (same as TargetSelectorPanel) — FindChild('Main') fallback resolves TestSetup at runtime"
metrics:
  duration_minutes: 3
  completed_date: "2026-06-22"
  tasks_completed: 2
  tasks_total: 3
  files_modified: 2
---

# Phase 07 Plan 02: WarpConfirmationScreen + Main.tscn Wiring Summary

**One-liner:** Phosphor-green warp confirmation Control panel (TargetSelectorPanel pattern) wired into Main.tscn — opens on J with active target, travel-time HSlider updates warp speed live, Enter calls ClosePanel then EngageWarp, Esc/J cancels.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Create WarpConfirmationScreen.cs following TargetSelectorPanel pattern | aac56c9 | Scripts/Hud/WarpConfirmationScreen.cs |
| 2 | Wire WarpConfirmationScreen into Main.tscn under CanvasLayer | b408e3c | Main.tscn |

## Task 3 Status: PENDING CHECKPOINT

Task 3 is a `type="checkpoint:human-verify"` — full-phase play-test of all eight Phase-7 behaviors. This plan is paused at the checkpoint; the executor has committed all auto tasks and this SUMMARY.md, and returned structured checkpoint state to the orchestrator.

## What Was Built

### Scripts/Hud/WarpConfirmationScreen.cs (Task 1)

New file, `namespace Hud`, `public partial class WarpConfirmationScreen : Control`.

**Exports (mirroring TargetSelectorPanel):**
- `[Export] NodePath WorldPath` / `HudPath` / `FlightPath` — resolved in `_Ready` with FindChild fallback
- `[Export] Color PhosphorGreen` — default `new Color(0.1f, 1.0f, 0.3f)`

**Private refs:** `_world` (TestSetup), `_hud` (Hud.Hud), `_flight` (Flight.FlightController), `_vbox` (VBoxContainer)

**UI children built in `_Ready`:**
- Title label "WARP DRIVE" (highlighted phosphor-green)
- `_targetLabel`, `_distLabel`, `_timeLabel` — live display rows
- `_timeSlider` (HSlider): MinValue=1.0, MaxValue=60.0, Step=0.5, Value=2.0 (minutes)
- `ValueChanged` handler: `_selectedTravelTimeSec = value * 60.0` + null-guarded `RefreshDisplay()`
- `_speedLabel` — computed warp speed display
- Hint label "ENTER engage   ESC/J cancel"

**`_UnhandledInput`:**
- `warp_engage` (J): toggle — close if visible, open if target active (`ActiveTargetIndex >= 0`), no-op otherwise (D-02)
- `ui_cancel` (Esc): ClosePanel()
- `ui_accept` (Enter): **ClosePanel() FIRST, then `_flight?.EngageWarp(_selectedTravelTimeSec)`** (T-07-09 / Pitfall 3)

**`OpenPanel`:** Visible=true, MouseFilter=Stop, MouseMode=Visible, IsPanelOpen=true, RefreshDisplay()

**`ClosePanel`:** Visible=false, MouseFilter=Ignore, MouseMode=Captured, IsPanelOpen=false

**`RefreshDisplay`:** Reads `Hud.ActiveTargetIndex`, bounds-safe lookups for ship + target, `UniMath.Distance` for dist → `Hud.FormatDistance`, warp speed = `Min(dist / _selectedTravelTimeSec, WarpMaxSpeed)` → `Hud.FormatSpeed`. Falls back to "NO TARGET" state gracefully.

**Read-only contract (D-53):** Zero writes to GameObjects[], .LocalPos, or .ChildIndices (verified by grep).

### Main.tscn (Task 2)

- Added `[ext_resource type="Script" uid="uid://bwarpconfirm1" path="res://Scripts/Hud/WarpConfirmationScreen.cs" id="13_warpscreen"]`
- Added `WarpConfirmationScreen` node under `CanvasLayer`, sibling of `TargetSelectorPanel`:
  - Centered layout (anchors_preset=8), 400×240 px centered — confirmation dialog style
  - `mouse_filter = 2` (Ignore when hidden, Stop when open — set by OpenPanel)
  - `HudPath = NodePath("../Hud")`, `FlightPath = NodePath("../../FlightController")`
  - WorldPath not set — FindChild("Main") fallback resolves TestSetup at runtime

## Verification Results

- `dotnet build EcoSpace.csproj`: **0 errors, 0 warnings** (after both tasks)
- `grep -c WarpConfirmationScreen Main.tscn`: **2** (ext_resource + node)
- Read-only contract: no `GameObjects[..]=`, no `.LocalPos =`, no `.ChildIndices =` in WarpConfirmationScreen.cs
- Enter path: ClosePanel() on line 156, EngageWarp() on line 157 — correct order
- Play-test (Task 3): **PENDING** — requires human verification (checkpoint:human-verify)

## Deviations from Plan

None. Both auto tasks executed exactly as planned. All threat model mitigations (T-07-06 through T-07-09) implemented as specified.

## Known Stubs

None. All implemented symbols are functional. The "no target" display state in `ShowNoTarget()` is proper fallback behavior, not a stub.

## Threat Flags

None. No new network endpoints, auth paths, file access patterns, or schema changes introduced. The trust boundaries (player input → warp screen → FlightController, world state → screen display) are all mitigated per the plan's threat register.

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `Scripts/Hud/WarpConfirmationScreen.cs` exists | FOUND |
| `Main.tscn` modified with WarpConfirmationScreen | FOUND |
| Commit aac56c9 (Task 1) exists | FOUND |
| Commit b408e3c (Task 2) exists | FOUND |
| `.planning/phases/07-autopilot-warp-drive/07-02-SUMMARY.md` exists | FOUND |
