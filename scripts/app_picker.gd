extends RefCounted

# File picking uses the same form controls instead of a system dialog.
const Design = preload("res://scripts/settings_design.gd")
var dock: Control
var current := OS.get_system_dir(OS.SYSTEM_DIR_DESKTOP)
var path_field: LineEdit
var file_list: VBoxContainer
var status: Label
var selected_callback: Callable
var picker_title := "添加到 Dock"
var extensions: Array = []
var save_mode := false
var suggested_name := ""

func open_for(host: Control, title: String, callback: Callable, filters: Array = [], save_file := false, filename := "") -> void:
	selected_callback = callback
	picker_title = title
	extensions = filters
	save_mode = save_file
	suggested_name = filename
	open(host)

func open(host: Control) -> void:
	dock = host
	var body: VBoxContainer = dock._new_panel("app_picker", 540)
	var heading := HBoxContainer.new()
	body.add_child(heading)
	var title: Label = dock._label(picker_title, 15, Design.TEXT)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.gui_input.connect(dock._settings_title_input)
	heading.add_child(title)
	heading.add_child(dock._button("取消", dock._close_panel))
	var navigation := HBoxContainer.new()
	body.add_child(navigation)
	navigation.add_child(dock._button("上一级", func(): _browse(current.get_base_dir())))
	path_field = LineEdit.new()
	path_field.text = current
	path_field.context_menu_enabled = false
	path_field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	path_field.add_theme_font_size_override("font_size", 13)
	path_field.add_theme_color_override("font_color", Design.TEXT)
	path_field.add_theme_color_override("caret_color", Design.TEXT)
	path_field.add_theme_color_override("selection_color", Color("b8d5ff"))
	var style := Design.outline(Color.WHITE, 6, Color(0, 0, 0, 0.08))
	style.content_margin_left = 8
	style.content_margin_right = 8
	path_field.add_theme_stylebox_override("normal", style)
	path_field.add_theme_stylebox_override("focus", Design.focus())
	navigation.add_child(path_field)
	path_field.text_submitted.connect(_choose_path)
	if save_mode:
		var save_row := HBoxContainer.new()
		body.add_child(save_row)
		var filename := LineEdit.new()
		filename.context_menu_enabled = false
		filename.text = suggested_name
		filename.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		filename.add_theme_stylebox_override("normal", style)
		filename.add_theme_stylebox_override("focus", Design.focus())
		save_row.add_child(filename)
		save_row.add_child(dock._button("保存新备份", func():
			if filename.text.get_file() != filename.text or filename.text.is_empty():
				status.text = "请输入文件名，不要包含文件夹路径。"
				return
			var destination := current.path_join(filename.text)
			if FileAccess.file_exists(destination):
				status.text = "文件已存在，请换一个名称。"
				return
			dock._close_panel()
			selected_callback.call(destination)))
	var shortcuts := HBoxContainer.new()
	body.add_child(shortcuts)
	shortcuts.add_child(dock._button("桌面", func(): _browse(OS.get_system_dir(OS.SYSTEM_DIR_DESKTOP))))
	for i in DirAccess.get_drive_count():
		var drive := DirAccess.get_drive_name(i)
		shortcuts.add_child(dock._button(drive, func(): _browse(drive)))
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size.y = 290
	body.add_child(scroll)
	file_list = VBoxContainer.new()
	file_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	file_list.add_theme_constant_override("separation", 2)
	scroll.add_child(file_list)
	var bar := scroll.get_v_scroll_bar()
	bar.add_theme_stylebox_override("scroll", Design.rounded(Color.TRANSPARENT, 4))
	for key in ["grabber", "grabber_highlight", "grabber_pressed"]:
		var thumb := Design.rounded(Color(0, 0, 0, 0.18), 4)
		thumb.content_margin_left = 4
		thumb.content_margin_right = 4
		bar.add_theme_stylebox_override(key, thumb)
	for key in ["increment", "decrement", "increment_highlight", "decrement_highlight", "increment_pressed", "decrement_pressed"]:
		bar.add_theme_icon_override(key, ImageTexture.create_from_image(Image.create(1, 1, false, Image.FORMAT_RGBA8)))
	status = dock._label("选择应用或快捷方式，也可以直接拖入 Dock。", 12, Design.SECONDARY)
	body.add_child(status)
	_browse(current)

func _choose_path(path: String) -> void:
	if DirAccess.dir_exists_absolute(path):
		_browse(path)
	elif FileAccess.file_exists(path):
		if not extensions.is_empty() and not path.get_extension().to_lower() in extensions:
			status.text = "请选择支持的文件格式：" + ", ".join(extensions)
			return
		if save_mode:
			status.text = "请在上方填写新的备份文件名。"
			return
		if selected_callback.is_valid():
			dock._close_panel()
			selected_callback.call(path)
		else:
			dock._pin_path(path)
			dock._close_panel()
	else:
		status.text = "找不到这个位置，请检查路径。"

func _browse(path: String) -> void:
	var directory := DirAccess.open(path)
	if directory == null:
		status.text = "无法打开这个文件夹。"
		return
	current = directory.get_current_dir()
	path_field.text = current
	for child in file_list.get_children():
		file_list.remove_child(child)
		child.queue_free()
	for folder in directory.get_directories():
		var full_path := current.path_join(folder)
		var button: Button = dock._button(folder + "   ›", func(): _browse(full_path))
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		file_list.add_child(button)
	for file in directory.get_files():
		if not extensions.is_empty() and not file.get_extension().to_lower() in extensions: continue
		var full_path := current.path_join(file)
		var button: Button = dock._button(file, func(): _choose_path(full_path))
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		file_list.add_child(button)
	status.text = "选择文件，或输入完整路径后按回车。" if selected_callback.is_valid() else "选择文件添加到 Dock，或输入完整路径后按回车。"
