extends Control

const CONFIG := "res://data/preferences.json"
const DockSwitch = preload("res://scripts/dock_switch.gd")
const DockSlider = preload("res://scripts/dock_slider.gd")
const DockChoice = preload("res://scripts/dock_choice.gd")
const SettingsDesign = preload("res://scripts/settings_design.gd")
const KeyVisualizer = preload("res://scripts/key_visualizer.gd")
const Design = preload("res://scripts/dock_design.gd")
const Motion = preload("res://scripts/motion.gd")
const WidgetPreview = preload("res://scripts/widget_preview.gd")
var prefs: Dictionary = {"dock_theme": "light", "reduce_motion": false, "hide_taskbar": false, "auto_hide": false, "key_visualizer": true, "key_statistics": true, "show_tray_apps": true, "icon_size": 48.0, "magnification": 1.65, "screen": -1, "pins": []}
var state: Dictionary = {}
var items: Array = []
var running_order: Array = []
var hit_rects: Array[Rect2] = []
var scales: Dictionary = {}
var textures: Dictionary = {}
var bounces: Dictionary = {}
var reveals: Dictionary = {}
var reorder_offsets: Dictionary = {}
var pending_reorder_positions: Dictionary = {}
var tip_alpha := 0.0
var ipc := ""
var bridge_pid := -1
var sequence := 0
var action_id := 0
var poll_time := 0.0
var ui_poll_time := 0.0
var elapsed := 0.0
var hover := -1
var focused := -1
var panel: PanelContainer
var panel_mode := ""
var context_item: Dictionary = {}
var context_menu: Window
var app_picker: RefCounted
var active_box_editor := ""
var hide_amount := 0.0
var dock_rect := Rect2()
var extra_region := Rect2()
var notice := ""
var notice_time := 0.0
var notice_age := 0.0
var drag_start := Vector2.ZERO
var drag_index := -1
var dragging := false
var pressed_key := ""
var background_pressed := false
var hide_wait := 0.0
var hover_key := ""
var hover_elapsed := 0.0
var preview_signature := ""
var settings_preview_until := 0.0
var settings_slider_drag := ""
var magnification_preview_key := ""
var font := SystemFont.new()
var check_hide: Button
var screen_menu: OptionButton
var last_geometry := ""
var last_material := ""
var material_elapsed := 0.0
var primary_option := true
var native_ready := false
var startup_ready := false
var startup_frame_pending := false
var launch_ready_path := ""
var qa := false
var qa_reference := false
var reference_items: Array = []
var qa_pointer := Vector2.ZERO
var qa_input := false
var quitting := false
var system_textures: Dictionary = {}
var dark_textures: Dictionary = {}
var dock_unit := 1.0
var dock_radius := 20.0
var layout_base := 48.0
var layout_gap := 12.0
var layout_separator := 14.0
var layout_x := 0.0
var layout_floor := 0.0
var glass_visual: ColorRect
var glass_shader := ShaderMaterial.new()
var key_socket := PacketPeerUDP.new()
var key_token := ""
var key_window: Window
var key_canvas: KeyVisualizer
var key_enabled := false
var stats_panel: Control
var stats_summary: Button
var settings_window: Window
var settings_placed := false

func _ready() -> void:
	for argument in OS.get_cmdline_user_args():
		if str(argument).begins_with("--launch-ready="):
			launch_ready_path = str(argument).substr(15)
	if not ("--qa" in OS.get_cmdline_user_args() or "--qa-keys" in OS.get_cmdline_user_args() or "--qa-theme" in OS.get_cmdline_user_args() or "--qa-reference" in OS.get_cmdline_user_args()):
		modulate.a = 0.0
	texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	glass_shader.shader = preload("res://scripts/dock_glass.gdshader")
	glass_visual = ColorRect.new()
	glass_visual.material = glass_shader
	glass_visual.mouse_filter = Control.MOUSE_FILTER_IGNORE
	glass_visual.show_behind_parent = true
	add_child(glass_visual)
	font.font_names = PackedStringArray(["Microsoft YaHei UI", "Segoe UI"])
	var theme_data := Theme.new()
	theme_data.default_font = font
	theme_data.default_font_size = 14
	for kind in Design.ICONS:
		var icon_image := Image.load_from_file(ProjectSettings.globalize_path(Design.ICONS[kind]))
		if icon_image:
			icon_image.fix_alpha_edges()
			icon_image.generate_mipmaps()
			system_textures[kind] = ImageTexture.create_from_image(icon_image)
	for kind in Design.DARK_ICONS:
		var icon_image := Image.load_from_file(ProjectSettings.globalize_path(Design.DARK_ICONS[kind]))
		if icon_image:
			icon_image.fix_alpha_edges()
			icon_image.generate_mipmaps()
			dark_textures[kind] = ImageTexture.create_from_image(icon_image)
	for control_type in ["Button", "OptionButton", "PopupMenu", "Label"]:
		theme_data.set_color("font_color", control_type, Color("252527"))
		theme_data.set_color("font_hover_color", control_type, Color("151517"))
		theme_data.set_color("font_pressed_color", control_type, Color("151517"))
	var separator := StyleBoxLine.new()
	separator.color = Color(0.1, 0.1, 0.12, 0.11)
	theme_data.set_stylebox("separator", "HSeparator", separator)
	var popup := StyleBoxFlat.new()
	popup.bg_color = Color("f3f3f5")
	popup.set_corner_radius_all(9)
	popup.set_content_margin_all(7)
	theme_data.set_stylebox("panel", "PopupMenu", popup)
	var selection := StyleBoxFlat.new()
	selection.bg_color = Color("ffffff")
	selection.set_corner_radius_all(6)
	selection.set_border_width_all(1)
	selection.border_color = Color("d4d4d8")
	selection.content_margin_left = 10
	selection.content_margin_right = 10
	selection.content_margin_top = 5
	selection.content_margin_bottom = 5
	theme_data.set_stylebox("normal", "OptionButton", selection)
	theme_data.set_stylebox("hover", "OptionButton", selection)
	theme_data.set_stylebox("pressed", "OptionButton", selection)
	theme_data.set_color("font_color", "OptionButton", Color("36363a"))
	for track_name in ["slider", "grabber_area", "grabber_area_highlight"]:
		var track := StyleBoxFlat.new()
		track.bg_color = Design.BLUE if track_name != "slider" else Color("d4d4d8")
		track.set_corner_radius_all(2)
		track.content_margin_top = 2
		track.content_margin_bottom = 2
		theme_data.set_stylebox(track_name, "HSlider", track)
	theme = theme_data
	get_window().title = "有兜 · UDo"
	DisplayServer.set_icon(Image.load_from_file(ProjectSettings.globalize_path("res://assets/udo.png")))
	get_window().transparent_bg = true
	get_window().transparent = true
	get_window().borderless = true
	get_window().always_on_top = true
	get_window().unresizable = true
	get_window().wrap_controls = false
	get_tree().auto_accept_quit = false
	get_window().close_requested.connect(_quit)
	get_window().focus_exited.connect(_cancel_pointer)
	get_window().files_dropped.connect(_files_dropped)
	qa_reference = "--qa-reference" in OS.get_cmdline_user_args()
	qa = "--qa" in OS.get_cmdline_user_args() or "--qa-keys" in OS.get_cmdline_user_args() or "--qa-theme" in OS.get_cmdline_user_args() or qa_reference
	if key_socket.bind(0, "127.0.0.1") == OK:
		key_token = Crypto.new().generate_random_bytes(16).hex_encode()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://data"))
	var ignore := FileAccess.open("res://data/.gdignore", FileAccess.WRITE)
	if ignore:
		ignore.close()
	if FileAccess.file_exists(CONFIG):
		var loaded = JSON.parse_string(FileAccess.get_file_as_string(CONFIG))
		if loaded is Dictionary:
			prefs.merge(loaded, true)
	if str(prefs.dock_theme) not in ["light", "dark", "glass"]: prefs.dock_theme = "light"
	if not prefs.has("reduce_motion"): prefs.reduce_motion = false
	# The old preference stored a WinForms enumeration index, not a Godot display.
	# Migrate once to the primary screen; future choices use the Godot coordinate space.
	if int(prefs.get("display_schema", 0)) != 2:
		prefs["display_schema"] = 2
		prefs["screen"] = -1
		_save()
	prefs.icon_size = clampf(float(prefs.icon_size), 36, 72)
	prefs.magnification = clampf(float(prefs.magnification), 1, 2)
	if not prefs.pins is Array:
		prefs.pins = []
	if prefs.pins.is_empty():
		var windir := OS.get_environment("WINDIR")
		prefs.pins = [
			{"name": "文件资源管理器", "path": windir.path_join("explorer.exe"), "kind": "files"},
			{"name": "浏览器", "path": _browser(), "kind": "browser"},
			{"name": "终端", "path": windir.path_join("System32/WindowsPowerShell/v1.0/powershell.exe"), "kind": "terminal"},
			{"name": "记事本", "path": windir.path_join("System32/notepad.exe"), "kind": "notes"}]
	ipc = ProjectSettings.globalize_path("res://data/sessions").path_join(str(OS.get_process_id()) + "-" + str(Time.get_ticks_msec()))
	DirAccess.make_dir_recursive_absolute(ipc)
	var bridge := ProjectSettings.globalize_path("res://native/bin/DockBridge.exe")
	if OS.get_name() == "Windows" and FileAccess.file_exists(bridge):
		bridge_pid = OS.create_process(bridge, [str(OS.get_process_id()), ipc], false)
	else:
		_show_notice("原生组件未找到，请先运行 build.ps1")
	_rebuild()
	_position_window()
	_layout_dock()
	queue_redraw()
	if qa:
		startup_ready = true
		prefs.hide_taskbar = false
		prefs.auto_hide = false
		get_tree().create_timer(3.0).timeout.connect(_qa_theme_capture if "--qa-theme" in OS.get_cmdline_user_args() else (_qa_keys_capture if "--qa-keys" in OS.get_cmdline_user_args() else (_qa_reference_capture if qa_reference else _qa_capture)))

func _try_finish_startup() -> void:
	if qa or quitting or startup_ready or startup_frame_pending or not native_ready:
		return
	if not bool(state.get("initial_scan_complete", false)):
		return
	var icons: Dictionary = state.get("icons", {})
	for path_value in [OS.get_environment("USERPROFILE").path_join("Downloads"), "shell:RecycleBinFolder", "builtin:settings", "builtin:app"]:
		if not icons.has(path_value): return
	for pin in prefs.pins:
		if pin is Dictionary and pin.has("path") and not icons.has(str(pin.path)): return
	for item in items:
		var path: String = str(item.get("icon", ""))
		if path.is_empty(): path = str(icons.get("builtin:app", ""))
		if not path.is_empty() and not textures.has(path) and FileAccess.file_exists(path):
			var im := Image.load_from_file(path)
			if im:
				im.fix_alpha_edges()
				im.generate_mipmaps()
				textures[path] = ImageTexture.create_from_image(im)
	for key in reveals: reveals[key] = 1.0
	startup_frame_pending = true
	modulate.a = 1.0
	queue_redraw()
	RenderingServer.frame_post_draw.connect(_finish_startup_frame, CONNECT_ONE_SHOT)

func _finish_startup_frame() -> void:
	startup_ready = true
	_publish_material()
	if not launch_ready_path.is_empty():
		var marker := FileAccess.open(launch_ready_path, FileAccess.WRITE)
		if marker:
			marker.store_string("ready")
			marker.close()

func _browser() -> String:
	for base in [OS.get_environment("ProgramFiles(x86)"), OS.get_environment("ProgramFiles"), OS.get_environment("LOCALAPPDATA")]:
		for suffix in ["Microsoft/Edge/Application/msedge.exe", "Google/Chrome/Application/chrome.exe", "Mozilla Firefox/firefox.exe"]:
			var candidate: String = str(base).path_join(suffix)
			if FileAccess.file_exists(candidate):
				return candidate
	return "https://www.bing.com"

