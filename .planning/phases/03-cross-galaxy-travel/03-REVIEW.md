---
phase: 03-cross-galaxy-travel
reviewed: 2026-06-16T18:29:17Z
depth: standard
files_reviewed: 6
files_reviewed_list:
  - Scripts/Flight/FlightController.cs
  - Scripts/Render/WorldRenderer.cs
  - Scripts/Render/SkyboxRenderer.cs
  - Scripts/UniObject.cs
  - Scripts/TestSetup.cs
  - Shaders/skybox.gdshader
findings:
  critical: 0
  warning: 5
  info: 5
  total: 10
status: issues_found
---

# Phase 3: Code Review Report

**Reviewed:** 2026-06-16T18:29:17Z
**Depth:** standard
**Files Reviewed:** 6
**Status:** issues_found

## Summary

Reviewed the Phase 03 cross-galaxy-travel changes: removal of the speed-of-light cap
in the flight model, ObjectType-based render routing that keeps Galaxy-tier bodies out
of the world-space mesh path, the procedural galaxy sky-disc shader, and the home-galaxy
skybox suppression guard. I cross-referenced the supporting precision primitives
(`UniMath`, `UniVec3`), the shared appearance model (`StarRendering`), and the tier
classifier to confirm the position-math and NaN-propagation claims in the docstrings.

Overall the high-value concerns hold up well:

- **NaN-before-normalize blackout:** The shader now guards the `cross(disc_normal,(0,1,0))`
  degeneracy *before* `normalize()` (skybox.gdshader:103-104), and the live degenerate
  case (home galaxy authored with normal `(0,1,0)`) is additionally removed from the
  galaxy array entirely by the C# suppression guard (SkyboxRenderer.cs:208-209). Both
  defenses are present — good defense-in-depth. The C# direction path also floors
  `len < 1e-30` to a safe `Vector3.Up` before dividing (SkyboxRenderer.cs:174). No
  NaN/Inf propagation path found that reaches `COLOR`.
- **Position math:** All cross-space distance/direction work routes through
  `UniMath.RelativePosition` / `RelativeMetres` (SkyboxRenderer.cs:168,
  WorldRenderer.cs:371). No hand-rolled `Units*Scale+Offset` accumulation and no
  absolute-from-root subtraction at universe scale was found in the changed files.
- **Removed SpeedOfLight cap:** `MaxSpeed`'s setter clamps out negatives/NaN via
  `System.Math.Max(0.0, value)` (FlightController.cs:149) and `ApplyMotion` re-checks
  `double.IsFinite(CurrentSpeed)` before touching `TranslatePos` (FlightController.cs:465).
  With `MaxSpeed = 2e20`, `forward*CurrentSpeed*delta` stays comfortably within double
  range, so the finite guard is adequate.

No blockers. Five warnings (logic/robustness) and five info items are below.

## Warnings

### WR-01: Non-finite `MaxSpeed` export bypasses the setter guard and can poison motion

**File:** `Scripts/Flight/FlightController.cs:146-150, 431, 465`
**Issue:** The `MaxSpeed` setter relies on `System.Math.Max(0.0, value)` to "block negative
and NaN inputs" (per the docstring). For NaN this is fragile: `System.Math.Max(0.0, double.NaN)`
returns `NaN` in .NET (it does **not** return `0.0`), because `Math.Max` propagates NaN.
So a NaN editor export survives the setter and lands in `_maxSpeed`. From there:
- `UpdateSpeedEnvelope` line 431 computes `nearest = _maxSpeed / Math.Max(_speedPerMeter, 1.0)`
  → `NaN`, then `Mathf.Clamp(nearest * _speedPerMeter, _minSpeed, _maxSpeed)` with NaN
  operands yields a NaN/garbage `_contextMax`, which leaks into `_easedSpeed`.

The single `double.IsFinite(CurrentSpeed)` guard in `ApplyMotion` (line 465) does catch this
before `TranslatePos`, so it is contained — but the docstring's claim that the setter
"blocks NaN inputs (T-03-04 mitigation)" is false, and the speed envelope state silently
becomes NaN. The comment is load-bearing safety documentation that does not match behavior.
**Fix:** Reject non-finite explicitly in the setter so the invariant the docstring promises
actually holds:
```csharp
set => _maxSpeed = double.IsFinite(value) ? System.Math.Max(0.0, value) : _maxSpeed;
```
Apply the same pattern to `MinSpeed`, `SpeedPerMeter`, and `SpeedEasing` setters, whose
docstrings make the same NaN-blocking claim.

