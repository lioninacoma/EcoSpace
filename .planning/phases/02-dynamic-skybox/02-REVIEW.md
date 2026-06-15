---
phase: 02-dynamic-skybox
reviewed: 2026-06-15T00:00:00Z
depth: standard
files_reviewed: 9
files_reviewed_list:
  - Scripts/TierClassifier.cs
  - Scripts/Render/SkyboxRenderer.cs
  - Scripts/Render/StarRendering.cs
  - Scripts/Render/WorldRenderer.cs
  - Shaders/skybox.gdshader
  - Scripts/TestSetup.cs
  - Scripts/UniObject.cs
  - EcoSpace.Tests/TierClassifierTests.cs
  - EcoSpace.Tests/EcoSpace.Tests.csproj
findings:
  critical: 1
  warning: 6
  info: 5
  total: 12
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-06-15
**Depth:** standard
**Files Reviewed:** 9
**Status:** issues_found

## Summary

Phase 02 adds a dynamic skybox: `TierClassifier` (pure re-tier logic), `SkyboxRenderer`
(per-frame sky-point push), `StarRendering` (shared mesh/sky appearance model), updates to
`WorldRenderer`, the `skybox.gdshader`, test scene data, and a unit-test project.

The architecture is sound and the read-only contract is largely respected — both renderers
consume `GameWorld` state without calling `TranslatePos` or mutating `UniVec3`/`LocalPos`/
`ChildIndices`. The double-precision hierarchy walk in `AbsolutePositionInRoot` correctly
avoids the interstellar-distance precision collapse warned about in research.

The most serious finding is that the unit-test suite for this phase no longer tests the
shipped brightness model: `TierClassifierTests` re-implements an obsolete linear
`LuminosityScale`/`MinBrightFloor` formula inline, while the production code in
`StarRendering` uses a logarithmic magnitude model. The tests pass but verify nothing about
the real code — a silent regression-guard failure. Several per-frame heap allocations in the
`WorldRenderer` hot path and a shared-mutable-static brightness knob round out the warnings.

## Critical Issues

### CR-01: Brightness unit tests validate a dead formula, not the shipped model

**File:** `EcoSpace.Tests/TierClassifierTests.cs:188-229`
**Issue:** The two `MinBrightFloor_*` tests claim to verify the D-19 brightness model "using
the exact same formula as SkyboxRenderer.SyncSkyPoints." They do not. They re-implement an
old linear model inline:

```csharp
float rawAlpha = (float)(luminosity * luminosityScale / (dist * dist));
float brightness = System.Math.Max(rawAlpha, floor);
```

But the production code (`StarRendering.ApparentBrightness`, used by both `SkyboxRenderer`
and `WorldRenderer`) is a logarithmic magnitude model with entirely different constants:

```csharp
double flux  = luminosity / (distMeters * distMeters);
double bright = (System.Math.Log10(flux) - LogFluxFloor + Exposure) * Contrast;
return (float)System.Math.Clamp(bright, 0.0, 1.0);
```

`LuminosityScale` (2e35) and `MinBrightFloor` (0.1) no longer exist anywhere in source
(grep confirms references only in `.planning/` docs, the test file, and a stale shader
comment). The tests never call `StarRendering`, so they compile and pass while asserting
properties of code that was deleted. The brightness model — the headline deliverable of the
quick-task `260615-m4b` — therefore has zero real test coverage, and a future regression in
`ApparentBrightness` would not be caught. This is a correctness/verification defect: the
phase's stated test guarantee is false.

**Fix:** Rewrite both tests to call the actual API and assert real behavior. Example:

```csharp
[Fact]
public void ApparentBrightness_ClampedToZero_ForFaintDistantBody()
{
    StarRendering.Exposure = 0f;
    float b = StarRendering.ApparentBrightness(luminosity: 0.001, distMeters: 1e26);
    Assert.Equal(0f, b);                       // log-floor clamps to 0
}

[Fact]
public void ApparentBrightness_InRange_ForSiriusLikeBody()
{
    StarRendering.Exposure = 0f;
    float b = StarRendering.ApparentBrightness(luminosity: 25.4, distMeters: 8.13e16);
    Assert.InRange(b, 0f, 1f);
    Assert.True(b > StarRendering.ApparentBrightness(0.0035, 5.63e16)); // brighter than Barnard
}
```

To compile, add `<Compile Include="..\Scripts\Render\StarRendering.cs" />` to
`EcoSpace.Tests.csproj` (it pulls in only `using Godot;`, already referenced via GodotSharp).
Note `StarRendering.Exposure` is a mutable static — set it explicitly per test to avoid
cross-test bleed (see WR-02).

## Warnings

### WR-01: Per-frame heap allocations in `WorldRenderer.SyncBodies` hot path

**File:** `Scripts/Render/WorldRenderer.cs:213, 217`
**Issue:** `SyncBodies()` runs every frame and allocates two new collections each call:

