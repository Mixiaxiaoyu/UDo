extends HSlider

const SettingsDesign = preload("res://scripts/settings_design.gd")
var visual_ratio := 0.0
var dragging := false
var ratio_tween: Tween

func _ready() -> void:
	custom_minimum_size = Vector2(140, 24)
	size_flags_vertical = Control.SIZE_SHRINK_CENTER
	# Keep native slider input, keyboard handling and a 20px knob hit geometry.
	var empty := GradientTexture2D.new()
	empty.width = 20
	empty.height = 20
	empty.gradient = Gradient.new()
	empty.gradient.colors = PackedColorArray([Color.TRANSPARENT, Color.TRANSPARENT])
	for key in ["grabber", "grabber_highlight", "grabber_disabled"]:
		add_theme_icon_override(key, empty)
	for key in ["slider", "grabber_area", "grabber_area_highlight"]:
		add_theme_stylebox_override(key, StyleBoxEmpty.new())
	visual_ratio = ratio
	value_changed.connect(_animate_value)
	drag_started.connect(func(): dragging = true)
	drag_ended.connect(func(_changed: bool): dragging = false)
	focus_entered.connect(queue_redraw)
	focus_exited.connect(queue_redraw)

func _animate_value(_value: float) -> void:
	if ratio_tween and ratio_tween.is_running(): ratio_tween.kill()
	if dragging or bool(get_meta("reduce_motion", false)):
		visual_ratio = ratio
		queue_redraw()
		return
	ratio_tween = create_tween().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	ratio_tween.tween_property(self, "visual_ratio", ratio, 0.18)

func _process(_delta: float) -> void:
	if ratio_tween and ratio_tween.is_running(): queue_redraw()

func _draw() -> void:
	# Figma Slider 512:3367: 6px track with 1px vertical insets, 20x16 knob.
	var center := Vector2(10 + (size.x - 20) * visual_ratio, size.y / 2)
	draw_style_box(SettingsDesign.rounded(SettingsDesign.CONTROL, 2), Rect2(7, center.y - 2, size.x - 14, 4))
	draw_style_box(SettingsDesign.rounded(SettingsDesign.TINT, 2), Rect2(7, center.y - 2, center.x - 4, 4))
	var knob := SettingsDesign.rounded(Color.WHITE, 8)
	knob.shadow_color = Color(0, 0, 0, 0.05)
	knob.shadow_size = 2
	draw_style_box(knob, Rect2(center - Vector2(10, 8), Vector2(20, 16)))
	if has_focus():
		draw_style_box(SettingsDesign.focus(), Rect2(center - Vector2(12, 10), Vector2(24, 20)))
