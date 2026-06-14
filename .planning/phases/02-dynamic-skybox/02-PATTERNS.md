# Phase 02: Dynamic Skybox - Pattern Map

**Mapped:** 2026-06-14
**Files analyzed:** 7 new/modified files
**Analogs found:** 7 / 7

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Render/SkyboxRenderer.cs` | renderer (read-only consumer) | request-response, per-frame | `Scripts/Render/WorldRenderer.cs` | exact |
| `Shaders/skybox.gdshader` | shader | transform (direction → color) | `Shaders/body_lit.gdshader` | role-match |
| `Scripts/UniObject.cs` | model (field addition) | CRUD | `Scripts/UniObject.cs` (self) | exact |
| `Scripts/TestSetup.cs` | config / authored data | CRUD | `Scripts/TestSetup.cs` (self) | exact |
| `Scripts/TierClassifier.cs` | utility / pure logic | transform | `Scripts/GameWorld.cs` (Space enum logic) | role-match |
| `EcoSpace.Tests/EcoSpace.Tests.csproj` | config (test project) | — | `EcoSpace.csproj` | partial |
| `EcoSpace.Tests/TierClassifierTests.cs` | test | CRUD | none in codebase | no analog |

---

## Pattern Assignments

### `Scripts/Render/SkyboxRenderer.cs` (renderer, request-response / per-frame)

**Analog:** `Scripts/Render/WorldRenderer.cs`

**Imports pattern** (WorldRenderer.cs lines 1-3):
```csharp
using Godot;
using System.Collections.Generic;

