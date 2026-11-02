extends RefCounted

# Presentation adapter only. The native partition controller owns state and transactions.
const Design = preload("res://scripts/settings_design.gd")
const Switch = preload("res://scripts/dock_switch.gd")
const PartitionSlider = preload("res://scripts/dock_slider.gd")
const Choice = preload("res://scripts/dock_choice.gd")
const TEMPLATES := {"two": "双区", "three": "三栏", "four": "四宫格", "focus": "主辅布局", "vertical": "纵向三区"}

static func handle(dock: Control, message: Dictionary) -> void:
	match str(message.get("kind", "")):
		"notice": dock._show_notice(str(message.message))
		"menu": _menu(dock, message)
		"settings": _settings(dock, message)
		"organize": _organize(dock, message)
		"overflow": _overflow(dock, message)

static func _send(dock: Control, op: String, extras := {}) -> void:
	var command := {"op": "partition_" + op}
	command.merge(extras, true)
	dock._send(command)

static func _update(dock: Control, zone: Dictionary, field: String, value: Variant) -> void:
	_send(dock, "update", {"id": zone.Id, "field": field, "value": value})

static func _menu(dock: Control, message: Dictionary) -> void:
	var rows: Array = []
	var zone = message.get("zone")
	if zone is Dictionary:
		if not str(message.get("item", "")).is_empty():
			var item: String = message.item
			rows.append({"text": "打开", "run": func(): _send(dock, "open", {"id": zone.Id, "item": item})})
			rows.append({"text": "移回桌面", "run": func(): _send(dock, "release", {"id": zone.Id, "item": item})})
			var targets: Array = []
			for target in message.targets:
				if target.Id != zone.Id: targets.append({"text": target.Name, "run": func(): _send(dock, "transfer", {"id": zone.Id, "target": target.Id, "item": item})})
			if not targets.is_empty(): rows.append({"text": "移到分区", "children": targets})
			rows.append({"separator": true})
		rows.append({"text": "分区外观与名称…", "run": func(): _send(dock, "settings", {"id": zone.Id})})
		rows.append({"text": "显示名称", "checked": zone.style.TitleVisible, "run": func(): _update(dock, zone, "title_visible", not zone.style.TitleVisible)})
		rows.append({"text": "切换为无感样式", "run": func(): _update(dock, zone, "invisible", true)})
		rows.append({"text": "适应图标内容", "run": func(): _send(dock, "fit", {"id": zone.Id})})
		rows.append({"text": "查看全部文件…", "run": func(): _send(dock, "overflow", {"id": zone.Id})})
		rows.append({"text": "图标排列", "children": [
			{"text": "按名称", "run": func(): _send(dock, "sort", {"id": zone.Id, "by": "name"})},
			{"text": "按类型", "run": func(): _send(dock, "sort", {"id": zone.Id, "by": "type"})}
		]})
		rows.append({"separator": true})
	rows.append({"text": "新建无感分区", "run": func(): _send(dock, "canvas", {"monitor": message.monitor})})
	rows.append({"text": "完成分区编辑" if message.editing else "编辑分区", "run": func(): _send(dock, "edit")})
	rows.append({"text": "布局与显示器…", "run": func(): _send(dock, "settings", {"monitor": message.monitor})})
	rows.append({"text": "一键整理为分区…", "run": func(): _send(dock, "organize", {"monitor": message.monitor})})
	rows.append({"text": "撤销分区操作", "run": func(): _send(dock, "undo", {"monitor": message.monitor})})
	rows.append({"text": "Windows 桌面菜单…", "run": func(): _send(dock, "desktop_menu", {"id": zone.Id if zone is Dictionary else "", "x": message.x, "y": message.y})})
	if zone is Dictionary:
		rows.append({"separator": true})
		rows.append({"text": "复制空分区", "run": func(): _send(dock, "duplicate", {"id": zone.Id, "monitor": message.monitor})})
		rows.append({"text": "移出所有图标，保留文件", "run": func(): _send(dock, "clear", {"id": zone.Id, "monitor": message.monitor})})
		rows.append({"text": "删除分区，保留文件", "run": func(): _send(dock, "delete", {"id": zone.Id, "monitor": message.monitor})})
	dock._show_menu(rows, dock._native_point(message.x, message.y))

