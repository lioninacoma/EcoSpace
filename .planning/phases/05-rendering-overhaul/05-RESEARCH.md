# Phase 5: Rendering Overhaul - Research

**Researched:** 2026-06-19 (revised — overwrites stale pre-05-02 document)
**Domain:** Godot 4.6 Forward+ rendering pipeline, sky shaders, spatial post-process composition, LOD blending
**Confidence:** HIGH (grounded in live codebase code read + Godot 4 rendering model)

> **This document REPLACES the previous 05-RESEARCH.md.** The prior version described an
> architecture (unified depth-aware post-process pass replacing the sky shader) that the 05-02
> play-test **disproved**. All content below is grounded in the REVISED architecture from
> 05-CONTEXT.md (post-revision). Do NOT use the old file; it has been overwritten.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** Architecturally a rewrite (one unified classify→describe→draw pipeline), but the hard-won primitives are preserved/extended: `UniMath` LCA-relative walks, the floating-origin per-frame sync loop, `StarRendering`, `TierClassifier`, `body_lit.gdshader`.
- **D-02 (DELIVERED, Plan 1):** One descriptor (`LuminousBodyDescriptor`) shared by all drawers. Produced by `LuminousDescriptorBuilder` (process_priority=-10). Never re-classify per drawer.
- **D-03 (RATIONALE UPDATED):** Crossfades are distance-driven and continuous. Occlusion handled per-layer: sky shader renders distant bodies behind all geometry automatically; near-star glow is screen-space; meshes are depth-sorted by the mesh renderer.
- **D-04:** Acceptance bar = "nothing gets worse," judged subjectively per tier at each plan's play-test gate. No pixel-matching.
- **D-05 (SCOPE NARROWED):** Near-star glow/halo pass composes in HDR/linear BEFORE dither quantizes the frame. Ordering of glow pass vs dither pass must be correct.
- **D-06:** CRT scanlines out of scope — deferred.
- **D-07 (REVERSED from original):** Keep `skybox.gdshader` + `SkyboxRenderer` + Sky `Environment`. Do NOT remove the skybox. Refeed it from `LuminousDescriptorBuilder.Descriptors` instead of its own `SyncSkyPoints` cache.
- **D-08 (REVISED):** 4 sequential play-test-gated plans. Plan 1 done. Plans 2-4 per the findability-first re-slice.
- **D-09 (REVISED):** Fold P1 (galaxy-space-star-meshes-invisible) + P2 (galaxy-visibility-in-universe-space) in here. Skybox is NOT removed.
- **D-10:** Branch `phase-05-rendering-overhaul`. Full revert trivial.
- **D-11:** Three-stage continuous star handoff: FAR=sky-shader point; MID=sky point fades out / post-process glow fades in; NEAR=WorldRenderer sphere mesh + glow. Weights from descriptor `LodWeight` (`LuminousLod.StarMeshWeight`).
- **D-12:** Near stars (parent sun + in-SOI siblings) render as sphere meshes + post-process glow. The 05-02 missing-sun regression is in-scope to fix here.
- **D-13:** Galaxies rendered in sky shader (procedural disc). Distance crossfade: far disc fades out as constituent stars take over approaching; fades back in leaving. Fix the "galaxy pops out of nowhere" bug (crossfade-threshold / antipodal-gate seen at 05-02 play-test).
- **Carried forward — NEVER re-litigate:**
  - No manual clip-space billboard MultiMesh (abandoned `StarPointRenderer` anti-pattern).
  - `UniMath` LCA-relative precision math preserved (CLAUDE.md ss"Position Math").
  - `StarRendering` is the single source of truth for star appearance.
  - 8-bit dither, unit-space render, per-space `1e-8` render factors, `1e6` camera far plane, `body_lit.gdshader` space-independent shading.
  - `EYEDIR` is sky-shader-only; in `shader_type spatial` reconstruct world view ray via `normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz)` (05-02 fix commit 22e4bc8).

### Claude's Discretion

- Exact mechanism for feeding the descriptor array into `skybox.gdshader` uniforms.
- Near-star glow/halo kernel and exact `LodWeight` thresholds for the point->glow->mesh stages.
- Galaxy disc crossfade thresholds and antipodal/threshold fix for "pops out of nowhere".
- Whether 05-02 `LuminousPassRenderer` node is repurposed or its quad is reworked; correct ordering of glow pass vs dither pass.
- Always-visible brightness floor for distant star findability (P1).

### Deferred Ideas (OUT OF SCOPE)

- CRT scanline effect (RND-01) — deferred to its own later task (D-06).
- `galaxy-disc-tilt-foreshortening` — re-confirm after the refeed, not actively reworked.
- Procedural universe generation, cockpit art, economy, combat.

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RND-02 | Bodies in the current render tier are rendered as sphere-mesh geometry; bodies one tier out are in the skybox | Sky-shader refeed covers distant tier; WorldRenderer near-star mesh fix covers in-tier stars (D-12) |
| RND-04 | Current system's star(s)/sun(s) rendered as bright, light-emitting sphere meshes casting light | WorldRenderer emissive star mesh path already exists; D-12 ensures parent sun renders up close (the 05-02 regression) |
| RND-05 | Dynamic spherical skybox represents bodies just outside the current render tier; never drifts with camera rotation | `skybox.gdshader` with `EYEDIR` (world-fixed in sky shaders) handles this; refeed from descriptor |
| RND-07 | Skybox-mesh handoff is visually continuous; no pop, jump, or flicker when crossing a scale boundary | Three-stage blend (D-11) via `LodWeight` from `LuminousDescriptorBuilder` — same numbers fed to different drawers |
| RND-01 | Floating origin: player at coordinate origin, world translated each frame, no precision jitter | Already done Phase 1; WorldRenderer preserves this; do not break |
| RND-03 | Planets rendered as sphere meshes with dithering and 8-bit palette | Already done; `dithering.gdshader` + `body_lit.gdshader`; glow must compose before dither (D-05) |
| RND-06 | Unit-space 1:1 render, per-space render factors, camera far <= 1e6 render units | Already done Phase 1; refeed must not change this |

</phase_requirements>

---

## Summary

