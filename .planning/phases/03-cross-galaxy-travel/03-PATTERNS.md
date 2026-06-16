# Phase 3: Cross-Galaxy Travel - Pattern Map

**Mapped:** 2026-06-16
**Files analyzed:** 7 (all modifications to existing files — no new files)
**Analogs found:** 7 / 7

---

## File Classification

| Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---------------|------|-----------|----------------|---------------|
| `Scripts/UniObject.cs` | model | transform | self (extend enum + add fields) | exact |
| `Scripts/TestSetup.cs` | config/data | CRUD | self (extend SetupScene body block) | exact |
| `Scripts/Render/SkyboxRenderer.cs` | service | request-response | self (extend SyncSkyPoints loop) | exact |
| `Shaders/skybox.gdshader` | shader | transform | self (extend star loop pattern) | exact |
| `Scripts/Render/WorldRenderer.cs` | service | request-response | self (extend SyncBodies/IsStarBody) | exact |
| `Scripts/Flight/FlightController.cs` | service | request-response | self (modify MaxSpeed setter) | exact |
| `Scripts/GameWorld.cs` | service | event-driven | no change needed — SOI logic unchanged | N/A |

---

## Pattern Assignments

### `Scripts/UniObject.cs` (model, extend Type enum + add galaxy fields)

**Analog:** `Scripts/UniObject.cs` (self — extend in-place)

**Current Type enum** (lines 8–11):
```csharp
public enum Type
{
    Orb, Asteroid, Ship, None
}
```

**Extend to** (D-38):
```csharp
public enum Type
{
    Orb, Asteroid, Ship, None,
    Star,    // emissive mesh in current tier; sky-point in ancestor tier
    Galaxy,  // procedural sky disc only (D-28); WorldRenderer skips entirely
    Planet,  // lit mesh in current tier
}
```

**Existing Godot field pattern** (lines 1–4, 80–94) — shows that `using Godot;` is already present, so `Vector3` and `Color` are available without qualification:
```csharp
using Godot;
// ...
public Godot.Color     BaseColor;
public double          RadiusMeters;
public double          Luminosity = 1.0;
```

**Add galaxy-specific fields** after `Luminosity` (D-29). Use `ObjectType` not `Type` to avoid C# enum/field name collision (Pitfall 1 in RESEARCH.md):
```csharp
/// <summary>Body role: drives renderer routing (D-38). Use ObjectType to avoid
/// shadowing the Type enum name.</summary>
public Type            ObjectType = Type.None;

// ── Galaxy presentation fields (D-29) ─────────────────────────────────
/// <summary>0 = spiral, 1 = elliptical (D-29).</summary>
public int             GalaxyType;
/// <summary>Procedural arm/texture seed packed as float (D-29).</summary>
public float           GalaxySeed;
/// <summary>Disc normal in world space; controls orientation of the galaxy disc (D-29).</summary>
public Vector3         GalaxyOrientation;
```

---

### `Scripts/TestSetup.cs` (config/data, extend SetupScene)

**Analog:** `Scripts/TestSetup.cs` (self — extend the existing body-authoring block)

**Existing body authoring pattern** (lines 109–155) — the canonical template to copy for every new body:
```csharp
_galaxy  = AddGameObject(_root,    new Double3(0, 0, 0),         5e3);

_star    = AddGameObject(_galaxy,  new Double3(0, 0, 0),         StarSOI);
GameObjects[_star].Name         = "STAR";
GameObjects[_star].BaseColor    = Star_Color;
GameObjects[_star].RadiusMeters = Star_RadiusMeters;
GameObjects[_star].Luminosity   = 1.0;
```

**Sibling star pattern** (lines 137–155) — same template for Galaxy-space children:
```csharp
int _sib1 = AddGameObject(_galaxy, new Double3(Sibling1_GalX, 0, 0), StarSOI);
GameObjects[_sib1].Name         = "ALPHA CEN";
GameObjects[_sib1].BaseColor    = Sibling1_Color;
GameObjects[_sib1].RadiusMeters = Star_RadiusMeters;
GameObjects[_sib1].Luminosity   = Sibling1_Luminosity;
```

**New pattern: ObjectType tagging** — add after each existing body block (D-38):
```csharp
// For each body, add after existing fields:
GameObjects[_star].ObjectType    = UniObject.Type.Star;
GameObjects[_planetA].ObjectType = UniObject.Type.Planet;
GameObjects[_planetB].ObjectType = UniObject.Type.Planet;
// Siblings already have Star_RadiusMeters so they just need:
GameObjects[_sib1].ObjectType    = UniObject.Type.Star;
GameObjects[_sib2].ObjectType    = UniObject.Type.Star;
GameObjects[_sib3].ObjectType    = UniObject.Type.Star;
```