static func _heading(dock: Control, title_text: String) -> VBoxContainer:
	var body: VBoxContainer = dock._new_panel("partition_settings", 600)
	var heading := HBoxContainer.new()
	body.add_child(heading)
	var title: Label = dock._label(title_text, 15, Design.TEXT)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.gui_input.connect(dock._settings_title_input)
	heading.add_child(title)
	heading.add_child(dock._button("关闭", dock._close_panel))
	return body

static func _scroll(dock: Control, body: VBoxContainer) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size.y = 1
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	body.add_child(scroll)
	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 10)
	scroll.add_child(content)
	var panel_id: int = dock.panel.get_instance_id()
	content.minimum_size_changed.connect(func(): _fit_scroll.call_deferred(dock, scroll, content, panel_id))
	_fit_scroll.call_deferred(dock, scroll, content, panel_id)
	var bar := scroll.get_v_scroll_bar()
	bar.add_theme_stylebox_override("scroll", Design.rounded(Color.TRANSPARENT, 4))
	for key in ["grabber", "grabber_highlight", "grabber_pressed"]:
		var thumb := Design.rounded(Color(0, 0, 0, 0.18), 4)
		thumb.content_margin_left = 4
		thumb.content_margin_right = 4
		bar.add_theme_stylebox_override(key, thumb)
	for key in ["increment", "decrement", "increment_highlight", "decrement_highlight", "increment_pressed", "decrement_pressed"]:
		bar.add_theme_icon_override(key, ImageTexture.create_from_image(Image.create(1, 1, false, Image.FORMAT_RGBA8)))
	return content

static func _fit_scroll(dock: Control, scroll: ScrollContainer, content: VBoxContainer, panel_id: int) -> void:
	if not is_instance_valid(scroll) or not is_instance_valid(content) or not is_instance_valid(dock.panel) or dock.panel.get_instance_id() != panel_id: return
	var cap := minf(500, DisplayServer.screen_get_usable_rect().size.y - 220)
	var height := clampf(content.get_combined_minimum_size().y, 1, maxf(100, cap))
	if not is_equal_approx(scroll.custom_minimum_size.y, height):
		scroll.custom_minimum_size.y = height
		dock._finish_settings_layout.call_deferred(panel_id)

static func _field(value: String, callback: Callable, width := 230) -> LineEdit:
	var field := LineEdit.new()
	field.text = value
	field.context_menu_enabled = false
	field.custom_minimum_size = Vector2(width, 24)
	field.max_length = 60
	field.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	field.add_theme_font_size_override("font_size", 13)
	field.add_theme_color_override("font_color", Design.TEXT)
	field.add_theme_color_override("caret_color", Design.TEXT)
	field.add_theme_color_override("selection_color", Color("b8d5ff"))
	var style := Design.outline(Color.WHITE, 6, Color(0, 0, 0, 0.08))
	style.content_margin_left = 8
	style.content_margin_right = 8
	field.add_theme_stylebox_override("normal", style)
	field.add_theme_stylebox_override("focus", Design.focus())
	field.text_submitted.connect(callback)
	field.focus_exited.connect(func(): if is_instance_valid(field) and field.text != value: callback.call(field.text))
	return field

static func _choice(labels: Array, selected: int, callback: Callable) -> OptionButton:
	var choice := Choice.new()
	choice.fit_to_longest_item = false
	choice.clip_text = true
	choice.custom_minimum_size.x = 170
	for label in labels: choice.add_item(str(label))
	choice.selected = selected
	choice.item_selected.connect(callback)
	return choice

static func _slider(dock: Control, body: VBoxContainer, title: String, value: float, low: float, high: float, callback: Callable, unit := "px") -> void:
	var row: HBoxContainer = dock._row(body, title)
	var slider := PartitionSlider.new()
	slider.min_value = low
	slider.max_value = high
	slider.step = 1
	slider.value = value
	slider.custom_minimum_size = Vector2(190, 24)
	row.add_child(slider)
	var label: Label = dock._label("%d %s" % [value, unit], 12, Design.SECONDARY)
	label.custom_minimum_size.x = 50
	row.add_child(label)
	slider.value_changed.connect(func(v: float): label.text = "%d %s" % [v, unit]; callback.call(int(v)))