namespace Render
{
```

**Class declaration pattern** (WorldRenderer.cs line 36):
```csharp
public partial class WorldRenderer : Node3D
```
SkyboxRenderer uses `Node` (not `Node3D`) since it manages no 3D meshes — it pushes uniforms to a `ShaderMaterial` on a `Sky` resource.

**Export properties pattern** (WorldRenderer.cs lines 41-96):
```csharp
[Export] public NodePath WorldPath { get; set; }
[Export] public float CameraFarPlane { get; set; } = 1e6f;
[Export] public float PlanetRenderFactor { get; set; } = 1e-8f;
[Export] public float StarEmissionEnergy { get; set; } = 3.0f;
[Export] public float BodyLightEnergy { get; set; } = 1.8f;
```
SkyboxRenderer exports: `NodePath WorldPath`, `float LuminosityScale`, `float MinBrightFloor`, `float SizePerBright`, `float MinStarSize`, `float MaxStarSize` — same `[Export]` on auto-properties pattern.

**Private state pattern** (WorldRenderer.cs lines 109-137):
```csharp
private TestSetup _world;
private Shader _bodyLitShader;
private readonly Dictionary<int, MeshInstance3D> _meshes = [];
private readonly Dictionary<int, ShaderMaterial> _litMaterials = [];
```
SkyboxRenderer private state: `_world`, `_skyMat` (ShaderMaterial on Sky resource), fixed-size arrays `_dirs` / `_colors` / `_sizes`, `const int MaxStars = 8`.

**_Ready pattern** (WorldRenderer.cs lines 141-155):
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
SkyboxRenderer._Ready: resolve `_world` identically; then obtain the `Sky` resource ShaderMaterial from `Camera3D.Environment.Sky.SkyMaterial` (cast to `ShaderMaterial`); store as `_skyMat`.

**_Process / delegate pattern** (WorldRenderer.cs lines 157-162):
```csharp
public override void _Process(double delta)
{
    if (_world == null) return;
    SyncBodies();
}
```
SkyboxRenderer mirrors exactly: `if (_world == null || _skyMat == null) return; SyncSkyPoints();`

**Bounds-check idiom** (WorldRenderer.cs lines 177-179):
```csharp
int shipIndex = _world.ShipIndex;
var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
if (ship == null) return;
```
Apply identically in `SyncSkyPoints()`.

**SetShaderParameter pattern** (WorldRenderer.cs lines 254-256):
```csharp
mat.SetShaderParameter("star_dir",     starDir);
mat.SetShaderParameter("light_energy", BodyLightEnergy);
mat.SetShaderParameter("ambient",      BodyAmbient);
```
SkyboxRenderer pushes after computing all sky bodies per frame:
```csharp
_skyMat.SetShaderParameter("star_count", count);
_skyMat.SetShaderParameter("star_dirs",   _dirs);    // Vector3[]
_skyMat.SetShaderParameter("star_colors", _colors);  // Color[]
_skyMat.SetShaderParameter("star_sizes",  _sizes);   // float[]
```

**Read-only constraint** (WorldRenderer.cs header, lines 33-34):
```
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```
Copy this doc comment verbatim to SkyboxRenderer's class summary.

**Null-check in loop** (WorldRenderer.cs line 214):
```csharp
var body = (uint)childIdx < (uint)gameObjects.Count ? gameObjects[childIdx] : null;
if (body == null) continue;
```
Apply in the SkyboxRenderer body-classification loop.

---

### `Shaders/skybox.gdshader` (shader, transform)

**Analog:** `Shaders/body_lit.gdshader` (closest existing shader in project)

**Shader type declaration pattern** (body_lit.gdshader lines 29-30):
```glsl
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, diffuse_lambert;
```
skybox.gdshader uses a DIFFERENT shader type — `shader_type sky` — with no render_mode line needed. This is the only structural difference.

**Uniform declaration pattern** (body_lit.gdshader lines 33-47):
```glsl
uniform vec4 albedo : source_color = vec4(0.75, 0.85, 0.75, 1.0);
uniform vec3 star_dir = vec3(0.0, 1.0, 0.0);
uniform float light_energy : hint_range(0.0, 8.0) = 1.8;
uniform float ambient : hint_range(0.0, 1.0) = 0.03;
```
skybox.gdshader uses fixed-size array uniforms (required by GLSL):
```glsl
const int MAX_STARS = 8;
uniform int   star_count              = 0;
uniform vec3  star_dirs[MAX_STARS];       // world-space unit direction, ship→sky body
uniform vec4  star_colors[MAX_STARS];     // .rgb = BaseColor, .a = apparent brightness (>1 blooms)
uniform float star_sizes[MAX_STARS];      // disc half-angle in smoothstep space
```

**Fragment body pattern** (body_lit.gdshader lines 49-61):
```glsl
void fragment()
{
    vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    float ndl = max(dot(n, normalize(star_dir)), 0.0);
    ALBEDO = albedo.rgb * (ambient + ndl * light_energy);
}
```
skybox.gdshader uses `void sky()` instead of `void fragment()`. Output variable is `COLOR` (vec3), not `ALBEDO`. Built-in `EYEDIR` replaces `NORMAL`. Loop over N star points:
```glsl
void sky() {
    vec3 col = vec3(0.0);
    for (int i = 0; i < star_count; i++) {
        float d    = dot(EYEDIR, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }
    COLOR = col;   // values > 1.0 feed WorldEnvironment glow automatically (D-20)
}
```

**Header comment pattern** (body_lit.gdshader lines 1-28):
```
// body_lit.gdshader
// [multi-line explanation of technique, uniforms, and math]
```
skybox.gdshader should have an equivalent header explaining: `shader_type sky`, EYEDIR world-space property, DO NOT sample RADIANCE (Godot 4.6 regression #115441), MAX_STARS fixed array requirement, and smoothstep disc technique.

---

### `Scripts/UniObject.cs` (model, field addition — D-26)

**Analog:** `Scripts/UniObject.cs` (self — adding a field after existing presentation-data block)

**Existing presentation-data block** (UniObject.cs lines 72-87):
```csharp
// ── Presentation data ─────────────────────────────────────────────
/// <summary>Human-readable name displayed by the HUD and target readout.</summary>
public string          Name;

/// <summary>
/// Authored base hue for dithered rendering. Consumed by WorldRenderer when
/// building the body's <c>StandardMaterial3D.AlbedoColor</c> (Plan 01-02).
/// </summary>
public Godot.Color     BaseColor;

/// <summary>
/// True 1:1 physical radius in metres (Star-space-equivalent units).
/// Consumed by WorldRenderer for mesh scaling...
/// </summary>
public double          RadiusMeters;
```

**Add after `RadiusMeters`** — follow the same `/// <summary>` + `public double Field = default;` pattern:
```csharp
/// <summary>
/// Absolute luminosity in solar luminosity units (L_sun = 3.828e26 W).
/// Drives the magnitude model in SkyboxRenderer (D-26/D-17).
/// Default 1.0 = solar luminosity; set to 0.0 for non-emissive bodies.
/// </summary>
public double          Luminosity = 1.0;
```

---

### `Scripts/TestSetup.cs` (authored data extension — D-23)

**Analog:** `Scripts/TestSetup.cs` (self — extending the existing SetupScene pattern)

**Existing constant block pattern** (TestSetup.cs lines 28-51):
```csharp
private const double PlanetA_Z = 1.496e11;
private const double StarSOI = 1.5e15;
private const double PlanetSOI = 1.0e9;
private const double PlanetA_RadiusMeters = 6.371e6;
private static readonly Color PlanetA_Color = new Color(0.25f, 0.50f, 0.95f);
```
Add sibling star constants following the same UPPER_SNAKE_CASE for doubles and `static readonly Color` for colors:
```csharp
// ----- Sibling star systems (D-23, Phase 2 skybox test data) --------
// Galaxy space: 1 unit = 10 000 m → interstellar distances in Galaxy units.
// Sibling1: Alpha Cen A-like (4.2 ly ≈ 3.97e16 m → 3.97e12 Galaxy units)
private const double Sibling1_GalX = 3.97e12;
private const double Sibling1_Luminosity = 1.519;
private static readonly Color Sibling1_Color = new Color(1.0f, 0.92f, 0.70f);
// Sibling2: Barnard's Star-like (dim M-dwarf, 5.96 ly ≈ 5.63e12 Galaxy units)
private const double Sibling2_GalX = 5.63e12;
private const double Sibling2_Luminosity = 0.0035;
private static readonly Color Sibling2_Color = new Color(1.0f, 0.30f, 0.15f);
// Sibling3: Sirius-like (very bright A-type, 8.6 ly, offset in X and Z)
private const double Sibling3_GalX = -6.0e12;
private const double Sibling3_GalZ =  5.6e12;
private const double Sibling3_Luminosity = 25.4;
private static readonly Color Sibling3_Color = new Color(0.70f, 0.85f, 1.0f);
```

**Existing SetupScene AddGameObject + field assignment pattern** (TestSetup.cs lines 83-86):
```csharp
_star    = AddGameObject(_galaxy,  new Double3(0, 0, 0),         StarSOI);
GameObjects[_star].Name         = "STAR";
GameObjects[_star].BaseColor    = Star_Color;
GameObjects[_star].RadiusMeters = Star_RadiusMeters;
```
Add after existing body setup — same pattern, add `Luminosity` assignment:
```csharp
int _sib1 = AddGameObject(_galaxy, new Double3(Sibling1_GalX, 0, 0), StarSOI);
GameObjects[_sib1].Name         = "ALPHA CEN";
GameObjects[_sib1].BaseColor    = Sibling1_Color;
GameObjects[_sib1].RadiusMeters = Star_RadiusMeters;
GameObjects[_sib1].Luminosity   = Sibling1_Luminosity;
// (repeat for _sib2, _sib3)
```
Also add `Luminosity` to the existing STAR entry so it participates in the brightness model.

**Existing star Luminosity addition** — the existing star (index `_star`) must also receive an explicit `Luminosity` so the magnitude model works from the start:
```csharp
GameObjects[_star].Luminosity = 1.0;  // solar luminosity (baseline)
```

---

### `Scripts/TierClassifier.cs` (utility, pure-logic transform)

**Analog:** `Scripts/GameWorld.cs` — `TrySpaceTransition` / Space enum usage (closest pattern for Space-enum-based classification)

**Global namespace, no `using Godot`** — this file is intentionally Godot-free so it can be tested from a plain `classlib` without `GodotSharp`. Follow the global-namespace pattern used by `UniObject.cs` and `GameWorld.cs` (no `namespace` wrapper).

**Enum + static class pattern** (mirrors UniObject.cs lines 8-64 for enum style; no direct analog for static utility class — use `public static class`):
```csharp
// Scripts/TierClassifier.cs
// Pure C# — no Godot dependency. Intentionally kept dependency-free for unit testing.
// Source of truth for which bodies SkyboxRenderer renders as sky points vs WorldRenderer
// as meshes, per the tiered space model (RND-05, D-22).
using System.Collections.Generic;

public enum SkyTier { Skip, CurrentTierMesh, NextTierSkybox, Beyond }

public static class TierClassifier
{
    public static SkyTier Classify(UniObject body, UniObject ship)
    {
        if (body == null || ship == null)                   return SkyTier.Skip;
        if (body.Index == ship.Index)                       return SkyTier.Skip;
        if (body.CurrentSpace == UniObject.Space.Root)      return SkyTier.Skip;

        // WorldRenderer owns all bodies that share the ship's current parent space.
        if (body.CurrentSpace == ship.CurrentSpace)         return SkyTier.CurrentTierMesh;

        // Next tier out = ParentSpace(ship.CurrentSpace).
        UniObject.Space nextOut = UniObject.ParentSpace(ship.CurrentSpace);
        if (body.CurrentSpace == nextOut)                   return SkyTier.NextTierSkybox;

        return SkyTier.Beyond;
    }
}
```

**Error-guard pattern** from GameWorld.cs (bounds check idiom): Classify receives `UniObject` references; caller guards with `(uint)i < (uint)count` before passing — consistent with the existing idiom. `Classify` itself null-guards its arguments.

---

### `EcoSpace.Tests/EcoSpace.Tests.csproj` (test project config)

**Analog:** `EcoSpace.csproj` (closest structural analog in the repo)

The test project is a plain `classlib` targeting `net8.0` with NO `GodotSharp` reference. It references `EcoSpace` source files for `UniObject`, `TierClassifier`, etc. — or better, extracts pure logic so no project reference is needed. Either xUnit or gdUnit4.api may be used (gdUnit4.api version must be verified on NuGet before install — flagged [ASSUMED] in RESEARCH.md).

Minimal structure (xUnit path, no external Godot dep):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

---

### `EcoSpace.Tests/TierClassifierTests.cs` (test, pure C#)

**Analog:** None in codebase. Use standard xUnit `[Fact]` / `[Theory]` pattern.

All test cases are purely deterministic: construct `UniObject` instances with explicit `CurrentSpace` values, call `TierClassifier.Classify`, assert the returned `SkyTier`. No Godot runtime required.

Representative test structure:
```csharp
public class TierClassifierTests
{
    private static UniObject MakeObj(int index, UniObject.Space space, int parentIdx = -1) =>
        new UniObject { Index = index, CurrentSpace = space, ParentIndex = parentIdx };

    [Fact]
    public void Ship_In_Planet_Space_SiblingPlanet_Is_CurrentTierMesh()
    {
        var ship = MakeObj(0, UniObject.Space.Planet);
        var planet = MakeObj(1, UniObject.Space.Planet);
        Assert.Equal(SkyTier.CurrentTierMesh, TierClassifier.Classify(planet, ship));
    }

    [Fact]
    public void Ship_In_Planet_Space_SiblingStarSystem_Is_NextTierSkybox()
    {
        var ship = MakeObj(0, UniObject.Space.Planet);
        var sibling = MakeObj(1, UniObject.Space.Galaxy);  // sibling star system lives in Galaxy space
        Assert.Equal(SkyTier.NextTierSkybox, TierClassifier.Classify(sibling, ship));
    }

    [Fact]
    public void MinBrightFloor_Is_Returned_When_Luminosity_Over_SquaredDist_Is_Tiny()
    {
        // MagnitudeModel test: raw = L / d^2 where L=0.001, d=1e20 → raw ≈ 0 < floor=0.1
        float floor = 0.1f;
        float raw = (float)(0.001 / (1e20 * 1e20));
        float brightness = Godot.Mathf.Max(raw * 4e6f, floor);
        Assert.Equal(floor, brightness, precision: 5);
    }
}
```

---

## Shared Patterns

### Read-Only Consumer Rule
**Source:** `Scripts/Render/WorldRenderer.cs` header (lines 33-34)
**Apply to:** `SkyboxRenderer.cs`
```
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```
SkyboxRenderer only calls `SetShaderParameter` on the `ShaderMaterial` — it never writes to `GameObjects`, `LocalPos`, or `ChildIndices`.

### Bounds-Check Idiom
**Source:** `Scripts/Render/WorldRenderer.cs` lines 177-179
**Apply to:** `SkyboxRenderer.cs` (ship resolve, body loop), `TierClassifier.cs` (callers)
```csharp
var ship = (uint)shipIndex < (uint)gameObjects.Count ? gameObjects[shipIndex] : null;
if (ship == null) return;
```

### ShaderMaterial Parameter Push
**Source:** `Scripts/Render/PostProcessRenderer.cs` lines 28-29, WorldRenderer.cs lines 254-256
**Apply to:** `SkyboxRenderer.cs`
```csharp
_material?.SetShaderParameter("dithering", value);
// or per-frame:
mat.SetShaderParameter("star_dir", starDir);
```
Use `_skyMat?.SetShaderParameter(...)` (null-coalescing) in `_Ready` guard; use direct call in `_Process` after null check.

### Export Properties with Backing Fields (optional — for PostProcessRenderer style)
**Source:** `Scripts/Render/PostProcessRenderer.cs` lines 15-68
**Apply to:** SkyboxRenderer tuning knobs if live-editing in Inspector is desired
```csharp
private float _minBrightFloor = 0.1f;
[Export]
public float MinBrightFloor
{
    get => _minBrightFloor;
    set { _minBrightFloor = value; /* optionally push to shader */ }
}
```
For SkyboxRenderer, plain `[Export] public float MinBrightFloor { get; set; } = 0.1f;` is sufficient since parameters are re-pushed every `_Process` frame anyway (unlike PostProcessRenderer which pushes only on property change).

### Null Guard in Object Loop
**Source:** `Scripts/TestSetup.cs` lines 108-109 (PrintState); WorldRenderer.cs line 214
**Apply to:** `SkyboxRenderer.SyncSkyPoints()` body loop
```csharp
if (o == null) continue;
```

### Per-Space Switch Pattern
**Source:** `Scripts/Render/WorldRenderer.cs` lines 120-127
**Apply to:** `TierClassifier` or `SkyboxRenderer` if per-space logic is needed
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

### Double3 for Direction Math at Scale
**Source:** `Scripts/Render/WorldRenderer.cs` lines 293-303 (`ComputeStarRenderPosFromHierarchy`)
**Apply to:** `SkyboxRenderer.ComputeSkyBodyDirection()` and `AbsolutePositionInRoot()`
```csharp
Double3 planetInStar = planet.LocalPos.ToDouble3();
Double3 shipInStar = planetInStar + ship.LocalPos.ToDouble3();
double obsFactor = factor / ship.LocalPos.Scale;
return new Vector3(
    (float)(-shipInStar.X * obsFactor),
    (float)(-shipInStar.Y * obsFactor),
    (float)(-shipInStar.Z * obsFactor));
```
Normalize direction in `double` before casting to `Vector3` to avoid precision loss at interstellar distances (Pitfall 5 in RESEARCH.md).

### Authored Body Data Block (TestSetup)
**Source:** `Scripts/TestSetup.cs` lines 39-51, 82-99
**Apply to:** Sibling star additions in `SetupScene()`
```csharp
private const double PlanetA_RadiusMeters = 6.371e6;
private static readonly Color PlanetA_Color = new Color(0.25f, 0.50f, 0.95f);
// ...
_planetA = AddGameObject(_star, new Double3(0, 0, PlanetA_Z), PlanetSOI);
GameObjects[_planetA].Name         = "PLANET A";
GameObjects[_planetA].BaseColor    = PlanetA_Color;
GameObjects[_planetA].RadiusMeters = PlanetA_RadiusMeters;
```
Add `GameObjects[idx].Luminosity = ...;` as the fourth field assignment for any star-type body.

---

## No Analog Found

All files have close analogs except the test file:

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `EcoSpace.Tests/TierClassifierTests.cs` | test | CRUD | No test files exist in the codebase yet |

Planner should use the standard xUnit `[Fact]`/`[Theory]` pattern (documented in RESEARCH.md) for this file.

---

## Metadata

**Analog search scope:** `Scripts/`, `Scripts/Render/`, `Scripts/Math/`, `Shaders/`
**Files read:** WorldRenderer.cs, PostProcessRenderer.cs, UniObject.cs, TestSetup.cs, body_lit.gdshader, dithering.gdshader (header)
**Pattern extraction date:** 2026-06-14
