// luminous_pass.gdshader
// Depth-aware spatial quad pass for EcoSpace — draws near-star PSF lens flare only.
// Galaxies are rendered entirely in skybox.gdshader (D-13 / Plan 3 narrowing).
//
// TECHNIQUE — "advanced post-processing" pattern (Godot docs):
//   Camera3D-child Node3D hosts a 2×2 QuadMesh with FlipFaces=true.
//   Vertex stage writes POSITION = vec4(VERTEX.xy, 1.0, 1.0) to bypass all transforms and
//   keep the quad in front of every pixel regardless of camera movement.
//   This is the ONLY valid way to read hint_depth_texture in Forward+ (Pitfall 1 — canvas_item
//   shaders cannot bind the depth buffer, Godot bug #74464).
//
// DEPTH OCCLUSION (D-03 / Pitfall 5 / Play-test feedback — Iteration 2):
//   Godot 4.3+ Forward+ uses reversed-Z: far plane = 0.0, near plane = 1.0.
//   raw_depth ≈ 0 → empty sky pixel.  raw_depth > ε → foreground geometry present.
//
//   Per-pixel depth gate (REVISED — Iteration 2):
//   Instead of hand-computing star depth as distMeters * RenderFactor (which uses mismatched
//   units and only happened to work when very close to a planet), the star's scene depth is
//   now read directly from the depth texture sampled at the star's projected screen UV
//   (star_uv). Both sides of the depth comparison now come from the same source (depth_texture
//   linearized the same way), so occlusion is correct at ALL camera distances.
//
//   Algorithm per PSF-contributing star:
//     1. Project star_dirs[i] to star_uv (already done for screen-space PSF centering).
//     2. Skip the star's PSF entirely if star_uv is outside [0,1] (off-screen / behind camera).
//     3. Sample depth_texture at star_uv → star_raw_depth.  Linearize to star_lin_depth.
//        This is the true depth of whatever geometry is at the star's center pixel:
//          - Star's own mesh when near → star_lin_depth ≈ actual star surface depth.
//          - A nearer planet occluding the star → star_lin_depth is that planet's depth
//            → the whole PSF is suppressed because the star is hidden.  (Correct!)
//          - Empty sky → star_raw_depth ≈ 0 (sky pixel) → treated as infinite depth → PSF allowed.
//     4. Per-pixel depth gate: allow PSF if the current pixel is AT or BEYOND the star depth:
//          depth_ok = sky_pixel || (lin_depth >= star_lin_depth - psf_depth_epsilon)
//        psf_depth_epsilon is a small ADDITIVE tolerance in linearized view-space units so a
//        thin tolerance avoids z-fight shimmer at the star-mesh surface.
//
//   PERF GUARD: the star-center depth sample (step 3) is only executed for stars whose
//   psf_lod > 0.001 (the early-continue threshold).  Far stars (lod≈0) early-continue
//   before any extra depth texture read.
//
// WORLD VIEW RAY RECONSTRUCTION (Open Question 1 — RESOLVED):
//   EYEDIR is NOT available in shader_type spatial fragments — it is a built-in exclusive to
//   shader_type sky.  Using EYEDIR in a spatial shader causes a compile error at runtime.
//   Resolution: reconstruct the per-fragment world-space view ray from screen coordinates.
//
//   Method (chosen for reliability on a screen-filling bypass quad):
//     1. Build NDC from SCREEN_UV and raw_depth (already available).
//     2. Unproject to view space via INV_PROJECTION_MATRIX.
//     3. Rotate to world space via INV_VIEW_MATRIX with w=0 (direction, not position).
//   This gives a world-space direction FROM the camera THROUGH the fragment — the same
//   semantic as EYEDIR in a sky shader.  Computed once per fragment and stored in
//   `world_view_dir` for all subsequent dot-product tests.
//
// SCREEN-SPACE PSF (Aperture Diffraction — Play-test feedback):
//   Replaces the simple smoothstep halo with a physically-inspired lens point-spread function:
//     Core:   spectrum / (d*d*d) where d = length(delta_px * psf_core_scale)
//     Spike H: spectrum / (d*d*d) where d = length(delta_px * vec2(spike_long, spike_short))
//     Spike V: spectrum / (d*d*d) where d = length(delta_px * vec2(spike_short, spike_long))
//   delta_px is the screen-space pixel offset from the star's projected position,
//   corrected for aspect ratio. This matches the ShaderToy reference pattern requested
//   in the play-test feedback.
//
//   Star projected position: transform star_dirs[i] through VIEW_MATRIX + PROJECTION_MATRIX
//   to get clip-space XY, convert to screen UV. Requires the star to be in front of the camera
//   (dot(world_view_dir, star_dirs[i]) > 0 guard).
//
// LOD REWORK (Play-test feedback — Iteration 2):
//   Old: lod_fade = 1 - lod_weight made PSF vanish when near (backwards for lens flare).
//   New: PSF intensity scales WITH lod_weight so the lens flare is strongest when the star
//   is near and its mesh is dominant. The sky shader's point disc handles the far-field.
//   A minimum PSF floor keeps some lens flare visible even at mid-range (MID stage of D-11).
//
//   Iteration 2 improvement: the smoothstep ramp width is now controlled by psf_lod_range
//   (default 0.6) instead of the hardcoded 0.2. This makes the PSF ease in gradually over
//   a much longer portion of the approach (smoothstep over [psf_lod_floor, psf_lod_floor+0.6])
//   instead of snapping on over a narrow 0.2-wide window.  psf_lod_floor remains tunable so
//   the very first hint appears early but faint, then grows smoothly.
//
// LOD FADE + DEPTH GATE BLEND (D-11 / D-03 / RND-07):
//   Far stars  (lod_weight≈0): PSF is very faint; sky_pixel gate prevents bleed over geometry.
//   Near stars (lod_weight→1): PSF is strong; depth gate allows PSF over the star mesh surface
//                               but suppresses it for foreground objects (planets, etc.).
//
// GALAXIES REMOVED (D-13 / Plan 3):
//   Galaxy rendering belongs entirely to the sky shader (shader_type sky renders at infinite
//   depth behind all geometry automatically).  All galaxy uniforms, helper functions, and the
//   galaxy loop have been removed from this pass.  LuminousPassRenderer no longer pushes
//   galaxy uniforms.
//
// DO NOT add hint_depth_texture to dithering.gdshader (canvas_item) — Pitfall 1 / bug #74464.

shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never;

// ── Depth + screen texture ─────────────────────────────────────────────────────
uniform sampler2D depth_texture  : hint_depth_texture;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

// ── Star uniforms ─────────────────────────────────────────────────────────────
const int MAX_STARS = 128;

/// World-space unit directions ship→star. Same EYEDIR frame as skybox.gdshader star_dirs.
uniform vec3  star_dirs[MAX_STARS];

/// RGB = BaseColor, A = ApparentBrightness (same packing as skybox.gdshader star_colors).
uniform vec4  star_colors[MAX_STARS];

/// Angular disc half-size in (1 - cos θ) smoothstep space, same as skybox.gdshader star_sizes.
uniform float star_sizes[MAX_STARS];

/// LOD weight from LuminousLod.StarMeshWeight: 0 = far/point-dominant, 1 = near/mesh-dominant.
/// PSF intensity scales WITH lod_weight (strongest when near — lens flare is a near-star effect).
uniform float star_lod_weights[MAX_STARS];

/// Number of valid entries in the star arrays above.
uniform int   star_count = 0;

// ── PSF tuning uniforms (pushed from LuminousPassRenderer [Export] knobs) ─────

/// Scale applied to the screen-space pixel delta for the PSF core falloff.
/// Larger value → smaller, tighter core. [ASSUMED D-04 play-test knob — default 80.0]
uniform float psf_core_scale       = 80.0;