static func _toggle(dock: Control, body: VBoxContainer, title: String, value: bool, callback: Callable) -> void:
	var toggle := Switch.new()
	toggle.button_pressed = value
	dock._row(body, title).add_child(toggle)
	toggle.toggled.connect(callback)

static func _pick(dock: Control, title: String, callback: Callable, filters: Array, save := false) -> void:
	dock.app_picker = preload("res://scripts/app_picker.gd").new()
	dock.app_picker.open_for(dock, title, callback, filters, save, "UDo-分区备份-%d.json" % Time.get_unix_time_from_system())

static func _background_image(dock: Control, zone: Dictionary, path: String) -> void:
	if path.get_extension().to_lower() == "webp":
		var file := FileAccess.open(path, FileAccess.READ)
		if not file or file.get_length() > 2 * 1024 * 1024:
			dock._show_notice("图片无法读取或超过 2 MB。")
			return
		file.close()
		var image := Image.load_from_file(path)
		if not image or image.get_width() > 8192 or image.get_height() > 8192:
			dock._show_notice("图片格式或尺寸不受支持。")
			return
		path = dock.ipc.path_join("partition-background-%d.png" % Time.get_ticks_usec())
		if image.save_png(path) != OK:
			dock._show_notice("无法读取图片，请选择 PNG 或 JPG。")
			return
	_send(dock, "image", {"id": zone.Id, "path": path})

static func _primary(dock: Control, text: String, callback: Callable) -> Button:
	var button: Button = dock._button(text, callback)
	button.custom_minimum_size.y = 36
	for key in ["normal", "hover", "pressed"]:
		var style := Design.rounded(Design.TINT.darkened(0.12) if key == "pressed" else Design.TINT, 8)
		style.content_margin_left = 20
		style.content_margin_right = 20
		button.add_theme_stylebox_override(key, style)
	for key in ["font_color", "font_hover_color", "font_pressed_color"]: button.add_theme_color_override(key, Color.WHITE)
	return button

static func _canvas(dock: Control, message: Dictionary) -> void:
	_send(dock, "canvas", {"monitor": message.monitor})
	dock._close_panel()

static func _settings(dock: Control, message: Dictionary, tab := "") -> void:
	if tab.is_empty(): tab = str(message.get("tab", "style" if not str(message.selected).is_empty() else "layout"))
	if tab == "layout": message._details = false
	var titles := {"layout": "桌面分区", "style": "分区外观", "templates": "选择布局", "help": "使用帮助"}
	var body := _heading(dock, titles.get(tab, "桌面分区"))
	if tab != "layout":
		var back: Button = dock._button("返回所有分区", func(): _settings(dock, message, "layout"))
		back.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		body.add_child(back)
	var content := _scroll(dock, body)
	match tab:
		"style": _appearance(dock, content, message)
		"templates": _templates(dock, content, message)
		"help": _help(dock, content, message)
		_: _layout(dock, content, message)
	for field in body.find_children("*", "LineEdit", true, false): field.context_menu_enabled = false

