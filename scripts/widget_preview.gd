extends Control

const Design = preload("res://scripts/settings_design.gd")
var kind := "clock"
var total := 0
var font := SystemFont.new()
var last_second := -1

func _ready() -> void:
	font.font_names = PackedStringArray(["Segoe UI", "Microsoft YaHei UI"])
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if custom_minimum_size == Vector2.ZERO:
		custom_minimum_size = Vector2(96, 96) if kind == "clock" else Vector2(146, 76)

func _process(_delta: float) -> void:
	if kind != "clock": return
	var second := int(Time.get_datetime_dict_from_system().second)
	if second != last_second:
		last_second = second
		queue_redraw()

func _draw() -> void:
	var bounds := Rect2(Vector2.ZERO, size)
	if kind == "clock":
		draw_style_box(Design.outline(Color("fafafc"), 17, Color(1, 1, 1, 0.65)), bounds)
		var center := size * Vector2(0.5, 0.48)
		var radius := minf(size.x, size.y) * 0.38
		for i in range(12):
			var angle := float(i) * TAU / 12.0 - PI / 2.0
			var point := center + Vector2(cos(angle), sin(angle)) * radius
			draw_circle(point, 1.6 if i % 3 == 0 else 0.9, Color("252527") if i % 3 == 0 else Color("aeb3bb"))
		var now := Time.get_datetime_dict_from_system()
		var hour_angle := (float(now.hour % 12) + float(now.minute) / 60.0) * TAU / 12.0 - PI / 2.0
		var minute_angle := (float(now.minute) + float(now.second) / 60.0) * TAU / 60.0 - PI / 2.0
		var second_angle := float(now.second) * TAU / 60.0 - PI / 2.0
		draw_line(center, center + Vector2(cos(hour_angle), sin(hour_angle)) * radius * 0.48, Color("252527"), 2.4, true)
		draw_line(center, center + Vector2(cos(minute_angle), sin(minute_angle)) * radius * 0.76, Color("252527"), 1.8, true)
		draw_line(center, center + Vector2(cos(second_angle), sin(second_angle)) * radius * 0.9, Design.TINT, 1.1, true)
		draw_circle(center, 2.2, Color("252527"))
		var label := "%02d月%02d日  %02d:%02d" % [now.month, now.day, now.hour, now.minute]
		var label_rect := Rect2(size.x * 0.1, size.y * 0.68, size.x * 0.8, size.y * 0.13)
		draw_style_box(Design.rounded(Color("fafafc"), 6), label_rect)
		draw_string(font, Vector2(5, size.y * 0.79), label, HORIZONTAL_ALIGNMENT_CENTER, size.x - 10, 9, Design.TEXT)
	else:
		draw_style_box(Design.rounded(Design.TINT, 18), bounds)
		var right := Design.rounded(Color("e8ebf0"), 18)
		right.corner_radius_top_left = 0
		right.corner_radius_bottom_left = 0
		draw_style_box(right, Rect2(size.x * 0.75, 0, size.x * 0.25, size.y))
		var number := _format(total)
		var font_size := 34
		while font_size > 17 and font.get_string_size(number, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x > size.x - 16: font_size -= 1
		draw_string(font, Vector2(8, size.y * 0.71), number, HORIZONTAL_ALIGNMENT_CENTER, size.x - 16, font_size, Color("182131"))
		draw_string(font, Vector2(10, 14), "今日按键", HORIZONTAL_ALIGNMENT_LEFT, -1, 9, Color("182131"))

func _format(value: int) -> String:
	return str(value)
