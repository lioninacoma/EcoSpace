// skybox.gdshader
// Dynamic spherical skybox for EcoSpace — renders real next-tier-out bodies (sibling
// star systems) AND galaxy discs as world-fixed procedural structures using EYEDIR.
//
// EYEDIR is world-space in Godot 4 sky shaders — NOT camera-relative.
// It is the exact same coordinate frame as Godot's world coordinate system.
// This means dot(EYEDIR, star_dirs[i]) gives a world-fixed result automatically:
// rotating the ship/camera does NOT move the star points (RND-05 criterion 2).
// No rotation matrix is needed. [Source: godotengine.org/article/custom-sky-shaders-godot-4-0/]
//
// MAX_STARS: GLSL requires fixed-size array declarations. star_count controls the
// actual loop count so unused slots are skipped. [Source: github.com/godotengine/godot/issues/77970]
//
// Disc technique: smoothstep(1 - size, 1, dot(EYEDIR, dir)) produces an antialiased
// circular disc when dot product is close to 1 (i.e. EYEDIR nearly == dir).
//
// Galaxy discs additionally apply a D-59 safe-basis 3D tilt (foreshortening) and a
// bounded D-54 volumetric grain overlay.
//
// DO NOT sample RADIANCE: Godot 4.6 has a regression (#115441, #115599) that breaks
// texture(RADIANCE, EYEDIR) in sky shaders. This shader never samples RADIANCE — all
// color is computed directly from uniform data — so the regression does not affect us.
//
// Dither integration (D-27): The Sky renders BEFORE the 3D scene and the CanvasLayer
// post-process. hint_screen_texture in dithering.gdshader captures sky + meshes together
// in a single pass. DO NOT add a second dither pass for the sky.
//
// Bloom (D-20): COLOR values > 1.0 feed WorldEnvironment glow automatically.

shader_type sky;

const int MAX_STARS = 128;
const int MAX_GALAXIES = 32;

/// World-space unit directions from ship to each sky body. Packed as 8-element fixed array.
/// Computed each frame by SkyboxRenderer using the LCA-relative hierarchy walk (Pitfall 5).
uniform vec3  star_dirs[MAX_STARS];

/// RGB = BaseColor of the body (D-18), alpha = apparent brightness (> 1 enables bloom, D-20).
uniform vec4  star_colors[MAX_STARS] : source_color;

/// Angular disc half-size in smoothstep space, per star. SkyboxRenderer computes it from the
/// star's PHYSICAL angular radius θ = RadiusMeters/distance as (1 − cos θ), floored at one
/// screen pixel and capped below π/2.
uniform float star_sizes[MAX_STARS];

/// Number of valid sky bodies in the arrays above. Shader loops only up to this count.
uniform int   star_count = 0;

// ── Galaxy uniforms (D-40) ────────────────────────────────────────────────

/// World-space unit direction to each galaxy center (same EYEDIR frame as star_dirs).
uniform vec3  galaxy_dirs[MAX_GALAXIES];

/// rgb = BaseColor, a = ApparentBrightness (same StarRendering model as stars, D-30).
uniform vec4  galaxy_colors[MAX_GALAXIES] : source_color;

/// Angular disc half-size in (1 − cos θ) smoothstep units, same convention as star_sizes.
uniform float galaxy_sizes[MAX_GALAXIES];

/// 0 = spiral, 1 = elliptical (D-29).
uniform int   galaxy_types[MAX_GALAXIES];

/// xyz = disc normal (world-space unit vector), w = procedural arm seed (D-29).
uniform vec4  galaxy_orientations[MAX_GALAXIES];

/// Per-galaxy LOD crossfade weight from LuminousLod.GalaxyDiscWeight (D-13 groundwork).
/// 0 = galaxy suppressed (inside SOI or fading in); 1 = galaxy fully visible.
/// Multiplied into the galaxy contribution so the disc fades in/out continuously
/// instead of popping on SOI boundary crossing (Pitfall 2 / RESEARCH Pattern 2).
uniform float galaxy_disc_weights[MAX_GALAXIES];

/// Number of valid galaxies in the arrays above. Shader loops only up to this count.
uniform int   galaxy_count = 0;

// ── Galaxy LOD + disc-scale + tilt constants ─────────────────────────────────
//
//   GALAXY_LOD_THRESHOLD: smoothstep-space size above which the structured disc renders.
//   GALAXY_DISC_SCALE:    normalizes angular-size units to disc-plane UV coordinates.
//   TILT_FLOOR (D-59):    minimum foreshortening applied even when the disc is viewed nearly
//                         edge-on. MUST stay > 0 so the minor-axis scale never approaches zero
//                         (ring-collapse landmine, commit fef1d91 / T-05-03). Tune in-game.
//   detail_blend (D-54):  strength of the bounded volumetric grain overlay [0..1].

