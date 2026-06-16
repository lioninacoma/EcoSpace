---
phase: quick
plan: 260616-riw
type: execute
wave: 1
depends_on: []
files_modified:
  - Scripts/Render/SkyboxRenderer.cs
  - EcoSpace.Tests/UniMathTests.cs
autonomous: false
requirements: [RND-05, D-22, D-40]

must_haves:
  truths:
    - "While the ship is inside the home galaxy's SOI (the galaxy is an ancestor of the ship), the home galaxy is NOT pushed into the galaxy sky uniforms."
    - "Only the 2 OTHER galaxies render as discs from inside the home system (phase 03 must-have truth #2)."
    - "The Star branch, the _skyDirs handoff cache, and all GameWorld state are unchanged (guard is strictly read-only and galaxy-branch-local)."
    - "Build stays clean; the existing test suite (28 green) plus the new ancestry assertion all pass."
  artifacts:
    - path: "Scripts/Render/SkyboxRenderer.cs"
      provides: "Home-galaxy suppression guard in the galaxy branch of SyncSkyPoints"
      contains: "FindLca"
    - path: "EcoSpace.Tests/UniMathTests.cs"
      provides: "Ancestry-predicate test proving FindLca(ship, ancestorGalaxy) == galaxy.Index"
      contains: "FindLca"
  key_links:
    - from: "Scripts/Render/SkyboxRenderer.cs"
      to: "UniMath.FindLca"
      via: "ancestry test in galaxy branch"
      pattern: "UniMath\\.FindLca\\(ship, body, objs\\)"
---

<objective>
Suppress the home galaxy in `SkyboxRenderer` while the ship is inside that galaxy's SOI
(the galaxy is an ancestor of the ship). Today every Universe-space galaxy classifies
`NextTierSkybox` regardless of ancestry, so the home galaxy renders as a large disc in its
own direction alongside the 2 other galaxies — violating phase 03 must-have truth #2 ("the
2 *other* galaxies"). This resolves the open design question deferred from plan 03-01
(03-01-SUMMARY.md line 135): the locked decision is to SUPPRESS (skip) the home galaxy, NOT
render it as a Milky-Way band.

Purpose: Make the in-system sky match must-have truth #2 — exactly the 2 other galaxies as discs.
Output: A galaxy-branch-local read-only guard in `SyncSkyPoints` plus a pure-C# ancestry test.
</objective>

<execution_context>
@$HOME/.claude/gsd-core/workflows/execute-plan.md
@$HOME/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md
@Scripts/Render/SkyboxRenderer.cs
@Scripts/Math/UniMath.cs
@Scripts/UniObject.cs
@EcoSpace.Tests/UniMathTests.cs
@.planning/phases/03-cross-galaxy-travel/03-01-SUMMARY.md
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add home-galaxy ancestry guard to the galaxy branch of SyncSkyPoints</name>
  <files>Scripts/Render/SkyboxRenderer.cs</files>
  <action>
    In `SyncSkyPoints`, suppress any galaxy body that is an ancestor of the ship (i.e. the
    ship is currently inside that galaxy's SOI). Use the ancestry test
    `UniMath.FindLca(ship, body, objs) == body.Index`.

    Justification for choosing FindLca over a hand-rolled ParentIndex walk: `FindLca` (confirmed
    against UniMath.cs lines 52-77) builds the ship's ancestor set by walking `ParentIndex` to
    Root, then walks the body's chain until it hits that set, returning the LCA index. When the
    body IS an ancestor of the ship, the LCA of (ship, body) is the body itself — so
    `FindLca(ship, body, objs) == body.Index` is exactly "body is an ancestor of (or equal to)
    the ship." The ship-self case is already excluded earlier (`body.Index == shipIdx` continue
    at the existing guard), so a body reaching this point can only be a proper ancestor. FindLca
    is the project-sanctioned hierarchy primitive (CLAUDE.md UniMath conventions), is robust at
    all scales (pure integer index walk, no metres), already has bounds/null/cycle guards, and is
    already link-compiled into the test project — so the predicate is unit-testable without Godot.
    This avoids duplicating a ParentIndex walk inline.

    Placement and scope: the guard MUST live inside the galaxy branch (the
    `if (body.ObjectType == UniObject.Type.Galaxy && galCount < MaxGalaxies)` block around the
    `_galDirs[galCount] = dir3;` partition). Add a `continue`-style skip: when the galaxy is an
    ancestor of the ship, do NOT write any `_gal*` array entry and do NOT increment `galCount`.
    Prefer guarding at the top of the galaxy branch so the body is dropped before any galaxy
    uniform write. Do NOT place the test before the Star branch and do NOT touch the Star branch,
    the `_skyDirs` handoff cache, or `count`. The guard is strictly read-only — it calls only
    `UniMath.FindLca` (read-only consumer) and mutates no GameWorld / UniVec3 / LocalPos state.

    Add a short comment citing must-have truth #2 and the deferred design question
    (03-01-SUMMARY.md line 135) so the rationale is discoverable: the home galaxy is an ancestor
    of the ship while in-system, so it is suppressed; only the 2 other (non-ancestor) galaxies
    render as discs.

    No shader edit. No change to galaxy authoring in TestSetup. No new fields, no signature changes.
  </action>
  <verify>
    <automated>cd /c/Users/frede/workspace/godot/eco-space && dotnet build EcoSpace.Tests/EcoSpace.Tests.csproj -c Debug 2>&1 | grep -iE "error|Build succeeded"</automated>
  </verify>
  <done>
    `SkyboxRenderer.SyncSkyPoints` skips a galaxy body when `UniMath.FindLca(ship, body, objs) ==
    body.Index`, the skip lives only inside the galaxy branch (Star branch and `_skyDirs`
    untouched), no `_gal*` entry is written and `galCount` is not incremented for the suppressed
    galaxy, and the build is clean.
  </done>
