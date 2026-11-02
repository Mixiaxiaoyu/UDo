extends Control

const Design = preload("res://scripts/settings_design.gd")
var keys: Array[Dictionary] = []
var counts: Dictionary = {}
var names: Dictionary = {}
var hovered := -1
var last_hovered := -1
var fading_hover := -1
var hover_progress := 1.0
var loaded := false
var font := SystemFont.new()

func _ready() -> void:
	font.font_names = PackedStringArray(["Segoe UI", "Microsoft YaHei UI"])
	font.font_weight = 400
	mouse_filter = Control.MOUSE_FILTER_STOP
	focus_mode = Control.FOCUS_ALL
	resized.connect(_layout)
	mouse_exited.connect(func(): hovered = -1; queue_redraw())
	focus_exited.connect(func(): hovered = -1; queue_redraw())
	_layout()

func _process(delta: float) -> void:
	if hovered != last_hovered:
		fading_hover = last_hovered
		last_hovered = hovered
		hover_progress = 0.0
	if hover_progress < 1.0:
		hover_progress = minf(1.0, hover_progress + delta / 0.16)
		queue_redraw()

func _key(code: int, text: String, x: float, y: float, w := 1.0, h := 1.0) -> void:
	var pitch := size.x / 23.0
	var start := Vector2(x * pitch, 34.0 + y * pitch)
	var end := start + Vector2(w * pitch - 4, h * pitch - 4)
	keys.append({"code": code, "text": text, "rect": Rect2(start.round(), (end.round() - start.round()))})

func _layout() -> void:
	keys.clear()
	_key(27, "Esc", 0, 0)
	for i in range(12):
		_key(112 + i, "F%d" % (i + 1), 2.0 + i + floorf(i / 4.0) * 0.5, 0)
	for i in range(3): _key([44, 145, 19][i], ["PrtSc", "Scroll", "Pause"][i], 15.5 + i, 0)
	_key(192, "`", 0, 1.5)
	for i in range(10): _key(48 + (i + 1) % 10, str((i + 1) % 10), 1 + i, 1.5)
	_key(189, "−", 11, 1.5)
	_key(187, "=", 12, 1.5)
	_key(8, "Backspace", 13, 1.5, 2)
	_key(9, "Tab", 0, 2.5, 1.5)
	for i in range(10): _key("QWERTYUIOP".unicode_at(i), "QWERTYUIOP"[i], 1.5 + i, 2.5)
	_key(219, "[", 11.5, 2.5)
	_key(221, "]", 12.5, 2.5)
	_key(220, "\\", 13.5, 2.5, 1.5)
	_key(20, "Caps", 0, 3.5, 1.75)
	for i in range(9): _key("ASDFGHJKL".unicode_at(i), "ASDFGHJKL"[i], 1.75 + i, 3.5)
	_key(186, ";", 10.75, 3.5)
	_key(222, "'", 11.75, 3.5)
	_key(13, "Enter", 12.75, 3.5, 2.25)
	_key(160, "Shift", 0, 4.5, 2.25)
	for i in range(7): _key("ZXCVBNM".unicode_at(i), "ZXCVBNM"[i], 2.25 + i, 4.5)
	_key(188, ",", 9.25, 4.5)
	_key(190, ".", 10.25, 4.5)
	_key(191, "/", 11.25, 4.5)
	_key(161, "Shift", 12.25, 4.5, 2.75)
	_key(162, "Ctrl", 0, 5.5, 1.25)
	_key(91, "Win", 1.25, 5.5, 1.25)
	_key(164, "Alt", 2.5, 5.5, 1.25)
	_key(32, "Space", 3.75, 5.5, 6.25)
	_key(165, "Alt", 10, 5.5, 1.25)
	_key(92, "Win", 11.25, 5.5, 1.25)
	_key(93, "Menu", 12.5, 5.5, 1.25)
	_key(163, "Ctrl", 13.75, 5.5, 1.25)
	for i in range(3):
		_key([45, 36, 33][i], ["Ins", "Home", "PgUp"][i], 15.5 + i, 1.5)
		_key([46, 35, 34][i], ["Del", "End", "PgDn"][i], 15.5 + i, 2.5)
	_key(38, "↑", 16.5, 4.5)
	for i in range(3): _key([37, 40, 39][i], ["←", "↓", "→"][i], 15.5 + i, 5.5)
	for i in range(4): _key([144, 111, 106, 109][i], ["Num", "/", "×", "−"][i], 19 + i, 1.5)
	for row in range(3):
		for col in range(3):
			var digit := 7 - row * 3 + col
			_key(96 + digit, str(digit), 19 + col, 2.5 + row)
	_key(107, "+", 22, 2.5, 1, 2)
	_key(269, "Enter", 22, 4.5, 1, 2)
	_key(96, "0", 19, 5.5, 2)
	_key(110, ".", 21, 5.5)
	# Rare/media/F13–F24 keys remain accessible when they have counts, below
	# the standard keyboard. Only this exceptional overflow needs scrolling.
	var known := {}
	for key in keys: known[int(key.code)] = true
	var extra: Array[int] = []
	for code in counts:
		if not known.has(int(code)) and int(counts[code]) > 0: extra.append(int(code))
	extra.sort()
	for i in range(extra.size()):
		_key(extra[i], key_name(extra[i]), (i % 8) * 2.875, 7.0 + floorf(i / 8.0), 2.875)
	var rows := 6.5 if extra.is_empty() else 7.0 + ceilf(extra.size() / 8.0)
	custom_minimum_size.y = ceilf(34 + rows * size.x / 23.0)
	if hovered >= keys.size(): hovered = -1
	if fading_hover >= keys.size(): fading_hover = -1
	queue_redraw()

