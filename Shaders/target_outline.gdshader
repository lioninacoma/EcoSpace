// target_outline.gdshader
// Unshaded spatial shader that draws only the silhouette ring of a unit sphere,
// producing a phosphor-green sphere-outline target marker (D-50/D-51).
//
// Technique: rim/fresnel single-pass fragment discard.
//   render_mode unshaded, cull_back — Godot renders the front-facing hemisphere.
//   In fragment(), the dot of VIEW (camera-toward-fragment direction, view space) and
//   NORMAL (surface normal, view space) is computed. Where |dot| is large the fragment
//   faces the camera (interior of the sphere); where |dot| is near zero the fragment
//   is at a grazing angle (the silhouette ring).
//   Fragments where |dot(VIEW, NORMAL)| > rim_width are discarded, leaving only the
//   edge ring visible. The result is a round outline that distorts with perspective
//   exactly like the body mesh it encapsulates (D-50) because it IS a sphere mesh
//   drawn at the target's render-space position and radius.
//
// Uniforms:
//   outline_color  – phosphor-green RGBA (source_color; pushed from TargetMarkerRenderer each frame)
//   rim_width      – controls ring thickness; 0.0 = invisible, 1.0 = full hemisphere;
//                    0.25 gives a thin, readable ring at typical screen sizes.
//
// No depth_prepass or depth_draw_opaque: the outline must not occlude the body mesh behind
// it (the marker encapsulates the body; the front face of the outline sits just outside
// the body surface). depth_draw_opaque would write to the depth buffer and cause the ring
// to clip the body's glow. render_mode unshaded disables all Godot engine lighting.

shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;

// Phosphor-green outline color (RGBA). Pushed from TargetMarkerRenderer each frame.
// Default matches HUD PhosphorGreen (0.1, 1.0, 0.3, 1.0).
uniform vec4 outline_color : source_color = vec4(0.1, 1.0, 0.3, 1.0);

// Rim/fresnel threshold controlling ring thickness.
// dot(VIEW, NORMAL) in [0, 1]: 0 = grazing edge, 1 = directly facing camera.
// Fragments with |dot| > rim_width are discarded (interior); only the rim ring remains.
// 0.25 = thin ring; 0.4 = thicker band. Play-test [Export] knob on TargetMarkerRenderer.
uniform float rim_width : hint_range(0.0, 1.0) = 0.25;

void fragment()
{
    // VIEW and NORMAL are both in view space in Godot 4 spatial shaders.
    // dot(VIEW, NORMAL): VIEW points from fragment toward the camera (-Z when facing forward),
    // NORMAL is the outward surface normal. At the silhouette the two are perpendicular → dot ~ 0.
    // At the center the normal points toward the camera → dot ~ 1.
    float rim = abs(dot(normalize(VIEW), normalize(NORMAL)));

    // Discard the interior; keep only the silhouette ring.
    if (rim > rim_width) discard;

    ALBEDO = outline_color.rgb;
    ALPHA  = outline_color.a;
}