```csharp
var activeIndices = new HashSet<int>();
var renderPositions = new Dictionary<int, Vector3>();
```

The phase brief explicitly calls out per-frame allocations in `SyncBodies` as a focus area.
`SkyboxRenderer` deliberately uses preallocated arrays (`_dirs`/`_colors`/`_sizes`) and a
reused dictionary to avoid this; `WorldRenderer` does not follow the same discipline. These
produce steady GC pressure proportional to body count every frame.

**Fix:** Promote both to readonly instance fields and `.Clear()` them at the top of
`SyncBodies` (the same pattern already used for `_lastRenderPositions`):

```csharp
private readonly HashSet<int> _activeIndices = [];
private readonly Dictionary<int, Vector3> _renderPositions = [];
// in SyncBodies():
_activeIndices.Clear();
_renderPositions.Clear();
```

### WR-02: `StarRendering.Exposure` is a mutable public static — shared global state

**File:** `Scripts/Render/StarRendering.cs:32`
**Issue:** `public static float Exposure = 0f;` is process-global mutable state written via
`WorldRenderer.StarBrightness`. With a single WorldRenderer this is benign, but: (1) it
survives across scene reloads and (in the editor/tests) across runs, so an exported value
set in one scene leaks into the next; (2) any unit test touching `ApparentBrightness`
mutates it for all other tests (xUnit shares the process), creating order-dependent
flakiness once CR-01 is fixed; (3) it violates the project convention that "all state is
owned by TestSetup instance" (CLAUDE.md). A `static` field is not thread-safe either, though
the project is single-threaded today.

**Fix:** Either pass exposure as a parameter to `ApparentBrightness(lum, dist, exposure)`, or
document and reset it deterministically. At minimum, tests must set it explicitly before each
assertion.

### WR-03: `SkyboxRenderer` silently caps visible sky bodies at 8 with no diagnostic

**File:** `Scripts/Render/SkyboxRenderer.cs:34, 112`
**Issue:** The loop `for (int i = 0; i < objs.Count && count < MaxStars; i++)` stops at
`MaxStars = 8`. If more than 8 bodies classify as `NextTierSkybox`, the extras are silently
dropped with no warning, and which 8 survive depends on `GameObjects` insertion order rather
than brightness/proximity. The shader's `MAX_STARS` is also 8 — if the two ever drift apart,
the C# side would push arrays longer than the shader reads (also silent). For the MVP scene
(3 siblings) this never triggers, but it is a latent correctness/maintainability trap.

**Fix:** Emit a one-time `GD.PrintErr` (or `push_warning`) when `count` would exceed
`MaxStars`, and add a comment/compile-time assertion tying C# `MaxStars` to shader
`MAX_STARS`. If priority matters, sort by apparent brightness before truncating.

### WR-04: `pixelAngle` floor on disc size can produce `size >= 1.0`, breaking the shader disc

**File:** `Scripts/Render/SkyboxRenderer.cs:148-149`, `Shaders/skybox.gdshader:60`
**Issue:** `_sizes[count] = 1f - Mathf.Cos(eff)` where `eff = Max(theta, pixelAngle)`. For a
very near, large body (e.g. the home star promoted into the sky set, or any body where
`theta` approaches or exceeds ~π/2 — `RadiusMeters/dist` is large), `1 - cos(eff)` approaches
or exceeds 1. The shader computes `smoothstep(1.0 - star_sizes[i], 1.0, d)`; if
`star_sizes[i] >= 1.0` the lower edge becomes `<= 0`, and with `>= 1.0` exactly the inner
bound equals/exceeds the outer bound, producing undefined/degenerate smoothstep behavior (a
hard full-hemisphere fill or NaN ramp). `AngularRadius` is also unbounded (it is a small-angle
`r/d` approximation, not `asin`), so `theta` can exceed 1 radian for close bodies, making
`eff` and thus `1-cos(eff)` jump around. There is no clamp.

**Fix:** Clamp the disc size to a safe sub-1 range and use a proper angular radius:

```csharp
double theta = StarRendering.AngularRadius(body.RadiusMeters, len); // r/d small-angle
float eff = Mathf.Min(Mathf.Max((float)theta, pixelAngle), 1.5533f); // < π/2
_sizes[count] = Mathf.Min(1f - Mathf.Cos(eff), 0.999f);
```

### WR-05: `SkyboxRenderer` never clears stale shader uniforms when sky set empties

**File:** `Scripts/Render/SkyboxRenderer.cs:160-167`
**Issue:** When `count == 0`, the code sets `star_count = 0` but deliberately skips pushing
the arrays "to avoid sending empty arrays." That is fine for the disc loop (it reads
`star_count`). But the previous frame's `star_dirs`/`star_colors`/`star_sizes` remain on the
material. This is only safe as long as the shader strictly honors `star_count` — which it
currently does. However, the comment frames skipping the push as an optimization when it is
actually a correctness dependency on shader behavior. If a future shader edit reads a fixed
range or `MAX_STARS`, stale star data would reappear. Couple this with WR-03 (count/MAX_STARS
divergence) and it is a fragile contract.

