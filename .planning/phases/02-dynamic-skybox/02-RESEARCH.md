# Phase 02: Dynamic Skybox - Research

**Researched:** 2026-06-14
**Domain:** Godot 4.6 sky shaders, floating-origin rendering, tiered-space classification, C# unit testing
**Confidence:** MEDIUM

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-17: Real-magnitude brightness/size model.** Each point's apparent brightness and size derive from the body's real luminosity attenuated by true distance — points are magnitude-ranked (Elite/Frontier realism), not uniform dots.
- **D-18: Points carry the body's authored color.** A point uses the body's `BaseColor`.
- **D-19: Minimum-brightness floor.** Apparent brightness is clamped to a floor so every real next-tier body always shows as at least one lit dithered pixel. Exact floor/curve = planner tuning.
- **D-20: Points feed the shared bloom.** Skybox points bloom through the same `WorldEnvironment` glow the star mesh uses.
- **D-21: Instant exact-match swap.** On tier transition, point and new mesh occupy the same screen position with pre-matched color & brightness — no crossfade/alpha blend.
- **D-22: Promotion/demotion triggered by scale-boundary crossings only.** Never by proximity/screen-size.
- **D-23: Add 2-3 sibling star systems under the Galaxy.** Authored in `TestSetup` at realistic interstellar distances with varied colors/luminosities.
- **D-24: Phase 2 = in-system build + logic; visible re-tier/promotion = Phase 3.**
- **D-25: Real next-tier bodies only — no decorative starfield.**
- **D-26: Add an explicit `Luminosity` attribute to `UniObject`.** Field name/units = implementation detail.
- **D-27: Skybox points pass through the SAME dithering post-process as the meshes.**

### Claude's Discretion

- Sky **technique** (Godot `Sky` resource + sky `ShaderMaterial` vs. inverted far-sphere mesh vs. other).
- Half-resolution sky pass — worth it here or not.
- Exact min-brightness floor value and luminosity→apparent-brightness curve.
- Exact sibling-star coordinates, colors, and luminosity values.
- `Luminosity` field name, units, and default.
- How re-tier logic is unit-tested.

### Deferred Ideas (OUT OF SCOPE)

- Visible Star↔Galaxy re-tier + point→mesh promotion demo (Phase 3).
- Other-galaxy point data (Phase 3).
- In-galaxy stars-as-meshes (Phase 3).
- Decorative/ambient background starfield (rejected v1).
- Crossfade/blended handoff (rejected D-21).
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RND-05 | A dynamic spherical skybox represents the bodies just outside the current render tier as distant light points — the stars of other star systems while inside a system. Updates when the player crosses a scale boundary and never drifts with camera rotation. | Sky technique (Q1), world-fixed direction (Q1/Q2), re-tier logic (Q7), per-frame update (Q1) |
| RND-07 | The skybox↔mesh handoff is visually continuous — no pop, jump, or flicker in apparent position, brightness, or color when a star switches between a skybox light-point and a rendered mesh. | Handoff machinery (Q8), magnitude model (Q5), dither integration (Q3) |
</phase_requirements>

---

## Summary

Phase 2 builds a world-fixed, data-driven spherical skybox that renders real sibling-star-system stars as magnitude-ranked, dithered light points on top of the existing floating-origin render pipeline. Research covered seven technical questions flagged as novel or uncertain in STATE.md and CONTEXT.md.

**Sky technique recommendation:** Use Godot 4's `Sky` resource with a custom `ShaderMaterial` (`shader_type sky`). EYEDIR is already in world space — it is the exact same coordinate frame as Godot's world, not camera-relative — so world-fixed direction projection is automatic: `dot(EYEDIR, normalize(star_dir)) > threshold` draws a world-fixed point with no extra math. The Sky renders before 3D geometry and its output is captured by `hint_screen_texture` in the CanvasLayer post-process (dithering.gdshader), satisfying D-27 automatically.