</task>

<task type="auto">
  <name>Task 2: Add a pure-C# ancestry-predicate test proving the suppression rule</name>
  <files>EcoSpace.Tests/UniMathTests.cs</files>
  <action>
    Add an xUnit fact to `UniMathTests` that proves the exact predicate the guard uses, against
    the existing canonical 7-node hierarchy from `BuildHierarchy()` (Index 1 = Galaxy in Universe
    space, ancestor of Index 4 = Ship in Planet space).

    SkyboxRenderer itself is Godot-`Node`-coupled and is NOT link-compiled into the test project
    (the .csproj link-compiles only UniObject, TierClassifier, StarRendering, the Math types, and
    UniMath — not any `Render` Node). Per the constraints, do NOT invent a Godot test harness.
    Instead assert the pure ancestry predicate directly on `UniMath.FindLca`, which IS the logic
    the guard delegates to:

    - `FindLca(ship, homeGalaxy, objs) == homeGalaxy.Index` is TRUE — the home galaxy (Index 1)
      is an ancestor of the ship (Index 4), so it would be suppressed.
    - A NON-ancestor galaxy is NOT suppressed: confirm a body that is not an ancestor of the ship
      yields `FindLca(ship, body, objs) != body.Index`. Use a sibling-style body already in the
      hierarchy whose LCA with the ship is an intermediate node, not the body itself
      (e.g. AlphaCen at Index 5, a Galaxy-space sibling star — its LCA with the ship is the
      Universe-space Galaxy at Index 1, not Index 5). This proves the predicate does not
      over-suppress the other galaxies. (The two "other galaxies" in the real scene are
      non-ancestors of the in-system ship by the same logic; AlphaCen stands in as the
      non-ancestor case the unit hierarchy already provides.)

    Mirror the existing test style (xUnit `[Fact]`, `BuildHierarchy()` helper, `Assert.Equal` /
    `Assert.NotEqual`). Add a comment tying the test to the suppression guard in
    `SkyboxRenderer.SyncSkyPoints` and to must-have truth #2 so the intent survives.
  </action>
  <verify>
    <automated>cd /c/Users/frede/workspace/godot/eco-space && dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj 2>&1 | grep -iE "Passed!|Failed!|error"</automated>
  </verify>
  <done>
    A new fact in `UniMathTests` asserts `FindLca(ship, homeGalaxy) == homeGalaxy.Index` (ancestor
    → suppressed) and a non-ancestor body yields `FindLca(ship, body) != body.Index` (not
    suppressed). `dotnet test` reports all tests passing (29+ green, previously 28).
  </done>
</task>

<task type="checkpoint:human-verify" gate="blocking">
  <what-built>
    A read-only guard in `SkyboxRenderer.SyncSkyPoints` that skips the home galaxy while the ship
    is inside its SOI (galaxy is an ancestor of the ship), plus a pure-C# unit test proving the
    ancestry predicate. No shader edit; no TestSetup authoring change.
  </what-built>
  <how-to-verify>
    1. Launch the game from the home system (spawn position) in the Godot editor.
    2. Look around the full sky. Confirm exactly 2 galaxy discs are visible — the DEST galaxy
       (warm orange-gold, in the +Z direction from spawn) and the ELLIPTICAL CLUSTER (warm golden
       yellow). The home galaxy must NOT appear as a large disc in any direction.
    3. Confirm the star points are unchanged (no regression in the Star branch / sky stars).
    4. Confirm no black sky, no flicker, no NaN-style artifacts (the prior 03-01 NaN fix must
       still hold).
  </how-to-verify>
  <resume-signal>Type "approved" if exactly the 2 other galaxies render and stars are unaffected, or describe what you see.</resume-signal>
</task>

</tasks>

<verification>
- `dotnet build EcoSpace.Tests/EcoSpace.Tests.csproj` succeeds with no errors.
- `dotnet test` reports all tests passing (29+ green; previously 28).
- `SkyboxRenderer.cs` galaxy branch contains the `UniMath.FindLca(ship, body, objs) == body.Index`
  guard; Star branch and `_skyDirs` are textually unchanged.
- Human visual check confirms exactly 2 galaxy discs from the home system.
</verification>

<success_criteria>
- The home galaxy is suppressed (not pushed to galaxy uniforms) while it is an ancestor of the ship.
- Only the 2 other galaxies render as discs from inside the home system (must-have truth #2 satisfied).
- The change is C#-only, galaxy-branch-local, and strictly read-only.
- Build clean; existing 28 tests plus the new ancestry test all green.
</success_criteria>

<output>
Create `.planning/quick/260616-riw-suppress-the-home-galaxy-in-skyboxrender/260616-riw-SUMMARY.md` when done.
</output>
