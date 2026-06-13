// body_lit.gdshader
// Unshaded spatial shader that computes Lambert day/night lighting in the fragment
// shader from a per-body star direction uniform. Godot adds NO engine lighting of
// its own (render_mode unshaded). This guarantees identical shading CHARACTER in
// every space the ship occupies — Star space, Planet space, or any future tier —
// because lighting is computed from an explicit world-space direction, not from
// Godot OmniLight3D / DirectionalLight3D nodes whose presence varies per space.
//
// Uniforms set by RenderBridge each frame:
//   albedo        – base surface colour (from body.BaseColor)
//   star_dir      – world-space unit vector FROM this body's surface TOWARD the star
//                   (i.e. the direction photons travel FROM the star TO the surface
//                    reversed — so the lit side is where dot(N, star_dir) > 0)
//   light_energy  – sun brightness multiplier on the lit hemisphere
//   ambient       – minimum brightness on the dark (night) hemisphere
//
// Fragment output: ALBEDO = albedo * (ambient + max(dot(N_world, star_dir), 0) * light_energy)
// This is a simple Lambert model with an ambient floor on the night side, matching
// the retro space-sim aesthetic (dark hemisphere is not pure black).
//
// Normal world-space conversion:
//   In Godot 4 spatial shaders the built-in NORMAL is in VIEW space.
//   INV_VIEW_MATRIX (4×4, available in fragment) transforms from view to world.
//   Applying it to (NORMAL, 0) as a direction (w=0) gives the world-space normal.
//   normalize() is applied after transformation because INV_VIEW_MATRIX may have
//   non-unit scale when the node transform includes non-uniform scaling — though for
//   uniform sphere Scale we also normalize for robustness.

shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, diffuse_lambert;

// Base surface colour (RGB). Set from body.BaseColor by RenderBridge.
uniform vec4 albedo : source_color = vec4(0.75, 0.85, 0.75, 1.0);

// World-space unit direction FROM the body surface TOWARD the star (light source).
// Computed each frame in RenderBridge as normalize(starRenderPos - bodyRenderPos).
// Because render space is the same coordinate frame as Godot's world (floating-origin
// camera at world origin), this direction is already in the same frame as NORMAL once
// NORMAL is converted from view space to world space via INV_VIEW_MATRIX.
uniform vec3 star_dir = vec3(0.0, 1.0, 0.0);

// Sun brightness on the lit hemisphere. Default matches legacy StarLightEnergy=1.8.
uniform float light_energy : hint_range(0.0, 8.0) = 1.8;

// Minimum brightness on the night hemisphere (ambient floor).
// Default 0.03 matches the prior WorldEnvironment ambient_light_energy.
uniform float ambient : hint_range(0.0, 1.0) = 0.03;

void fragment()
{
	// Convert view-space NORMAL to world space using INV_VIEW_MATRIX (direction, w=0).
	// INV_VIEW_MATRIX is the inverse of the camera view matrix, i.e. camera→world.
	// Multiplying a view-space direction by it yields the world-space direction.
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);

	// Lambert diffuse: max(dot(n, L), 0) where L = star_dir (toward star = toward light).
	float ndl = max(dot(n, normalize(star_dir)), 0.0);

	// Combine: ambient floor keeps night side dark-but-visible; lit side is albedo * energy.
	ALBEDO = albedo.rgb * (ambient + ndl * light_energy);
}
