extends Control

signal chosen(id: String)
var zones: Array = []
var selected_id := ""
var hovered_id := ""
var hover_strength := 0.0
var screen_ratio := 16.0 / 9.0
const Design = preload("res://scripts/settings_design.gd")

func _ready() -> void:
	focus_mode = Control.FOCUS_ALL
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	resized.connect(queue_redraw)
	mouse_exited.connect(func(): hovered_id = ""; queue_redraw())

func _process(delta: float) -> void:
	var target := 1.0 if not hovered_id.is_empty() else 0.0
	var next := target if bool(get_meta("reduce_motion", false)) else lerpf(hover_strength, target, 1.0 - exp(-delta * 18.0))
	if absf(next - hover_strength) > 0.001:
		hover_strength = next
		queue_redraw()

func _screen_rect() -> Rect2:
	var width := minf(size.x - 16, (size.y - 16) * screen_ratio)
	var dimensions := Vector2(width, width / screen_ratio)
	return Rect2((size - dimensions) / 2, dimensions)

func _draw() -> void:
	draw_style_box(Design.rounded(Color("eef1f6"), 12), Rect2(Vector2.ZERO, size))
	var font := get_theme_default_font()
	var screen := _screen_rect()
	for zone in zones:
		var rect := Rect2(screen.position + Vector2(zone.X, zone.Y) * screen.size, Vector2(zone.W, zone.H) * screen.size)
		var highlight := 1.0 if zone.Id == selected_id else (hover_strength * 0.65 if zone.Id == hovered_id else 0.0)
		draw_style_box(Design.outline(Color("e2eafa").lerp(Color("d5e8ff"), highlight), 6, Color("becbe0").lerp(Design.TINT, highlight)), rect)
		if rect.size.x > 45 and rect.size.y > 24:
			draw_string(font, rect.position + Vector2(8, 19), str(zone.Name), HORIZONTAL_ALIGNMENT_LEFT, maxf(1, rect.size.x - 16), 12, Design.TEXT)
	if zones.is_empty(): draw_string(font, Vector2(18, 34), "还没有分区，点击「绘制分区」开始", HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Design.SECONDARY)

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var next_hover := ""
		var screen := _screen_rect()
		for zone in zones:
			if Rect2(screen.position + Vector2(zone.X, zone.Y) * screen.size, Vector2(zone.W, zone.H) * screen.size).has_point(event.position):
				next_hover = str(zone.Id)
				break
		if next_hover != hovered_id:
			hovered_id = next_hover
			queue_redraw()
	if event is InputEventKey and event.pressed and not zones.is_empty():
		var index := -1
		for i in zones.size():
			if zones[i].Id == selected_id: index = i
		if event.keycode in [KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN]:
			var backwards: bool = event.keycode in [KEY_LEFT, KEY_UP]
			index = (zones.size() - 1 if backwards else 0) if index < 0 else posmod(index + (-1 if backwards else 1), zones.size())
			selected_id = zones[index].Id
			queue_redraw()
			accept_event()
		elif event.keycode in [KEY_ENTER, KEY_SPACE] and index >= 0:
			chosen.emit(selected_id)
			accept_event()
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		var screen := _screen_rect()
		for zone in zones:
			if Rect2(screen.position + Vector2(zone.X, zone.Y) * screen.size, Vector2(zone.W, zone.H) * screen.size).has_point(event.position):
				chosen.emit(str(zone.Id))
				accept_event()
				return