func _send(command: Dictionary) -> void:
	if bridge_pid <= 0:
		return
	sequence += 1
	var path := ipc.path_join("cmd-%010d.json" % sequence)
	var file := FileAccess.open(path + ".tmp", FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(command))
		file.close()
		DirAccess.rename_absolute(path + ".tmp", path)

func _save() -> void:
	if qa:
		return
	var file := FileAccess.open(CONFIG + ".tmp", FileAccess.WRITE)
	if file:
		file.store_string(JSON.stringify(prefs, "\t"))
		file.close()
		DirAccess.rename_absolute(ProjectSettings.globalize_path(CONFIG + ".tmp"), ProjectSettings.globalize_path(CONFIG))

func _poll_ui_event() -> void:
	if ipc.is_empty(): return
	var path := ipc.path_join("ui-event.json")
	if not FileAccess.file_exists(path): return
	var file := FileAccess.open(path, FileAccess.READ)
	if not file: return
	var payload := file.get_as_text()
	file.close()
	var event = JSON.parse_string(payload)
	if event is Dictionary: _dispatch_ui_event(event)

func _dispatch_ui_event(event: Dictionary) -> void:
	if int(event.get("action_id", 0)) > action_id:
		action_id = int(event.action_id)
		var action: String = event.action
		if action == "settings":
			_open_settings()
		elif action == "quit":
			_quit()
		elif action == "restore":
			prefs.hide_taskbar = false
			_save()
			if is_instance_valid(check_hide):
				check_hide.set_pressed_no_signal(false)
			_show_notice("Windows 任务栏已恢复")
		elif action == "menu_dismiss":
			if is_instance_valid(context_menu): context_menu.close()
		elif action.begins_with("box:"):
			var message = JSON.parse_string(action.substr(4))
			if message is Dictionary: preload("res://scripts/storage_box_ui.gd").handle(self, message)
		elif action.begins_with("partition:"):
			var message = JSON.parse_string(action.substr(10))
			if message is Dictionary: preload("res://scripts/partition_ui.gd").handle(self, message)
		elif action.begins_with("pin:"):
			_pin_path(action.substr(4))

func _poll() -> void:
	var path := ipc.path_join("state.json")
	if not FileAccess.file_exists(path):
		if elapsed > 8 and not native_ready:
			_show_notice("Windows 组件未响应 · 退出后重新启动试试")
		return
	# The native writer may atomically replace this file between the exists/open calls.
	var file := FileAccess.open(path, FileAccess.READ)
	if not file:
		return
	var contents := file.get_as_text()
	file.close()
	if contents.is_empty():
		return
	var parsed = JSON.parse_string(contents)
	if not parsed is Dictionary:
		return
	state = parsed
	if not native_ready:
		native_ready = true
		_send({"op": "register", "hwnd": str(DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE))})
		for path_value in [OS.get_environment("USERPROFILE").path_join("Downloads"), "shell:RecycleBinFolder", "builtin:settings", "builtin:app"]:
			_send({"op": "icon", "path": path_value})
		for pin in prefs.pins:
			_send({"op": "icon", "path": pin.path})
		_send({"op": "hide", "value": prefs.hide_taskbar})
		_sync_key_visualizer()
		_sync_key_statistics()
	_dispatch_ui_event(state)
	if not str(state.get("error", "")).is_empty():
		_show_notice(str(state.error))
	_rebuild()
	_position_window()
	_try_finish_startup()
	if is_instance_valid(stats_summary):
		var today: Dictionary = state.get("key_statistics", {})
		stats_summary.text = "今天 %s 次  ›" % str(int(today.get("total", 0)))
	if is_instance_valid(stats_panel):
		stats_panel.update_report(state.get("key_statistics_view", {}))

func _key(path: String) -> String:
	var resolved: Dictionary = state.get("resolved", {})
	return str(resolved.get(path, path)).replace("\\", "/").to_lower()

func _rebuild() -> void:
	if qa_reference and not reference_items.is_empty():
		items = reference_items
		return
	if not pressed_key.is_empty():
		return
	var grouped := {}
	for win in state.get("windows", []):
		var key := _key(str(win.path))
		if not grouped.has(key):
			grouped[key] = []
		grouped[key].append(win)
	var tray_grouped := {}
	for tray_app in state.get("tray_apps", []):
		var key := _key(str(tray_app.path))
		if not tray_grouped.has(key):
			tray_grouped[key] = []
		tray_grouped[key].append(tray_app)
		if bool(prefs.show_tray_apps) and not grouped.has(key):
			grouped[key] = []
	var next: Array = []
	for pin in prefs.pins:
		if not pin is Dictionary or not pin.has("path"):
			continue
		var item: Dictionary = pin.duplicate(true)
		item["key"] = _key(str(pin.path))
		item["windows"] = grouped.get(item.key, [])
		item["tray_apps"] = tray_grouped.get(item.key, [])
		item["pinned"] = true
		# A running window can supply its own taskbar icon. Prefer that over
		# the executable icon used while the pin is idle.
		item["icon"] = item["windows"][0].icon if not item["windows"].is_empty() else state.get("icons", {}).get(pin.path, "")
		next.append(item)
		grouped.erase(item.key)
	for key in running_order.duplicate():
		if not grouped.has(key):
			running_order.erase(key)
	for key in grouped:
		if not running_order.has(key):
			running_order.append(key)
	for key in running_order:
		var group: Array = grouped[key]
		var trays: Array = tray_grouped.get(key, [])
		var source: Dictionary = group[0] if not group.is_empty() else trays[0]
		next.append({"key": key, "name": str(source.path).replace("\\", "/").get_file().get_basename(), "path": source.path, "icon": source.icon, "windows": group, "tray_apps": trays, "pinned": false, "kind": "app"})
	next.append({"key": "settings", "name": "有兜设置", "path": "", "kind": "settings", "icon": state.get("icons", {}).get("builtin:settings", ""), "windows": [], "pinned": false})
	next.append({"key": "downloads", "name": "下载", "path": OS.get_environment("USERPROFILE").path_join("Downloads"), "kind": "downloads", "icon": state.get("icons", {}).get(OS.get_environment("USERPROFILE").path_join("Downloads"), ""), "windows": [], "pinned": false})
	next.append({"key": "trash", "name": "回收站", "path": "shell:RecycleBinFolder", "kind": "trash", "icon": state.get("icons", {}).get("shell:RecycleBinFolder", ""), "windows": [], "pinned": false})
	var live := {}
	for i in range(next.size()):
		var key: String = str(next[i].key)
		live[key] = true
		if not reveals.has(key): reveals[key] = 1.0 if prefs.reduce_motion or not startup_ready else -minf(i * 0.045, 0.35)
	for key in reveals.keys():
		if not live.has(key): reveals.erase(key)
	items = next

func _selected_screen() -> int:
	var selected := int(prefs.get("screen", -1))
	if selected < 0 or selected >= DisplayServer.get_screen_count():
		return DisplayServer.get_primary_screen()
	return selected

func _screen_area(index: int) -> Rect2i:
	# Never feed Win32 coordinates to Godot: Windows' primary origin differs
	# from Godot's virtual-desktop origin when a display sits left or above it.
	if bool(state.get("hidden", false)):
		return Rect2i(DisplayServer.screen_get_position(index), DisplayServer.screen_get_size(index))
	return DisplayServer.screen_get_usable_rect(index)

func _position_window() -> void:
	var available := _screen_area(_selected_screen())
	var height := 470 if panel_mode == "context" else 240
	height = mini(height, available.size.y - 16)
	var width := mini(available.size.x - 32, maxi(730, int(items.size() * (float(prefs.icon_size) + 16) + 130)))
	if qa_reference:
		width = mini(available.size.x - 32, 2352)
	var target := Vector2i(available.position.x + roundi((available.size.x - width) / 2.0), available.end.y - height)
	var target_size := Vector2i(width, height)
	if get_window().size != target_size:
		get_window().size = target_size
	if get_window().position != target:
		get_window().position = target
	if is_instance_valid(panel) and panel.get_window() == get_window():
		panel.position.x = (width - panel.size.x) / 2.0
	if is_instance_valid(key_window):
		var key_size := Vector2i(available.size.x, mini(240, available.size.y))
		var key_position := Vector2i(available.position.x, available.end.y - key_size.y)
		if key_window.position != key_position or key_window.size != key_size:
			key_canvas.clear()
			key_window.size = key_size
			key_window.position = key_position

func _sync_key_visualizer() -> void:
	key_enabled = bool(prefs.key_visualizer) and not qa and not key_token.is_empty() and not quitting
	if not key_enabled:
		_send({"op": "key_visualizer", "enabled": false})
		if is_instance_valid(key_window):
			key_canvas.clear()
			key_window.hide()
		return
	_ensure_key_window()
	_send({"op": "key_visualizer", "enabled": true, "port": key_socket.get_local_port(), "token": key_token})

func _sync_key_statistics() -> void:
	_send({"op": "key_statistics", "enabled": bool(prefs.key_statistics) and not qa and not quitting})

func _ensure_key_window() -> void:
	if not is_instance_valid(key_window):
		key_window = Window.new()
		key_window.visible = false
		key_window.title = "UDo Keys"
		key_window.force_native = true
		key_window.borderless = true
		key_window.transparent = true
		key_window.transparent_bg = true
		key_window.unfocusable = true
		key_window.mouse_passthrough = true
		key_window.always_on_top = true
		key_window.unresizable = true
		add_child(key_window)
		key_canvas = KeyVisualizer.new()
		key_window.add_child(key_canvas)
		key_canvas.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_position_window()
	key_window.show()
	_send({"op": "key_overlay", "hwnd": str(DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, key_window.get_window_id()))})

func _poll_keys() -> void:
	# Datagram contents remain in memory and expire with the animation.
	var count := 0
	while key_socket.get_available_packet_count() > 0 and count < 64:
		var packet := key_socket.get_packet().get_string_from_ascii()
		count += 1
		if not key_enabled or not is_instance_valid(key_canvas):
			continue
		if packet.begins_with(key_token + ":"):
			key_canvas.add_key(int(packet.substr(33)))

func _publish_material() -> void:
	if not native_ready or bridge_pid <= 0 or not dock_rect.has_area():
		return
	var data := {"x": int(dock_rect.position.x), "y": int(dock_rect.position.y), "w": int(dock_rect.size.x), "h": int(dock_rect.size.y), "radius": int(dock_radius), "visible": (startup_ready or qa) and hide_amount < 0.93 and not quitting, "theme": prefs.dock_theme, "scale": dock_unit}
	var serialized := JSON.stringify(data)
	if serialized == last_material:
		return
	var file := FileAccess.open(ipc.path_join("layout.json.tmp"), FileAccess.WRITE)
	if file:
		file.store_string(serialized)
		file.close()
		if DirAccess.rename_absolute(ipc.path_join("layout.json.tmp"), ipc.path_join("layout.json")) == OK:
			last_material = serialized

