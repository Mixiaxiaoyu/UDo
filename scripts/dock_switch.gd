extends Button

const SettingsDesign = preload("res://scripts/settings_design.gd")
var visual_on := 0.0
var state_tween: Tween

func _init() -> void:
	toggle_mode = true

func _ready() -> void:
	custom_minimum_size = Vector2(54, 24)
	size_flags_vertical = Control.SIZE_SHRINK_CENTER
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	for state_name in ["normal", "hover", "pressed", "hover_pressed", "focus", "disabled"]:
		add_theme_stylebox_override(state_name, StyleBoxEmpty.new())
	visual_on = 1.0 if button_pressed else 0.0
	toggled.connect(_animate_toggle)

func _animate_toggle(value: bool) -> void:
	if state_tween and state_tween.is_running(): state_tween.kill()
	if bool(get_meta("reduce_motion", false)):
		visual_on = 1.0 if value else 0.0
		queue_redraw()
		return
	state_tween = create_tween().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	state_tween.tween_property(self, "visual_on", 1.0 if value else 0.0, 0.24)
	state_tween.tween_callback(queue_redraw)

func _process(_delta: float) -> void:
	if state_tween and state_tween.is_running(): queue_redraw()

func _draw() -> void:
	# Switch 121:12161: 54x24 track, 2px inset, 32x20 capsule, 2x10 AX mark.
	# The user's two pictured appearances are On/active and On/inactive in Figma.
	# Use those as the app's blue/gray binary states, retaining the right-hand
	# capsule in both. Hover, focus and mouse-down introduce no third appearance.
	var track := Rect2(Vector2((size.x - 54) / 2, (size.y - 24) / 2), Vector2(54, 24))
	var neutral := Color.WHITE.blend(SettingsDesign.GROUP).blend(SettingsDesign.CONTROL)
	var style := SettingsDesign.rounded(neutral.lerp(SettingsDesign.TINT, visual_on), 12)
	draw_style_box(style, track)
	var mark := SettingsDesign.rounded(Color("c6c6c6").lerp(Color.WHITE, visual_on), 1)
	draw_style_box(mark, Rect2(track.position + Vector2(10, 7), Vector2(2, 10)))
	var knob := SettingsDesign.rounded(Color.WHITE, 10)
	knob.shadow_color = Color(0, 0, 0, 0.05)
	knob.shadow_size = 1
	draw_style_box(knob, Rect2(track.position + Vector2(20, 2), Vector2(32, 20)))
