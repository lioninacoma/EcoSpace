// luminous_pass.gdshader
// Depth-aware spatial quad pass for EcoSpace — draws star glow/halo (near) and points (far)
// and galaxy disc/points for all luminous bodies, composited additively before the CanvasLayer
// dither pass (D-05 ordering: transparent 3D pass < glow < CanvasLayer).
//
// TECHNIQUE — "advanced post-processing" pattern (Godot docs):
//   Camera3D-child Node3D hosts a 2×2 QuadMesh with FlipFaces=true.
//   Vertex stage writes POSITION = vec4(VERTEX.xy, 1.0, 1.0) to bypass all transforms and
//   keep the quad in front of every pixel regardless of camera movement.
//   This is the ONLY valid way to read hint_depth_texture in Forward+ (Pitfall 1 — canvas_item
//   shaders cannot bind the depth buffer, Godot bug #74464).
//
// DEPTH OCCLUSION (D-03 / Pitfall 5):
//   Godot 4.3+ Forward+ uses reversed-Z: far plane = 0.0, near plane = 1.0.
//   raw_depth ≈ 0 → empty sky pixel.  raw_depth > ε → foreground geometry present.
//   Sky-pixel gate is scale-independent (raw depth, not linear depth) so it works correctly
//   across all coordinate spaces (PlanetSpace render factor 1e-8 through Galaxy space).
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
// LOD FADE (D-03 / RND-07):
//   lod_fade = 1.0 - star_lod_weights[i]: when LodWeight=1 (star very near, mesh dominant),
//   the post-process contribution fades to 0 so only the body_lit mesh is visible.
//   When LodWeight=0 (star far away), lod_fade=1 so the point/glow is fully visible.
//   This continuous blend is the only mechanism — no discrete SOI-boundary swap (D-03).
//
// GALAXY STUB (Plan 2):
//   Galaxy disc helper functions are included verbatim from skybox.gdshader (D-59 TILT_FLOOR
//   guard, d>0.0 antipodal ghost gate).  The full procedural disc is wired in Plan 3.
//   In Plan 2, galaxies render as a faint point-disc weighted by galaxy_disc_weights.
//
// DO NOT add hint_depth_texture to dithering.gdshader (canvas_item) — Pitfall 1 / bug #74464.

shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_test_disabled, depth_draw_never;

// ── Depth + screen texture ─────────────────────────────────────────────────────
uniform sampler2D depth_texture  : hint_depth_texture;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

// ── Star uniforms ─────────────────────────────────────────────────────────────
const int MAX_STARS = 8;

/// World-space unit directions ship→star. Same EYEDIR frame as skybox.gdshader star_dirs.
uniform vec3  star_dirs[MAX_STARS];

/// RGB = BaseColor, A = ApparentBrightness (same packing as skybox.gdshader star_colors).
uniform vec4  star_colors[MAX_STARS];

/// Angular disc half-size in (1 - cos θ) smoothstep space, same as skybox.gdshader star_sizes.
uniform float star_sizes[MAX_STARS];

/// LOD weight from LuminousLod.StarMeshWeight: 0 = far/point-dominant, 1 = near/mesh-dominant.
/// lod_fade = 1 - star_lod_weights[i] so the post-process glow fades as the mesh takes over.
uniform float star_lod_weights[MAX_STARS];

/// Number of valid entries in the star arrays above.
uniform int   star_count = 0;

// ── Galaxy uniforms ────────────────────────────────────────────────────────────
const int MAX_GALAXIES = 4;

/// World-space unit directions ship→galaxy center.
uniform vec3  galaxy_dirs[MAX_GALAXIES];

/// RGB = BaseColor, A = ApparentBrightness.
uniform vec4  galaxy_colors[MAX_GALAXIES];

/// Angular disc half-size in (1 - cos θ) smoothstep space.
uniform float galaxy_sizes[MAX_GALAXIES];

/// LOD weight from LuminousLod.GalaxyDiscWeight: 0 = inside galaxy (no disc), 1 = far (full disc).
uniform float galaxy_disc_weights[MAX_GALAXIES];

/// 0 = spiral, 1 = elliptical (D-29).
uniform int   galaxy_types[MAX_GALAXIES];

/// xyz = disc normal (world-space unit vector), w = procedural arm seed (D-29).
uniform vec4  galaxy_orientations[MAX_GALAXIES];