**Critical risk:** Godot 4.6 introduced a regression (issue #115441, #115599) that breaks `texture(RADIANCE, EYEDIR)` sampling in sky shaders — the RADIANCE map is incorrect in the negative-Z world direction in 4.6.x. The fix is targeted for 4.7. **However, this regression does NOT affect the EcoSpace sky technique.** Drawing star points via `dot(EYEDIR, star_dir)` / smoothstep does not touch RADIANCE at all. The regression only matters for physically-based sky shaders that sample the IBL radiance cubemap for sky color. Our sky is fully custom and will not sample RADIANCE.

**Half-resolution sky pass:** Not recommended for Phase 2. The half-res pass is designed for expensive procedural calculations (volumetric clouds). With only 2–3 sibling stars and a per-point dot product loop, the sky shader will be trivially cheap; half-res adds complexity and visual quality cost for zero performance benefit at this star count.

**Primary recommendation:** Implement `SkyboxRenderer.cs` in `namespace Render` as a read-only consumer of `GameWorld` state. Each frame: classify bodies into current-tier (meshes) / next-tier-out (sky) / beyond; compute each sky body's world-direction from the ship's root-relative absolute position; pack N directions + colors + sizes into vec3/vec4 shader uniforms; update `ShaderMaterial` on the `Sky` resource. The sky shader loops through up to N points and draws each as an anti-aliased disc via smoothstep.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Tier classification (mesh vs. sky vs. beyond) | API / Backend (C# `SkyboxRenderer`) | — | Pure data query on `GameWorld.GameObjects`; no rendering involved |
| Sky projection (draw star directions) | Frontend / Shader (`sky.gdshader`) | C# uniform push | EYEDIR is inherently world-space; fragment shader loops over N directions per pixel |
| Magnitude / brightness computation | API / Backend (C# `SkyboxRenderer`) | — | Computed once per body per frame in C#, passed as float uniforms to shader |
| Dither integration | CDN / Post-process (existing `dithering.gdshader`) | — | Sky renders before CanvasLayer; screen_texture captures sky — no extra work needed |
| Bloom integration | CDN / Post-process (existing `WorldEnvironment` glow) | — | Bright sky output values feed the existing glow pipeline automatically |
| Handoff alignment (point ↔ mesh) | API / Backend (C# `SkyboxRenderer`) | `WorldRenderer` | Consumes same `renderPositions` math as `WorldRenderer` to align screen positions |
| Test data extension (sibling stars) | API / Backend (`TestSetup.cs`) | `UniObject.cs` | Authored in `SetupScene()`, `Luminosity` field on `UniObject` |
| Re-tier logic unit tests | API / Backend (pure C# test project) | — | No Godot runtime needed for `Space`-enum classification logic |

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Godot 4.6.2 Sky/ShaderMaterial | built-in | Sky background rendering | Native engine sky path; world-fixed EYEDIR; glow/dither integrate automatically |
| GDShader (`shader_type sky`) | built-in | Per-pixel star point rendering | Only way to run fragment code over the sky background; cannot use spatial shader for sky |

### Supporting (test only)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| gdUnit4.api | 5.0.0 [ASSUMED] | C# unit test framework for Godot | For re-tier logic tests that need no Godot runtime |
| gdUnit4.test.adapter | 3.0.0 [ASSUMED] | VSTest adapter for `dotnet test` | Enables `dotnet test` CLI and IDE test runner |

**Alternative (pure xUnit, no Godot dependency):** Extract the tier-classifier into a plain `static class` in a new `EcoSpace.Tests` class-library `.csproj` with no `GodotSharp` reference, then use xUnit directly. This avoids adding any external dependency to the main Godot project. Recommended if the re-tier logic can be kept free of Godot types (use `int` indices and `UniObject.Space` which has no Godot dependency). [ASSUMED]

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Sky resource + ShaderMaterial | Inverted far-sphere mesh | Sphere approach uses `shader_type spatial` + `render_mode depth_draw_never, cull_front`; requires camera-follow node to keep origin; does NOT get depth-zero treatment automatically; sky resource is the proper Godot path for backgrounds |
| Sky resource + ShaderMaterial | `MeshInstance3D` with `SphereMesh` + BackgroundLayer | Mesh renders in scene space — must chase camera every frame; harder to ensure it stays behind all geometry; no native sky semantics |
| Per-frame uniform push | Texture2D data encode | Texture approach (pack directions into Image pixels, `ImageTexture.Update()`) has ~30ms CPU cost for any substantial image. For 2–3 bodies, uniform push is instant and trivially correct |

**Installation (test project only):**
```bash
# Only if creating a separate test .csproj
dotnet add EcoSpace.Tests.csproj package gdUnit4.api --version 5.0.0
dotnet add EcoSpace.Tests.csproj package gdUnit4.test.adapter --version 3.0.0
```

**Version verification note:** gdUnit4.api 5.0.0 and gdUnit4.test.adapter 3.0.0 were found on NuGet search results [ASSUMED — not verified against official GdUnit4 documentation]. Verify with `dotnet search gdUnit4.api` or NuGet.org before use.

---

## Package Legitimacy Audit

> This phase installs no runtime packages into the Godot project. The optional test framework (gdUnit4Net) would only appear in a separate test `.csproj`, if the team chooses to create one.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| gdUnit4.api | NuGet | ~4 yrs | moderate [ASSUMED] | github.com/godot-gdunit-labs/gdUnit4Net | OK [ASSUMED] | Approved (test project only) |
| gdUnit4.test.adapter | NuGet | ~4 yrs | moderate [ASSUMED] | github.com/godot-gdunit-labs/gdUnit4Net | OK [ASSUMED] | Approved (test project only) |

**Packages removed due to [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

*gdUnit4.api/gdUnit4.test.adapter were found via WebSearch and NuGet listing; tagged [ASSUMED]. Planner must gate install behind a `checkpoint:human-verify` task confirming the package and version before adding to test project.*

---

## Architecture Patterns

### System Architecture Diagram

```
[GameWorld.GameObjects]
         │
         ▼ (read-only, each frame)
[SkyboxRenderer._Process]
         │
         ├─── TierClassifier.Classify(ship.CurrentSpace, gameObjects)
         │         │
         │         ├── current-tier bodies  ──► [WorldRenderer] (existing, meshes)
         │         ├── next-tier-out bodies ──► sky uniform arrays (dirs, colors, sizes)
         │         └── beyond              ──► ignored
         │
         ├─── for each sky body: ComputeAbsoluteDirection(ship, body)
         │         └── ChildPosToParentSpace chain → world-root offset → normalize
         │
         ├─── ApplyMagnitudeModel(luminosity, trueDistanceMeters) → apparent brightness/size
         │
         ├─── ShaderMaterial.SetShaderParameter("star_dirs[N]", ...)
         │    ShaderMaterial.SetShaderParameter("star_colors[N]", ...)
         │    ShaderMaterial.SetShaderParameter("star_sizes[N]", ...)
         │    ShaderMaterial.SetShaderParameter("star_count", N)
         │
         ▼
[Sky ShaderMaterial] (sky.gdshader, shader_type sky)
         │  void sky() { for each i in star_count:
         │    float d = dot(EYEDIR, star_dirs[i]);
         │    float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
         │    COLOR += star_colors[i] * disc; }
         │
         ▼ (sky renders before 3D geometry, before CanvasLayer)
[hint_screen_texture in dithering.gdshader]  ← captures sky + meshes together
         │
         ▼
[WorldEnvironment glow] ← bright sky values bloom automatically
         │
         ▼
[Final frame on screen]
```

### Recommended Project Structure

```
Shaders/
├── dithering.gdshader       # existing — unchanged
├── body_lit.gdshader        # existing — unchanged
└── skybox.gdshader          # NEW: shader_type sky, star point loop

Scripts/
├── Render/
│   ├── WorldRenderer.cs     # existing — add renderPositions accessor for handoff
│   └── SkyboxRenderer.cs    # NEW: namespace Render, reads GameWorld, pushes sky uniforms
├── UniObject.cs             # add Luminosity field (D-26)
└── TestSetup.cs             # add sibling star systems + Luminosity values (D-23)

# Optional, only if unit tests are created:
EcoSpace.Tests/
├── EcoSpace.Tests.csproj    # classlib, no GodotSharp ref, xUnit or gdUnit4.api
└── TierClassifierTests.cs   # pure C# tests for Classify() logic
```

### Pattern 1: World-Fixed Direction Projection via EYEDIR

**What:** EYEDIR in Godot 4's sky shader is already in world space (same frame as Godot's world coordinate system). It is NOT camera-relative. Each pixel's EYEDIR points from the camera outward in a direction that does not change when the camera rotates — the sky thus stays world-fixed with no extra math.

**When to use:** Always — this is the fundamental mechanism for the RND-05 "never drifts with camera rotation" requirement.

**Confirmation:** [CITED: kelvinvanhoorn.com/tutorials/godot_skybox_shader] — "EYEDIR provides the view direction in world-space... used directly to calculate angles with world-space sun and moon directions: `float sun_view_dot = dot(sun_dir, view_dir)` where all directions share the same coordinate space." Also confirmed by [CITED: docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html] — EYEDIR described as "Normalized direction of the current pixel" (world space, confirmed by community usage pattern).

**Example (skybox.gdshader):**
```glsl
// Source: pattern derived from kelvinvanhoorn.com + godotengine.org/article/custom-sky-shaders-godot-4-0/
shader_type sky;

uniform int   star_count    = 0;
uniform vec3  star_dirs[8];       // world-space unit directions to each sky body
uniform vec4  star_colors[8];     // rgb = BaseColor, a = apparent brightness (>1 for bloom)
uniform float star_sizes[8];      // angular radius in smoothstep space (0..1 scale)

void sky() {
    vec3 col = vec3(0.0);
    for (int i = 0; i < star_count; i++) {
        float d    = dot(EYEDIR, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * star_colors[i].a * disc;
    }
    COLOR = col;
}
```

**C# uniform push (SkyboxRenderer.cs, per frame):**
```csharp
// Source: Godot 4.6.2 C# API — ShaderMaterial.SetShaderParameter
// Confirmed working: vec3[] from C# as Vector3[] (vec4[] array had issues pre-4.2, resolved)
_skyMaterial.SetShaderParameter("star_count", skyBodies.Count);
_skyMaterial.SetShaderParameter("star_dirs",   _dirs);    // Vector3[]
_skyMaterial.SetShaderParameter("star_colors", _colors);  // Color[] (maps to vec4)
_skyMaterial.SetShaderParameter("star_sizes",  _sizes);   // float[]
```

**Pitfall:** Uniform arrays in GDShader must be declared with a fixed maximum size (e.g. `vec3 star_dirs[8]`). You cannot declare a runtime-sized array. Set `star_count` as an int uniform and loop `for (int i = 0; i < star_count; i++)`. Maximum array size should be the maximum number of sky bodies (8 is generous for Phase 2 with 2–3 siblings). [CITED: github.com/godotengine/godot/issues/77970 — closed/fixed]

### Pattern 2: Computing World-Fixed Absolute Direction from Ship to Sky Body

**What:** The skybox needs to know the world-space direction from the ship to each next-tier-out body. Since bodies live in different coordinate spaces (sibling stars are in Galaxy space, ship is in Star/Planet space), you cannot directly diff `LocalPos` values. Walk the hierarchy via `ChildPosToParentSpace` to convert both positions to the same root-relative frame.

**When to use:** Once per sky body per frame, in C#.

**Example (SkyboxRenderer.cs):**
```csharp
// Source: derived from existing GameWorld.ChildPosToParentSpace pattern
// IMPORTANT: Read-only — MUST NOT mutate any UniObject or call TranslatePos.

private static Vector3 ComputeSkyBodyDirection(UniObject skyBody, UniObject ship, List<UniObject> objs)
{
    // Accumulate skyBody's absolute position (in root-space units) by walking up.
    // Using Double3 throughout to avoid precision loss at interstellar distances.
    Double3 skyBodyRoot = AbsolutePositionInRoot(skyBody, objs);
    Double3 shipRoot    = AbsolutePositionInRoot(ship, objs);
    Double3 delta       = skyBodyRoot - shipRoot;
    double  len         = delta.Length();
    if (len < 1e-30) return Vector3.Up; // safety: coincident
    Double3 dir = delta * (1.0 / len);
    return new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
}

private static Double3 AbsolutePositionInRoot(UniObject obj, List<UniObject> objs)
{
    // Walk up parent chain, converting each LocalPos to the parent's scale.
    // Mirrors GameWorld.ChildPosToParentSpace logic — read-only.
    Double3 pos   = obj.LocalPos.ToDouble3();
    double  scale = obj.LocalPos.Scale;
    int     pIdx  = obj.ParentIndex;

    while ((uint)pIdx < (uint)objs.Count && objs[pIdx] != null)
    {
        var parent = objs[pIdx];
        // Convert pos from child scale to parent scale
        Double3 inParent = pos * (scale / parent.LocalPos.Scale);
        pos   = parent.LocalPos.ToDouble3() + inParent;
        scale = parent.LocalPos.Scale;
        pIdx  = parent.ParentIndex;
    }
    return pos;
}
```

**Key constraint:** `AbsolutePositionInRoot` traverses the hierarchy in a read-only fashion. At interstellar distances (1e16 m in Universe scale), `double` precision is ~1.8e-7 m relative error — adequate for a direction vector (we only need the direction, not exact position). [ASSUMED — based on double precision IEEE 754 analysis]

### Pattern 3: Magnitude Model (D-17/D-18/D-19)

**What:** Compute apparent brightness from luminosity (absolute) and true distance (meters). Apply a minimum floor so sub-threshold bodies are clamped up rather than invisible.

**Formula (tunable curve):**
```csharp
// Source: inverse-square law, standard astrophysics [CITED: Wikipedia "Apparent magnitude"]
// apparentBrightness ∝ Luminosity / (distanceMeters^2)
//
// Concrete curve (all parameters are planner-tunable):
//   float raw  = (float)(body.Luminosity / (dist * dist));
//   float norm = raw * LuminosityScale;        // Export: tune so Sun at 1 AU ≈ 1.0
//   float clamped = Mathf.Max(norm, MinBrightFloor); // Export: e.g. 0.05 → always visible
//   // Sizes: angular disc tied to brightness — bigger = brighter star
//   float size = Mathf.Clamp(clamped * SizePerBrightness, MinStarSize, MaxStarSize);
//   // Color: body.BaseColor * clamped → drives both disc color and bloom (D-20)
//   Color apparent = body.BaseColor * clamped;  // a channel carries brightness for bloom

// Starting values (planner tunes):
//   Luminosity unit: solar luminosity (L_sun = 3.828e26 W)
//   Interstellar dist: ~4e16 m (4 light-years, Alpha Centauri-like)
//   At L=1.0, d=4e16: raw = 3.828e26 / (1.6e33) ≈ 2.4e-7 — scale factor ~4e6 to get 1.0
//   MinBrightFloor: 0.1 → guarantees at least 1 bright dithered pixel (D-19)
```

**Luminosity field on UniObject (D-26):**
```csharp
// In UniObject.cs — add after RadiusMeters:
/// <summary>
/// Absolute luminosity in solar luminosity units (L_sun = 3.828e26 W).
/// Drives the magnitude model in SkyboxRenderer (D-26).
/// Default 1.0 = solar luminosity.
/// </summary>
public double Luminosity = 1.0;
```

### Pattern 4: Re-Tier Classification Logic

**What:** Given the ship's `CurrentSpace`, decide whether each body is "current tier mesh" or "next-tier-out sky body" or "beyond/irrelevant".

**Rule:**
```csharp
// Source: derived from UniObject.Space enum semantics and RND-05/D-22
// "next tier out" = bodies whose CurrentSpace is ParentSpace(ship.CurrentSpace)
// "current tier" = bodies in the same space as the ship's PARENT (rendered by WorldRenderer)
// "beyond" = bodies more than one tier out

static SkyTier ClassifyBody(UniObject body, UniObject ship, List<UniObject> gameObjects)
{
    // Ship's direct parent space = current render tier
    UniObject.Space currentTier = ship.CurrentSpace;
    // Next tier out = one level above the ship's space in the hierarchy
    UniObject.Space nextTierOut = UniObject.ParentSpace(currentTier);

    // Skip ship itself
    if (body.Index == ship.Index) return SkyTier.Skip;
    // Skip root (no meaningful position)
    if (body.ParentIndex < 0 && body.CurrentSpace == UniObject.Space.Root) return SkyTier.Skip;

    // Bodies in the same space as the ship's parent = current render tier (WorldRenderer owns these)
    if (body.CurrentSpace == currentTier) return SkyTier.CurrentTierMesh;

    // Bodies one tier out = skybox points
    if (body.CurrentSpace == nextTierOut) return SkyTier.NextTierSkybox;

    // Everything else = beyond scope
    return SkyTier.Beyond;
}

enum SkyTier { Skip, CurrentTierMesh, NextTierSkybox, Beyond }
```

**Concrete example (ship in Planet space):**
- Planet space → current tier; Star space → next tier out
- Sibling planets (Star space, same parent) → `CurrentTierMesh` (WorldRenderer)
- Sibling star systems (Galaxy space children) → `NextTierSkybox`
- The galaxy itself (Universe space) → `Beyond`

**Note on existing WorldRenderer classification:** WorldRenderer currently renders bodies in `ship.ParentIndex`'s children (sibling loop) + the parent itself. The `SkyboxRenderer` complements this by rendering the GALAXY-level siblings (other star systems' stars). Both renderers classify independently via the `Space` enum — no shared state mutation.

### Pattern 5: Handoff Machinery (RND-07 Baseline)

**What:** When the visible Star↔Galaxy re-tier happens (Phase 3), the sky point and the newly-spawned mesh must occupy identical screen positions. Phase 2 builds the infrastructure:

1. `WorldRenderer` exposes its computed `renderPositions` dictionary (or a `GetRenderPosition(int bodyIdx)` accessor) so `SkyboxRenderer` can read the same render-space Vector3 for a body currently rendered as a mesh.
2. `SkyboxRenderer` caches the last computed sky direction for each sky body in a `Dictionary<int, Vector3> _skyDirs`.
3. On tier transition (`GameWorld` SOI events), both dictionaries are compared to verify position alignment before promotion is committed (Phase 3 validation logic; Phase 2 builds the data structures).

**Color match:** Both WorldRenderer (mesh `BaseColor`) and SkyboxRenderer (sky `BaseColor`) read `body.BaseColor` — they are identical by construction (D-18). Brightness match: `body.Luminosity` drives both the star mesh `EmissionEnergyMultiplier` and the sky point brightness — same data source.

### Anti-Patterns to Avoid

- **Sampling `RADIANCE` in the sky shader:** Broken in Godot 4.6 (issue #115441, regression in radiance octahedral map). The fix is targeted for 4.7. DO NOT sample RADIANCE; the EcoSpace sky computes color directly from uniform data — this is completely fine.
- **Updating sky uniforms only on events:** The sky must update every frame (ship rotates, sibling star directions are static but brightness depends on ship position which changes). Push uniforms every `_Process` call — radiance cubemap update cost is trivial for a non-TIME shader.
- **Using `shader_type spatial` with an inverted sphere for the sky background:** A spatial shader mesh must chase the camera every frame and does not integrate with `WorldEnvironment` background semantics. The `Sky` resource is the correct path.
- **Half-resolution sky pass for Phase 2:** The half-res pass is for expensive procedural content (volumetric clouds). A 2–3 body loop with dot products is trivially cheap; adding half-res adds blurriness at edges and shader complexity for no gain.
- **World-space position jitter on sky directions:** At galaxy scale (1e16 m/unit), converting through `float` loses all precision. Keep the direction math in `double` (`Double3`) until the final `Vector3` cast. The direction is a normalized unit vector so even the final float cast is safe (only ~7 decimal digits needed for normalized components).
- **Mutating GameWorld state from SkyboxRenderer:** `SkyboxRenderer` MUST be a read-only consumer. It reads `GameObjects`, computes directions, and calls `ShaderMaterial.SetShaderParameter`. It MUST NOT call `TranslatePos`, modify `LocalPos`, or touch `ChildIndices`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| World-fixed sky background rendering | Custom camera-following sphere mesh | Godot `Sky` resource + `shader_type sky` | EYEDIR is already world-space; Sky integrates with background, glow, and screen_texture |
| Dither post-process integration | Second dither pass or custom skybox canvas layer | Existing `dithering.gdshader` CanvasLayer (already in scene) | Sky renders before the CanvasLayer; `hint_screen_texture` captures sky + meshes in a single pass |
| Bloom/glow for sky points | Separate glow or additive blend layer | Existing `WorldEnvironment` glow (already enabled) | Sky COLOR output values >1 automatically feed the bloom pipeline |
| Star direction calculation at 1:1 scale | float-precision direction math | `Double3` hierarchy walk (existing pattern from `ComputeStarRenderPosFromHierarchy`) | Interstellar distances in float lose all meaningful bits |
| C# test runner | Custom test harness | xUnit in a separate `classlib` `.csproj` OR gdUnit4.api with pure-C# mode | Re-tier `Classify()` has no Godot dependency; plain `dotnet test` works |

**Key insight:** The most dangerous custom-build temptation is the dither integration — it is already solved for free because the Sky resource renders in the 3D scene pass before the CanvasLayer's `hint_screen_texture` captures the full composed frame. Adding a second full-screen dither pass for the sky would double the dither effect on sky pixels.

---

## Common Pitfalls

### Pitfall 1: Assuming EYEDIR is camera-relative (it is world-space)

**What goes wrong:** Developer treats EYEDIR like a camera-view direction and tries to undo camera rotation. The stars rotate with the ship.
**Why it happens:** In spatial shaders, NORMAL/VIEW are camera-relative. Sky shaders are different — EYEDIR is already world-space.
**How to avoid:** Use `dot(EYEDIR, normalize(body_world_dir))` directly. No rotation matrix needed. [CITED: kelvinvanhoorn.com/tutorials/godot_skybox_shader]
**Warning signs:** Star disc visible correctly when facing north but wrong when facing south.

### Pitfall 2: Sampling RADIANCE in the sky shader (Godot 4.6 regression)

**What goes wrong:** `texture(RADIANCE, EYEDIR)` produces incorrect colors (especially in negative-Z direction) in Godot 4.6.x. Fix is targeted for 4.7 (issue #115441).
**Why it happens:** A regression in the radiance cubemap's octahedral encoding (PRs #107902, #114773) broke RADIANCE sampling in sky shaders.
**How to avoid:** Do not sample RADIANCE in the EcoSpace sky shader. Our sky computes all color from uniform data — this is by design and bypasses the bug entirely.
**Warning signs:** Sky looks wrong on certain angles, especially facing -Z. Check if any RADIANCE sampling accidentally crept in.

### Pitfall 3: Uniform array size limit — GLSL requires fixed-size arrays

**What goes wrong:** `uniform vec3 star_dirs[];` (unsized) fails to compile. `vec4[]` array pass from C# had issues in early Godot 4.x (pre-4.2).
**Why it happens:** GLSL requires statically-sized array declarations. C# `Vector4[]` → `uniform vec4[]` had a bug that was fixed (issue #77970 closed).
**How to avoid:** Declare `uniform vec3 star_dirs[MAX_STARS]` with a constant `MAX_STARS = 8`. Use `uniform int star_count` to control the loop. Pass `Vector3[]` from C#. Use `Color[]` (not `Vector4[]`) for color uniforms if targeting `vec4 : source_color`. [CITED: github.com/godotengine/godot/issues/77970]
**Warning signs:** Shader compilation errors; uniform not updating silently.

### Pitfall 4: Radiance cubemap re-triggers on every uniform push (performance)

**What goes wrong:** Updating any sky shader uniform triggers a radiance cubemap update (up to 6 sub-passes). For 2–3 bodies this is fine, but for 50+ bodies it could cause a per-frame cubemap rebuild overhead.
**Why it happens:** By design — Godot invalidates the radiance cache when uniforms change because the sky might affect IBL.
**How to avoid:** For Phase 2 (2–3 bodies), this is a non-issue — radiance cubemap update cost is tiny. Monitor with Godot profiler if star count grows in later phases. Optimization path: `render_mode use_half_res_pass` as a staging buffer could help, but not needed now.
**Warning signs:** Frame time spikes visible in profiler under "radiance update."

### Pitfall 5: Double-precision loss when computing sky directions at interstellar scale

**What goes wrong:** Galaxy-space body positions at ~1e16 m/unit. Converting to `float` for direction math produces zero vector (all precision lost). Star appears at wrong angle or is invisible.
**Why it happens:** `float` has ~7 significant decimal digits. At 1e16 the mantissa bits represent meter-range precision — relative star-to-star offsets within the galaxy (1e14 m) may be lost.
**How to avoid:** Perform all direction arithmetic in `Double3` / `double`. Convert to `Vector3` only after normalizing the direction. [ASSUMED — based on IEEE 754 float range analysis]
**Warning signs:** Sky body appears at `(0,0,0)` direction or at `Vector3.Up` fallback.

### Pitfall 6: Sky points getting dithered twice

**What goes wrong:** Someone adds a second dithering pass specifically for the sky. The existing CanvasLayer `hint_screen_texture` already captured the sky; the second pass dithers an already-dithered image.
**Why it happens:** Misunderstanding that D-27 is "automatically satisfied" — the sky renders before the CanvasLayer post-process.
**How to avoid:** Do not add any sky-specific dither pass. The single `dithering.gdshader` on the CanvasLayer ColorRect handles everything.
**Warning signs:** Excessive pixelation or color banding on sky compared to meshes.

### Pitfall 7: Forgetting to add `Luminosity` to `AddGameObject` call chain

**What goes wrong:** Sibling stars added in `TestSetup.SetupScene()` have `Luminosity = 0.0` (default if field is not explicitly set). All sky points have zero brightness — sky appears empty.
**Why it happens:** `UniObject` is initialized with field initializers; adding a field with a default of 0 makes all existing objects have zero luminosity until explicitly authored.
**How to avoid:** Set `Luminosity` default to 1.0 (solar luminosity) in `UniObject`. Explicit values in `TestSetup` override; existing bodies get solar default. [ASSUMED — planner should decide exact default]

---

## Code Examples

### Sky Shader — Complete Minimal Pattern

```glsl
// Shaders/skybox.gdshader
// Source: EYEDIR world-space property — godotengine.org/article/custom-sky-shaders-godot-4-0/
//         Star disc pattern — kelvinvanhoorn.com/tutorials/godot_skybox_shader/
shader_type sky;

const int MAX_STARS = 8;

uniform int   star_count               = 0;
uniform vec3  star_dirs[MAX_STARS];        // world-space unit direction from ship to sky body
uniform vec4  star_colors[MAX_STARS];      // .rgb = BaseColor, .a = apparent brightness (>1 enables bloom)
uniform float star_sizes[MAX_STARS];       // disc half-angle in smoothstep units (e.g. 0.0005 to 0.005)

void sky() {
    // AT_CUBEMAP_PASS: radiance cubemap is being baked.
    // We still draw the star points so IBL sees them, but skip
    // the RADIANCE sample to avoid the Godot 4.6 regression (#115441).
    vec3 col = vec3(0.0);
    for (int i = 0; i < star_count; i++) {
        float d    = dot(EYEDIR, star_dirs[i]);
        // smoothstep: creates antialiased circular disc.
        // Inner edge = 1.0 - star_sizes[i] (bright core), outer = 1.0 (hard sky edge).
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }
    COLOR = col;  // values > 1.0 feed WorldEnvironment glow (D-20)
}
```

### SkyboxRenderer.cs — Per-Frame Uniform Push Pattern

```csharp
// Scripts/Render/SkyboxRenderer.cs
// Source: WorldRenderer.cs pattern — namespace Render, read-only consumer
using Godot;
using System.Collections.Generic;

namespace Render
{
    public partial class SkyboxRenderer : Node
    {
        [Export] public NodePath WorldPath { get; set; }
        [Export] public float LuminosityScale { get; set; } = 4e6f;   // tune so L_sun at 4 ly ≈ 1.0
        [Export] public float MinBrightFloor  { get; set; } = 0.1f;   // D-19 minimum floor
        [Export] public float SizePerBright   { get; set; } = 0.002f; // angular disc scale
        [Export] public float MinStarSize     { get; set; } = 0.0003f;
        [Export] public float MaxStarSize     { get; set; } = 0.005f;

        private TestSetup      _world;
        private ShaderMaterial _skyMat;

        private const int MaxStars = 8;
        private Vector3[] _dirs   = new Vector3[MaxStars];
        private Color[]   _colors = new Color[MaxStars];
        private float[]   _sizes  = new float[MaxStars];

        public override void _Ready()
        {
            _world = /* resolve via WorldPath, same pattern as WorldRenderer */;
            // Obtain the Sky resource's ShaderMaterial from the Camera3D's Environment.
            var env   = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
            var sky   = env?.Environment?.Sky;
            _skyMat   = sky?.SkyMaterial as ShaderMaterial;
        }

        public override void _Process(double delta)
        {
            if (_world == null || _skyMat == null) return;
            SyncSkyPoints();
        }

        private void SyncSkyPoints()
        {
            var objs     = _world.GameObjects;
            int shipIdx  = _world.ShipIndex;
            var ship     = (uint)shipIdx < (uint)objs.Count ? objs[shipIdx] : null;
            if (ship == null) return;

            int count = 0;
            for (int i = 0; i < objs.Count && count < MaxStars; i++)
            {
                var body = objs[i];
                if (body == null || body.Index == shipIdx) continue;

                if (ClassifyBody(body, ship) != SkyTier.NextTierSkybox) continue;

                // Direction: walk hierarchy to get world-space direction
                _dirs[count]   = ComputeSkyBodyDirection(body, ship, objs);

                // Magnitude: inverse square law + floor (D-17/D-19)
                double dist       = ComputeTrueDistanceMeters(body, ship, objs);
                float  raw        = dist > 0 ? (float)(body.Luminosity / (dist * dist)) : 1f;
                float  brightness = Mathf.Max(raw * LuminosityScale, MinBrightFloor);
                float  size       = Mathf.Clamp(brightness * SizePerBright, MinStarSize, MaxStarSize);

                // Color from BaseColor (D-18) — alpha carries brightness for bloom (D-20)
                _colors[count] = new Color(body.BaseColor.R, body.BaseColor.G,
                                           body.BaseColor.B, brightness);
                _sizes[count]  = size;
                count++;
            }

            _skyMat.SetShaderParameter("star_count", count);
            if (count > 0)
            {
                _skyMat.SetShaderParameter("star_dirs",   _dirs);
                _skyMat.SetShaderParameter("star_colors", _colors);
                _skyMat.SetShaderParameter("star_sizes",  _sizes);
            }
        }
    }
}
```

### Tier Classifier — Pure C# (Unit-Testable)

```csharp
// Scripts/TierClassifier.cs (no Godot dependency — can live in a plain classlib)
// Source: UniObject.Space enum semantics + RND-05 tiered model
public enum SkyTier { Skip, CurrentTierMesh, NextTierSkybox, Beyond }

public static class TierClassifier
{
    public static SkyTier Classify(UniObject body, UniObject ship)
    {
        if (body == null || ship == null) return SkyTier.Skip;
        if (body.Index == ship.Index)     return SkyTier.Skip;
        if (body.CurrentSpace == UniObject.Space.Root) return SkyTier.Skip;

        // WorldRenderer renders all bodies whose CurrentSpace == ship.CurrentSpace
        // (i.e. they share the same parent frame as the ship).
        if (body.CurrentSpace == ship.CurrentSpace)
            return SkyTier.CurrentTierMesh;

        // Next tier out = the parent space of the ship's current space.
        // e.g. ship in Planet/Star → next tier is Star/Galaxy
        UniObject.Space nextOut = UniObject.ParentSpace(ship.CurrentSpace);
        if (body.CurrentSpace == nextOut)
            return SkyTier.NextTierSkybox;

        return SkyTier.Beyond;
    }
}
```

### Sibling Star Test Data (D-23) — TestSetup Extension

```csharp
// In TestSetup.SetupScene() — add after existing star/planet setup
// Source: real interstellar distances; D-23 requirement for 2-3 siblings

// Alpha Centauri-like (4.2 ly = 3.97e16 m from our star in Galaxy space)
// Galaxy scale: 1 unit = 10000 m → 3.97e16 m = 3.97e12 Galaxy units
private const double Sibling1_X = 3.97e12;
private const double Sibling1_Luminosity = 1.519;  // ~Alpha Cen A (solar units)
private static readonly Color Sibling1_Color = new Color(1.0f, 0.92f, 0.70f); // G-type warm white

// Barnard's Star-like (5.96 ly = 5.63e16 m, dim red dwarf)
private const double Sibling2_X = 5.63e12;
private const double Sibling2_Luminosity = 0.0035; // M-dwarf, very dim
private static readonly Color Sibling2_Color = new Color(1.0f, 0.30f, 0.15f); // M-type red

// Sirius-like (8.6 ly = 8.13e16 m, very bright blue-white)
private const double Sibling3_X = -6.0e12;
private const double Sibling3_Z =  5.6e12;
private const double Sibling3_Luminosity = 25.4;   // very luminous, bright in sky
private static readonly Color Sibling3_Color = new Color(0.70f, 0.85f, 1.0f); // A-type blue-white

// In SetupScene:
int sib1 = AddGameObject(_galaxy, new Double3(Sibling1_X, 0, 0), StarSOI);
GameObjects[sib1].Name        = "ALPHA CEN";
GameObjects[sib1].BaseColor   = Sibling1_Color;
GameObjects[sib1].RadiusMeters = Star_RadiusMeters;  // placeholder radius
GameObjects[sib1].Luminosity  = Sibling1_Luminosity;

// (repeat for sib2, sib3)
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| PanoramaSkyMaterial (HDRI) | Custom `shader_type sky` ShaderMaterial | Godot 4.0+ | Full programmatic control; data-driven from C# |
| Light nodes driving sky shading | EYEDIR-based world-space direction math | Godot 4.0+ | No camera dependency for world-fixed sky |
| Inverted sphere mesh skybox (Godot 3 era) | Sky resource + ShaderMaterial | Godot 4.0+ | Native background semantics; glow integration |
| Uniform vec4 array C# bug | Fixed in Godot 4.x (issue #77970) | ~4.1.x | C# `Color[]` → `uniform vec4[]` works |

**Deprecated/outdated:**
- `set_shader_param` (Godot 3 GDScript API): replaced by `SetShaderParameter` in Godot 4.
- `RADIANCE` sampling in sky shaders: **broken in Godot 4.6** (regression #115441, fix in 4.7). Do not use in 4.6.2.
- `background_mode = 1` (flat black) in Main.tscn: this is what currently exists; changing to `background_mode = 2` (sky) is the Phase 2 activation step.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | gdUnit4.api 5.0.0 / gdUnit4.test.adapter 3.0.0 are current stable versions | Standard Stack | Wrong version causes build failure in test project; check NuGet before use |
| A2 | gdUnit4.api / gdUnit4.test.adapter are legitimate packages (GitHub: godot-gdunit-labs) | Package Audit | Low risk — confirmed GitHub org exists; validate via NuGet before install |
| A3 | `double`-precision hierarchy walk is adequate for galaxy-scale direction vectors | Pattern 2 code | If wrong: directions computed at galaxy scale could have visible angular error; mitigated by using `Double3` throughout and only casting final normalized float |
| A4 | `Luminosity = 1.0` is a safe field default on `UniObject` | Pattern 3 code | If existing bodies pick up 1.0 and SkyboxRenderer classifies them as sky bodies, they'd appear as points with solar luminosity brightness — but they're classified as `CurrentTierMesh` so they never reach the sky path |
| A5 | `hint_screen_texture` in the existing CanvasLayer dithering shader captures sky output (confirms D-27 is automatic) | Dither integration | If wrong: sky bypasses dither — would need explicit sky-in-viewport workaround; verify in first smoke test |
| A6 | Sibling star data (distances, luminosities) are real-ish but approximate | Code Examples | Stars will appear at wrong visual magnitudes — planner tunes `LuminosityScale` / `MinBrightFloor` exports anyway |
| A7 | C# `Color[]` maps correctly to `uniform vec4 : source_color` in Godot 4.6.2 | Pattern 1 | If wrong: color array not accepted; fallback = use `vec3[]` for directions + separate float for brightness |

**If this table is empty:** All claims in this research were verified or cited — not the case here; A1–A7 require validation during Wave 0.

---

## Open Questions

1. **Does `hint_screen_texture` in the CanvasLayer ColorRect reliably capture sky output in Godot 4.6.2 Forward+ / DX12?**
   - What we know: Sky renders before 3D geometry and before CanvasLayer; `hint_screen_texture` is documented to capture the previously-rendered frame content.
   - What's unclear: Whether any DX12/Forward+ pipeline ordering issue causes the sky to be captured only from the previous frame (1-frame lag), which would cause a 1-frame dither delay on sky — visible as a brief un-dithered flash on scene load.
   - Recommendation: Verify in Wave 0 by enabling the sky and checking that dithering visibly applies to the sky background. If 1-frame lag occurs, it is cosmetically acceptable for Phase 2.

2. **Does the Godot 4.6 sky shader regression (#115599, #115441) affect *any* use of `shader_type sky`?**
   - What we know: The regression specifically breaks `texture(RADIANCE, EYEDIR)` sampling. Our sky shader does NOT sample RADIANCE — it draws points from uniform directions only.
   - What's unclear: Whether the regression also corrupts the EYEDIR built-in itself or only RADIANCE sampling.
   - Recommendation: Smoke-test first: add a plain `shader_type sky` that sets `COLOR = vec3(EYEDIR.x * 0.5 + 0.5, 0, 0)` (red = +X, black = -X). If the color hemisphere is correct, EYEDIR is unaffected by the regression.

3. **Uniform array acceptance: `Color[]` → `uniform vec4[] : source_color` in 4.6.2 Mono?**
   - What we know: vec4 array bugs were fixed in earlier 4.x (issue #77970). C# `Color[]` is the recommended mapping for `uniform vec4 : source_color`.
   - What's unclear: Whether 4.6.2 Mono specifically has any remaining quirks (issue #91056 noted `source_color` tag mismatch).
   - Recommendation: In Wave 0, test with a single `uniform vec4 star_colors[1]` and `SetShaderParameter("star_colors", new Color[]{Colors.Red})`. If rejected, fall back to separate float arrays for R, G, B, A.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Godot 4.6.2 Mono | Sky shader + C# scripting | ✓ | 4.6.2 | — |
| .NET 8.0 SDK | C# compilation | ✓ (existing project builds) | 8.0 | — |
| DX12 GPU | Forward+ renderer (Windows) | ✓ (project already runs) | — | — |
| gdUnit4.api NuGet | Re-tier unit tests (optional) | Unknown | 5.0.0 [ASSUMED] | Use separate xUnit classlib instead |

**Missing dependencies with no fallback:** None (all sky work is pure Godot built-ins).
**Missing dependencies with fallback:** gdUnit4Net (fallback: plain xUnit/NUnit in a separate classlib with no Godot dependency).

---

## Validation Architecture

> Nyquist validation is disabled for this run (per research instructions). This section documents the testing strategy for the planner's use.

### Test Strategy (no automated framework required by nyquist)

**Pure-logic unit tests (Wave 0 — no Godot runtime needed):**
- `TierClassifier.Classify()` — test all combinations of `ship.CurrentSpace` (Star, Planet, Galaxy) × body spaces (Root, Universe, Galaxy, Star, Planet) → expected `SkyTier`. Fully deterministic, no scene needed.
- `MagnitudeModel` — test that `MinBrightFloor` is always returned when luminosity/distance makes raw brightness < floor. Test inverse-square attenuation at known distances.

**Smoke tests (manual, Wave 0):**
- Sky background visible (non-black) when `background_mode = 2` (sky) set.
- EYEDIR direction test: `COLOR = vec3(EYEDIR.x * 0.5 + 0.5, 0, 0)` → correct hemisphere colors.
- Star point visible at correct world-space direction (place a `MeshInstance3D` in the direction and verify alignment).
- Dither applies to sky: toggle dithering on/off in PostProcessRenderer inspector and verify sky pixelation changes.
- Bloom: a bright sky point (brightness > 1) should produce visible glow halo.

**Integration test (manual, Phase 2 final):**
- Fly to Star space: 3 sibling star points visible, correctly ranked by apparent brightness (Sirius-analog brightest, Barnard's analog dimmest).
- Rotate ship: sky points do not drift — they stay world-fixed.
- Enter Planet space: sky points remain correct (ship.CurrentSpace changes, re-tier classification correct).
- Handoff alignment check: Position `WorldRenderer`'s star mesh render position against the `SkyboxRenderer` sky body direction — verify they overlap within 1 pixel.

---

## Security Domain

> Skipped — no auth, no network, no user input beyond game controls. `security_enforcement` not applicable to a game rendering phase.

---

## Sources

### Primary (MEDIUM confidence — web-verified against official Godot docs)

- [godotengine.org — Custom sky shaders in Godot 4.0](https://godotengine.org/article/custom-sky-shaders-godot-4-0/) — EYEDIR world-space property, sky shader architecture, subpasses
- [docs.godotengine.org — Sky shaders reference](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html) — full built-in variable list, AT_CUBEMAP_PASS, radiance update triggers
- [kelvinvanhoorn.com — Godot skybox tutorial](https://kelvinvanhoorn.com/tutorials/godot_skybox_shader/) — EYEDIR world-space confirmation, glow/bloom via WorldEnvironment

### Secondary (MEDIUM confidence — verified GitHub issues, official tracker)

- [github.com/godotengine/godot/issues/115441](https://github.com/godotengine/godot/issues/115441) — RADIANCE sampling broken in Godot 4.6; fix targeted 4.7
- [github.com/godotengine/godot/issues/115599](https://github.com/godotengine/godot/issues/115599) — Major rendering regression Godot 4.6 (sky shaders, VoxelGI, SDFGI)
- [github.com/godotengine/godot/issues/77970](https://github.com/godotengine/godot/issues/77970) — vec4 array sky shader bug (closed/fixed)
- [github.com/godotengine/godot-proposals/issues/12442](https://github.com/godotengine/godot-proposals/issues/12442) — Camera forward direction proposal; confirms EYEDIR is world-space and sending rotation via script is the workaround for multi-camera setups
- [github.com/godot-gdunit-labs/gdUnit4Net](https://github.com/godot-gdunit-labs/gdUnit4Net) — C# unit testing framework, pure-C# mode, dotnet test support

### Tertiary (LOW confidence — WebSearch, training knowledge, community sources)

- [godotshaders.com — Stylized Sky](https://godotshaders.com/shader/stylized-sky/) — smoothstep disc pattern for sun/moon (community shader)
- [nuget.org — gdUnit4.api](https://www.nuget.org/packages/gdUnit4.api/) — version 5.0.0 (NuGet listing)
- Wikipedia — Apparent magnitude / inverse-square law brightness formula

---

## Metadata

**Confidence breakdown:**
- Sky technique (EYEDIR world-space, Sky resource approach): MEDIUM — confirmed via official Godot docs and multiple community tutorials
- Godot 4.6 regression (RADIANCE bug): MEDIUM — confirmed via official GitHub issues, still open as of research date
- Dither integration (D-27 automatic): LOW-MEDIUM — theoretically sound (sky renders before CanvasLayer) but requires Wave 0 smoke-test to confirm `hint_screen_texture` captures sky in DX12/Forward+
- Uniform array (vec3[], Color[]): MEDIUM — known bugs closed, but Mono-specific behavior in 4.6.2 needs Wave 0 verification
- Re-tier logic: HIGH — pure derivation from existing `UniObject.Space` enum semantics, no novel mechanism
- Testing (gdUnit4Net): LOW — found via WebSearch, not verified from official documentation
- Magnitude model: MEDIUM — inverse-square law is physics; tuning constants are planner-decided

**Research date:** 2026-06-14
**Valid until:** 2026-07-14 (30 days for stable Godot APIs; the 4.6 regression status may change if 4.7 releases)