func _process(delta: float) -> void:
	_poll_keys()
	ui_poll_time += delta
	if ui_poll_time >= 0.025:
		ui_poll_time = 0.0
		_poll_ui_event()
	elapsed += delta
	material_elapsed += delta
	poll_time += delta
	if poll_time >= 0.2:
		poll_time = 0
		_poll()
		if native_ready and bridge_pid > 0 and not OS.is_process_running(bridge_pid) and not quitting:
			bridge_pid = -1
			prefs.hide_taskbar = false
			_show_notice("原生组件已停止，任务栏将由守护进程恢复")
	if not qa and not startup_ready and not startup_frame_pending and elapsed > 12.0:
		_show_notice("原生组件加载超时，请检查安装文件")
		for key in reveals: reveals[key] = 1.0
		startup_frame_pending = true
		modulate.a = 1.0
		queue_redraw()
		RenderingServer.frame_post_draw.connect(_finish_startup_frame, CONNECT_ONE_SHOT)
	notice_time = maxf(0, notice_time - delta)
	if notice_time > 0: notice_age += delta
	var mouse := qa_pointer if qa and qa_input else Vector2(DisplayServer.mouse_get_position() - get_window().position)
	var over_settings := is_instance_valid(settings_window) and settings_window.visible and Rect2(Vector2(settings_window.position), Vector2(settings_window.size)).has_point(mouse + Vector2(get_window().position))
	var pointer_available := not over_settings and panel_mode != "context"
	var previewing := panel_mode == "settings" and (not settings_slider_drag.is_empty() or elapsed < settings_preview_until)
	var at_dock := pointer_available and dock_rect.grow(24).has_point(mouse)
	var at_bottom := pointer_available and mouse.y >= size.y - 8 and mouse.x > 0 and mouse.x < size.x
	var outside: bool = bool(prefs.auto_hide) and panel_mode != "context" and not previewing and pressed_key.is_empty() and not at_dock and not at_bottom and notice_time <= 0
	hide_wait = hide_wait + delta if outside else 0.0
	var should_hide := outside and hide_wait >= 0.25
	hide_amount = (1.0 if should_hide else 0.0) if prefs.reduce_motion else lerpf(hide_amount, 1.0 if should_hide else 0.0, 1.0 - exp(-delta * 13))
	hover = -1
	for i in range(hit_rects.size()):
		if pointer_available and hit_rects[i].has_point(mouse):
			hover = i
	var new_hover_key := str(items[hover].key) if hover >= 0 and hover < items.size() else ""
	hover_elapsed = hover_elapsed + delta if new_hover_key == hover_key else 0.0
	hover_key = new_hover_key
	var anchors := _rest_centers()
	var preview_index := _item_index(magnification_preview_key) if previewing else -1
	for i in range(items.size()):
		var key: String = items[i].key
		var distance := 10000.0
		if pointer_available and i < hit_rects.size() and (dock_rect.grow(10).has_point(mouse) or hover >= 0):
			distance = absf(mouse.x - anchors[i])
		elif preview_index >= 0:
			distance = absf(anchors[preview_index] - anchors[i])
		# Keep the wave proportional to icon size: one clear peak, subtle neighbors.
		var radius := maxf(16.0, layout_base) * 0.9
		var target := 1.0 + (float(prefs.magnification) - 1.0) * exp(-pow(distance / radius, 2.0)) * (1 - hide_amount)
		if pressed_key.is_empty():
			scales[key] = target if prefs.reduce_motion else lerpf(float(scales.get(key, 1.0)), target, 1.0 - exp(-delta * 18.0))
		if reveals.has(key): reveals[key] = minf(1.0, float(reveals[key]) + delta * 4.5)
	for key in bounces.keys():
		bounces[key] = float(bounces[key]) - delta
		if bounces[key] <= 0:
			bounces.erase(key)
	for key in reorder_offsets.keys():
		var next_offset := lerpf(float(reorder_offsets[key]), 0.0, 1.0 - exp(-delta * 17.0))
		if absf(next_offset) < 0.25: reorder_offsets.erase(key)
		else: reorder_offsets[key] = next_offset
	var tip_visible: bool = hover >= 0 and hover_elapsed >= 0.22 and pressed_key.is_empty() and panel_mode != "context" and hide_amount < 0.1 and not (native_ready and items[hover].windows.size() > 0)
	tip_alpha = (1.0 if tip_visible else 0.0) if prefs.reduce_motion else lerpf(tip_alpha, 1.0 if tip_visible else 0.0, 1.0 - exp(-delta * 20.0))
	# Calculate once before drawing. The blur surface, fill and shadow consume
	# exactly the same snapped geometry, including during magnification.
	_layout_dock()
	_sync_window_preview()
	if is_instance_valid(key_canvas) and is_instance_valid(key_window):
		key_canvas.exclusion = Rect2(Vector2(get_window().position - key_window.position) + dock_rect.position, dock_rect.size) if hide_amount < 0.9 else Rect2()
	if material_elapsed >= 0.016:
		material_elapsed = 0
		_publish_material()
	queue_redraw()

func _sync_window_preview() -> void:
	var signature := ""
	var chosen: Dictionary = {}
	if native_ready and hover >= 0 and hover < items.size() and hover < hit_rects.size() and hover_elapsed >= 0.32 and pressed_key.is_empty() and panel_mode.is_empty():
		chosen = items[hover]
		if chosen.windows.size() > 0:
			signature = str(chosen.key)
			for win in chosen.windows:
				signature += ":" + str(win.hwnd)
	if signature == preview_signature:
		return
	preview_signature = signature
	if signature.is_empty():
		_send({"op": "preview"})
	else:
		var anchor := hit_rects[hover]
		_send({"op": "preview", "windows": chosen.windows, "x": anchor.position.x, "y": anchor.position.y, "w": anchor.size.x, "h": anchor.size.y, "dock_w": get_window().size.x, "dock_h": get_window().size.y})

func _rest_centers() -> PackedFloat32Array:
	# Use the idle layout as the magnification input, not last frame's expanded
	# rectangles. A stationary cursor must always produce the same target sizes.
	var result := PackedFloat32Array()
	if items.is_empty():
		return result
	var units := items.size() * 45.0 + 27.0 + 3.0 * 36.0 * (float(prefs.magnification) - 1)
	var base := maxf(16, minf(float(prefs.icon_size), (size.x - 32) * 36.0 / units))
	var unit := base / Design.ICON_SIZE
	var gap := Design.GAP * unit
	var total := items.size() * base + (items.size() - 1) * gap + 16.0 * unit + 20.0 * unit
	var x := (size.x - total) / 2 + 8.0 * unit
	for item in items:
		if item.kind == "downloads":
			x += 20.0 * unit
		result.append(x + base / 2)
		x += base + gap
	return result

func _box(rect: Rect2, color: Color, radius: float, border := Color.TRANSPARENT, shadow := 0) -> void:
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.set_corner_radius_all(int(radius))
	style.corner_detail = 16
	if border.a > 0:
		style.set_border_width_all(1)
		style.border_color = border
	if shadow > 0:
		style.shadow_color = Color(0, 0, 0, 0.15)
		style.shadow_size = shadow
		style.shadow_offset = Vector2.ZERO
	draw_style_box(style, rect)

func _layout_dock() -> void:
	if items.is_empty():
		return
	# Scale the Figma 36px grid as a unit, including the 41px separator.
	# Reserve enough room for neighboring icons at maximum magnification.
	var units := items.size() * 45.0 + 27.0 + 3.0 * 36.0 * (float(prefs.magnification) - 1)
	var base := minf(float(prefs.icon_size), (size.x - 32) * 36.0 / units)
	base = maxf(16, base)
	dock_unit = base / Design.ICON_SIZE
	var gap := Design.GAP * dock_unit
	var padding := Design.PADDING_X * dock_unit
	var separator_width := 11.0 * dock_unit
	var total := padding * 2 - gap
	for item in items:
		total += base * float(scales.get(item.key, 1.0)) + gap
	total += separator_width + gap
	layout_x = (size.x - total) / 2 + padding
	var dock_bottom := size.y - Design.BOTTOM_GAP + hide_amount * (62.0 * dock_unit + 20)
	layout_floor = dock_bottom - 13.5 * dock_unit
	layout_base = base
	layout_gap = gap
	layout_separator = separator_width
	dock_rect = Rect2(roundf((size.x - total) / 2), roundf(dock_bottom - 62.0 * dock_unit), roundf(total), roundf(62.0 * dock_unit))
	var material_ready: bool = state.get("material", "") == "blur"
	dock_radius = roundf(Design.RADIUS * dock_unit)
	glass_visual.position = dock_rect.position - Vector2(24, 24)
	glass_visual.size = dock_rect.size + Vector2(48, 48)
	glass_shader.set_shader_parameter("shelf_size", dock_rect.size)
	glass_shader.set_shader_parameter("radius", dock_radius)
	glass_shader.set_shader_parameter("layout_scale", dock_unit)
	glass_shader.set_shader_parameter("native_material", material_ready)
	glass_shader.set_shader_parameter("glass_material", prefs.dock_theme == "glass")
	var palette := _dock_palette()
	var fallback_opacity := 0.7 if prefs.dock_theme == "dark" else (0.08 if prefs.dock_theme == "glass" else 0.52)
	glass_shader.set_shader_parameter("fill_opacity", palette.opacity if material_ready else maxf(fallback_opacity, palette.opacity))
	glass_shader.set_shader_parameter("fill_color", palette.fill)
	glass_shader.set_shader_parameter("edge_color", palette.edge)
	glass_shader.set_shader_parameter("shadow_opacity", palette.shadow)
	glass_shader.set_shader_parameter("shadow_sigma", palette.shadow_sigma * dock_unit)
	glass_shader.set_shader_parameter("shadow_spread", palette.shadow_spread * dock_unit)
	glass_shader.set_shader_parameter("inset_strength", palette.inset)

func _dock_palette() -> Dictionary:
	if prefs.dock_theme == "dark": return Design.DARK
	if prefs.dock_theme == "glass": return Design.GLASS
	return Design.LIGHT

func _set_dock_theme(value: String) -> void:
	if value not in ["light", "dark", "glass"]: return
	prefs.dock_theme = value
	_layout_dock()
	queue_redraw()
	_save()

