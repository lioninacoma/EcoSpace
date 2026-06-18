# Phase 5: Outer-tier body findability & galaxy visibility - Research

**Researched:** 2026-06-18
**Domain:** Godot 4 spatial/sky shaders, MultiMesh PSF rendering, floating-origin star rendering, GDShader GLSL
**Confidence:** MEDIUM (shader technique details are ASSUMED/training; game-code integration points are HIGH from direct source read)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Galaxy visibility in Universe space (D-28 fork)**
- D-48: Skybox-only — D-28 REAFFIRMED. Current-tier galaxies rendered on the skybox; no galaxy meshes or billboards ever.
- D-49: Galaxies are ALWAYS sky entities, regardless of ship's tier. TierClassifier routes Type==Galaxy to sky unconditionally.
- D-50: Target marker derived from UniObject configured parameters, NOT from any mesh — refines D-46. Position from UniMath.RelativePosition (ship→body, LCA frame → camera projection); size from StarRendering.AngularRadius(RadiusMeters, trueDist) with D-46 min on-screen radius floor. Circle stops gating on WorldRenderer.GetRenderPosition.
- D-51: Approach/distance cue for sky-only galaxy = disc growth + HUD distance.
- D-52: Galaxy→Universe exit disc appearance is an in-game verification item, not a locked mechanism.

**Star findability & unified rendering model (P1 fix)**
- D-53: Always-on brightness-floored PSF point + additive sphere-mesh blend — SUPERSEDES D-21 & D-22. Every star always drawn as a brightness-floored point (PSF-style) at all tiers. Sphere mesh blends in additively as true distance shrinks — continuous distance-driven blend, NOT instant point↔mesh swap and NOT scale-crossing-only promotion.
- D-54: Unify the star and galaxy LOOK now; defer nebula. Star points and galaxy discs share one visual family (PSF/glow for stars; Star-Nest-style volumetric look for galaxy discs). True nebula rendering deferred.
- D-55: Proximity brightening = distance-driven glow halo + bloom (semi-realistic). Volumetric glow halo (PSF wings / additive radial falloff) intensifies as true distance shrinks, feeding existing WorldEnvironment bloom. Inverse-square-ish, clamped by luminosity_cap, reusing StarRendering brightness curve + existing glow pipeline.
- D-56: One unified star-point renderer (MultiMesh PSF) for every star at every tier. Single star-point renderer driven by UniObject + StarRendering; sphere mesh layers on top. Skybox shader's star-point loop is RETIRED; galaxies stay in skybox shader.
- D-57: Borrow godot-starlight's technique, custom-fit to EcoSpace. MIT license CONFIRMED (see below). Adopt techniques but implement against EcoSpace's model — UniObject, StarRendering.ApparentBrightness/AngularRadius, floating-origin, 8-bit dither.

**Floor consistency & handoff continuity**
- D-58: Implement the new rendering model first; address floor-release/consistency only if it manifests.

**Galaxy disc tilt (polish)**
- D-59: Build disc tilt into the new unified galaxy shader. MANDATORY safe approach: stable basis from galaxy_dir, foreshorten toward disc_normal with clamp FLOOR so it never collapses to a sky-spanning ring. Keep dot(EYEDIR, galaxy_dir) > 0 front-hemisphere gate. Re-wire currently-unused galaxy_orientations[i].xyz.

### Claude's Discretion
- Exact PSF kernel / min_size / luminosity_cap / glow-falloff values — tune by feel in-game via [Export] knobs.
- Far-plane-clip technique specifics for never culling distant star points.
- Galaxy-disc volumetric-look shader specifics (how much Star-Nest-style detail vs current procedural spiral/elliptical functions) within D-54 "shared family, no true nebula" guideline.
- Exact foreshorten clamp-floor value for D-59.
- How MultiMesh per-instance data (position/luminosity/color) is fed from GameWorld/UniObject each frame within the floating-origin model.
- Whether any new tunable belongs on WorldRenderer, a new star-point renderer node, or the shader.

### Deferred Ideas (OUT OF SCOPE)
- True nebula rendering + authored nebula bodies (the full Star-Nest volumetric star+nebula+galaxy shader) — future phase; Phase 5 unifies star+galaxy LOOK only (D-54).
- Full volumetric light scattering / god-rays near stars — future phase; Phase 5 uses glow halo + bloom (D-55).
- Whole-hierarchy nav-HUD + tracking name/distance label — backlog 999.1; Phase 5 only reworks the existing target circle to be UniObject-driven (D-50).
- Shader-rendered ellipsoid target outline — backlog 999.2; the D-50 circle stays a 2D analytic circle.
- Floor-release curve / fine consistency tuning — only if it manifests in play-test (D-58), not pre-designed.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RND-02 (refine) | Bodies belonging to current render tier rendered dynamically as sphere-mesh geometry — in Galaxy space the galaxy's stars must be mesh-visible | MultiMesh PSF renderer (D-56) ensures every star is always a visible point regardless of mesh sub-pixel status; additive mesh blend (D-53) layers sphere on approach |
| RND-04 (refine) | Current system's stars rendered as bright emissive sphere meshes | Star mesh EmissionEnergyMultiplier continues to derive from StarRendering.ApparentBrightness; additive PSF point supplements at all distances |
| RND-07 (revisit) | Skybox↔mesh handoff visually continuous | D-53 replaces the instant swap with a continuous additive blend — always-on PSF means the handoff is never a pop, the point simply grows into the mesh |
| D-28 revisit | Galaxies visible in Universe space | D-48/D-49: current-tier galaxies pushed to skybox via SkyboxRenderer; TierClassifier extended to route Galaxy→sky unconditionally regardless of ship tier |
</phase_requirements>

---

## Summary

Phase 5 fixes two Phase-03 UAT gaps and adds polish. The root causes are structural: (1) in Galaxy space the home galaxy's star meshes have sub-pixel radii AND near-zero emission at light-year distances, making them invisible; (2) in Universe space galaxies are the current tier but TierClassifier currently classifies them as `CurrentTierMesh` while WorldRenderer skips all Galaxy-typed bodies (D-28), so nothing draws them. The galaxy disc tilt was dropped as a safety measure (commit fef1d91) and needs reinstating with a collapse-safe basis.

The fix is a unified, always-on rendering model. Every star gets a `MultiMeshInstance3D` PSF renderer (godot-starlight technique, MIT confirmed) that keeps every star always visible at all tiers via a brightness-floored additive quad. The sphere mesh contribution blends in additively as true distance falls. Galaxies gain their current-tier visibility fix through a SkyboxRenderer extension that pushes galaxies regardless of whether they are "next tier out" or "current tier" — TierClassifier is extended with a Galaxy-unconditional path. The galaxy disc shader is reworked with a Star-Nest-influenced volumetric look and D-59 safe-basis tilt. The Hud target circle is decoupled from WorldRenderer.GetRenderPosition to become a pure UniObject-driven projection so it works for sky-only bodies (galaxies, sub-pixel galaxy-space stars).