static func _layout(dock: Control, body: VBoxContainer, message: Dictionary) -> void:
	var current := 0
	if message.screens.size() > 1:
		var names: Array = []
		for i in message.screens.size():
			names.append("显示器 %d%s" % [i + 1, "（主屏）" if str(message.screens[i].name).contains("主屏") else ""])
			if message.screens[i].id == message.monitor: current = i
		dock._row(dock._form_group(body), "正在查看").add_child(_choice(names, current, func(index: int): _send(dock, "settings", {"monitor": message.screens[index].id})))
	var intro := HBoxContainer.new()
	intro.add_theme_constant_override("separation", 20)
	intro.custom_minimum_size.y = 66
	body.add_child(intro)
	var words := VBoxContainer.new()
	words.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	words.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	intro.add_child(words)
	words.add_child(dock._label("正在调整桌面分区" if message.editing else "把桌面分成顺手的区域", 16, Design.TEXT))
	words.add_child(dock._label("完成后退出编辑，保留当前布局。" if message.editing else "打开画布，连续拖画矩形，最后统一应用。", 12, Design.SECONDARY))
	intro.add_child(_primary(dock, "完成编辑" if message.editing else "绘制分区", func():
		if message.editing:
			_send(dock, "edit", {"monitor": message.monitor})
			dock._close_panel()
		else: _canvas(dock, message)))
	var preview := preload("res://scripts/partition_preview.gd").new()
	preview.zones = message.zones
	preview.custom_minimum_size.y = 210
	preview.selected_id = message.selected
	if not message.screens.is_empty(): preview.screen_ratio = float(message.screens[current].w) / float(message.screens[current].h)
	preview.chosen.connect(func(id: String): message.selected = id; _settings(dock, message, "style"))
	body.add_child(preview)
	if not message.zones.is_empty():
		var selection := HBoxContainer.new()
		body.add_child(selection)
		var hint: Label = dock._label("点选上方区域，修改名称和外观", 12, Design.SECONDARY)
		hint.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		selection.add_child(hint)
		var choices: Array = ["选择分区…"]
		choices.append_array(message.zones.map(func(z: Dictionary): return z.Name))
		selection.add_child(_choice(choices, 0, func(index: int):
			if index > 0:
				message.selected = message.zones[index - 1].Id
				_settings(dock, message, "style")))
	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation", 10)
	body.add_child(actions)
	actions.add_child(dock._button("自动分类整理…", func(): _send(dock, "organize", {"monitor": message.monitor})))
	actions.add_child(dock._button("更多", func(): _layout_more(dock, message)))
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	actions.add_child(spacer)
	if int(message.get("undo", 0)) > 0: actions.add_child(dock._button("撤销上次调整", func(): _send(dock, "undo", {"monitor": message.monitor})))

static func _layout_more(dock: Control, message: Dictionary) -> void:
	dock._show_menu([
		{"text": "使用布局模板…", "run": func(): _settings(dock, message, "templates")},
		{"text": "重新绘制分区…", "run": func(): _canvas(dock, message)},
		{"separator": true},
		{"text": "导出布局…", "run": func(): _pick(dock, "导出分区布局", func(path: String): _send(dock, "export", {"path": path}), ["json"], true)},
		{"text": "导入布局…", "run": func(): _pick(dock, "导入分区布局（可撤销）", func(path: String): _send(dock, "import", {"path": path, "monitor": message.monitor}), ["json"])},
		{"separator": true},
		{"text": "使用帮助", "run": func(): _settings(dock, message, "help")}
	])

static func _templates(dock: Control, body: VBoxContainer, message: Dictionary) -> void:
	body.add_child(dock._label("选择后替换当前屏幕布局，可撤销。", 12, Design.SECONDARY))
	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 10)
	body.add_child(grid)
	for id in TEMPLATES:
		var button: Button = dock._button(TEMPLATES[id], func(): _send(dock, "template", {"monitor": message.monitor, "template": id}))
		button.custom_minimum_size = Vector2(265, 46)
		grid.add_child(button)
	if not str(message.template).is_empty(): _slider(dock, dock._form_group(body), "分区间距", message.gap, 4, 48, func(v: int): _send(dock, "gap", {"monitor": message.monitor, "value": v}))

static func _help(dock: Control, body: VBoxContainer, message: Dictionary) -> void:
	for text in ["绘制分区：空白处拖画矩形，点击完成后统一应用。", "调整分区：点击选中，拖动内部移动、边角缩放。", "相邻分区：拖动公共分隔线，两侧同步调整。", "修改外观：在布局预览中点选一个区域。", "系统右键：直接右键分区空白处。", "分区右键：按住 Ctrl，再右键空白处。", "每个显示器独立保存布局。", "删除分区仅解除归属，文件保留在原处。"]:
		body.add_child(dock._label(text, 13, Design.TEXT))
	if int(message.get("offline", 0)) > 0: body.add_child(dock._label("已保留 %d 个离线显示器的布局。" % message.offline, 12, Design.SECONDARY))