func _draw() -> void:
	if items.is_empty() or not dock_rect.has_area():
		return
	extra_region = Rect2()
	var palette := _dock_palette()
	var x := layout_x
	var floor_y := layout_floor
	var base := layout_base
	var gap := layout_gap
	var separator_width := layout_separator
	hit_rects.clear()
	var upper := dock_rect.position.y
	for i in range(items.size()):
		var item: Dictionary = items[i]
		if item.kind == "downloads":
			var separator_x := x + 5.0 * dock_unit
			draw_line(Vector2(separator_x, dock_rect.position.y + 10 * dock_unit), Vector2(separator_x, dock_rect.end.y - 11 * dock_unit), palette.separator, dock_unit, true)
			x += separator_width + gap
		var side := base * float(scales.get(item.key, 1.0))
		var bounce := 0.0
		if bounces.has(item.key):
			var time := 1.3 - float(bounces[item.key])
			bounce = absf(sin(time * 9)) * 23 * maxf(0, 1 - time / 1.3)
		var rect := Rect2(x, floor_y - side - bounce, side, side)
		if pending_reorder_positions.has(item.key):
			reorder_offsets[item.key] = float(pending_reorder_positions[item.key]) - rect.get_center().x
			pending_reorder_positions.erase(item.key)
		var slide := float(reorder_offsets.get(item.key, 0.0))
		rect.position.x += slide
		hit_rects.append(Rect2(rect.position.x - gap / 2, floor_y - side - 8, side + gap, side + 32))
		upper = minf(upper, rect.position.y - 6)
		var reveal := clampf(float(reveals.get(item.key, 1.0)), 0.0, 1.0)
		var press_scale := 0.94 if pressed_key == item.key and not dragging and not prefs.reduce_motion else 1.0
		var visual_rect := Rect2(rect.get_center() - rect.size * (0.82 + reveal * 0.18) * press_scale * 0.5, rect.size * (0.82 + reveal * 0.18) * press_scale)
		if dragging and pressed_key == item.key and not prefs.reduce_motion: visual_rect.position.y -= 7.0 * dock_unit
		if focused == i:
			_box(rect.grow(4), Color(0.5, 0.7, 1, 0.12), 17, Color(0.6, 0.8, 1, 0.8))
		_draw_icon(item, visual_rect, reveal)
		var wins: Array = item.windows
		if not wins.is_empty() or not item.get("tray_apps", []).is_empty():
			var active := false
			for win in wins:
				active = active or bool(win.active)
			draw_circle(Vector2(rect.get_center().x, floor_y + 6 * dock_unit), 1.5 * dock_unit, palette.active if active else palette.indicator, true, -1, true)
			if wins.size() > 1:
				draw_circle(Vector2(rect.get_center().x + 5 * dock_unit, floor_y + 6 * dock_unit), dock_unit, Color(palette.indicator, 0.36), true, -1, true)
		x += side + gap
	var active_tip := hover if hover >= 0 and hover_elapsed >= 0.22 and pressed_key.is_empty() else focused
	if active_tip >= 0 and active_tip < items.size() and panel_mode != "context" and hide_amount < 0.1 and not (active_tip == hover and native_ready and items[active_tip].windows.size() > 0):
		var title: String = items[active_tip].name
		if items[active_tip].windows.size() > 1:
			title += " · %d 个窗口" % items[active_tip].windows.size()
		var tw := minf(font.get_string_size(title, HORIZONTAL_ALIGNMENT_LEFT, -1, 13).x, 380) + 26
		var center := hit_rects[active_tip].get_center().x
		var tip := Rect2(clampf(center - tw / 2, 8, size.x - tw - 8), upper - 40, tw, 29)
		var tip_visibility := 1.0 if focused == active_tip and hover != active_tip else tip_alpha
		_box(tip, Color(palette.tip, palette.tip.a * tip_visibility), 8)
		extra_region = tip.grow(5)
		draw_string(font, tip.position + Vector2(13, 20), title, HORIZONTAL_ALIGNMENT_LEFT, tw - 26, 13, Color(palette.text, palette.text.a * tip_visibility))
		upper = tip.position.y - 4
	if notice_time > 0:
		var notice_alpha := 1.0 if prefs.reduce_motion else minf(1.0, minf(notice_age / 0.18, notice_time / 0.28))
		var nw := minf(font.get_string_size(notice, HORIZONTAL_ALIGNMENT_LEFT, -1, 13).x + 28, size.x - 24)
		var nr := Rect2((size.x - nw) / 2, 12 + (1.0 - notice_alpha) * 5.0, nw, 34)
		_box(nr, Color(0.957, 0.957, 0.965, notice_alpha), 10, Color(0, 0, 0, 0.14 * notice_alpha))
		extra_region = extra_region.merge(nr.grow(3)) if extra_region.has_area() else nr.grow(3)
		draw_string(font, nr.position + Vector2(14, 23), notice, HORIZONTAL_ALIGNMENT_LEFT, nw - 28, 13, Color(0.145, 0.145, 0.153, notice_alpha))
		upper = 8
	_update_region(upper)

func _draw_icon(item: Dictionary, rect: Rect2, alpha: float) -> void:
	var kind := str(item.get("kind", "app"))
	# The Dock's own settings button keeps the UDo mark. App and shell icons
	# follow Windows, even when a Figma mockup supplied a decorative icon.
	if kind == "settings" and system_textures.has(kind):
		draw_texture_rect(system_textures[kind], rect, false, Color(1, 1, 1, alpha))
		return
	var path: String = item.get("icon", "")
	if path.is_empty():
		if prefs.dock_theme == "dark" and dark_textures.has(kind):
			draw_texture_rect(dark_textures[kind], rect, false, Color(1, 1, 1, alpha))
			return
		if system_textures.has(kind):
			draw_texture_rect(system_textures[kind], rect, false, Color(1, 1, 1, alpha))
			return
		path = str(state.get("icons", {}).get("builtin:app", ""))
	if path.is_empty():
		return
	if not textures.has(path) and FileAccess.file_exists(path):
		var im := Image.load_from_file(path)
		if im:
			im.fix_alpha_edges()
			im.generate_mipmaps()
			textures[path] = ImageTexture.create_from_image(im)
	if textures.has(path):
		# One texture pass only: no offset black silhouette or icon drop shadow.
		var icon_rect := rect.grow(-1.5)
		draw_texture_rect(textures[path], icon_rect, false, Color(1, 1, 1, alpha))

func _update_region(upper: float) -> void:
	var left := maxf(0, dock_rect.position.x - 10)
	var right := minf(size.x, dock_rect.end.x + 10)
	if extra_region.has_area():
		left = minf(left, extra_region.position.x)
		right = maxf(right, extra_region.end.x)
	var top := maxf(0, upper - 7)
	if hide_amount > 0.96 and panel_mode != "context" and notice_time <= 0:
		top = size.y - 5
	if is_instance_valid(panel) and panel.get_window() == get_window():
		var p := panel.get_rect().grow(12)
		var joint := minf(top, p.end.y)
		get_window().mouse_passthrough_polygon = PackedVector2Array([Vector2(p.position.x, p.position.y), Vector2(p.end.x, p.position.y), Vector2(p.end.x, joint), Vector2(right, joint), Vector2(right, size.y), Vector2(left, size.y), Vector2(left, joint), Vector2(p.position.x, joint)])
	else:
		get_window().mouse_passthrough_polygon = PackedVector2Array([Vector2(left, top), Vector2(right, top), Vector2(right, size.y), Vector2(left, size.y)])

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var pointed := _hit_index(event.position)
		if event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			if pointed >= 0:
				_open_context(items[pointed])
			else:
				_open_desktop_context()
		elif event.button_index == MOUSE_BUTTON_LEFT:
			if event.pressed:
				drag_start = event.position
				drag_index = pointed
				pressed_key = str(items[pointed].key) if pointed >= 0 else ""
				background_pressed = pointed < 0
				dragging = false
			else:
				var source := _item_index(pressed_key)
				if dragging and source >= 0 and pointed >= 0:
					_reorder(source, pointed)
				elif not dragging and source >= 0 and pointed == source:
					_activate(items[source])
				elif background_pressed and pointed < 0 and panel_mode == "context":
					_close_panel()
				_cancel_pointer()
				_rebuild()
	elif event is InputEventMouseMotion and not pressed_key.is_empty():
		dragging = dragging or event.position.distance_to(drag_start) > 8
		mouse_default_cursor_shape = Control.CURSOR_DRAG if dragging else Control.CURSOR_POINTING_HAND

func _hit_index(point: Vector2) -> int:
	for i in range(mini(hit_rects.size(), items.size())):
		if hit_rects[i].has_point(point):
			return i
	return -1

func _item_index(key: String) -> int:
	if key.is_empty():
		return -1
	for i in range(items.size()):
		if str(items[i].key) == key:
			return i
	return -1

func _cancel_pointer() -> void:
	pressed_key = ""
	background_pressed = false
	drag_index = -1
	dragging = false
	mouse_default_cursor_shape = Control.CURSOR_ARROW

func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed:
		return
	if event.keycode == KEY_ESCAPE:
		_close_panel()
	elif event.keycode == KEY_RIGHT or event.keycode == KEY_TAB:
		focused = (focused + 1) % items.size()
	elif event.keycode == KEY_LEFT:
		focused = (focused - 1 + items.size()) % items.size()
	elif event.keycode == KEY_ENTER and focused >= 0:
		_activate(items[focused])

func _activate(item: Dictionary) -> void:
	if item.kind == "settings":
		if panel_mode in ["settings", "statistics", "widgets", "box_settings", "app_picker", "partition_settings"] and is_instance_valid(settings_window):
			settings_window.show()
			settings_window.grab_focus()
		else:
			_open_settings()
		return
	if panel_mode == "context": _close_panel()
	var wins: Array = item.windows
	var trays: Array = item.get("tray_apps", [])
	if wins.is_empty() and not trays.is_empty() and not Input.is_key_pressed(KEY_SHIFT):
		_send({"op": "tray_activate", "id": trays[0].id, "pid": trays[0].pid})
	elif wins.is_empty() or Input.is_key_pressed(KEY_SHIFT):
		if not prefs.reduce_motion: bounces[item.key] = 1.3
		_send({"op": "launch", "path": item.path})
	else:
		# Decide against native foreground state at click time, not stale IPC flags.
		_send({"op": "toggle", "windows": wins})

func _show_notice(text: String) -> void:
	notice = text
	notice_time = 5
	notice_age = 0.0

func _pin_path(path: String) -> void:
	if prefs.pins.size() >= 24:
		_show_notice("最多固定 24 个项目")
		return
	for pin in prefs.pins:
		if _key(pin.path) == _key(path):
			_show_notice("这个应用已经固定在 Dock 上")
			return
	prefs.pins.append({"name": path.replace("\\", "/").get_file().get_basename(), "path": path, "kind": "app"})
	_send({"op": "icon", "path": path})
	_save()
	_rebuild()
	_show_notice("已添加 · 拖动图标可调整顺序")

func _files_dropped(files: PackedStringArray) -> void:
	for path in files:
		_pin_path(path)

func _reorder(from: int, to: int) -> void:
	if from >= prefs.pins.size() or to >= prefs.pins.size():
		return
	if not prefs.reduce_motion:
		pending_reorder_positions.clear()
		for i in range(mini(items.size(), hit_rects.size())):
			pending_reorder_positions[str(items[i].key)] = hit_rects[i].get_center().x
	var pin: Dictionary = prefs.pins.pop_at(from)
	prefs.pins.insert(to, pin)
	_save()
	_rebuild()

func _style_panel() -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.97, 0.97, 0.98, 0.98)
	style.border_color = Color(0.0, 0.0, 0.0, 0.15)
	style.set_border_width_all(1)
	style.set_corner_radius_all(26)
	style.set_content_margin_all(25)
	style.shadow_color = Color(0, 0, 0, 0.17)
	style.shadow_size = 8
	return style

func _new_panel(mode: String, width: float) -> VBoxContainer:
	var detached := mode in ["settings", "statistics", "widgets", "box_settings", "app_picker", "partition_settings"]
	_close_panel(detached)
	panel_mode = mode
	_position_window()
	panel = PanelContainer.new()
	panel.custom_minimum_size = Vector2(width, 0)
	panel.position = Vector2(8, 8) if detached else Vector2((size.x - width) / 2, 20)
	panel.add_theme_stylebox_override("panel", SettingsDesign.panel() if mode in ["settings", "statistics", "widgets", "box_settings", "app_picker", "partition_settings"] else _style_panel())
	if detached:
		_ensure_settings_window()
		settings_window.add_child(panel)
		panel.gui_input.connect(_settings_background_input)
		_finish_settings_layout.call_deferred(panel.get_instance_id())
	else:
		add_child(panel)
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 10 if mode in ["settings", "statistics", "widgets", "box_settings", "app_picker", "partition_settings"] else 13)
	panel.add_child(body)
	return body

func _ensure_settings_window() -> void:
	if is_instance_valid(settings_window): return
	settings_window = Window.new()
	settings_window.title = "有兜设置"
	settings_window.visible = false
	settings_window.force_native = true
	# No transient owner: an owned window would inherit the topmost Dock's band.
	settings_window.transient = false
	settings_window.exclusive = false
	settings_window.always_on_top = false
	settings_window.borderless = true
	settings_window.transparent = true
	settings_window.transparent_bg = true
	settings_window.unresizable = true
	settings_window.gui_embed_subwindows = true
	settings_window.theme = theme
	add_child(settings_window)
	settings_window.close_requested.connect(_close_panel)
	settings_window.files_dropped.connect(_files_dropped)

