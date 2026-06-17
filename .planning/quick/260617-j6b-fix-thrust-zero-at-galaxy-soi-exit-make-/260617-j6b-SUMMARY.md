---
status: incomplete
result: rejected-at-checkpoint
phase: quick-260617-j6b
plan: "01"
subsystem: Flight
tags: [flight, speed-envelope, proximity-clamp, direction-aware, bug-fix]
dependency_graph:
  requires: []
  provides: [direction-aware-proximity-clamp]
  affects: [Scripts/Flight/FlightController.cs]
tech_stack:
  added: []
  patterns: [closing-velocity-gate, dot-product-direction-test]
key_files:
  created: []
  modified:
    - Scripts/Flight/FlightController.cs
decisions:
  - "closing <= 0 (receding) sets targetMax = _maxSpeed so _contextMax lerp ramps up smoothly"
  - "throttle ~0 (abs < 1e-6) defaults to approaching clamp — safe because speed is ~0"
  - "zero-vector guard at 1e-9 prevents NaN from normalize of degenerate position"
  - "both _contextMax and _easedSpeed lerps are unconditional on all paths (D-07 / Bug 4 preserved)"
metrics:
  duration: "~5 min"
  completed_date: "2026-06-17"
  tasks_completed: 1
  tasks_total: 3
  files_changed: 1
---

# Phase quick-260617-j6b Plan 01: Fix thrust-zero-at-galaxy-soi-exit Summary

**One-liner:** Direction-aware proximity clamp in UpdateSpeedEnvelope — receding (closing <= 0) exempts the clamp so speed ramps back up when exiting a galaxy SOI.

## Task Outcomes

### Task 1: Make the proximity clamp direction-aware in UpdateSpeedEnvelope — COMPLETE

**Commit:** f343cc3

**What changed in `UpdateSpeedEnvelope`:**

During the nearest-body scan, the method now tracks the single nearest body and its unit toward-direction:
- **Parent body branch:** `towardNearestUnit = -normalize(ship.LocalPos.ToDouble3())` — the ship-to-parent-centre direction.
- **Sibling loop:** `towardNearestUnit = normalize((body.LocalPos - ship.LocalPos).ToDouble3())` — exact in the shared parent frame.
- Zero-vector guard: skip recording if magnitude < 1e-9.

After the scan, before computing `targetMax`:
- `motionDir = (-_shipBasis.Z) * sign(_throttle01)` as a `Double3` — same forward convention as `ApplyMotion`.
- `closing = Double3.Dot(motionDir, towardNearestUnit)`
- `closing > 0` (approaching) → existing proximity clamp `Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed)`
- `closing <= 0` (receding) → `targetMax = _maxSpeed` (exempt from clamp)
- Throttle ~0 or no toward-direction recorded → default to proximity clamp (safe)

Both the `_contextMax` and `_easedSpeed` lerps remain unconditional — no speed snap on any path.

XML doc-comment updated to document the direction-aware (receding-exempt) clamp and reference the tech-debt fix.

**Verification:**
- `dotnet build EcoSpace.csproj -clp:ErrorsOnly`: 0 errors, 0 warnings
- `dotnet test EcoSpace.Tests`: 30/30 green

## Checkpoint: Play-Test REJECTED (2026-06-17)

The blocking human-verify checkpoint **failed**. In-game play-test found:

- **In-system travel unusable** — receding from the nearest body exempts the clamp and
  jumps to the global intergalactic `MaxSpeed` (2e20 m/s), so flying toward the home
  star ramps to ly/s and the ship flies straight past it. Reasonable thrust only right
  next to the start planet.
- **Intergalactic untestable** — galaxies disappear in Universe space (no mesh, no
  skybox at that tier), so there's no distance cue to the target galaxy.
- Additional HUD/target bugs surfaced (flickering nearest, galaxy stars not nearest,
  target cycling broken).

**Root problem exposed:** a single global `MaxSpeed` for every scale tier; the
proximity clamp was the only in-system ceiling.

## Disposition — Task 2 & 3 NOT run

- Approach abandoned; **recommend reverting commit f343cc3** to restore the usable
  in-system baseline.
- `thrust-zero-at-galaxy-soi-exit.md` marked `superseded` (not closed).
- Folded into the new design item
  `.planning/todos/pending/flight-speed-model-tier-and-target-aware.md`.
- Spun off 4 tech-debt todos (flight speed model, galaxy visibility in Universe space,
  HUD nearest/target in Galaxy space, target cycling).

## Deviations from Plan

None — Task 1 executed exactly as specified.

## Self-Check (Task 1)

- [x] `Scripts/Flight/FlightController.cs` committed at f343cc3
- [x] `closing > 0` → proximity clamp applied
- [x] `closing <= 0` → `targetMax = _maxSpeed`
- [x] Throttle ~0 defaults to clamp
- [x] Zero-vector normalize guarded
- [x] Both `_contextMax` and `_easedSpeed` lerps unconditional
- [x] No new `[Export]` properties or flight modes added
- [x] XML doc-comment updated
- [x] Build: 0 errors / 0 warnings
- [x] Tests: 30/30 green