### WR-02: Open-space speed caps at half `MaxSpeed`, contradicting the "allow max speed" comment

**File:** `Scripts/Flight/FlightController.cs:429-434`
**Issue:** When no bodies are found, the comment says "open space: allow max speed" and sets
`nearest = _maxSpeed / Math.Max(_speedPerMeter, 1.0)`. With the default `SpeedPerMeter = 0.5`,
`Math.Max(0.5, 1.0) = 1.0`, so `nearest = _maxSpeed`. Then line 434 computes
`targetMax = clamp(nearest * _speedPerMeter, MinSpeed, MaxSpeed) = clamp(_maxSpeed * 0.5, …)`
= `0.5 * _maxSpeed`. The intergalactic crossing therefore tops out at **half** the configured
`MaxSpeed`, undermining the D-35 "~2-minute crossing at 2e20" tuning rationale (the actual
ceiling in deep space is 1e20). The division by `Math.Max(_speedPerMeter, 1.0)` and the later
multiply by `_speedPerMeter` only cancel when `SpeedPerMeter >= 1.0`.
**Fix:** Make the open-space fallback target `MaxSpeed` directly rather than round-tripping
through the divide/multiply:
```csharp
if (nearest == double.MaxValue)
{
    // Open space — no surface to scale against; target MaxSpeed directly.
    _contextMax = Mathf.Lerp(_contextMax, _maxSpeed, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
    double tSpeed = _throttle01 * _contextMax;
    _easedSpeed = Mathf.Lerp(_easedSpeed, tSpeed, Mathf.Clamp(_speedEasing * delta, 0.0, 1.0));
    CurrentSpeed = _easedSpeed;
    return;
}
```
or, minimally, set `nearest = _maxSpeed / Math.Max(_speedPerMeter, EPSILON)` so the later
multiply by `_speedPerMeter` recovers `_maxSpeed` for any positive `SpeedPerMeter`.

### WR-03: Galaxy disc tangent basis is non-orthonormal for non-unit/tilted normals

**File:** `Shaders/skybox.gdshader:103-109`
**Issue:** `galaxy_disc_coords` builds `t1 = normalize(c)` (unit) but then
`t2 = cross(disc_normal, t1)`. `t2` is **not** normalized, and `disc_normal` is generally
**not** a unit vector — the authored orientations in TestSetup are e.g. `(0.2, 0.98, 0)`,
`(0.3, 0.95, 0.1)` (length ≈ 1.0 by luck but not guaranteed), and the `GalaxyOrientation`
field is documented as a "disc normal" with no normalization step anywhere in the C# push
(`SkyboxRenderer.cs:216-217` copies it verbatim into `galaxy_orientations.xyz`). The result
is that `t2`'s length scales with `|disc_normal|`, so the disc-plane UV is anisotropically
stretched/skewed depending on the authored vector magnitude. For the elliptical/spiral
procedural functions this distorts arm spacing and aspect ratio in a way that is silent and
hard to diagnose. (No NaN risk — the `dot(c,c) < 0.001` guard prevents the only blackout
path.)
**Fix:** Normalize the basis explicitly so the disc plane is scale-independent of the authored
normal:
```glsl
vec3 n  = normalize(disc_normal);
vec3 c  = cross(n, vec3(0.0, 1.0, 0.0));
if (dot(c, c) < 0.001) c = cross(n, vec3(1.0, 0.0, 0.0));
vec3 t1 = normalize(c);
vec3 t2 = normalize(cross(n, t1));
vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
```

### WR-04: `RenderFactorFor` default arm silently maps unexpected spaces to Star factor

**File:** `Scripts/Render/WorldRenderer.cs:126-133`
**Issue:** The `switch` falls through to `StarRenderFactor` for `Space.Root` and any future/
unexpected space. Combined with `ship.LocalPos.Scale` being used as the divisor everywhere
(e.g. line 378, 431, 446), a ship that ever reaches `Space.Root` (Scale = -1, per
`UniObject.Scale`) would produce negative observer factors and inverted/garbage render
positions and radii. The current scene never puts the ship at Root, so this is latent, but
the silent default hides the misconfiguration rather than surfacing it.
**Fix:** Either assert/log on the unexpected case, or guard the `ship.LocalPos.Scale <= 0`
divisor at the top of `SyncBodies` and bail:
```csharp
if (ship.LocalPos.Scale <= 0.0) return; // Root/invalid frame — nothing to render
```