func _finish_settings_layout(content_id: int) -> void:
	# A deferred callback from a replaced page must not resize the current page.
	var content = instance_from_id(content_id)
	if not is_instance_valid(content) or content != panel: return
	content.size = content.get_combined_minimum_size()
	settings_window.size = Vector2i(content.size.ceil()) + Vector2i(16, 16)
	var available := _screen_area(_selected_screen()) if not settings_placed else DisplayServer.screen_get_usable_rect(settings_window.current_screen)
	if not settings_placed:
		settings_window.position = available.position + Vector2i(Vector2(available.size - settings_window.size) / 2.0)
		settings_placed = true
	else:
		# Preserve a dragged position when navigating; clamp only for the new size.
		settings_window.position = Vector2i(clampi(settings_window.position.x, available.position.x, maxi(available.position.x, available.end.x - settings_window.size.x)), clampi(settings_window.position.y, available.position.y, maxi(available.position.y, available.end.y - settings_window.size.y)))
	Motion.bind_buttons(content, bool(prefs.reduce_motion))
	if not content.has_meta("motion_entered"):
		content.set_meta("motion_entered", true)
		Motion.enter(content, bool(prefs.reduce_motion))
		if content.get_child_count() > 0: Motion.reveal_rows(content.get_child(0), bool(prefs.reduce_motion))
	settings_window.show()
	settings_window.grab_focus()

func _settings_title_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		DisplayServer.window_start_drag(settings_window.get_window_id())

func _settings_background_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.position.y <= 48:
		_settings_title_input(event)

func _label(text: String, font_size := 14, color := Color("252527")) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", color)
	return label

func _button(text: String, callback: Callable, accent := false) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size.y = 32
	button.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	var normal := StyleBoxFlat.new()
	normal.bg_color = Design.BLUE if accent else Color("e6e6e6")
	normal.set_corner_radius_all(100)
	for key in ["font_color", "font_hover_color", "font_pressed_color"]:
		button.add_theme_color_override(key, Color.WHITE if accent else Color("303034"))
	normal.content_margin_left = 14
	normal.content_margin_right = 14
	button.add_theme_stylebox_override("normal", normal)
	var over: StyleBoxFlat = normal.duplicate()
	over.bg_color = normal.bg_color.lightened(0.14)
	button.add_theme_stylebox_override("hover", over)
	button.add_theme_stylebox_override("pressed", normal)
	button.pressed.connect(callback)
	if panel_mode in ["settings", "statistics", "widgets", "box_settings", "app_picker", "partition_settings"]:
		button.custom_minimum_size.y = SettingsDesign.CONTROL_HEIGHT
		button.add_theme_font_size_override("font_size", SettingsDesign.FONT_SIZE)
		var neutral := SettingsDesign.button()
		for key in ["normal", "hover", "pressed"]:
			button.add_theme_stylebox_override(key, neutral)
		var clicked: StyleBoxFlat = neutral.duplicate()
		clicked.bg_color = Color(0, 0, 0, 0.15) # Push Button 502:5915.
		button.add_theme_stylebox_override("pressed", clicked)
		for key in ["font_color", "font_hover_color", "font_pressed_color"]:
			button.add_theme_color_override(key, SettingsDesign.TEXT)
		button.add_theme_stylebox_override("focus", SettingsDesign.focus())
	return button

func _form_group(body: VBoxContainer) -> VBoxContainer:
	var group := PanelContainer.new()
	var style := SettingsDesign.rounded(SettingsDesign.GROUP, SettingsDesign.GROUP_RADIUS)
	style.content_margin_left = SettingsDesign.GROUP_INSET
	style.content_margin_right = SettingsDesign.GROUP_INSET
	style.content_margin_top = 0
	style.content_margin_bottom = 0
	group.add_theme_stylebox_override("panel", style)
	body.add_child(group)
	var rows := VBoxContainer.new()
	rows.add_theme_constant_override("separation", 0)
	group.add_child(rows)
	return rows

func _row(body: VBoxContainer, title: String, subtitle := "") -> HBoxContainer:
	if body.get_child_count() > 0:
		var separator := HSeparator.new()
		var line := StyleBoxLine.new()
		line.color = SettingsDesign.SEPARATOR
		line.thickness = 1
		separator.add_theme_stylebox_override("separator", line)
		separator.add_theme_constant_override("separation", 1)
		body.add_child(separator)
	var row := HBoxContainer.new()
	# A 41px content row plus the following 1px divider follows the 42px grid.
	row.custom_minimum_size.y = SettingsDesign.ROW_HEIGHT - 1
	row.add_theme_constant_override("separation", 16)
	row.tooltip_text = subtitle
	body.add_child(row)
	var label := _label(title, SettingsDesign.FONT_SIZE, SettingsDesign.TEXT)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	label.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	row.add_child(label)
	return row

func _open_settings() -> void:
	var body := _new_panel("settings", 500)
	var heading := HBoxContainer.new()
	heading.custom_minimum_size.y = 24
	body.add_child(heading)
	var title := _label("有兜设置", 15, SettingsDesign.TEXT)
	var heading_font := SystemFont.new()
	heading_font.font_names = font.font_names
	heading_font.font_weight = 700
	title.add_theme_font_override("font", heading_font)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	title.mouse_filter = Control.MOUSE_FILTER_STOP
	title.gui_input.connect(_settings_title_input)
	heading.add_child(title)
	heading.add_child(_button("完成", _close_panel))
	var appearance := _form_group(body)
	var theme_row := _row(appearance, "Dock 外观")
	var theme_menu := DockChoice.new()
	theme_menu.name = "DockThemeChoice"
	theme_menu.add_item("浅色", 0)
	theme_menu.add_item("深色", 1)
	theme_menu.add_item("玻璃材质", 2)
	theme_menu.select(["light", "dark", "glass"].find(str(prefs.dock_theme)))
	theme_menu.item_selected.connect(func(index: int): _set_dock_theme(["light", "dark", "glass"][index]))
	theme_row.add_child(theme_menu)
	_slider_row(appearance, "图标大小", "icon_size", 36, 72, 1)
	_slider_row(appearance, "悬停放大", "magnification", 1, 2, 0.05)
	var monitor_row := _row(appearance, "显示器")
	screen_menu = DockChoice.new()
	screen_menu.add_item("跟随主显示器", -1)
	for i in range(DisplayServer.get_screen_count()):
		var resolution := DisplayServer.screen_get_size(i)
		screen_menu.add_item("显示器 %d%s" % [i + 1, "（主屏）" if i == DisplayServer.get_primary_screen() else ""], i)
		screen_menu.get_popup().set_item_tooltip(i + 1, "%d × %d" % [resolution.x, resolution.y])
	screen_menu.select(int(prefs.screen) + 1 if int(prefs.screen) >= 0 else 0)
	screen_menu.item_selected.connect(func(index: int): prefs.screen = index - 1; _save(); _position_window())
	monitor_row.add_child(screen_menu)
	var behavior := _form_group(body)
	var auto_row := _row(behavior, "自动隐藏 Dock", "鼠标移到屏幕底边时呼出")
	var auto := DockSwitch.new()
	auto.name = "AutoHideToggle"
	auto.button_pressed = bool(prefs.auto_hide)
	auto.toggled.connect(func(value: bool): prefs.auto_hide = value; _save())
	auto_row.add_child(auto)
	var taskbar_row := _row(behavior, "隐藏 Windows 任务栏", "退出有兜 后自动恢复；Ctrl + Alt + F12 可立即恢复")
	check_hide = DockSwitch.new()
	check_hide.button_pressed = bool(prefs.hide_taskbar)
	check_hide.disabled = not native_ready or bridge_pid <= 0
	check_hide.toggled.connect(func(value: bool):
		prefs.hide_taskbar = value
		_send({"op": "hide", "value": value})
		_save())
	taskbar_row.add_child(check_hide)
	var tray_row := _row(behavior, "显示托盘应用", "将仅在托盘运行的应用自动加入 Dock；已固定的应用不受影响")
	var tray_toggle := DockSwitch.new()
	tray_toggle.name = "TrayAppsToggle"
	tray_toggle.button_pressed = bool(prefs.show_tray_apps)
	tray_toggle.toggled.connect(func(value: bool):
		prefs.show_tray_apps = value
		_rebuild()
		_position_window()
		_save())
	tray_row.add_child(tray_toggle)
	var keys_row := _row(behavior, "按键可视化", "输入字母或数字时在屏幕底部弹出；只显示按键，不保存输入记录")
	var keys_toggle := DockSwitch.new()
	keys_toggle.button_pressed = bool(prefs.key_visualizer)
	keys_toggle.toggled.connect(func(value: bool): prefs.key_visualizer = value; _sync_key_visualizer(); _save())
	keys_row.add_child(keys_toggle)
	var motion_row := _row(behavior, "减少动效", "关闭过渡动画，保留必要的状态反馈")
	var motion_toggle := DockSwitch.new()
	motion_toggle.button_pressed = bool(prefs.reduce_motion)
	motion_toggle.toggled.connect(func(value: bool):
		prefs.reduce_motion = value
		if value:
			bounces.clear()
			reorder_offsets.clear()
			pending_reorder_positions.clear()
			for key in reveals: reveals[key] = 1.0
		Motion.bind_buttons(panel, value)
		_save())
	motion_row.add_child(motion_toggle)
	var statistics_group := _form_group(body)
	var statistics_row := _row(statistics_group, "按键统计", "查看每天的总次数与各按键次数")
	stats_summary = _button("今天 %s 次  ›" % str(int(state.get("key_statistics", {}).get("total", 0))), _open_statistics)
	statistics_row.add_child(stats_summary)
	var widgets_group := _form_group(body)
	var widgets_row := _row(widgets_group, "桌面组件", "时钟与今日按键次数 · 在桌面实时更新，可移动和缩放")
	widgets_row.add_child(_button("管理组件…", _open_widgets))
	var zones_group := _form_group(body)
	var zones_error := str(state.get("zones_error", ""))
	var zones_row := _row(zones_group, "收纳盒子", "右键桌面新建 · 拖入图标整理" if zones_error.is_empty() else zones_error)
	zones_row.add_child(_button("新建盒子", func(): _send({"op": "box_create"})))
	var partition_row := _row(zones_group, "无感桌面分区", "独立布局 · 日常隐藏边界，编辑时显示轮廓")
	partition_row.add_child(_button("管理分区…", func(): _send({"op": "partition_settings"})))
	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation", 10)
	body.add_child(actions)
	actions.add_child(_button("添加应用…", _open_app_picker))
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	actions.add_child(spacer)
	actions.add_child(_button("退出有兜", _quit))
	var version_divider := HSeparator.new()
	var version_line := StyleBoxLine.new()
	version_line.color = SettingsDesign.SEPARATOR
	version_line.thickness = 1
	version_divider.add_theme_stylebox_override("separator", version_line)
	body.add_child(version_divider)
	var version_footer := HBoxContainer.new()
	version_footer.custom_minimum_size.y = 20
	body.add_child(version_footer)
	var product_name := _label("有兜 · UDo", 11, SettingsDesign.SECONDARY)
	product_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	version_footer.add_child(product_name)
	version_footer.add_child(_label("预览版  v%s" % str(ProjectSettings.get_setting("application/config/version", "0.1.0")), 11, SettingsDesign.SECONDARY))
	if not state.get("hotkey", true):
		taskbar_row.tooltip_text = "快捷键被其他程序占用；可从托盘菜单恢复任务栏"

func _slider_row(body: VBoxContainer, title: String, key: String, low: float, high: float, step: float) -> void:
	var row := _row(body, title)
	var value := _label("%d px" % prefs[key] if key == "icon_size" else "%.2f ×" % prefs[key], 11, SettingsDesign.SECONDARY)
	value.custom_minimum_size.x = 52
	value.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	var slider := DockSlider.new()
	slider.name = key + "Slider"
	slider.min_value = low
	slider.max_value = high
	slider.step = step
	slider.value = float(prefs[key])
	slider.drag_started.connect(func(): settings_slider_drag = key; _preview_setting(key))
	slider.drag_ended.connect(func(_changed: bool): settings_slider_drag = ""; settings_preview_until = elapsed + 0.8)
	slider.value_changed.connect(func(v: float):
		prefs[key] = v
		_preview_setting(key)
		value.text = "%d px" % v if key == "icon_size" else "%.2f ×" % v
		_save()
		_position_window())
	row.add_child(slider)
	row.add_child(value)