/// Number of valid entries in the galaxy arrays above.
uniform int   galaxy_count = 0;

// ── Galaxy disc constants (verbatim from skybox.gdshader) ─────────────────────
const float GALAXY_LOD_THRESHOLD = 2e-4;  // sub-pixel point → structured disc threshold
const float GALAXY_DISC_SCALE    = 80.0;  // angular→UV scale; tune for arm/disc appearance
const float TILT_FLOOR           = 0.15;  // D-59: minimum tilt compression — MUST stay > 0
                                           // removing this causes ring-collapse (T-05-03)
const float detail_blend         = 0.35;  // D-54: bounded grain overlay strength [0..1]

// ── Galaxy helper functions (verbatim from skybox.gdshader) ───────────────────

/// Returns a 2D disc-plane UV for eye_dir relative to a galaxy at galaxy_dir,
/// with D-59 safe-basis tilt foreshortening via disc_normal.
///
/// Basis is built from galaxy_dir ONLY — NEVER from disc_normal (fef1d91 ring-collapse
/// landmine: building from disc_normal degenerates when disc_normal ≈ world-up).
/// TILT_FLOOR clamps foreshortening so it never reaches zero (T-05-03).
/// Caller MUST gate on dot(eye_dir, galaxy_dir) > 0 (front hemisphere only) to prevent
/// the perpendicular projection from ghosting the disc onto the antipode (Pitfall 4).
vec2 galaxy_disc_coords_tilted(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    // 1. Stable face-on tangent basis from galaxy_dir (never from disc_normal — D-59 rule)
    vec3 up0 = abs(galaxy_dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 t1  = normalize(cross(up0, galaxy_dir));
    vec3 t2  = cross(galaxy_dir, t1);
    // View-plane offset of the eye ray from the galaxy center.
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    vec2 uv = vec2(dot(delta, t1), dot(delta, t2));

    // 2. D-59 tilt foreshortening via disc_normal's in-plane projection
    vec2  disc_normal_uv  = vec2(dot(disc_normal, t1), dot(disc_normal, t2));
    float disc_normal_len = length(disc_normal_uv);
    if (disc_normal_len > 0.001) {
        vec2  minor_axis  = disc_normal_uv / disc_normal_len;
        // tilt_factor: face-on = 1 (circular), edge-on = 0 (collapsed)
        float tilt_factor = abs(dot(disc_normal, galaxy_dir));
        float safe_tilt   = clamp(tilt_factor, TILT_FLOOR, 1.0);
        float minor_comp  = dot(uv, minor_axis);
        vec2  major_dir   = vec2(-minor_axis.y, minor_axis.x);
        float major_comp  = dot(uv, major_dir);
        uv = minor_axis * minor_comp * safe_tilt + major_dir * major_comp;
    }
    return uv;
}

/// Returns [0,1] brightness for a procedural 2-arm spiral galaxy at disc-plane UV.
float spiral_galaxy(vec2 uv, float seed) {
    float r     = length(uv);
    float theta = atan(uv.y, uv.x);
    float disc  = exp(-r * r * 4.0);
    float arms  = 0.5 + 0.5 * sin(2.0 * theta - 8.0 * r + seed * 6.28318);
    float grain = fract(sin(r * 37.0 + seed * 13.0 + theta * 7.0) * 43758.5453);
    return disc * mix(arms, grain, smoothstep(0.2, 0.6, r));
}

/// Returns [0,1] brightness for a procedural elliptical galaxy at disc-plane UV.
float elliptical_galaxy(vec2 uv) {
    float r = length(vec2(uv.x * 1.6, uv.y));
    return exp(-r * r * 3.0);
}

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
    // raw_depth < 1e-6 → far plane → empty sky pixel → glow allowed.
    // raw_depth > 1e-6 → foreground geometry present → suppress glow.
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
    // Handedness: Godot world space is right-handed (+Y up, -Z forward).
    // star_dirs[] are world-space unit vectors built by LuminousDescriptorBuilder via
    // UniMath (LCA frame walk), same coordinate system → no sign flip needed.
    vec3 world_view_dir = normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz);

    // ── DEBUG: world view ray sanity check (Open Question 1 — replaces EYEDIR debug) ──
    // Set DEBUG_EYEDIR to 1 to verify world_view_dir is world-locked (not screen-locked).
    // Rotating the ship should make the gradient move — if it stays fixed, the quad
    // transform bypasses INV_VIEW_MATRIX (unlikely for Camera3D-child quads but verify).
    // Default: DEBUG_EYEDIR = 0 (production mode).
    #define DEBUG_EYEDIR 0
    #if DEBUG_EYEDIR
        ALBEDO = world_view_dir * 0.5 + 0.5;
        ALPHA  = 1.0;
        return;
    #endif

    // ── Star loop ──────────────────────────────────────────────────────────────
    for (int i = 0; i < star_count; i++) {
        // dot(world_view_dir, star_dirs[i]) → cos of angle between view ray and star direction.
        // Approaches 1.0 when looking directly at the star.
        // world_view_dir replaces EYEDIR (sky-shader-only built-in — see header comment).
        float d    = dot(world_view_dir, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);

        // Glow halo: wider softened ring around the point core.
        // halo_size multiplier = 8.0 is an [ASSUMED] play-test knob — tune for desired bloom radius.
        // halo strength 0.3 is an [ASSUMED] play-test knob — tune for desired glow intensity.
        float halo_size = star_sizes[i] * 8.0;
        float halo      = smoothstep(1.0 - halo_size, 1.0, d) * 0.3;

        // Depth occlusion gate: only paint on empty-sky pixels (Pitfall 5 / D-03).
        // Uses the scale-independent sky_pixel flag (raw_depth < 1e-6).
        float vis = sky_pixel ? 1.0 : 0.0;

        // LOD fade: glow/point fades OUT as the star mesh takes over (D-03 / RND-07).
        // lod_fade = 1 when LodWeight=0 (far, point dominant); 0 when LodWeight=1 (near, mesh dominant).
        float lod_fade = 1.0 - star_lod_weights[i];

        col.rgb += star_colors[i].rgb * (star_colors[i].a * (disc + halo) * vis * lod_fade);
    }

    // ── Galaxy loop ────────────────────────────────────────────────────────────
    for (int i = 0; i < galaxy_count; i++) {
        float disc_w = galaxy_disc_weights[i];
        // Inside the home galaxy (disc_w ≈ 0) — skip entirely (home-galaxy suppression).
        if (disc_w < 0.001) continue;

        float size       = galaxy_sizes[i];
        float d          = dot(world_view_dir, galaxy_dirs[i]);
        float point_disc = smoothstep(1.0 - size, 1.0, d);

        float galaxy_bright;
        if (size < GALAXY_LOD_THRESHOLD) {
            // Sub-pixel / tiny apparent size: render as a point identical to star.
            galaxy_bright = point_disc * galaxy_colors[i].a;
        } else if (d > 0.0) {
            // FRONT HEMISPHERE ONLY — d > 0.0 gate prevents disc from painting its antipode
            // (Pitfall 4 / skybox.gdshader line 165 / d>0.0 Antipodal Ghost Gate).
            float seed     = galaxy_orientations[i].w;
            vec3  disc_nrm = galaxy_orientations[i].xyz;

            // D-59 safe-basis tilt: UV from galaxy_dir basis, minor axis compressed by disc_normal.
            // world_view_dir replaces normalize(EYEDIR) — same world-space semantic, spatial-safe.
            vec2  uv = galaxy_disc_coords_tilted(world_view_dir, galaxy_dirs[i], disc_nrm);
            uv /= max(size * GALAXY_DISC_SCALE, 0.001);

            float disc_bright;
            if (galaxy_types[i] == 0)
                disc_bright = spiral_galaxy(uv, seed);
            else
                disc_bright = elliptical_galaxy(uv);

            // D-54: bounded value-noise grain MULTIPLIED into disc_bright.
            float grain = fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
            disc_bright *= mix(1.0, 0.55 + 0.45 * grain, detail_blend);

            // Blend: 30% point (center highlight + star-matching handoff) + 70% disc structure.
            galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
        } else {
            galaxy_bright = 0.0;
        }

        col.rgb += galaxy_colors[i].rgb * galaxy_bright * disc_w;
    }

    // ── Output ─────────────────────────────────────────────────────────────────
    // blend_add: ALPHA is ignored; only ALBEDO contributes additively to the 3D scene buffer.
    ALBEDO = col.rgb;
    ALPHA  = 1.0;
}