**New pattern: galaxy constant block** — follows the existing sibling constant pattern (lines 62–80):
```csharp
// ── Galaxy scale (D-34) ───────────────────────────────────────────────
// Universe space: 1 unit = 1e16 m  →  GalaxySOI = 5e4 units = 5e20 m (~50 kly)
private const double GalaxySOI           = 5e4;    // replace placeholder 5e3
private const double Galaxy_RadiusMeters = 5e20;   // for speed envelope (D-36)

// Galaxy 2: destination mirror (~Andromeda, 2.4e22 m → 2.4e6 Universe units)
private const double Galaxy2_UniZ        = 2.4e6;
// Galaxy 3: elliptical cluster (~1.8e22 m at 45° → ~1.27e6 units each axis)
private const double Galaxy3_UniX        = 1.27e6;
private const double Galaxy3_UniZ        = 1.27e6;
```

**New pattern: galaxy body authoring** — follows same `AddGameObject` + field-set style:
```csharp
// Replace: _galaxy = AddGameObject(_root, new Double3(0, 0, 0), 5e3);
// With:
_galaxy  = AddGameObject(_root, new Double3(0, 0, 0), GalaxySOI);
GameObjects[_galaxy].Name              = "HOME GALAXY";
GameObjects[_galaxy].ObjectType        = UniObject.Type.Galaxy;
GameObjects[_galaxy].RadiusMeters      = Galaxy_RadiusMeters;
GameObjects[_galaxy].Luminosity        = 1e10;
GameObjects[_galaxy].BaseColor         = new Color(0.7f, 0.75f, 1.0f);
GameObjects[_galaxy].GalaxyType        = 0;          // spiral
GameObjects[_galaxy].GalaxySeed        = 0.42f;
GameObjects[_galaxy].GalaxyOrientation = new Vector3(0f, 1f, 0f);

int _galaxy2 = AddGameObject(_root, new Double3(0, 0, Galaxy2_UniZ), GalaxySOI);
GameObjects[_galaxy2].Name              = "DEST GALAXY";
// ... same field set pattern
GameObjects[_galaxy2].GalaxyType        = 0;  // spiral mirror

int _galaxy3 = AddGameObject(_root, new Double3(Galaxy3_UniX, 0, Galaxy3_UniZ), GalaxySOI);
GameObjects[_galaxy3].Name              = "ELLIPTICAL CLUSTER";
GameObjects[_galaxy3].GalaxyType        = 1;  // elliptical
```

**Debug print pattern** (lines 160–172) — unchanged, already handles any number of GameObjects.

---

### `Scripts/Render/SkyboxRenderer.cs` (service, request-response — extend SyncSkyPoints)

**Analog:** `Scripts/Render/SkyboxRenderer.cs` (self — add galaxy partition alongside existing star loop)

**Existing array declaration pattern** (lines 49–57) — copy exactly for galaxy arrays:
```csharp
private const int MaxStars = 8;
private const float MaxDiscAngle = 0.5f;
private readonly Vector3[] _dirs   = new Vector3[MaxStars];
private readonly Color[]   _colors = new Color[MaxStars];
private readonly float[]   _sizes  = new float[MaxStars];
```

**Add galaxy arrays** (mirroring the star pattern above):
```csharp
private const int MaxGalaxies = 4;
private readonly Vector3[] _galDirs         = new Vector3[MaxGalaxies];
private readonly Color[]   _galColors       = new Color[MaxGalaxies];
private readonly float[]   _galSizes        = new float[MaxGalaxies];
private readonly int[]     _galTypes        = new int[MaxGalaxies];
private readonly Vector4[] _galOrientations = new Vector4[MaxGalaxies];
```

**Existing classify + direction math** (lines 134–186) — the full loop body to extend with a Type partition:
```csharp
if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;

bool hasCommonAncestor = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
Double3 delta = hasCommonAncestor ? relUni.ToDouble3() : Double3.Zero;
double  len   = hasCommonAncestor ? delta.Magnitude() : 0.0;

Vector3 dir3;
if (!hasCommonAncestor || len < 1e-30)
    dir3 = Vector3.Up;
else
{
    Double3 dir = delta * (1.0 / len);
    dir3 = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
}
```

