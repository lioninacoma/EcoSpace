# Phase 5: Rendering Overhaul - Pattern Map

**Mapped:** 2026-06-19
**Files analyzed:** 8 new/modified files
**Analogs found:** 8 / 8

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `Scripts/Render/LuminousBodyDescriptor.cs` | model/struct | transform | `Scripts/UniObject.cs` (data struct) | role-match |
| `Scripts/Render/LuminousDescriptorBuilder.cs` | service | request-response | `Scripts/Render/SkyboxRenderer.cs` (SyncSkyPoints loop) | exact |
| `Scripts/Render/LuminousPassRenderer.cs` | renderer/Node3D | request-response | `Scripts/Render/PostProcessRenderer.cs` + `WorldRenderer.cs` | role-match |
| `Shaders/luminous_pass.gdshader` | shader | request-response | `Shaders/skybox.gdshader` (uniform arrays + galaxy disc) | exact |
| `Scripts/Render/LuminousLod.cs` | utility | transform | `Scripts/Render/StarRendering.cs` (pure static math) | exact |
| `EcoSpace.Tests/LuminousLodTests.cs` | test | — | `EcoSpace.Tests/TierClassifierTests.cs` | exact |
| `EcoSpace.Tests/LuminousDescriptorBuilderTests.cs` | test | — | `EcoSpace.Tests/UniMathTests.cs` | exact |
| `Scripts/Render/WorldRenderer.cs` (modified) | renderer | request-response | self (existing file) | exact |

---

## Pattern Assignments

---

### `Scripts/Render/LuminousBodyDescriptor.cs` (model struct, transform)

**Analog:** `Scripts/UniObject.cs` (public-fields data struct) + RESEARCH.md Pattern 1

**Role:** Plain data struct, no Godot dependency, consumed by both `WorldRenderer` and `LuminousPassRenderer`. Replaces the dual `_skyDirs` + `_lastRenderPositions` caches (D-02).

**Namespace pattern** — match existing Render namespace files:
```csharp
using Godot;

namespace Render
{
    // No summary needed for field-level struct, but type-level summary is required.
    /// <summary>
    /// Per-body descriptor built once per frame by LuminousDescriptorBuilder.
    /// Consumed read-only by WorldRenderer (mesh side) and LuminousPassRenderer (post-process side).
    /// Replaces the dual _skyDirs / _lastRenderPositions caches (D-02).
    /// </summary>
    public struct LuminousBodyDescriptor
    {
        public int      BodyIndex;
        public Vector3  Direction;         // world-space unit vector, ship→body (UniMath LCA path)
        public float    AngularSize;       // (1 - cos theta), floored at pixel, capped at MaxDiscAngle
        public float    Brightness;        // [0,1] from StarRendering.ApparentBrightness
        public Color    BaseColor;         // body.BaseColor (rgb) + Brightness (a)
        public float    LodWeight;         // 0=far/post-process-only; 1=near/mesh-only
        public UniObject.Type BodyType;    // Star vs Galaxy
        public int      GalaxyType;        // 0=spiral, 1=elliptical (galaxies only)
        public Vector4  GalaxyOrientation; // xyz=disc_normal, w=seed (galaxies only)
        public double   DistanceMeters;    // used for LOD weight; not pushed to shader
    }
}
```

**Key invariants:**
- `LodWeight` is a smooth function of `DistanceMeters` from `LuminousLod`; never a SOI-boundary flag.
- `BaseColor.A` holds `Brightness` (packed as alpha), matching the `star_colors[i].a` convention in `skybox.gdshader` (line 39) and `SkyboxRenderer` (line 214).

---

### `Scripts/Render/LuminousDescriptorBuilder.cs` (service, request-response)

**Analog:** `Scripts/Render/SkyboxRenderer.cs` — `SyncSkyPoints()` method (lines 131–258)

**Role:** Runs once per frame; iterates `GameObjects`; outputs `LuminousBodyDescriptor[]`. Replaces the classify+project loops in both `SkyboxRenderer.SyncSkyPoints` and the mesh-position tracking in `WorldRenderer.SyncBodies`. Must be a separate `Node` with deterministic process order so both renderers consume its output without double-computing.

