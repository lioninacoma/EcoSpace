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
// DEPTH OCCLUSION (D-03 / Pitfall 5 / Play-test feedback):
//   Godot 4.3+ Forward+ uses reversed-Z: far plane = 0.0, near plane = 1.0.
//   raw_depth ≈ 0 → empty sky pixel.  raw_depth > ε → foreground geometry present.
//
//   Per-pixel depth gate for the PSF: suppress PSF where scene linear depth is LESS
//   than the star's approximate linear depth (foreground occluder between camera and star).
//   Allow PSF where lin_depth >= star_lin_depth (empty sky or star mesh surface).
//   star_lin_depths[] is pushed in render units (distMeters * renderFactor) by LuminousPassRenderer.
//   A small multiplier PsfDepthEpsilon (< 1.0) creates a tolerance band at the star surface
//   to avoid z-fighting shimmer exactly on the star mesh.
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
// LOD REWORK (Play-test feedback):
//   Old: lod_fade = 1 - lod_weight made PSF vanish when near (backwards for lens flare).
//   New: PSF intensity scales WITH lod_weight so the lens flare is strongest when the star
//   is near and its mesh is dominant. The sky shader's point disc handles the far-field.
//   A minimum PSF floor keeps some lens flare visible even at mid-range (MID stage of D-11).
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

/// Approximate linear depth of each star in view-space render units (distMeters * renderFactor).
/// Used to determine if a pixel's geometry is a foreground occluder (closer than the star).
/// Pushed from LuminousPassRenderer each frame.
uniform float star_lin_depths[MAX_STARS];

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

/// Minimum LOD weight below which PSF fades to zero (smooth ramp).
/// Keeps far-field sky-shader points from acquiring any PSF contribution.
/// [ASSUMED D-04 play-test knob — default 0.02]
uniform float psf_lod_floor         = 0.02;

/// Depth tolerance multiplier for the per-pixel depth gate (0.0 < value < 1.0).
/// PSF is allowed at pixels where lin_depth >= star_lin_depth * psf_depth_epsilon.
/// Values close to 1.0 allow only pixels at or beyond the star; smaller values are more permissive.
/// [ASSUMED D-04 play-test knob — default 0.8]
uniform float psf_depth_epsilon     = 0.8;

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
        // Also use dot product for angular disc (original approach preserved):
        // d approaches 1.0 when looking directly at the star.
        float d = face_d;

        // LOD weight: 0=far/sky-point, 1=near/mesh.
        float lod = star_lod_weights[i];

        // PSF is a NEAR-STAR effect: intensity scales with lod_weight.
        // Smooth ramp from psf_lod_floor (avoid sudden cutoff at 0).
        // Far-field stars (lod≈0) contribute negligible PSF; sky shader handles their disc.
        float psf_lod = smoothstep(psf_lod_floor, psf_lod_floor + 0.2, lod);

        // Clamp: skip stars with near-zero PSF contribution (early-out optimization).
        if (psf_lod < 0.001) continue;

        // ── Per-pixel depth gate ──────────────────────────────────────────────
        // PSF is allowed at this pixel if:
        //   a) It is a sky pixel (no geometry) — glow always allowed in empty sky.
        //   b) The scene geometry at this pixel is AT or BEYOND the star's depth
        //      (the star's own mesh surface, or sky behind the star).
        //   c) The star is behind the camera — suppress entirely.
        // Foreground objects (planets between camera and star) have lin_depth LESS
        // than star_lin_depths[i] and are suppressed by this gate.
        float star_depth = star_lin_depths[i];
        // Use a tolerance factor to avoid z-fighting shimmer on the star mesh surface.
        bool  depth_ok   = sky_pixel || (lin_depth >= star_depth * psf_depth_epsilon);
        // Also suppress if the star is behind the camera.
        bool  in_front   = (face_d > 0.0);

        if (!depth_ok || !in_front) continue;

        // ── Star projected screen position ───────────────────────────────────
        // Project star_dirs[i] into view space, then to clip space, to get the star's
        // screen UV. The PSF is centered at this 2D position.
        //
        // star_dirs[i] is a world-space unit direction. To project:
        //   1. Rotate to view space (VIEW_MATRIX rotates world→camera; w=0 = direction).
        //   2. Project to NDC via PROJECTION_MATRIX (scale by a large scalar first so
        //      the projection's perspective divide gives a meaningful result; direction
        //      × far-plane distance gives a point on the far plane in view space).
        //   3. Convert NDC to UV: uv = ndc.xy * 0.5 + 0.5.
        vec4 view_star = VIEW_MATRIX * vec4(star_dirs[i], 0.0);
        // Multiply by a reference distance to get a point in view space on the far plane.
        // We use 1.0 here — the actual distance doesn't matter for the UV projection
        // because PROJECTION_MATRIX is a linear transform for direction vectors (w→0 after divide).
        vec4 clip_star  = PROJECTION_MATRIX * vec4(view_star.xyz, 1.0);
        vec2 star_ndc   = clip_star.xy / clip_star.w;
        vec2 star_uv    = star_ndc * 0.5 + 0.5;

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
