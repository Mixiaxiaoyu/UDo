extends RefCounted

# macOS 26 Community: Form 2548:2849 / Content 2302:6385 / Group 2302:6716.
# The composition contains only the Dock's actual settings, using these tokens.
const TEXT := Color(0, 0, 0, 0.85)
const SECONDARY := Color(0, 0, 0, 0.5)
const TINT := Color("0d6fff")
const GROUP := Color(0, 0, 0, 0.03)
const SEPARATOR := Color(0, 0, 0, 0.05)
const CONTROL := Color(0, 0, 0, 0.05)
const FONT_SIZE := 13
const ROW_HEIGHT := 42
const CONTENT_INSET := 20
const GROUP_INSET := 10
const GROUP_RADIUS := 12
const CONTROL_HEIGHT := 24
const CONTROL_RADIUS := 6

static func rounded(color: Color, radius: int) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = color
	style.set_corner_radius_all(radius)
	style.corner_detail = 16
	style.shadow_size = 0
	return style

static func outline(fill: Color, radius: int, ink: Color, surface := Color.WHITE) -> StyleBoxFlat:
	# Resolve translucent design tokens against the actual surface before drawing.
	# StyleBoxFlat's inner AA interpolates border/background RGBA; a translucent
	# black border against opaque white produces dark wedges at tight corners.
	var style := rounded(surface.blend(fill), radius)
	style.border_color = style.bg_color.blend(ink)
	style.set_border_width_all(1)
	return style

static func panel() -> StyleBoxFlat:
	# Window 585:3372: white, radius 26. User requested a lighter shadow.
	var style := rounded(Color.WHITE, 26)
	style.set_content_margin_all(CONTENT_INSET)
	style.shadow_color = Color(0, 0, 0, 0.08)
	style.shadow_size = 6
	style.shadow_offset = Vector2(0, 2)
	return style

static func button() -> StyleBoxFlat:
	# Push Button 121:11924, neutral variant: 24 high, radius 6, inset 16.
	var style := rounded(CONTROL, CONTROL_RADIUS)
	style.content_margin_left = 16
	style.content_margin_right = 16
	return style

static func focus() -> StyleBoxFlat:
	var style := rounded(Color.TRANSPARENT, CONTROL_RADIUS)
	style.border_color = Color(TINT, 0.5)
	style.set_border_width_all(2)
	return style