**Imports pattern** (mirror SkyboxRenderer lines 1–4):
```csharp
using Godot;
using System.Collections.Generic;

namespace Render
{
```

**World reference pattern** (SkyboxRenderer._Ready lines 92–107):
```csharp
public override void _Ready()
{
    if (WorldPath != null && !WorldPath.IsEmpty)
        _world = GetNode<TestSetup>(WorldPath);
    else
        _world = GetParent<TestSetup>() ?? GetTree().Root.FindChild("Main", true, false) as TestSetup;
}
```

**Core classify+project loop** (migrate from SkyboxRenderer.SyncSkyPoints lines 148–237):
```csharp
// Pre-allocated arrays — never allocate per frame (WR-01 pattern from WorldRenderer)
private readonly LuminousBodyDescriptor[] _descriptors = new LuminousBodyDescriptor[MaxStars + MaxGalaxies];
private int _descriptorCount = 0;

public void BuildDescriptors()
{
    var objs    = _world.GameObjects;
    int shipIdx = _world.ShipIndex;
    var ship    = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
    if (ship == null) { _descriptorCount = 0; return; }

    float pixelAngle = PixelAngularSize();   // same helper as SkyboxRenderer line 291
    _descriptorCount = 0;

    for (int i = 0; i < objs.Count; i++)
    {
        var body = objs[i];
        if (body == null) continue;
        if (body.Index == shipIdx) continue;

        var tier = TierClassifier.Classify(body, ship);
        if (tier == SkyTier.Skip || tier == SkyTier.Beyond) continue;

        // LCA-relative direction — MANDATORY path (CLAUDE.md §Position Math)
        bool hasLca = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
        Double3 delta = hasLca ? relUni.ToDouble3() : Double3.Zero;
        double  len   = hasLca ? delta.Magnitude() : 0.0;

        Vector3 dir3;
        if (!hasLca || len < 1e-30)
            dir3 = Vector3.Up;
        else
        {
            Double3 dir = delta * (1.0 / len);
            dir3 = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
        }

        // Appearance — StarRendering single source of truth (same as SkyboxRenderer lines 186-196)
        double theta = StarRendering.AngularRadius(body.RadiusMeters, len);
        float  eff   = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
        float  size  = 1f - Mathf.Cos(eff);
        float  alpha = StarRendering.ApparentBrightness(body.Luminosity, len);

        // LOD weight — NEW field (from LuminousLod)
        float lodWeight = body.ObjectType == UniObject.Type.Galaxy
            ? LuminousLod.GalaxyDiscWeight(len, body.SOIMeters)
            : LuminousLod.StarMeshWeight(len);

        // Home-galaxy suppression guard (SkyboxRenderer lines 207-209)
        if (body.ObjectType == UniObject.Type.Galaxy
            && UniMath.FindLca(ship, body, objs) == body.Index)
            continue;

        _descriptors[_descriptorCount++] = new LuminousBodyDescriptor
        {
            BodyIndex        = body.Index,
            Direction        = dir3,
            AngularSize      = size,
            Brightness       = alpha,
            BaseColor        = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha),
            LodWeight        = lodWeight,
            BodyType         = body.ObjectType,
            GalaxyType       = body.GalaxyType,
            GalaxyOrientation = new Vector4(body.GalaxyOrientation.X, body.GalaxyOrientation.Y,
                                            body.GalaxyOrientation.Z, body.GalaxySeed),
            DistanceMeters   = len,
        };

        if (_descriptorCount >= _descriptors.Length) break;
    }
}
```

**Null/bounds guard idiom** (WorldRenderer line 206, SkyboxRenderer line 136):
```csharp
var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
if (ship == null) return;
```

