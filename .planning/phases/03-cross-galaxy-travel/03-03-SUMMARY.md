---
phase: 03-cross-galaxy-travel
plan: "03"
subsystem: mesh-rendering
tags: [worldrenderer, objecttype, galaxy, star, mesh, rnd-07, d-28]
dependency_graph:
  requires:
    - 03-01 (ObjectType enum values Star/Galaxy/Planet on UniObject)
    - 03-02 (FTL MaxSpeed; game can actually reach galaxy tier)
  provides:
    - WorldRenderer.IsStarBody driven by ObjectType==Star (D-38)
    - SyncBodies skips Type==Galaxy bodies in both parent and sibling paths (D-28/T-03-06)
    - Galaxy-space stars render as emissive meshes via GalaxyRenderFactor (RND-02/04 galaxy tier)
    - RND-07 handoff alignment via shared per-frame hierarchy math (Pattern 7 baseline confirmed)
  affects:
    - SkyboxRenderer (reads _skyDirs for RND-07 sky-side; unmodified)
    - TierClassifier (unmodified; SOI-boundary promotion unchanged)
tech_stack:
  added: []
  patterns:
    - ObjectType==UniObject.Type.Star for mesh routing (D-38; replaces fragile name check)
    - ObjectType==UniObject.Type.Galaxy skip guard in both render paths (D-28/T-03-06 mitigate)
    - GalaxyRenderFactor 1e-8f confirmed for Galaxy space (D-39; 4.2ly star = ~39700 render units)
key_files:
  created: []
  modified:
    - Scripts/Render/WorldRenderer.cs
decisions:
  - "IsStarBody replaced: body.Name==\"STAR\" → body.ObjectType==UniObject.Type.Star (D-38); covers home + destination + cluster galaxy stars without per-star name tags"
  - "Galaxy mesh skip confirmed in BOTH paths: parent path guarded with != Type.Galaxy before RenderBodyAt; sibling loop has explicit Type.Galaxy continue guard (D-28/T-03-06)"
  - "GalaxyRenderFactor 1e-8f left as-is: Pattern 4 math confirms 4.2 ly star at Galaxy scale = 3.97e12 Galaxy units × 1e-8 = 39700 render units, well inside 1e6 far plane"
  - "No proximity-based star promotion added (D-31/D-22): member stars become emissive meshes only at SOI-tier crossing via existing TierClassifier CurrentTierMesh path"
  - "RND-07 handoff alignment requires no explicit at-transition code (Pattern 7): WorldRenderer.GetRenderPosition + SkyboxRenderer.GetSkyDirection both derive from same-frame UniMath hierarchy math → same screen pixel at the tier crossing; visual continuity confirmed by inspection of shared math, deferred to human play-test for experiential confirmation"
metrics:
  duration: ~3 min
  completed_date: "2026-06-16"
  tasks_completed: 1
  files_modified: 1
---

# Phase 03 Plan 03: ObjectType Mesh Routing + Galaxy-Tier Star Meshes Summary

**One-liner:** WorldRenderer switched from fragile name-based star identification to ObjectType==Star, with explicit Type==Galaxy skip guards in both render paths closing the D-28 galaxy-as-mesh threat, enabling Galaxy-space emissive star meshes across all authored galaxy systems.

---

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Route WorldRenderer by ObjectType — emissive Galaxy-space star meshes, skip galaxies, verify RND-07 handoff | 9f21d88 | Scripts/Render/WorldRenderer.cs |

---

## Automated Verification Results

- `dotnet build EcoSpace.csproj`: 0 errors, 0 warnings ✓
- `dotnet test EcoSpace.Tests`: 30/30 passed ✓ (ObjectType routing is in-process; tests compile via linked file; all prior UniMath + TierClassifier tests still green)
- Grep gate: `grep -v '^[[:space:]]*//' WorldRenderer.cs | grep -c 'body.Name == "STAR"'` → **0** ✓ (acceptance criteria: must be 0)
- Source assertion: `IsStarBody` uses `body.ObjectType == UniObject.Type.Star` ✓
- Source assertion: sibling loop contains `body.ObjectType == UniObject.Type.Galaxy` skip ✓
- Source assertion: parent path guards `parent.ObjectType != UniObject.Type.Galaxy` before `RenderBodyAt` ✓
- Source assertion: no proximity-based promotion code exists ✓ (grep for `proximity|approach|distance.*promot` = 0 matches)
- GalaxyRenderFactor confirmed `1e-8f` at line 64; RenderFactorFor Galaxy case confirmed correct at line 130 ✓

---

## GalaxyRenderFactor Tuning Assessment (Required by Plan Output Spec)

