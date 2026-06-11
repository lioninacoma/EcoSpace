shader_type canvas_item;

uniform bool dithering = true;
uniform int dithering_size = 10;
uniform int resolution_scale = 2;
uniform float threshold = 60.0;
uniform vec4 white: source_color = vec4(1,1,1,1);
uniform vec4 black: source_color = vec4(0,0,0,1);
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;

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
	// Skalierte Koordinate = 1 Einheit pro Pixelblock
	ivec2 fragcoord = ivec2(FRAGCOORD.xy / float(resolution_scale));

	// Snapped UV: auf Blockgröße einrasten → normalisierte UV
	vec2 screen_size = vec2(textureSize(screen_texture, 0));
	ivec2 snapped = fragcoord * resolution_scale;
	vec2 uv = (vec2(snapped) + 0.5) / screen_size;

	vec3 color = texture(screen_texture, uv).rgb;

	ivec3 c = ivec3(round(color * 255.0));

	if (dithering) {
		c += ivec3(dithering_pattern(fragcoord));
	}

	float avg = float(c.x + c.y + c.z) / 3.0;
	COLOR = avg < threshold ? black : white;
}