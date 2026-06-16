// skybox.gdshader
// Dynamic spherical skybox for EcoSpace — renders real next-tier-out bodies (sibling
// star systems) as world-fixed dithered light points using EYEDIR.
//
// EYEDIR is world-space in Godot 4 sky shaders — NOT camera-relative.
// It is the exact same coordinate frame as Godot's world coordinate system.
// This means dot(EYEDIR, star_dirs[i]) gives a world-fixed result automatically:
// rotating the ship/camera does NOT move the star points (RND-05 criterion 2).
// No rotation matrix is needed. [Source: godotengine.org/article/custom-sky-shaders-godot-4-0/]
//
// MAX_STARS: GLSL requires fixed-size array declarations. star_count controls the
// actual loop count so unused slots are skipped. Maximum 8 covers Phase 2 (2-3 stars)
// with generous room for future data. [Source: github.com/godotengine/godot/issues/77970]
//
// Disc technique: smoothstep(1 - size, 1, dot(EYEDIR, dir)) produces an antialiased
// circular disc when dot product is close to 1 (i.e. EYEDIR nearly == dir).
// star_sizes[i] controls the angular radius in smoothstep units. [Source: kelvinvanhoorn.com]
//
// DO NOT sample RADIANCE: Godot 4.6 has a regression (#115441, #115599) that breaks
// texture(RADIANCE, EYEDIR) in sky shaders. This shader never samples RADIANCE — all
// color is computed directly from uniform data — so the regression does not affect us.
//
// Dither integration (D-27): The Sky renders BEFORE the 3D scene and the CanvasLayer
// post-process. hint_screen_texture in dithering.gdshader captures sky + meshes together
// in a single pass. DO NOT add a second dither pass for the sky.
//
// Bloom (D-20): COLOR values > 1.0 feed WorldEnvironment glow automatically. Set alpha
// channel of star_colors[i] to values > 1 for bright stars to bloom naturally.

shader_type sky;

const int MAX_STARS = 8;

/// World-space unit directions from ship to each sky body. Packed as 8-element fixed array.
/// Computed each frame by SkyboxRenderer using double-precision hierarchy walk (Pitfall 5).
uniform vec3  star_dirs[MAX_STARS];

/// RGB = BaseColor of the body (D-18), alpha = apparent brightness (> 1 enables bloom, D-20).
/// Mapped from C# Color[] → vec4[] : source_color.
uniform vec4  star_colors[MAX_STARS] : source_color;

/// Angular disc half-size in smoothstep space, per star. SkyboxRenderer computes it from the
/// star's PHYSICAL angular radius θ = RadiusMeters/distance as (1 − cos θ), floored at one
/// screen pixel and capped below π/2. Apparent magnitude is conveyed by star_colors[].a
/// (brightness from the shared log model) + HDR bloom.
uniform float star_sizes[MAX_STARS];

/// Number of valid sky bodies in the arrays above. Shader loops only up to this count.
uniform int   star_count = 0;

// ── Galaxy uniforms (D-40) ────────────────────────────────────────────────
//
// MAX_GALAXIES: small fixed array; galaxy_count controls actual loop iterations.
// galaxy_types[]: GLSL int array — verified at runtime (Task 1 Wave-0 de-risk).
// If int[] push via SetShaderParameter fails in Godot 4.6, fall back to packing
// type (0/1) into galaxy_orientations[i].w < 0.5 ? spiral : elliptical.
// The chosen path is recorded in 03-01-SUMMARY.md as the contract for SkyboxRenderer.

const int MAX_GALAXIES = 4;

/// World-space unit direction to each galaxy center (same EYEDIR frame as star_dirs).
uniform vec3  galaxy_dirs[MAX_GALAXIES];

/// rgb = BaseColor, a = ApparentBrightness (same StarRendering model as stars, D-30).
uniform vec4  galaxy_colors[MAX_GALAXIES] : source_color;

/// Angular disc half-size in (1 − cos θ) smoothstep units, same convention as star_sizes.
/// Sub-pixel galaxies render as star-like points; growing discs switch to structured mode.
uniform float galaxy_sizes[MAX_GALAXIES];

/// 0 = spiral, 1 = elliptical (D-29). If int[] fails, pack into galaxy_orientations.w.
uniform int   galaxy_types[MAX_GALAXIES];

/// xyz = disc normal (world-space unit vector), w = procedural arm seed (D-29).
uniform vec4  galaxy_orientations[MAX_GALAXIES];

/// Number of valid galaxies in the arrays above. Shader loops only up to this count.
uniform int   galaxy_count = 0;