**PixelAngularSize helper** (copy verbatim from SkyboxRenderer lines 291–298):
```csharp
private float PixelAngularSize()
{
    float fovRad = Mathf.DegToRad(_cam?.Fov ?? 75f);
    float height = GetViewport()?.GetVisibleRect().Size.Y ?? 1080f;
    if (height < 1f) height = 1080f;
    return fovRad / height;
}
```

**Read-only contract comment** (SkyboxRenderer lines 9–10, must reproduce):
```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```

---

### `Scripts/Render/LuminousPassRenderer.cs` (renderer Node3D, request-response)

**Analog:** `Scripts/Render/PostProcessRenderer.cs` (shader host + parameter push pattern) + `Scripts/Render/SkyboxRenderer.cs` (array push to ShaderMaterial)

**Role:** Node3D child of `Camera3D`. Creates a 2×2 `QuadMesh` + `MeshInstance3D` in `_Ready`. In `_Process`, consumes `LuminousDescriptorBuilder.Descriptors` (read-only) and pushes packed arrays to the spatial `ShaderMaterial`. Renders in the 3D transparent pass before `PostProcessRenderer`'s CanvasLayer dither (D-05).

**Imports pattern:**
```csharp
using Godot;

namespace Render
{
```

**_Ready: quad setup** (RESEARCH.md Pattern 3 + Code Examples §"Adding the Quad to Main.tscn"):
```csharp
public override void _Ready()
{
    var quad = new QuadMesh { Size = new Vector2(2f, 2f), FlipFaces = true };
    var mesh = new MeshInstance3D { Mesh = quad };

    _mat = new ShaderMaterial
    {
        Shader = GD.Load<Shader>("res://Shaders/luminous_pass.gdshader")
    };
    mesh.MaterialOverride = _mat;
    AddChild(mesh);   // LuminousPassRenderer is Camera3D child; quad is its child
}
```

**_Process: array push to shader** (mirrors SkyboxRenderer lines 239–257):
```csharp
public override void _Process(double delta)
{
    if (_world == null || _mat == null || _builder == null) return;

    // Builder already ran this frame (higher process priority).
    // Read-only: do NOT call Build() again here (Pitfall 3 in RESEARCH.md).
    int starCount = 0, galCount = 0;

    for (int i = 0; i < _builder.DescriptorCount; i++)
    {
        ref var d = ref _builder.Descriptors[i];
        if (d.BodyType == UniObject.Type.Star && starCount < MaxStars)
        {
            _starDirs[starCount]       = d.Direction;
            _starColors[starCount]     = d.BaseColor;
            _starSizes[starCount]      = d.AngularSize;
            _starLodWeights[starCount] = d.LodWeight;
            starCount++;
        }
        else if (d.BodyType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
        {
            _galDirs[galCount]         = d.Direction;
            _galColors[galCount]       = d.BaseColor;
            _galSizes[galCount]        = d.AngularSize;
            _galDiscWeights[galCount]  = d.LodWeight;
            _galTypes[galCount]        = d.GalaxyType;
            _galOrientations[galCount] = d.GalaxyOrientation;
            galCount++;
        }
    }

    _mat.SetShaderParameter("star_count",       starCount);
    if (starCount > 0)
    {
        _mat.SetShaderParameter("star_dirs",        _starDirs);
        _mat.SetShaderParameter("star_colors",      _starColors);
        _mat.SetShaderParameter("star_sizes",       _starSizes);
        _mat.SetShaderParameter("star_lod_weights", _starLodWeights);
    }

    _mat.SetShaderParameter("galaxy_count",       galCount);
    if (galCount > 0)
    {
        _mat.SetShaderParameter("galaxy_dirs",         _galDirs);
        _mat.SetShaderParameter("galaxy_colors",       _galColors);
        _mat.SetShaderParameter("galaxy_sizes",        _galSizes);
        _mat.SetShaderParameter("galaxy_disc_weights", _galDiscWeights);
        _mat.SetShaderParameter("galaxy_types",        _galTypes);
        _mat.SetShaderParameter("galaxy_orientations", _galOrientations);
    }
}
```

