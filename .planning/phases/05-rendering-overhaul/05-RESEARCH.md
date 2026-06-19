# Phase 5: Rendering Overhaul - Research

**Researched:** 2026-06-19
**Domain:** Godot 4.6 Forward+ post-process rendering — luminous-body descriptor pipeline, depth-aware screen-space pass, skybox replacement
**Confidence:** MEDIUM (architecture from codebase inspection HIGH; Godot 4.6-specific API behaviour MEDIUM/LOW due to rapidly-evolving engine internals)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01** Consolidate, preserve precision math. Rewrite the structure; preserve UniMath LCA-relative walks, floating-origin loop, StarRendering, TierClassifier, body_lit.gdshader.
- **D-02** One descriptor, shared by all drawers. Each frame, each luminous body is described once (direction, angular size, brightness, color, distance LOD weight). Collapses `_skyDirs` + `_lastRenderPositions` into one source.
- **D-03** Crossfades are distance-driven, continuous, and depth-aware. Star near↔far and galaxy far↔near are smooth functions of distance, not discrete SOI-boundary swaps. Post-process pass must read the depth buffer.
- **D-04** No-regression bar, improvement welcome; judged by per-tier in-game play-test.
- **D-05** Glow/point/galaxy pass composes in HDR/linear BEFORE the dither quantizes the whole frame. New pass + dither pass must be ordered correctly.
- **D-06** CRT scanlines (RND-01) OUT OF SCOPE.
- **D-07** Remove SkyboxRenderer + skybox.gdshader; keep WorldRenderer meshes. Descriptor that fed the Sky shader now feeds post-process pass.
- **D-08** One phase, 4 sequential play-test-gated plans:
  1. Branch + descriptor/projection foundation (alongside still-running skybox; no visual change)
  2. Post-process star glow + point lights (depth-aware; replace Sky-shader star points)
  3. Galaxy distance crossfade (disc fades out approaching; remove skybox)
  4. Dither composition + cleanup
