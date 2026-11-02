extends RefCounted

# macOS 26 Community, RPsoArokQLQKcIWGhp5i7H, Dock I682:3935;495:4607.
# Dimensions are the reference's 36px icon grid, scaled together at runtime.
const ICON_SIZE := 36.0
const GAP := 9.0
const PADDING_X := 8.0
const RADIUS := 15.0
const BOTTOM_GAP := 5.0
const BORDER := Color(0.102, 0.102, 0.102, 0.46)
const SEPARATOR := Color(0.235, 0.235, 0.263, 0.29)
const BLUE := Color("0088ff")
const LIGHT := {"fill": Color("f6f6f6"), "opacity": 0.36, "edge": Color(0.102, 0.102, 0.102, 0.46), "shadow": 0.15, "shadow_sigma": 3.0, "shadow_spread": 0.0, "inset": 0.0, "separator": SEPARATOR, "indicator": Color(0.1, 0.1, 0.12, 0.48), "active": Color(0.08, 0.08, 0.09, 0.8), "tip": Color(0.96, 0.96, 0.97, 0.96), "text": Color("252527")}
const DARK := {"fill": Color("4a4a4a"), "opacity": 0.39, "edge": Color("1a1a1a"), "shadow": 0.0, "shadow_sigma": 0.0, "shadow_spread": 0.0, "inset": 0.0, "separator": Color(1, 1, 1, 0.25), "indicator": Color(1, 1, 1, 0.6), "active": Color(1, 1, 1, 0.9), "tip": Color(0.16, 0.16, 0.17, 0.97), "text": Color(1, 1, 1, 0.9)}
const GLASS := {"fill": Color.WHITE, "opacity": 0.0, "edge": Color.TRANSPARENT, "shadow": 0.0, "shadow_sigma": 0.0, "shadow_spread": 0.0, "inset": 1.0, "separator": SEPARATOR, "indicator": Color(0.1, 0.1, 0.12, 0.48), "active": Color(0.08, 0.08, 0.09, 0.8), "tip": Color(0.96, 0.96, 0.97, 0.96), "text": Color("252527")}
const DARK_ICONS := {}
const ICONS := {
	"settings": "res://assets/udo.png",
}
