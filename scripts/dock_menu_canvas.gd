extends Window

# One native viewport for every level. Submenu hover only shows retained panels.
const Design = preload("res://scripts/settings_design.gd")
const Motion = preload("res://scripts/motion.gd")
signal dismissed
class Branch extends RefCounted:
	var panel: PanelContainer
	var buttons: Array[Button] = []
	var children: Dictionary = {}
	var row_offsets: Array[int] = []
	var rect := Rect2i()
	var parent := -1
	var selected := -1
	var active_child := -1

var entries: Array = []
var buttons: Array[Button] = []
var child_menu: Branch
var closing := true
var selection := -1
var branches: Array[Branch] = []
var normal_style: StyleBoxFlat
var selected_style: StyleBoxFlat
var submenu_timer: Timer
var configured := false
var active_branch := 0
var pending_branch := -1
var work := Rect2i()
var reduce_motion := false

func _init() -> void:
	visible = false

func _configure() -> void:
	if configured: return
	configured = true
	title = "有兜菜单"
	borderless = true
	transparent = true
	transparent_bg = true
	force_native = true
	transient = false
	always_on_top = true
	unresizable = true
	gui_embed_subwindows = true
	normal_style = Design.rounded(Color.TRANSPARENT, 8)
	selected_style = Design.rounded(Color("0088ff"), 8)
	for style in [normal_style, selected_style]:
		style.content_margin_left = 7
		style.content_margin_right = 25
	submenu_timer = Timer.new()
	submenu_timer.one_shot = true
	submenu_timer.wait_time = 0.08
	add_child(submenu_timer)
	submenu_timer.timeout.connect(func():
		if not closing and pending_branch >= 0: _open_branch(pending_branch, branches[pending_branch].selected))
	close_requested.connect(close)
	focus_exited.connect(func(): _check_focus.call_deferred())
	window_input.connect(_input_menu)

func open(items: Array, at: Vector2i, inherited_theme: Theme, reduced: Variant = false) -> void:
	_configure()
	reduce_motion = reduced if reduced is bool else false
	submenu_timer.stop()
	for branch in branches:
		remove_child(branch.panel)
		branch.panel.queue_free()
	branches.clear()
	buttons = []
	child_menu = null
	selection = -1
	active_branch = 0
	pending_branch = -1
	closing = false
	entries = items
	theme = inherited_theme
	work = DisplayServer.screen_get_usable_rect(DisplayServer.get_screen_from_rect(Rect2i(at, Vector2i.ONE)))
	_build_branch(items, -1)
	_place_branch(0, at)
	# Reserve the bounds once; no viewport resize or native window on hover.
	var canvas := branches[0].rect
	for branch in branches: canvas = canvas.merge(branch.rect)
	position = canvas.position
	size = canvas.size
	for branch in branches:
		branch.panel.position = Vector2(branch.rect.position - position + Vector2i(8, 8))
		branch.panel.visible = branch == branches[0]
	buttons = branches[0].buttons
	show()
	Motion.bind_buttons(branches[0].panel, reduce_motion)
	Motion.enter(branches[0].panel, reduce_motion)
	# The main viewport already paces rendering. A second VSync wait in this
	# compatibility-renderer window otherwise doubles menu frame latency.
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED, get_window_id())
	grab_focus()

func _build_branch(items: Array, parent: int) -> int:
	var branch := Branch.new()
	branch.parent = parent
	var branch_index := branches.size()
	branches.append(branch)
	var shell := PanelContainer.new()
	branch.panel = shell
	shell.custom_minimum_size.x = 250
	var background := Design.outline(Color("f5f5f5"), 13, Color(1, 1, 1, 0.8))
	background.content_margin_left = 5
	background.content_margin_right = 5
	background.content_margin_top = 5
	background.content_margin_bottom = 5
	background.shadow_size = 7
	background.shadow_color = Color(0, 0, 0, 0.12)
	shell.add_theme_stylebox_override("panel", background)
	add_child(shell)
	var rows := VBoxContainer.new()
	rows.add_theme_constant_override("separation", 0)
	shell.add_child(rows)
	var row_y := 13
	for entry in items:
		if entry.get("separator", false):
			var line := HSeparator.new()
			line.custom_minimum_size.y = 11
			var style := StyleBoxLine.new()
			style.color = Color("e6e6e6")
			style.thickness = 1
			style.content_margin_left = 7
			style.content_margin_right = 7
			line.add_theme_stylebox_override("separator", style)
			line.add_theme_constant_override("separation", 11)
			rows.add_child(line)
			row_y += 11
			continue
		var button := Button.new()
		button.custom_minimum_size.y = 24
		button.text = ("✓   " if entry.get("checked", false) else "     ") + str(entry.get("text", ""))
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.focus_mode = Control.FOCUS_NONE
		button.toggle_mode = true
		button.add_theme_font_size_override("font_size", 13)
		for key in ["normal", "hover", "pressed", "focus"]: button.add_theme_stylebox_override(key, normal_style)
		button.add_theme_stylebox_override("pressed", selected_style)
		button.add_theme_stylebox_override("hover_pressed", selected_style)
		for key in ["font_color", "font_hover_color", "font_pressed_color"]: button.add_theme_color_override(key, Design.TEXT)
		button.add_theme_color_override("font_pressed_color", Color.WHITE)
		button.add_theme_color_override("font_hover_pressed_color", Color.WHITE)
		rows.add_child(button)
		if entry.has("children"):
			var arrow := Label.new()
			arrow.text = "›"
			arrow.add_theme_font_size_override("font_size", 18)
			arrow.add_theme_color_override("font_color", Design.SECONDARY)
			arrow.mouse_filter = Control.MOUSE_FILTER_IGNORE
			button.add_child(arrow)
			arrow.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
			arrow.offset_right = -10
			arrow.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
			arrow.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		var index := branch.buttons.size()
		button.set_meta("entry", entry)
		branch.buttons.append(button)
		branch.row_offsets.append(row_y)
		row_y += 24
		button.mouse_entered.connect(func(): _select_branch(branch_index, index, true))
		button.pressed.connect(func(): _activate_branch(branch_index, index))
	shell.size = shell.get_combined_minimum_size()
	branch.rect.size = Vector2i(shell.size.ceil()) + Vector2i(16, 16)
	for i in branch.buttons.size():
		var entry: Dictionary = branch.buttons[i].get_meta("entry")
		if entry.has("children"): branch.children[i] = _build_branch(entry.children, branch_index)
	return branch_index