**Existing size + brightness math** (lines 172–184) — reuse identically for galaxies:
```csharp
double theta = StarRendering.AngularRadius(body.RadiusMeters, len);
float  eff   = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
_sizes[count] = 1f - Mathf.Cos(eff);

float alpha = StarRendering.ApparentBrightness(body.Luminosity, len);
_colors[count] = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);
```

**New Type-partitioned loop body** — add after the existing classify check:
```csharp
// Partition by ObjectType (D-40)
if (body.ObjectType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
{
    // direction/distance: same UniMath path as stars
    // size/brightness: same StarRendering path as stars (D-30)
    _galDirs[galCount]         = dir3;
    _galSizes[galCount]        = 1f - Mathf.Cos(Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle));
    _galColors[galCount]       = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);
    _galTypes[galCount]        = body.GalaxyType;
    _galOrientations[galCount] = new Vector4(
        body.GalaxyOrientation.X, body.GalaxyOrientation.Y, body.GalaxyOrientation.Z,
        body.GalaxySeed);
    galCount++;
}
else if (body.ObjectType == UniObject.Type.Star && count < MaxStars)
{
    _dirs[count]   = dir3;
    _sizes[count]  = ...;
    _colors[count] = ...;
    _skyDirs[body.Index] = dir3;  // preserve RND-07 cache (D-21)
    count++;
}
```

**Existing shader push pattern** (lines 189–196) — copy for galaxy arrays:
```csharp
_skyMat.SetShaderParameter("star_count", count);
if (count > 0)
{
    _skyMat.SetShaderParameter("star_dirs",   _dirs);
    _skyMat.SetShaderParameter("star_colors", _colors);
    _skyMat.SetShaderParameter("star_sizes",  _sizes);
}
```

**Galaxy push** (same style, add after star push):
```csharp
_skyMat.SetShaderParameter("galaxy_count", galCount);
if (galCount > 0)
{
    _skyMat.SetShaderParameter("galaxy_dirs",         _galDirs);
    _skyMat.SetShaderParameter("galaxy_colors",       _galColors);
    _skyMat.SetShaderParameter("galaxy_sizes",        _galSizes);
    _skyMat.SetShaderParameter("galaxy_types",        _galTypes);      // int[] — see Pitfall 5
    _skyMat.SetShaderParameter("galaxy_orientations", _galOrientations);
}
```

**Pitfall note (Pitfall 5):** If `int[]` fails in `SetShaderParameter`, pack type (0/1) into `_galOrientations[i].W` channel and remove the separate `galaxy_types` uniform. Keep as a Wave 0 verification item.

---

### `Shaders/skybox.gdshader` (shader, extend star loop with galaxy loop)

**Analog:** `Shaders/skybox.gdshader` (self — add parallel galaxy loop)

**Existing uniform + loop pattern** (lines 32–66) — canonical template:
```glsl
shader_type sky;

const int MAX_STARS = 8;

uniform vec3  star_dirs[MAX_STARS];
uniform vec4  star_colors[MAX_STARS] : source_color;
uniform float star_sizes[MAX_STARS];
uniform int   star_count = 0;

void sky()
{
    vec3 col = vec3(0.0);
    for (int i = 0; i < star_count; i++)
    {
        float d    = dot(EYEDIR, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }
    COLOR = col;
}
```

**Add galaxy uniform set** (D-40) — after the existing star uniforms, before `void sky()`:
```glsl
const int MAX_GALAXIES = 4;

/// World-space unit direction to each galaxy center (same EYEDIR frame as stars).
uniform vec3  galaxy_dirs[MAX_GALAXIES];
/// rgb = BaseColor, a = ApparentBrightness (same StarRendering model, D-30).
uniform vec4  galaxy_colors[MAX_GALAXIES] : source_color;
/// Angular disc half-size in (1 - cos θ) smoothstep units, same as star_sizes.
uniform float galaxy_sizes[MAX_GALAXIES];
/// 0 = spiral, 1 = elliptical (D-29). Pack into galaxy_orientations.w if int[] fails.
uniform int   galaxy_types[MAX_GALAXIES];
/// xyz = disc normal (world-space), w = arm seed (D-29).
uniform vec4  galaxy_orientations[MAX_GALAXIES];
uniform int   galaxy_count = 0;
```

