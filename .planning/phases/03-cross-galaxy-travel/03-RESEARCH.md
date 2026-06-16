# Phase 3: Cross-Galaxy Travel - Research

**Researched:** 2026-06-16
**Domain:** Multi-tier universe rendering — procedural galaxy sky shaders, intergalactic flight model, Galaxy-tier mesh promotion, star/galaxy type routing
**Confidence:** MEDIUM (architecture deduced from full codebase read; shader techniques LOW; scale math HIGH from first principles)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-28:** Galaxies render entirely in the sky shader — never as world-space meshes or billboards. `WorldRenderer` gains no galaxy code path.
- **D-29:** Per-galaxy procedural type (spiral vs elliptical) + orientation/seed/color; no texture assets.
- **D-30:** Strict 1:1, pixel-floored galaxy sizing via `StarRendering.AngularRadius`; floored at one screen pixel.
- **D-31:** "Resolve into stars on approach" via procedural disc LOD only. D-22 (no proximity-based promotion) stays intact.
- **D-32:** 3 galaxies: home (spiral, full system) + full-mirror destination + third lighter elliptical bare-star cluster.
- **D-33:** Destination galaxy = full mirror system (star + 1–2 planets + sibling stars).
- **D-34:** True 1:1 intergalactic separation (~Andromeda scale, ~2.4e22 m). Galaxy SOI ≈ galaxy physical radius. `_galaxy` SOI placeholder `5e3` must be replaced.
- **D-35:** Design to a target crossing time; tune the one locked distance→speed curve (D-06/07/08) — no new mode.
- **D-36:** Natural ease-out FTL approach — trust the curve. FTL overshoot flagged as research/test item.
- **D-37:** Reuse Phase-1 HUD as-is.
- **D-38:** Extend `UniObject.Type` with Star/Galaxy/Planet. `WorldRenderer` renders Planet+Star, skips Galaxy. `SkyboxRenderer` renders Galaxy+Star.
- **D-39:** Extend the one RND-06 unit-space render model; tune `GalaxyRenderFactor` + star light range for Galaxy space. `UniverseRenderFactor` is moot (no meshes in Universe space).
- **D-40:** Separate galaxy uniform set + procedural render path in `skybox.gdshader` alongside existing star loop. `SkyboxRenderer` partitions `NextTierSkybox` bodies by `Type`. `MAX_GALAXIES` small (~4).
- **D-41:** Home galaxy reuses existing 4 stars + planets. Destination = full mirror. Third = small elliptical cluster (~3–5 stars, no planets).

### Claude's Discretion

- Exact galaxy SOI values (≈ physical radius), and replacing the `_galaxy` SOI placeholder `5e3`.
- Exact `GalaxyRenderFactor` value, star light range at galaxy scale.
- The target intergalactic crossing-time number and the distance→speed curve shape/easing at Universe scale.
- Procedural galaxy shader specifics: spiral-arm/elliptical functions, disc-LOD detail thresholds, orientation encoding, dither/bloom integration.
- Exact sibling/destination/third-galaxy coordinates, colors, luminosities, galaxy types, and member-star counts within the D-41 guideline.
- Star→Galaxy visible point→mesh handoff wiring specifics.
- Whether FTL overshoot needs a safety guard (default: none; add only if tunneling proves real).

### Deferred Ideas (OUT OF SCOPE)

