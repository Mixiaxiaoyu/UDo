extends OptionButton

const SettingsDesign = preload("res://scripts/settings_design.gd")
var check_normal: ImageTexture
var check_active: ImageTexture
var arrow_open := 0.0
var arrow_tween: Tween

func _ready() -> void:
	# Figma Combo Box 121:13028: white field, 6px corners, 8% border,
	# and a 24px neutral trailing button inset by 2px.
	custom_minimum_size = Vector2(170, 24)
	size_flags_vertical = Control.SIZE_SHRINK_CENTER
	add_theme_font_size_override("font_size", 13)
	for key in ["font_color", "font_hover_color", "font_pressed_color"]:
		add_theme_color_override(key, SettingsDesign.TEXT)
	var field := SettingsDesign.outline(Color.WHITE, 6, Color(0, 0, 0, 0.08))
	field.content_margin_left = 8
	field.content_margin_right = 4
	field.content_margin_top = 0
	field.content_margin_bottom = 0
	for key in ["normal", "hover", "pressed"]:
		add_theme_stylebox_override(key, field)
	add_theme_stylebox_override("focus", SettingsDesign.focus())
	var arrow := GradientTexture2D.new()
	arrow.width = 24
	arrow.height = 20
	arrow.gradient = Gradient.new()
	arrow.gradient.colors = PackedColorArray([Color.TRANSPARENT, Color.TRANSPARENT])
	add_theme_icon_override("arrow", arrow)
	add_theme_constant_override("arrow_margin", 2)
	get_popup().add_theme_font_size_override("font_size", 13)
	# Menu 483:8811: 13px corners, 12/5px padding, 24px rows, 8px selection.
	var menu := get_popup()
	# Embedded popup shares the transparent Godot canvas: no second Windows
	# border/shadow or opaque square corners behind the 13px menu silhouette.
	get_window().gui_embed_subwindows = true
	menu.force_native = false
	menu.transparent = true
	menu.transparent_bg = true
	# Decode the popup viewport's premultiplied RGB before Godot embeds it.
	# Otherwise even an opaque white outline acquires a dark outer AA fringe.
	var alpha_layer := CanvasLayer.new()
	alpha_layer.layer = 100
	menu.add_child(alpha_layer)
	var alpha_pass := ColorRect.new()
	alpha_pass.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var alpha_material := ShaderMaterial.new()
	alpha_material.shader = preload("res://scripts/popup_alpha.gdshader")
	alpha_pass.material = alpha_material
	alpha_layer.add_child(alpha_pass)
	alpha_pass.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	menu.add_theme_stylebox_override("embedded_border", StyleBoxEmpty.new())
	menu.add_theme_stylebox_override("embedded_unfocused_border", StyleBoxEmpty.new())
	var background := SettingsDesign.outline(Color("f5f5f5"), 13, Color(1, 1, 1, 0.8))
	background.corner_detail = 16
	background.content_margin_left = 12
	background.content_margin_right = 12
	background.content_margin_top = 5
	background.content_margin_bottom = 5
	menu.add_theme_stylebox_override("panel", background)
	menu.add_theme_stylebox_override("hover", SettingsDesign.rounded(SettingsDesign.TINT, 8))
	menu.add_theme_color_override("font_color", SettingsDesign.TEXT)
	menu.add_theme_color_override("font_hover_color", Color.WHITE)
	menu.add_theme_constant_override("v_separation", 6)
	# Windows symbol adaptation: a small check, with empty space for other rows.
	var check_image := Image.new()
	check_image.load_svg_from_string('<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 14 14"><path d="M3 7l2.5 2.5L11 4" fill="none" stroke="#252525" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>', 2.0)
	var check := ImageTexture.create_from_image(check_image)
	# Draw the oversampled symbol at native logical dimensions.
	check.set_size_override(Vector2i(14, 14))
	check_normal = check
	var white := check_image.duplicate()
	for y in range(white.get_height()):
		for x in range(white.get_width()):
			white.set_pixel(x, y, Color(1, 1, 1, white.get_pixel(x, y).a))
	check_active = ImageTexture.create_from_image(white)
	check_active.set_size_override(Vector2i(14, 14))
	var empty_image := Image.create(14, 14, false, Image.FORMAT_RGBA8)
	empty_image.fill(Color.TRANSPARENT)
	menu.add_theme_icon_override("radio_checked", check)
	menu.add_theme_icon_override("radio_unchecked", ImageTexture.create_from_image(empty_image))
	menu.id_focused.connect(func(_id: int): _update_check())
	menu.about_to_popup.connect(func(): _update_check.call_deferred(); _animate_arrow(true))
	menu.popup_hide.connect(func(): _animate_arrow(false))

func _animate_arrow(opened: bool) -> void:
	if arrow_tween and arrow_tween.is_running(): arrow_tween.kill()
	if bool(get_meta("reduce_motion", false)):
		arrow_open = 1.0 if opened else 0.0
		queue_redraw()
		return
	arrow_tween = create_tween().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	arrow_tween.tween_property(self, "arrow_open", 1.0 if opened else 0.0, 0.18)

func _process(_delta: float) -> void:
	if arrow_tween and arrow_tween.is_running(): queue_redraw()

func _update_check() -> void:
	get_popup().add_theme_icon_override("radio_checked", check_active if get_popup().get_focused_item() == selected else check_normal)

func _draw() -> void:
	var bounds := Rect2(size.x - 26, 2, 24, size.y - 4)
	draw_style_box(SettingsDesign.rounded(SettingsDesign.CONTROL, 5), bounds)
	var center := bounds.get_center()
	var vertical := lerpf(1.0, -1.0, arrow_open)
	draw_polyline(PackedVector2Array([center + Vector2(-3, -1.5 * vertical), center + Vector2(0, 1.5 * vertical), center + Vector2(3, -1.5 * vertical)]), SettingsDesign.TEXT, 1.4, true)