**Galaxy disc helpers** — add above `void sky()`:
```glsl
// LOD threshold: size (smoothstep space) above which structured disc renders (~3 px).
// GALAXY_DISC_SCALE is a play-test tuning knob — see RESEARCH.md Pitfall 6.
const float GALAXY_LOD_THRESHOLD = 2e-4;
const float GALAXY_DISC_SCALE    = 80.0;

vec2 galaxy_disc_coords(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    vec3 t1 = normalize(cross(disc_normal, vec3(0.0, 1.0, 0.0)));
    if (dot(t1, t1) < 0.001) t1 = normalize(cross(disc_normal, vec3(1.0, 0.0, 0.0)));
    vec3 t2 = cross(disc_normal, t1);
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    return vec2(dot(delta, t1), dot(delta, t2));
}

float spiral_galaxy(vec2 uv, float seed) {
    float r     = length(uv);
    float theta = atan(uv.y, uv.x);
    float disc  = exp(-r * r * 4.0);
    float arms  = 0.5 + 0.5 * sin(2.0 * theta - 8.0 * r + seed * 6.28318);
    float grain = fract(sin(r * 37.0 + seed * 13.0 + theta * 7.0) * 43758.5453);
    return disc * mix(arms, grain, smoothstep(0.2, 0.6, r));
}

float elliptical_galaxy(vec2 uv) {
    float r = length(vec2(uv.x * 1.6, uv.y));
    return exp(-r * r * 3.0);
}
```

**Galaxy loop inside `void sky()`** — after the existing star loop, before `COLOR = col;`:
```glsl
    // ── Galaxy loop (D-40) ────────────────────────────────────────
    for (int i = 0; i < galaxy_count; i++) {
        float size = galaxy_sizes[i];
        float d    = dot(EYEDIR, galaxy_dirs[i]);
        float point_disc = smoothstep(1.0 - size, 1.0, d);

        float galaxy_bright;
        if (size < GALAXY_LOD_THRESHOLD) {
            // Sub-pixel point: identical to star smoothstep (D-30)
            galaxy_bright = point_disc * galaxy_colors[i].a;
        } else {
            // Disc mode: structured procedural rendering
            vec3  disc_normal = galaxy_orientations[i].xyz;
            float seed        = galaxy_orientations[i].w;
            vec2  uv          = galaxy_disc_coords(EYEDIR, galaxy_dirs[i], disc_normal);
            uv /= max(size * GALAXY_DISC_SCALE, 0.001);

            float disc_bright;
            if (galaxy_types[i] == 0)
                disc_bright = spiral_galaxy(uv, seed);
            else
                disc_bright = elliptical_galaxy(uv);
            galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
        }
        col += galaxy_colors[i].rgb * galaxy_bright;
    }
```

---

### `Scripts/Render/WorldRenderer.cs` (service, request-response — type routing + skip Galaxy)

**Analog:** `Scripts/Render/WorldRenderer.cs` (self — targeted changes to two methods)

**IsStarBody — current** (line 384):
```csharp
private static bool IsStarBody(UniObject body) => body.Name == "STAR";
```

**IsStarBody — replace with** (D-38):
```csharp
private static bool IsStarBody(UniObject body) =>
    body.ObjectType == UniObject.Type.Star;
```

**Galaxy skip** — add at the top of both the parent block and the sibling loop in `SyncBodies()`. Copy the existing null-guard pattern (line 247):
```csharp
// Sibling loop (lines 243–253): add after null check:
if (body == null) continue;
if (body.ObjectType == UniObject.Type.Galaxy) continue;  // D-28: galaxies sky-only
```

Also guard the parent render call (before `RenderBodyAt` at line 237):
```csharp
// If parent is a Galaxy (ship somehow in Universe space), skip mesh render
if (parent.ObjectType == UniObject.Type.Galaxy) { /* skip parent mesh */ }
```

**GalaxyRenderFactor** — already declared at line 64 as `1e-8f`; no change needed. The `RenderFactorFor` switch (lines 126–133) already routes `Space.Galaxy` to it:
```csharp
private float RenderFactorFor(UniObject.Space space) => space switch
{
    UniObject.Space.Planet   => PlanetRenderFactor,
    UniObject.Space.Star     => StarRenderFactor,
    UniObject.Space.Galaxy   => GalaxyRenderFactor,   // already 1e-8f ✓
    UniObject.Space.Universe => UniverseRenderFactor,
    _                        => StarRenderFactor,
};
```

---

### `Scripts/Flight/FlightController.cs` (service, request-response — MaxSpeed cap removal)

