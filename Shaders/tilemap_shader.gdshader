// tilemap_shader.gdshader
shader_type canvas_item;

uniform sampler2D tileset_texture : filter_nearest, repeat_disable;
uniform int tileset_cols = 16;   // tiles across the atlas texture
uniform int tileset_rows = 16;   // tiles down the atlas texture
uniform int grid_cols   = 128;   // cells wide in the visible grid
uniform int grid_rows   = 72;    // cells tall in the visible grid
uniform float cell_offset = 0.0;

void fragment() {
	// UV goes 0..1 over the full mesh; map to cell coordinates
	float cell_x_f = UV.x * float(grid_cols);
	float cell_y_f = UV.y * float(grid_rows);

	// Integer cell index (floor)
	int cell_x = int(cell_x_f);
	int cell_y = int(cell_y_f);

	// Fractional position within this cell (for sampling within the tile)
	float frac_x = fract(cell_x_f);
	float frac_y = fract(cell_y_f);

	int offset = int(cell_offset);
	int tile_col = int(mod(float(cell_x + offset), float(tileset_cols)));
	int tile_row = int(mod(float(cell_y + offset), float(tileset_rows)));

	// Compute UV into the atlas texture
	float atlas_u = (float(tile_col) + frac_x) / float(tileset_cols);
	float atlas_v = (float(tile_row) + frac_y) / float(tileset_rows);

	COLOR = texture(tileset_texture, vec2(atlas_u, atlas_v));
}