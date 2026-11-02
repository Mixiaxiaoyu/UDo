extends Button

const Design = preload("res://scripts/settings_design.gd")
var direction := -1

func _ready() -> void:
	text = ""
	custom_minimum_size = Vector2(28, 24)
	size_flags_vertical = Control.SIZE_SHRINK_CENTER
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	for state in ["normal", "disabled"]:
		add_theme_stylebox_override(state, Design.rounded(Color.TRANSPARENT, 4))
	add_theme_stylebox_override("hover", Design.rounded(Color(0, 0, 0, 0.035), 4))
	add_theme_stylebox_override("pressed", Design.rounded(Color(0, 0, 0, 0.065), 4))
	add_theme_stylebox_override("focus", Design.focus())
	add_theme_constant_override("outline_size", 0)
	add_theme_color_override("font_shadow_color", Color.TRANSPARENT)

func _draw() -> void:
	var center := size / 2
	var tint := Color(0, 0, 0, 0.22) if disabled else Design.TEXT
	draw_polyline(PackedVector2Array([center + Vector2(-direction * 1.5, -3), center + Vector2(direction * 1.5, 0), center + Vector2(-direction * 1.5, 3)]), tint, 1.2, true)