**Analog:** `Scripts/Flight/FlightController.cs` (self — modify MaxSpeed setter)

**SpeedOfLight constant** (line 42):
```csharp
private const double SpeedOfLight = 3e8;
```

**MaxSpeed field + setter — current** (lines 133–143):
```csharp
private double _maxSpeed = 1e11;

[Export]
public double MaxSpeed
{
    get => _maxSpeed;
    set => _maxSpeed = Mathf.Clamp(value, 0.0, SpeedOfLight);  // caps at 3e8 — REMOVE
}
```

**MaxSpeed field + setter — replace with** (D-35, security guard from RESEARCH.md §V5):
```csharp
private double _maxSpeed = 2e20;  // 2e20 m/s ≈ 2-minute intergalactic crossing; planner tunes

[Export]
public double MaxSpeed
{
    get => _maxSpeed;
    set => _maxSpeed = System.Math.Max(0.0, value);  // no SpeedOfLight cap; NaN/Inf blocked by Max
}
```

**SpeedOfLight constant** — keep as a comment-only reference or remove. Do not use it in any setter.

**Existing SpeedEasing / MinSpeed setter pattern** (lines 150–155) — shows the `System.Math.Max(0.0, value)` guard used by other setters:
```csharp
set => _speedEasing = System.Math.Max(0.0, value);
set => _minSpeed    = System.Math.Max(0.0, value);
```

---

## Shared Patterns

### UniMath.RelativePosition (mandatory for all cross-space directions)

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 149–161
**Apply to:** `SkyboxRenderer.SyncSkyPoints` (galaxy direction), anywhere galaxy→ship distance is needed
```csharp
bool hasCommonAncestor = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
Double3 delta = hasCommonAncestor ? relUni.ToDouble3() : Double3.Zero;
double  len   = hasCommonAncestor ? delta.Magnitude() : 0.0;
// Never use UniVec3 operator- across spaces for galaxy distances — catastrophic cancellation
```

### Bounds-check idiom

**Source:** `Scripts/Render/WorldRenderer.cs` line 206, `Scripts/Render/SkyboxRenderer.cs` line 123
**Apply to:** every new index lookup
```csharp
var body = (uint)idx < (uint)objs.Count ? objs[idx] : null;
if (body == null) continue;
```

### Null-guarded shader parameter push

**Source:** `Scripts/Render/SkyboxRenderer.cs` lines 189–196
**Apply to:** all `SetShaderParameter` calls for galaxy uniforms
```csharp
_skyMat.SetShaderParameter("galaxy_count", galCount);
if (galCount > 0)
{
    _skyMat.SetShaderParameter("galaxy_dirs", _galDirs);
    // ...
}
```

### AddGameObject + field-set body authoring

**Source:** `Scripts/TestSetup.cs` lines 111–155
**Apply to:** every new galaxy body and destination system body in `SetupScene()`
```csharp
int idx = AddGameObject(parentIdx, new Double3(x, y, z), soiMeters);
GameObjects[idx].Name         = "NAME";
GameObjects[idx].BaseColor    = color;
GameObjects[idx].RadiusMeters = radius;
GameObjects[idx].Luminosity   = lum;
GameObjects[idx].ObjectType   = UniObject.Type.Galaxy;  // new field
```

---

## No Analog Found

All modified files are extensions of existing files; all patterns have direct analogs in the codebase. No greenfield files.

---

## Tuning Knobs (flag for play-test wave)

| Constant | File | Default | Note |
|----------|------|---------|------|
| `GALAXY_LOD_THRESHOLD` | `skybox.gdshader` | `2e-4` | ASSUMED; tune disc appearance |
| `GALAXY_DISC_SCALE` | `skybox.gdshader` | `80.0` | ASSUMED; tune arm/structure size |
| `_maxSpeed` default | `FlightController.cs` | `2e20` | ASSUMED 2-min crossing; planner confirms |
| `Galaxy_RadiusMeters` | `TestSetup.cs` | `5e20` | ASSUMED Milky Way-like; planner confirms |
| Galaxy `Luminosity` | `TestSetup.cs` | `1e10` | ASSUMED galaxy-scale; tune visibility |

---

## Metadata

**Analog search scope:** `Scripts/`, `Shaders/`
**Files read:** UniObject.cs, TestSetup.cs, SkyboxRenderer.cs, WorldRenderer.cs, FlightController.cs (lines 1–180), skybox.gdshader
**Pattern extraction date:** 2026-06-16