**Pre-allocated arrays** (SkyboxRenderer lines 58–70 — never allocate per frame):
```csharp
private const int MaxStars    = 8;
private const int MaxGalaxies = 4;

private readonly Vector3[] _starDirs        = new Vector3[MaxStars];
private readonly Color[]   _starColors      = new Color[MaxStars];
private readonly float[]   _starSizes       = new float[MaxStars];
private readonly float[]   _starLodWeights  = new float[MaxStars];
private readonly Vector3[] _galDirs         = new Vector3[MaxGalaxies];
private readonly Color[]   _galColors       = new Color[MaxGalaxies];
private readonly float[]   _galSizes        = new float[MaxGalaxies];
private readonly float[]   _galDiscWeights  = new float[MaxGalaxies];
private readonly int[]     _galTypes        = new int[MaxGalaxies];
private readonly Vector4[] _galOrientations = new Vector4[MaxGalaxies];
```

**Null-coalescing shader push** (PostProcessRenderer line 29):
```csharp
_mat?.SetShaderParameter("star_count", starCount);
```

---

### `Shaders/luminous_pass.gdshader` (shader, depth-aware post-process)

**Analog:** `Shaders/skybox.gdshader` (uniform arrays, galaxy disc math, star loop) + RESEARCH.md Pattern 3 (spatial quad technique)

**Role:** `shader_type spatial`, rendered in the 3D transparent pass (camera-child quad). Reads depth buffer. Draws star point/glow/halo and galaxy disc. Composes additively before CanvasLayer dither (D-05).

**Shader type + render modes** (RESEARCH.md Pattern 3, Code Examples §"Bypass Vertex Trick"):
```glsl
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never;
```

**Depth + screen texture declarations** (RESEARCH.md Pattern 3 fragment):
```glsl
uniform sampler2D depth_texture  : hint_depth_texture;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
```

**Uniform array declarations** — copy names and sizes from `skybox.gdshader` lines 32–69, then add LOD fields:
```glsl
const int MAX_STARS    = 8;
const int MAX_GALAXIES = 4;

uniform vec3  star_dirs[MAX_STARS];
uniform vec4  star_colors[MAX_STARS];       // rgb=BaseColor, a=brightness (same packing as skybox.gdshader line 39)
uniform float star_sizes[MAX_STARS];
uniform float star_lod_weights[MAX_STARS];  // NEW: 0=far/point, 1=near/mesh
uniform int   star_count = 0;

uniform vec3  galaxy_dirs[MAX_GALAXIES];
uniform vec4  galaxy_colors[MAX_GALAXIES];
uniform float galaxy_sizes[MAX_GALAXIES];
uniform float galaxy_disc_weights[MAX_GALAXIES]; // NEW: 0=inside/no-disc, 1=far/full-disc
uniform int   galaxy_types[MAX_GALAXIES];
uniform vec4  galaxy_orientations[MAX_GALAXIES];
uniform int   galaxy_count = 0;
```

**Bypass vertex** (RESEARCH.md Code Examples §"Bypass Vertex Trick", must be exact):
```glsl
void vertex() {
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}
```

**Depth linearization + sky-pixel test** (RESEARCH.md Code Examples §"Depth Linearization", Pitfall 5):
```glsl
float raw_depth = texture(depth_texture, SCREEN_UV).x;
// Scale-independent sky pixel check: raw_depth ≈ 0 = far plane = empty sky (reversed-Z).
bool sky_pixel = (raw_depth < 1e-6);

// Linear depth for glow suppression (render units, space-dependent scale).
vec3  ndc       = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
vec4  view      = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
view.xyz       /= view.w;
float lin_depth = -view.z;
```

**Galaxy helper functions** — copy verbatim from `skybox.gdshader` lines 88–137:
- `galaxy_disc_coords_tilted()` (lines 98–121) — safe-basis D-59 tilt, TILT_FLOOR anti-collapse guard
- `spiral_galaxy()` (lines 123–131)
- `elliptical_galaxy()` (lines 133–137)