**Primary recommendation:** Build in this order — (1) MultiMesh PSF star-point renderer (D-56, the headline structural fix); (2) TierClassifier/SkyboxRenderer galaxy current-tier extension (D-48/D-49); (3) Hud target circle rework (D-50); (4) proximity glow halo (D-55); (5) galaxy shader volumetric look + disc tilt (D-54/D-59). This order maximises early findability and separates shader iteration (play-test required) from code logic.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Always-on star PSF point | Render / 3D world (MultiMesh spatial shader) | — | Must be a world-space Node3D that participates in floating-origin; sky shader retired for stars (D-56) |
| Additive sphere-mesh blend | Render / 3D world (WorldRenderer.RenderBodyAt) | — | RenderBodyAt already computes per-star distance + EmissionEnergyMultiplier; additive term layers here |
| Current-tier galaxy sky | Sky shader / SkyboxRenderer | — | Galaxies stay sky-only (D-48/D-49); SkyboxRenderer extends its classify filter |
| Galaxy disc tilt | Sky shader (skybox.gdshader galaxy loop) | — | Shader-only change; tilt math applied in the galaxy fragment path |
| UniObject-driven target marker | HUD / Hud.cs | Camera3D (projection) | Decoupled from WorldRenderer mesh set; UniMath provides the LCA direction |
| Proximity glow halo | Render / 3D world (MultiMesh PSF shader or dedicated quad) | WorldEnvironment bloom | Additive radial falloff feeds existing bloom; no new post-process pass |
| StarRendering shared model | Cross-cutting (Render namespace) | — | PSF point, mesh emission, glow, marker all derive from StarRendering — prevents drift |

---

## Standard Stack

### Core (existing — extend only)

| Component | Version | Purpose | Note |
|-----------|---------|---------|------|
| `MultiMeshInstance3D` + `MultiMesh` | Godot 4.6.2 built-in | Batch-render N star PSF quads efficiently | `use_colors=true`, `use_custom_data=true`; C# API: `SetInstanceTransform`, `SetInstanceColor`, `SetInstanceCustomData` [CITED: docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html] |
| Godot spatial shader (`shader_type spatial`) | Godot 4.6.2 built-in | PSF star quad shader + star halo shader | `render_mode blend_add, unshaded, depth_test_disabled` for additive light sources [CITED: docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html] |
| Godot sky shader (`shader_type sky`) | Godot 4.6.2 built-in | Galaxy disc (reworked), EYEDIR world-space always correct | Star loop removed; galaxy loop reworked [ASSUMED: per D-56 / existing skybox.gdshader] |
| `StarRendering.cs` | Project (existing) | Single source of truth: ApparentBrightness, AngularRadius, Exposure | PSF, mesh emission, glow halo, marker ALL derive from this — no drift |
| `UniMath.cs` | Project (existing) | LCA-relative cross-frame position/distance math | MANDATORY for marker direction and glow distance at galaxy/universe scale |
| `WorldEnvironment` (glow/bloom) | Godot 4.6.2 built-in | Bloom from emissive surfaces automatically | Star mesh + PSF quad emission > 1.0 feeds bloom; no extra pass needed |

### No External Packages Required

Phase 5 borrows the **technique** from godot-starlight but does not import the addon. All code is written against EcoSpace's own model. No NuGet packages are added.

---

## Package Legitimacy Audit

No external packages are installed in this phase. The godot-starlight reference is a technique source only (MIT license confirmed, see below). The package legitimacy gate is not applicable.

---

## Architecture Patterns

### System Architecture Diagram

```
[GameWorld / TestSetup]  ← UniObject[] (star positions, Luminosity, RadiusMeters, BaseColor)
         │
         ├──► [StarPointRenderer]  (NEW MultiMeshInstance3D Node3D child of Main)
         │        │  per-frame: UniMath.RelativeMetres(ship, star) → render-space Vector3
         │        │  MultiMesh.SetInstanceTransform(i, floatingOriginPos)
         │        │  MultiMesh.SetInstanceColor(i, BaseColor)
         │        │  MultiMesh.SetInstanceCustomData(i, luminosity packed)
         │        └─► [PSF quad shader]  blend_add, unshaded, depth_test_disabled
         │                               POSITION.z forced to far plane (never culled)
         │                               INSTANCE_CUSTOM.x → luminosity → brightness floor
         │
         ├──► [WorldRenderer]  (existing, extended)
         │        │  RenderBodyAt → additive EmissionEnergyMultiplier (existing)
         │        │  + additive blend contribution as distance shrinks (D-53)
         │        └─► star sphere mesh  StandardMaterial3D (existing)
         │
         ├──► [SkyboxRenderer]  (extended)
         │        │  galaxy_count: now includes current-tier galaxies (D-48)
         │        │  TierClassifier: Galaxy → sky always (D-49)
         │        └─► [skybox.gdshader]  galaxy loop reworked:
         │                               + Star-Nest volumetric look (D-54)
         │                               + D-59 safe-basis disc tilt
         │                               star loop REMOVED (D-56)
         │
         └──► [Hud]  (extended)
                  │  UpdateTargetCircle: removed GetRenderPosition gate
                  │  screenPos = camera.UnprojectPosition(worldRenderer.GlobalPosition
                  │              + UniMath.RelativeMetres(ship, target) × renderFactor)
                  │  radius = StarRendering.AngularRadius × projectedPixelRadius
                  └─► DrawArc (unchanged draw call)
```

### Recommended Project Structure

```
Scripts/Render/
├── StarPointRenderer.cs   # NEW: MultiMesh PSF star-point renderer node
├── WorldRenderer.cs       # EXTEND: additive mesh blend term (D-53)
├── SkyboxRenderer.cs      # EXTEND: current-tier galaxy push (D-48/D-49)
└── StarRendering.cs       # UNCHANGED (single shared model)
Scripts/
└── TierClassifier.cs      # EXTEND: Galaxy unconditional sky path (D-49)
Shaders/
└── skybox.gdshader        # REWORK: galaxy loop (D-54/D-59), star loop REMOVED (D-56)
Shaders/star_point.gdshader  # NEW: PSF quad shader (blend_add, depth_test_disabled)
Scripts/Hud/
└── Hud.cs                 # EXTEND: UpdateTargetCircle decoupled from GetRenderPosition (D-50)
```

---

## godot-starlight License Confirmation

**D-57 requires: CONFIRMED.**

`https://github.com/tiffany352/godot-starlight/blob/main/LICENSE.md`

> Copyright 2023 Tiffany Bennett
> MIT License

[CITED: github.com/tiffany352/godot-starlight/blob/main/LICENSE.md]