/// Long-axis scale for diffraction spikes (applied to one screen axis).
/// Larger → narrower spike in the perpendicular direction. [ASSUMED D-04 knob — default 12.0]
uniform float psf_spike_long_scale = 12.0;

/// Short-axis scale for diffraction spikes (the elongated axis of each spike).
/// Smaller → fatter spike along the spike direction. [ASSUMED D-04 knob — default 0.25]
uniform float psf_spike_short_scale = 0.25;

/// Overall PSF intensity multiplier. Scales the additive contribution to screen color.
/// [ASSUMED D-04 play-test knob — default 1.0]
uniform float psf_intensity         = 1.0;

/// Minimum LOD weight below which PSF contribution fades to zero (start of ramp).
/// The first hint of PSF appears here; the ramp then grows over psf_lod_range.
/// Keeps far-field sky-shader points from acquiring any PSF contribution.
/// [D-04 play-test knob — default 0.02]
uniform float psf_lod_floor         = 0.02;

/// Width of the LOD ramp over which PSF grows from zero to full intensity.
/// smoothstep(psf_lod_floor, psf_lod_floor + psf_lod_range, lod).
/// Larger value = gentler fade-in over a longer approach (less pop).
/// Default 0.6 gives a smooth ease-in across most of the near-approach range.
/// [D-04 play-test knob — Iteration 2 — default 0.6]
uniform float psf_lod_range         = 0.6;

/// Additive depth tolerance in linearized view-space units for the per-pixel depth gate.
/// PSF is allowed at pixels where pixel_lin_depth >= star_lin_depth - psf_depth_epsilon.
/// A small positive value (e.g. 0.05) avoids z-fight shimmer at the star mesh surface
/// without over-permitting PSF through foreground geometry.
/// (Iteration 2: was a multiplicative factor on a hand-computed star depth; now an additive
///  offset on the texture-sampled star depth, so units match at all camera ranges.)
/// [D-04 play-test knob — Iteration 2 — default 0.05]
uniform float psf_depth_epsilon     = 0.05;

// ── Vertex stage ───────────────────────────────────────────────────────────────