const float GALAXY_LOD_THRESHOLD = 2e-4;  // switch sub-pixel point → structured disc
const float GALAXY_DISC_SCALE    = 80.0;  // angular→UV scale; tune for arm/disc appearance
const float TILT_FLOOR           = 0.15;  // D-59: minimum tilt compression (must be > 0; T-05-03)
const float detail_blend         = 0.35;  // D-54: bounded grain overlay strength [0..1]

// ── Galaxy helper functions ───────────────────────────────────────────────────

/// Returns a 2D disc-plane UV for EYEDIR relative to a galaxy centered at galaxy_dir,
/// with D-59 safe-basis tilt foreshortening applied using disc_normal.
///
/// Basis is built from galaxy_dir ONLY — NEVER from disc_normal (the fef1d91 ring-collapse
/// landmine: building from disc_normal degenerates when disc_normal ≈ world-up, collapsing
/// the cross products and painting a sky-spanning ring). The stable basis is then used to
/// project disc_normal into the face-on plane, from which the minor axis is derived for
/// foreshortening. TILT_FLOOR clamps the compression so it never reaches zero (T-05-03).
///
/// The caller MUST gate on dot(EYEDIR, galaxy_dir) > 0 (front hemisphere only) so the
/// perpendicular projection never ghosts the disc onto the antipode (Pitfall 7/fef1d91).
vec2 galaxy_disc_coords_tilted(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    // 1. Stable face-on tangent basis from galaxy_dir (never from disc_normal — D-59 rule)
    vec3 up0 = abs(galaxy_dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 t1  = normalize(cross(up0, galaxy_dir));
    vec3 t2  = cross(galaxy_dir, t1);
    // View-plane offset of the eye ray from the galaxy center (perpendicular to galaxy_dir).
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    vec2 uv = vec2(dot(delta, t1), dot(delta, t2));

    // 2. D-59 tilt foreshortening via disc_normal's in-plane projection
    vec2 disc_normal_uv = vec2(dot(disc_normal, t1), dot(disc_normal, t2));
    float disc_normal_len = length(disc_normal_uv);
    if (disc_normal_len > 0.001) {
        vec2 minor_axis = disc_normal_uv / disc_normal_len;
        // tilt_factor = |dot(disc_normal, galaxy_dir)|: face-on = 1; edge-on = 0.
        float tilt_factor = abs(dot(disc_normal, galaxy_dir));
        float safe_tilt = clamp(tilt_factor, TILT_FLOOR, 1.0);
        float minor_comp = dot(uv, minor_axis);
        vec2  major_dir  = vec2(-minor_axis.y, minor_axis.x);
        float major_comp = dot(uv, major_dir);
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

void sky()
{
    vec3 col = vec3(0.0);

    // ── Star loop ────────────────────────────────────────────────────────────────
    // Sibling-system stars are drawn here as world-fixed light points (D-30).
    for (int i = 0; i < star_count; i++)
    {
        float d    = dot(EYEDIR, star_dirs[i]);
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }

    // ── Galaxy loop (D-40) ───────────────────────────────────────────────────────
    // Loop is bounded by galaxy_count ≤ MAX_GALAXIES (T-03-01 DoS mitigation).
    for (int i = 0; i < galaxy_count; i++) {
        float size = galaxy_sizes[i];
        float d    = dot(EYEDIR, galaxy_dirs[i]);

        // Base smoothstep disc (same formula as stars — point mode, D-30)
        float point_disc = smoothstep(1.0 - size, 1.0, d);

        float galaxy_bright;
        if (size < GALAXY_LOD_THRESHOLD) {
            // Sub-pixel / tiny-apparent-size: render as a colored point identical to star (D-30)
            galaxy_bright = point_disc * galaxy_colors[i].a;
        } else if (d > 0.0) {
            // Disc mode: structured procedural rendering (D-31), FRONT HEMISPHERE ONLY.
            // The d > 0.0 gate stops the disc from also painting the antipode (Pitfall 7).
            float seed     = galaxy_orientations[i].w;
            vec3  disc_nrm = galaxy_orientations[i].xyz;  // world-space disc normal (D-59)

            // D-59 safe-basis tilt: UV from galaxy_dir basis, minor axis compressed by disc_normal.
            vec2  uv = galaxy_disc_coords_tilted(EYEDIR, galaxy_dirs[i], disc_nrm);
            uv /= max(size * GALAXY_DISC_SCALE, 0.001);

            float disc_bright;
            if (galaxy_types[i] == 0)
                disc_bright = spiral_galaxy(uv, seed);
            else
                disc_bright = elliptical_galaxy(uv);

            // D-54: bounded value-noise grain MULTIPLIED into disc_bright so it only modulates
            // where the disc is already lit and can never fill the sky.
            float grain = fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
            disc_bright *= mix(1.0, 0.55 + 0.45 * grain, detail_blend);

            // Blend: 30% point (center highlight + star-matching handoff) + 70% disc structure
            galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
        } else {
            galaxy_bright = 0.0;
        }
        col += galaxy_colors[i].rgb * galaxy_bright * galaxy_disc_weights[i];
    }

    COLOR = col;
}
