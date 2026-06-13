shader_type canvas_item;

// Preserved uniforms (still consumed by UniRenderer.cs parameter pushes)
uniform bool  dithering       = true;
uniform int   dithering_size  = 10;
uniform int   resolution_scale = 2;
uniform float threshold       = 60.0;
uniform vec4  white : source_color = vec4(1, 1, 1, 1);
uniform vec4  black : source_color = vec4(0, 0, 0, 1);
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

// Per-channel ordered quantize (D-13 color palette extension).
// quantize_levels controls how many discrete levels per RGB channel.
// Default 4 → ~64 colours (4^3), giving an 8-bit palette feel while
// preserving hue. Guard: UniRenderer ensures levels >= 1.
uniform int quantize_levels = 4;

// 4x4 Bayer ordered-dither offset table (preserved from original).
int dithering_pattern(ivec2 fragcoord) {
	const int pattern[] = {
		-4, +0, -3, +1,
		+2, -2, +3, -1,
		-3, +1, -4, +0,
		+3, -1, +2, -2
	};
	int x = fragcoord.x % 4;
	int y = fragcoord.y % 4;

	return pattern[y * 4 + x] * dithering_size;
}

void fragment() {
	// resolution_scale block-snapping: snap UV to pixel-block grid (preserved).
	ivec2 fragcoord  = ivec2(FRAGCOORD.xy / float(resolution_scale));
	vec2 screen_size = vec2(textureSize(screen_texture, 0));
	ivec2 snapped    = fragcoord * resolution_scale;
	vec2  uv         = (vec2(snapped) + 0.5) / screen_size;

	vec3 color = texture(screen_texture, uv).rgb;

	// Apply Bayer ordered-dither offset per channel (preserved, now in float space).
	// The offset is scaled to [0..1] range: dithering_pattern returns ints in roughly
	// [-4*size .. +4*size]; dividing by 255 maps it to the same 0-1 color range.
	float bayer_offset = 0.0;
	if (dithering) {
		bayer_offset = float(dithering_pattern(fragcoord)) / 255.0;
	}

	// Hue-preserving per-channel ordered quantize (replaces 1-bit avg<threshold collapse).
	// Each channel is independently posterized to quantize_levels steps using the Bayer
	// offset, producing ~levels^3 distinct colours while retaining the ordered-dither
	// texture. Formula: floor(c * levels + bayer) / levels   (standard posterize + dither).
	float levels = float(max(quantize_levels, 1));
	vec3  c      = color + vec3(bayer_offset);
	c = floor(c * levels) / (levels - 1.0 + 1e-6);
	c = clamp(c, 0.0, 1.0);

	// white/black uniforms: still available for tinting / overlay use.
	// They no longer drive a hard 1-bit branch; instead they serve as optional
	// tint endpoints. The output is full-colour by default.
	// (threshold, white, black uniforms kept declared so UniRenderer pushes don't break.)
	COLOR = vec4(c, 1.0);
}
