// luminous_pass.gdshader
// Depth-aware spatial quad pass for EcoSpace — draws near-star glow/halo only.
// Galaxies are rendered entirely in skybox.gdshader (D-13 / Plan 3 narrowing).
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
// LOD FADE + DEPTH GATE BLEND (D-11 / D-03 / RND-07 / Plan 3):
//   lod_fade = 1.0 - star_lod_weights[i]: when LodWeight=1 (star very near, mesh dominant),
//   the post-process contribution fades to 0 so only the body_lit mesh is visible.
//   When LodWeight=0 (star far away), lod_fade=1 so the point/glow is fully visible.
//
//   is_near = star_lod_weights[i]: used to relax the sky_pixel depth gate for near stars.
//   Far stars (is_near≈0): glow only paints on sky pixels, preventing bleed over geometry.
//   Near stars (is_near→1): glow paints over the visible mesh as a halo.
//   When LodWeight=1 (mesh dominant): vis=1 but lod_fade=0, so vis*lod_fade=0 — halo gone.
//   This continuous blend drives the three-stage star handoff (D-11): pop-free by construction.
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
const int MAX_STARS = 8;

/// World-space unit directions ship→star. Same EYEDIR frame as skybox.gdshader star_dirs.
uniform vec3  star_dirs[MAX_STARS];

/// RGB = BaseColor, A = ApparentBrightness (same packing as skybox.gdshader star_colors).
uniform vec4  star_colors[MAX_STARS];

/// Angular disc half-size in (1 - cos θ) smoothstep space, same as skybox.gdshader star_sizes.
uniform float star_sizes[MAX_STARS];

/// LOD weight from LuminousLod.StarMeshWeight: 0 = far/point-dominant, 1 = near/mesh-dominant.
/// lod_fade = 1 - star_lod_weights[i] so the post-process glow fades as the mesh takes over.
/// is_near  = star_lod_weights[i] relaxes the sky_pixel gate so the halo wraps the mesh.
uniform float star_lod_weights[MAX_STARS];

/// Number of valid entries in the star arrays above.
uniform int   star_count = 0;

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
    // raw_depth > 1e-6 → foreground geometry present → suppress glow (for far stars).
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

    // ── Star loop (near-star glow/halo only — D-11 / Plan 3) ──────────────────
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

        // LOD fade: glow/point fades OUT as the star mesh takes over (D-03 / RND-07).
        // lod_fade = 1 when LodWeight=0 (far, point dominant); 0 when LodWeight=1 (near, mesh dominant).
        float lod_fade = 1.0 - star_lod_weights[i];

        // Depth gate blend (RESEARCH Pattern 4 / D-11 / Plan 3):
        //   is_near blends between sky-gated (far star) and fully open (near star halo).
        //   Far star (is_near=0): only paint on sky pixels — prevents glow bleeding over geometry.
        //   Near star (is_near=1): paint over the star mesh — the halo wraps the visible sphere.
        //   Product vis * lod_fade = 0 at LodWeight=1: glow fully gone when mesh dominates.
        //   Pop-free by construction (D-11): glow and mesh cross-fade continuously via lod_fade.
        float is_near = star_lod_weights[i];
        float vis     = mix(sky_pixel ? 1.0 : 0.0, 1.0, is_near);

        col.rgb += star_colors[i].rgb * (star_colors[i].a * (disc + halo) * vis * lod_fade);
    }

    // ── Output ─────────────────────────────────────────────────────────────────
    // blend_add: ALPHA is ignored; only ALBEDO contributes additively to the 3D scene buffer.
    ALBEDO = col.rgb;
    ALPHA  = 1.0;
}
