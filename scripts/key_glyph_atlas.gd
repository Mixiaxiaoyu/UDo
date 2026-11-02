extends Control

const SettingsDesign = preload("res://scripts/settings_design.gd")
var font := SystemFont.new()

func _ready() -> void:
	font.font_names = PackedStringArray(["Segoe UI", "Microsoft YaHei UI"])
	font.font_weight = 500
	mouse_filter = Control.MOUSE_FILTER_IGNORE

func _draw() -> void:
	# Existing 44 x 24 field, 6px corners and 13px label, rendered at 2x.
	var chip := SettingsDesign.rounded(Color(1, 1, 1, 0.95), 12)
	chip.border_color = Color(0, 0, 0, 0.08)
	chip.set_border_width_all(2)
	for index in range(36):
		var rect := Rect2(index * 96 + 4, 8, 88, 48)
		draw_style_box(chip, rect)
		var label := String.chr(65 + index if index < 26 else 48 + index - 26)
		var width := font.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, 26).x
		draw_string(font, rect.position + Vector2(roundf((88 - width) / 2), 34), label, HORIZONTAL_ALIGNMENT_LEFT, -1, 26, Color(0, 0, 0, 0.85))