static func _appearance(dock: Control, body: VBoxContainer, message: Dictionary) -> void:
	if message.zones.is_empty():
		body.add_child(dock._label("还没有分区，先画一个吧。", 14, Design.TEXT))
		body.add_child(_primary(dock, "绘制分区", func(): _canvas(dock, message)))
		return
	var current := 0
	for i in message.zones.size():
		if message.zones[i].Id == message.selected: current = i
	var zone: Dictionary = message.zones[current]
	var style: Dictionary = zone.style
	var group: VBoxContainer = dock._form_group(body)
	dock._row(group, "分区名称").add_child(_field(zone.Name, func(value: String): zone.Name = value; _update(dock, zone, "name", value)))
	_toggle(dock, group, "在桌面显示名称", style.TitleVisible, func(value: bool): style.TitleVisible = value; _update(dock, zone, "title_visible", value); _settings(dock, message, "style"))
	dock._row(group, "背景").add_child(_choice(["透明（推荐）", "纯色", "图片"], ["none", "color", "image"].find(style.Background), func(index: int):
		style.Background = ["none", "color", "image"][index]
		_update(dock, zone, "background", style.Background)
		_settings(dock, message, "style")))
	if style.Background == "image":
		dock._row(group, "背景图片").add_child(dock._button("选择图片…", func(): _pick(dock, "选择分区背景图片", func(path: String): _background_image(dock, zone, path), ["png", "jpg", "jpeg", "webp", "gif", "bmp"])))
		if style.has_image: dock._row(group, "当前图片").add_child(dock._button("移除图片", func(): style.has_image = false; style.Background = "none"; _update(dock, zone, "clear_image", true); _settings(dock, message, "style")))
	if style.Background == "color": _color(dock, group, "背景颜色", zone, "color", style.Color)
	_toggle(dock, group, "图标与桌面一致", style.get("FollowDesktop", true), func(v: bool): style.FollowDesktop = v; _update(dock, zone, "follow_desktop", v); _settings(dock, message, "style"))
	var details := bool(message.get("_details", false))
	var expand: Button = dock._button("收起更多外观选项" if details else "更多外观选项", func(): message._details = not details; _settings(dock, message, "style"))
	expand.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	body.add_child(expand)
	if details:
		var advanced: VBoxContainer = dock._form_group(body)
		if style.TitleVisible:
			dock._row(advanced, "名称对齐").add_child(_choice(["左对齐", "居中", "右对齐"], ["left", "center", "right"].find(style.TitleAlign), func(index: int): style.TitleAlign = ["left", "center", "right"][index]; _update(dock, zone, "title_align", style.TitleAlign)))
			_color(dock, advanced, "名称颜色", zone, "title_color", style.TitleColor)
		if style.Background != "none": _slider(dock, advanced, "背景不透明度", style.Opacity, 0, 100, func(v: int): style.Opacity = v; _update(dock, zone, "opacity", v), "%")
		_toggle(dock, advanced, "显示边框", style.Border, func(v: bool): style.Border = v; _update(dock, zone, "border", v); _settings(dock, message, "style"))
		if style.Border: _color(dock, advanced, "边框颜色", zone, "border_color", style.BorderColor)
		if style.Border or style.Background != "none": _slider(dock, advanced, "圆角", style.Radius, 0, 28, func(v: int): style.Radius = v; _update(dock, zone, "radius", v))
		if not style.get("FollowDesktop", true): _slider(dock, advanced, "图标尺寸", style.IconSize, 28, 64, func(v: int): style.IconSize = v; _update(dock, zone, "icon_size", v))
		_slider(dock, advanced, "图标间距", style.IconGap, 0, 32, func(v: int): style.IconGap = v; _update(dock, zone, "icon_gap", v))
		_slider(dock, advanced, "内边距", style.Padding, 0, 32, func(v: int): style.Padding = v; _update(dock, zone, "padding", v))
	var actions := HBoxContainer.new()
	body.add_child(actions)
	actions.add_child(dock._button("更多操作", func(): dock._show_menu([
		{"text": "恢复无感样式", "run": func(): _update(dock, zone, "invisible", true); _send(dock, "settings", {"id": zone.Id})},
		{"text": "适应图标内容", "run": func(): _send(dock, "fit", {"id": zone.Id})},
		{"text": "查看全部文件…", "run": func(): _send(dock, "overflow", {"id": zone.Id})},
		{"text": "复制空分区", "run": func(): _send(dock, "duplicate", {"id": zone.Id, "monitor": message.monitor})},
		{"separator": true},
		{"text": "移出所有图标，保留文件", "run": func(): _send(dock, "clear", {"id": zone.Id, "monitor": message.monitor})},
		{"text": "删除分区，保留文件", "run": func(): _send(dock, "delete", {"id": zone.Id, "monitor": message.monitor})}
	])))
	var saved: Label = dock._label("修改自动保存", 12, Design.SECONDARY)
	saved.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	saved.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	saved.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	actions.add_child(saved)