**Constants** — copy from `skybox.gdshader` lines 80–83:
```glsl
const float GALAXY_LOD_THRESHOLD = 2e-4;
const float GALAXY_DISC_SCALE    = 80.0;
const float TILT_FLOOR           = 0.15;   // D-59: must stay > 0 (T-05-03)
const float detail_blend         = 0.35;
```

**Star loop in fragment** (evolves skybox.gdshader lines 145–150, adds glow + LOD fade):
```glsl
for (int i = 0; i < star_count; i++) {
    float d    = dot(normalize(EYEDIR), star_dirs[i]);
    float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);

    // Glow halo: wider softened ring around the point core
    float halo_size = star_sizes[i] * 8.0;
    float halo      = smoothstep(1.0 - halo_size, 1.0, d) * 0.3;

    // Depth gate: suppress glow where foreground geometry covers this pixel (Pitfall 5)
    float vis = sky_pixel ? 1.0 : 0.0;

    // LOD fade: point/glow fades out as star mesh takes over
    float lod_fade = 1.0 - star_lod_weights[i];

    col.rgb += star_colors[i].rgb * (star_colors[i].a * (disc + halo) * vis * lod_fade);
}
```

**Galaxy loop in fragment** (evolves skybox.gdshader lines 154–192, adds disc_weight fade):
```glsl
for (int i = 0; i < galaxy_count; i++) {
    float disc_w = galaxy_disc_weights[i];
    if (disc_w < 0.001) continue;   // inside galaxy — no disc

    float size = galaxy_sizes[i];
    float d    = dot(normalize(EYEDIR), galaxy_dirs[i]);

    // Base point disc (same smoothstep as skybox.gdshader line 159)
    float point_disc = smoothstep(1.0 - size, 1.0, d);

    float galaxy_bright;
    if (size < GALAXY_LOD_THRESHOLD) {
        galaxy_bright = point_disc * galaxy_colors[i].a;
    } else if (d > 0.0) {
        // CRITICAL: d > 0.0 gate prevents antipodal ghost (Pitfall 4 / skybox.gdshader line 165)
        float seed    = galaxy_orientations[i].w;
        vec3  disc_nrm = galaxy_orientations[i].xyz;
        vec2  uv = galaxy_disc_coords_tilted(EYEDIR, galaxy_dirs[i], disc_nrm);
        uv /= max(size * GALAXY_DISC_SCALE, 0.001);

        float disc_bright;
        if (galaxy_types[i] == 0)
            disc_bright = spiral_galaxy(uv, seed);
        else
            disc_bright = elliptical_galaxy(uv);

        float grain = fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
        disc_bright *= mix(1.0, 0.55 + 0.45 * grain, detail_blend);
        galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
    } else {
        galaxy_bright = 0.0;
    }

    col.rgb += galaxy_colors[i].rgb * galaxy_bright * disc_w;
}
```

**Fragment output** (additive blend; alpha irrelevant for blend_add):
```glsl
ALBEDO = col.rgb;
ALPHA  = 1.0;
```

---

### `Scripts/Render/LuminousLod.cs` (utility, transform)

**Analog:** `Scripts/Render/StarRendering.cs` — pure static math class, no Godot dependency

**Role:** Pure static class, no Godot types (unit-testable). Implements smooth distance→LOD weight curves. May live as a nested class inside `LuminousDescriptorBuilder` or as a standalone file — standalone is preferred for testability, matching the `StarRendering` / `TierClassifier` precedent.

