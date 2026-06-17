---
phase: 04-flight-model-v2-tier-and-target-aware-speed
plan: "01"
subsystem: flight-controller
tags: [flight, speed-envelope, tier-aware, target-aware, hud]
dependency_graph:
  requires: []
  provides:
    - FlightController.TierSpeedFactor export (k, D-40)
    - FlightController.SpeedPerTarget export (k', D-43)
    - FlightController._hud reference + target ease-out in UpdateSpeedEnvelope
    - Hud.ActiveTargetIndex read-only accessor
  affects:
    - Scripts/Flight/FlightController.cs
    - Scripts/Hud/Hud.cs
tech_stack:
  added: []
  patterns:
    - Per-tier speed ceiling from parent.SOIMeters × k (D-40)
    - Symmetric proximity damp capped at tierCeiling (D-42 — core fix vs reverted 260617-j6b)
    - Target ease-out via UniMath.Distance LCA path (D-43)
    - Read-only Hud accessor pattern (HUD is a read-only consumer)
key_files:
  created: []
  modified:
    - Scripts/Flight/FlightController.cs
    - Scripts/Hud/Hud.cs
decisions:
  - "D-40: tierCeiling = parent.SOIMeters × TierSpeedFactor; no per-tier switch table"
  - "D-42: symmetric proximity damp capped at tierCeiling, not global _maxSpeed"
  - "D-43/D-44: target ease-out uses UniMath.Distance; tier ceiling still caps"
  - "D-45: targeting reach stays current-tier via existing BuildTargetableList (D-12 preserved)"
  - "T-04-01: guard parent.SOIMeters < double.MaxValue/2.0 before multiply"
  - "T-04-02: System.Math.Max(0.0,value) in TierSpeedFactor + SpeedPerTarget setters"
  - "T-04-03: ActiveTargetIndex rebuilds targetable list fresh each call + clamps _targetIndex"
metrics:
  duration: "~5 min"
  completed: "2026-06-17T18:34:21Z"
  tasks: 3
  files_modified: 2
---

# Phase 04 Plan 01: Tier- and Target-Aware Speed Envelope Summary

**One-liner:** Per-tier speed ceiling from `parent.SOIMeters × k`, symmetric proximity damp, and optional target ease-out via `UniMath.Distance` — fixes both Phase-03 UAT in-system over-speed and galaxy-SOI-exit dead zone.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add Hud.ActiveTargetIndex read-only accessor | 7b61d3a | Scripts/Hud/Hud.cs |
| 2 | Add TierSpeedFactor + SpeedPerTarget exports and _hud reference | bc4c92a | Scripts/Flight/FlightController.cs |
| 3 | Reshape UpdateSpeedEnvelope: tier ceiling + symmetric damp + target ease-out | 2645982 | Scripts/Flight/FlightController.cs |

## What Was Built

### Task 1 — `Hud.ActiveTargetIndex`

Added a `public int ActiveTargetIndex { get; }` property to `Hud` (Scripts/Hud/Hud.cs, line 91) in the "Target cycling state" section alongside `_targetIndex`. The getter:

- Returns `-1` if `_world` is null, ship index out of range, or targetable list is empty
- Calls the existing private `BuildTargetableList` (same-class call, no visibility change)
- Clamps `_targetIndex` to live list bounds (T-04-03 mitigation: stale index after SOI transition)
- Never mutates `_targetIndex` or any sim state (HUD read-only contract honored)
- Carries an XML `/// <summary>` documenting the `-1` sentinel and the read-only contract

### Task 2 — New exports + `_hud` reference in FlightController

Added to `Scripts/Flight/FlightController.cs`:

- `[Export] public double TierSpeedFactor` (backing field `_tierSpeedFactor = 1e-5`), setter `System.Math.Max(0.0, value)` (T-04-02)
- `[Export] public double SpeedPerTarget` (backing field `_speedPerTarget = 0.1`), setter `System.Math.Max(0.0, value)` (T-04-02)
- `private Hud.Hud _hud;` field in private-references section
- `_hud` resolved in `_Ready` via `GetTree().Root.FindChild("Hud", true, false) as Hud.Hud` — mirrors `_steeringReticle` no-export style; callers use `?.`

### Task 3 — Reshaped `UpdateSpeedEnvelope`

Surgical edits to `UpdateSpeedEnvelope` in `Scripts/Flight/FlightController.cs`:

1. **Tier ceiling (D-40):** `double tierCeiling = parent.SOIMeters < double.MaxValue / 2.0 ? Mathf.Clamp(parent.SOIMeters * _tierSpeedFactor, _minSpeed, _maxSpeed) : _maxSpeed`. The `< double.MaxValue / 2.0` guard prevents Infinity when the ship is in Universe/Root space (T-04-01, Pitfall 1).

2. **Open-space fallback:** Changed from `_maxSpeed / _speedPerMeter` to `tierCeiling / _speedPerMeter` for consistency with D-42.

3. **Symmetric proximity damp (D-42):** `targetMax` clamp upper bound changed from `_maxSpeed` → `tierCeiling`. This is the core fix: when the ship recedes from a galaxy boundary, `targetMax` returns to `tierCeiling` (a contextually appropriate in-tier speed), never to the global intergalactic `_maxSpeed`. The sibling scan uses `UniVec3.Distance` (same-frame path, Pitfall 3 preserved, unchanged).

4. **Target ease-out (D-43/D-44):** Reads `_hud?.ActiveTargetIndex ?? -1`; if a valid target, computes `distToTarget = UniMath.Distance(ship, gameObjects[tgtIdx], gameObjects)` (LCA path, CLAUDE.md mandatory) and clamps `targetMax = min(targetMax, Clamp(distToTarget * _speedPerTarget, _minSpeed, tierCeiling))`. Tier ceiling caps the ease-out (D-44).

5. **Both lerps unchanged and unconditional:** `Mathf.Lerp(_contextMax, ...)` and `Mathf.Lerp(_easedSpeed, ...)` run on every path with no `return` inserted above them (Bug 4 / Pitfall 2 — the one correct property of the reverted 260617-j6b fix).

## Verification Results

All structural assertions pass:

```
dotnet build: 0 errors, 0 warnings
TierSpeedFactor: FlightController.cs:172 ✓
SpeedPerTarget: FlightController.cs:185 ✓
private Hud.Hud _hud: FlightController.cs:219 ✓
FindChild("Hud"): FlightController.cs:261 ✓
SOIMeters * _tierSpeedFactor: FlightController.cs:437 ✓
double.MaxValue / 2.0 guard: FlightController.cs:436 ✓
tierCeiling in targetMax clamp: FlightController.cs:484 ✓
UniMath.Distance(ship, ...) for target: FlightController.cs:498 ✓
_hud?.ActiveTargetIndex: FlightController.cs:494 ✓
Mathf.Lerp(_contextMax, ...): FlightController.cs:508 ✓
Mathf.Lerp(_easedSpeed, ...): FlightController.cs:517 ✓
No switch(CurrentSpace): confirmed absent ✓
Hud.ActiveTargetIndex property: Hud.cs:91 ✓
targets[clamped].Index return: Hud.cs:104 ✓
```

Behavioral verification (in-system usable, galaxy-SOI-exit dead zone gone, smooth tier easing, target ease-out feel) is gated to the in-game play-test checkpoint in Plan 04-02.

## Deviations from Plan

None — plan executed exactly as written. All three tasks implemented surgically with no structural changes beyond those specified.

## Known Stubs

None — all new symbols are wired to live data sources. `TierSpeedFactor` and `SpeedPerTarget` are play-test tuning knobs (by design, per D-40/D-43 "Claude's Discretion") with concrete default values that produce numerically reasonable in-system speeds at StarSOI scale.

## Threat Flags

No new security-relevant surface introduced beyond what the plan's threat model covers. Both new exports have `System.Math.Max(0.0, value)` setters (T-04-02). Root SOI guard in place (T-04-01). `ActiveTargetIndex` rebuilds fresh and bounds-checks (T-04-03).

## Self-Check: PASSED

- `Scripts/Hud/Hud.cs` — exists with `public int ActiveTargetIndex` at line 91
- `Scripts/Flight/FlightController.cs` — exists with `TierSpeedFactor`, `SpeedPerTarget`, `_hud`, `UniMath.Distance` call
- Commit 7b61d3a — Task 1 (Hud accessor)
- Commit bc4c92a — Task 2 (exports + _hud ref)
- Commit 2645982 — Task 3 (envelope reshape)
- All commits on main branch, build 0/0