// ── Galaxy LOD + disc-scale constants (D-31) ─────────────────────────────────
//
// PLAY-TEST TUNING KNOBS — adjust these after visual review at end of Phase 3:
//   GALAXY_LOD_THRESHOLD: smoothstep-space size above which the structured disc renders.
//                         Below this the galaxy is a star-like colored point.
//                         2e-4 ≈ 3 screen pixels at 1080p/75° FOV [ASSUMED — see RESEARCH A3].
//   GALAXY_DISC_SCALE:    normalizes angular-size units to disc-plane UV coordinates.
//                         80.0 is a starting estimate [ASSUMED — see RESEARCH A4/Pitfall 6].

const float GALAXY_LOD_THRESHOLD = 2e-4;  // switch sub-pixel point → structured disc
const float GALAXY_DISC_SCALE    = 80.0;  // angular→UV scale; tune for arm/disc appearance

// ── Galaxy helper functions ───────────────────────────────────────────────────

/// Projects EYEDIR into the tangent plane of a galaxy disc and returns a 2D UV
/// coordinate suitable for use in spiral_galaxy / elliptical_galaxy.
vec2 galaxy_disc_coords(vec3 eye_dir, vec3 galaxy_dir, vec3 disc_normal) {
    // Build two orthonormal tangent vectors spanning the disc plane.
    vec3 t1 = normalize(cross(disc_normal, vec3(0.0, 1.0, 0.0)));
    if (dot(t1, t1) < 0.001) t1 = normalize(cross(disc_normal, vec3(1.0, 0.0, 0.0)));
    vec3 t2 = cross(disc_normal, t1);
    // Angular offset from galaxy center in the disc tangent plane.
    vec3 delta = eye_dir - dot(eye_dir, galaxy_dir) * galaxy_dir;
    return vec2(dot(delta, t1), dot(delta, t2));
}

/// Returns [0,1] brightness for a procedural 2-arm spiral galaxy at disc-plane UV.
float spiral_galaxy(vec2 uv, float seed) {
    float r     = length(uv);
    float theta = atan(uv.y, uv.x);
    // Radial falloff: Gaussian disc
    float disc  = exp(-r * r * 4.0);
    // 2-arm spiral pattern: sin wraps with winding rate and seed offset
    float arms  = 0.5 + 0.5 * sin(2.0 * theta - 8.0 * r + seed * 6.28318);
    // Outer grain: modulates arm sharpness at large radii for a grainy spiral look
    float grain = fract(sin(r * 37.0 + seed * 13.0 + theta * 7.0) * 43758.5453);
    return disc * mix(arms, grain, smoothstep(0.2, 0.6, r));
}

/// Returns [0,1] brightness for a procedural elliptical galaxy at disc-plane UV.
float elliptical_galaxy(vec2 uv) {
    // Aspect ratio ~1.6:1 for a typical E3 elliptical; pure Gaussian falloff
    float r = length(vec2(uv.x * 1.6, uv.y));
    return exp(-r * r * 3.0);
}

void sky()
{
    vec3 col = vec3(0.0);
    for (int i = 0; i < star_count; i++)
    {
        // dot(EYEDIR, star_dirs[i]) approaches 1.0 when the pixel direction matches the star.
        float d    = dot(EYEDIR, star_dirs[i]);
        // smoothstep creates an antialiased circular disc:
        //   inner = 1.0 - star_sizes[i] (bright core boundary)
        //   outer = 1.0 (hard sky-direction edge)
        float disc = smoothstep(1.0 - star_sizes[i], 1.0, d);
        // Accumulate: rgb * brightness * disc. Values > 1.0 automatically feed glow (D-20).
        col += star_colors[i].rgb * (star_colors[i].a * disc);
    }

    // ── Galaxy loop (D-40) ───────────────────────────────────────────────────────
    // Runs alongside the star loop — in-system the sky shows sibling stars AND galaxies.
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
        } else {
            // Disc mode: structured procedural rendering (D-31)
            vec3  disc_normal = galaxy_orientations[i].xyz;
            float seed        = galaxy_orientations[i].w;
            vec2  uv          = galaxy_disc_coords(EYEDIR, galaxy_dirs[i], disc_normal);
            // Scale UV: angular-size units → disc-plane [-1..1] approx.
            uv /= max(size * GALAXY_DISC_SCALE, 0.001);

            float disc_bright;
            if (galaxy_types[i] == 0)
                disc_bright = spiral_galaxy(uv, seed);
            else
                disc_bright = elliptical_galaxy(uv);
            // Blend: 30% point (center highlight + star-matching handoff) + 70% disc structure
            galaxy_bright = mix(disc_bright, point_disc, 0.3) * galaxy_colors[i].a;
        }
        col += galaxy_colors[i].rgb * galaxy_bright;
    }

    COLOR = col;
}