Phase 5 delivers a **descriptor-driven two-layer luminous-body renderer**. Plan 1 (complete) built the `LuminousBodyDescriptor` pipeline — every luminous body is described once per frame with direction, angular size, brightness, color, and LOD weight. Plans 2-4 consume that pipeline.

The architecture has two drawers plus the existing mesh path:

1. **Sky shader (`skybox.gdshader`, KEPT)** — draws distant stars as points and galaxies as procedural discs. It renders at infinite distance behind all geometry automatically (`shader_type sky` semantic) so no depth-occlusion hacks are needed. Refeed its uniforms from `LuminousDescriptorBuilder.Descriptors` instead of `SkyboxRenderer`'s own `SyncSkyPoints` loop.

2. **Post-process glow (`luminous_pass.gdshader`, repurposed from 05-02)** — draws glow/halo around near stars in screen space. It is a Camera3D-child spatial quad with `blend_add` that composes additively into the HDR 3D buffer before the CanvasLayer dither quantizes it.

3. **Mesh path (`WorldRenderer`)** — draws planets and near stars as sphere meshes. D-12 requires the parent sun to render as a mesh (fixes the 05-02 missing-sun regression); the existing `GetOrCreateMesh` path already handles emissive star materials but the body selection logic must include stars whose `LodWeight` signals near-mesh dominance.

The three-stage star handoff (D-11) is continuous by construction: sky point fades out via `1 - LodWeight`, glow fades in as `LodWeight` approaches 1 with a `lod_fade` blend, and the mesh dominates at `LodWeight = 1`. All three drawers read the same descriptor — the same `Direction`, `Brightness`, and `AngularSize` — so no pop is possible.

