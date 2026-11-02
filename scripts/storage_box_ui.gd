extends RefCounted

# Protocol/presentation adapter. Reuses the Dock's form, controls and menu component.
const Design = preload("res://scripts/settings_design.gd")
const Switch = preload("res://scripts/dock_switch.gd")
const BoxSlider = preload("res://scripts/dock_slider.gd")
const Choice = preload("res://scripts/dock_choice.gd")

static func handle(dock: Control, message: Dictionary) -> void:
	match str(message.get("kind", "")):
		"settings": _settings(dock, message.box)
		"menu": _menu(dock, message)
		"error": dock._show_notice(str(message.message))
		"dismiss_menu":
			if is_instance_valid(dock.context_menu): dock.context_menu.close()
		"tray":
			dock._show_menu([
				{"text": "有兜设置", "run": dock._open_settings},
				{"text": "新建收纳盒子", "run": func(): dock._send({"op": "box_create", "x": message.x, "y": message.y})},
				{"text": "无感桌面分区…", "run": func(): dock._send({"op": "partition_settings"})},
				{"separator": true},
				{"text": "恢复 Windows 任务栏", "run": func(): dock.prefs.hide_taskbar = false; dock._send({"op": "hide", "value": false}); dock._save()},
				{"text": "退出有兜", "run": dock._quit}
			], dock._native_point(message.x, message.y))

static func _command(dock: Control, box: Dictionary, op: String, extras := {}) -> void:
	var command := {"op": op, "id": box.Id}
	command.merge(extras, true)
	dock._send(command)

static func _menu(dock: Control, message: Dictionary) -> void:
	var box: Dictionary = message.box
	var rows: Array = []
	if not str(message.item).is_empty():
		rows.append({"text": "打开", "run": func(): _command(dock, box, "box_open", {"item": message.item})})
		rows.append({"text": "移回桌面", "run": func(): _command(dock, box, "box_release", {"item": message.item})})
		rows.append({"separator": true})
	rows.append({"text": "新建收纳盒子", "run": func(): dock._send({"op": "box_create", "x": message.x, "y": message.y})})
	rows.append({"text": "收纳盒设置…", "run": func(): _command(dock, box, "box_edit")})
	rows.append({"text": "展开盒子" if box.Collapsed else "折叠盒子", "run": func(): _command(dock, box, "box_fold")})
	rows.append({"text": "显示名称", "checked": box.ShowName, "run": func(): _command(dock, box, "box_update", {"show_name": not box.ShowName})})
	rows.append({"separator": true})
	rows.append({"text": "图标排列", "children": [
		{"text": "按名称", "run": func(): _command(dock, box, "box_sort", {"by": "name"})},
		{"text": "按类型", "run": func(): _command(dock, box, "box_sort", {"by": "type"})}
	]})
	var screens: Array = []
	for screen in message.screens:
		screens.append({"text": screen.name, "checked": screen.id == box.Monitor, "run": func(): _command(dock, box, "box_monitor", {"monitor": screen.id})})
	if screens.size() > 1: rows.append({"text": "移动到显示器", "children": screens})
	rows.append({"separator": true})
	rows.append({"text": "删除盒子，保留文件", "run": func(): _command(dock, box, "box_delete")})
	dock._show_menu(rows, dock._native_point(message.x, message.y))
	_command(dock, box, "box_ui_open")

static func _settings(dock: Control, box: Dictionary) -> void:
	var body: VBoxContainer = dock._new_panel("box_settings", 500)
	dock.active_box_editor = str(box.Id)
	_command(dock, box, "box_ui_open")
	var heading := HBoxContainer.new()
	body.add_child(heading)
	var title: Label = dock._label("收纳盒设置", 15, Design.TEXT)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.gui_input.connect(dock._settings_title_input)
	heading.add_child(title)
	var group: VBoxContainer = dock._form_group(body)
	var name := LineEdit.new()
	name.text = str(box.Name)
	name.max_length = 60
	name.context_menu_enabled = false
	name.custom_minimum_size = Vector2(230, 24)
	name.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	name.add_theme_font_size_override("font_size", 13)
	name.add_theme_color_override("font_color", Design.TEXT)
	name.add_theme_color_override("caret_color", Design.TEXT)
	name.add_theme_color_override("selection_color", Color("b8d5ff"))
	var field := Design.outline(Color.WHITE, 6, Color(0, 0, 0, 0.08))
	field.content_margin_left = 8
	field.content_margin_right = 8
	name.add_theme_stylebox_override("normal", field)
	name.add_theme_stylebox_override("focus", Design.focus())
	dock._row(group, "名称").add_child(name)
	name.text_submitted.connect(func(value: String): _command(dock, box, "box_update", {"name": value}))
	name.focus_exited.connect(func(): _command(dock, box, "box_update", {"name": name.text}))
	var show_name := Switch.new()
	show_name.button_pressed = box.ShowName
	dock._row(group, "显示名称").add_child(show_name)
	show_name.toggled.connect(func(value: bool): _command(dock, box, "box_update", {"show_name": value}))
	var material_group: VBoxContainer = dock._form_group(body)
	var material := Choice.new()
	material.add_item("浅色玻璃")
	material.add_item("深色玻璃")
	material.selected = 1 if box.Theme == "dark" else 0
	dock._row(material_group, "材质").add_child(material)
	material.item_selected.connect(func(index: int): _command(dock, box, "box_update", {"theme": "dark" if index == 1 else "light"}))
	var opacity_row: HBoxContainer = dock._row(material_group, "透明度")
	var slider := BoxSlider.new()
	slider.min_value = 0
	slider.max_value = 90
	slider.step = 1
	slider.value = roundf(float(box.Transparency) * 100)
	slider.custom_minimum_size = Vector2(180, 24)
	opacity_row.add_child(slider)
	var value_label: Label = dock._label("%d%%" % slider.value, 13, Design.SECONDARY)
	value_label.custom_minimum_size.x = 38
	opacity_row.add_child(value_label)
	slider.value_changed.connect(func(value: float):
		value_label.text = "%d%%" % value
		_command(dock, box, "box_update", {"transparency": value / 100.0}))
	var layout_group: VBoxContainer = dock._form_group(body)
	var fit := Switch.new()
	fit.button_pressed = box.AutoFit
	dock._row(layout_group, "尺寸自适应").add_child(fit)
	fit.toggled.connect(func(value: bool): _command(dock, box, "box_update", {"auto_fit": value}))
	dock._row(layout_group, "收起内容").add_child(dock._button("展开" if box.Collapsed else "折叠", func(): _command(dock, box, "box_fold"); dock._close_panel()))
	body.add_child(dock._label("拖到屏幕边缘即可收起，鼠标悬停白条展开。", 12, Design.SECONDARY))
	heading.add_child(dock._button("完成", func(): _command(dock, box, "box_update", {"name": name.text}); dock._close_panel()))
