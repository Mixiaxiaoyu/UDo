extends Control

const GlyphAtlas = preload("res://scripts/key_glyph_atlas.gd")
const LIFETIME := 1.05
const MAX_PARTICLES := 36
var particles: Array[Dictionary] = []
var exclusion := Rect2()
var last_hit := -1.0
var clock := 0.0
var atlas: SubViewport

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR
	# Cache complete keycaps at 2x so fractional motion keeps text and edges in phase.
	atlas = SubViewport.new()
	atlas.size = Vector2i(96 * 36, 64)
	atlas.transparent_bg = true
	atlas.disable_3d = true
	atlas.render_target_update_mode = SubViewport.UPDATE_ONCE
	add_child(atlas)
	atlas.add_child(GlyphAtlas.new())
	var blend := CanvasItemMaterial.new()
	blend.blend_mode = CanvasItemMaterial.BLEND_MODE_PREMULT_ALPHA
	material = blend

func clear() -> void:
	particles.clear()
	last_hit = -1.0
	queue_redraw()

func lane_count() -> int:
	return clampi(floori((size.x - 60.0) / 52.0), 1, MAX_PARTICLES)

func add_key(code: int) -> void:
	if not (code >= 65 and code <= 90 or code >= 48 and code <= 57):
		return
	var count := lane_count()
	# Reserve columns until expiry. Auto-repeat cannot stack translucent copies.
	# At saturation, skip the new pulse instead of abruptly removing a visible one.
	var occupied := {}
	for particle in particles:
		occupied[int(particle.lane)] = true
	var index := code - 65 if code >= 65 else code - 48 + 26
	var preferred := (index * 17) % count
	var lane := -1
	for offset in range(count):
		var candidate := (preferred + offset) % count
		if not occupied.has(candidate):
			lane = candidate
			break
	if lane < 0:
		return
	var x := 30.0 + (size.x - 60.0) * (float(lane) + 0.5) / count
	var baseline := size.y - 8.0
	if exclusion.has_area() and x >= exclusion.position.x - 24 and x <= exclusion.end.x + 24:
		baseline = minf(baseline, exclusion.position.y - 8)
	var intensity := clampf(1.0 - (clock - last_hit) / 0.35, 0, 1) if last_hit >= 0 else 0.0
	last_hit = clock
	particles.append({"glyph": index, "lane": lane, "x": x, "base": baseline, "age": 0.0, "rise": 40.0 + 24.0 * intensity})
	queue_redraw()

func _process(delta: float) -> void:
	clock += delta
	if particles.is_empty():
		return
	for particle in particles:
		particle.age += delta
	particles = particles.filter(func(p: Dictionary): return float(p.age) < LIFETIME)
	queue_redraw()

func pulse_rect(particle: Dictionary) -> Rect2:
	var t := clampf(float(particle.age) / LIFETIME, 0, 1)
	# Continuous deceleration to rest, with no abrupt change of direction.
	var lift := (1.0 - pow(1.0 - t, 4.0)) * float(particle.rise)
	return Rect2(float(particle.x) - 22, float(particle.base) - 24 - lift, 44, 24)

func _draw() -> void:
	if not is_instance_valid(atlas):
		return
	for particle in particles:
		var t := float(particle.age) / LIFETIME
		var alpha := smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.45, 1.0, t))
		var rect := pulse_rect(particle).grow_individual(2, 4, 2, 4)
		# Viewport textures are premultiplied: fade RGB and alpha together.
		draw_texture_rect_region(atlas.get_texture(), rect, Rect2(int(particle.glyph) * 96, 0, 96, 64), Color(alpha, alpha, alpha, alpha))
