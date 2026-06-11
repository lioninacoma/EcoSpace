shader_type canvas_item;

// Liest den fertigen 3D-Frame (Framebuffer)
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

uniform float scanline_intensity : hint_range(0.0, 1.0) = 0.3;

void fragment() {
    vec4 color = texture(screen_texture, SCREEN_UV);

    float scanline = sin(SCREEN_UV.y * 800.0) * 0.5 + 0.5;
    scanline = 1.0 - scanline * scanline_intensity;

    COLOR = vec4(color.rgb * scanline, color.a);
}