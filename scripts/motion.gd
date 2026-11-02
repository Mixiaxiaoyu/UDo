extends RefCounted

# Shared timing for the independent Dock, settings and menu viewports.
const QUICK := 0.11
const CONTROL := 0.18
const PANEL := 0.26

static func bind_buttons(root: Node, reduced := false) -> void:
	for child in root.get_children():
		if child is Control: child.set_meta("reduce_motion", reduced)
		if child is Button and not child is HSlider:
			if not child.has_meta("motion_bound"): _bind_button(child)
		bind_buttons(child, reduced)

static func _bind_button(button: Button) -> void:
	button.set_meta("motion_bound", true)
	button.resized.connect(func(): button.pivot_offset = button.size * 0.5)
	button.pivot_offset = button.size * 0.5
	button.mouse_entered.connect(func(): _button_to(button, 1.025, 0.96))
	button.mouse_exited.connect(func(): _button_to(button, 1.0, 1.0))
	button.button_down.connect(func(): _button_to(button, 0.965, 0.86))
	button.button_up.connect(func(): _button_to(button, 1.025 if button.get_global_rect().has_point(button.get_global_mouse_position()) else 1.0, 1.0))
	button.focus_entered.connect(func(): _button_to(button, 1.025, 1.0))
	button.focus_exited.connect(func(): _button_to(button, 1.0, 1.0))

static func _button_to(button: Button, target_scale: float, brightness: float) -> void:
	if not is_instance_valid(button) or button.disabled: return
	if button.has_meta("motion_tween"):
		var old: Tween = button.get_meta("motion_tween")
		if old and old.is_running(): old.kill()
	if bool(button.get_meta("reduce_motion", false)):
		button.scale = Vector2.ONE
		button.modulate = Color.WHITE
		return
	var tween := button.create_tween().set_parallel(true).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(button, "scale", Vector2.ONE * target_scale, CONTROL)
	tween.tween_property(button, "modulate", Color(brightness, brightness, brightness), CONTROL)
	button.set_meta("motion_tween", tween)

static func enter(control: Control, reduced := false, delay := 0.0) -> void:
	if reduced: return
	control.pivot_offset = control.size * 0.5
	control.modulate.a = 0.0
	control.scale = Vector2(0.975, 0.975)
	var tween := control.create_tween().set_parallel(true).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(control, "modulate:a", 1.0, PANEL).set_delay(delay)
	tween.tween_property(control, "scale", Vector2.ONE, PANEL).set_delay(delay)

static func reveal_rows(root: Node, reduced := false) -> void:
	if reduced: return
	var index := 0
	for child in root.get_children():
		if child is Control and child.visible:
			child.modulate.a = 0.0
			var tween := child.create_tween().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
			tween.tween_property(child, "modulate:a", 1.0, CONTROL).set_delay(minf(index * 0.025, 0.15))
			index += 1