The MIT license permits use, modification, and distribution in any project including commercial. Attribution (keeping the copyright notice) is required. EcoSpace may freely adopt the technique.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Additive light accumulation for stars | Custom blending math | `render_mode blend_add` in spatial shader | Godot's pipeline handles GPU alpha compositing correctly; manual blending gets premult wrong |
| Far-plane depth test bypass | Custom z-buffer hacks | `render_mode depth_test_disabled` + POSITION.z = far_plane trick in vertex | godot-starlight's confirmed pattern; writing POSITION in vertex gives full control of clip-space Z |
| Per-instance luminosity | Uniform arrays | `MultiMesh.use_custom_data = true` → `SetInstanceCustomData` → `INSTANCE_CUSTOM.x` in shader | The correct Godot per-instance data channel for extra float data beyond color |
| Cross-frame ship→star distance at galaxy scale | Raw `ToDouble3()` on absolute positions | `UniMath.RelativeMetres(ship, star, objs)` | Catastrophic cancellation at 1e30 m is a documented landmine in CLAUDE.md; LCA path is the only safe route |
| Screen projection of sky-only body | Reverse-engineering sky shader direction | `camera.UnprojectPosition(GlobalPosition + relMetres_cast_to_render_units)` | Camera.UnprojectPosition works on any 3D world position regardless of whether a mesh exists |
| Min on-screen radius floor | Re-implementing AngularRadius | `StarRendering.AngularRadius(body.RadiusMeters, dist)` | Already exists; Hud already uses it for MIN_CIRCLE_RADIUS; extending it keeps mesh/sky/marker consistent |

---

## Common Pitfalls

### Pitfall 1: Float precision at galaxy/universe scale in StarPointRenderer

**What goes wrong:** The new StarPointRenderer computes a star's render-space position from its UniVec3 LocalPos. If it naively calls `body.LocalPos.ToDouble3()` and multiplies by the render factor, it forms an absolute-from-root metres value. At galaxy scale (~1e20 m) this loses all low bits — the star position jitters or lands at the wrong pixel.

**Why it happens:** `UniVec3.ToDouble3()` zeroes the Long3.Units (only the fractional Offset survives), collapsing a 1e20 m position to ~1e4 m precision. The same catastrophic-cancellation problem already bit `SkyboxRenderer` (commit 83dfce4) and `WorldRenderer` (see `ComputeStarRenderPosFromHierarchy`).

**How to avoid:** Always call `UniMath.RelativeMetres(ship, star, objs)` to get the ship-relative vector in metres first; then convert to render-space: `relMetres / ship.LocalPos.Scale * factor`. The LCA path cancels the large common-ancestor offset in integer arithmetic before calling ToDouble3 on the small delta.

**Warning signs:** Star points drift with camera rotation or jump frame-to-frame; star that should be directly ahead appears off-angle.

### Pitfall 2: MultiMesh position not floating-origin anchored

**What goes wrong:** StarPointRenderer sets `MultiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, absoluteWorldPos))` instead of ship-relative positions. The camera stays at world origin; stars drift as the ship moves.

**Why it happens:** Godot MultiMesh transforms are in world space. If the node itself isn't positioned at the world origin AND instance transforms are absolute, the double-offset makes every star appear at the wrong position.