**Fix:** Document that `star_count = 0` is the authoritative gate and the shader MUST loop
only to `star_count`; or push the (cheap, 8-element) arrays unconditionally so material state
always matches the current frame. Prefer the latter for robustness.

### WR-06: `len < 1e-30` coincident-body fallback points all overlapping bodies the same way

**File:** `Scripts/Render/SkyboxRenderer.cs:129-130`
**Issue:** When ship and body are effectively coincident (`len < 1e-30`), `dir3 = Vector3.Up`.
For sky bodies (siblings light-years away) this is unreachable in practice, but if any
ancestor body ever coincides with the ship the direction is fabricated as Up and
`AngularRadius`/`ApparentBrightness` both early-return 0/0, so the body silently vanishes.
This is acceptable degradation but undocumented as a behavior (the comment only says it is
guarded). Minor robustness note rather than a live bug for the MVP scene.

**Fix:** Add a comment clarifying that a coincident sky body is intentionally rendered as a
zero-size, zero-brightness point (invisible), and that `Vector3.Up` is an arbitrary
placeholder direction never actually displayed.

## Info

### IN-01: Stale `StarAngularSize` reference in shader doc comment

**File:** `Shaders/skybox.gdshader:42-44`
**Issue:** The `star_sizes` uniform comment says "A small CONSTANT for every star ...
SkyboxRenderer.StarAngularSize. Apparent magnitude is conveyed by star_colors[].a ... NOT
disc size." This is doubly wrong now: `StarAngularSize` does not exist, and `SkyboxRenderer`
computes a *physical, per-star* angular disc (`1 - cos(theta)`), not a constant. Misleading
documentation for the next maintainer.
**Fix:** Update the comment to describe the per-star physical angular radius floored at one
pixel.

### IN-02: Stale `MinBrightFloor`/`LuminosityScale` references in test header

**File:** `EcoSpace.Tests/TierClassifierTests.cs:24-27, 191-204`
**Issue:** Test header and comments document a `MinBrightFloor`/`LuminosityScale` model that
no longer exists. Tied to CR-01; even after the tests are rewritten, these comments should be
purged so they do not re-seed the obsolete mental model.
**Fix:** Remove obsolete references; describe the logarithmic magnitude model.

### IN-03: `IsStarBody` matches by magic string "STAR"

**File:** `Scripts/Render/WorldRenderer.cs:371`
**Issue:** `body.Name == "STAR"` is a stringly-typed identity check. `TestSetup` names the
home star "STAR" but the siblings "ALPHA CEN" / "BARNARD" / "SIRIUS", so they would NOT be
treated as stars by `WorldRenderer` if they ever entered the mesh tier (they have
`Luminosity > 0` and are emissive). For Phase 2 siblings are never meshes, so no live bug,
but the heuristic is fragile and inconsistent with the `Luminosity`-based model everywhere
else.
**Fix:** Identify stars by `body.Luminosity > 0.0` (or an explicit `Type`/flag) rather than
by name, so the mesh path stays coherent if a sibling is ever promoted.

### IN-04: Unused locals `_sib1`/`_sib2`/`_sib3` flagged by underscore-prefix convention

**File:** `Scripts/TestSetup.cs:137, 144, 151`
**Issue:** Local variables use an `_` prefix (`int _sib1 = ...`), which the project reserves
for private fields (CLAUDE.md naming conventions). They are local indices used only on the
next lines. Cosmetic, but violates the documented convention and reads like field access.
**Fix:** Rename to `sib1`/`sib2`/`sib3` (camelCase locals).

### IN-05: `ComputeStarRenderPosFromHierarchy` uses `ToDouble3()` cross-frame — precision note

**File:** `Scripts/Render/WorldRenderer.cs:349-354`
**Issue:** This path sums `planet.LocalPos.ToDouble3()` (Star-space metres) with
`ship.LocalPos.ToDouble3()` (Planet-space metres). The two are produced from different-scale
UniVec3 values; the addition is dimensionally valid (both yield metres) but bypasses the
`UniVec3` operator path that handles scale conversion and integer-unit preservation. At the
~1.5e11 m star distance the result is direction-only and the float cast is harmless, but this
is exactly the `ToDouble3()`-collapses-Units pattern `SkyboxRenderer.AbsolutePositionInRoot`
was written to avoid. For Galaxy/Universe tiers (placeholder factors) this could lose
precision. Not a live bug at MVP scale; flagged for consistency with the documented Pitfall 5
mitigation.
**Fix:** When the Galaxy/Universe tiers are exercised, route this through the same
double-precision Units*Scale+Offset accumulation used in `AbsolutePositionInRoot` rather than
`ToDouble3()`.

---

_Reviewed: 2026-06-15_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