func set_counts(value: Dictionary, ready_value := true) -> void:
	if counts == value and loaded == ready_value: return
	counts = value.duplicate()
	loaded = ready_value
	_layout()

func key_name(code: int) -> String:
	if code >= 65 and code <= 90 or code >= 48 and code <= 57: return String.chr(code)
	if code >= 96 and code <= 105: return "数字区 %d" % (code - 96)
	if code >= 112 and code <= 135: return "F%d" % (code - 111)
	return str(names.get(code, "键 0x%02X" % code))

func hover_text() -> String:
	if hovered < 0 or hovered >= keys.size(): return ""
	var code := int(keys[hovered].code)
	var digits := str(int(counts.get(str(code), 0)))
	var formatted := ""
	for i in range(digits.length()):
		if i > 0 and (digits.length() - i) % 3 == 0: formatted += ","
		formatted += digits[i]
	return "%s · %s 次" % [key_name(code), formatted] if loaded else "%s · 加载中" % key_name(code)

func hit_test(point: Vector2) -> int:
	for i in range(keys.size()):
		if keys[i].rect.has_point(point): return i
	return -1

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var next := hit_test(event.position)
		if next != hovered: hovered = next; queue_redraw()
	elif event is InputEventKey and event.pressed:
		if event.keycode in [KEY_RIGHT, KEY_DOWN, KEY_LEFT, KEY_UP]:
			hovered = posmod(hovered + (-1 if event.keycode in [KEY_LEFT, KEY_UP] else 1), keys.size())
			queue_redraw()
			accept_event()

func _draw() -> void:
	for i in range(keys.size()):
		var key: Dictionary = keys[i]
		var rect: Rect2 = key.rect
		var strength := (hover_progress if i == hovered else (1.0 - hover_progress if i == fading_hover else 0.0))
		if bool(get_meta("reduce_motion", false)): strength = 1.0 if i == hovered else 0.0
		var style := Design.outline(Color.WHITE.lerp(Color(Design.TINT, 0.06), strength), 5, Color(0, 0, 0, 0.13).lerp(Color(Design.TINT, 0.65), strength), Color.WHITE.blend(Design.GROUP))
		style.corner_detail = 12
		draw_style_box(style, rect)
		var font_size := 11
		while font_size > 8 and font.get_string_size(key.text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x > rect.size.x - 5: font_size -= 1
		var extent := font.get_string_size(key.text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size)
		var baseline := rect.position + Vector2((rect.size.x - extent.x) / 2, (rect.size.y - font.get_height(font_size)) / 2 + font.get_ascent(font_size))
		draw_string(font, baseline.round(), key.text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Design.TEXT)
	var hint := hover_text()
	if hint.is_empty(): return
	var key_rect: Rect2 = keys[hovered].rect
	var width := font.get_string_size(hint, HORIZONTAL_ALIGNMENT_LEFT, -1, 13).x + 20
	# The reserved top strip keeps the count readable without covering other keys.
	var visible_top := float(get_parent().scroll_vertical) if get_parent() is ScrollContainer else 0.0
	var tip := Rect2(clampf(key_rect.get_center().x - width / 2, 0, size.x - width), visible_top + 2, width, 24)
	var bubble := Design.outline(Color.WHITE, 6, Color(0, 0, 0, 0.08))
	bubble.corner_detail = 12
	draw_style_box(bubble, tip)
	draw_string(font, tip.position + Vector2(10, 17), hint, HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Design.TEXT)
