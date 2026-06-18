# Phase 5: Outer-tier body findability & galaxy visibility - Pattern Map

**Mapped:** 2026-06-18
**Files analyzed:** 7 (1 new C# file, 1 new shader, 5 modified files)
**Analogs found:** 7 / 7

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Render/StarPointRenderer.cs` | renderer (NEW) | request-response, batch | `Scripts/Render/WorldRenderer.cs` | exact (same Node3D, floating-origin, per-frame UniMath, [Export] knobs) |
| `Scripts/Render/WorldRenderer.cs` | renderer (MODIFY) | request-response | self | — (extend `RenderBodyAt`, additive blend D-53) |
| `Scripts/Render/SkyboxRenderer.cs` | renderer (MODIFY) | request-response | self | — (extend `SyncSkyPoints` galaxy filter) |
| `Scripts/TierClassifier.cs` | classifier (MODIFY) | transform | self | — (insert Galaxy-unconditional guard) |
| `Scripts/Hud/Hud.cs` | HUD (MODIFY) | request-response | self | — (replace `UpdateTargetCircle` Guard 3 / size source) |
| `Shaders/skybox.gdshader` | sky shader (MODIFY) | transform | self | — (add tilt func, rework galaxy loop, remove star loop) |
| `Shaders/star_point.gdshader` | spatial shader (NEW) | transform | `Shaders/dithering.gdshader` + `Shaders/body_lit.gdshader` | partial (GDShader conventions; blend_add/unshaded is a new render_mode) |

---

## Pattern Assignments

### `Scripts/Render/StarPointRenderer.cs` (NEW renderer, batch / request-response)

**Analog:** `Scripts/Render/WorldRenderer.cs`

**Imports pattern** (WorldRenderer.cs lines 1-3):
```csharp
using Godot;
using System.Collections.Generic;

namespace Render
{
```

**Class declaration pattern** (WorldRenderer.cs lines 36-37):
```csharp
public partial class WorldRenderer : Node3D
{
```
StarPointRenderer must be `public partial class StarPointRenderer : Node3D` in namespace `Render`.

**[Export] tuning-knob pattern** (WorldRenderer.cs lines 40-76):
```csharp
[Export] public NodePath WorldPath { get; set; }
[Export] public float CameraFarPlane { get; set; } = 1e6f;
// private backing fields for per-space factors (not exported individually)
private float PlanetRenderFactor { get; set; } = 1e-8f;
```
New class uses same `[Export] public NodePath WorldPath` plus `[Export] public float MinSizeRatio` and `[Export] public float LuminosityCap` as per-class tuning knobs (Claude's Discretion).

**_Ready world-resolve pattern** (WorldRenderer.cs lines 168-182):
```csharp
public override void _Ready()
{
    if (WorldPath != null && !WorldPath.IsEmpty)
        _world = GetNode<TestSetup>(WorldPath);
    else
        _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;

    var cam = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
    if (cam != null) cam.Far = CameraFarPlane;

    _bodyLitShader = GD.Load<Shader>("res://Shaders/body_lit.gdshader");
}
```
StarPointRenderer._Ready: resolve `_world` the same way; load `res://Shaders/star_point.gdshader`; build `_starIndices` by scanning `_world.GameObjects` for `ObjectType == UniObject.Type.Star`; allocate `MultiMesh` with `instance_count = _starCount`, `use_colors = true`, `use_custom_data = true`.

**_Process null-guard pattern** (WorldRenderer.cs lines 184-188):
```csharp
public override void _Process(double delta)
{
    if (_world == null) return;
    SyncBodies();
}
```

**RenderFactorFor pattern** (WorldRenderer.cs lines 126-133):
```csharp
private float RenderFactorFor(UniObject.Space space) => space switch
{
    UniObject.Space.Planet   => PlanetRenderFactor,
    UniObject.Space.Star     => StarRenderFactor,
    UniObject.Space.Galaxy   => GalaxyRenderFactor,
    UniObject.Space.Universe => UniverseRenderFactor,
    _                        => StarRenderFactor,
};
```
StarPointRenderer copies this verbatim — same per-space factor table. The factor must be `internal` or `public` if Hud needs it (see D-50 / Hud section).

**Floating-origin LCA position pattern — the MANDATORY cross-frame formula** (WorldRenderer.cs lines 392-403):
```csharp
// LCA-relative cross-frame position: star − ship in metres.
Double3 starRelToShip = UniMath.RelativeMetres(ship, star, gameObjects);

// Fallback if UniMath found no common ancestor (should not occur in a valid hierarchy).
if (starRelToShip.X == 0.0 && starRelToShip.Y == 0.0 && starRelToShip.Z == 0.0)
    return Vector3.Up * 1e7f;

// Convert to observer units (÷ ship.LocalPos.Scale) then to render units (× factor).
double obsFactor = factor / ship.LocalPos.Scale;
return new Vector3(
    (float)(starRelToShip.X * obsFactor),
    (float)(starRelToShip.Y * obsFactor),
    (float)(starRelToShip.Z * obsFactor));
```
StarPointRenderer's per-star loop uses EXACTLY this formula — `UniMath.RelativeMetres(ship, star, objs)` then `/ ship.LocalPos.Scale * factor`. Never raw `ToDouble3()` on an absolute position (Pitfall 1).

**Bounds-check idiom** (WorldRenderer.cs lines 206-207):
```csharp
var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
if (ship == null) return;
```

**Core per-instance update pattern** (from RESEARCH.md C# sketch, adapted to WorldRenderer conventions):
```csharp
for (int i = 0; i < _starIndices.Length; i++) {
    var star = objs[_starIndices[i]];
    Double3 relM = UniMath.RelativeMetres(ship, star, objs);
    float rf = factor / (float)scale;
    Vector3 rp = new Vector3(
        (float)(relM.X * rf), (float)(relM.Y * rf), (float)(relM.Z * rf));

    float bright = Mathf.Min(
        StarRendering.ApparentBrightness(star.Luminosity, relM.Magnitude()),
        LuminosityCap);

    _mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, rp));
    _mm.SetInstanceColor(i, new Color(
        star.BaseColor.R, star.BaseColor.G, star.BaseColor.B, bright));
    _mm.SetInstanceCustomData(i, new Color((float)star.Luminosity, 0, 0, 0));
}
```

**GetOrCreateMesh lazy-init pattern** (WorldRenderer.cs lines 512-518 — adapt for MultiMesh):
```csharp
private MeshInstance3D GetOrCreateMesh(int bodyIdx, UniObject body)
{
    if (_meshes.TryGetValue(bodyIdx, out var existing))
        return existing;
    // ...
    AddChild(meshInstance);
    _meshes[bodyIdx] = meshInstance;
    return meshInstance;
}
```
StarPointRenderer allocates the MultiMesh once in `_Ready` (not lazily), since instance count is fixed at startup. Pattern to borrow: `AddChild(_mmi)` in `_Ready`.

---

### `Scripts/Render/WorldRenderer.cs` (MODIFY — additive mesh blend, D-53)

**Analog:** self (existing code, extend `RenderBodyAt`)

**Star emission pattern to extend** (WorldRenderer.cs lines 480-488):
```csharp
if (isStar && mesh.MaterialOverride is StandardMaterial3D starMat)
{
    double distMeters = relUnits.Magnitude() * ship.LocalPos.Scale;
    starMat.EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
}
```
D-53 additive blend is architecturally already present — the PSF renderer adds on top. RESEARCH.md confirms no new WorldRenderer code is needed for the blend itself. The only WorldRenderer change needed is making `RenderFactorFor` accessible (change `private` to `internal`) for Hud D-50.

**Read-only contract** (WorldRenderer.cs line 34):
```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```
Any new accessor methods added must preserve this contract.

---

### `Scripts/Render/SkyboxRenderer.cs` (MODIFY — current-tier galaxy push, D-48/D-49)

**Analog:** self (existing code, extend the classify filter in `SyncSkyPoints`)

**Classify filter pattern to modify** (SkyboxRenderer.cs line 158):
```csharp
if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;
```
With the TierClassifier D-49 fix (Galaxy always returns `NextTierSkybox`), this line requires NO change — galaxies in Universe space will now pass through automatically.

**Galaxy uniform push pattern** (SkyboxRenderer.cs lines 248-257):
```csharp
_skyMat.SetShaderParameter("galaxy_count", galCount);
if (galCount > 0)
{
    _skyMat.SetShaderParameter("galaxy_dirs",         _galDirs);
    _skyMat.SetShaderParameter("galaxy_colors",       _galColors);
    _skyMat.SetShaderParameter("galaxy_sizes",        _galSizes);
    _skyMat.SetShaderParameter("galaxy_types",        _galTypes);
    _skyMat.SetShaderParameter("galaxy_orientations", _galOrientations);
}
```
This pattern is unchanged. Galaxy orientation data (xyz) is already pushed via `body.GalaxyOrientation` into `_galOrientations[galCount]`.

**Star push code to REMOVE** (SkyboxRenderer.cs lines 239-245 — dead after D-56):
```csharp
_skyMat.SetShaderParameter("star_count", count);
if (count > 0)
{
    _skyMat.SetShaderParameter("star_dirs",   _dirs);
    _skyMat.SetShaderParameter("star_colors", _colors);
    _skyMat.SetShaderParameter("star_sizes",  _sizes);
}
```
Remove alongside the star loop and the `_dirs`/`_colors`/`_sizes` fields.

**Home-galaxy suppression guard to PRESERVE** (SkyboxRenderer.cs lines 208-209):
```csharp
if (UniMath.FindLca(ship, body, objs) == body.Index)
    continue;
```
This must stay — it suppresses the home galaxy while inside it. It fires correctly even for current-tier galaxies in Universe space (the galaxy is the ancestor of the ship, so it is suppressed while inside).

---

### `Scripts/TierClassifier.cs` (MODIFY — Galaxy-unconditional sky, D-49)

**Analog:** self (existing Classify method)

**Current classify entry** (TierClassifier.cs lines 54-62):
```csharp
public static SkyTier Classify(UniObject body, UniObject ship)
{
    if (body == null || ship == null)               return SkyTier.Skip;
    if (body.Index == ship.Index)                   return SkyTier.Skip;
    if (body.CurrentSpace == UniObject.Space.Root)  return SkyTier.Skip;

    if (body.CurrentSpace == ship.CurrentSpace)     return SkyTier.CurrentTierMesh;
    // ...
```

**Insert D-49 guard BEFORE the CurrentSpace match** (after Root check, before CurrentTierMesh):
```csharp
// D-49: Galaxy-typed bodies are ALWAYS sky entities, regardless of tier match.
// Prevents Galaxy-in-Universe-space from returning CurrentTierMesh (which WorldRenderer skips).
if (body.ObjectType == UniObject.Type.Galaxy)       return SkyTier.NextTierSkybox;

if (body.CurrentSpace == ship.CurrentSpace)         return SkyTier.CurrentTierMesh;
```

**No-Godot-dependency rule** (TierClassifier.cs line 2):
```
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing
```
Must remain pure C#. `UniObject.Type.Galaxy` is available (global namespace).

---

### `Scripts/Hud/Hud.cs` (MODIFY — UniObject-driven target circle, D-50)

**Analog:** self (existing `UpdateTargetCircle`)

**Guard 1 (keep)** (Hud.cs line 375):
```csharp
if (_worldRenderer == null || _camera == null) return;
```
After D-50 the `_worldRenderer` reference is still needed for `GlobalPosition` (render-space origin anchor) and for `RenderFactorFor`. Keep the null guard.

**Guard 2 (keep — target resolution)** (Hud.cs lines 378-384):
```csharp
var targets = BuildTargetableList(ship.ParentIndex, _world.ShipIndex, gameObjects);
if (targets.Count == 0) return;
int clamped = Mathf.Clamp(_targetIndex, 0, targets.Count - 1);
int tgtIdx = targets[clamped].Index;
```

**Guard 3 (REPLACE)** (Hud.cs line 385 — current, to be removed):
```csharp
// OLD — REMOVE THIS (D-50):
if (!_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos)) return;
```
Replace with UniMath-derived render-space position (RESEARCH.md Pitfall 6 code):
```csharp
// D-50: position from UniObject via UniMath, not from WorldRenderer mesh set
var targetObj = (uint)tgtIdx < (uint)gameObjects.Count ? gameObjects[tgtIdx] : null;
if (targetObj == null) return;
Double3 relMetres = UniMath.RelativeMetres(ship, targetObj, gameObjects);
double dist = relMetres.Magnitude();
float factor = _worldRenderer.RenderFactorFor(ship.CurrentSpace);   // make internal/public
double obsFactor = factor / ship.LocalPos.Scale;
Vector3 renderPos = new Vector3(
    (float)(relMetres.X * obsFactor),
    (float)(relMetres.Y * obsFactor),
    (float)(relMetres.Z * obsFactor));
```

**Guards 4–5 (keep — behind-camera + off-screen)** (Hud.cs lines 391-402):
```csharp
Vector3 globalPos = _worldRenderer.GlobalPosition + renderPos;
Vector3 camLocal = _camera.GlobalTransform.AffineInverse() * (globalPos - _camera.GlobalPosition);
if (camLocal.Z > 0) return;  // behind camera
// ...
Vector2 screenPos = _camera.UnprojectPosition(globalPos);
if (screenPos.X < 0 || ...) return;
```

**Size computation (REPLACE)** (Hud.cs lines 418-434 — current GetRenderRadius path):
```csharp
// OLD — relies on WorldRenderer mesh; returns 0 for sky-only targets
if (_worldRenderer.GetRenderRadius(tgtIdx, out float renderRadius) && renderRadius > 0f)
{ ... }
```
Replace with:
```csharp
// D-50: angular radius from UniObject, same formula as SkyboxRenderer
double angularRadius = StarRendering.AngularRadius(targetObj.RadiusMeters, dist);
float bodyPixelRadius = MIN_CIRCLE_RADIUS;
if (angularRadius > 0.0 && camLocal.Z < -1e-4f)
{
    float depth = -(float)camLocal.Z;
    float fovRad = Mathf.DegToRad(_camera.Fov);
    float tanHalfFov = Mathf.Tan(fovRad * 0.5f);
    float refExtent = _camera.KeepAspect == Camera3D.KeepAspectEnum.Height
        ? vpSize.Y * 0.5f : vpSize.X * 0.5f;
    bodyPixelRadius = refExtent * ((float)angularRadius) / tanHalfFov * CIRCLE_BODY_PADDING;
}
_targetCircleRadius = Mathf.Clamp(bodyPixelRadius, MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS);
```

**_Draw pattern (unchanged)** (Hud.cs lines 447-458):
```csharp
public override void _Draw()
{
    if (!_showTargetCircle) return;
    Vector2 localPos = _targetCirclePos - GlobalPosition;
    DrawArc(localPos, _targetCircleRadius, 0f, Mathf.Tau, 32, PhosphorGreen, 1.5f);
}
```

---

### `Shaders/skybox.gdshader` (MODIFY — D-56 star loop removal + D-54/D-59 galaxy rework)

**Analog:** self (existing shader)

**Star loop to REMOVE** (skybox.gdshader lines 135-145):
```glsl
for (int i = 0; i < star_count; i++)
{
    float d    = dot(EYEDIR, star_dirs[i]);
    float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
    col += star_colors[i].rgb * (star_colors[i].a * disc);
}
```
Remove loop AND the `star_dirs`/`star_colors`/`star_sizes`/`star_count` uniform declarations (lines 36-49).

**Stable basis function to EXTEND** (skybox.gdshader lines 103-110 — keep, extend for tilt):
```glsl
vec2 galaxy_disc_coords(vec3 eye_dir, vec3 galaxy_dir) {
    vec3 up0 = abs(galaxy_dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 t1  = normalize(cross(up0, galaxy_dir));
    vec3 t2  = cross(galaxy_dir, t1);
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    return vec2(dot(delta, t1), dot(delta, t2));
}
```
Replace with `galaxy_disc_coords_tilted(eye_dir, galaxy_dir, disc_normal)` that:
1. Builds the SAME stable basis from `galaxy_dir` (never from `disc_normal` — fef1d91 landmine).
2. Computes `tilt_factor = abs(dot(disc_normal, galaxy_dir))`.
3. Applies `safe_tilt = clamp(tilt_factor, TILT_FLOOR, 1.0)` (TILT_FLOOR ~0.15, Claude's Discretion).
4. Foreshortens the minor axis of UV by `safe_tilt` (see RESEARCH.md D-59 math, lines 532-560).

**Anti-antipode gate to PRESERVE** (skybox.gdshader line 161):
```glsl
} else if (d > 0.0) {
```
This `d > 0.0` front-hemisphere gate prevents the antipode ghost (commit fef1d91). It must remain in the reworked galaxy loop.

**Galaxy loop disc-bright pattern to extend with Star-Nest overlay** (skybox.gdshader lines 167-177):
```glsl
float seed = galaxy_orientations[i].w;
vec2  uv   = galaxy_disc_coords(EYEDIR, galaxy_dirs[i]);
uv /= max(size * GALAXY_DISC_SCALE, 0.001);

float disc_bright;
if (galaxy_types[i] == 0)
    disc_bright = spiral_galaxy(uv, seed);
else
    disc_bright = elliptical_galaxy(uv);
galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
```
After computing `disc_bright`, add the Star-Nest fractal overlay layer (RESEARCH.md lines 499-509):
```glsl
float star_nest_brightness = 0.0;
vec3 p = vec3(uv * 0.5, 0.0);
for (int j = 0; j < 4; j++) {
    p = abs(p) / dot(p, p) - 0.53;
    star_nest_brightness += pow(max(length(p) - 0.5, 0.0), -2.0) * 0.01;
}
star_nest_brightness = clamp(star_nest_brightness, 0.0, 1.0);
disc_bright = mix(disc_bright, disc_bright + star_nest_brightness * 0.4, detail_blend);
```
`detail_blend` and `TILT_FLOOR` are new shader constants (Claude's Discretion for exact values).

**Disc normal wire-up** — pass `galaxy_orientations[i].xyz` as `disc_normal` to the tilted coords function. This data is already pushed by `SkyboxRenderer` from `body.GalaxyOrientation`; no C# changes needed.

---

### `Shaders/star_point.gdshader` (NEW spatial shader)

**Analog:** `Shaders/dithering.gdshader` (GDShader file conventions) + PSF technique from RESEARCH.md

**Shader type and render mode** (RESEARCH.md lines 93-94):
```glsl
shader_type spatial;
render_mode blend_add, unshaded, depth_test_disabled;
```
This is the critical departure from existing shaders (which use `shader_type canvas_item` or `shader_type spatial` with default blend). `blend_add` makes each star quad additive; `depth_test_disabled` + POSITION.z trick is what prevents far-plane culling.

**Uniform declarations** (per godot-starlight pattern):
```glsl
// Uniform tuning knobs — set from StarPointRenderer [Export] values via ShaderMaterial
uniform float min_size_ratio : hint_range(0.0, 0.1) = 0.003;
uniform float luminosity_cap : hint_range(0.0, 2.0) = 0.95;
uniform float emission_energy : hint_range(0.0, 10.0) = 1.0;
uniform float halo_scale : hint_range(0.0, 2.0) = 0.3;   // D-55 glow halo
```

**Vertex shader — billboard + far-plane-clip trick** (RESEARCH.md lines 224-241):
```glsl
void vertex() {
    // Billboard: extract world position from MODEL_MATRIX
    vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    vec4 projected = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world_pos, 1.0);
    // Force Z to far plane so it is never clipped (godot-starlight technique)
    POSITION = projected;
    if (OUTPUT_IS_SRGB) {
        POSITION.z = -1.0 * projected.w;   // Forward+
    } else {
        POSITION.z = 0.0 * projected.w;    // Compatibility
    }
    // Billboard scale: VERTEX.xy are in [-0.5, 0.5] (QuadMesh default)
    // Scale by brightness (via INSTANCE_CUSTOM.x luminosity) + min_size_ratio floor
    float bright = COLOR.a;   // per-instance brightness packed into alpha by SetInstanceColor
    float scale = mix(min_size_ratio, 1.0, bright) * some_world_scale;
    POSITION.xy += VERTEX.xy * scale * projected.w;
}
```

**Fragment shader — PSF Gaussian + halo** (RESEARCH.md lines 577-589):
```glsl
void fragment() {
    // UV is centered on the quad; r is distance from center in [0..1]
    vec2 center_uv = UV - vec2(0.5);
    float r = length(center_uv) * 2.0;

    float bright = COLOR.a;   // per-instance brightness from SetInstanceColor alpha

    // PSF core: Gaussian falloff
    float core = exp(-r * r * 8.0) * bright;

    // Halo wings (D-55): wider, dimmer Gaussian — feeds bloom via emission_energy > 1
    float halo_r = r * 3.0;
    float halo = exp(-halo_r * halo_r * 0.5) * bright * halo_scale;

    // Apply luminosity cap
    float total = min(core + halo, luminosity_cap);

    // blend_add: ALBEDO unused in unshaded; use EMISSION for additive compositing
    ALBEDO = COLOR.rgb;
    EMISSION = COLOR.rgb * total * emission_energy;
    ALPHA = 1.0;   // blend_add composites via EMISSION, not alpha
}
```

**Dithering.gdshader conventions to follow** (GDShader file header style, skybox.gdshader lines 1-29):
```glsl
// star_point.gdshader
// PSF star-point renderer for EcoSpace — additive quad batch for all stars
// at all tiers (D-56). Replaces the skybox star loop.
// ...
shader_type spatial;
```

---

## Shared Patterns

### Floating-origin LCA position (MANDATORY for StarPointRenderer and Hud D-50)

**Source:** `Scripts/Render/WorldRenderer.cs` lines 392-403 (`ComputeStarRenderPosFromHierarchy`)
**Also used in:** `Scripts/Render/SkyboxRenderer.cs` lines 168-179 (`SyncSkyPoints`)
**Apply to:** `StarPointRenderer._Process` (per-star), `Hud.UpdateTargetCircle` (target position)

```csharp
// MANDATORY — never raw ToDouble3() on absolute position at galaxy scale
Double3 relMetres = UniMath.RelativeMetres(ship, body, gameObjects);
double obsFactor = factor / ship.LocalPos.Scale;
Vector3 renderPos = new Vector3(
    (float)(relMetres.X * obsFactor),
    (float)(relMetres.Y * obsFactor),
    (float)(relMetres.Z * obsFactor));
```

### StarRendering shared model (single source of truth)

**Source:** `Scripts/Render/StarRendering.cs`
**Apply to:** `StarPointRenderer` (brightness), `WorldRenderer.RenderBodyAt` (emission, unchanged), `Hud.UpdateTargetCircle` (angular radius for circle size)

```csharp
float bright  = StarRendering.ApparentBrightness(body.Luminosity, relMetres.Magnitude());
double angRad = StarRendering.AngularRadius(body.RadiusMeters, dist);
```

### Bounds-check idiom

**Source:** `Scripts/Render/WorldRenderer.cs` lines 206-207; `Scripts/Hud/Hud.cs` line 105
**Apply to:** all new index lookups in StarPointRenderer

```csharp
var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
if (ship == null) return;
```

### Read-only consumer contract

**Source:** `Scripts/Render/WorldRenderer.cs` line 34; `Scripts/Render/SkyboxRenderer.cs` line 8
**Apply to:** `StarPointRenderer`

```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```
`StarPointRenderer` only calls `SetInstanceTransform/Color/CustomData` on its own `MultiMesh`. It reads `GameObjects` but never writes back.

### [Export] tuning-knob pattern

**Source:** `Scripts/Render/WorldRenderer.cs` lines 40-111; `Scripts/Hud/Hud.cs` lines 37-50
**Apply to:** `StarPointRenderer`, and any new shader constants exposed for play-test tuning

```csharp
[Export] public float MinSizeRatio { get; set; } = 0.003f;   // PSF min floor
[Export] public float LuminosityCap { get; set; } = 0.95f;   // brightness cap
[Export] public float HaloScale { get; set; } = 0.3f;        // D-55 glow wing width
```

### Galaxy loop front-hemisphere gate (anti-antipode)

**Source:** `Shaders/skybox.gdshader` line 161
**Apply to:** reworked galaxy loop in skybox.gdshader

```glsl
} else if (d > 0.0) {
    // Disc mode — front hemisphere only (prevents antipode ghost, commit fef1d91)
```

---

## No Analog Found

All files have strong analogs from the existing codebase. No files require relying solely on RESEARCH.md external references, though the PSF shader render_mode (`blend_add, unshaded, depth_test_disabled`) and the MultiMesh API are patterns new to this codebase.

| File | New Pattern | Risk |
|------|-------------|------|
| `Shaders/star_point.gdshader` | `blend_add` + `depth_test_disabled` + direct `POSITION` write in vertex | Verify `OUTPUT_IS_SRGB` availability in Godot 4.6.2 (RESEARCH.md A4); `INSTANCE_CUSTOM.x` channel mapping (A2) |
| `Scripts/Render/StarPointRenderer.cs` | MultiMesh C# API (`SetInstanceTransform`, `SetInstanceColor`, `SetInstanceCustomData`) | Verify exact GodotSharp 4.6.2 property names `use_colors`/`use_custom_data` (RESEARCH.md A1) |

---

## Metadata

**Analog search scope:** `Scripts/Render/`, `Scripts/Hud/`, `Scripts/`, `Shaders/`
**Files read directly:** WorldRenderer.cs, SkyboxRenderer.cs, StarRendering.cs, Hud.cs, TierClassifier.cs, skybox.gdshader
**Pattern extraction date:** 2026-06-18