- Whole-hierarchy target selector + world-pinned target outline + tracking label (Backlog 999.1).
- Proximity-promoting real member stars on galaxy approach (amending D-22).
- FTL overshoot safety guard (add only if tunneling proves real in testing).
- Galaxy-tier distinct render treatment (separate far plane/LOD/light model) — defer unless extended RND-06 breaks visibly.
- Richer galaxies (6–10 stars each).
- Texture-based/billboard galaxies.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| TRV-02 | Player can fly from one galaxy to another, with SOI transitions and the skybox updating correctly along the way | Speed curve analysis (§Architecture Patterns §FTL Overshoot), FlightController MaxSpeed fix (§Don't Hand-Roll), galaxy SOI values (§Standard Stack) |
| RND-02 (galaxy tier) | In Galaxy space the current galaxy's stars/suns render as emissive sphere meshes | TierClassifier analysis (§Architecture Patterns), WorldRenderer.SyncBodies routing via Type==Star (§Code Examples) |
| RND-04 (galaxy tier) | The current galaxy's star(s)/sun(s) render as emissive, light-emitting sphere meshes in Galaxy space | Same as RND-02; WorldRenderer already handles emissive star meshes — extends to Galaxy-space context |
| RND-05 (galaxy tier) | In Galaxy space the skybox carries only other galaxies | TierClassifier.Classify verified: galaxies (Universe space) → NextTierSkybox when ship in Galaxy space; galaxy-tier stars → CurrentTierMesh (§Architecture Patterns) |
| RND-07 (galaxy tier) | The Star↔Galaxy skybox↔mesh handoff is visually continuous | Per-frame math correctness guarantees alignment; D-21 baseline caches (GetSkyDirection/GetRenderPosition) confirmed built in Phase 2 (§Architecture Patterns §RND-07) |
</phase_requirements>

---

## Summary

Phase 3 extends the existing multi-tier render stack across the full Root→Universe→Galaxy→Star→Planet hierarchy. The central new capability is procedural galaxy rendering in the sky shader — galaxies grow from sub-pixel points to structured spiral/elliptical discs as the ship approaches, fully procedurally in GLSL with no texture assets. At SOI entry the ship passes through the galaxy disc and the sky re-tiers: galaxies drop to sky points and the galaxy's member stars appear as emissive sphere meshes — the same WorldRenderer path used by Phase 1 star meshes, now triggered by `Type==Star` rather than a name check.

The intergalactic flight model is an extension of the existing D-06/07/08 distance→speed curve. The only structural change needed is removing the erroneous `SpeedOfLight=3e8 m/s` cap on `MaxSpeed` (which prevents intergalactic speeds) and adding `RadiusMeters` to galaxy bodies so the speed curve can compute surface distance. No new "FTL mode" or sub-stepping guard is needed — analysis confirms that at a 2-minute crossing speed (~2e20 m/s), the galaxy SOI (5e20 m) takes ~150 frames to cross and the existing iterative MaxIterations=32 SOI logic is sufficient.

The `skybox.gdshader` gains a second parallel loop for galaxies (alongside the existing star loop), with per-galaxy `dirs/sizes/colors/types/orientations/count` uniform arrays. `SkyboxRenderer` partitions `NextTierSkybox` bodies by `Type`: stars feed the existing `star_*` arrays; galaxies feed the new `galaxy_*` arrays. `UniObject.Type` is extended with `Star/Galaxy/Planet` to support this routing, and `WorldRenderer.IsStarBody()` migrates from a fragile name check to `Type==Star`.

**Primary recommendation:** Implement the five work streams in order: (1) UniObject.Type extension + TestSetup data (3 galaxies + mirror system + cluster), (2) `skybox.gdshader` galaxy uniform set + procedural loop, (3) `SkyboxRenderer` galaxy partitioning, (4) `WorldRenderer` Type routing + GalaxyRenderFactor, (5) `FlightController` MaxSpeed cap removal + MaxSpeed raise.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Galaxy visual rendering | Sky shader (`skybox.gdshader`) | `SkyboxRenderer` (C# uniform push) | D-28: galaxies never world-space meshes; sky shader owns all galaxy visuals |
| Galaxy-tier star mesh rendering | `WorldRenderer` (C# mesh sync) | `TierClassifier` (routing) | Stars in Galaxy space are `CurrentTierMesh` per TierClassifier; WorldRenderer renders them via same emissive path as Phase 1 |
| SOI transitions (all tiers) | `GameWorld` (C# SOI logic) | — | Unchanged from Phase 1/2; iterative MaxIterations=32 handles Universe↔Galaxy crossings |
| Intergalactic flight speed | `FlightController` (C# speed envelope) | `TestSetup` (galaxy RadiusMeters) | D-06/07/08 curve extends to Universe scale; no new mode needed |
| Skybox re-tier logic | `TierClassifier` (pure C# classifier) | `SkyboxRenderer` + `WorldRenderer` consumers | TierClassifier already routes correctly; only consumer routing by Type is new |
| Star↔Galaxy handoff | `SkyboxRenderer` + `WorldRenderer` (per-frame math) | — | D-21: correctness of per-frame floating-origin math guarantees alignment; Phase 2 caches available as verification |
| World data (test universe) | `TestSetup` (C# authored data) | `UniObject` (type/fields) | Three galaxies + two full systems + cluster authoring in TestSetup.SetupScene |
| HUD (speed/context label) | `Hud` (GDScript) | `FlightController` (speed source) | D-37: reused as-is; adaptive units already reach ly/s |

---

## Standard Stack

Phase 3 adds no new packages. All work is within the existing Godot 4.6.2 / C# 12 / .NET 8 / GLSL stack.

### Core (existing, being extended)

| Component | Version | Purpose | Changes in Phase 3 |
|-----------|---------|---------|---------------------|
| `Godot.NET.Sdk` | 4.6.2 | Engine, sky shader execution | No version change |
| `Scripts/UniObject.cs` | — | Body type/space model | Add `Star`, `Galaxy`, `Planet` to `Type` enum; add `GalaxyType`, `GalaxyOrientation`, `GalaxySeed` fields |
| `Scripts/TestSetup.cs` | — | Authored world data | Add 2 more galaxies (Universe space), destination mirror system, elliptical cluster; replace `_galaxy` SOI |
| `Scripts/GameWorld.cs` | — | SOI transitions | No structural change; Universe↔Galaxy crossings handled by existing iterative logic |
| `Scripts/Render/SkyboxRenderer.cs` | — | Sky uniform push | Add galaxy `Type` partitioning; push `galaxy_dirs/colors/sizes/types/orientations/count` arrays |
| `Scripts/Render/WorldRenderer.cs` | — | Mesh sync + lighting | Migrate `IsStarBody` to `Type==Star`; skip `Type==Galaxy`; tune `GalaxyRenderFactor` |
| `Scripts/Render/StarRendering.cs` | — | Shared brightness/size model | No change; galaxies reuse `AngularRadius`/`ApparentBrightness` for consistent handoff |
| `Scripts/Flight/FlightController.cs` | — | Speed envelope | Remove `SpeedOfLight` cap from `MaxSpeed` setter; raise `MaxSpeed` default |
| `Shaders/skybox.gdshader` | — | Sky rendering | Add `MAX_GALAXIES` + galaxy uniform set + procedural galaxy loop |

### No new packages

This phase installs no new dependencies. The Package Legitimacy Audit section is omitted (no packages to audit).

---

## Architecture Patterns

### System Architecture Diagram

```
Ship motion (FlightController)
    │
    ▼
GameWorld.TranslatePos(shipIdx, delta)
    │
    ├── TrySpaceTransition ──────────────────────────┐
    │   (Universe↔Galaxy↔Star↔Planet SOI checks)    │
    │   MaxIterations=32 iterative; no recursion     │
    │   Universe-space galaxy siblings drive         │
    │   exit/entry when ship crosses galaxy SOI      │
    └────────────────────────────────────────────────┘
                │
         frame boundary
                │
    ┌───────────▼────────────┐
    │   SkyboxRenderer       │
    │   _Process each frame  │
    │                        │
    │  TierClassifier.Classify(body, ship)           │
    │   - Universe-space galaxies + star bodies      │
    │     classified into NextTierSkybox or          │
    │     CurrentTierMesh                            │
    │                        │
    │  Partition by Type:    │
    │   Type==Star  → star_dirs/colors/sizes         │
    │   Type==Galaxy→ galaxy_dirs/colors/sizes/      │
    │                types/orientations/count        │
    │                        │
    │  SetShaderParameter → skybox.gdshader          │
    └────────────────────────┘
                │
    ┌───────────▼────────────┐
    │   skybox.gdshader      │
    │                        │
    │  Star loop (existing): │
    │   smoothstep disc for  │
    │   each NextTierSkybox  │
    │   star body            │
    │                        │
    │  Galaxy loop (new):    │
    │   for i < galaxy_count │
    │   project EYEDIR onto  │
    │   disc plane           │
    │   → size gate:         │
    │     sub-pixel → point  │
    │     ≥ LOD_THRESHOLD →  │
    │     spiral/elliptical  │
    │     procedural disc    │
    └────────────────────────┘

    ┌───────────────────────────┐
    │   WorldRenderer           │
    │   _Process each frame     │
    │                           │
    │   SyncBodies():           │
    │   - Render parent + sibs  │
    │     in ship.CurrentSpace  │
    │   - Skip Type==Galaxy     │
    │   - IsStarBody → Type==Star│
    │   - GalaxyRenderFactor    │
    │     when ship in Galaxy   │
    │   - emissive star meshes  │
    │     for galaxy-tier stars │
    └───────────────────────────┘
```

### Recommended Project Structure (additions only)

No new files or directories needed. All changes are in-place extensions of existing files:

```
Scripts/
├── UniObject.cs          -- Type enum: add Star/Galaxy/Planet; add GalaxyType/Orientation/Seed fields
├── TestSetup.cs          -- Galaxy 2/3 data + mirror system + replace galaxy SOI
├── Render/
│   ├── SkyboxRenderer.cs -- galaxy partition + array push
│   └── WorldRenderer.cs  -- IsStarBody -> Type==Star; skip Galaxy; GalaxyRenderFactor
├── Flight/
│   └── FlightController.cs -- MaxSpeed cap removal
Shaders/
└── skybox.gdshader       -- MAX_GALAXIES + galaxy loop
```

### Pattern 1: UniObject.Type Extension (D-38)

**What:** Add `Star`, `Galaxy`, `Planet` values to `UniObject.Type` enum. Set `Type` on each body in `TestSetup`. Replace `IsStarBody(body)` name check with `body.ObjectType == UniObject.Type.Star`.

**Why:** The name-check `body.Name == "STAR"` is fragile (breaks with multiple named stars in the destination galaxy). Type is the correct discriminator for routing bodies between WorldRenderer (Planet+Star meshes) and SkyboxRenderer (Galaxy procedural discs).

**Pattern:**

```csharp
// Source: existing WorldRenderer.IsStarBody pattern, extended per D-38
// In UniObject.cs
public enum Type
{
    Orb, Asteroid, Ship, None,
    Star,    // emissive, renders as mesh in current tier; sky-point in ancestor tier
    Galaxy,  // procedural sky disc only (D-28); WorldRenderer skips entirely
    Planet,  // lit mesh in current tier
}

// Field rename to avoid collision with System.Type:
public Type ObjectType = Type.None;

// Optional galaxy-specific fields (D-29):
public int    GalaxyType;        // 0=spiral, 1=elliptical
public float  GalaxySeed;        // arm pattern seed packed as float
public Vector3 GalaxyOrientation; // disc normal in world space (Godot Vector3)
```

**Note on field naming:** Rename `UniObject.Type` enum member access to `UniObject.Type.Star` etc., and the instance field to `ObjectType` to avoid `UniObject.Type Type` ambiguity. Or keep field named `BodyType`. The planner should pick consistently.

**In TestSetup.SetupScene:** `GameObjects[_star].ObjectType = UniObject.Type.Star;` for every star; `GameObjects[_galaxy].ObjectType = UniObject.Type.Galaxy;` etc.

### Pattern 2: Galaxy Uniform Arrays in skybox.gdshader (D-40)

**What:** Add a second parallel uniform set and render loop for galaxies alongside the existing `star_*` loop.

**GLSL int array note:** Godot 4 sky shaders support `uniform int` and GLSL integer array uniforms. The C# side passes `int[]` via `SetShaderParameter`. If Godot's `ShaderMaterial.SetShaderParameter` rejects `int[]` for a GLSL `int[]` uniform, pack type as a float or use the `w` channel of an existing `vec4`. [ASSUMED - verify at runtime]

```glsl
// Source: extension of existing skybox.gdshader pattern [ASSUMED: GLSL functions, verified: Godot sky shader EYEDIR/uniform approach]
shader_type sky;

const int MAX_STARS   = 8;
const int MAX_GALAXIES = 4;

// ── Existing star uniforms (unchanged) ─────────────────────────
uniform vec3  star_dirs[MAX_STARS];
uniform vec4  star_colors[MAX_STARS] : source_color;
uniform float star_sizes[MAX_STARS];
uniform int   star_count = 0;

// ── New galaxy uniforms (D-40) ──────────────────────────────────
/// World-space unit direction to each galaxy center (same EYEDIR frame).
uniform vec3  galaxy_dirs[MAX_GALAXIES];
/// rgb = BaseColor, a = ApparentBrightness (same model as stars, D-30).
uniform vec4  galaxy_colors[MAX_GALAXIES] : source_color;
/// Angular disc half-size in smoothstep units (1 - cos θ), same as star_sizes.
/// When sub-pixel, galaxy renders as a star-like point.
uniform float galaxy_sizes[MAX_GALAXIES];
/// 0 = spiral, 1 = elliptical (D-29).
uniform int   galaxy_types[MAX_GALAXIES];
/// Disc orientation: xyz = disc normal (world-space), w = arm seed.
uniform vec4  galaxy_orientations[MAX_GALAXIES];
/// Number of valid galaxies.
uniform int   galaxy_count = 0;

// ── Helpers ─────────────────────────────────────────────────────

/// Disc-space polar coordinates of EYEDIR relative to galaxy disc center.
/// disc_normal should be the unit normal of the galaxy disc plane.
/// Returns xy = disc-plane projection of (EYEDIR - galaxy_dir),
/// scaled to galaxy_sizes units (0..1 ≈ disc edge).
vec2 galaxy_disc_coords(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    // Project EYEDIR into the disc tangent plane
    vec3 t1 = normalize(cross(disc_normal, vec3(0.0, 1.0, 0.0)));
    if (dot(t1, t1) < 0.001) t1 = normalize(cross(disc_normal, vec3(1.0, 0.0, 0.0)));
    vec3 t2 = cross(disc_normal, t1);
    // Angular offset from galaxy center
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    return vec2(dot(delta, t1), dot(delta, t2));
}

/// Procedural spiral galaxy disc brightness at disc-plane position uv.
/// Returns [0,1] brightness contribution.
float spiral_galaxy(vec2 uv, float seed) {
    float r     = length(uv);
    float theta = atan(uv.y, uv.x);
    // Radial falloff
    float disc  = exp(-r * r * 4.0);
    // Spiral arms: 2-arm pattern (sin of theta - winding * r)
    float arms  = 0.5 + 0.5 * sin(2.0 * theta - 8.0 * r + seed * 6.28318);
    // High-r grain: modulate arm brightness with r-dependent noise
    float grain = fract(sin(r * 37.0 + seed * 13.0 + theta * 7.0) * 43758.5453);
    return disc * mix(arms, grain, smoothstep(0.2, 0.6, r));
}

/// Procedural elliptical galaxy disc brightness at disc-plane position uv.
float elliptical_galaxy(vec2 uv) {
    float r = length(vec2(uv.x * 1.6, uv.y));  // aspect ratio ~1.6:1 for typical elliptical
    return exp(-r * r * 3.0);
}

// LOD threshold: size (in smoothstep-space) above which structured disc renders.
// Below this, galaxy renders as a colored point (same smoothstep as star loop).
const float GALAXY_LOD_THRESHOLD = 2e-4;  // ~3 screen pixels; tune in play-test [ASSUMED]

void sky() {
    vec3 col = vec3(0.0);

    // ── Star loop (unchanged) ─────────────────────────────────
    for (int i = 0; i < star_count; i++) {
        float d    = dot(EYEDIR, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }

    // ── Galaxy loop (new, D-40) ───────────────────────────────
    for (int i = 0; i < galaxy_count; i++) {
        float size  = galaxy_sizes[i];
        float d     = dot(EYEDIR, galaxy_dirs[i]);

        // Base point rendering (same smoothstep as stars — sub-pixel or approaching)
        float point_disc = smoothstep(1.0 - size, 1.0, d);

        float galaxy_bright;
        if (size < GALAXY_LOD_THRESHOLD) {
            // Point mode: render identically to a star point
            galaxy_bright = point_disc * galaxy_colors[i].a;
        } else {
            // Disc mode: structured procedural rendering
            vec3 disc_normal = galaxy_orientations[i].xyz;
            float seed       = galaxy_orientations[i].w;
            vec2  uv         = galaxy_disc_coords(EYEDIR, galaxy_dirs[i], disc_normal);
            // Scale uv to [0..1] ≈ disc edge based on angular size
            uv /= max(size * 80.0, 0.001);  // 80 = empirical disc-to-smoothstep scale [ASSUMED]

            float disc_bright;
            if (galaxy_types[i] == 0) {
                disc_bright = spiral_galaxy(uv, seed);
            } else {
                disc_bright = elliptical_galaxy(uv);
            }
            // Blend: point for center highlight, disc for structure
            galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
        }
        col += galaxy_colors[i].rgb * galaxy_bright;
    }

    COLOR = col;
}
```

**Scaling note:** The `uv /= max(size * 80.0, 0.001)` magic constant is a starting estimate [ASSUMED] — the planner should note it as a tuning knob for the play-test wave.

### Pattern 3: SkyboxRenderer Galaxy Partitioning (D-40)

**What:** In `SkyboxRenderer.SyncSkyPoints()`, after the existing `TierClassifier.Classify` check, route `NextTierSkybox` bodies into either the `star_*` arrays (Type==Star) or the new `galaxy_*` arrays (Type==Galaxy).

```csharp
// Source: extension of existing SkyboxRenderer.SyncSkyPoints pattern [ASSUMED: Type field name]
private const int MaxGalaxies = 4;

private readonly Vector3[] _galDirs        = new Vector3[MaxGalaxies];
private readonly Color[]   _galColors      = new Color[MaxGalaxies];
private readonly float[]   _galSizes       = new float[MaxGalaxies];
private readonly int[]     _galTypes       = new int[MaxGalaxies];
private readonly Vector4[] _galOrientations= new Vector4[MaxGalaxies];

// In SyncSkyPoints, replace the single-loop body with two counters:
int starCount = 0;
int galCount  = 0;

foreach body in NextTierSkybox bodies:
    if (body.ObjectType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
    {
        // direction, size, brightness — identical math to stars
        _galDirs[galCount]         = dir3;
        double theta               = StarRendering.AngularRadius(body.RadiusMeters, len);
        float eff                  = Mathf.Clamp((float)theta, pixelAngle, MaxDiscAngle);
        _galSizes[galCount]        = 1f - Mathf.Cos(eff);
        float alpha                = StarRendering.ApparentBrightness(body.Luminosity, len);
        _galColors[galCount]       = new Color(body.BaseColor.R, body.BaseColor.G, body.BaseColor.B, alpha);
        _galTypes[galCount]        = body.GalaxyType;
        _galOrientations[galCount] = new Vector4(
            body.GalaxyOrientation.X, body.GalaxyOrientation.Y, body.GalaxyOrientation.Z,
            body.GalaxySeed);
        galCount++;
    }
    else if (body.ObjectType == UniObject.Type.Star && starCount < MaxStars)
    {
        // existing star logic ...
        starCount++;
    }
    // else: skip (non-star/galaxy NextTierSkybox — should not occur in current data)

// Push galaxy uniforms
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

### Pattern 4: WorldRenderer Type Routing (D-38/D-39)

**What:** Replace `IsStarBody(body)` name check with `body.ObjectType == UniObject.Type.Star`. Skip `Type==Galaxy` bodies entirely.

```csharp
// Source: extension of WorldRenderer.GetOrCreateMesh and RenderBodyAt [ASSUMED field name ObjectType]

// In SyncBodies / RenderBodyAt:
private static bool IsStarBody(UniObject body) =>
    body.ObjectType == UniObject.Type.Star;   // was: body.Name == "STAR"

// Skip galaxies: add at the top of the sibling render loop
if (body.ObjectType == UniObject.Type.Galaxy) continue;

// GalaxyRenderFactor: same 1e-8 as StarRenderFactor works for Galaxy space
// Verification: galaxy-tier star at 4.2 ly = 3.97e12 Galaxy units
// → 3.97e12 * 1e-8 = 39,700 render units (far plane = 1e6 → 12x headroom) ✓
// [VERIFIED: codebase calculation from existing Scale/factor constants]
private float GalaxyRenderFactor { get; set; } = 1e-8f;  // same as StarRenderFactor (confirmed by math)
```

### Pattern 5: FlightController MaxSpeed Extension (D-35/D-36)

**What:** Remove the `SpeedOfLight=3e8` cap that prevents `MaxSpeed` from being set to intergalactic speeds. Raise `MaxSpeed` default to enable a reasonable crossing time.

```csharp
// Current (problematic):
// private const double SpeedOfLight = 3e8;
// set => _maxSpeed = Mathf.Clamp(value, 0.0, SpeedOfLight);  // caps at 3e8 m/s!

// Fix: remove SpeedOfLight from MaxSpeed setter; keep it as a comment-only reference.
// The intergalactic MaxSpeed is data (editor export), not a physical constant.
// [ASSUMED: crossing-time tuning is planner discretion per D-35]

[Export]
public double MaxSpeed
{
    get => _maxSpeed;
    set => _maxSpeed = System.Math.Max(0.0, value);  // no SpeedOfLight cap
}

// New default (planner tunes for target crossing time):
// 2e20 m/s → ~2 minute crossing at 2.4e22 m intergalactic distance
// 4e19 m/s → ~10 minute crossing (more deliberate pacing)
private double _maxSpeed = 2e20;  // [ASSUMED: planner confirms per D-35]
```

**Important:** Galaxy bodies need `RadiusMeters` set to feed the speed envelope's surface-distance calculation:

```csharp
// In TestSetup.SetupScene (for each galaxy):
GameObjects[_galaxy].RadiusMeters = 5e20;  // ~50 kly physical radius [ASSUMED: Milky Way-like]
```

Without this, `RadiusMeters <= 0.0` causes the galaxy to be skipped in the speed scan → ship maintains MaxSpeed even when touching the galaxy disc (no natural deceleration).

### Pattern 6: TestSetup Galaxy World Data (D-32/D-33/D-34/D-41)

**What:** Replace the single-galaxy placeholder with three real galaxies at Universe-scale distances. The existing `_galaxy` `SOIMeters=5e3` and `RadiusMeters=0` must both be replaced.

```csharp
// Source: extension of TestSetup.SetupScene [ASSUMED: coordinate values derived from 1:1 scales]

// Universe space scale: 1 unit = 1e16 m
// ── Home galaxy (index 1, existing) ──────────────────────────────────────────
//   SOI placeholder 5e3 → replace with 5e4 Universe units = 5e20 m (Milky Way radius)
//   Add RadiusMeters, ObjectType, GalaxyType, GalaxyOrientation, GalaxySeed

private const double GalaxySOI_UniverseUnits = 5e4;    // 5e20 m = ~50 kly radius [ASSUMED]
private const double Galaxy_RadiusMeters      = 5e20;   // physical radius for speed curve

// Home galaxy (spiral, existing _galaxy object):
_galaxy  = AddGameObject(_root, new Double3(0, 0, 0), GalaxySOI_UniverseUnits);
GameObjects[_galaxy].Name              = "HOME GALAXY";
GameObjects[_galaxy].ObjectType        = UniObject.Type.Galaxy;
GameObjects[_galaxy].RadiusMeters      = Galaxy_RadiusMeters;
GameObjects[_galaxy].GalaxyType        = 0;             // spiral
GameObjects[_galaxy].GalaxySeed        = 0.42f;
GameObjects[_galaxy].GalaxyOrientation = new Vector3(0.0f, 1.0f, 0.0f); // disc in XZ plane
GameObjects[_galaxy].BaseColor         = new Color(0.7f, 0.75f, 1.0f);  // cool blue-white [ASSUMED]
GameObjects[_galaxy].Luminosity        = 1e10;          // galaxy-scale luminosity [ASSUMED]

// Galaxy 2: destination mirror (spiral, full system mirroring home)
// Placed at ~2.4e22 m in +Z → 2.4e6 Universe units
private const double Galaxy2_UniverseZ   = 2.4e6;      // 2.4e22 m / 1e16 m/unit
_galaxy2 = AddGameObject(_root, new Double3(0, 0, Galaxy2_UniverseZ), GalaxySOI_UniverseUnits);
// ... mirror system: star + planets + siblings under _galaxy2

// Galaxy 3: elliptical cluster
// Placed at ~1.8e22 m at 45° offset from Galaxy 2 (visible in different sky direction)
private const double Galaxy3_UniverseX   = 1.27e6;
private const double Galaxy3_UniverseZ   = 1.27e6;
_galaxy3 = AddGameObject(_root, new Double3(Galaxy3_UniverseX, 0, Galaxy3_UniverseZ), GalaxySOI_UniverseUnits);
GameObjects[_galaxy3].GalaxyType = 1;  // elliptical
```

**Key constraint:** All galaxy positions are in Universe space (`_root` as parent). The Scale for Universe-space objects is `1e16 m/unit` (confirmed from `UniObject.Scale(Space.Universe) = 1e16`). Universe units of `2.4e6` are well within `Long3` range (~9.2e18).

### Pattern 7: RND-07 Star↔Galaxy Handoff

**What:** The Star↔Galaxy visible handoff occurs at Universe↔Galaxy SOI boundary crossings. Phase 2 built both cache accessors. Phase 3 wires them.

**Analysis of what actually happens at each boundary:**

| Crossing direction | Star body state | What happens |
|---|---|---|
| Ship: Galaxy→Star SOI entry | `NextTierSkybox` → `CurrentTierMesh` | Sky point vanishes; mesh appears at the star's floating-origin render position |
| Ship: Star→Galaxy SOI exit | `CurrentTierMesh` → `NextTierSkybox` | Mesh hides; sky point appears at the same world direction |

**Why it's already correct:** Both `SkyboxRenderer` and `WorldRenderer` derive the star's position from the same hierarchy math (`UniMath.RelativePosition` / `ToLocalDoubleUnits`) each frame. The sky direction and the render-space position both point to the same physical location — so they already land on the same screen pixel. No explicit "at-transition" alignment step is needed beyond ensuring both renderers use the same math.

**Verification (using Phase 2 caches):** In a test/debug pass, compare `GetSkyDirection(starIdx)` (frame N-1, when it was a sky point) vs `GetRenderPosition(starIdx)` converted to a view-space direction (frame N, when it becomes a mesh). They should be within 1–2 pixels.

### Anti-Patterns to Avoid

- **Using `body.Name == "STAR"` for star identification after D-38:** With multiple named stars across three galaxy systems, name checks break. Always use `body.ObjectType == UniObject.Type.Star`. [VERIFIED: WorldRenderer.IsStarBody exists and must be changed]
- **Setting galaxy positions in Galaxy units instead of Universe units:** Galaxies are Universe-space children of Root. Use Universe-space coordinates (1 unit = 1e16 m). [VERIFIED: TestSetup current `_galaxy = AddGameObject(_root, ...)` pattern]
- **Calling `UniVec3` operator subtraction across Universe↔Galaxy spaces:** Use `UniMath.RelativePosition` / `RelativeMetres` for cross-space directions. At intergalactic separation (~1e22 m), naive cross-scale subtraction loses precision catastrophically. [VERIFIED: CLAUDE.md UniMath rules + existing SkyboxRenderer uses this correctly]
- **Leaving `SpeedOfLight = 3e8` as the cap on MaxSpeed:** This silently prevents intergalactic speeds. The cap must be removed from the `MaxSpeed` setter. [VERIFIED: FlightController.cs line 141: `Mathf.Clamp(value, 0.0, SpeedOfLight)`]
- **Leaving galaxy `RadiusMeters = 0`:** Without RadiusMeters, the speed curve skips galaxies in the nearest-body scan → no natural deceleration on approach (D-36 trust-the-curve breaks). [VERIFIED: FlightController.UpdateSpeedEnvelope line: `if (body == null || body.RadiusMeters <= 0.0) continue;`]
- **Rendering galaxies as meshes in Universe space:** `TierClassifier` returns `CurrentTierMesh` for Universe-space bodies when ship is in Universe space. `WorldRenderer.SyncBodies` MUST skip `Type==Galaxy` explicitly (D-28). Without this skip, it would try to render a galaxy as a sphere mesh. [VERIFIED: TierClassifier.Classify logic traced]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-space position math (ship→galaxy direction) | Custom UniVec3 subtraction across scales | `UniMath.RelativePosition(ship, galaxy, objs)` | Cross-scale subtraction routes through `ToDouble3()` zeroing Long3 Units → catastrophic cancellation at 1e22 m [VERIFIED: CLAUDE.md, UniMath.cs] |
| Galaxy apparent brightness/size | New brightness model | `StarRendering.AngularRadius` + `StarRendering.ApparentBrightness` | Same model guarantees point↔disc handoff cannot pop (D-30 honors D-15/D-25) [VERIFIED: StarRendering.cs] |
| Star body identification | `body.Name == "STAR"` string check | `body.ObjectType == UniObject.Type.Star` | Name check breaks with multiple star systems (destination galaxy has its own star named differently) [VERIFIED: WorldRenderer.IsStarBody] |
| FTL deceleration on approach | Per-frame step clamp or CCD sub-stepping | Existing distance→speed curve (galaxy `RadiusMeters` feeds `nearest` scan) | Galaxy SOI (~5e20 m) takes ~150 frames to cross at 2e20 m/s — not a tunneling risk; existing iterative MaxIterations=32 is sufficient [VERIFIED: analysis] |
| Galaxy disc LOD | Separate LOD mesh or billboard system | Shader-internal `size < GALAXY_LOD_THRESHOLD` branch | D-31 + D-28: LOD must be in-shader only; no world-space geometry ever for galaxies |

**Key insight:** The entire intergalactic flight model is a data extension (galaxy SOI, RadiusMeters, MaxSpeed) and a constraint removal (SpeedOfLight cap) on the existing D-06/07/08 curve — not a new system. The shader gains a second loop block mirroring the existing star loop. The renderer gains a Type partitioner. Nothing genuinely new is designed from scratch.

---

## FTL Overshoot / Tunneling Analysis (D-36 research item)

**Conclusion: no special guard needed.** Derived from first principles:

| Quantity | Value | Derivation |
|----------|-------|------------|
| Recommended MaxSpeed | ~2e20 m/s | 2.4e22 m / 120 s crossing time [ASSUMED crossing time, derivable] |
| Delta per frame at 60 fps | ~3.3e18 m | `2e20 / 60` |
| Galaxy SOI | 5e20 m | Physical radius ~50 kly [ASSUMED: Milky Way-like] |
| Frames to cross Galaxy SOI | ~150 | `5e20 / 3.3e18` |
| Star SOI (within galaxy) | 1.5e15 m | [VERIFIED: TestSetup constant] |
| Star SOI frames at MaxSpeed | ~0.05 | `1.5e15 / 3.3e18` — tunneling risk only if at full speed |

**The speed curve prevents the Star SOI tunneling:** When the ship approaches a galaxy disc, nearest-body = galaxy → contextMax eases down to MinSpeed. Once inside Galaxy space, nearest-body = nearest star → contextMax is proportional to star surface distance. The ship cannot be at MaxSpeed when near a star SOI because the curve would have already decelerated it. **Verified by tracing `FlightController.UpdateSpeedEnvelope` logic.**

**Residual risk:** If the ship is aligned to fly directly through a star system's center at full Galaxy-space speed (before the galaxy has been entered — i.e., approaching from Universe space into a galaxy whose star is near the galaxy center), the speed might not decelerate fast enough. However: (1) SpeedEasing lerp smooths the deceleration over multiple frames, and (2) the iterative MaxIterations=32 SOI checker handles multi-boundary crossings per frame even if one frame overshoots. **Flagged for play-test verification per D-36.**

---

## Scale and Coordinate Reference

All values computed from first principles using existing codebase constants. [VERIFIED: UniObject.Scale values from codebase; physical values ASSUMED from real astronomy]

| Space | Scale (m/unit) | Practical range |
|-------|---------------|-----------------|
| Universe | 1e16 | Intergalactic: ~2.4e6 units for Andromeda-like separation |
| Galaxy | 1e4 | Interstellar: 3.97e12 units for 4.2 ly |
| Star | 1 | Planetary: 1.496e11 units for 1 AU |
| Planet | 1e-4 | Surface: 6.371e10 units for Earth radius |

| Body | Value | Notes |
|------|-------|-------|
| Galaxy SOI (Universe units) | 5e4 | Replace `5e3` placeholder; 10× larger |
| Galaxy SOI (meters) | 5e20 | ~50 kly physical radius [ASSUMED] |
| Galaxy `RadiusMeters` | 5e20 | Required for speed curve surface distance |
| Galaxy 2 Z position (Universe units) | 2.4e6 | 2.4e22 m / 1e16 m/unit |
| Galaxy 3 X, Z positions (Universe units) | 1.27e6, 1.27e6 | 1.8e22 m at 45° offset [ASSUMED] |
| GalaxyRenderFactor | 1e-8 | Same as StarRenderFactor; galaxy stars at 4.2 ly = 39,700 render units (far plane 1e6 = 12× headroom) |
| MaxSpeed (new default) | 2e20 m/s | 2-minute crossing; planner tunes [ASSUMED] |
| MaxSpeed (SpeedOfLight cap) | Remove | Current `3e8` cap prevents intergalactic speeds |

---

## Common Pitfalls

### Pitfall 1: Type Enum Field Name Collision

**What goes wrong:** Adding a field `public Type Type;` to `UniObject` creates an ambiguity with the enum `UniObject.Type` — the compiler sees `Type` as both a type name and a field.

**Why it happens:** The existing enum is already named `Type` inside `UniObject`. Adding an instance field with the same name shadows the enum name.

**How to avoid:** Name the instance field `ObjectType` (or `BodyType`). Use `body.ObjectType == UniObject.Type.Star` everywhere. Update the one reference in `WorldRenderer.IsStarBody`.

**Warning signs:** `CS0118: 'Type' is a namespace or type but is used like a variable` at compile time.

### Pitfall 2: Galaxy Positions in Wrong Scale

**What goes wrong:** Setting galaxy positions in Galaxy-space units (1 unit = 1e4 m) when they should be in Universe-space units (1 unit = 1e16 m), or vice versa.

**Why it happens:** Galaxies are Universe-space children of Root (`AddGameObject(_root, ...)` → `Space = Universe`), so all coordinates passed to `AddGameObject` are in Universe units. The visual impression is that "galaxy distances are huge" but Universe units make them small numbers.

**How to avoid:** Galaxy 2 at 2.4e22 m → `Galaxy2_UniverseZ = 2.4e6` (not 2.4e22). Always divide by `UniObject.Scale(Space.Universe) = 1e16`.

**Warning signs:** SOI transitions never occur (ship never reaches galaxy) OR SOI transitions happen immediately at spawn.

### Pitfall 3: Missing Galaxy RadiusMeters Breaks Speed Curve

**What goes wrong:** Ship maintains MaxSpeed all the way into the galaxy disc with no deceleration; the "natural ease-out" on approach (D-36) never happens.

**Why it happens:** `FlightController.UpdateSpeedEnvelope` skips any body with `RadiusMeters <= 0.0`. Without a galaxy radius, galaxies don't feed the `nearest` scan.

**How to avoid:** Set `GameObjects[_galaxyIdx].RadiusMeters = 5e20` for every galaxy in TestSetup.

**Warning signs:** Speed stays at MaxSpeed throughout intergalactic space, including when flying straight into the galaxy disc.

### Pitfall 4: Forgetting to Skip Galaxy Type in WorldRenderer

**What goes wrong:** When ship is in Universe space, `TierClassifier.Classify(galaxy_body, ship)` returns `CurrentTierMesh` (same space). Without a `Type==Galaxy` skip, `WorldRenderer` tries to create and render a `MeshInstance3D` sphere for the galaxy — violating D-28, and creating a sphere the size of a galaxy disc in render units.

**Why it happens:** `TierClassifier` has no knowledge of types — it only routes by space relationship. The `Type` filter belongs in `WorldRenderer.SyncBodies`.

**How to avoid:** Add `if (body.ObjectType == UniObject.Type.Galaxy) continue;` in `WorldRenderer.SyncBodies` for both the parent check and the sibling loop.

### Pitfall 5: GLSL `int[]` Uniform Arrays in Godot 4

**What goes wrong:** `SetShaderParameter("galaxy_types", new int[] {...})` may not work correctly in Godot 4.6 if `ShaderMaterial` does not support passing `int[]` for a GLSL `uniform int array`.

**Why it happens:** Godot's C# `SetShaderParameter` accepts `Variant` and maps C# types to GLSL types. `int[]` might need to be passed as a Godot `int[]` Variant or may require packing into a `float[]`.

**How to avoid:** If `int[]` fails, pack galaxy type (0/1) into the `.a` channel of `galaxy_orientations` instead of a separate `galaxy_types[]` array. Or use `float[]` with `0.0f`/`1.0f` values and compare in shader with `galaxy_types_f[i] < 0.5`. [ASSUMED: verify at runtime in Wave 0/1]

**Warning signs:** Godot editor console error on `SetShaderParameter` or shader type mismatch; galaxy types ignored (all render as one type).

### Pitfall 6: Galaxy LOD Scale Constant is Magic

**What goes wrong:** The `uv /= max(size * 80.0, 0.001)` shader expression produces a disc that is either too small (galaxy structure invisible) or too large (structure overflows disc edge).

**Why it happens:** The mapping from angular smoothstep units to disc-plane UV coordinates depends on the relationship between `galaxy_sizes[i]` (which is `1 - cos(theta)`) and the disc's apparent angular size in the shader.

**How to avoid:** The `80.0` constant is a starting estimate [ASSUMED]. Add it as a GLSL `const float GALAXY_DISC_SCALE = 80.0;` at the top of the shader for easy tuning during play-test. Document the tuning intent in a comment.

### Pitfall 7: SpeedEasing Slows MaxSpeed Transition at Galaxy Entry

**What goes wrong:** When the ship crosses Universe→Galaxy, the nearest-body changes from galaxy (~5e20 m surface distance) to nearest star (~ly). The contextMax target changes, but the SpeedEasing lerp means it takes multiple seconds to slow down.

**Why it happens:** SpeedEasing is applied both to `_contextMax` and `_easedSpeed`. At galaxy entry, these two lerps in sequence mean there's a perceptible "ghost speed" where the ship is going faster than the new context allows for a second or two.

**How to avoid:** This is by design (D-07 — hide boundary discontinuities). The question is whether it feels good or feels like tunneling. Flag for play-test; D-36 says add no guard unless tunneling proves real.

---

## Code Examples

### GalaxyRenderFactor Verification (Mathematics)

```
// Source: derived from existing WorldRenderer constants [VERIFIED: codebase constants]
//
// In Galaxy space: ship.LocalPos.Scale = UniObject.Scale(Space.Galaxy) = 1e4 m/unit
// GalaxyRenderFactor = 1e-8 (same as StarRenderFactor and PlanetRenderFactor)
//
// Star at Alpha Cen distance (4.2 ly = 3.97e12 Galaxy units):
//   relUnits = 3.97e12 observer-units
//   renderPos = 3.97e12 * 1e-8 = 39,700 render units
//   far plane = 1e6 render units → 25× headroom ✓
//
// Star radius:
//   radiusMeters = 6.96e8 m
//   r = 6.96e8 / 1e4 (ship scale) * 1e-8 (factor) = 6.96e-4 render units
//   → sub-pixel at this distance (correct, D-15 1:1 honesty)
//
// GalaxyRenderFactor = 1e-8 confirmed correct for Galaxy space. ✓
```

### TestSetup Galaxy SOI Replacement

```csharp
// Source: extends TestSetup.SetupScene [VERIFIED: AddGameObject signature, existing _galaxy line]
// Replace: _galaxy = AddGameObject(_root, new Double3(0, 0, 0), 5e3);
// With:
private const double GalaxySOI = 5e4;   // Universe units; 5e4 * 1e16 = 5e20 m = ~50 kly ✓

_galaxy = AddGameObject(_root, new Double3(0, 0, 0), GalaxySOI);
GameObjects[_galaxy].Name              = "HOME GALAXY";
GameObjects[_galaxy].ObjectType        = UniObject.Type.Galaxy;
GameObjects[_galaxy].RadiusMeters      = 5e20;
GameObjects[_galaxy].Luminosity        = 1e10;        // galaxy-scale, planner tunes
GameObjects[_galaxy].BaseColor         = new Color(0.7f, 0.75f, 1.0f);
GameObjects[_galaxy].GalaxyType        = 0;           // spiral
GameObjects[_galaxy].GalaxySeed        = 0.42f;
GameObjects[_galaxy].GalaxyOrientation = new Vector3(0f, 1f, 0f); // disc in XZ
```

### UniMath Usage for Galaxy Direction (SkyboxRenderer)

```csharp
// Source: existing SkyboxRenderer.SyncSkyPoints pattern [VERIFIED: SkyboxRenderer.cs lines 149-161]
// Already uses UniMath.RelativePosition — no change needed for galaxies.
// Galaxy is just another NextTierSkybox body; the LCA walk handles Universe↔Galaxy scale gap.

bool hasCommonAncestor = UniMath.RelativePosition(ship, galaxyBody, objs, out UniVec3 relUni);
Double3 delta = hasCommonAncestor ? relUni.ToDouble3() : Double3.Zero;
double  len   = hasCommonAncestor ? delta.Magnitude() : 0.0;
// At Andromeda separation: relUni has Units~2.4e6, Scale~1e16; ToDouble3() = ~2.4e22 m ✓
// Double3 handles this: 2.4e22 m well within double range (~1.8e308)
```

### FlightController MaxSpeed Fix

```csharp
// Source: FlightController.cs [VERIFIED: line 141 Mathf.Clamp(value, 0.0, SpeedOfLight)]
// Remove the SpeedOfLight clamp from MaxSpeed setter:

// BEFORE (line ~141):
set => _maxSpeed = Mathf.Clamp(value, 0.0, SpeedOfLight);   // BUG: caps at 3e8 m/s

// AFTER:
set => _maxSpeed = System.Math.Max(0.0, value);

// Also change the default value in the field declaration:
private double _maxSpeed = 2e20;  // 2e20 m/s ≈ 2-minute intergalactic crossing [ASSUMED: planner confirms]
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Recursive `TrySpaceTransition` | Iterative with `MaxIterations=32` | Phase 1 | Prevents stack overflow; handles multi-boundary crossings per frame |
| Absolute-from-root position math (`AbsolutePositionInRoot`) | LCA-relative `UniMath.RelativePosition` | 2026-06-16 (quick task) | Exact precision at Universe scale |
| Name-based star check (`body.Name == "STAR"`) | Type-based (`body.ObjectType == Type.Star`) | Phase 3 (this phase) | Supports multiple stars across galaxies |
| Single-galaxy Universe | Three-galaxy Universe | Phase 3 (this phase) | Enables cross-galaxy travel TRV-02 |
| Star-only sky loop | Star + galaxy parallel loops | Phase 3 (this phase) | Renders procedural galaxy discs |

**Deprecated/outdated:**

- `_galaxy` SOI placeholder `5e3` Universe units: 10× too small (should be `5e4`). Set in `TestSetup` Phase 1. Replaced in Phase 3.
- `IsStarBody(body)` name-string check in `WorldRenderer`: replaced with `body.ObjectType == UniObject.Type.Star` in Phase 3.
- `MaxSpeed` default `1e11` m/s and `SpeedOfLight = 3e8` as cap on MaxSpeed: both too small for intergalactic travel. Replaced in Phase 3.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Galaxy physical radius ≈ 5e20 m (Milky Way-like), used as both SOI and RadiusMeters | Scale Reference, Pattern 6 | If wrong, galaxy SOI is too large/small → ship enters/exits at wrong visual disc edge |
| A2 | GLSL `uniform int galaxy_types[N]` works correctly with Godot 4.6 `SetShaderParameter` | Pattern 3, Pitfall 5 | May need packing type into float channel instead; minor workaround available |
| A3 | `GALAXY_LOD_THRESHOLD = 2e-4` produces visible disc at ~3 screen pixels | Pattern 2, Pitfall 6 | Requires tuning in play-test; not a blocking issue |
| A4 | `uv /= max(size * 80.0, 0.001)` maps disc angular size to UV correctly | Pattern 2, Pitfall 6 | Requires tuning; disc may appear too small or too large |
| A5 | MaxSpeed default of `2e20 m/s` gives a "reasonable" intergalactic crossing time | Pattern 5, Pitfall 3 | Planner confirms crossing time; 2e20 gives ~2 minutes at 2.4e22 m |
| A6 | Galaxy luminosity value `1e10` solar luminosities produces visible galaxy brightness | Pattern 6 | May need tuning; StarRendering.ApparentBrightness handles galaxy-scale flux through the same log curve |
| A7 | Galaxy BaseColor, GalaxyOrientation, seed values (aesthetics) | Pattern 6 | Author taste; planner specifies |
| A8 | Third galaxy placed at ~1.8e22 m at 45° offset (X=Z=1.27e6 Universe units) | Pattern 6 | Angular separation from Galaxy 2 sufficient for distinct sky positions; planner may adjust |
| A9 | `GALAXY_DISC_SCALE = 80.0` is the right normalization for angular-size to disc-UV mapping | Code Examples | Tuning; note in plan as play-test knob |

**If this table is empty:** All claims in this research were verified or cited — no user confirmation needed.
*This table is non-empty; A1/A5/A7/A8 should be confirmed by the planner when authoring test data.*

---

## Open Questions

1. **Godot 4.6 `SetShaderParameter` and `int[]` uniform arrays**
   - What we know: Godot's `ShaderMaterial.SetShaderParameter` accepts `Variant`; GLSL allows `uniform int array`.
   - What's unclear: Whether the C# `int[]` → `Variant` → GLSL `int[]` pathway works in Godot 4.6 Mono.
   - Recommendation: Wave 0 task — write a minimal test that pushes `int[] { 0, 1 }` to a `uniform int arr[2]` in a test sky shader. If it fails, use `float[]` with `0.0f`/`1.0f` values and adjust the shader comparison.

2. **Godot `Vector3` in `UniObject` for `GalaxyOrientation`**
   - What we know: `UniObject` is in the global namespace (no `using Godot`); `Vector3` is a Godot type.
   - What's unclear: Whether adding `Godot.Vector3` to the global-namespace `UniObject` struct is consistent with the project's namespace conventions (currently `UniObject` uses `Godot.Color` and `Godot.Color` is already imported via `using Godot` at the top of `UniObject.cs`).
   - Recommendation: [VERIFIED: UniObject.cs line 4 `using Godot;`] — `Godot.Vector3` already available; use `Vector3` directly.

3. **Galaxy `Luminosity` scale**
   - What we know: `StarRendering.ApparentBrightness` uses `flux = L / dist^2` with `LogFluxFloor = -40.0`.
   - What's unclear: What `Luminosity` value makes a distant galaxy (~2.4e22 m) appear as a pixel-bright point at `Exposure=0`.
   - Recommendation: The log model compresses flux linearly; `L=1e10` solar luminosities at 2.4e22 m → flux = 1e10 / (2.4e22)^2 ≈ 1.7e-35 → log10 = -34.77. With LogFluxFloor=-40: brightness = (-34.77 - (-40)) * 0.048 = 0.251. Visible. [ASSUMED: computation correct] Mark as play-test tuning knob.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | C# compilation | ✓ | 9.0.306 | — |
| Godot Engine 4.6.2 | Runtime/editor | Assumed ✓ | 4.6.2 | — |
| AVX2 CPU | Double3 SIMD path | Assumed ✓ | — | Scalar fallback in Double3 |

*No new external dependencies introduced in Phase 3.*

---

## Validation Architecture

`nyquist_validation` is `false` in `.planning/config.json` — this section is omitted per config.

---

## Security Domain

`security_enforcement` is `true` in `.planning/config.json`.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | Single-player game — no auth |
| V3 Session Management | No | No session/network |
| V4 Access Control | No | No multi-user |
| V5 Input Validation | Yes (minimal) | `MaxSpeed` setter now uncapped — validate that no export accidentally sets MaxSpeed to NaN/Infinity; use `System.Math.Max(0.0, value)` not just assignment |
| V6 Cryptography | No | No encryption needed |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Uncapped `MaxSpeed` export causes NaN/Infinity position | Tampering | `System.Math.Max(0.0, value)` guard in setter; `double.IsFinite` check in `ApplyMotion` before `TranslatePos` |
| Galaxy `RadiusMeters` ≤ 0 skips speed scan | Tampering (gameplay integrity) | TestSetup must set `RadiusMeters > 0` for all galaxy bodies; or add `>0` guard comment |

---

## Package Legitimacy Audit

Phase 3 installs no new packages. This section is not applicable.

---

## Sources

### Primary (MEDIUM confidence)
- `Scripts/GameWorld.cs` — SOI transition logic, MaxIterations, iterative structure
- `Scripts/TestSetup.cs` — existing world data, `_galaxy` SOI placeholder `5e3`
- `Scripts/UniObject.cs` — Type enum, Space enum, Scale values
- `Scripts/Render/SkyboxRenderer.cs` — existing sky uniform push, `_skyDirs` cache, `MaxStars`
- `Scripts/Render/WorldRenderer.cs` — `IsStarBody`, `GalaxyRenderFactor`, `SyncBodies` pattern
- `Scripts/Render/StarRendering.cs` — `AngularRadius`, `ApparentBrightness`, `Exposure`
- `Scripts/Flight/FlightController.cs` — `MaxSpeed`, `SpeedOfLight` cap, `UpdateSpeedEnvelope`
- `Scripts/TierClassifier.cs` — classification logic traced for all tier scenarios
- `Scripts/Math/UniMath.cs` — LCA walk, RelativePosition, RelativeMetres
- `Shaders/skybox.gdshader` — existing star loop structure, EYEDIR note, MAX_STARS
- `.planning/phases/03-cross-galaxy-travel/03-CONTEXT.md` — locked decisions D-28 through D-41
- [godotengine.org/article/custom-sky-shaders-godot-4-0/](https://godotengine.org/article/custom-sky-shaders-godot-4-0/) — EYEDIR world-space confirmation
- [github.com/godotengine/godot-docs/blob/master/tutorials/shaders/shader_reference/sky_shader.rst](https://github.com/godotengine/godot-docs/blob/master/tutorials/shaders/shader_reference/sky_shader.rst) — sky shader built-in variables

### Secondary (LOW confidence)
- WebSearch: GLSL procedural spiral galaxy patterns — arm functions, disc falloff patterns
- WebSearch: FTL tunneling / CCD game dev concepts — confirmed standard CCD is unnecessary at these scales

---

## Metadata

**Confidence breakdown:**

- Standard Stack: HIGH — everything confirmed from codebase read; no new packages
- Architecture: HIGH — full codebase trace of TierClassifier, SkyboxRenderer, WorldRenderer, FlightController interactions
- Scale math: HIGH — derived from first principles using verified codebase constants
- Procedural shader techniques: LOW — GLSL galaxy shaders are well-understood aesthetically but specific constants (LOD threshold, disc scale, arm parameters) require play-test tuning
- Pitfalls: HIGH — identified from direct code inspection

**Research date:** 2026-06-16
**Valid until:** 2026-07-16 (stable stack; shader constants LOW-confidence items need play-test)