**Pattern** (StarRendering.cs lines 26–67 — static class, named constants, Clamp guard):
```csharp
// No namespace wrapper — pure global namespace like TierClassifier, StarRendering
/// <summary>
/// Pure distance-to-LOD-weight curves for the unified luminous-body descriptor (D-03).
/// No Godot dependency — intentionally kept free for unit testing.
/// Thresholds are [ASSUMED] starting points; calibrate at play-test gates (D-04).
/// </summary>
public static class LuminousLod
{
    // ── Star LOD ──────────────────────────────────────────────────────────────────
    private const double StarNearStart = 5e12;   // ~0.5 ly — tune in play-test
    private const double StarNearEnd   = 5e13;   // ~5 ly

    /// <summary>
    /// Star mesh weight: 1.0 = near (mesh dominant), 0.0 = far (point/glow dominant).
    /// Smooth clamp — no discrete SOI boundary (D-03 anti-pattern).
    /// </summary>
    public static float StarMeshWeight(double distMeters)
    {
        if (distMeters <= 1e-30) return 1f;
        double t = System.Math.Clamp(
            (distMeters - StarNearStart) / (StarNearEnd - StarNearStart), 0.0, 1.0);
        return (float)(1.0 - t);
    }

    // ── Galaxy LOD ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Galaxy disc weight: 1.0 = far from galaxy (disc visible), 0.0 = inside galaxy (disc hidden).
    /// Smooth clamp — continuous crossfade, not a SOI boundary swap.
    /// <paramref name="galaxySoiMeters"/>: body.SOIMeters from UniObject.
    /// </summary>
    public static float GalaxyDiscWeight(double distMeters, double galaxySoiMeters)
    {
        if (galaxySoiMeters <= 1e-30) return 1f;
        double fadeStart = 0.1 * galaxySoiMeters;
        double fadeEnd   = 0.5 * galaxySoiMeters;
        double t = System.Math.Clamp(
            (distMeters - fadeStart) / (fadeEnd - fadeStart), 0.0, 1.0);
        return (float)t;
    }
}
```

**Division-by-zero guard** (matches StarRendering.cs line 57 and CLAUDE.md §Error Handling):
```csharp
if (distMeters <= 1e-30) return 1f;
if (galaxySoiMeters <= 1e-30) return 1f;
```

---

### `EcoSpace.Tests/LuminousLodTests.cs` (test)

**Analog:** `EcoSpace.Tests/TierClassifierTests.cs` — xUnit, MakeObj helper, `[Fact]` attributes, Assert.Equal

**Imports + structure** (TierClassifierTests.cs lines 30–41):
```csharp
using Xunit;

public class LuminousLodTests
{
    // ── StarMeshWeight ────────────────────────────────────────────────────

    [Fact]
    public void StarMeshWeight_AtNearStart_ReturnsOne()
    {
        float w = LuminousLod.StarMeshWeight(5e12);
        Assert.True(w >= 0.99f, $"Expected ≥ 0.99 at NearStart, got {w}");
    }

    [Fact]
    public void StarMeshWeight_AtNearEnd_ReturnsZero()
    {
        float w = LuminousLod.StarMeshWeight(5e13);
        Assert.True(w <= 0.01f, $"Expected ≤ 0.01 at NearEnd, got {w}");
    }

    [Fact]
    public void StarMeshWeight_Midpoint_IsSmooth()
    {
        float wLow  = LuminousLod.StarMeshWeight(5e12);
        float wMid  = LuminousLod.StarMeshWeight(2.75e13);
        float wHigh = LuminousLod.StarMeshWeight(5e13);
        Assert.True(wHigh < wMid && wMid < wLow, "Weight must be monotone decreasing");
    }

    [Fact]
    public void StarMeshWeight_ZeroDistance_ReturnsOne()
    {
        Assert.Equal(1f, LuminousLod.StarMeshWeight(0.0));
    }

    // ── GalaxyDiscWeight ──────────────────────────────────────────────────

    [Fact]
    public void GalaxyDiscWeight_InsideGalaxy_ReturnsZero()
    {
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(0.05 * soi, soi);
        Assert.True(w <= 0.01f);
    }

    [Fact]
    public void GalaxyDiscWeight_FarFromGalaxy_ReturnsOne()
    {
        double soi = 1e21;
        float w = LuminousLod.GalaxyDiscWeight(0.6 * soi, soi);
        Assert.True(w >= 0.99f);
    }

    [Fact]
    public void GalaxyDiscWeight_ZeroSoi_DoesNotThrow()
    {
        float w = LuminousLod.GalaxyDiscWeight(1e20, 0.0);
        Assert.Equal(1f, w);
    }
}
```

