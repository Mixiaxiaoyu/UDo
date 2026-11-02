extends VBoxContainer

signal back_requested
signal date_requested(date: String)
signal recording_changed(enabled: bool)
signal title_pressed(event: InputEvent)

const Design = preload("res://scripts/settings_design.gd")
const DockSwitch = preload("res://scripts/dock_switch.gd")
const KeyboardDiagram = preload("res://scripts/keyboard_statistics.gd")
const DateArrow = preload("res://scripts/date_arrow.gd")
const NAMES := {8: "退格", 9: "Tab", 13: "回车", 16: "Shift", 17: "Ctrl", 18: "Alt", 19: "Pause", 20: "Caps Lock", 27: "Esc", 32: "空格", 33: "Page Up", 34: "Page Down", 35: "End", 36: "Home", 37: "←", 38: "↑", 39: "→", 40: "↓", 44: "Print Screen", 45: "Insert", 46: "Delete", 91: "左 Win", 92: "右 Win", 93: "菜单", 106: "数字区 ×", 107: "数字区 +", 109: "数字区 −", 110: "数字区 .", 111: "数字区 /", 144: "Num Lock", 145: "Scroll Lock", 160: "左 Shift", 161: "右 Shift", 162: "左 Ctrl", 163: "右 Ctrl", 164: "左 Alt", 165: "右 Alt", 173: "静音", 174: "音量 −", 175: "音量 +", 176: "下一曲", 177: "上一曲", 178: "停止", 179: "播放 / 暂停", 186: "; :", 187: "= +", 188: ", <", 189: "− _", 190: ". >", 191: "/ ?", 192: "` ~", 219: "[ {", 220: "\\ |", 221: "] }", 222: "' \"", 269: "数字区回车"}
var recording := true
var selected_date := ""
var date_field: LineEdit
var total_label: Label
var note: Label
var next_button: Button
var keyboard: KeyboardDiagram

func _ready() -> void:
	add_theme_constant_override("separation", 10)
	var heading := HBoxContainer.new()
	add_child(heading)
	var title := label("按键统计", 15)
	var heading_font := SystemFont.new()
	heading_font.font_names = PackedStringArray(["Microsoft YaHei UI", "Segoe UI"])
	heading_font.font_weight = 700
	title.add_theme_font_override("font", heading_font)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.mouse_filter = Control.MOUSE_FILTER_STOP
	title.gui_input.connect(func(event: InputEvent): title_pressed.emit(event))
	heading.add_child(title)
	heading.add_child(button("返回", func(): back_requested.emit()))
	var record_row := row_group("记录按键次数")
	var toggle := DockSwitch.new()
	toggle.button_pressed = recording
	toggle.toggled.connect(func(value: bool): recording = value; recording_changed.emit(value))
	record_row.add_child(toggle)
	var date_row := HBoxContainer.new()
	date_row.add_theme_constant_override("separation", 8)
	add_child(date_row)
	var date_surface := PanelContainer.new()
	var date_style := Design.outline(Color.WHITE, 6, Color(0, 0, 0, 0.06))
	date_style.set_content_margin_all(2)
	date_style.corner_detail = 16
	date_surface.add_theme_stylebox_override("panel", date_style)
	date_row.add_child(date_surface)
	var date_controls := HBoxContainer.new()
	date_controls.add_theme_constant_override("separation", 0)
	date_surface.add_child(date_controls)
	var previous := DateArrow.new()
	previous.pressed.connect(func(): change_day(-1))
	previous.tooltip_text = "前一天"
	date_controls.add_child(previous)
	date_field = LineEdit.new()
	date_field.custom_minimum_size = Vector2(130, 24)
	date_field.max_length = 10
	date_field.alignment = HORIZONTAL_ALIGNMENT_CENTER
	date_field.tooltip_text = "输入日期，例如 2026-10-25，按回车查看"
	var field := Design.rounded(Color.TRANSPARENT, 4)
	field.content_margin_left = 8
	field.content_margin_right = 8
	date_field.add_theme_stylebox_override("normal", field)
	date_field.add_theme_stylebox_override("focus", Design.focus())
	date_field.add_theme_font_size_override("font_size", 13)
	date_field.add_theme_color_override("font_color", Design.TEXT)
	date_field.add_theme_color_override("font_shadow_color", Color.TRANSPARENT)
	date_field.add_theme_constant_override("outline_size", 0)
	date_field.text_submitted.connect(select_date)
	date_controls.add_child(date_field)
	next_button = DateArrow.new()
	next_button.direction = 1
	next_button.tooltip_text = "后一天"
	next_button.pressed.connect(func(): change_day(1))
	date_controls.add_child(next_button)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	date_row.add_child(spacer)
	date_row.add_child(button("今天", func(): select_date(Time.get_date_string_from_system())))
	var total_row := row_group("当日总次数")
	total_label = label("0 次", 15)
	total_row.add_child(total_label)
	var surface := PanelContainer.new()
	var fill := Design.rounded(Design.GROUP, Design.GROUP_RADIUS)
	fill.set_content_margin_all(10)
	surface.add_theme_stylebox_override("panel", fill)
	add_child(surface)
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size.y = 234
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	surface.add_child(scroll)
	keyboard = KeyboardDiagram.new()
	keyboard.names = NAMES
	keyboard.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(keyboard)
	scroll.get_v_scroll_bar().value_changed.connect(func(_value: float): keyboard.hovered = -1; keyboard.queue_redraw())
	note = label("", 11, Design.SECONDARY)
	note.hide()
	add_child(note)
	select_date(Time.get_date_string_from_system())