static func _color(dock: Control, body: VBoxContainer, title: String, zone: Dictionary, key: String, value: String) -> void:
	var row: HBoxContainer = dock._row(body, title)
	var property_name: String = {"color": "Color", "title_color": "TitleColor", "border_color": "BorderColor"}[key]
	var field := _field(value, func(v: String): zone.style[property_name] = v; _update(dock, zone, key, v), 100)
	field.max_length = 7
	row.add_child(field)
	for color in ["#ffffff", "#252527", "#e8effa", "#efe8df"]:
		var button: Button = dock._button("●", func(): field.text = color; zone.style[property_name] = color; _update(dock, zone, key, color))
		button.add_theme_color_override("font_color", Color(color))
		row.add_child(button)

static func _organize(dock: Control, message: Dictionary) -> void:
	var body := _heading(dock, "一键整理为无感分区")
	body.add_child(dock._label("确认后替换当前屏幕的分区；未选类别回到桌面，操作可撤销。", 12, Design.SECONDARY))
	var content := _scroll(dock, body)
	var selected: Array = []
	for category in message.groups:
		selected.append(category.name)
		var row: HBoxContainer = dock._row(content, "%s · %d 项" % [category.name, category.count], "、".join(category.sample))
		var toggle := Switch.new()
		toggle.button_pressed = true
		row.add_child(toggle)
		toggle.toggled.connect(func(value: bool): if value: selected.append(category.name)
		else: selected.erase(category.name))
	if message.groups.is_empty(): content.add_child(dock._label("当前屏幕没有可整理的桌面文件。", 13, Design.SECONDARY))
	body.add_child(dock._button("生成分类分区", func(): _send(dock, "organize", {"monitor": message.monitor, "categories": selected})))

static func _overflow(dock: Control, message: Dictionary) -> void:
	var body := _heading(dock, "「%s」的全部文件" % message.zone.Name)
	body.add_child(dock._label("共 %d 项 · 第 %d / %d 页" % [message.zone.count, int(message.page) + 1, maxi(1, message.pages)], 12, Design.SECONDARY))
	var content := _scroll(dock, body)
	for item in message.items:
		var row: HBoxContainer = dock._row(content, str(item).get_file())
		row.add_child(dock._button("打开", func(): _send(dock, "open", {"id": message.zone.Id, "item": item})))
		row.add_child(dock._button("移动…", func():
			var entries: Array = [{"text": "移回桌面", "run": func(): _send(dock, "release", {"id": message.zone.Id, "item": item}); _send(dock, "overflow", {"id": message.zone.Id, "page": message.page})}]
			for target in message.targets: entries.append({"text": "移到「%s」" % target.Name, "run": func(): _send(dock, "transfer", {"id": message.zone.Id, "target": target.Id, "item": item}); _send(dock, "overflow", {"id": message.zone.Id, "page": message.page})})
			dock._show_menu(entries)))
	var pages := HBoxContainer.new()
	body.add_child(pages)
	var previous: Button = dock._button("上一页", func(): _send(dock, "overflow", {"id": message.zone.Id, "page": int(message.page) - 1}))
	previous.disabled = message.page <= 0
	pages.add_child(previous)
	var next: Button = dock._button("下一页", func(): _send(dock, "overflow", {"id": message.zone.Id, "page": int(message.page) + 1}))
	next.disabled = int(message.page) + 1 >= int(message.pages)
	pages.add_child(next)