### WR-05: `ComputeStarRenderPosFromHierarchy` uses exact `== 0.0` to detect the no-LCA sentinel

**File:** `Scripts/Render/WorldRenderer.cs:374-375`
**Issue:** The fallback test `starRelToShip.X == 0.0 && Y == 0.0 && Z == 0.0` reuses
`Double3.Zero` as a sentinel returned by `UniMath.RelativeMetres` when no common ancestor
exists. Two problems: (1) it conflates "no LCA" with "genuinely coincident star" — a star
exactly co-located with the ship would be misread as a hierarchy error and forced to
`Vector3.Up * 1e7f`, flipping the lighting direction; (2) exact float equality on a value
that has passed through `ToDouble3()` arithmetic is brittle. In practice the home hierarchy
always has an LCA so this never fires, but the sentinel-via-magic-value pattern is unsafe.
**Fix:** Prefer the boolean-returning primitive directly so the sentinel is explicit:
```csharp
if (!UniMath.RelativePosition(ship, star, gameObjects, out UniVec3 relUni))
    return Vector3.Up * 1e7f;
Double3 starRelToShip = relUni.ToDouble3();
```

## Info

### IN-01: Dead/commented constant left in place

**File:** `Scripts/Flight/FlightController.cs:41-43`
**Issue:** `// private const double SpeedOfLight = 3e8;` is commented-out code retained after
the cap removal. The XML doc above it and the inline `// no SpeedOfLight cap (Plan 03-02)`
comments already document the removal; the dead declaration adds noise.
**Fix:** Delete the commented constant; keep the explanatory doc comment only.

### IN-02: `_galColors`/`_galSizes`/`_dirs` arrays not zeroed when counts shrink

**File:** `Scripts/Render/SkyboxRenderer.cs:212-234, 240-257`
**Issue:** The uniform scratch arrays are written only up to `count`/`galCount` and never
cleared, so slots above the current counts retain previous-frame data. This is currently
**safe** because the shader loops strictly `i < star_count` / `i < galaxy_count`
(skybox.gdshader:135, 150) and the counts are always pushed. Flagged only so a future shader
change that reads a fixed `MAX_*` range would not silently consume stale directions/colors.
**Fix:** No change required today; if the shader ever iterates the full array, clear the tail
slots or document the count-bounded contract at the array declarations.

### IN-03: Magic numbers in galaxy procedural functions

**File:** `Shaders/skybox.gdshader:117-122, 128-129`
**Issue:** `exp(-r*r*4.0)`, `sin(2.0*theta - 8.0*r + …)`, `43758.5453`, `1.6`, `exp(-r*r*3.0)`
are unnamed tuning constants embedded in the spiral/elliptical helpers. The disc-scale and
LOD constants were promoted to named `const` (lines 89-90) but these inner shape constants
were not.
**Fix:** Promote the visually meaningful ones (arm count `2.0`, winding `8.0`, elliptical
aspect `1.6`) to named `const float` so play-test tuning has documented handles.

### IN-04: `IndexToSpace`/`SpaceToIndex` allocate via LINQ on every call

**File:** `Scripts/UniObject.cs:60-70`
**Issue:** Both helpers call `Enum.GetValues(...).Cast<Space>().ToArray()` per invocation,
allocating a fresh array each time. Not a correctness issue and not on a per-frame hot path
in the reviewed code, but it is avoidable garbage in otherwise allocation-conscious code.
**Fix:** Cache the `Space[]` in a `static readonly` field and index into it.

### IN-05: Default body color fallback compares against `default(Color)` (transparent black)

**File:** `Scripts/Render/WorldRenderer.cs:540`
**Issue:** `body.BaseColor.IsEqualApprox(default)` treats an unauthored color as
`Color(0,0,0,0)` and substitutes a grey-green default. A body intentionally authored as
opaque black (`Color(0,0,0,1)`) would *not* hit this branch (alpha differs), but a body left
at struct-default would. This is benign in the current scene (all bodies set `BaseColor`), but
the "is unset?" test conflates an unset color with a legitimately authored transparent one.
**Fix:** Track presentation-set state explicitly (e.g. a `bool HasColor`) rather than inferring
it from a sentinel color value.

---

_Reviewed: 2026-06-16T18:29:17Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