func _preview_setting(key: String) -> void:
	settings_preview_until = elapsed + 0.8
	magnification_preview_key = ""
	if key == "magnification" and not items.is_empty():
		var index := hover if hover >= 0 else int((items.size() - 1) / 2)
		magnification_preview_key = str(items[index].key)
	_layout_dock()
	_publish_material()
	queue_redraw()

func _native_point(x: int, y: int) -> Vector2i:
	var origin := Vector2i.ZERO
	for monitor in state.get("screens", []):
		origin.x = mini(origin.x, int(monitor.x))
		origin.y = mini(origin.y, int(monitor.y))
	return Vector2i(x, y) - origin

func _show_menu(entries: Array, at := DisplayServer.mouse_get_position()) -> void:
	if is_instance_valid(context_menu):
		context_menu.close()
	else:
		context_menu = preload("res://scripts/dock_menu.gd").new()
		add_child(context_menu)
		context_menu.dismissed.connect(func():
			_send({"op": "watch_menu", "hwnd": "0"})
			_send({"op": "box_ui_closed"})
			if not active_box_editor.is_empty(): _send({"op": "box_ui_open", "id": active_box_editor}))
	context_menu.open(entries, at, theme, bool(prefs.reduce_motion))
	_send({"op": "watch_menu", "hwnd": str(DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, context_menu.get_window_id()))})

func _open_desktop_context() -> void:
	_show_menu([
		{"text": "新建收纳盒子", "run": func(): _send({"op": "box_create"})},
		{"text": "新建无感分区", "run": func(): _send({"op": "partition_canvas"})},
		{"text": "管理桌面分区…", "run": func(): _send({"op": "partition_settings"})},
		{"separator": true},
		{"text": "有兜设置…", "run": _open_settings}
	])

func _open_context(item: Dictionary) -> void:
	if item.kind == "settings":
		_open_desktop_context()
		return
	var entries: Array = [{"text": "新建收纳盒子", "run": func(): _send({"op": "box_create"})}, {"separator": true}]
	entries.insert(1, {"text": "无感桌面分区…", "run": func(): _send({"op": "partition_settings"})})
	entries.append({"text": "打开新窗口" if not item.windows.is_empty() else "打开", "run": func():
		if item.windows.is_empty() and not item.get("tray_apps", []).is_empty():
			var tray_app: Dictionary = item.tray_apps[0]
			_send({"op": "tray_activate", "id": tray_app.id, "pid": tray_app.pid})
		else:
			_send({"op": "launch", "path": item.path})})
	if item.kind in ["app", "files", "browser", "terminal", "notes"]:
		if item.pinned:
			entries.append({"text": "从 Dock 移除", "run": func():
				for i in range(prefs.pins.size()):
					if _key(prefs.pins[i].path) == item.key:
						prefs.pins.remove_at(i)
						break
				_save(); _rebuild()})
		else:
			entries.append({"text": "固定到 Dock", "run": func(): _pin_path(item.path)})
	if not item.windows.is_empty():
		var windows_entries: Array = []
		for win in item.windows:
			windows_entries.append({"text": str(win.title).left(28), "run": func(): _send({"op": "activate", "hwnd": win.hwnd, "pid": win.pid})})
		entries.append({"text": "应用窗口", "children": windows_entries})
		entries.append({"text": "关闭应用窗口", "run": func():
			for win in item.windows:
				_send({"op": "close", "hwnd": win.hwnd, "pid": win.pid})})
	_show_menu(entries)

func _open_app_picker() -> void:
	if native_ready and bridge_pid > 0:
		_send({"op": "pick_app"})
		return
	app_picker = preload("res://scripts/app_picker.gd").new()
	app_picker.open(self)

func _widget_info(kind: String) -> Dictionary:
	var widgets = state.get("widgets", [])
	if widgets is Array:
		for entry in widgets:
			if entry is Dictionary and str(entry.get("kind", "")) == kind: return entry
	return {"kind": kind, "enabled": false, "width": 210 if kind == "clock" else 290}

func _open_widgets() -> void:
	var body := _new_panel("widgets", 540)
	var heading := HBoxContainer.new()
	heading.custom_minimum_size.y = 24
	body.add_child(heading)
	var title := _label("桌面组件", 15, SettingsDesign.TEXT)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	title.mouse_filter = Control.MOUSE_FILTER_STOP
	title.gui_input.connect(_settings_title_input)
	heading.add_child(title)
	heading.add_child(_button("返回设置", _open_settings))
	for kind in ["clock", "keys"]:
		_widget_card(body, kind)
	body.add_child(_label("在桌面上拖动组件可移动；拖动右下角可调整大小。", 12, SettingsDesign.SECONDARY))
	if not str(state.get("widgets_error", "")).is_empty():
		body.add_child(_label(str(state.widgets_error), 12, SettingsDesign.SECONDARY))

func _widget_card(body: VBoxContainer, kind: String) -> void:
	var info := _widget_info(kind)
	var card := PanelContainer.new()
	var background := SettingsDesign.rounded(SettingsDesign.GROUP, SettingsDesign.GROUP_RADIUS)
	background.content_margin_left = 14
	background.content_margin_right = 14
	background.content_margin_top = 14
	background.content_margin_bottom = 14
	card.add_theme_stylebox_override("panel", background)
	body.add_child(card)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 18)
	card.add_child(row)
	var preview := WidgetPreview.new()
	preview.kind = kind
	preview.total = int(state.get("key_statistics", {}).get("total", 0))
	preview.custom_minimum_size = Vector2(100, 100) if kind == "clock" else Vector2(150, 84)
	preview.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	row.add_child(preview)
	var details := VBoxContainer.new()
	details.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details.add_theme_constant_override("separation", 8)
	row.add_child(details)
	var name := _label("时钟" if kind == "clock" else "今日按键次数", 14, SettingsDesign.TEXT)
	details.add_child(name)
	var description := _label("指针与日期每秒更新" if kind == "clock" else "沿用按键统计，每次按下即时累加", 12, SettingsDesign.SECONDARY)
	details.add_child(description)
	var show_row := HBoxContainer.new()
	show_row.add_child(_label("显示在桌面", 13, SettingsDesign.TEXT))
	var show_space := Control.new()
	show_space.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	show_row.add_child(show_space)
	var toggle := DockSwitch.new()
	toggle.name = "WidgetClockToggle" if kind == "clock" else "WidgetKeysToggle"
	toggle.button_pressed = bool(info.get("enabled", false))
	toggle.toggled.connect(func(value: bool): _send({"op": "widget_show", "kind": kind, "enabled": value}))
	show_row.add_child(toggle)
	details.add_child(show_row)
	var size_row := HBoxContainer.new()
	size_row.add_theme_constant_override("separation", 8)
	size_row.add_child(_label("大小", 12, SettingsDesign.SECONDARY))
	var slider := DockSlider.new()
	slider.name = "WidgetClockSize" if kind == "clock" else "WidgetKeysSize"
	slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	slider.custom_minimum_size.x = 120
	slider.min_value = 140 if kind == "clock" else 190
	slider.max_value = 420 if kind == "clock" else 560
	slider.step = 1
	slider.value = int(info.get("width", 210 if kind == "clock" else 290))
	size_row.add_child(slider)
	var size_label := _label("%d px" % slider.value, 11, SettingsDesign.SECONDARY)
	size_label.custom_minimum_size.x = 48
	size_row.add_child(size_label)
	slider.value_changed.connect(func(value: float):
		size_label.text = "%d px" % value
		_send({"op": "widget_size", "kind": kind, "width": int(value)}))
	details.add_child(size_row)
	var reset := _button("恢复默认位置", func(): _send({"op": "widget_reset", "kind": kind}))
	reset.size_flags_horizontal = Control.SIZE_SHRINK_END
	details.add_child(reset)
	if kind == "keys" and not bool(prefs.key_statistics):
		details.add_child(_label("按键统计已暂停；开启后数字继续实时更新。", 11, SettingsDesign.SECONDARY))

func _open_statistics() -> void:
	var body := _new_panel("statistics", minf(760, _screen_area(_selected_screen()).size.x - 64))
	stats_panel = preload("res://scripts/key_statistics_panel.gd").new()
	stats_panel.recording = bool(prefs.key_statistics)
	stats_panel.title_pressed.connect(_settings_title_input)
	stats_panel.back_requested.connect(_open_settings)
	stats_panel.date_requested.connect(func(date: String): _send({"op": "key_statistics_date", "date": date}))
	stats_panel.recording_changed.connect(func(value: bool):
		prefs.key_statistics = value
		_sync_key_statistics()
		_save())
	body.add_child(stats_panel)
	stats_panel.update_report(state.get("key_statistics", {}))

func _close_panel(keep_settings_window := false) -> void:
	if panel_mode == "box_settings":
		active_box_editor = ""
		_send({"op": "box_ui_closed"})
	settings_slider_drag = ""
	settings_preview_until = 0.0
	magnification_preview_key = ""
	if is_instance_valid(panel):
		panel.get_parent().remove_child(panel)
		panel.queue_free()
		panel = null
	if is_instance_valid(settings_window) and not keep_settings_window:
		settings_window.hide()
	stats_panel = null
	stats_summary = null
	panel_mode = ""
	_position_window()

func _quit() -> void:
	if quitting:
		return
	quitting = true
	_sync_key_statistics()
	_sync_key_visualizer()
	key_socket.close()
	_send({"op": "hide", "value": false})
	_send({"op": "quit"})
	var deadline := Time.get_ticks_msec() + 12000
	while Time.get_ticks_msec() < deadline and bridge_pid > 0 and not FileAccess.file_exists(ipc.path_join("restore-complete.txt")):
		await get_tree().create_timer(0.1).timeout
	get_tree().quit()

func _qa_theme_capture() -> void:
	var results: Array = []
	var original_theme: String = prefs.dock_theme
	qa_input = true
	qa_pointer = Vector2(5, 5)
	_open_settings()
	await get_tree().create_timer(0.5).timeout
	var picker: OptionButton = panel.find_child("DockThemeChoice", true, false)
	var initial_rect := dock_rect
	var light_image: Image
	for index in range(3):
		picker.select(index)
		picker.item_selected.emit(index)
		var mode: String = ["light", "dark", "glass"][index]
		await get_tree().create_timer(0.4).timeout
		await RenderingServer.frame_post_draw
		var frame := get_viewport().get_texture().get_image()
		frame.save_png("res://qa-theme-%s-render.png" % mode)
		results.append({"check": "%s_native_effect_active" % mode, "passed": state.get("material", "") == "blur" and str(state.get("material_error", "")).is_empty()})
		results.append({"check": "%s_picker_applies_without_geometry_jump" % mode, "passed": prefs.dock_theme == mode and dock_rect == initial_rect})
		_send({"op": "snapshot", "name": "theme-%s" % mode})
		await get_tree().create_timer(0.3).timeout
		if FileAccess.file_exists(ipc.path_join("theme-%s.png" % mode)):
			DirAccess.copy_absolute(ipc.path_join("theme-%s.png" % mode), ProjectSettings.globalize_path("res://qa-theme-%s.png" % mode))
			var desktop := Image.load_from_file(ipc.path_join("theme-%s.png" % mode))
			var sample := Vector2i(dock_rect.position + Vector2(dock_rect.size.x / 2.0, 7 * dock_unit))
			if index == 0: light_image = desktop
			elif index == 1 and light_image:
				results.append({"check": "native_dark_surface_is_darker", "passed": desktop.get_pixelv(sample).get_luminance() < light_image.get_pixelv(sample).get_luminance() - 0.05})
			var body := desktop.get_pixelv(sample).get_luminance()
			var edge := 0.0
			for y in range(2):
				edge = maxf(edge, desktop.get_pixel(int(sample.x), int(dock_rect.position.y) + y).get_luminance())
			if mode != "glass":
				results.append({"check": "%s_additive_edge_lightens_backdrop" % mode, "passed": edge > body + 0.015})
		else:
			results.append({"check": "%s_native_snapshot_exists" % mode, "passed": false})
	results.append({"check": "all_dark_builtin_assets_loaded", "passed": dark_textures.size() == Design.DARK_ICONS.size()})
	_open_settings()
	await get_tree().create_timer(0.3).timeout
	results.append({"check": "reopened_settings_retains_theme_selection", "passed": panel.find_child("DockThemeChoice", true, false).selected == 2})
	await RenderingServer.frame_post_draw
	settings_window.get_texture().get_image().save_png("res://qa-theme-settings.png")
	var output := FileAccess.open("res://tests/theme-results.json", FileAccess.WRITE)
	output.store_string(JSON.stringify(results, "\t"))
	output.close()
	for check in results:
		if not check.passed: push_error("QA_FAILED: " + check.check)
	prefs.dock_theme = original_theme
	await _quit()

