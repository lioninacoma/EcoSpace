---
phase: quick-260616-cf9
plan: "01"
subsystem: math/render
tags: [precision, refactor, univec3, skybox, worldrenderer]
dependency_graph:
  requires: []
  provides: [UniMath]
  affects: [SkyboxRenderer, WorldRenderer, CLAUDE.md]
tech_stack:
  added: [UniMath (Scripts/Math/UniMath.cs)]
  patterns: [LCA-relative UniVec3 subtraction, single-ToDouble3-on-delta, read-only consumer contract]
key_files:
  created:
    - Scripts/Math/UniMath.cs
    - EcoSpace.Tests/UniMathTests.cs
  modified:
    - Scripts/Render/SkyboxRenderer.cs
    - Scripts/Render/WorldRenderer.cs
    - EcoSpace.Tests/EcoSpace.Tests.csproj
    - CLAUDE.md
decisions:
  - UniMath placed in global namespace (Scripts/Math/) matching UniObject/GameWorld/Double3 convention
  - RelativePosition(from, to) convention returns to-from (ship->body = RelativePosition(ship, body))
  - WorldRenderer star-dir sign re-derived: UniMath.RelativeMetres(ship, star) = star-ship directly; old explicit negation dropped
  - FlightController and Hud NOT refactored (single-frame, same-scale, already exact; LCA path would add overhead with zero precision gain and risk parent-frame special-case bugs)
  - UniMath xUnit tests added (Part B landed cleanly; 12 new tests, 28 total passing)
metrics:
  completed: "2026-06-16"
  duration: ~15 min
  tasks_completed: 4
  files_changed: 6
---

# Phase quick-260616-cf9 Plan 01: Refactor Math to Maximize UniVec3 Precision — Summary