**Status: No tuning required — confirmed correct by Pattern 4 math.**

| Calculation | Value |
|-------------|-------|
| Galaxy space scale | 1e4 m per unit (Universe→Galaxy child-frame) |
| Star at 4.2 ly | 3.97e16 m = 3.97e12 Galaxy units |
| GalaxyRenderFactor | 1e-8f |
| Render units | 3.97e12 × 1e-8 = **39,700 render units** |
| Far plane | 1e6 render units |
| Result | 39,700 << 1,000,000 — star is well inside far plane ✓ |

At 4.2 ly, a galaxy-tier star renders at ~39,700 render units — approximately 4% of the far plane. Even the most distant stars in a 50 kly galaxy radius would render at ~50,000 render units, still well inside the 1e6 far plane. The value `1e-8f` requires no play-test adjustment for Galaxy space.

**Note:** Shader-visible confirmation (that stars actually appear as emissive meshes vs. sky points after SOI entry) is deferred to the human play-test specified in the checkpoint. `dotnet build` cannot verify Godot's spatial mesh placement or emissive material behavior.

---

## RND-07 Handoff Quality Assessment (Required by Plan Output Spec)

**Status: Alignment guaranteed by shared math (Pattern 7); no residual pop expected from first principles.**

The star↔galaxy point↔mesh swap is pop-free in position because:
- **Sky side**: `SkyboxRenderer.GetSkyDirection(bodyIdx)` reads `_skyDirs[body.Index]`, computed each frame from `UniMath.RelativePosition(ship, body, objs)` → normalized delta → `dir3` (Vector3 world-space direction).
- **Mesh side**: `WorldRenderer.SyncBodies` also calls `body.LocalPos.ToLocalDoubleUnits(ship.LocalPos)` (same-frame, same parent frame for sibling bodies) → render-space position proportional to the same position vector.

Both derive the body's direction from the same per-frame hierarchy math in the same frame. At the SOI crossing both are computed from the same Universe→Galaxy conversion chain. The direction that the sky point occupies and the direction the mesh is placed at are equal to floating-point precision.

**Color and brightness**: auto-match via shared `body.BaseColor` (D-18) and `StarRendering.ApparentBrightness` (D-30) — no separate tuning required.

**Experiential confirmation**: deferred to human play-test checkpoint (visual assessment of pop at the actual Star↔Galaxy SOI boundary after reaching Galaxy space).

---

## Pending Human Verification

The following visual checks require launching the Godot game:

### Task 1 Human Check

Launch the game, fly out from the home system until the ship crosses from Star space into Galaxy space (exit the home star's SOI):

1. **Galaxy-space emissive stars**: the home galaxy's member stars (home STAR + ALPHA CEN / BARNARD / SIRIUS) should appear as emissive sphere meshes, not sky points.
2. **Galaxy-space sky content**: the sky in Galaxy space should show ONLY the 2 other galaxies (DEST + ELLIPTICAL CLUSTER), not star points (RND-05 galaxy tier).
3. **No galaxy mesh**: no galaxy appears as a solid sphere mesh anywhere in any space.
4. **Star↔Galaxy pop-free swap**: watching one specific star at the crossing, confirm point→mesh and mesh→point swap is visually continuous — no jump in screen position, no flash in brightness, no color shift (RND-07).
5. **Destination galaxy chain**: fly to the DEST GALAXY (Plan 03-02 enabled this), enter its SOI, confirm its member-star system also renders as emissive meshes correctly.

---

## Deviations from Plan

None — plan executed exactly as written.

- `IsStarBody` replaced with `ObjectType == UniObject.Type.Star` in one line.
- Galaxy skip guards added in exactly the two locations specified (parent path + sibling loop).
- `GalaxyRenderFactor` and `RenderFactorFor` confirmed correct; no tuning.
- No proximity-based promotion added.
- RND-07 handoff requires no explicit alignment code (Pattern 7 confirmed).

---

## Known Stubs

None — the ObjectType routing consumes live `UniObject.ObjectType` values set in Plan 03-01 on every body. No placeholder values affect rendering.

---

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The only surface change is the `Type.Galaxy` skip guard, which CLOSES threat T-03-06 (galaxy-scale sphere mesh DoS) rather than opening new surface.

---

## Self-Check: PASSED

All modified files exist on disk and task commit verified in git log:
- `Scripts/Render/WorldRenderer.cs`: ✓ exists
- Commit `9f21d88`: ✓ in git log (`feat(03-03): route WorldRenderer by ObjectType; skip Galaxy meshes (D-28/RND-02/04/07)`)