func label(text: String, font_size := 13, color := Design.TEXT) -> Label:
	var result := Label.new()
	result.text = text
	result.add_theme_font_size_override("font_size", font_size)
	result.add_theme_color_override("font_color", color)
	result.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	return result

func button(text: String, action: Callable) -> Button:
	var result := Button.new()
	result.text = text
	result.custom_minimum_size.y = 24
	result.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	for state in ["normal", "hover", "pressed", "disabled"]:
		result.add_theme_stylebox_override(state, Design.button())
	result.add_theme_color_override("font_disabled_color", Color(0, 0, 0, 0.25))
	for state in ["font_color", "font_hover_color", "font_pressed_color"]:
		result.add_theme_color_override(state, Design.TEXT)
	result.add_theme_stylebox_override("focus", Design.focus())
	result.add_theme_font_size_override("font_size", 13)
	result.pressed.connect(action)
	return result

func row_group(text: String) -> HBoxContainer:
	var group := PanelContainer.new()
	var style := Design.rounded(Design.GROUP, Design.GROUP_RADIUS)
	style.content_margin_left = 10
	style.content_margin_right = 10
	group.add_theme_stylebox_override("panel", style)
	add_child(group)
	var row := HBoxContainer.new()
	row.custom_minimum_size.y = 41
	group.add_child(row)
	var title := label(text)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(title)
	return row

static func valid_date(value: String) -> bool:
	var regex := RegEx.new()
	regex.compile("^[0-9]{4}-[0-9]{2}-[0-9]{2}$")
	if not regex.search(value): return false
	var parts := value.split("-")
	var year := int(parts[0])
	var month := int(parts[1])
	var day := int(parts[2])
	if year < 1970 or month < 1 or month > 12: return false
	var days := [31, 29 if year % 4 == 0 and (year % 100 != 0 or year % 400 == 0) else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
	return day >= 1 and day <= days[month - 1] and value <= Time.get_date_string_from_system()

func select_date(value: String) -> void:
	if not valid_date(value):
		date_field.text = selected_date
		note.text = "请输入有效日期（YYYY-MM-DD），不能晚于今天。"
		note.show()
		return
	selected_date = value
	note.hide()
	date_field.text = value
	next_button.disabled = value == Time.get_date_string_from_system()
	total_label.text = "…"
	keyboard.set_counts({}, false)
	date_requested.emit(value)

func change_day(offset: int) -> void:
	var stamp := Time.get_unix_time_from_datetime_string(selected_date + "T12:00:00") + offset * 86400
	select_date(Time.get_date_string_from_unix_time(stamp))

func update_report(report: Dictionary) -> void:
	if not is_node_ready(): return
	next_button.disabled = selected_date >= Time.get_date_string_from_system()
	if str(report.get("date", "")) != selected_date: return
	var error := str(report.get("error", ""))
	if not error.is_empty():
		note.text = error
		note.show()
		total_label.text = "暂不可用"
		keyboard.set_counts({}, false)
		return
	note.hide()
	total_label.text = "%s 次" % format_count(int(report.get("total", 0)))
	keyboard.set_counts(report.get("counts", {}))

static func key_name_for(code: int) -> String:
	if code >= 65 and code <= 90 or code >= 48 and code <= 57: return String.chr(code)
	if code >= 96 and code <= 105: return "数字区 %d" % (code - 96)
	if code >= 112 and code <= 135: return "F%d" % (code - 111)
	return str(NAMES.get(code, "键 0x%02X" % code))

static func format_count(count: int) -> String:
	var digits := str(count)
	var result := ""
	for i in range(digits.length()):
		if i > 0 and (digits.length() - i) % 3 == 0: result += ","
		result += digits[i]
	return result