**One-liner:** Global-namespace `UniMath` helper centralizes LCA-relative UniVec3 position math; SkyboxRenderer and WorldRenderer refactored to consume it; 12 new precision unit tests confirm exact integer Units cancellation at galaxy scale.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Create UniMath (global namespace, UniVec3-based, read-only) | 0d3795c | Scripts/Math/UniMath.cs |
| 2 | Refactor SkyboxRenderer to UniMath; delete dead helpers | 181ca56 | Scripts/Render/SkyboxRenderer.cs |
| 3 | Centralize WorldRenderer cross-frame math; audit Flight/Hud | f370857 | Scripts/Render/WorldRenderer.cs |
| 4 | Add CLAUDE.md convention; UniMath unit test; full build verify | 7567e80 | CLAUDE.md, EcoSpace.Tests/*.csproj, EcoSpace.Tests/UniMathTests.cs |

## What Was Built

### UniMath (Task 1)

`Scripts/Math/UniMath.cs` — public static class in the global namespace (no namespace wrapper, matching Double3/Long3/UniVec3/UniObject/GameWorld convention). Implements the full LCA-relative UniVec3 precision strategy from the plan's `<precision_model>`:

- `FindLca(a, b, objs)` — ports SkyboxRenderer.FindLca verbatim in behaviour: HashSet of a's ancestors, walk b upward. Same `(uint)` bounds + null + cycle guards.
- `ToAncestorFrame(node, ancestorIdx, objs)` — accumulates `node.LocalPos` into the ancestor's child-frame entirely in UniVec3 via per-level `pos = pos.Convert(parentScale) + parent.LocalPos`. This is the key advance over the old metres-based `PositionRelativeToAncestor`: each step's `Normalize()` inside the UniVec3 constructor re-splits offset into integer Units, restoring precision after every level rather than accumulating float error.
- `RelativePosition(from, to, objs, out UniVec3 result)` — the primary precision primitive. Walks both ends to the LCA via `ToAncestorFrame`, then calls `toFrame - fromFrame` (both at the same child-scale). `UniVec3.operator-` does `a.Units - b.Units` — an exact 64-bit integer subtraction regardless of magnitude. The large common-ancestor offset never enters the arithmetic.
- `RelativeMetres(from, to, objs)` — sole sanctioned `ToDouble3()` call, applied to the small differenced delta from `RelativePosition`.
- `Distance(a, b, objs)` — scalar metres via `RelativeMetres`.

Read-only consumer contract enforced: no mutations of UniObject, LocalPos, ChildIndices, no TranslatePos.

### SkyboxRenderer Refactor (Task 2)

`SyncSkyPoints` now calls `UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni)` then `Double3 delta = hasCommonAncestor ? relUni.ToDouble3() : Double3.Zero;`. Direction is identical: old `RelativePosition(body, ship)` returned `body − ship`; new `UniMath.RelativePosition(from=ship, to=body)` returns `body − ship`. All downstream guards, shader pushes, `_skyDirs` cache, `MaxStars`, `MaxDiscAngle`, and `GetSkyDirection` are byte-for-byte behaviourally unchanged.

Deleted three dead private helpers: `RelativePosition`, `FindLca`, `PositionRelativeToAncestor`. `using System.Collections.Generic` retained (Dictionary/List still used by `_skyDirs` and `SyncSkyPoints` params).

### WorldRenderer Refactor (Task 3)

`ComputeStarRenderPosFromHierarchy` replaced the manual two-step metres accumulation (`planetInStar + ship.LocalPos.ToDouble3()` then `-shipInStar`) with `UniMath.RelativeMetres(ship, star, gameObjects)`. Sign re-derived: `RelativeMetres(from=ship, to=star)` returns `star − ship` directly — the old explicit negation is dropped. Added `Double3.Zero` fallback for the no-LCA edge case. `RenderBodyAt` and all other WorldRenderer math unchanged (floating-origin anchoring, sibling/parent paths, radius math, `_lastRenderPositions`).

**FlightController and Hud audit (no changes made):** Both consumers operate strictly within a single same-scale frame: FlightController uses `ship.LocalPos.Magnitude()` (distance to parent at frame origin) and `UniVec3.Distance(ship.LocalPos, body.LocalPos)` for same-parent siblings; Hud uses `ship.LocalPos.ToDouble3() * -1.0` for parent and `body.LocalPos.ToLocalDouble(ship.LocalPos)` for siblings. All math is SOI-bounded and already exact. Routing these through UniMath's cross-frame LCA path would add overhead with zero precision gain and risk the documented parent-frame special-case bugs (Hud: must NOT subtract parent.LocalPos from ship.LocalPos — mismatched frame; FlightController Bug 3). No refactor warranted.

### CLAUDE.md Convention + Unit Tests (Task 4)

**Part A:** New `## Position Math (UniVec3 / UniMath)` subsection added under `## Module Design` in Conventions. Captures: UniVec3-first strategy, subtract-in-UniVec3/ToDouble3-once pattern, full UniMath public API list with direction conventions, and the cross-scale Convert trap warning. GSD markers untouched.

**Part B:** 12 new xUnit tests in `EcoSpace.Tests/UniMathTests.cs`:
- `FindLca` correctness (ship+AlphaCen→Galaxy, ship+PlanetB→Star, self-LCA)
- `RelativePosition` result scale matches LCA child-scale; disconnected hierarchy returns false
- `RelativeMetres` antisymmetry at two scales (galaxy: 1e-3 m tol; star: 1e-6 m tol)
- `Distance` positive and symmetric
- **Precision headroom assertion:** two bodies placed 1.0 m apart at 4e16 m Galaxy-space offset; `RelativeMetres` recovers the 1.0 m separation to < 1e-9 m error — demonstrating exact integer Units cancellation that the old absolute-from-root metres walk could not deliver at this scale.

`UniMath.cs` added to `EcoSpace.Tests.csproj` via `<Compile Include>` (no Godot Node types in UniMath; compiled cleanly).

## Verification

**Godot game build:** `dotnet build EcoSpace.csproj -c Debug` → **0 errors, 0 warnings**.

**Test suite:** `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj` → **28 passed, 0 failed** (16 TierClassifier + 12 UniMath).

**Grep checks:**
- `SkyboxRenderer.cs` contains no `FindLca`/`PositionRelativeToAncestor` declarations; calls `UniMath.RelativePosition` at line 149.
- `Scripts/Math/UniMath.cs` exists in the global namespace (no namespace wrapper); `CLAUDE.md` contains "UniMath".

## MVP-Scale Equivalence Reasoning

At current MVP scene scale, LCA(ship, Galaxy-space siblings like AlphaCen/Barnard/Sirius) = Galaxy (index 1), which sits at the origin of Root space. The LCA-frame offsets for ship-side and body-side are both SOI-bounded (Galaxy-space separations ≤ ~1e17 m, well within double precision). The UniMath UniVec3 walk produces the same metres delta as the old SkyboxRenderer metres walk — the precision win is headroom at true Universe scale (where absolute-from-root values reach ~1e30 m and the old walk would lose low bits), not a behaviour change at MVP scale.

## Deviations from Plan

None — plan executed exactly as written. All four tasks completed. Part B (UniMath unit tests) landed cleanly without any link-compile issues; no skip was required.

## Audit: FlightController and Hud

FlightController and Hud audited — all position math is single-frame, same-scale, already exact; no UniMath refactor warranted (cross-frame LCA path would add cost and risk the parent-frame special cases without precision gain).

## Self-Check

- [x] `Scripts/Math/UniMath.cs` — verified created
- [x] `Scripts/Render/SkyboxRenderer.cs` — verified dead helpers removed, UniMath called
- [x] `Scripts/Render/WorldRenderer.cs` — verified `ComputeStarRenderPosFromHierarchy` updated
- [x] `EcoSpace.Tests/UniMathTests.cs` — verified created, 12 tests pass
- [x] `EcoSpace.Tests/EcoSpace.Tests.csproj` — verified UniMath added to Compile ItemGroup
- [x] `CLAUDE.md` — verified "Position Math (UniVec3 / UniMath)" section added
- [x] Commits 0d3795c, 181ca56, f370857, 7567e80 verified in git log

## Self-Check: PASSED
