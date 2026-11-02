shader_type canvas_item;
render_mode unshaded, blend_disabled;

uniform sampler2D popup_frame : hint_screen_texture, repeat_disable, filter_nearest;

void fragment() {
	// Embedded windows are composited using straight alpha, while their canvas
	// texture contains premultiplied RGB. Decode once before that composition.
	vec4 pixel = texture(popup_frame, SCREEN_UV);
	COLOR = vec4(pixel.a > 0.00001 ? pixel.rgb / pixel.a : vec3(0.0), pixel.a);
}
