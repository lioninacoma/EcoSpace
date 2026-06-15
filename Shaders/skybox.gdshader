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

/// Angular disc half-size in smoothstep space. A small CONSTANT for every star
/// (stars are unresolved point sources) — SkyboxRenderer.StarAngularSize. Apparent
/// magnitude is conveyed by star_colors[].a (brightness) + HDR bloom, NOT disc size.
uniform float star_sizes[MAX_STARS];

/// Number of valid sky bodies in the arrays above. Shader loops only up to this count.
uniform int   star_count = 0;

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
    COLOR = col;
}