func _place_branch(index: int, at: Vector2i) -> void:
	var branch := branches[index]
	branch.rect.position = Vector2i(clampi(at.x - 8, work.position.x, maxi(work.position.x, work.end.x - branch.rect.size.x)), clampi(at.y - 8, work.position.y, maxi(work.position.y, work.end.y - branch.rect.size.y)))
	for row: int in branch.children:
		var child: int = branch.children[row]
		var next_at := branch.rect.position + Vector2i(branch.rect.size.x - 10, branch.row_offsets[row] - 5)
		if next_at.x + branches[child].rect.size.x > work.end.x: next_at.x = branch.rect.position.x - branches[child].rect.size.x + 16
		_place_branch(child, next_at)

func _check_focus() -> void:
	if not closing and not has_focus(): close()

func _hide_children(index: int) -> void:
	var branch := branches[index]
	if branch.active_child >= 0:
		_hide_children(branch.active_child)
		branches[branch.active_child].panel.hide()
		branch.active_child = -1
	if index == 0: child_menu = null

func _select(index: int, open_submenu: bool) -> void:
	_select_branch(0, index, open_submenu)

func _select_branch(branch_index: int, index: int, open_submenu: bool) -> void:
	if closing: return
	var branch := branches[branch_index]
	if index < 0 or index >= branch.buttons.size(): return
	active_branch = branch_index
	if branch.selected != index:
		submenu_timer.stop()
		_hide_children(branch_index)
		var previous := branch.selected
		branch.selected = index
		for i in [previous, index]:
			if i < 0: continue
			branch.buttons[i].set_pressed_no_signal(i == index)
			for child in branch.buttons[i].get_children():
				if child is Label: child.add_theme_color_override("font_color", Color.WHITE if i == index else Design.SECONDARY)
	if branch_index == 0: selection = index
	if open_submenu and branch.children.has(index) and branch.active_child < 0 and submenu_timer.is_stopped():
		pending_branch = branch_index
		submenu_timer.start()

func _open_submenu(index: int) -> void:
	_open_branch(0, index)

func _open_branch(branch_index: int, index: int) -> void:
	if closing: return
	var branch := branches[branch_index]
	if not branch.panel.visible or index != branch.selected or not branch.children.has(index): return
	branch.active_child = branch.children[index]
	branches[branch.active_child].panel.show()
	branches[branch.active_child].panel.move_to_front()
	Motion.bind_buttons(branches[branch.active_child].panel, reduce_motion)
	Motion.enter(branches[branch.active_child].panel, reduce_motion)
	if branch_index == 0: child_menu = branches[branch.active_child]

func _activate(index: int) -> void:
	_activate_branch(0, index)

func _activate_branch(branch_index: int, index: int) -> void:
	if closing: return
	var branch := branches[branch_index]
	if index < 0 or index >= branch.buttons.size(): return
	var entry: Dictionary = branch.buttons[index].get_meta("entry")
	if entry.has("children"):
		_select_branch(branch_index, index, false)
		_open_branch(branch_index, index)
		return
	var callback: Callable = entry.get("run", Callable())
	close()
	if callback.is_valid(): callback.call()

func _input_menu(event: InputEvent) -> void:
	if closing: return
	if event is InputEventMouseButton and event.pressed:
		if not branches.any(func(b: Branch): return b.panel.visible and b.panel.get_global_rect().has_point(event.position)): close()
		return
	if event is InputEventKey and event.pressed:
		var branch := branches[active_branch]
		if event.keycode == KEY_ESCAPE: close()
		elif event.keycode in [KEY_UP, KEY_DOWN] and not branch.buttons.is_empty():
			var next := branch.buttons.size() - 1 if branch.selected < 0 and event.keycode == KEY_UP else posmod(branch.selected + (-1 if event.keycode == KEY_UP else 1), branch.buttons.size())
			_select_branch(active_branch, next, false)
		elif event.keycode in [KEY_ENTER, KEY_RIGHT] and branch.selected >= 0:
			_activate_branch(active_branch, branch.selected)
			if branch.active_child >= 0: _select_branch(branch.active_child, 0, false)
		elif event.keycode == KEY_LEFT and branch.parent >= 0:
			active_branch = branch.parent
			_hide_children(active_branch)

func close() -> void:
	if closing: return
	closing = true
	submenu_timer.stop()
	hide()
	dismissed.emit()