- **D-09** Fold P1 (galaxy-space-star-meshes-invisible) + P2 (galaxy-visibility-in-universe-space) in; close them here.
- **D-10** Work on branch `phase-05-rendering-overhaul`.
- ⛔ NO manual clip-space billboard MultiMesh (StarPointRenderer anti-pattern).
- ✅ UniMath LCA-relative precision math preserved.
- ✅ StarRendering single source of truth for appearance.
- ✅ 8-bit dither (D-27), unit-space render, per-space 1e-8 render factors, 1e6 camera far plane, body_lit.gdshader.
- ✅ D-21 instant exact-match (no palette cross-dissolve; D-03's LOD blend is in HDR before quantization).

### Claude's Discretion

- Exact post-process technique for feeding N projected luminous-body positions to a full-screen shader — flag for research; must be depth-aware.
- Depth-buffer access in the post-process pass (Godot 4.6 Forward+ DEPTH_TEXTURE / screen-space) and correct ordering relative to dither pass.
- Exact glow/halo kernel, distance→LOD-weight curves for star mesh↔point and galaxy disc↔stars crossfades, always-visible brightness floor (P1).
- Galaxy placeholder representation in post (reuse procedural disc look vs simpler sprite) and disc↔stars crossfade thresholds.
- How the descriptor/projection is structured for unit-testability (extend TierClassifier tests with representation/LOD-weight logic).

### Deferred Ideas (OUT OF SCOPE)

- CRT scanline effect (RND-01) — own later task.
- `galaxy-disc-tilt-foreshortening` — RESOLVED-PENDING-VERIFY; re-confirm after skybox rework.
- Procedural universe generation, cockpit art, economy, combat.

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RND-02 | Bodies in current render tier as sphere meshes; bodies one tier out deferred to sky/post | Descriptor D-02 unifies tier classification; WorldRenderer keeps meshes; post-process covers the rest |
| RND-04 | Current system's star(s) rendered as bright emissive sphere meshes with lighting effects | body_lit.gdshader preserved; glow/halo post-process wraps mesh depth-aware |
| RND-05 | Dynamic sky (now: post-process pass) represents bodies outside current tier as distant light points | Post-process luminous pass replaces skybox; star_dirs/galaxy_dirs → LuminousBodyDescriptor |
| RND-07 | Sky↔mesh handoff is visually continuous — no pop, jump, or flicker on tier crossing | D-02 single descriptor guarantees continuous representation; distance LOD weight replaces discrete swap |
| RND-01 | 8-bit dithered look (CRT deferred) | D-05 HDR-before-dither ordering; dithering.gdshader unchanged |
| RND-03 | Planets rendered as sphere meshes with dithering and 8-bit palette | Unchanged; WorldRenderer + body_lit.gdshader preserved |
| RND-06 | 1:1-scale unit-space render with bounded far plane | 1e-8 render factors + 1e6 far plane preserved unchanged |

</phase_requirements>

---

## Summary

Phase 5 replaces the current two-renderer architecture (WorldRenderer meshes + SkyboxRenderer Sky-shader points) with a single unified pipeline: **classify → describe once → draw with multiple drawers**. The structural problem is that the existing Sky `shader_type sky` renders behind everything and cannot interact with depth, so glow cannot wrap around near meshes and galaxy/star crossfades are impossible. The solution is a **depth-aware post-process pass** that runs after the 3D scene but before the 8-bit dither.

The key technical facts established by this research:

1. **`hint_depth_texture` does NOT work in `canvas_item` shaders** (Godot bug #74464). Depth-aware post-processing must use a `MeshInstance3D` quad (shader_type spatial) with `POSITION = vec4(VERTEX.xy, 1.0, 1.0)` to bypass transforms and write directly to clip space — this is the "advanced post-processing" pattern in official Godot docs and is confirmed working in Forward+.

2. **The existing `PostProcessRenderer` ColorRect (dithering.gdshader) reads `hint_screen_texture`**, which captures the 3D scene **after WorldEnvironment glow/tonemap** has been applied. Adding a luminous-body quad *ahead* of the ColorRect in draw order, using `render_mode unshaded, additive`, composes glow/halo in the 3D scene's sRGB-space buffer — not a separate HDR pass. To truly compose in HDR/linear (D-05), a SubViewport strategy or CompositorEffect would be needed. **Practical recommendation: use a spatial quad with additive blend mode placed as a WorldRenderer child** — this renders during the 3D transparent pass (after opaque geometry, before glow/env effects), so the luminous contribution is naturally included in glow. This achieves D-05's intent (luminous contributes before dither quantizes) without requiring CompositorEffect.

3. **Uniform arrays (the existing `star_dirs` / `star_colors` / `star_sizes` pattern) remain the right technique** for feeding N body descriptors to a post-process pass. The 65 KB uniform buffer limit allows ~1365 vec4s; 8 stars × 4 vec4s + 4 galaxies × 6 vec4s = ~56 vec4s total — well within budget. No texture buffer needed.

4. **Galaxy disc logic from `skybox.gdshader`** (procedural spiral/elliptical disc with D-59 tilt foreshortening) is directly reusable in the new post-process shader — the math is screen-space and does not depend on `EYEDIR` being a sky-shader built-in; it can be rewritten using a world-space direction uniform.

**Primary recommendation:** Build a single new `LuminousPassRenderer` (spatial shader quad, camera child) that reads the depth texture, samples screen_texture for blending, receives the unified descriptor arrays, draws glow/point/halo for stars and disc for galaxies, and renders in the 3D transparent pass so WorldEnvironment glow picks it up naturally before the CanvasLayer dither pass reads it.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Floating-origin mesh sync (planets, near stars) | WorldRenderer (3D world) | — | Unchanged; meshes stay in floating-origin space |
| Body appearance values (brightness, size, color) | StarRendering (pure static) | — | Single source of truth; feed to all drawers |
| Tier/distance classification | TierClassifier (pure, tested) | LuminousBodyDescriptor | Classify first, then extend to LOD weight |
| Per-frame descriptor computation | LuminousBodyDescriptor builder (Render layer) | WorldRenderer (mesh side) | One pass; replaces two parallel caches |
| Post-process star glow/point, galaxy disc | LuminousPassRenderer (spatial quad in 3D) | — | Depth-aware; in 3D transparent pass before glow |
| 8-bit dither quantisation | PostProcessRenderer (CanvasLayer ColorRect) | — | Unchanged; runs after 3D scene incl. luminous pass |
| Precision position math | UniMath (global, pure) | — | Mandatory for all projection; no alternatives |
| Sky background (starfield) | REMOVED (D-07) | — | SkyboxRenderer + skybox.gdshader deleted in Plan 3 |

---

## Standard Stack

### Core (all preserved/reused — no new packages)

| Component | Version | Purpose | Status |
|-----------|---------|---------|--------|
| Godot 4.6.2 Mono | 4.6.2 | Engine, Forward+ DX12, spatial/canvas shaders | Locked [VERIFIED: project.godot] |
| C# 12 / .NET 8.0 | net8.0 | Game logic, renderer classes | Locked [VERIFIED: EcoSpace.csproj] |
| GDShader (GLSL subset) | Godot 4.6 | Shader language for all .gdshader files | Locked [ASSUMED] |
| UniMath / UniVec3 | codebase | LCA-relative position math | Locked [VERIFIED: codebase] |
| StarRendering | codebase | Brightness/size single source of truth | Locked [VERIFIED: codebase] |
| TierClassifier | codebase | Space classification, 30+ tests | Locked/extend [VERIFIED: codebase] |
| PostProcessRenderer + dithering.gdshader | codebase | 8-bit dither; run unchanged after new pass | Locked [VERIFIED: codebase] |
| body_lit.gdshader | codebase | Lambert shading for planet/body meshes | Locked [VERIFIED: codebase] |

### New Components (Phase 5 introduces)

| Component | Kind | Purpose |
|-----------|------|---------|
| `LuminousBodyDescriptor` (struct/class, C#) | New | Per-body: direction, angular-size, brightness, color, LOD weight, type |
| `LuminousDescriptorBuilder` (C#, Render namespace) | New | Per-frame classify+project loop; replaces SkyboxRenderer._skyDirs + WorldRenderer._lastRenderPositions |
| `LuminousPassRenderer` (Node3D child, spatial shader quad) | New | Depth-aware post-process star glow/halo + galaxy disc; draws in 3D transparent pass |
| `luminous_pass.gdshader` (spatial, unshaded+additive) | New | Screen-space point/glow/galaxy-disc with hint_depth_texture |

### No New Packages Required

This phase is a pure codebase restructure. No NuGet packages, no Godot asset library packages.

---

## Package Legitimacy Audit

> Not applicable — no external packages are introduced. All dependencies are codebase-internal or the existing locked runtime (Godot 4.6.2 Mono, .NET 8.0).

---

## Architecture Patterns

### System Architecture Diagram

```
PER FRAME (_Process)
─────────────────────────────────────────────────────────────────────
GameWorld.GameObjects (read-only)
          │
          ▼
  LuminousDescriptorBuilder
  ┌───────────────────────────────────────────────────┐
  │ For each body:                                    │
  │   TierClassifier.Classify() → SkyTier            │
  │   UniMath.RelativePosition() → direction UniVec3  │
  │   StarRendering.AngularRadius() → angular size    │
  │   StarRendering.ApparentBrightness() → brightness │
  │   DistanceLODWeight(dist) → [0..1]                │
  └──────────┬────────────────────────────────────────┘
             │  LuminousBodyDescriptor[]
             ├──────────────────────────────────────────►  WorldRenderer
             │                                             (meshes: planets,
             │                                              near stars — unchanged)
             │
             └──────────────────────────────────────────►  LuminousPassRenderer
                                                           (spatial quad, camera child)
                                                           reads:  hint_depth_texture
                                                                   hint_screen_texture (optional)
                                                           draws:  star point/glow/halo
                                                                   galaxy disc/crossfade
                                                           blend:  additive (3D transparent pass)
                                                                   → WorldEnvironment glow picks it up

3D RENDER PASS ORDER (Forward+ DX12)
─────────────────────────────────────────────────────────────────────
  [1] Depth prepass
  [2] Opaque 3D (WorldRenderer meshes: planets, star spheres, body_lit)
  [3] Sky (REMOVED in Plan 3 — SkyboxRenderer + skybox.gdshader deleted)
  [4] Transparent 3D  ←── LuminousPassRenderer quad renders HERE
  [5] WorldEnvironment glow / tonemap (picks up glow from step 4)
  [6] CanvasLayer 2D  ←── PostProcessRenderer dithering.gdshader
      (hint_screen_texture captures 3D scene + luminous pass + glow)
```

### Recommended Project Structure

```
Scripts/Render/
├── WorldRenderer.cs           # Keep; remove _lastRenderPositions cache (collapse into descriptor)
├── StarRendering.cs           # Keep unchanged
├── PostProcessRenderer.cs     # Keep unchanged
├── SkyboxRenderer.cs          # DELETED in Plan 3
├── LuminousBodyDescriptor.cs  # New: per-body data struct
├── LuminousDescriptorBuilder.cs  # New: per-frame classify+project loop
└── LuminousPassRenderer.cs    # New: Node3D, spatial quad, pushes descriptor arrays to shader

Shaders/
├── luminous_pass.gdshader     # New: shader_type spatial, depth-aware glow+disc
├── dithering.gdshader         # Keep unchanged
├── body_lit.gdshader          # Keep unchanged
└── skybox.gdshader            # DELETED in Plan 3
```

### Pattern 1: The Unified Descriptor (D-02)

**What:** A single C# struct built once per frame per body, consumed by both WorldRenderer (mesh side) and LuminousPassRenderer (post-process side). Replaces the dual-cache (`_skyDirs` in SkyboxRenderer, `_lastRenderPositions` in WorldRenderer).

**When to use:** Every frame, every body, before any drawing.

```csharp
// Source: derived from existing SkyboxRenderer.SyncSkyPoints pattern + new LOD field
public struct LuminousBodyDescriptor
{
    public int      BodyIndex;
    public Vector3  Direction;       // world-space unit vector, ship→body (from UniMath)
    public float    AngularSize;     // smoothstep space: (1 - cos theta), floored at pixel
    public float    Brightness;      // [0,1] from StarRendering.ApparentBrightness
    public Color    BaseColor;       // body.BaseColor
    public float    LodWeight;       // 0 = point/disc only; 1 = mesh only; blend in between
    public UniObject.Type BodyType;  // Star vs Galaxy
    public int      GalaxyType;      // spiral=0, elliptical=1 (for galaxies)
    public Vector4  GalaxyOrientation; // xyz=disc_normal, w=seed (for galaxies)
    public double   DistanceMeters;  // for LOD weight computation
}
```

**Key invariant:** `LodWeight` is a smooth function of `DistanceMeters`, not a SOI-boundary flag. Same numbers → different drawers → pops are mathematically impossible.

### Pattern 2: Distance LOD Weight Curve

**What:** Maps `distanceMeters` to a scalar `[0..1]` where 0 = "distant body, post-process only" and 1 = "near body, mesh only". Values in between drive crossfade alpha.

**When to use:** For stars: blend between point/glow (low LOD weight) and mesh+glow (high LOD weight). For galaxies: blend between disc placeholder (high LOD weight = far) and no disc (low LOD weight = inside, individual stars take over).

```csharp
// Source: [ASSUMED] — thresholds to be tuned in play-test (D-04)
// Smoothstep crossfade: near star at < NearFadeStart gets full mesh; beyond NearFadeEnd is point-only
static float StarLodWeight(double distMeters)
{
    const double NearFadeStart = 5e12;  // ~0.5 light-year — tune in play-test
    const double NearFadeEnd   = 5e13;  // ~5 light-years
    float t = (float)Math.Clamp((distMeters - NearFadeStart) / (NearFadeEnd - NearFadeStart), 0.0, 1.0);
    return 1.0f - t;  // 1 = near (mesh), 0 = far (point)
}

// Galaxy: disc appears when far from galaxy centre; fades out as you enter the galaxy
static float GalaxyDiscWeight(double distMeters, double galaxySoiMeters)
{
    // disc fully visible when dist > 0.5 * SOI; fades to 0 when dist < 0.1 * SOI
    float t = (float)Math.Clamp((distMeters - 0.1 * galaxySoiMeters) / (0.4 * galaxySoiMeters), 0.0, 1.0);
    return t;
}
```

**Note:** These threshold values are `[ASSUMED]` starting points. Play-test gates in each plan will calibrate them.

### Pattern 3: Depth-Aware Post-Process Quad (CRITICAL — new technique)

**What:** A `MeshInstance3D` with a `QuadMesh` (2×2, Flip Faces enabled), added as a **child of the Camera3D** in the scene tree. Uses `shader_type spatial` with vertex stage that writes directly to clip space. This is the **only way to sample `hint_depth_texture` in Godot 4 Forward+** — `canvas_item` shaders cannot access the depth buffer (Godot issue #74464). [CITED: docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html]

**When to use:** Plan 2 — when the LuminousPassRenderer node is built.

```glsl
// Source: [CITED: docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html]
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never;

uniform sampler2D depth_texture   : hint_depth_texture;
uniform sampler2D screen_texture  : hint_screen_texture, repeat_disable, filter_nearest;

// Luminous body descriptor arrays (same structure as SkyboxRenderer pushed to skybox.gdshader)
const int MAX_STARS    = 8;
const int MAX_GALAXIES = 4;
uniform vec3  star_dirs[MAX_STARS];
uniform vec4  star_colors[MAX_STARS];    // rgb=BaseColor, a=brightness
uniform float star_sizes[MAX_STARS];     // (1 - cos theta) smoothstep space
uniform float star_lod_weights[MAX_STARS]; // 0=far/point, 1=near/mesh
uniform int   star_count = 0;

uniform vec3  galaxy_dirs[MAX_GALAXIES];
uniform vec4  galaxy_colors[MAX_GALAXIES];
uniform float galaxy_sizes[MAX_GALAXIES];
uniform float galaxy_disc_weights[MAX_GALAXIES]; // 0=inside/no-disc, 1=far/full-disc
uniform int   galaxy_types[MAX_GALAXIES];
uniform vec4  galaxy_orientations[MAX_GALAXIES];
uniform int   galaxy_count = 0;

void vertex() {
    // Bypass all transforms — write directly to clip space.
    // VERTEX.xy on a 2x2 QuadMesh is in [-1,1]. w=1.0 → NDC depth at far plane.
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
    vec4 col = vec4(0.0);

    // --- Depth sample (for occlusion: skip glow where geometry is close) ---
    float raw_depth = texture(depth_texture, SCREEN_UV).x;
    // Convert to linear depth (view-space Z). Forward+ uses reversed-Z (near=1, far=0).
    vec3  ndc       = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
    vec4  view      = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    float lin_depth = -view.z / view.w;   // positive = distance from camera in metres (render units)

    // --- Star loop ---
    for (int i = 0; i < star_count; i++) {
        float d    = dot(normalize(EYEDIR), star_dirs[i]);   // EYEDIR available in spatial fragment
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);

        // Glow halo: wider softened ring around the point core
        float halo_size = star_sizes[i] * 8.0;   // tune in play-test
        float halo = smoothstep(1.0 - halo_size, 1.0, d) * 0.3;

        // Depth gate: suppress glow/point if a mesh is in front (renders behind near geometry)
        // star_dirs[i] encodes a world-space direction; lin_depth check is approximate but sufficient
        float vis = (lin_depth > 0.5) ? 1.0 : 0.0;   // 0.5 render units ~ close geometry present

        float alpha  = star_colors[i].a * (disc + halo) * vis;
        // LOD blend: fade point/glow out as star approaches (mesh takes over)
        float lod_fade = 1.0 - star_lod_weights[i];
        col.rgb += star_colors[i].rgb * alpha * lod_fade;
    }

    // --- Galaxy loop ---
    for (int i = 0; i < galaxy_count; i++) {
        float d         = dot(normalize(EYEDIR), galaxy_dirs[i]);
        float disc_w    = galaxy_disc_weights[i];

        if (disc_w < 0.001) continue;  // inside galaxy — no disc

        float size      = galaxy_sizes[i];
        float point_disc = smoothstep(1.0 - size, 1.0, d);

        float galaxy_bright;
        if (size < 2e-4) {
            galaxy_bright = point_disc * galaxy_colors[i].a;
        } else if (d > 0.0) {
            // Reuse procedural disc logic from skybox.gdshader (same UV derivation)
            // [galaxy_disc_coords_tilted + spiral_galaxy / elliptical_galaxy — copy verbatim]
            galaxy_bright = point_disc * galaxy_colors[i].a;  // placeholder; Plan 3 adds disc
        } else {
            galaxy_bright = 0.0;
        }

        col.rgb += galaxy_colors[i].rgb * galaxy_bright * disc_w;
    }

    ALBEDO = col.rgb;
    ALPHA  = 1.0;  // additive blend; alpha is ignored by blend_add
}
```

**Depth occlusion note:** `lin_depth` is in render units (scaled by the per-space factor). "Geometry is in front" means `lin_depth < expected_star_dist_in_render_units`. Since stars are always far beyond the far plane in render units, a simpler check "is there any foreground geometry at this pixel?" uses the raw depth: `raw_depth < 1.0` (not at the far value) means opaque geometry exists. Tune this with play-test.

### Pattern 4: `EYEDIR` in Spatial Shader Fragment

**What:** In a `shader_type spatial` fragment, the built-in `EYEDIR` is the normalized view direction in **world space** — identical in meaning to EYEDIR in the sky shader. This means the dot-product test `dot(EYEDIR, star_dirs[i])` from `skybox.gdshader` works unchanged in the new spatial shader. [CITED: docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html] [ASSUMED — verify in Godot 4.6 specifically; API may differ from 4.4 docs]

### Pattern 5: Descriptor Push — C# to Shader

**What:** The same `SetShaderParameter` pattern used by `SkyboxRenderer` applies to `LuminousPassRenderer`. The spatial shader's `ShaderMaterial` receives packed arrays.

```csharp
// Source: mirrors existing SkyboxRenderer.SyncSkyPoints pattern [VERIFIED: codebase]
// In LuminousPassRenderer._Process or SyncDescriptor():
_mat.SetShaderParameter("star_dirs",        _starDirs);      // Vector3[]
_mat.SetShaderParameter("star_colors",      _starColors);    // Color[]
_mat.SetShaderParameter("star_sizes",       _starSizes);     // float[]
_mat.SetShaderParameter("star_lod_weights", _starLodWeights); // float[]
_mat.SetShaderParameter("star_count",       starCount);
// ...galaxies similarly
```

Arrays are pre-allocated at MaxStars / MaxGalaxies capacity; same pattern as existing code. Uniform buffer usage: 8 stars × ~4 vec4s + 4 galaxies × ~6 vec4s ≈ 56 vec4s × 16 bytes = ~896 bytes — far below the 65 KB limit. [VERIFIED: codebase — existing arrays work; budget calc ASSUMED]

### Pattern 6: Descriptor Builder — Replacing Two Caches

**What:** `LuminousDescriptorBuilder` is a new C# class in the `Render` namespace that runs once per frame, iterates `GameObjects`, calls `TierClassifier.Classify`, calls `UniMath.RelativePosition` for direction, calls `StarRendering.*` for appearance, and produces a `LuminousBodyDescriptor[]`. Both WorldRenderer and LuminousPassRenderer consume this instead of maintaining their own parallel caches.

**Key contract:** read-only consumer of `GameWorld` state. Must NOT call `TranslatePos` or mutate `UniVec3`. [VERIFIED: codebase — same constraint as existing renderers]

### Anti-Patterns to Avoid

- **Anti-pattern: `hint_depth_texture` in a `canvas_item` shader.** Godot bug #74464 breaks shader compilation in Forward+. The spatial quad is the only valid depth-access approach. [CITED: github.com/godotengine/godot/issues/74464]
- **Anti-pattern: Absolute-from-root metres before subtracting.** `UniVec3` cast to double before LCA walk → catastrophic cancellation at Universe scale. Always `UniMath.RelativePosition` first, `ToDouble3()` after. [VERIFIED: codebase — CLAUDE.md + UniMath.cs docs]
- **Anti-pattern: Manual clip-space billboard MultiMesh (StarPointRenderer).** The prior `StarPointRenderer` dead-end. Locked ⛔ constraint. Depth-aware spatial quad is the allowed alternative. [VERIFIED: codebase — HANDOFF + STATE.md]
- **Anti-pattern: Two renderers pushing the same body.** In Plans 1-2, the sky shader and new pass coexist. The descriptor builder feeds BOTH. Do not run the classify+project loop twice — both renderers read from the same `LuminousBodyDescriptor[]` built once per frame.
- **Anti-pattern: Discrete LOD swap on SOI boundary.** Pops. The old D-22 approach. Use the continuous `DistanceLodWeight()` curve instead (D-03).
- **Anti-pattern: Galaxy mesh in `WorldRenderer`.** D-28/T-03-06 guard. Galaxies are post-process only. `WorldRenderer` already has the `body.ObjectType == UniObject.Type.Galaxy` skip guard — preserve it.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-space position → screen direction | Custom absolute-from-root metres accumulation | `UniMath.RelativePosition` → `ToDouble3()` | Catastrophic cancellation at ~1e30 m Universe scale |
| Star brightness/size | Custom inverse-square math | `StarRendering.ApparentBrightness` + `AngularRadius` | Single source of truth; ensures mesh + sky match |
| Body tier classification | Custom space comparison switch | `TierClassifier.Classify` | 30+ green tests; pure; extensible |
| Depth-aware post-process | `canvas_item` + `hint_depth_texture` | Spatial quad (`shader_type spatial`, camera child) | canvas_item depth access broken in Forward+ (bug #74464) |
| Galaxy disc math | Rewrite spiral/elliptical from scratch | Copy `spiral_galaxy` / `elliptical_galaxy` / `galaxy_disc_coords_tilted` from `skybox.gdshader` | Already debugged, has D-59 tilt fix, TILT_FLOOR anti-collapse guard |
| LOD weight curve | Per-body special-case branches | `DistanceLodWeight(distMeters)` smooth function | Pops are impossible when LOD is continuous |
| Descriptor array sizing | Dynamically allocate per frame | Pre-allocated `Vector3[MaxStars]` / `Color[MaxStars]` etc. (same as SkyboxRenderer) | Avoids steady GC pressure per WR-01 |

**Key insight:** The structural complexity of this phase is about *plumbing* (who builds the descriptor, who consumes it, how the quad gets added to the scene) not novel algorithms. The math already exists and is tested.

---

## Common Pitfalls

### Pitfall 1: Depth Texture in canvas_item Shader Breaks Compilation

**What goes wrong:** Adding `uniform sampler2D depth_texture : hint_depth_texture;` to `dithering.gdshader` (a `canvas_item` shader) causes a shader compilation crash in Godot 4 Forward+ (bug #74464). The entire 3D scene may stop rendering.

**Why it happens:** `canvas_item` shaders run in the 2D rendering pipeline which does not have the depth buffer bound. The hint tells the compiler to expect a depth buffer binding that never arrives.

**How to avoid:** All depth-reading code lives in the new `luminous_pass.gdshader` (`shader_type spatial`). The dithering shader remains untouched.

**Warning signs:** Shader compilation errors mentioning depth textures, or the 3D scene rendering black.

### Pitfall 2: EYEDIR Not Available in All Fragment Contexts

**What goes wrong:** `EYEDIR` is a built-in in sky shaders and in spatial fragment shaders on full-screen quads. However, if the quad mesh is not properly set up as a camera child with the bypass vertex trick, frustum culling may cull the quad entirely — no fragment shader runs.

**Why it happens:** `QuadMesh` with `POSITION = vec4(VERTEX.xy, 1.0, 1.0)` bypasses Godot's visibility/culling. Without that vertex trick, the quad at world origin may be outside the frustum.

**How to avoid:** Use the exact vertex shader: `POSITION = vec4(VERTEX.xy, 1.0, 1.0);` with `render_mode unshaded, fog_disabled` on a 2×2 QuadMesh with Flip Faces enabled, added as a child of `Camera3D` in `Main.tscn`.

**Warning signs:** Star glow not visible at all; no shader artifacts.

### Pitfall 3: LuminousPassRenderer and WorldRenderer Double-Update

**What goes wrong:** In Plans 1–2 (skybox still running), three renderers run per frame: SkyboxRenderer, WorldRenderer, and LuminousDescriptorBuilder. If the descriptor builder is called inside both WorldRenderer and LuminousPassRenderer, the classify+project loop runs twice per frame.

**Why it happens:** Each renderer calls the builder independently without sharing the output.

**How to avoid:** Make `LuminousDescriptorBuilder` a separate `Node` that runs first in `_Process` and stores the descriptor array as a field. Both WorldRenderer and LuminousPassRenderer read that field (read-only). Execution order: sort node positions or use Godot's process priority.

**Warning signs:** CPU performance regression (double classify+project loop is ~2× body iterations).

### Pitfall 4: Galaxy Antinode Ghost (Pitfall 7 from skybox.gdshader)

**What goes wrong:** The procedural galaxy disc logic in `skybox.gdshader` has a `d > 0.0` gate. Without this gate, the disc renders on BOTH hemispheres (the galaxy and its antipodal ghost). This must be preserved in `luminous_pass.gdshader`.

**Why it happens:** The perpendicular-projection UV derivation returns a value even when `EYEDIR` points away from the galaxy.

**How to avoid:** Copy the exact `d > 0.0` guard from `skybox.gdshader` line 165 into the galaxy disc branch of `luminous_pass.gdshader`.

**Warning signs:** Galaxy disc visible in all directions simultaneously; huge sky-spanning ring artifact.

### Pitfall 5: Depth Occlusion Threshold vs Render Scale

**What goes wrong:** The depth buffer value for "a planet is in front of this pixel" depends on the per-space render factor (1e-8). A planet at render radius 637 units in Planet space produces a linear depth of 637 render units. A star point occlusion threshold written for Star space may be wrong in Planet space.

**Why it happens:** Linear depth is in render units, not metres; the render factor differs per space.

**How to avoid:** Occlusion check: use the raw (nonlinear) depth. Raw depth approaching 1.0 (near plane, reversed-Z) means close geometry; raw depth near 0.0 (far plane) means empty sky. Use `raw_depth < (1.0 - epsilon)` to detect "foreground geometry exists at this pixel" regardless of scale. This is scale-independent.

**Warning signs:** Star points visible through planets; or star glow absent even in empty sky.

### Pitfall 6: Plan 3 Removes Skybox Before Luminous Pass is Fully Tested

**What goes wrong:** If `SkyboxRenderer` and `skybox.gdshader` are deleted in Plan 2 instead of Plan 3, there is no fallback when the new luminous pass has bugs — the sky is just black.

**Why it happens:** Temptation to clean up before verifying the replacement.

**How to avoid:** D-08 strictly gates: Plan 2 = add luminous pass alongside skybox; Plan 3 = remove skybox. The play-test gate between Plans 2 and 3 confirms the new pass covers what the skybox was drawing before the old code is removed.

**Warning signs:** Pure black sky with some galaxy space stars missing.

---

## Code Examples

### Projection: World-Space Direction from UniMath (migrate from SkyboxRenderer)

```csharp
// Source: [VERIFIED: codebase] — SkyboxRenderer.SyncSkyPoints, lines 168-179
// This pattern is the blueprint for LuminousDescriptorBuilder.Build()
bool hasLca = UniMath.RelativePosition(ship, body, objs, out UniVec3 relUni);
Double3 delta = hasLca ? relUni.ToDouble3() : Double3.Zero;
double len = hasLca ? delta.Magnitude() : 0.0;

Vector3 dir3;
if (!hasLca || len < 1e-30)
    dir3 = Vector3.Up;
else {
    Double3 dir = delta * (1.0 / len);
    dir3 = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
}
```

### Depth Linearization in Spatial Shader (Forward+, reversed-Z)

```glsl
// Source: [CITED: docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html]
// Godot 4.3+ uses reversed-Z: near plane = 1.0, far plane = 0.0.
float raw_depth = texture(depth_texture, SCREEN_UV).x;
vec3  ndc       = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
vec4  view      = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
view.xyz       /= view.w;
float lin_depth = -view.z;   // positive = distance from camera in render units
// "No foreground geometry" check (scale-independent):
bool  sky_pixel = (raw_depth < 1e-6);   // raw_depth ≈ 0 = far plane = empty sky
```

### Bypass Vertex Trick (Camera-Child Quad)

```glsl
// Source: [CITED: docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html]
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never;

void vertex() {
    // VERTEX.xy on a 2x2 QuadMesh is in [-1,1] NDC.
    // w=1.0 places this at the far plane depth without clipping.
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}
```

### Adding the Quad to Main.tscn (C# setup in LuminousPassRenderer._Ready)

```csharp
// Source: [ASSUMED] — derived from Godot 4 advanced post-processing pattern
// Add LuminousPassRenderer as a child of Camera3D; it creates its own quad.
public override void _Ready()
{
    var quad = new QuadMesh { Size = new Vector2(2f, 2f), FlipFaces = true };
    var mesh = new MeshInstance3D { Mesh = quad };

    _mat = new ShaderMaterial
    {
        Shader = GD.Load<Shader>("res://Shaders/luminous_pass.gdshader")
    };
    mesh.MaterialOverride = _mat;
    AddChild(mesh);   // LuminousPassRenderer is a child of Camera3D
}
```

### TierClassifier Extension — LOD Weight (new pure method)

```csharp
// Source: [ASSUMED] — to be added to TierClassifier.cs or a companion LuminousLod.cs
// Pure, no Godot types, unit-testable.
public static class LuminousLod
{
    // Star LOD: 1 = near (mesh dominant), 0 = far (point/glow dominant)
    // Thresholds are [ASSUMED] starting points; tune in play-test.
    private const double StarNearStart = 5e12;   // ~0.5 ly
    private const double StarNearEnd   = 5e13;   // ~5 ly

    public static float StarMeshWeight(double distMeters)
    {
        double t = Math.Clamp((distMeters - StarNearStart) / (StarNearEnd - StarNearStart), 0.0, 1.0);
        return (float)(1.0 - t);   // 1 = mesh, 0 = point
    }

    // Galaxy disc: 1 = far from galaxy (disc visible), 0 = inside galaxy (disc hidden)
    public static float GalaxyDiscWeight(double distMeters, double galaxySoiMeters)
    {
        double fadeStart = 0.1 * galaxySoiMeters;
        double fadeEnd   = 0.5 * galaxySoiMeters;
        double t = Math.Clamp((distMeters - fadeStart) / (fadeEnd - fadeStart), 0.0, 1.0);
        return (float)t;
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `shader_type sky` for background star points | `shader_type spatial` quad for depth-aware post-process | Phase 5 | Depth buffer access, foreground occlusion, glow compositing |
| Discrete SOI-boundary tier swap (D-22) | Continuous distance LOD weight crossfade (D-03) | Phase 5 | Pops impossible by construction |
| Two parallel caches (`_skyDirs` + `_lastRenderPositions`) | One `LuminousBodyDescriptor[]` per frame (D-02) | Phase 5 | Single source of truth; structural alignment |
| SkyboxRenderer + WorldRenderer independently classify bodies | LuminousDescriptorBuilder runs once; both renderers consume | Phase 5 | Eliminates double classify+project; consistent data |

**Deprecated/outdated for Phase 5:**
- `SkyboxRenderer.cs` — deleted in Plan 3 (D-07)
- `Shaders/skybox.gdshader` — deleted in Plan 3 (D-07); galaxy disc logic is copied into `luminous_pass.gdshader`
- `WorldRenderer._lastRenderPositions` dictionary — collapsed into the descriptor (D-02); the `GetRenderPosition` accessor may be simplified or removed
- `SkyboxRenderer._skyDirs` dictionary — collapsed into the descriptor (D-02); `GetSkyDirection` accessor removed

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `EYEDIR` built-in is available in `shader_type spatial` fragment shaders on the quad mesh in Godot 4.6 | Architecture Patterns – Pattern 3 | EYEDIR may need to be derived from VERTEX/camera manually; straightforward to fix but requires shader rewrite |
| A2 | `blend_add` render_mode on a spatial shader quad works as expected in Forward+ DX12 without alpha sorting issues | Architecture Patterns – Pattern 3 | Additive blend on transparent pass may require explicit depth_test_disabled; test in Plan 2 |
| A3 | Star LOD fade thresholds (5e12m–5e13m) produce visually acceptable crossfade in the authored scene | Code Examples – LuminousLod | Wrong thresholds → stars pop in/out visibly; tunable in play-test (D-04) |
| A4 | Galaxy disc weight thresholds (0.1×SOI to 0.5×SOI) produce correct fade | Code Examples – LuminousLod | Wrong thresholds → disc persists inside galaxy or disappears too early; tunable in play-test (D-04) |
| A5 | The spatial quad approach (camera child) is sufficient for depth occlusion without CompositorEffect | Architecture Patterns | CompositorEffect is GPU-thread-based and requires high pipeline knowledge; if occlusion quality is insufficient, CompositorEffect is the fallback |
| A6 | WorldEnvironment glow in Main.tscn picks up the additive luminous pass output naturally (because the quad renders in the 3D transparent pass before glow) | Architecture Diagram | If glow does not capture the luminous pass, a second glow-like effect must be baked into `luminous_pass.gdshader` directly |
| A7 | Raw depth ≈ 0.0 (far plane) is a reliable "empty sky pixel" indicator in Godot 4.6 Forward+ reversed-Z | Pitfall 5 / Pattern 3 | Different sky clearing behaviour may populate far-plane depth differently; verify in Plan 2 |

---

## Open Questions

1. **Does `EYEDIR` exist in `shader_type spatial` fragment on a quad, or must it be derived?**
   - What we know: EYEDIR is documented for sky shaders and is described as "world-space view direction" in Godot 4.x spatial docs.
   - What's unclear: Godot 4.6-specific availability in a quad-spatial fragment vs sky shader context.
   - Recommendation: In Plan 2, add a debug colour that visualises `EYEDIR` directly (output it as ALBEDO.rgb) to confirm it is world-space as expected. If not, derive from `(INV_VIEW_MATRIX * vec4(0,0,-1,0)).xyz`.

2. **Does WorldEnvironment glow pick up the additive luminous pass?**
   - What we know: Glow runs after the 3D transparent pass in Forward+. The spatial quad renders in the transparent pass. Additive blend adds to the colour buffer.
   - What's unclear: Whether Godot's glow threshold samples the pre-composite or post-composite buffer.
   - Recommendation: In Plan 2 play-test, set star brightness very high and confirm glow halos appear. If not, bake a Gaussian blur halo kernel directly into `luminous_pass.gdshader` (fallback not required for D-05's intent — the dither still captures it after the 3D scene).

3. **Can the galaxy disc logic from `skybox.gdshader` be ported verbatim?**
   - What we know: `galaxy_disc_coords_tilted`, `spiral_galaxy`, `elliptical_galaxy` are pure math functions with no sky-shader-specific built-ins. `EYEDIR` in skybox maps to `EYEDIR` in spatial fragment (or derived equivalent).
   - What's unclear: Whether the angular-size units in `(1-cos theta)` smoothstep space transfer correctly to the new context (the `GALAXY_DISC_SCALE=80.0` tuning constant may need recalibration).
   - Recommendation: Copy verbatim in Plan 3; recalibrate `GALAXY_DISC_SCALE` during play-test.

---

## Environment Availability

> Step 2.6: SKIPPED — this phase is a pure codebase restructure with no external tool dependencies. All runtime dependencies (Godot 4.6.2 Mono, .NET 8.0 SDK, DirectX 12) are confirmed present by the fact that the existing codebase builds and runs. No CLI tools, databases, or external services are required.

---

## Validation Architecture

> `workflow.nyquist_validation` is explicitly `false` in `.planning/config.json`. Formal test framework is skipped per config. This section covers what CAN vs CANNOT be unit-tested for this phase, so the planner can include appropriate checkpoints.

### What CAN Be Unit-Tested (extend existing xUnit suite in EcoSpace.Tests)

The key unit-testable seam is **pure C# logic with no Godot dependency** — same constraint as the existing `TierClassifier` and `UniMath` tests.

| New Behavior | Test Type | File | Notes |
|---|---|---|---|
| `LuminousLod.StarMeshWeight(distMeters)` — returns 1.0 at ≤StarNearStart, 0.0 at ≥StarNearEnd, smooth in between | Unit | `LuminousLodTests.cs` | Pure math, no Godot |
| `LuminousLod.GalaxyDiscWeight(distMeters, soiMeters)` — returns 0.0 inside galaxy, 1.0 when far | Unit | `LuminousLodTests.cs` | Pure math, no Godot |
| `LuminousDescriptorBuilder.Build()` — produces correct descriptor fields from a mock hierarchy | Unit | `LuminousDescriptorBuilderTests.cs` | Requires mock `List<UniObject>` like UniMathTests |
| Descriptor direction field = UniMath.RelativePosition output | Unit | `LuminousDescriptorBuilderTests.cs` | Same hierarchy as UniMathTests |
| StarRendering.ApparentBrightness / AngularRadius (existing, regression guard) | Unit | `TierClassifierTests.cs` (existing) | Already covered |
| TierClassifier.Classify (existing, regression guard) | Unit | `TierClassifierTests.cs` (existing) | Already covered; run in Plan 1 to confirm no regression |

**Test framework:** xUnit + GodotSharp linked via `EcoSpace.Tests.csproj`. Run with `dotnet test`. [VERIFIED: codebase — EcoSpace.Tests/ exists, 30+ tests passing]

**Run command:** `dotnet test C:/Users/frede/workspace/godot/eco-space/EcoSpace.Tests/EcoSpace.Tests.csproj`

### What CANNOT Be Unit-Tested (must be play-tested in-engine)

| Behavior | Why Not Unit-Testable | Verification Method |
|---|---|---|
| Spatial shader quad renders in the right pass | Godot rendering pipeline; no C# test hook | Plan 2 play-test: fly in Star space, confirm star glow |
| Depth texture reads correctly (occlusion) | GPU/rendering; no simulation | Plan 2 play-test: fly behind a planet, confirm star glow absent |
| Galaxy disc crossfade transitions smoothly | Visual continuity; no pixel test | Plan 3 play-test: fly from Universe into galaxy, confirm disc fades |
| WorldEnvironment glow picks up luminous pass | Engine rendering order | Plan 2 play-test: verify glow halos appear |
| Dither correctly quantizes the composed frame | D-05 ordering; visual | Plan 4 play-test: 8-bit palette looks consistent |
| No skybox ghost after Plan 3 removal | Visual regression | Plan 3 play-test: all tiers, no black holes or missing bodies |
| TILT_FLOOR (D-59) galaxy foreshortening preserved | Visual; angular geometry | Plan 3 play-test: galaxy at 45° angle still looks tilted |
| Sibling stars visible from Galaxy space (P1) | [ASSUMED] floor brightness | Plan 2 play-test: Galaxy space → stars visible as points |
| Galaxies visible from Universe space (P2) | Distance crossfade correctness | Plan 3 play-test: Universe space → 3 galaxies visible as discs |

### Play-Test Gate Protocol (per D-08)

Each plan's play-test covers:

| Plan | Space to Test | Gate Question |
|------|-------------|--------------|
| Plan 1 | All tiers (no visual change) | Does the projection maths produce the same sky directions as SkyboxRenderer? (compare visually — sky unchanged) |
| Plan 2 | Star space, Galaxy space | Star glow visible? Star point visible from Galaxy space (P1 closed)? Planets/near meshes still correct? |
| Plan 3 | Galaxy space, Universe space | Galaxy discs visible from Universe space (P2 closed)? Disc fades correctly approaching? Old skybox gone with no artifacts? |
| Plan 4 | All tiers | Dither palette consistent edge-to-edge? No banding seams between luminous pass and rest of scene? Final parity/improvement pass. |

---

## Security Domain

> `security_enforcement: true` in config. ASVS Level 1 applies.

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | No auth in rendering layer |
| V3 Session Management | No | No session state |
| V4 Access Control | No | Single-player game, no multi-user |
| V5 Input Validation | Minimal | Shader array sizes clamped by MaxStars/MaxGalaxies constants (existing T-03-01 guard); `(uint)i < (uint)Count` bounds checks preserved |
| V6 Cryptography | No | No cryptographic operations |

**Known threat patterns for this stack:**

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Shader loop DoS (unbounded star/galaxy count) | Denial of Service | MaxStars=8, MaxGalaxies=4 constants in shader and C# — already in existing codebase; preserve in new pass |
| Null UniObject in GameObjects list | Elevation of Privilege | `if (body == null) continue;` guard — already present; preserve in descriptor builder |
| LOD weight = NaN from zero-distance body | Tampering | Clamp LOD weight to [0,1]; guard `distMeters > 1e-30` before division (same as StarRendering.ApparentBrightness) |

---

## Sources

### Primary (verified against codebase — HIGH confidence)
- `Scripts/Render/SkyboxRenderer.cs` — classify→project→appearance loop blueprint for LuminousDescriptorBuilder
- `Scripts/Render/WorldRenderer.cs` — mesh sync loop, render factors, GetRenderPosition accessor
- `Scripts/Render/StarRendering.cs` — appearance rules (ApparentBrightness, AngularRadius, Exposure)
- `Scripts/Render/PostProcessRenderer.cs` — dithering pass host; unchanged by phase 5
- `Scripts/TierClassifier.cs` — tier classification; extend for LOD weight
- `Shaders/skybox.gdshader` — galaxy disc math reusable verbatim (spiral/elliptical/tilt)
- `Shaders/dithering.gdshader` — 8-bit dither; hint_screen_texture reads after 3D scene
- `Shaders/body_lit.gdshader` — Lambert shading; unchanged
- `Scripts/Math/UniMath.cs` — LCA-relative position math; mandatory projection path
- `Main.tscn` — scene structure; Camera3D, CanvasLayer, glow settings
- `EcoSpace.Tests/TierClassifierTests.cs`, `UniMathTests.cs` — existing test patterns

### Secondary (official docs — MEDIUM confidence)
- [Advanced Post-Processing — Godot docs](https://docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html) — spatial quad + depth texture technique
- [Custom Post-Processing — Godot docs](https://docs.godotengine.org/en/stable/tutorials/shaders/custom_postprocessing.html) — CanvasLayer approach limitations (no depth access)
- [The Compositor — Godot docs](https://docs.godotengine.org/en/stable/tutorials/rendering/compositor.html) — CompositorEffect for deeper pipeline access (noted as fallback)
- [Spatial Shaders — Godot docs 4.4](https://docs.godotengine.org/en/4.4/tutorials/shaders/shader_reference/spatial_shader.html) — EYEDIR, render modes, blend modes

### Tertiary (web search — LOW confidence; marked ASSUMED in findings)
- [Godot issue #74464](https://github.com/godotengine/godot/issues/74464) — canvas_item depth texture broken in Forward+
- [Godot forum: hint_screen_texture glow](https://forum.godotengine.org/t/how-do-i-make-sure-the-hdr-glow-bloom-effect-doesnt-get-applied-to-certain-canvas-layers/106258) — glow + CanvasLayer ordering

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all components are existing codebase
- Architecture: HIGH (existing code) / MEDIUM (new spatial quad pattern, Godot 4.6-specific)
- Pitfalls: HIGH — pitfalls 1/2/4/6 are codebase-verified; 3/5 MEDIUM
- LOD thresholds: LOW — [ASSUMED]; calibrated by play-test

**Research date:** 2026-06-19
**Valid until:** 2026-08-01 (Godot 4.6 is the locked engine version; no drift expected)