---

### `EcoSpace.Tests/LuminousDescriptorBuilderTests.cs` (test)

**Analog:** `EcoSpace.Tests/UniMathTests.cs` — hierarchy builder helper, `List<UniObject>` mock, `[Fact]`

**Hierarchy builder pattern** (UniMathTests.cs lines 38–50):
```csharp
using System.Collections.Generic;
using Xunit;

public class LuminousDescriptorBuilderTests
{
    /// <summary>
    /// Builds a minimal 5-node test hierarchy mirroring the MVP scene:
    ///   [0] Root (Space.Root)
    ///   [1] Galaxy (Space.Universe, parent=0)
    ///   [2] Star (Space.Galaxy, parent=1)
    ///   [3] Planet (Space.Star, parent=2)
    ///   [4] Ship (Space.Planet, parent=3)
    /// </summary>
    private static List<UniObject> BuildMinimalHierarchy()
    {
        var objs = new List<UniObject>(new UniObject[5]);
        // ... author LocalPos values per UniMathTests.cs pattern
        return objs;
    }

    [Fact]
    public void Build_StarInGalaxySpace_IsDescribed()
    {
        // When ship is in Planet space, the Star body (Space.Galaxy) should produce
        // a descriptor with BodyType == Star and Direction != Vector3.Zero.
    }

    [Fact]
    public void Build_GalaxyIsAncestorOfShip_IsSupressed()
    {
        // Home-galaxy suppression: when FindLca(ship, galaxy) == galaxy.Index,
        // the galaxy must NOT appear in the descriptor array.
    }
}
```

---

### `Scripts/Render/WorldRenderer.cs` (modified — remove _lastRenderPositions cache)

**Analog:** self (existing file `Scripts/Render/WorldRenderer.cs`)

**Modification scope:** After `LuminousDescriptorBuilder` provides the unified descriptor, the `_lastRenderPositions` dictionary (WorldRenderer.cs line 149) and the `GetRenderPosition` accessor (lines 327–332) become redundant — the descriptor's `Direction` field replaces them. Remove in Plan 4 only (after all descriptor consumers are wired). Until Plan 4, keep both caches in place (D-08 incremental gate).

**Galaxy skip guard** — preserve verbatim (WorldRenderer.cs lines 239, 255 — must not regress D-28/T-03-06):
```csharp
if (parent.ObjectType != UniObject.Type.Galaxy)
    RenderBodyAt(parentIdx, ...);

if (body.ObjectType == UniObject.Type.Galaxy) continue;
```

**Render factor pattern** — preserve (WorldRenderer.cs lines 126–133):
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

---

## Shared Patterns

### LCA-Relative Position Math (MANDATORY for all projection)

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 168–180 (verified pattern) + CLAUDE.md §"Position Math"

**Apply to:** `LuminousDescriptorBuilder.BuildDescriptors()`, any code that computes ship→body direction

```csharp
// NEVER: bodyUni - shipUni directly across spaces (catastrophic cancellation at ~1e30 m).
// ALWAYS: UniMath.RelativePosition first, ToDouble3() once on the small delta.
bool hasLca = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
Double3 delta = hasLca ? relUni.ToDouble3() : Double3.Zero;
double  len   = hasLca ? delta.Magnitude() : 0.0;
Vector3 dir3;
if (!hasLca || len < 1e-30)
    dir3 = Vector3.Up;
else {
    Double3 dir = delta * (1.0 / len);
    dir3 = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
}
```

---

### Null-Safe Bounds Check Idiom

**Source:** `Scripts/Render/WorldRenderer.cs` line 206, `Scripts/Render/SkyboxRenderer.cs` line 136

**Apply to:** `LuminousDescriptorBuilder`, `LuminousPassRenderer`, any code iterating `GameObjects`

```csharp
var ship = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
if (ship == null) return;
```

---

### Pre-Allocated Arrays (No Per-Frame GC)

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 58–70