func _qa_capture() -> void:
	var results: Array = []
	var original_screen: int = int(prefs.screen)
	qa_input = true
	qa_pointer = Vector2(5, 5)
	for display in range(DisplayServer.get_screen_count()):
		prefs.screen = display
		_close_panel()
		_position_window()
		await get_tree().create_timer(0.8).timeout
		results.append(_check_native_center(display, "dock"))
		results.append(_check_shelf_bounds(display, "idle"))
		qa_pointer = hit_rects[1].get_center()
		await get_tree().create_timer(0.6).timeout
		results.append(_check_shelf_bounds(display, "magnified"))
		qa_pointer = Vector2(5, 5)
		_send({"op": "snapshot", "name": "screen-%d-dock" % display})
		_open_settings()
		await get_tree().create_timer(0.8).timeout
		results.append(_check_native_center(display, "settings"))
		_send({"op": "snapshot", "name": "screen-%d-settings" % display})
		await get_tree().create_timer(0.2).timeout
	prefs.screen = DisplayServer.get_primary_screen()
	_close_panel()
	_position_window()
	await get_tree().create_timer(0.8).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(ProjectSettings.globalize_path("res://qa-dock.png"))
	qa_input = true
	qa_pointer = hit_rects[1].get_center()
	await get_tree().create_timer(0.7).timeout
	results.append({"check": "hover_magnification", "passed": float(scales[items[1].key]) > 1.2})
	var sample_key: String = items[1].key
	var low_scale := float(scales[sample_key])
	var high_scale := low_scale
	for sample in range(12):
		await get_tree().create_timer(0.025).timeout
		low_scale = minf(low_scale, float(scales[sample_key]))
		high_scale = maxf(high_scale, float(scales[sample_key]))
	results.append({"check": "stationary_hover_settles", "passed": high_scale - low_scale < 0.01, "scale_variation": high_scale - low_scale})
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(ProjectSettings.globalize_path("res://qa-hover.png"))
	_send({"op": "snapshot", "name": "hover-desktop"})
	await get_tree().create_timer(0.3).timeout
	qa_pointer = Vector2(hit_rects[1].get_center().x, hit_rects[1].position.y + 12)
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "magnification_stays_over_enlarged_icon", "passed": float(scales[items[1].key]) > 1.2})
	qa_pointer = Vector2(5, 5)
	prefs.auto_hide = true
	await get_tree().create_timer(0.8).timeout
	results.append({"check": "auto_hide", "passed": hide_amount > 0.95})
	results.append({"check": "native_material_hides", "passed": not bool(state.get("material_visible", true))})
	qa_pointer = Vector2(size.x / 2, size.y - 2)
	await get_tree().create_timer(0.7).timeout
	results.append({"check": "bottom_edge_reveal", "passed": hide_amount < 0.05})
	prefs.auto_hide = false
	qa_pointer = Vector2(5, 5)
	_open_settings()
	await get_tree().create_timer(1.0).timeout
	results.append({"check": "settings_fits_native_window", "passed": panel.get_rect().end.y <= settings_window.size.y - 8})
	results.append({"check": "settings_is_independent_non_topmost_window", "passed": not settings_window.is_embedded() and not settings_window.transient and not settings_window.always_on_top and settings_window.get_window_id() != get_window().get_window_id()})
	var moved_position := settings_window.position + Vector2i(32, -24)
	settings_window.position = moved_position
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "settings_position_survives_dock_poll", "passed": settings_window.position == moved_position})
	results.append({"check": "bridge_connected", "passed": native_ready})
	# Exercise the actual controls while the independent settings window stays open.
	var preview_magnification: float = prefs.magnification
	var preview_size: float = prefs.icon_size
	var magnification_slider: HSlider = panel.find_child("magnificationSlider", true, false)
	magnification_slider.value = 1.9 if preview_magnification < 1.9 else 1.8
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "settings_slider_previews_magnification_before_done", "passed": panel_mode == "settings" and float(scales.get(magnification_preview_key, 1.0)) > float(prefs.magnification) - 0.04})
	magnification_slider.value = 1.0
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "settings_slider_can_disable_magnification_live", "passed": float(scales.get(magnification_preview_key, 1.0)) < 1.01})
	magnification_slider.value = preview_magnification
	var size_slider: HSlider = panel.find_child("icon_sizeSlider", true, false)
	var before_size := layout_base
	size_slider.value = preview_size + 4 if preview_size <= 68 else preview_size - 4
	await get_tree().create_timer(0.2).timeout
	results.append({"check": "settings_icon_size_applies_before_done", "passed": panel_mode == "settings" and absf(layout_base - before_size) > 1.0})
	size_slider.value = preview_size
	settings_preview_until = 0.0
	qa_pointer = Vector2(_rest_centers()[1], layout_floor - layout_base / 2)
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "dock_hover_works_with_settings_open", "passed": float(scales[items[1].key]) > float(prefs.magnification) - 0.04})
	qa_pointer = Vector2(5, 5)
	var auto_toggle: BaseButton = panel.find_child("AutoHideToggle", true, false)
	auto_toggle.button_pressed = true
	await get_tree().create_timer(0.8).timeout
	results.append({"check": "auto_hide_applies_before_done", "passed": panel_mode == "settings" and hide_amount > 0.95})
	qa_pointer = Vector2(size.x / 2, size.y - 2)
	await get_tree().create_timer(0.7).timeout
	results.append({"check": "bottom_reveal_works_with_settings_open", "passed": hide_amount < 0.05})
	auto_toggle.button_pressed = false
	qa_pointer = Vector2(5, 5)
	await get_tree().create_timer(0.3).timeout
	await RenderingServer.frame_post_draw
	settings_window.get_texture().get_image().save_png(ProjectSettings.globalize_path("res://qa-settings.png"))
	_send({"op": "snapshot", "name": "settings-desktop"})
	await get_tree().create_timer(0.3).timeout
	for capture in ["hover-desktop", "settings-desktop"]:
		if FileAccess.file_exists(ipc.path_join(capture + ".png")):
			DirAccess.copy_absolute(ipc.path_join(capture + ".png"), ProjectSettings.globalize_path("res://qa-" + capture + ".png"))
	var selector_background := settings_window.get_texture().get_image()
	screen_menu.show_popup()
	await get_tree().create_timer(0.2).timeout
	results.append({"check": "selector_popup_embedded_transparent", "passed": screen_menu.get_popup().is_embedded() and screen_menu.get_popup().transparent_bg})
	await RenderingServer.frame_post_draw
	var selector_image := settings_window.get_texture().get_image()
	selector_image.save_png("res://qa-selector.png")
	var menu := screen_menu.get_popup()
	var menu_darkening := 0.0
	for corner_point in [menu.position, menu.position + Vector2i(menu.size.x - 10, 0), menu.position + Vector2i(0, menu.size.y - 10), menu.position + menu.size - Vector2i(10, 10)]:
		for y in range(10):
			for x in range(10):
				var pixel := Vector2i(corner_point.x + x, corner_point.y + y)
				# Outside the rounded menu the pre-existing switch/field may be gray.
				var expected_min := minf(245.0 / 255.0, selector_background.get_pixelv(pixel).r)
				menu_darkening = maxf(menu_darkening, expected_min - selector_image.get_pixelv(pixel).r)
	results.append({"check": "popup_four_corners_have_no_dark_fringe", "passed": menu_darkening <= 0.012, "excess_darkening": menu_darkening})
	screen_menu.get_popup().hide()
	_open_widgets()
	await get_tree().create_timer(0.45).timeout
	results.append({"check": "widget_settings_has_both_components", "passed": panel_mode == "widgets" and is_instance_valid(panel.find_child("WidgetClockToggle", true, false)) and is_instance_valid(panel.find_child("WidgetKeysToggle", true, false))})
	results.append({"check": "widget_settings_has_size_controls", "passed": is_instance_valid(panel.find_child("WidgetClockSize", true, false)) and is_instance_valid(panel.find_child("WidgetKeysSize", true, false))})
	results.append({"check": "widget_settings_fits", "passed": panel.get_rect().end.y <= settings_window.size.y - 8})
	await RenderingServer.frame_post_draw
	settings_window.get_texture().get_image().save_png("res://qa-widgets-settings.png")
	_open_statistics()
	await get_tree().create_timer(0.4).timeout
	results.append({"check": "statistics_panel_fits", "passed": panel.get_rect().end.y <= settings_window.size.y - 8})
	results.append({"check": "statistics_keeps_dragged_position", "passed": settings_window.position == moved_position})
	results.append({"check": "statistics_today_report_loaded", "passed": stats_panel.total_label.text != "…" and stats_panel.keyboard.keys.size() >= 104})
	# Render explicit fixture totals without writing them to the user's archive.
	set_process(false)
	stats_panel.update_report({"date": Time.get_date_string_from_system(), "total": 12345, "counts": {"65": 1320, "66": 901, "69": 7184, "32": 2450, "13": 368, "162": 122}})
	for i in range(stats_panel.keyboard.keys.size()):
		if stats_panel.keyboard.keys[i].code == 65:
			var hover_event := InputEventMouseMotion.new()
			hover_event.position = stats_panel.keyboard.keys[i].rect.get_center()
			stats_panel.keyboard._gui_input(hover_event)
	await RenderingServer.frame_post_draw
	var statistics_image := settings_window.get_texture().get_image()
	statistics_image.save_png("res://qa-statistics.png")
	# Inspect a real rendered corner away from text: no darker AA wedges than
	# the intended 13% outline (222/255), allowing 4 levels for raster rounding.
	var escape_rect: Rect2 = stats_panel.keyboard.keys[0].rect
	var corner := Vector2i(stats_panel.keyboard.global_position + escape_rect.position)
	var darkest := 1.0
	for y in range(7):
		for x in range(7):
			darkest = minf(darkest, statistics_image.get_pixel(corner.x + x, corner.y + y).r)
	results.append({"check": "key_corner_has_no_black_fringe", "passed": darkest >= 0.85, "darkest": darkest})
	results.append({"check": "statistics_totals_and_hover_counts_formatted", "passed": stats_panel.total_label.text == "12,345 次" and stats_panel.keyboard.hover_text() == "A · 1,320 次"})
	var key_layout_ok := true
	var key_codes := {}
	for i in range(stats_panel.keyboard.keys.size()):
		var key: Dictionary = stats_panel.keyboard.keys[i]
		key_layout_ok = key_layout_ok and not key_codes.has(key.code) and stats_panel.keyboard.hit_test(key.rect.get_center()) == i
		key_codes[key.code] = true
		for j in range(i):
			key_layout_ok = key_layout_ok and not key.rect.intersects(stats_panel.keyboard.keys[j].rect)
	results.append({"check": "all_104_keys_have_unique_nonoverlapping_hits", "passed": key_layout_ok and key_codes.size() == 104 and key_codes.has(13) and key_codes.has(269)})
	for i in range(stats_panel.keyboard.keys.size()):
		if stats_panel.keyboard.keys[i].code == 90: stats_panel.keyboard.hovered = i
	results.append({"check": "unused_key_hover_shows_zero", "passed": stats_panel.keyboard.hover_text() == "Z · 0 次"})
	stats_panel.change_day(-1)
	results.append({"check": "date_change_clears_previous_key_counts", "passed": not stats_panel.keyboard.loaded and stats_panel.keyboard.counts.is_empty()})
	set_process(true)
	await get_tree().create_timer(0.5).timeout
	results.append({"check": "statistics_history_query_completes", "passed": stats_panel.total_label.text != "…" and stats_panel.selected_date < Time.get_date_string_from_system()})
	_close_panel()
	await get_tree().create_timer(0.3).timeout
	var settings_index := _item_index("settings")
	var settings_point := hit_rects[settings_index].get_center()
	_qa_mouse_button(settings_point, false)
	results.append({"check": "release_without_press_does_not_activate", "passed": panel_mode.is_empty()})
	_qa_mouse_button(settings_point, true)
	_qa_mouse_button(settings_point, false)
	results.append({"check": "matching_press_release_activates", "passed": panel_mode == "settings"})
	_close_panel()
	await get_tree().create_timer(0.3).timeout
	settings_point = hit_rects[_item_index("settings")].get_center()
	_qa_mouse_button(settings_point, true)
	var move := InputEventMouseMotion.new()
	move.position = settings_point + Vector2(20, 0)
	_gui_input(move)
	move.position = settings_point
	_gui_input(move)
	_qa_mouse_button(settings_point, false)
	results.append({"check": "drag_return_does_not_click", "passed": panel_mode.is_empty()})
	var original_size: float = prefs.icon_size
	var original_magnification: float = prefs.magnification
	prefs.magnification = 1.65
	for test_size in [32.0, 64.0]:
		prefs.icon_size = test_size
		qa_pointer = Vector2(5, 5)
		await get_tree().create_timer(0.5).timeout
		qa_pointer = Vector2(_rest_centers()[1], hit_rects[1].get_center().y)
		await get_tree().create_timer(0.6).timeout
		var peak := float(scales[items[1].key])
		var neighbor := maxf(float(scales[items[0].key]), float(scales[items[2].key]))
		results.append({"check": "focused_magnification_%d" % test_size, "passed": peak > 1.6 and neighbor < 1.17 and float(scales[items[3].key]) < 1.01, "peak": peak, "neighbor": neighbor})
	prefs.magnification = 1.0
	await get_tree().create_timer(0.5).timeout
	results.append({"check": "magnification_off_returns_to_normal", "passed": float(scales[items[1].key]) < 1.01})
	prefs.icon_size = original_size
	prefs.magnification = original_magnification
	prefs.screen = original_screen
	var report := FileAccess.open("res://tests/ui-results.json", FileAccess.WRITE)
	if report:
		report.store_string(JSON.stringify(results, "\t"))
		report.close()
	for result in results:
		if not result.passed:
			push_error("QA_FAILED: " + str(result.check))
	print("QA_CAPTURE_OK ", ipc)
	await _quit()