**How to avoid:** The StarPointRenderer node must be added as a child of Main (at world origin). Instance transforms must be set to the SHIP-RELATIVE render-space Vector3 (same as WorldRenderer's pattern: `relUnits × factor`, where relUnits = `UniMath.RelativeMetres(ship, star, objs) / ship.LocalPos.Scale`). The node's own position stays at Vector3.Zero (world origin). This mirrors WorldRenderer's mesh pattern exactly.

**Warning signs:** Stars appear at wrong positions or all at one spot; stars move when ship moves.

### Pitfall 3: Far-plane culling removes distant star points

**What goes wrong:** A star 4.2 ly away is placed at a render-space position like `(4.2e16 / 1 * 1e-8) = 4.2e8` render units — well beyond the camera far plane of 1e6. Godot clips the MultiMesh instance before rasterization.

**Why it happens:** Camera frustum culling (far plane at 1e6 render units) eliminates geometry placed beyond it, regardless of apparent size.

**How to avoid (godot-starlight pattern):** In the vertex shader, write to `POSITION` (clip-space) directly instead of letting Godot transform `VERTEX`. Set `POSITION.z` to a value at or just inside the far plane. The godot-starlight shader uses:
```glsl
// shader_type spatial; render_mode blend_add, unshaded, depth_test_disabled;
void vertex() {
    // Billboard: face camera
    vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    vec4 projected = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world_pos, 1.0);
    // Force Z to far plane so it is never clipped
    // (OUTPUT_IS_SRGB differentiates Forward+ from Compatibility)
    POSITION = projected;
    if (OUTPUT_IS_SRGB) {
        POSITION.z = -1.0 * projected.w;
    } else {
        POSITION.z = 0.0 * projected.w;
    }
    // Apply billboard scale in clip space
    POSITION.xy += VERTEX.xy * some_scale * projected.w;
}
```
[CITED: github.com/tiffany352/godot-starlight (Star.gdshader)]

The key: `render_mode depth_test_disabled` prevents depth-buffer rejection; writing POSITION.z forces the primitive to the far plane (so it renders behind geometry, not in front of it).

**Warning signs:** Stars visible near camera but vanishing when moved far; no star points in galaxy space at all.

### Pitfall 4: Ring collapse when disc_normal ≈ galaxy_dir (D-59 landmine)

**What goes wrong:** Building the disc tangent basis from `disc_normal` instead of `galaxy_dir` causes the basis to degenerate when the disc normal points roughly toward (or away from) the camera. The cross product `cross(up, disc_normal)` approaches zero, and the galaxy renders as a 1-D band / sky-spanning ring.

**Why it happens:** The authored `disc_normal ≈ (0.2, 0.98, 0.0)` is nearly world-up. When `galaxy_dir` (the view direction TO the galaxy) is also near world-up (i.e. the galaxy is nearly overhead), the degenerate axis guard swaps to `vec3(1,0,0)` — but if `disc_normal` itself is nearly the same as `galaxy_dir`, one of the cross products collapses regardless. This is exactly the bug fixed in commit fef1d91.

**How to avoid (D-59 mandatory safe approach):**
1. Build the stable face-on basis from `galaxy_dir` (already robust — the current shader does this correctly).
2. To add tilt, compute a foreshortening scale factor from `disc_normal`:
   - `tilt = dot(disc_normal, galaxy_dir)` — this is cos(angle between disc plane normal and view direction).
   - When `tilt ≈ 0`, the disc is viewed edge-on (flat band); when `tilt ≈ 1`, it is face-on (circle).
   - To prevent collapse: `float safe_tilt = clamp(abs(tilt), TILT_FLOOR, 1.0)` where `TILT_FLOOR` is e.g. 0.15.
   - Apply foreshortening to the minor axis of the UV: `uv.y /= safe_tilt` (stretching the y-axis so the disc looks tilted without the UV collapsing).
3. Keep the `d > 0.0` front-hemisphere gate (prevents antipode ghost — commit fef1d91's second fix).

**Warning signs:** Galaxy renders as a sky-spanning band/ring; one galaxy appears twice (ghost at antipode); console shows no shader error (purely visual).

### Pitfall 5: TierClassifier misclassifies Galaxy-type bodies in Universe space

**What goes wrong:** When the ship is in Universe space (CurrentSpace == Universe), a Galaxy-typed body also has CurrentSpace == Universe, so TierClassifier returns `CurrentTierMesh`. WorldRenderer then skips it (D-28 galaxy guard), and SkyboxRenderer's current loop skips it because it only runs bodies with `NextTierSkybox` classification. Result: no galaxy renders in Universe space.

**Why it happens:** The current TierClassifier routes CurrentSpace-match → CurrentTierMesh, not distinguishing body type. D-49 requires Galaxy-typed bodies to ALWAYS go to SkyboxRenderer regardless of tier match.

**How to avoid:** Add an ObjectType check before the space comparison in TierClassifier (or in SkyboxRenderer's loop):
- Approach A — TierClassifier: if `body.ObjectType == UniObject.Type.Galaxy` → return `NextTierSkybox` unconditionally (regardless of CurrentSpace match). This is the cleanest, single-source-of-truth fix (D-49).
- Approach B — SkyboxRenderer: extend the classify filter to also accept `CurrentTierMesh` bodies when they are galaxies. This is less clean but avoids changing TierClassifier (which has 16 test cases).

**Recommendation:** Approach A (TierClassifier change) is structurally correct per D-49; update the unit tests to cover the Galaxy-unconditional-sky cases.

**Warning signs:** Flying to Universe space, galaxies disappear (not just dimmer — completely gone from sky).

### Pitfall 6: Target circle gated on GetRenderPosition fails for sky-only and sub-pixel bodies

**What goes wrong:** The existing `UpdateTargetCircle` guard 3 calls `_worldRenderer.GetRenderPosition(tgtIdx, out Vector3 renderPos)` and returns immediately if false. In Galaxy space the star mesh exists but may have been culled by distance; in Universe space galaxies never have a mesh. The circle never draws.

**Why it happens:** GetRenderPosition returns true only for bodies in `_lastRenderPositions` — bodies that were in the current render set during the last SyncBodies call and not visibility-hidden. At 4.2 ly a galaxy-space star mesh IS hidden (radius ~0 render units); galaxy bodies have no entry at all.

**How to avoid (D-50):** Remove guard 3 entirely. Replace the `renderPos` source with a UniMath-derived render-space position:
```csharp
// D-50: position from UniObject via UniMath, not from WorldRenderer mesh set
Double3 relMetres = UniMath.RelativeMetres(ship, targetObj, gameObjects);
double obsFactor = factor / ship.LocalPos.Scale;
Vector3 renderPos = new Vector3(
    (float)(relMetres.X * obsFactor),
    (float)(relMetres.Y * obsFactor),
    (float)(relMetres.Z * obsFactor));
Vector3 globalPos = _worldRenderer.GlobalPosition + renderPos;
```
Size: use `StarRendering.AngularRadius(body.RadiusMeters, dist)` projected to pixels (same formula as Hud.bodyPixelRadius), with MIN_CIRCLE_RADIUS floor. This works uniformly for planets, galaxy-space stars, and sky-only galaxies.

**Warning signs:** Target circle appears for close planets but disappears when targeting distant star in galaxy space; no circle ever appears for galaxy targets.

### Pitfall 7: Antipode ghost in galaxy disc shader

**What goes wrong:** `galaxy_disc_coords` projects EYEDIR onto the plane perpendicular to `galaxy_dir`. This projection is identical for EYEDIR = D and EYEDIR = -D (the antipode), so the galaxy disc renders in TWO locations 180° apart. This was the root cause of commit fef1d91.

**How to avoid:** Always gate structured disc rendering on `d > 0.0` (i.e. `dot(EYEDIR, galaxy_dirs[i]) > 0.0`). The current skybox.gdshader already has this gate. The reworked D-54/D-59 shader must preserve it.

---

## godot-starlight PSF Technique — Concrete Details

### How it works

[CITED: github.com/tiffany352/godot-starlight, tiffnix.com/star-rendering]

1. **MultiMeshInstance3D** with a `QuadMesh` (two triangles) is the batch primitive. One quad per star. `MultiMesh.TransformFormat = Transform3D`, `use_colors = true`, `use_custom_data = true`.

2. **Per-instance data fed at scene setup** (stars don't move relative to each other in godot-starlight's model). For EcoSpace, they must be fed EVERY FRAME because of floating-origin:
   - `SetInstanceTransform(i, Transform3D(Basis.Identity, renderSpacePos))` — the ship-relative render-space Vector3 (see Pitfall 2 for the correct computation).
   - `SetInstanceColor(i, new Color(baseColor.R, baseColor.G, baseColor.B, apparentBrightness))` — alpha channel carries the brightness.
   - `SetInstanceCustomData(i, new Color(luminosity, 0, 0, 0))` — red channel carries luminosity for the shader to use for size scaling.

3. **Vertex shader — billboard + far-plane-clip:** The quad is billboarded to face the camera. POSITION (clip-space) is written directly to bypass the far plane (Pitfall 3 above). Scale is driven by angular size: small for distant stars, growing on approach.

4. **Fragment shader — PSF texture + luminosity_cap floor:**
   - Samples a PSF (Point Spread Function) texture — a radial falloff that mimics the diffraction pattern of a real star in a telescope/eye. For EcoSpace's 8-bit dither aesthetic, a simple Gaussian falloff (exp(-r²/sigma²)) drawn into a texture serves as the PSF kernel.
   - `luminosity_cap`: clamps the per-star brightness to prevent near-star saturation. The cap is applied after the inverse-square scaling: `bright = min(StarRendering.ApparentBrightness(lum, dist), LUMINOSITY_CAP)`.
   - `min_size_ratio`: the minimum fraction of the PSF texture that is shown regardless of distance. At max distance, a star renders at `min_size_ratio × full_psf_size`. This is the "brightness floor" that keeps every star always a visible point. Implemented in vertex as `scale = mix(min_size_ratio, 1.0, scale_from_brightness)`.
   - `emission_energy`: an overall multiplier on the output COLOR. In Godot, `COLOR` from a spatial shader with `blend_add` is composited additively — values > 1.0 naturally feed bloom.

5. **EcoSpace adaptation** — Key differences from godot-starlight:
   - Instance transforms updated EVERY FRAME (floating-origin; stars move relative to ship).
   - Per-instance brightness computed from `StarRendering.ApparentBrightness(body.Luminosity, distMetres)` — identical curve to the skybox star loop (which is being retired).
   - No PSF texture asset needed immediately — start with a procedural Gaussian in the fragment shader (`exp(-r*r*k)` where r = distance from UV center). A texture can be swapped in later for a more realistic diffraction pattern.
   - `visible_instance_count` set to the actual star count each frame (not all 19 objects are stars — filter by `ObjectType == Type.Star`).

### C# integration sketch (StarPointRenderer pattern)

```csharp
// StarPointRenderer.cs — NEW class, pattern mirrors WorldRenderer
namespace Render {
    public partial class StarPointRenderer : Node3D {
        [Export] public NodePath WorldPath { get; set; }
        [Export] public float MinSizeRatio { get; set; } = 0.003f;   // tune in-game
        [Export] public float LuminosityCap { get; set; } = 0.95f;   // tune in-game

        private TestSetup _world;
        private MultiMeshInstance3D _mmi;
        private MultiMesh _mm;
        private int _starCount;
        private int[] _starIndices;  // precomputed once in _Ready

        public override void _Ready() {
            // build _starIndices from _world.GameObjects where ObjectType == Type.Star
            // allocate MultiMesh with instance_count = _starCount
            // _mm.use_colors = true; _mm.use_custom_data = true;
            // assign star_point.gdshader material
        }

        public override void _Process(double delta) {
            var objs = _world.GameObjects;
            var ship = objs[_world.ShipIndex];
            float factor = RenderFactorFor(ship.CurrentSpace);   // same as WorldRenderer
            double scale = ship.LocalPos.Scale;

            for (int i = 0; i < _starIndices.Length; i++) {
                var star = objs[_starIndices[i]];
                // MANDATORY: LCA-relative position (CLAUDE.md §Position Math)
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
        }
    }
}
```

[ASSUMED] — the exact property names (`use_colors`, `use_custom_data`) match Godot 4.6 MultiMesh C# API; verify against the actual GodotSharp binding if property access differs from GDScript.

---

## Additive Mesh Blend (D-53)

The existing `WorldRenderer.RenderBodyAt` already sets `starMat.EmissionEnergyMultiplier` from `StarRendering.ApparentBrightness`. The D-53 additive blend is already present in the standard material's emission behavior — `EmissionEnabled = true` on an unshaded material automatically composites additively with surrounding scene color under HDR + bloom.

The remaining gap for D-53 is that the emission floor drops to near-zero at light-year distances (brightness ≈ 0). The PSF star-point renderer (D-56) is what fixes this — the mesh contribution is the MESH's natural physical brightness as distance falls, and the PSF point is the always-present floor below it. D-53's "continuous blend" is the sum of both:
- Far (galaxy space): PSF point dominates, mesh has r≈0 and brightness≈0 → hidden by visibility.
- Approaching: mesh radius grows, mesh emission grows from `ApparentBrightness`; PSF point still present additively.
- Close (in Star space): mesh fills screen, PSF quad is still present but its contribution is swamped by the mesh.

**The additive blend is therefore: keep the star mesh's existing StandardMaterial3D emission behavior; the PSF renderer adds on top.** No new "additive blend" code is needed inside WorldRenderer itself — the PSF is always present, the mesh is always present, they sum. The floor is set by `min_size_ratio` in the PSF shader.

---

## Galaxy Current-Tier Extension (D-48/D-49)

### TierClassifier change

The cleanest implementation of D-49 (Galaxy-typed bodies always sky):

```csharp
public static SkyTier Classify(UniObject body, UniObject ship) {
    if (body == null || ship == null)               return SkyTier.Skip;
    if (body.Index == ship.Index)                   return SkyTier.Skip;
    if (body.CurrentSpace == UniObject.Space.Root)  return SkyTier.Skip;

    // D-49: Galaxy-typed bodies are ALWAYS sky entities, regardless of tier
    if (body.ObjectType == UniObject.Type.Galaxy)   return SkyTier.NextTierSkybox;

    // (rest of existing logic unchanged)
    if (body.CurrentSpace == ship.CurrentSpace)     return SkyTier.CurrentTierMesh;
    // ... walk upward ...
}
```

This makes TierClassifier return `NextTierSkybox` for galaxies in every tier (including Universe space where the ship and galaxy share the same space). SkyboxRenderer's existing galaxy loop already handles them; WorldRenderer's existing Galaxy guard (`body.ObjectType == UniObject.Type.Galaxy → continue`) remains as a safety backstop.

**Unit test update required:** The 16 existing TierClassifier tests must be augmented with galaxy-in-current-tier cases (e.g., ship in Universe space, galaxy in Universe space → NextTierSkybox, not CurrentTierMesh).

### SkyboxRenderer galaxy filter update

The `SkyboxRenderer.SyncSkyPoints` loop currently has:
```csharp
if (TierClassifier.Classify(body, ship) != SkyTier.NextTierSkybox) continue;
```

With the TierClassifier change, this naturally picks up current-tier galaxies in Universe space without any SkyboxRenderer code change. The home-galaxy suppression guard (`UniMath.FindLca(ship, body, objs) == body.Index`) still correctly fires for the home galaxy while inside it, and does not fire for destination galaxies. No SkyboxRenderer code change is needed beyond the TierClassifier fix.

---

## UniObject-Driven Target Marker (D-50)

### What changes in Hud.UpdateTargetCircle

Current flow (to be replaced):
1. Guard 2: resolve target via BuildTargetableList
2. Guard 3: `_worldRenderer.GetRenderPosition(tgtIdx, out renderPos)` — REMOVE THIS GATE
3. Guard 4: behind-camera check
4. Guard 5: off-screen check
5. Size: `_worldRenderer.GetRenderRadius(tgtIdx, out renderRadius)` — REPLACE WITH UniObject-derived

New flow (D-50):
1. Guard 2: resolve target via BuildTargetableList (unchanged)
2. Compute relMetres via `UniMath.RelativeMetres(ship, targetObj, gameObjects)` — LCA path
3. Convert to render-space Vector3 (same formula as StarPointRenderer)
4. Guard 3: behind-camera check on the render-space position (unchanged logic)
5. Guard 4: off-screen check (unchanged logic)
6. Size: `StarRendering.AngularRadius(targetObj.RadiusMeters, dist)` projected to pixels, clamped to [MIN_CIRCLE_RADIUS, MAX_CIRCLE_RADIUS]

**Dependency needed:** Hud needs access to the render factor and ship.LocalPos.Scale. Currently Hud holds a reference to `_worldRenderer`; it can call a new read-only accessor `WorldRenderer.RenderFactorFor(ship.CurrentSpace)` (currently private — make it `internal` or `public`) and read `ship.LocalPos.Scale` directly from the ship UniObject.

Alternatively, derive render units directly from a NEW `WorldRenderer.GetRenderSpacePosition(ship, targetObj, objs)` helper that encapsulates the formula — keeping Hud's dependency surface small.

**Galaxy target pixel radius:** For sky-only galaxies, `StarRendering.AngularRadius(Galaxy_RadiusMeters=5e20, dist)` at intergalactic distance (~2.4e22 m) = 5e20/2.4e22 ≈ 0.021 rad ≈ 1.2° — about 18 pixels at 1080p/75° FOV. This is visible and sensible. The MIN_CIRCLE_RADIUS floor (currently 6f) catches sub-pixel cases.

---

## Star Nest Volumetric Look for Galaxy Disc (D-54)

### What Star Nest does

[ASSUMED — trained knowledge of Star Nest shader by Pablo Roman Andrioli, ShaderToy #XlfGRj]

Star Nest uses iterative fractal folding (`p = abs(p)/dot(p,p) - formuparam`) applied in 3D volumetric ray steps (`volsteps` iterations along the eye ray). Each step accumulates a brightness contribution from the folded coordinate space. The result is a field of glowing fractal "stars" with depth cueing — deeper steps are dimmer, creating a volumetric nebula/galaxy look.

Key parameters:
- `iterations`: recursion depth (typically 17) — higher = more detail, more expensive
- `volsteps`: ray march depth (typically 10) — controls depth of field
- `formuparam`: the magic "galaxy shape" constant (~0.53) — small changes dramatically alter the look
- `brightness`, `darkmatter`, `distfading`: visual tuning
- `coloramp` (Godot port): optional gradient texture for coloring by density

### ShaderToy→Godot sky shader porting

[ASSUMED — general porting knowledge; partially CITED from godotengine.org/article/custom-sky-shaders-godot-4-0/]

Key differences:
1. **Entry point:** ShaderToy `void mainImage(out vec4 O, vec2 I)` → Godot `void sky()` with no explicit I/O parameters; use `COLOR` for output.
2. **fragCoord:** ShaderToy uses `fragCoord.xy / iResolution.xy` for screen UV. Godot sky shaders use `EYEDIR` (normalized world-space direction) — for a volumetric shader like Star Nest, the ray origin is implicitly the camera; the ray direction IS `EYEDIR`. No explicit screen UV needed — just initialize the ray marching with `vec3 ray = EYEDIR`.
3. **iTime → TIME:** Direct substitution.
4. **iResolution → SCREEN_SIZE_PIXELS or VIEWPORT_SIZE:** For effects that depend on resolution ratio.
5. **The RADIANCE sampler:** DO NOT sample RADIANCE in skybox.gdshader (existing comment in the shader — Godot 4.6 regression #115441/#115599). The Star Nest shader does not use RADIANCE; no issue.
6. **GLSL version:** Godot 4 uses GLSL 4.50 core; ShaderToy uses GLSL ES 3.00. Most Star Nest code is compatible; avoid texture2D (use texture()), avoid gl_FragCoord.

### What to port vs what to keep

**Keep the existing procedural spiral and elliptical functions** (`spiral_galaxy`, `elliptical_galaxy`, `galaxy_disc_coords`) — they are proven bug-free (fef1d91). D-54's "Star-Nest-inspired volumetric look" does NOT require replacing the whole galaxy shader with a full ray-march.

**The practical D-54 implementation:** Add a Star-Nest-influenced layered fractal brightness overlay on top of the existing disc functions:
```glsl
// Within the galaxy disc loop, after computing disc UV:
float star_nest_brightness = 0.0;
vec3 p = vec3(uv * 0.5, 0.0);
for (int j = 0; j < 4; j++) {   // low iteration count for performance
    p = abs(p) / dot(p, p) - 0.53;
    star_nest_brightness += pow(max(length(p) - 0.5, 0.0), -2.0) * 0.01;
}
star_nest_brightness = clamp(star_nest_brightness, 0.0, 1.0);
// Blend: existing disc look + star_nest detail layer
disc_bright = mix(disc_bright, disc_bright + star_nest_brightness * 0.4, detail_blend);
```

This adds fractal grain / "star-field within the disc" without requiring the full ray-march volume. It respects the "NO true nebula" boundary of D-54. The iteration count (4) keeps per-pixel cost low in the already-complex sky shader.

**License note:** ShaderToy shaders default to CC BY-NC-SA 3.0 unless the author specifies otherwise. For this minimal "inspired by" derivative (a few lines of math, not a copy of the shader), no attribution issue arises — the folding formula `abs(p)/dot(p,p) - c` is a well-known mathematical construct (not copyrightable by itself). If the full Star Nest code were copied verbatim, CC BY-NC-SA attribution would be required. [ASSUMED — not legal advice]

---

## Galaxy Disc Tilt — D-59 Safe Basis

### The exact math (safe approach)

The existing `galaxy_disc_coords` builds a face-on tangent basis from `galaxy_dir`:
```glsl
vec3 up0 = abs(galaxy_dir.y) < 0.99 ? vec3(0,1,0) : vec3(1,0,0);
vec3 t1  = normalize(cross(up0, galaxy_dir));
vec3 t2  = cross(galaxy_dir, t1);
vec2 uv  = vec2(dot(delta, t1), dot(delta, t2));
```

To add D-59 tilt (foreshortening from `disc_normal`), extend this function:
```glsl
vec2 galaxy_disc_coords_tilted(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    // 1. Stable face-on basis (from galaxy_dir — NEVER from disc_normal; see fef1d91)
    vec3 up0 = abs(galaxy_dir.y) < 0.99 ? vec3(0.0,1.0,0.0) : vec3(1.0,0.0,0.0);
    vec3 t1  = normalize(cross(up0, galaxy_dir));
    vec3 t2  = cross(galaxy_dir, t1);
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    vec2 uv = vec2(dot(delta, t1), dot(delta, t2));

    // 2. Tilt: foreshorten along the disc_normal's in-plane projection
    // disc_normal projected onto the face-on plane = its t1/t2 components
    vec2 disc_normal_uv = vec2(dot(disc_normal, t1), dot(disc_normal, t2));
    float disc_normal_len = length(disc_normal_uv);
    if (disc_normal_len > 0.001) {
        // The minor axis of the tilted ellipse is along disc_normal's in-plane direction
        vec2 minor_axis = disc_normal_uv / disc_normal_len;
        // tilt_factor = |dot(disc_normal, galaxy_dir)| = cos(tilt angle from face-on)
        // When = 1: disc_normal||galaxy_dir → face-on (no foreshortening)
        // When = 0: disc_normal⊥galaxy_dir → edge-on (fully foreshortened)
        float tilt_factor = abs(dot(disc_normal, galaxy_dir));
        float safe_tilt = clamp(tilt_factor, TILT_FLOOR, 1.0);  // TILT_FLOOR e.g. 0.15
        // Project uv onto minor axis, compress by safe_tilt
        float minor_comp = dot(uv, minor_axis);
        float major_comp_proj = dot(uv, vec2(-minor_axis.y, minor_axis.x));
        // Reconstruct foreshortened UV
        uv = minor_axis * minor_comp * safe_tilt
           + vec2(-minor_axis.y, minor_axis.x) * major_comp_proj;
    }
    return uv;
}
```

`TILT_FLOOR` is in Claude's Discretion (tune in-game). A floor of 0.10–0.20 prevents the ring collapse while still allowing visible foreshortening.

The caller passes `galaxy_orientations[i].xyz` as `disc_normal` (currently unused in the shader, but already pushed by SkyboxRenderer from `body.GalaxyOrientation`). **No C# data change required** — the orientation data is already there.

---

## Proximity Glow Halo (D-55)

### Approach

[ASSUMED — Godot 4.6 WorldEnvironment bloom behavior]

The existing `WorldEnvironment` in `Main.tscn` already has glow/bloom enabled (the star mesh already blooms from its `StandardMaterial3D` emission). D-55's "proximity glow halo" is an additional additive contribution that grows as true distance shrinks.

**Simplest implementation compatible with existing pipeline:** The PSF star-point shader already draws an additive quad around each star. The "glow halo" is the outer wings of the PSF — a larger, dimmer Gaussian that extends beyond the core point. The quad can be sized larger than the core PSF:
- Core: `min_size_ratio` floor — tiny bright core point.
- Halo wings: a second, larger scale factor applied to the OUTER Gaussian component — dimmer falloff that extends further.

Both are in the SAME shader pass with `blend_add` — the combined result naturally feeds WorldEnvironment bloom (COLOR > 1.0 → bloom).

**Distance-driven halo intensity:** In the vertex shader, compute halo scale from `INSTANCE_CUSTOM.x` (luminosity) and camera distance, applying the same inverse-square law as `StarRendering.ApparentBrightness` but with a lower contrast curve (so it grows visibly but doesn't saturate too quickly):
```glsl
// In fragment, after sampling PSF core:
float halo_r = uv_distance * 3.0;   // halo radius normalized to [0..1] beyond core
float halo = exp(-halo_r * halo_r * 0.5) * bright * HALO_SCALE;  // HALO_SCALE [Export]
COLOR = vec4((core_color + halo_color) * emission_energy, 1.0);
```

This avoids a second render pass and reuses the existing bloom pipeline.

---

## State of the Art

| Old Approach | Current Approach | Phase 5 Change |
|--------------|------------------|----------------|
| Star mesh only (invisible sub-pixel at light-year) | Mesh emission from StarRendering | Always-on PSF point PLUS mesh (D-53/D-56) |
| Skybox star loop in skybox.gdshader | SkyboxRenderer pushes star_dirs[] | RETIRED; replaced by MultiMesh PSF (D-56) |
| Galaxy only in next-tier-out | Galaxy only as NextTierSkybox | Galaxy ALWAYS sky (D-49); current-tier galaxies now visible in Universe space (D-48) |
| Target circle gated on mesh render set | Circle only for mesh-visible bodies | Circle decoupled; UniObject-driven for all targets (D-50) |
| Face-on galaxy discs (tilt dropped in fef1d91) | Face-on only (safe but inaccurate) | Safe-basis tilt reinstated with TILT_FLOOR clamp (D-59) |
| No proximity brightening | No specific proximity effect | Distance-driven PSF halo feeds existing bloom (D-55) |

**Deprecated/outdated in this phase:**
- `skybox.gdshader` star loop (`for (int i = 0; i < star_count; i++)`) — REMOVED (D-56); star_dirs/star_colors/star_sizes uniforms and star_count become dead code and should be removed.
- `SkyboxRenderer._dirs`, `_colors`, `_sizes` arrays and their `SetShaderParameter` calls — REMOVED alongside the star loop.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `MultiMesh.use_colors` and `MultiMesh.use_custom_data` are the correct C# property names in GodotSharp 4.6.2 | Standard Stack / C# integration sketch | Wrong: property may be named differently; breaks compilation. Verify by inspecting GodotSharp bindings in the project. |
| A2 | `INSTANCE_CUSTOM` in the spatial shader receives data from `MultiMesh.SetInstanceCustomData` via the `.x` channel | PSF Technique / C# integration | Wrong: channel mapping may differ; luminosity silently reads wrong data. Verify with a test quad in-editor. |
| A3 | Star Nest's iterative folding formula `abs(p)/dot(p,p) - formuparam` is implementable in the Godot sky shader without exceeding per-fragment cost budget at 60fps | Galaxy disc volumetric look | Wrong: too expensive for real-time; solution is to reduce iteration count or use only the face-on disc functions. |
| A4 | The `OUTPUT_IS_SRGB` built-in is available in Godot 4.6 spatial shaders for differentiating Forward+ from Compatibility renderer | Far-plane clip trick / Pitfall 3 | Wrong: may be named differently in 4.6.2 or may not exist; fallback is to always set `POSITION.z = 0.0 * projected.w`. |
| A5 | ShaderToy Star Nest's visual character (volumetric fractal grain) is achievable with low iteration count (4 iterations) in-disc and looks good with the existing dithering post-process | D-54 volumetric look | Wrong: may look too subtle or too noisy; solution is play-test tuning (D-58 discipline). |
| A6 | `WorldEnvironment` glow settings in Main.tscn are already configured to produce visible bloom at the emission levels the star mesh currently uses | D-55 proximity glow | Wrong: bloom thresholds may need tuning; check Main.tscn glow settings before assuming they work for the PSF quad. |
| A7 | `RenderFactorFor` in WorldRenderer can be made accessible (internal/public) to Hud for the D-50 render-space conversion, without breaking the read-only consumer contract | Hud D-50 | Wrong: may need a dedicated accessor method. Low risk — trivial to add. |

---

## Open Questions

1. **Should `StarPointRenderer` update all instance transforms every frame, or only dirty stars?**
   - What we know: the ship moves every frame (floating-origin shifts all relative positions); all star positions change relative to the ship.
   - What's unclear: whether per-frame SetInstanceTransform on all N star instances causes visible CPU overhead. EcoSpace has ~10 stars total — negligible. For a future 100k-star scene this would be a bottleneck.
   - Recommendation: update all instances every frame (simplest; no star count issue at current scale).

2. **Does `visible_instance_count = -1` (all) or the actual count need to be set?**
   - What we know: `visible_instance_count = -1` means "all instances" in Godot. If `instance_count = 10` (all stars), `-1` is fine.
   - Recommendation: set to actual star count; use `visible_instance_count` for any runtime filter (e.g. excluding stars with brightness below a threshold).

3. **Should the skybox star loop removal break any existing tests?**
   - What we know: no tests directly test the skybox shader (it runs on the GPU). The SkyboxRenderer C# code that pushes `star_dirs[]`, `star_colors[]`, `star_sizes[]`, `star_count` will become dead code.
   - Recommendation: remove the SkyboxRenderer star push code alongside the shader loop. No test impact expected (only galaxy uniforms remain).

4. **What is the exact render_mode for the PSF star quad shader?**
   - At minimum: `blend_add, unshaded, depth_test_disabled`
   - Optionally: `no_depth_prepass` — may improve performance when there are many additive overlapping quads.
   - Recommendation: start with `blend_add, unshaded, depth_test_disabled` and add `no_depth_prepass` if GPU load is visible in the profiler.

---

## Environment Availability

Step 2.6: SKIPPED — Phase 5 is purely code/shader changes. No external CLIs, databases, or services required beyond the existing Godot 4.6.2 + .NET 8 build environment already proven operational in prior phases.

---

## Security Domain

> `security_enforcement: true` in config.json; ASVS level 1.

Phase 5 is a rendering/visual phase. There are no network calls, authentication flows, user inputs beyond keyboard/mouse (existing), or data persistence changes. The applicable ASVS categories and their verdicts:

| ASVS Category | Applies | Assessment |
|---------------|---------|------------|
| V2 Authentication | No | No auth in this phase |
| V3 Session Management | No | No session state |
| V4 Access Control | No | No access-controlled resources |
| V5 Input Validation | Minimal | New [Export] float knobs (MinSizeRatio, LuminosityCap, TiltFloor) on StarPointRenderer — these are editor-only properties read at startup, not user-supplied runtime inputs. No validation required beyond Godot's own export system. |
| V6 Cryptography | No | No cryptographic operations |
| V7 Error Handling | Low | Null guards follow existing `(uint)i < (uint)count` pattern; MultiMesh SetInstance* does not throw on out-of-range in Godot (silent no-op). Maintain existing defensive patterns. |
| V9 Data Communications | No | No network I/O |

**Known threat patterns for rendering shaders:** None applicable to EcoSpace's offline single-player context. The shader GLSL code is compiled by the Godot engine at runtime — shader injection is not a concern in a single-player desktop game (no remote shader loading).

**One caution:** The `TILT_FLOOR` clamp value in D-59 must remain > 0 to prevent a division-by-zero in the foreshortening math. This is a stability concern, not a security concern, and is addressed by the `clamp(tilt_factor, TILT_FLOOR, 1.0)` pattern where `TILT_FLOOR` is an [Export] with a minimum of ~0.05.

---

## Sources

### Primary (code-verified, HIGH from direct codebase read)
- `Scripts/Render/WorldRenderer.cs` — `RenderBodyAt`, `GetRenderPosition`, `GetRenderRadius`, `SyncBodies`, `ComputeStarRenderPosFromHierarchy` — full source read
- `Scripts/Render/StarRendering.cs` — `ApparentBrightness`, `AngularRadius`, `Exposure` — full source read
- `Scripts/Render/SkyboxRenderer.cs` — `SyncSkyPoints`, galaxy loop, star loop, `GetSkyDirection`, galaxy uniform push — full source read
- `Scripts/Hud/Hud.cs` — `UpdateTargetCircle`, `UpdateDirectionMarker`, `BuildTargetableList`, `_Draw` — full source read
- `Scripts/Math/UniMath.cs` — `FindLca`, `ToAncestorFrame`, `RelativePosition`, `RelativeMetres`, `Distance` — full source read
- `Scripts/UniObject.cs` — `Type`, `Space`, `Scale`, field inventory — full source read
- `Scripts/TierClassifier.cs` — `Classify` logic — full source read
- `Scripts/TestSetup.cs` — authored galaxy/star data, `GalaxyOrientation` values — full source read
- `Shaders/skybox.gdshader` — full shader source read; galaxy disc coords, star loop, galaxy loop, anti-antipode gate
- `.planning/debug/galaxy-sky-disc-antipode.md` — root-cause analysis of ring collapse + fef1d91 fix
- `.planning/todos/pending/galaxy-disc-tilt-foreshortening.md` — tech-debt description and safe-basis approach sketch

### Secondary (MEDIUM — official Godot documentation, MIT license confirmation)
- [CITED: github.com/tiffany352/godot-starlight/blob/main/LICENSE.md] — MIT license confirmed, Copyright 2023 Tiffany Bennett
- [CITED: github.com/tiffany352/godot-starlight (Star.gdshader)] — PSF technique: min_size_ratio, luminosity_cap, blend_add, far-plane POSITION.z trick, per-instance color/custom data
- [CITED: github.com/tiffany352/godot-starlight (StarManager.gd)] — MultiMesh setup: use_colors=true, use_custom_data=true, SetInstanceTransform, SetInstanceColor, SetInstanceCustomData
- [CITED: tiffnix.com/star-rendering] — PSF texture cropping by brightness (inverse square), color from effective temperature
- [CITED: docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html] — blend_add render mode, POSITION built-in, INSTANCE_CUSTOM built-in, unshaded mode, depth_test_disabled
- [CITED: docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html] — EYEDIR world-space, TIME, RADIANCE regression caveat, multi-pass support
- [CITED: docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html] — instance_count vs visible_instance_count, SetInstanceTransform C# API
- [CITED: godotshaders.com/shader/star-nest-2/] — Star Nest Godot port: canvas_item type, iterations/volsteps parameters, volumetric ray march technique

### Tertiary (LOW — search results / training knowledge)
- Star Nest shader (ShaderToy XlfGRj, Pablo Roman Andrioli) — visual reference; full source not fetched (HTTP 403); technique description from training knowledge [ASSUMED]
- Defold ShaderToy porting guide (general GLSL porting patterns) [ASSUMED from training]
- godotengine.org/article/custom-sky-shaders-godot-4-0/ — EYEDIR is world-space, not camera-relative [CITED from WebSearch result]

---

## Metadata

**Confidence breakdown:**
- Standard stack (existing code): HIGH — direct source read of all files
- godot-starlight license: HIGH — fetched LICENSE.md directly
- godot-starlight technique (PSF shader details): MEDIUM — fetched Star.gdshader and StarManager.gd
- Architecture patterns (TierClassifier/SkyboxRenderer extension): HIGH — full source read + direct extension design
- D-59 tilt math: MEDIUM — derived from understanding of the existing galaxy_disc_coords + the tech-debt approach sketch
- Star Nest volumetric look: LOW-MEDIUM — Godot port confirmed on godotshaders.com; full ShaderToy source not accessible (403)
- Pitfalls: HIGH — most are documented bugs from prior phases (fef1d91, 83dfce4, 04-02 SC#5)

**Research date:** 2026-06-18
**Valid until:** 2026-07-18 (stable Godot 4.x APIs; shader techniques are stable)