**Primary recommendation:** Refeed `SkyboxRenderer.SyncSkyPoints` from `LuminousDescriptorBuilder.Descriptors` (drop the internal classify loop), add `galaxy_disc_weights` to `skybox.gdshader` to fix the galaxy-pop bug, extend `WorldRenderer.SyncBodies` to include near stars as sphere meshes (D-12), then narrow `luminous_pass.gdshader` to near-star glow/halo with the relaxed depth gate for mesh halos (Plan 3), and finally wire the HDR-before-dither composition and clean up dead code (Plan 4).

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Distant star points (sky-fixed, behind all geometry) | Sky shader (`skybox.gdshader`) | — | `shader_type sky` renders at infinite distance; EYEDIR is world-fixed automatically; no depth hacks needed |
| Galaxy procedural disc (far) | Sky shader (`skybox.gdshader`) | — | Same reasons as distant stars; disc already implemented in skybox.gdshader |
| Near-star glow / halo | Post-process pass (`luminous_pass.gdshader`) | — | Screen-space effect; composes additively over the 3D scene before dither quantizes; correct use of the spatial quad |
| Planet sphere meshes | WorldRenderer (mesh path) | — | Floating-origin sync, `body_lit.gdshader` Lambert shading — unchanged |
| Near-star sphere meshes | WorldRenderer (mesh path) | Post-process glow wraps them | Stars at `LodWeight >= threshold`; `GetOrCreateMesh` already creates emissive star mats |
| HDR->8-bit dither quantization | CanvasLayer dither (`dithering.gdshader`) | — | Runs last, over the fully-composed HDR 3D buffer + glow; must NOT add hint_depth_texture here (Godot bug #74464) |
| Descriptor production (single source of truth) | LuminousDescriptorBuilder (Node, priority=-10) | — | Must run before all drawers; already wired in Main.tscn |

---

## Standard Stack

### Core (no new packages — all in-engine or already in codebase)

| Component | Location | Purpose | Status |
|-----------|----------|---------|--------|
| `LuminousBodyDescriptor` | `Scripts/Render/LuminousBodyDescriptor.cs` | Per-body typed descriptor (D-02 feed) | DONE (Plan 1) |
| `LuminousLod` | `Scripts/Render/LuminousLod.cs` | Pure distance-to-LOD-weight curves | DONE (Plan 1) |
| `LuminousDescriptorBuilder` | `Scripts/Render/LuminousDescriptorBuilder.cs` | Per-frame classify+project loop; process_priority=-10 | DONE (Plan 1) |
| `SkyboxRenderer` | `Scripts/Render/SkyboxRenderer.cs` | Sky uniform push; **refeed from Descriptors[]** | TO MODIFY (Plan 2) |
| `skybox.gdshader` | `Shaders/skybox.gdshader` | Distant-star points + galaxy discs; **add galaxy_disc_weights** | TO MODIFY (Plan 2/4) |
| `LuminousPassRenderer` | `Scripts/Render/LuminousPassRenderer.cs` | Camera3D-child spatial quad; **narrow to near-star glow/halo** | TO MODIFY (Plan 3) |
| `luminous_pass.gdshader` | `Shaders/luminous_pass.gdshader` | Screen-space glow/halo; **narrow scope + fix depth gate** | TO MODIFY (Plan 3) |
| `WorldRenderer` | `Scripts/Render/WorldRenderer.cs` | Floating-origin mesh sync; **add near-star mesh rendering** | TO MODIFY (Plan 2) |
| `StarRendering` | `Scripts/Render/StarRendering.cs` | Single source of truth for appearance | PRESERVE UNCHANGED |
| `TierClassifier` | `Scripts/TierClassifier.cs` | Pure body-space classifier | PRESERVE UNCHANGED |
| `PostProcessRenderer` | `Scripts/Render/PostProcessRenderer.cs` | Dither host | PRESERVE UNCHANGED |
| `dithering.gdshader` | `Shaders/dithering.gdshader` | 8-bit ordered dither | PRESERVE UNCHANGED |

No external packages needed. This phase is entirely intra-project. [VERIFIED: codebase read]

---

## Architecture Patterns

### System Architecture Diagram

```
Frame N:
                    GameWorld.GameObjects
                           |
                  LuminousDescriptorBuilder (priority=-10)
                    |- UniMath.RelativePosition (LCA path)
                    |- StarRendering.AngularRadius/ApparentBrightness
                    |- LuminousLod.StarMeshWeight / GalaxyDiscWeight
                    +-> Descriptors[] (Direction, AngularSize, Brightness, BaseColor, LodWeight, BodyType)
                                 |
               +-----------------+----------------------------------+
               v                 v                                  v
        SkyboxRenderer    LuminousPassRenderer               WorldRenderer
       (refeed: drop       (narrow: glow/halo              (extend: near stars
        SyncSkyPoints,      for near stars only,             as sphere meshes,
        read Descriptors    blend_add spatial quad)           LodWeight gate)
        for dist stars                |                            |
        + galaxies)                   | blend_add HDR              |
               |                     v                            v
               v            3D transparent pass         3D opaque pass
        skybox.gdshader     (glow quad: additively       (planets, near-star
        (shader_type sky,    over 3D scene before         sphere meshes write
         EYEDIR world-fixed, bloom)                       depth, occlude sky)
         renders at inf dist)  |                               |
               |               +----------composed-------------+
               v                              |
        Background color buf       WorldEnvironment glow
        (drawn first, behind        (HDR bloom over full
         all geometry)               3D buffer)
                                              |
                                  CanvasLayer / dithering.gdshader
                                  (8-bit ordered dither: last pass,
                                   reads hint_screen_texture over bloom)
                                              |
                                         Final frame
```

### Render Pass Ordering (Forward+ confirmed from existing shader headers)

[VERIFIED: skybox.gdshader line 26 "Sky renders BEFORE the 3D scene and the CanvasLayer post-process"; luminous_pass.gdshader header "Camera3D-child Node3D, this quad renders in the 3D transparent pass — BEFORE WorldEnvironment glow and BEFORE the CanvasLayer dither"]

1. **Sky pass** — `skybox.gdshader` (`shader_type sky`) renders at the celestial sphere (infinite distance) before all 3D geometry. Star points and galaxy discs appear here. Result is in the background color buffer.
2. **3D opaque pass** — planet meshes and near-star sphere meshes. These occlude the sky automatically because they write depth.
3. **3D transparent pass** — `luminous_pass.gdshader` Camera3D-child spatial quad with `blend_add, depth_test_disabled`. Near-star glow/halo composes additively over the 3D scene (sky + opaque geometry). Runs BEFORE WorldEnvironment glow.
4. **WorldEnvironment glow** — built-in Godot bloom. Processes the HDR 3D buffer (sky + meshes + glow quad additive contribution). This is why the glow quad must fire in step 3 as a spatial pass, not in a CanvasLayer — it feeds the bloom.
5. **CanvasLayer dither** — `dithering.gdshader` (canvas_item) reads `hint_screen_texture` over the post-bloom buffer and quantizes to the 8-bit palette. This is the final pass.

**Critical:** The dither shader MUST NOT add `hint_depth_texture` (confirmed limitation: Godot bug #74464, canvas_item shaders cannot bind depth buffer in Forward+). This is already respected in the existing code. [VERIFIED: dithering.gdshader — no hint_depth_texture present]

### Pattern 1: Refeed SkyboxRenderer from Descriptors (Plan 2)

**What:** Replace `SkyboxRenderer.SyncSkyPoints` (which runs its own classify+project loop) with a loop that reads `LuminousDescriptorBuilder.Descriptors[]` directly and pushes them to the sky ShaderMaterial uniforms.

**When to use:** Plan 2 refactoring of SkyboxRenderer.

**Key insight from code read:** `SkyboxRenderer` currently does its own `UniMath.RelativePosition` loop and `TierClassifier.Classify` check (lines 149-237). `LuminousDescriptorBuilder` already does ALL of this work at priority=-10. After the refeed, `SkyboxRenderer.SyncSkyPoints` becomes a simple loop over `_builder.Descriptors[]`.

**What to preserve:** The `_skyDirs` RND-07 cache and `GetSkyDirection` accessor remain; they are consumed by the handoff baseline in future phases. Populate `_skyDirs[d.BodyIndex] = d.Direction` in the new loop for Star descriptors.

**What changes:** The internal classify+project loop is removed. `SkyboxRenderer` no longer needs `_world` or `_cam` for direction math — only `_builder` reference and `_skyMat`.

**Pre-requisite fix:** `Main.tscn` line 193 sets `process_mode = 4` (DISABLED) on `SkyboxRenderer`. This MUST be reset to `process_mode = 0` (INHERIT) or removed for the refeed to work. [VERIFIED: Main.tscn line 193]

**Refeed pattern skeleton:**
```csharp
// Source: synthesis from SkyboxRenderer.cs (live) + LuminousDescriptorBuilder.cs (live)
private void SyncSkyPoints()
{
    if (_builder == null || _skyMat == null) return;
    _skyDirs.Clear();   // RND-07 cache cleared each frame
    int count = 0, galCount = 0;

    for (int i = 0; i < _builder.DescriptorCount; i++)
    {
        ref var d = ref _builder.Descriptors[i];

        if (d.BodyType == UniObject.Type.Star && count < MaxStars)
        {
            _dirs[count]   = d.Direction;
            _sizes[count]  = d.AngularSize;
            // Brightness floor for findability (P1) [ASSUMED play-test knob]:
            float displayAlpha = Mathf.Max(d.Brightness, MinVisibleBrightness);
            _colors[count] = new Color(d.BaseColor.R, d.BaseColor.G, d.BaseColor.B, displayAlpha);
            _skyDirs[d.BodyIndex] = d.Direction;   // RND-07 handoff cache
            count++;
        }
        else if (d.BodyType == UniObject.Type.Galaxy && galCount < MaxGalaxies)
        {
            // Home-galaxy suppression already handled by LuminousDescriptorBuilder.
            // Do NOT add a second ancestor check here.
            _galDirs[galCount]         = d.Direction;
            _galSizes[galCount]        = d.AngularSize;
            _galColors[galCount]       = d.BaseColor;
            _galTypes[galCount]        = d.GalaxyType;
            _galOrientations[galCount] = d.GalaxyOrientation;
            _galDiscWeights[galCount]  = d.LodWeight;   // NEW: GalaxyDiscWeight crossfade
            galCount++;
        }
    }
    // Push to _skyMat (same pattern as existing SkyboxRenderer lines 240-257)
}
```

### Pattern 2: Add galaxy_disc_weights to skybox.gdshader (D-13, Plan 2)

**What:** `skybox.gdshader` currently has no `galaxy_disc_weights` array — it renders every non-suppressed galaxy at full opacity. This causes the "galaxy pops out of nowhere" bug: when the ship exits the galaxy SOI, the home-galaxy suppression guard (in `LuminousDescriptorBuilder`) stops firing and the galaxy suddenly appears at full opacity in one frame.

**Root cause from code read:** `skybox.gdshader` galaxy loop final line (line 191): `col += galaxy_colors[i].rgb * galaxy_bright;` — no weight applied. `luminous_pass.gdshader` already has this uniform (line 83: `uniform float galaxy_disc_weights[MAX_GALAXIES]`) and applies it (line 269: `col.rgb += galaxy_colors[i].rgb * galaxy_bright * disc_w;`). The fix is to add the same pattern to `skybox.gdshader`. [VERIFIED: skybox.gdshader line 191; luminous_pass.gdshader line 83 + 269]

**GLSL change:**
```glsl
// Add after galaxy_orientations declaration in skybox.gdshader:
uniform float galaxy_disc_weights[MAX_GALAXIES];

// In galaxy loop final line, change:
//   col += galaxy_colors[i].rgb * galaxy_bright;
// to:
col += galaxy_colors[i].rgb * galaxy_bright * galaxy_disc_weights[i];
```

**Also add the Main.tscn sub_resource default:** `shader_parameter/galaxy_disc_weights = PackedFloat32Array(0, 0, 0, 0)` to the ShaderMaterial_sky sub_resource.

**C# side:** Add `private readonly float[] _galDiscWeights = new float[MaxGalaxies];` to `SkyboxRenderer`, push via `_skyMat.SetShaderParameter("galaxy_disc_weights", _galDiscWeights)`.

### Pattern 3: Near-Star Mesh Rendering in WorldRenderer (D-12, Plan 2)

**What:** `WorldRenderer.SyncBodies` renders the ship's parent and its siblings — whatever shares the ship's parent frame. The parent sun renders as a mesh in Star space. The 05-02 missing-sun regression: when `LuminousPassRenderer` is active as a Camera3D-child quad, it may cover the sun's sky pixels, OR the sun mesh may be present but its `EmissionEnergyMultiplier` is set from `StarRendering.ApparentBrightness` which could be near-zero at close range if `distMeters` is computed wrongly.

**Diagnosis from code read (WorldRenderer.cs:480-488):** `EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters)` where `distMeters = relUnits.Magnitude() * ship.LocalPos.Scale`. For the parent star (isParent=true), `relUnits = ship.LocalPos.ToDouble3Units() * -1.0` (line 461). If the ship is very close to the star, `distMeters` approaches 0 and `ApparentBrightness` returns 0 (its guard: `if (distMeters <= 1e-30) return 0f`). This is the likely root cause of the missing sun. When the ship is inside the star's atmosphere (distMeters < 1e30), brightness floors to zero.

**The fix:** The emissive multiplier for a near star should be clamped to a minimum visible value (not zero) when the ship is inside or very close to the star body. The geometry is correct; only the brightness is wrong.

```csharp
// In WorldRenderer.RenderBodyAt, replace (line 487):
//   starMat.EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
// with:
float brightness = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
// Floor emissive energy so the star is always visibly bright when we're right next to it
// [ASSUMED play-test calibration knob: NearStarEmissionFloor = 0.8f]
starMat.EmissionEnergyMultiplier = Mathf.Max(brightness, NearStarEmissionFloor);
```

**Additional D-12 coverage:** WorldRenderer currently renders parent + siblings via `TierClassifier.CurrentTierMesh`. Stars ARE in this set when in Star space — so the mesh IS spawned. The issue is purely the brightness. No topology change to `SyncBodies` is needed to fix D-12; it is a brightness-floor fix on `RenderBodyAt`.

### Pattern 4: Near-Star Glow — Depth Gate Relaxation for Halo (D-11, Plan 3)

**What:** The 05-02 `luminous_pass.gdshader` uses `bool sky_pixel = (raw_depth < 1e-6)` to gate glow — only empty sky pixels get glow. For distant stars (sky points), this is correct. For near stars with a visible sphere mesh, this gate must be relaxed so the glow paints over the mesh as a halo effect.

**Current behavior (05-02 shader, line 219):** `float vis = sky_pixel ? 1.0 : 0.0;` — hard gate for all stars regardless of LOD weight.

**Revised behavior (Plan 3):**
```glsl
// Source: synthesis from luminous_pass.gdshader (live) fragment loop
float lod_fade = 1.0 - star_lod_weights[i];  // 1=far/glow dominant, 0=near/mesh dominant
float is_near  = star_lod_weights[i];          // 1=mesh taking over, 0=sky point only

// Far stars: sky-pixel gate prevents painting over geometry
// Near stars: gate relaxed so glow halos over the star mesh
float vis = mix(sky_pixel ? 1.0 : 0.0, 1.0, is_near);

col.rgb += star_colors[i].rgb * (star_colors[i].a * (disc + halo) * vis * lod_fade);
```

**Note on the product:** When `LodWeight = 1` (near, mesh dominant): `lod_fade = 0` AND `vis = 1` — but the product `vis * lod_fade = 0`, so the glow contribution is zero. This ensures the post-process glow fully fades out precisely when the mesh is dominant, with the halo brief during the transition window. Pop-free by construction.

### Pattern 5: Always-Visible Brightness Floor for Distant Stars (P1 debt fix)

**What:** Distant stars in Galaxy space have near-zero `ApparentBrightness` at light-year distances. The sky-shader `star_sizes` are already floored at one pixel (in `LuminousDescriptorBuilder.PixelAngularSize()`). But the BRIGHTNESS (alpha channel) is near zero, making the pixel-sized disc invisible.

**Key insight:** The fix is a display-floor on the `BaseColor.A` value pushed to sky uniforms — NOT a change to `StarRendering.ApparentBrightness` (which must stay physically correct for the mesh path). Apply the floor only when packing `_colors[count]` in the `SkyboxRenderer` refeed:

```csharp
// In SkyboxRenderer.SyncSkyPoints (after refeed):
private const float MinVisibleBrightness = 0.05f;  // [ASSUMED play-test knob]
// ...
float displayAlpha = Mathf.Max(d.Brightness, MinVisibleBrightness);
_colors[count] = new Color(d.BaseColor.R, d.BaseColor.G, d.BaseColor.B, displayAlpha);
```

**What does NOT change:** `LuminousBodyDescriptor.Brightness` field remains physically accurate. `WorldRenderer` still uses `d.Brightness` (via `StarRendering.ApparentBrightness`) for the mesh emissive multiplier (correct physics). Only the sky-point display alpha gets the floor.

### Anti-Patterns to Avoid

- **Running the classify+project loop twice per frame.** `LuminousDescriptorBuilder` runs at priority=-10. `SkyboxRenderer`, `LuminousPassRenderer`, and `WorldRenderer` MUST NOT call `BuildDescriptors()` themselves. They read `Descriptors[]` read-only. (Documented in source headers of all three consumers.)
- **Using `EYEDIR` in a `shader_type spatial` shader.** `EYEDIR` is only available in `shader_type sky`. The fix is already in `luminous_pass.gdshader` (commit 22e4bc8): `normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz)`. Do not regress this.
- **Adding `hint_depth_texture` to `dithering.gdshader`.** Canvas_item shaders cannot bind the depth buffer in Godot 4 Forward+ (bug #74464). The depth texture is only accessible from `shader_type spatial` or `shader_type sky`. [VERIFIED: dithering.gdshader — no hint_depth_texture]
- **Absolute-from-root UniVec3 subtraction.** Always use `UniMath.RelativePosition` for cross-space directions. Never form `bodyPos - shipPos` directly across spaces (catastrophic cancellation at ~1e30 m). [VERIFIED: CLAUDE.md ss"Position Math"]
- **Adding a second home-galaxy suppression check in SkyboxRenderer after the refeed.** `LuminousDescriptorBuilder.BuildDescriptors` already handles this (lines 125-127). The descriptor for the home galaxy does not appear in `Descriptors[]` when the ship is inside it. A second check in `SkyboxRenderer` is dead code and adds confusion.
- **Near-star glow on ALL pixels regardless of LodWeight.** For distant stars (sky points), glow must only paint on sky pixels. Only for near stars (LodWeight > 0) should the gate be relaxed for the halo effect. Treating all stars as "near" would paint glow over foreground geometry for distant sky-point stars.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Per-body classify + project | Custom loop per drawer | `LuminousDescriptorBuilder.Descriptors[]` (Plan 1, done) | Classifying once per frame prevents pop and eliminates duplicate UniMath walks |
| Star appearance math | Custom brightness/size formulas | `StarRendering.ApparentBrightness` / `StarRendering.AngularRadius` | Single source of truth; changing one changes both mesh and sky coherently |
| LOD curves | Custom distance-to-weight functions | `LuminousLod.StarMeshWeight` / `LuminousLod.GalaxyDiscWeight` | Unit-tested, guarded against NaN, continuously differentiable |
| Galaxy disc UV | Custom projection math | `galaxy_disc_coords_tilted` function in `skybox.gdshader` | Handles D-59 safe-basis tilt, TILT_FLOOR guard, antipodal ghost gate |
| Galaxy disc pattern | Custom noise / procedural | `spiral_galaxy` / `elliptical_galaxy` in `skybox.gdshader` | Proven; verified not to produce the ring-collapse bug (fef1d91) |
| World view ray in spatial shader | Custom matrix math | `normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz)` (22e4bc8 fix) | Already in `luminous_pass.gdshader`; exact pattern proven in play-test |

**Key insight:** Every piece of math in this phase either exists as a reusable function in the codebase or is a simple uniform-pass operation. The work is wiring (connecting drawers to consume the descriptor), not new mathematical invention.

---

## Common Pitfalls

### Pitfall 1: SkyboxRenderer has process_mode = 4 (Disabled) — it never runs

**What goes wrong:** `Main.tscn` line 193 sets `process_mode = 4` on `SkyboxRenderer`. Mode 4 in Godot is `PROCESS_MODE_DISABLED` — `_Process` is never called. After the refeed, if this is not fixed, `SkyboxRenderer` still won't push any uniforms to the sky shader.

**Why it happened:** During the 05-02 experiment, `SkyboxRenderer` was likely disabled to prevent it from fighting with `LuminousPassRenderer`. After the architecture reversal, the skybox is the primary distant-body drawer again, so it needs to run.

**How to avoid:** Plan 2 task must reset `process_mode` in `Main.tscn` for the `SkyboxRenderer` node (remove the `process_mode = 4` line, or set it to `process_mode = 0` which inherits from parent). [VERIFIED: Main.tscn line 193]

**Warning signs:** Stars and galaxies don't update directions as the ship moves; sky appears frozen or stuck at initial positions.

### Pitfall 2: galaxy_disc_weights missing from skybox.gdshader causes the "pop" bug

**What goes wrong:** Without `galaxy_disc_weights`, the galaxy disc renders at full opacity the instant the home-galaxy suppression guard lifts (when the ship exits the galaxy SOI). One frame: suppressed (not in Descriptors). Next frame: non-ancestor, appears at full disc opacity. Single-frame pop.

**Why it happens:** `skybox.gdshader` applies no fade to galaxy rendering — `galaxy_bright * galaxy_disc_weights` is not in the shader. The `LuminousDescriptorBuilder` home-galaxy suppression creates a hard boundary at the SOI edge.

**The boundary problem:** `LuminousLod.GalaxyDiscWeight(dist, soiMeters)` at `dist = soiMeters`: the ramp is from `0.1*soi` to `0.5*soi`, so at `1.0*soi` the value is already 1.0 (full weight). When the body first appears in Descriptors at `dist = soi`, it appears at `LodWeight = 1.0`. The pop occurs here.

**Fix:** After adding `galaxy_disc_weights` to the sky shader, also adjust the `LuminousLod.GalaxyDiscWeight` fade band to include the SOI boundary — e.g., ramp from `0.5*soi` to `1.2*soi` so the disc fades in gradually after the SOI exit. [ASSUMED — exact band is a play-test calibration knob]

**Warning signs:** Flying out of a galaxy causes the disc to suddenly appear at full brightness in a single frame.

### Pitfall 3: Near-star glow depth gate prevents halo over the star mesh

**What goes wrong:** The current `luminous_pass.gdshader` `sky_pixel` gate (`raw_depth < 1e-6`) prevents glow from painting over any geometry pixel. For near stars with a visible sphere mesh, glow appears only in the ring around the mesh, not as a halo over the visible star disc.

**Why it happens:** The sky_pixel gate was correct for the original scope (distant stars as sky points). For near stars with a mesh, the mesh surface pixels have `raw_depth > 1e-6`, so the gate suppresses glow exactly there.

**How to avoid:** Plan 3 modifies the gate to use `is_near = star_lod_weights[i]` to blend between gated (far) and ungated (near) behavior. The `lod_fade` product zeroes out the contribution fully when the mesh is dominant (`LodWeight = 1`), so the halo only appears during the transition window.

**Warning signs:** Near stars look like glowing rings with dark star-shaped holes in the center.

### Pitfall 4: WorldRenderer near-star emissive brightness goes to zero when close

**What goes wrong:** When the ship is INSIDE or very close to a star (distMeters near 0), `StarRendering.ApparentBrightness(luminosity, distMeters)` returns 0 (the guard `if (distMeters <= 1e-30) return 0f` fires, or the inverse-square flux overflows the log scale). The star mesh has `EmissionEnergyMultiplier = 0` and renders as a black sphere.

**Root cause from code:** `WorldRenderer.RenderBodyAt` line ~487: `starMat.EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters)`. When the ship clips into the star, `distMeters` can reach zero.

**How to avoid:** Add `NearStarEmissionFloor` clamp in `RenderBodyAt` for star meshes: `Mathf.Max(brightness, NearStarEmissionFloor)`. This is the D-12 fix. [ASSUMED floor value: 0.8f — play-test knob]

**Warning signs:** Flying close to the home star causes it to fade to black or become a dark sphere. This was reported as the "missing sun" 05-02 regression.

### Pitfall 5: Double classify+project loop per frame

**What goes wrong:** After the refeed, a developer adds back a classify loop to `SkyboxRenderer` "for safety," causing both `LuminousDescriptorBuilder` AND `SkyboxRenderer` to run `UniMath.RelativePosition` for every body every frame. This doubles the math cost and can cause subtle discrepancies if the results differ (e.g., due to a frame boundary where the ship just crossed an SOI).

**How to avoid:** `SkyboxRenderer.SyncSkyPoints` after the refeed must contain NO calls to `UniMath.RelativePosition`, `TierClassifier.Classify`, or `GameObjects` iteration. It reads `_builder.Descriptors[]` and pushes to `_skyMat`. That is all.

**Warning signs:** `GD.Print` shows classify logs running twice per frame; CPU cost doubles; sky and mesh positions subtly disagree after SOI transitions.

### Pitfall 6: RND-07 sky direction cache (_skyDirs) must be rebuilt in the refeed loop

**What goes wrong:** When `SkyboxRenderer.SyncSkyPoints` is refactored, the `_skyDirs` dictionary (line 85 of `SkyboxRenderer.cs`) that backs the `GetSkyDirection()` accessor is not populated. Plan 3 (handoff baseline) will read `GetSkyDirection(bodyIdx)` and always get `false`, breaking the pop-free handoff mechanism.

**How to avoid:** In the new refeed loop, for each Star descriptor processed, add: `_skyDirs[d.BodyIndex] = d.Direction;`. This mirrors lines 230-231 of the current `SkyboxRenderer`.

**Warning signs:** `SkyboxRenderer.GetSkyDirection(anyIndex, out _)` always returns `false` after the refeed.

---

## Code Examples

### Example 1: skybox.gdshader galaxy_disc_weights addition (D-13)

```glsl
// Source: synthesis from skybox.gdshader (live) + luminous_pass.gdshader (live)
// Add after galaxy_orientations uniform:
uniform float galaxy_disc_weights[MAX_GALAXIES];

// In galaxy loop, replace:
//   col += galaxy_colors[i].rgb * galaxy_bright;
// with:
col += galaxy_colors[i].rgb * galaxy_bright * galaxy_disc_weights[i];
```

### Example 2: Main.tscn ShaderMaterial_sky default for new uniform

```gdscript
// In Main.tscn sub_resource ShaderMaterial_sky, add:
shader_parameter/galaxy_disc_weights = PackedFloat32Array(0, 0, 0, 0)
```

### Example 3: SkyboxRenderer — new _galDiscWeights field + push

```csharp
// Source: live SkyboxRenderer.cs — new field mirrors _galSizes etc.
private readonly float[] _galDiscWeights = new float[MaxGalaxies];
// In SyncSkyPoints galaxy branch:
_galDiscWeights[galCount] = d.LodWeight;   // GalaxyDiscWeight value
// In push block:
_skyMat.SetShaderParameter("galaxy_disc_weights", _galDiscWeights);
```

### Example 4: WorldRenderer near-star emissive floor (D-12)

```csharp
// Source: live WorldRenderer.RenderBodyAt (~line 487)
// Replace:
//   starMat.EmissionEnergyMultiplier = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
// With:
private const float NearStarEmissionFloor = 0.8f;  // [ASSUMED play-test knob]
// ...
float brightness = StarRendering.ApparentBrightness(body.Luminosity, distMeters);
starMat.EmissionEnergyMultiplier = Mathf.Max(brightness, NearStarEmissionFloor);
```

### Example 5: luminous_pass.gdshader near-star depth gate blend (Plan 3)

```glsl
// Source: live luminous_pass.gdshader fragment star loop (~line 204-226)
// Replace the vis line and add is_near:
float lod_fade = 1.0 - star_lod_weights[i];  // 0=near mesh dominant, 1=far glow dominant
float is_near  = star_lod_weights[i];          // 0=far, 1=near mesh taking over

// Far stars: only paint on sky pixels (background behind geometry)
// Near stars: paint over everything for halo around visible star mesh
float vis = mix(sky_pixel ? 1.0 : 0.0, 1.0, is_near);

col.rgb += star_colors[i].rgb * (star_colors[i].a * (disc + halo) * vis * lod_fade);
// Note: when LodWeight=1, lod_fade=0 and vis=1, but product is 0 — glow fully gone at near.
// Pop-free by construction: glow and mesh fade in/out continuously via lod_fade.
```

---

## State of the Art (Godot 4 relevant)

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-drawer classify+project loop | Single `LuminousDescriptorBuilder` shared loop at priority=-10 | Plan 1 (05-01) complete | Eliminates dual caches; pops mathematically impossible |
| SkyboxRenderer with own classify loop | Refeed from Descriptors[] | Plan 2 (this phase) | SkyboxRenderer becomes a "uniform pusher" only |
| No fade on galaxy disc | Continuous `galaxy_disc_weights` crossfade | Plan 2 (D-13) | Fixes "galaxy pops out of nowhere" on SOI crossing |
| luminous_pass draws distant stars + galaxies | luminous_pass narrowed to near-star glow/halo | Plan 3 (D-11 rework) | Correct use of each shader type by what it must occlude against |
| WorldRenderer emissive brightness = 0 at close range | Emissive floor clamp in RenderBodyAt | Plan 2 (D-12) | Fixes 05-02 missing-sun regression |

**Deprecated/removed in this phase:**
- `SkyboxRenderer` internal classify+project loop — replaced by reading Descriptors[].
- `LuminousPassRenderer` galaxy uniforms and galaxy loop — galaxy drawing moves entirely to sky shader.

---

## Runtime State Inventory

This is a graphics/rendering refactor with no persistent external state. All state lives in the Godot scene graph and C# object fields, reset each game session.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — no database, no files, no serialized render state | None |
| Live service config | None | None |
| OS-registered state | None | None |
| Secrets/env vars | None | None |
| Build artifacts | `Main.tscn` has `process_mode = 4` on `SkyboxRenderer` — it is currently DISABLED | Fix in Plan 2: remove or set to 0 so it processes |

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.x (EcoSpace.Tests project, net8.0) |
| Config file | `EcoSpace.Tests/EcoSpace.Tests.csproj` |
| Quick run command | `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj --no-build -v quiet` |
| Full suite command | `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj -v normal` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RND-07 / D-11 | `LuminousLod.StarMeshWeight` returns 1 at near dist, 0 at far | unit | `dotnet test ... --filter "LuminousLodTests"` | Yes (LuminousLodTests.cs) |
| RND-07 / D-13 | `LuminousLod.GalaxyDiscWeight` returns 0 inside SOI, 1 far | unit | `dotnet test ... --filter "LuminousLodTests"` | Yes (LuminousLodTests.cs) |
| D-02 | Descriptor direction uses LCA path, not absolute-from-root | unit | `dotnet test ... --filter "LuminousDescriptorBuilderTests"` | Yes (LuminousDescriptorBuilderTests.cs) |
| D-12 | Near sun renders as mesh with visible brightness (visual) | manual play-test | Play-test Plan 2 gate: fly close to home star | Visual only |
| D-11 | Point->glow->mesh blend is pop-free (visual) | manual play-test | Play-test Plan 3 gate: approach/leave star slowly | Visual only |
| D-13 | Galaxy disc fades in/out without pop (visual) | manual play-test | Play-test Plan 4 gate: exit/enter galaxy SOI | Visual only |
| D-05 | Glow composes before dither (no quantization on glow halo) | manual play-test | Play-test Plan 4 gate | Visual only |

### Sampling Rate

- **Per task commit:** `dotnet test EcoSpace.Tests/EcoSpace.Tests.csproj --no-build -v quiet` (47 tests, <5s)
- **Per wave merge:** Same
- **Phase gate:** Full suite green AND per-plan visual play-test approved before advancing to next plan

### Wave 0 Gaps

None — the existing 47 tests cover the descriptor pipeline (Plans 2-4 consume it without changing it). Visual / shader behavior is not unit-testable in isolation from the Godot renderer. If Plan 2 adds a brightness-floor helper method to `SkyboxRenderer`, add a unit test for the floor clamp logic.

---

## Security Domain

Security enforcement is not applicable to a local game rendering pipeline with no external inputs, network traffic, or user-provided data. All inputs to the rendering system are authored test data loaded from `TestSetup.cs`.

---

## Package Legitimacy Audit

No external packages are introduced in Phase 5. This phase modifies existing Godot C# source files and GLSL shaders only. No `npm install`, `pip install`, or `dotnet add package` operations are required.

**Packages removed due to SLOP verdict:** None
**Packages flagged as suspicious SUS:** None

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Godot 4.6.2 Mono | All rendering changes | Assumed present | 4.6.2 | None — required |
| .NET 8.0 SDK | dotnet test | Assumed present | 8.0.x | None — required |
| DirectX 12 GPU | Forward+ renderer | Assumed present | Windows 11 | None — required for Forward+ |

All dependencies are the project's existing runtime. No new tools required.

---

## Open Questions

1. **SkyboxRenderer process_mode = 4 (Disabled) — needs confirmation before Plan 2 implementation**
   - What we know: `Main.tscn` line 193 sets `process_mode = 4` on `SkyboxRenderer`, which disables `_Process` entirely. [VERIFIED: Main.tscn]
   - What's unclear: Was this intentionally left disabled to prevent SkyboxRenderer and LuminousPassRenderer from both pushing to the sky in 05-02?
   - Recommendation: Plan 2 must reset `process_mode` to 0 (inherit) and verify that SkyboxRenderer runs each frame after the refeed.

2. **GalaxyDiscWeight fade band needs extension to cover the SOI boundary**
   - What we know: `LuminousLod.GalaxyDiscWeight(soiMeters, soiMeters)` returns 1.0 (the ramp tops out at `0.5*soi`, well below the `1.0*soi` SOI boundary). When the home galaxy first appears in Descriptors (at dist = soiMeters), it appears at full disc weight. [VERIFIED: LuminousLod.cs lines 65-71]
   - Recommendation: Extend the GalaxyDiscWeight fade band to cover the SOI boundary region. Possible knob: change `fadeEnd` from `0.5*soi` to `1.1*soi` so the ramp includes the SOI crossing. [ASSUMED — play-test knob]

3. **LuminousPassRenderer galaxy loop — remove entirely in Plan 3 or leave as a stub?**
   - What we know: `LuminousPassRenderer` currently handles galaxy rendering via `galaxy_disc_weights` (luminous_pass.gdshader lines 229-269). After the revised architecture, galaxies belong entirely to the sky shader path.
   - Recommendation: Remove the galaxy loop and galaxy uniforms from `LuminousPassRenderer` and `luminous_pass.gdshader` in Plan 3. The sky shader handles them. Keeping the stub would mean two shaders both claiming to draw galaxies — a source of future confusion.

4. **Near-star mesh from descriptor: WorldRenderer needs direction-based placement**
   - What we know: `WorldRenderer.RenderBodyAt` derives mesh position from `LocalPos.ToLocalDoubleUnits` (parent-frame relative position). For near stars that ARE already in the parent+siblings render set (in Star space), this is correct and no change is needed. The D-12 fix is specifically the `EmissionEnergyMultiplier` floor, not a position change.
   - Clarification: After deeper code review, D-12 does NOT require a new "RenderBodyAtDescriptor" overload. The star IS in the render set via the existing `SyncBodies` parent+siblings loop. The bug was purely the brightness floor.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | MinVisibleBrightness = 0.05f is a good starting floor for distant star findability | Pattern 5 | Stars too bright (floor too high) or still invisible (floor too low) — play-test knob |
| A2 | halo_size multiplier 8.0 and halo strength 0.3 in near-star glow kernel produce good results | Code Ex. 5 | Glow too large/small or dim/bright — play-test knob |
| A3 | NearStarEmissionFloor = 0.8f is a good floor for near-star mesh visibility | Code Ex. 4 | Star mesh still dark (floor too low) or blows out to white (floor too high) — play-test knob |
| A4 | GalaxyDiscWeight fadeEnd should be extended from 0.5*soi to ~1.1*soi to fix SOI-boundary pop | Open Question 2 | Galaxy may still pop on SOI exit if band mismatched — play-test knob |
| A5 | SkyboxRenderer process_mode = 4 was set during 05-02 testing and should be reset to 0 for the refeed | Pitfall 1 | If intentionally disabled for a reason, resetting it breaks the intended 05-02 architecture — needs confirmation |
| A6 | LuminousPassRenderer galaxy uniforms should be removed in Plan 3 | Open Question 3 | If galaxies need to appear in both drawers, removing them breaks that — verify intent |

---

## Sources

### Primary (HIGH confidence — live codebase read, authoritative for this project)

| File | What was verified |
|------|------------------|
| `Scripts/Render/LuminousBodyDescriptor.cs` | Descriptor struct fields, BaseColor.A=Brightness packing convention |
| `Scripts/Render/LuminousLod.cs` | StarMeshWeight/GalaxyDiscWeight curves, threshold knobs, zero-distance guards |
| `Scripts/Render/LuminousDescriptorBuilder.cs` | BuildDescriptors loop, home-galaxy suppression, process_priority=-10, Descriptors[] shape |
| `Scripts/Render/SkyboxRenderer.cs` | SyncSkyPoints loop, _skyDirs cache, uniform push pattern |
| `Shaders/skybox.gdshader` | Uniform declarations, galaxy_disc_weights ABSENCE confirmed (line 191 has no weight), EYEDIR availability |
| `Scripts/Render/LuminousPassRenderer.cs` | Camera3D-child quad setup, descriptor read pattern, galaxy uniforms present |
| `Shaders/luminous_pass.gdshader` | Depth gate, world_view_dir reconstruction, star loop, galaxy loop with disc_w, blend_add |
| `Scripts/Render/WorldRenderer.cs` | SyncBodies parent+siblings logic, RenderBodyAt emissive star path, brightness formula |
| `Scripts/Render/StarRendering.cs` | ApparentBrightness formula, zero-distance guard (returns 0f), Exposure knob |
| `Scripts/TierClassifier.cs` | SkyTier enum, Classify logic |
| `Scripts/Render/PostProcessRenderer.cs` | dithering.gdshader host, CanvasLayer ordering |
| `Shaders/dithering.gdshader` | canvas_item shader confirmed — no hint_depth_texture |
| `Main.tscn` | process_mode=4 on SkyboxRenderer (DISABLED), LuminousPassRenderer as Camera3D child, process_priority=-10 on builder |
| `.planning/phases/05-rendering-overhaul/05-CONTEXT.md` | All locked decisions D-01 through D-14, architecture revision rationale |
| `.planning/phases/05-rendering-overhaul/05-01-SUMMARY.md` | Plan 1 deliverables, what was tested and play-test approved |
| `CLAUDE.md` | Position math mandatory rules, namespace conventions, coding style |
| `.planning/todos/pending/galaxy-space-star-meshes-invisible.md` | Root cause analysis of P1 debt (emissive brightness + sub-pixel) |
| `.planning/todos/pending/galaxy-visibility-in-universe-space.md` | Root cause analysis of P2 debt (no mesh + skybox gap in Universe space) |

### Secondary (MEDIUM confidence — inferred from shader comments referencing Godot sources)

- `skybox.gdshader` header: "EYEDIR is world-space in Godot 4 sky shaders — NOT camera-relative." [CITED: skybox.gdshader line 8, citing godotengine.org/article/custom-sky-shaders-godot-4-0/]
- `luminous_pass.gdshader` header: render ordering confirmed "BEFORE WorldEnvironment glow and BEFORE the CanvasLayer dither" [CITED: luminous_pass.gdshader header comment]
- Godot canvas_item depth buffer limitation: Godot bug #74464 [CITED: luminous_pass.gdshader header comment line 43]
- Godot Forward+ reversed-Z: far plane = 0.0, near plane = 1.0 [CITED: luminous_pass.gdshader lines 168-170, consistent with `raw_depth < 1e-6` sky-pixel gate]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all components are existing code, fully read from disk
- Architecture: HIGH — render pass ordering confirmed from shader headers and existing behavior
- Pitfalls: HIGH — most pitfalls derived from live code analysis (process_mode=4, galaxy_disc_weights absence, brightness=0 at close range)
- LOD thresholds (MinVisibleBrightness, NearStarEmissionFloor, halo knobs): LOW — marked [ASSUMED], calibrated at each plan's play-test gate

**Research date:** 2026-06-19
**Valid until:** Grounded in the live codebase on branch `phase-05-rendering-overhaul`. Valid until files listed in Sources are modified. No external API dependency.

---

## RESEARCH COMPLETE
