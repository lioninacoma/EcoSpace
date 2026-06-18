// star_point.gdshader
// PSF star-point renderer for EcoSpace — additive quad batch for all stars at all tiers
// (D-56). Replaces the skybox star loop (retired in Phase 5).
//
// Technique borrowed from tiffany352/godot-starlight (Copyright 2023 Tiffany Bennett, MIT
// License). Custom-fit to EcoSpace: per-frame floating-origin positions via UniMath LCA
// path, per-instance brightness from StarRendering.ApparentBrightness, per-instance colour
// from UniObject.BaseColor (D-57).
//
// Render mode rationale:
//   blend_add       — additive compositing; star light accumulates rather than occludes (D-53)
//   unshaded        — no Godot lighting; emission is the whole contribution (EMISSION channel)
//   depth_test_disabled — disables depth-buffer test so the far-plane-clip trick can place
//                     the quad at the far plane (Pitfall 3 / Research.md lines 224-245)
//   cull_disabled   — a billboard quad written directly in clip space can land with either
//                     winding (and the sign of projected.w flips it); without this, default
//                     back-face culling silently discards the front-facing star quads.
//
// Far-plane-clip trick (Pitfall 3):
//   In vertex() we write POSITION (clip-space) directly. Setting POSITION.z ≈ ±projected.w
//   places the primitive just inside the far plane, preventing frustum culling of quads at
//   light-year render-space distances. depth_test_disabled ensures the depth buffer does not
//   reject it either. In Forward+ the convention is POSITION.z = -1.0 * projected.w;
//   the OUTPUT_IS_SRGB branch differentiates Forward+ from Compatibility.
//
// Per-instance data packing (C# side — StarPointRenderer.cs):
//   SetInstanceColor(i, new Color(R, G, B, brightness))
//     → COLOR.rgb = star BaseColor, COLOR.a = ApparentBrightness (capped by LuminosityCap)
//   SetInstanceCustomData(i, new Color(luminosity, 0, 0, 0))
//     → INSTANCE_CUSTOM.x = raw luminosity (available for future shader use)
//
// Bloom (D-55): EMISSION values > 1.0 feed WorldEnvironment glow automatically (same as
// the star sphere mesh). Set EmissionEnergy > 1 in the StarPointRenderer to scale brightness.

shader_type spatial;
render_mode blend_add, unshaded, depth_test_disabled, cull_disabled;

// ── Tuning knobs (set from StarPointRenderer [Export] values via ShaderMaterial) ────────

/// Minimum on-screen quad half-size, as a fraction of projected.w.
/// Controls the "brightness floor" — even the dimmest star at max range stays
/// at least this large. Tune in-game via StarPointRenderer.MinSizeRatio.
uniform float min_size_ratio : hint_range(0.0, 0.1) = 0.003;

/// Maximum per-star brightness (0–1). Prevents near-star saturation to white
/// and keeps BaseColor hue intact. Tune via StarPointRenderer.LuminosityCap.
uniform float luminosity_cap : hint_range(0.0, 2.0) = 0.95;

/// Overall emission multiplier. Values > 1.0 drive WorldEnvironment bloom (D-55).
/// Tune via StarPointRenderer.EmissionEnergy.
uniform float emission_energy : hint_range(0.0, 10.0) = 1.0;

/// Halo wing width multiplier (D-55). Controls the outer Gaussian's amplitude
/// relative to the core. Wider halo → more bloom feed. Tune via StarPointRenderer.HaloScale.
uniform float halo_scale : hint_range(0.0, 2.0) = 0.3;

// ── Vertex shader ────────────────────────────────────────────────────────────────────────

void vertex() {
    // Extract world-space position from the instance transform (MODEL_MATRIX column 3).
    // StarPointRenderer sets SetInstanceTransform to a ship-relative floating-origin
    // position; this is the correct render-space Vector3 for this star.
    vec3 world_pos = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;

    // Project to clip space.
    vec4 projected = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world_pos, 1.0);

    // Behind-camera guard (MANDATORY for a floating-origin starfield):
    // when the star is behind the camera projected.w <= 0, and the billboard math below
    // (POSITION.xy += VERTEX.xy * projected.w) mirrors the quad through the origin — a star
    // behind-left ghosts to front-right ("star in the wrong direction"). Push such quads
    // fully outside the clip volume so they are culled instead of ghosted. depth_test_disabled
    // does not cull these on its own, so the clip must be explicit here.
    if (projected.w <= 0.0) {
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);  // outside [-w, w] on every axis → clipped
    } else {
        // Write POSITION directly (bypasses the normal ModelView transform pipeline).
        POSITION = projected;

        // Depth-clamp trick (Pitfall 3): a star at light-year render distance has a natural
        // NDC z far beyond the far plane, so the rasterizer frustum-clips its quad. Override z
        // to the MIDDLE of the clip volume (NDC z = 0.5) so it always survives clipping,
        // independent of the depth convention (Vulkan/Forward+ uses [0,1]; setting z = -w gave
        // NDC z = -1, which is OUTSIDE [0,1] and silently discarded every front-facing star).
        // depth_test_disabled means the exact depth is irrelevant — only surviving the clip is.
        POSITION.z = projected.w * 0.5;

        // Billboard scaling in clip space.
        // VERTEX.xy is in [-0.5, 0.5] (QuadMesh default) — the quad faces the camera already
        // because we wrote POSITION from the world origin of the instance.
        // Scale: mix between min_size_ratio (distance floor) and 1.0, weighted by brightness.
        // Multiply by projected.w to convert from NDC scale to clip-space scale.
        float bright = COLOR.a;  // per-instance brightness from SetInstanceColor alpha
        float quad_scale = mix(min_size_ratio, 1.0, bright);
        POSITION.xy += VERTEX.xy * quad_scale * projected.w;
    }
}

// ── Fragment shader ──────────────────────────────────────────────────────────────────────

void fragment() {
    // UV is [0,1] over the quad; centre at (0.5, 0.5).
    vec2 center_uv = UV - vec2(0.5);
    // r in [0, ~0.7] (corner of unit quad); normalise so r = 1 at the quad edge.
    float r = length(center_uv) * 2.0;

    // Per-instance brightness packed into COLOR.a by StarPointRenderer.SetInstanceColor.
    float bright = COLOR.a;

    // PSF core: tight Gaussian — mimics the diffraction/seeing disc of a point source.
    float core = exp(-r * r * 8.0) * bright;

    // Halo wings (D-55): wider, dimmer Gaussian — extends beyond the core and feeds
    // WorldEnvironment bloom naturally (additive emission > 1.0 → bloom).
    float halo_r = r * 3.0;
    float halo = exp(-halo_r * halo_r * 0.5) * bright * halo_scale;

    // Combine and cap to prevent over-saturation (luminosity_cap).
    float total = min(core + halo, luminosity_cap);

    // Additive PSF: ALL contribution MUST come through EMISSION, which carries the Gaussian
    // falloff. In unshaded mode the final colour is ALBEDO + EMISSION, so a non-zero ALBEDO
    // floods the ENTIRE quad with COLOR.rgb at full intensity — that is what rendered the stars
    // as solid white squares. Keep ALBEDO black; EMISSION alone draws the soft point.
    ALBEDO = vec3(0.0);
    EMISSION = COLOR.rgb * total * emission_energy;
    ALPHA = 1.0;
}
