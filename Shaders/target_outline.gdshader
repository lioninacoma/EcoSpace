// target_outline.gdshader
// Unshaded spatial shader that draws only the silhouette ring of a unit sphere,
// producing a phosphor-green sphere-outline target marker (D-50/D-51).
//
// Technique: rim/fresnel single-pass fragment discard.
//   render_mode unshaded, cull_back — Godot renders the front-facing hemisphere.
//   depth_test_disabled — marker always draws on top of scene geometry (planets, stars, etc.)
//     even when the real body is in front of the camera. Prevents occlusion.
//   depth_draw_never — marker does not write to the depth buffer, so it never occludes
//     the body mesh or other markers.
//   In fragment(), the dot of VIEW (camera-toward-fragment direction, view space) and
//   NORMAL (surface normal, view space) is computed. Where |dot| is large the fragment
//   faces the camera (interior of the sphere); where |dot| is near zero the fragment
//   is at a grazing angle (the silhouette ring).
//   Fragments where |dot(VIEW, NORMAL)| > rim_width are discarded, leaving only the
//   edge ring visible. The result is a round outline that distorts with perspective
//   exactly like a body mesh at the same angular size (D-50).
//
// Placement: the C# side places this sphere at a FIXED distance from the camera
//   (MarkerCameraDistance), sized to the target's angular radius.
//
// Ring thickness: rim_width is pushed adaptively each frame by TargetMarkerRenderer so the
//   silhouette band is a constant TargetRingThicknessPx pixels wide regardless of marker size.
//   Geometry: T = R*(1 - sqrt(1 - rim_width²)) → rim_width = sqrt(1 - (1 - T/R)²).
//   At the D-52 minimum floor (R == T) rim_width reaches 1 (filled disc — acceptable).
//
// Uniforms:
//   outline_color  – phosphor-green RGBA (source_color; pushed from TargetMarkerRenderer each frame)
//   rim_width      – controls ring thickness; 0.0 = invisible, 1.0 = full hemisphere.
//                    Pushed adaptively per-frame (derived from TargetRingThicknessPx / pixelRadius).
//
// No depth_prepass or depth_draw_opaque: the outline must not occlude the body mesh behind
// it (the marker encapsulates the body; the front face of the outline sits just outside
// the body surface). render_mode unshaded disables all Godot engine lighting.

shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, depth_test_disabled;

// Phosphor-green outline color (RGBA). Pushed from TargetMarkerRenderer each frame.
// Default matches HUD PhosphorGreen (0.1, 1.0, 0.3, 1.0).
uniform vec4 outline_color : source_color = vec4(0.1, 1.0, 0.3, 1.0);

// Rim/fresnel threshold controlling ring thickness.
// dot(VIEW, NORMAL) in [0, 1]: 0 = grazing edge, 1 = directly facing camera.
// Fragments with |dot| > rim_width are discarded (interior); only the rim ring remains.
// Pushed per-frame from TargetMarkerRenderer (adaptive: rim_width = sqrt(1-(1-T/R)²))
// so the ring band is always TargetRingThicknessPx pixels wide on screen.
uniform float rim_width : hint_range(0.0, 1.0) = 0.25;

void fragment()
{
    // VIEW and NORMAL are both in view space in Godot 4 spatial shaders.
    // dot(VIEW, NORMAL): VIEW points from fragment toward the camera (-Z when facing forward),
    // NORMAL is the outward surface normal. At the silhouette the two are perpendicular -> dot ~ 0.
    // At the center the normal points toward the camera -> dot ~ 1.
    float rim = abs(dot(normalize(VIEW), normalize(NORMAL)));

    // Discard the interior; keep only the silhouette ring.
    if (rim > rim_width) discard;

    ALBEDO = outline_color.rgb;
    ALPHA  = outline_color.a;
}