func _qa_mouse_button(point: Vector2, down: bool) -> void:
	var button := InputEventMouseButton.new()
	button.button_index = MOUSE_BUTTON_LEFT
	button.position = point
	button.pressed = down
	_gui_input(button)

func _qa_keys_capture() -> void:
	qa_input = true
	qa_pointer = Vector2(5, 5)
	_ensure_key_window()
	await get_tree().create_timer(0.3).timeout
	var checks: Array = []
	# Exercise the same in-memory transport/decoder as real typing, without a hook.
	var sender := PacketPeerUDP.new()
	sender.set_dest_address("127.0.0.1", key_socket.get_local_port())
	key_enabled = true
	sender.put_packet((key_token + ":65").to_ascii_buffer())
	await get_tree().create_timer(0.1).timeout
	checks.append({"check": "native_packet_decoder_creates_pulse", "passed": key_canvas.particles.size() == 1})
	key_enabled = false
	sender.close()
	key_canvas.clear()
	for display in range(DisplayServer.get_screen_count()):
		prefs.screen = display
		_position_window()
		await get_tree().create_timer(0.1).timeout
		checks.append({"check": "key_overlay_screen_%d" % display, "passed": _screen_area(display).encloses(Rect2i(key_window.position, key_window.size))})
	prefs.screen = DisplayServer.get_primary_screen()
	_position_window()
	await get_tree().create_timer(0.2).timeout
	for code in "LUMADOCK123".to_ascii_buffer():
		key_canvas.add_key(code)
		await get_tree().create_timer(0.045).timeout
	checks.append({"check": "typing_creates_multiple_pulses", "passed": key_canvas.particles.size() >= 8})
	await RenderingServer.frame_post_draw
	key_window.get_texture().get_image().save_png("res://qa-keys.png")
	checks.append({"check": "overlay_does_not_accept_focus_or_mouse", "passed": key_window.unfocusable and key_window.mouse_passthrough and not key_window.has_focus()})
	await get_tree().create_timer(1.2).timeout
	checks.append({"check": "pulses_expire", "passed": key_canvas.particles.is_empty()})
	# A held key must never produce overlapping copies or restart an older pulse.
	for i in range(10):
		key_canvas.add_key(65)
		await get_tree().create_timer(0.065).timeout
	var separated := true
	for i in range(key_canvas.particles.size()):
		for j in range(i + 1, key_canvas.particles.size()):
			separated = separated and not key_canvas.pulse_rect(key_canvas.particles[i]).intersects(key_canvas.pulse_rect(key_canvas.particles[j]))
	checks.append({"check": "held_key_pulses_do_not_overlap", "passed": separated and key_canvas.particles.size() == 10})
	await RenderingServer.frame_post_draw
	key_window.get_texture().get_image().save_png("res://qa-keys-repeat.png")
	var probe: Dictionary = key_canvas.particles[0].duplicate()
	var before: Rect2 = key_canvas.pulse_rect(probe)
	key_canvas.exclusion = Rect2(0, 0, key_canvas.size.x, 200)
	checks.append({"check": "dock_movement_does_not_shift_existing_pulses", "passed": key_canvas.pulse_rect(probe) == before})
	var previous_y := float(probe.base)
	var monotonic := true
	var previous_speed := INF
	var decelerates := true
	for step in range(106):
		probe.age = step * 0.01
		var y: float = key_canvas.pulse_rect(probe).position.y
		var speed := previous_y - y
		monotonic = monotonic and speed >= -0.001
		if step > 1:
			decelerates = decelerates and speed <= previous_speed + 0.001
		previous_speed = speed
		previous_y = y
	checks.append({"check": "rise_has_no_reversal_or_velocity_jump", "passed": monotonic and decelerates and previous_speed < 0.001})
	var glyph_image := key_canvas.atlas.get_texture().get_image()
	checks.append({"check": "cached_keycaps_render_with_clear_padding", "passed": glyph_image.get_pixel(0, 0).a == 0 and glyph_image.get_pixel(48, 16).a > 0.8})
	key_canvas.clear()
	for i in range(100):
		key_canvas.add_key(65 + i % 26)
	checks.append({"check": "burst_is_bounded", "passed": key_canvas.particles.size() == key_canvas.lane_count()})
	var oldest: Dictionary = key_canvas.particles[0].duplicate()
	key_canvas.add_key(90)
	checks.append({"check": "saturated_burst_keeps_visible_pulses", "passed": key_canvas.particles[0] == oldest})
	prefs.key_visualizer = false
	_sync_key_visualizer()
	checks.append({"check": "toggle_clears_and_hides_overlay", "passed": key_canvas.particles.is_empty() and not key_window.visible})
	var output := FileAccess.open("res://tests/key-ui-results.json", FileAccess.WRITE)
	output.store_string(JSON.stringify(checks, "\t"))
	output.close()
	for check in checks:
		if not check.passed:
			push_error("QA_FAILED: " + str(check.check))
	print("QA_KEYS_OK")
	await _quit()

func _qa_reference_capture() -> void:
	qa_input = true
	qa_pointer = Vector2(5, 5)
	prefs.icon_size = 56.0
	prefs.magnification = 1.0
	prefs.screen = DisplayServer.get_primary_screen()
	var names := ["finder", "launchpad", "safari", "messages", "mail", "maps", "photos", "facetime", "calendar", "contacts", "reminders", "notes", "freeform", "tv", "music", "news", "store", "folder", "trash"]
	for name_value in names:
		var path := "res://assets/figma/%s.png" % name_value if name_value in ["finder", "notes", "folder", "trash"] else "res://tests/reference/%s.png" % name_value
		var image_value := Image.load_from_file(ProjectSettings.globalize_path(path))
		if not image_value:
			push_error("Missing reference asset: " + path)
			await _quit()
			return
		image_value.fix_alpha_edges()
		image_value.generate_mipmaps()
		var kind: String = "downloads" if name_value == "folder" else "reference_" + name_value
		system_textures[kind] = ImageTexture.create_from_image(image_value)
		reference_items.append({"key": name_value, "name": name_value, "kind": kind, "windows": [], "path": "", "pinned": false})
	_rebuild()
	scales.clear()
	_position_window()
	await get_tree().create_timer(0.5).timeout
	var bounds: Dictionary = state.dock_rect
	var backdrop := OS.create_process(ProjectSettings.globalize_path("res://tests/bin/MaterialBackdrop.exe"), [str(OS.get_process_id()), str(DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE)), ProjectSettings.globalize_path("res://tests/reference/wallpaper.png"), str(int(bounds.x)), str(int(bounds.y)), str(int(bounds.w)), str(int(bounds.h))])
	await get_tree().create_timer(1.2).timeout
	_send({"op": "snapshot", "name": "figma-material", "crop_x": roundi(dock_rect.position.x) - 25, "crop_y": roundi(dock_rect.position.y) - 20, "crop_w": roundi(dock_rect.size.x) + 50, "crop_h": roundi(dock_rect.size.y) + 25})
	await get_tree().create_timer(0.4).timeout
	DirAccess.copy_absolute(ipc.path_join("figma-material.png"), ProjectSettings.globalize_path("res://qa-figma-material.png"))
	if backdrop > 0:
		OS.kill(backdrop)
	print("REFERENCE_CAPTURE_OK ", ipc)
	await _quit()

func _check_native_center(display: int, mode: String) -> Dictionary:
	var origin := Vector2i.ZERO
	for monitor in state.get("screens", []):
		origin.x = mini(origin.x, int(monitor.x))
		origin.y = mini(origin.y, int(monitor.y))
	var available := _screen_area(display)
	available.position += origin
	var actual: Dictionary = state.get("dock_rect", {})
	var result := {"check": "native_center_screen_%d_%s" % [display, mode], "passed": false, "native_window": actual, "expected_area": str(available)}
	if not actual.is_empty():
		var bounds := Rect2i(int(actual.x), int(actual.y), int(actual.w), int(actual.h))
		result.passed = available.encloses(bounds) and absf(bounds.get_center().x - available.get_center().x) <= 1
	return result

func _check_shelf_bounds(display: int, mode: String) -> Dictionary:
	var available := Rect2(_screen_area(display))
	var shelf := Rect2(Vector2(get_window().position) + dock_rect.position, dock_rect.size)
	var icons_fit := true
	for hit in hit_rects:
		icons_fit = icons_fit and hit.position.x >= 0 and hit.end.x <= size.x and hit.position.y >= 0
	return {"check": "shelf_screen_%d_%s" % [display, mode], "passed": available.encloses(shelf) and absf(shelf.get_center().x - available.get_center().x) <= 1 and icons_fit, "shelf": str(shelf)}