void vertex() {
    // Bypass all transforms — write directly to clip space (Pitfall 2 / advanced post-process).
    // VERTEX.xy on a 2×2 QuadMesh is in [-1,1] NDC; w=1.0 places this at the far plane depth
    // so it does not clip.  This keeps the quad covering the full screen regardless of camera.
    POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// ── Fragment stage ─────────────────────────────────────────────────────────────

void fragment() {
    vec3 col = vec3(0.0);

    // ── Depth sample (scale-independent sky-pixel gate — Pitfall 5) ───────────
    // Forward+ reversed-Z: far plane = 0.0, near plane = 1.0.
    // raw_depth < 1e-6 → far plane → empty sky pixel → glow always allowed.
    // raw_depth > 1e-6 → foreground geometry present → use per-star depth gate.
    float raw_depth = texture(depth_texture, SCREEN_UV).x;
    bool  sky_pixel = (raw_depth < 1e-6);

    // Unproject fragment to view space for lin_depth AND world view ray.
    // "render units" = metres × per-space render factor (1e-8 for star/galaxy space).
    vec3  ndc       = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
    vec4  view      = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    view.xyz       /= view.w;
    float lin_depth = -view.z;   // positive = distance from camera in render units

    // ── World-space view direction (EYEDIR replacement — Open Question 1 resolved) ──
    // EYEDIR is only available in shader_type sky; in shader_type spatial we reconstruct
    // the world-space direction from camera through the fragment using INV_VIEW_MATRIX.
    //
    // w=0 → rotate only (no translation); normalize before use.
    vec3 world_view_dir = normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz);

    // ── DEBUG: world view ray sanity check ────────────────────────────────────
    // Set DEBUG_EYEDIR to 1 to verify world_view_dir is world-locked (not screen-locked).
    // Rotating the ship should make the gradient move.
    #define DEBUG_EYEDIR 0
    #if DEBUG_EYEDIR
        ALBEDO = world_view_dir * 0.5 + 0.5;
        ALPHA  = 1.0;
        return;
    #endif

    // Viewport aspect ratio for PSF pixel-space correction.
    // Multiply delta_uv.x by aspect to get uniform pixel-space (non-stretched PSF/spikes).
    float aspect = VIEWPORT_SIZE.x / VIEWPORT_SIZE.y;

    // ── Star PSF loop (near-star lens flare — D-11 / Play-test PSF feedback) ──
    for (int i = 0; i < star_count; i++) {

        // Skip stars behind the camera (dot product test).
        float face_d = dot(world_view_dir, star_dirs[i]);

        // LOD weight: 0=far/sky-point, 1=near/mesh.
        float lod = star_lod_weights[i];

        // PSF is a NEAR-STAR effect: intensity scales with lod_weight.
        // Smooth ramp from psf_lod_floor over psf_lod_range (Iteration 2: widened ramp).
        // Far-field stars (lod≈0) contribute negligible PSF; sky shader handles their disc.
        // Default: smoothstep(0.02, 0.62, lod) — PSF eases in across most of the approach.
        float psf_lod = smoothstep(psf_lod_floor, psf_lod_floor + psf_lod_range, lod);

        // PERF GUARD: skip stars with near-zero PSF contribution early, BEFORE the
        // extra depth texture sample.  Far stars (lod≈0) pay zero texture cost.
        if (psf_lod < 0.001) continue;

        // Also suppress if the star is behind the camera.
        bool  in_front = (face_d > 0.0);
        if (!in_front) continue;

        // ── Star projected screen position ───────────────────────────────────
        // Project star_dirs[i] into view space, then to clip space, to get the star's
        // screen UV. The PSF is centered at this 2D position.
        //
        // star_dirs[i] is a world-space unit direction. To project:
        //   1. Rotate to view space (VIEW_MATRIX rotates world→camera; w=0 = direction).
        //   2. Project to NDC via PROJECTION_MATRIX (scale by a large scalar first so
        //      the projection's perspective divide gives a meaningful result; direction
        //      x far-plane distance gives a point on the far plane in view space).
        //   3. Convert NDC to UV: uv = ndc.xy * 0.5 + 0.5.
        vec4 view_star = VIEW_MATRIX * vec4(star_dirs[i], 0.0);
        // Multiply by a reference distance to get a point in view space on the far plane.
        // We use 1.0 here — the actual distance doesn't matter for the UV projection
        // because PROJECTION_MATRIX is a linear transform for direction vectors (w→0 after divide).
        vec4 clip_star  = PROJECTION_MATRIX * vec4(view_star.xyz, 1.0);
        vec2 star_ndc   = clip_star.xy / clip_star.w;
        vec2 star_uv    = star_ndc * 0.5 + 0.5;

        // Skip star if its projected center is off-screen (star_uv outside [0,1]).
        // This covers both the "behind camera" case (already caught above) and partial
        // off-screen stars where depth-sampling would read invalid border data.
        if (any(lessThan(star_uv, vec2(0.0))) || any(greaterThan(star_uv, vec2(1.0)))) continue;

        // ── Per-pixel depth gate (Iteration 2: from depth texture, not hand-computed) ──
        // Sample the depth texture AT THE STAR'S CENTER UV to get the true depth of
        // whatever geometry is rendered at the star's projected position.
        //   - Star's own mesh near: star_raw_depth ≈ star mesh surface depth.
        //   - Nearer planet occluding the star: star_raw_depth = planet surface depth
        //     (small depth value, close to camera) → PSF suppressed for the whole frame.
        //   - Empty sky at star center: star_raw_depth ≈ 0 (sky threshold) → treated as
        //     infinite → PSF allowed (star is beyond all geometry).
        // This gives a consistent comparison: both lin_depth (current pixel) and
        // star_lin_depth are linearized from the same depth texture in the same units.
        float star_raw_depth = texture(depth_texture, star_uv).x;
        bool  star_is_sky    = (star_raw_depth < 1e-6);

        float star_lin_depth;
        if (star_is_sky) {
            // Sky at star center → treat star as infinitely far.
            // PSF is allowed everywhere (nothing occludes an infinitely-far star).
            star_lin_depth = 1e30;
        } else {
            // Linearize star center depth using the same INV_PROJECTION_MATRIX path
            // as the per-pixel lin_depth computed above — consistent units guaranteed.
            vec3  star_ndc3    = vec3(star_uv * 2.0 - 1.0, star_raw_depth);
            vec4  star_view    = INV_PROJECTION_MATRIX * vec4(star_ndc3, 1.0);
            star_lin_depth     = -star_view.z / star_view.w;
        }

        // PSF is allowed at this pixel if the current pixel is AT or BEYOND the star depth.
        // Foreground occluders (planets) have a smaller lin_depth (closer to camera) and
        // suppress the PSF.  psf_depth_epsilon is a small additive tolerance to avoid
        // z-fight shimmer at the star mesh surface itself.
        bool  depth_ok = sky_pixel || (lin_depth >= star_lin_depth - psf_depth_epsilon);

        if (!depth_ok) continue;

        // Delta from star center to current pixel in UV space, aspect-corrected.
        // Multiply x by aspect so the PSF/spikes are round/axis-aligned in pixel space.
        vec2 delta_uv   = SCREEN_UV - star_uv;
        vec2 delta_px   = delta_uv * vec2(aspect, 1.0);

        // ── Screen-space PSF (adapted from ShaderToy aperture diffraction reference) ─
        // Inverse-cube falloff: spectrum / (d³) gives a bright core with wide wings.
        // The core_scale and spike scales are [ASSUMED D-04 play-test knobs].
        //
        // spectrum = star BaseColor (hue preserved; brightness from star_colors[i].a).
        // Scaled by star's apparent brightness so bright stars get stronger PSF.
        vec3  spectrum  = star_colors[i].rgb * star_colors[i].a;

        // Core: circularly symmetric inverse-cube blob.
        float core_d    = length(delta_px) * psf_core_scale;
        core_d          = max(core_d, 0.001); // avoid /0 at star center
        vec3  core      = spectrum / (core_d * core_d * core_d);

        // Horizontal spike: elongate along X (short Y).
        float spike_h_d = length(delta_px * vec2(psf_spike_long_scale, psf_spike_short_scale)) * psf_core_scale;
        spike_h_d       = max(spike_h_d, 0.001);
        vec3  spike_h   = spectrum / (spike_h_d * spike_h_d * spike_h_d);

        // Vertical spike: elongate along Y (short X).
        float spike_v_d = length(delta_px * vec2(psf_spike_short_scale, psf_spike_long_scale)) * psf_core_scale;
        spike_v_d       = max(spike_v_d, 0.001);
        vec3  spike_v   = spectrum / (spike_v_d * spike_v_d * spike_v_d);

        // Total PSF contribution: core + both spikes, scaled by LOD weight and intensity.
        vec3  psf       = (core + spike_h + spike_v) * psf_lod * psf_intensity;

        // Clamp PSF to prevent a single star from blowing out the entire frame.
        // Cap at a generous level that still looks dramatic but doesn't destroy readability.
        // [ASSUMED D-04 play-test knob: max 8.0 per channel before bloom]
        psf = min(psf, vec3(8.0));

        col.rgb += psf;
    }

    // ── Output ─────────────────────────────────────────────────────────────────
    // blend_add: ALPHA is ignored; only ALBEDO contributes additively to the 3D scene buffer.
    ALBEDO = col.rgb;
    ALPHA  = 1.0;
}