**Apply to:** `LuminousPassRenderer` and `LuminousDescriptorBuilder` for all uniform arrays

```csharp
private readonly Vector3[] _dirs   = new Vector3[MaxStars];
private readonly Color[]   _colors = new Color[MaxStars];
private readonly float[]   _sizes  = new float[MaxStars];
```

---

### ShaderMaterial Parameter Push Pattern

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 239–257, `Scripts/Render/PostProcessRenderer.cs` lines 29, 87

**Apply to:** `LuminousPassRenderer._Process`

```csharp
// Push count first; push arrays only when count > 0 (avoids sending empty arrays)
_mat.SetShaderParameter("star_count", count);
if (count > 0)
{
    _mat.SetShaderParameter("star_dirs",   _dirs);
    _mat.SetShaderParameter("star_colors", _colors);
    _mat.SetShaderParameter("star_sizes",  _sizes);
}
// Null-coalescing guard (PostProcessRenderer pattern):
_mat?.SetShaderParameter("param", value);
```

---

### Read-Only Renderer Contract

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 9–10, `Scripts/Render/WorldRenderer.cs` lines 33–34

**Apply to:** `LuminousDescriptorBuilder`, `LuminousPassRenderer` — both must carry this contract in their class summary

```csharp
/// Read-only consumer of GameWorld state — MUST NOT mutate UniVec3 or call TranslatePos.
```

---

### Home-Galaxy Suppression Guard

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 207–210

**Apply to:** `LuminousDescriptorBuilder.BuildDescriptors()` — preserve exactly

```csharp
// While the ship is inside this galaxy's SOI, the galaxy must NOT render as a disc.
// FindLca(ship, body) == body.Index is exactly "body is an ancestor of the ship".
if (UniMath.FindLca(ship, body, objs) == body.Index)
    continue;
```

---

### TILT_FLOOR Anti-Collapse Guard (D-59)

**Source:** `Shaders/skybox.gdshader` lines 80–83, 113–114

**Apply to:** `luminous_pass.gdshader` — copy `TILT_FLOOR = 0.15` constant and the `clamp(tilt_factor, TILT_FLOOR, 1.0)` line verbatim; must never be removed (T-05-03 landmine)

```glsl
const float TILT_FLOOR = 0.15;   // D-59: must be > 0; removing causes ring-collapse (T-05-03)
float safe_tilt = clamp(tilt_factor, TILT_FLOOR, 1.0);
```

---

### d > 0.0 Antipodal Ghost Gate

**Source:** `Shaders/skybox.gdshader` line 165

**Apply to:** `luminous_pass.gdshader` galaxy disc branch — must be preserved (Pitfall 4 in RESEARCH.md)

```glsl
} else if (d > 0.0) {
    // FRONT HEMISPHERE ONLY — prevents disc from painting its antipode.
```

---

## No Analog Found

All new files have strong analogs in the codebase. No entries in this section.

---

## Metadata

**Analog search scope:** `Scripts/Render/`, `Scripts/`, `Shaders/`, `EcoSpace.Tests/`
**Files scanned:** 15 C# files + 3 shaders + 2 test files
**Pattern extraction date:** 2026-06-19

**Critical invariants to carry into every plan:**
1. `LuminousDescriptorBuilder` is the single classify+project loop — `LuminousPassRenderer` must NOT re-run it (Pitfall 3).
2. `skybox.gdshader` galaxy disc functions (`galaxy_disc_coords_tilted`, `spiral_galaxy`, `elliptical_galaxy`) are copied verbatim into `luminous_pass.gdshader` — do not rewrite from scratch (D-59 tilt already solved).
3. Plan 3 removes `SkyboxRenderer` + `skybox.gdshader`. Plans 1–2 keep the skybox running alongside the new pass.
4. `hint_depth_texture` must only appear in `shader_type spatial`, never `canvas_item` (Godot bug #74464 / Pitfall 1).
5. LOD weight is always a smooth `LuminousLod` function — never a discrete SOI-boundary flag (D-03).
