shader_type canvas_item;
render_mode unshaded;

uniform vec2 shelf_size = vec2(800.0, 82.0);
uniform float radius = 20.0;
uniform float margin = 24.0;
uniform float fill_opacity = 0.36;
uniform float layout_scale = 1.0;
uniform bool native_material = false;
uniform bool glass_material = false;
uniform vec4 fill_color : source_color = vec4(0.965, 0.965, 0.965, 1.0);
uniform vec4 edge_color : source_color = vec4(0.102, 0.102, 0.102, 0.46);
uniform float shadow_opacity = 0.15;
uniform float shadow_sigma = 3.0;
uniform float shadow_spread = 0.0;
uniform float inset_strength = 0.0;

float rounded_box(vec2 p, vec2 half_size, float r) {
	vec2 q = abs(p) - half_size + vec2(r);
	return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}

vec2 cubic(vec2 a, vec2 b, vec2 c, vec2 d, float t) {
	float u = 1.0 - t;
	return u*u*u*a + 3.0*u*u*t*b + 3.0*u*t*t*c + t*t*t*d;
}

void edge_distance(vec2 q, vec2 a, vec2 b, inout vec2 result) {
	vec2 v = b-a;
	vec2 delta = q - (a + v * clamp(dot(q-a, v) / max(dot(v,v), 0.00001), 0.0, 1.0));
	float d2 = dot(delta,delta);
	if (d2 < result.x) result = vec2(d2, v.x*(q.y-a.y)-v.y*(q.x-a.x));
}

float figma_distance(vec2 point, vec2 half_size, float r) {
	float p = 1.6*r;
	vec2 folded = abs(point);
	if (folded.x < half_size.x-p || folded.y < half_size.y-p) return rounded_box(point,half_size,r);
	vec2 q = vec2(folded.x-half_size.x, half_size.y-folded.y);
	float theta = 0.6*3.14159265359/4.0;
	float d = r*(1.0-cos(theta)), c = d/tan(theta);
	float arc = r*sqrt(2.0)*sin(0.4*3.14159265359/4.0);
	float b = (p-arc-c-d)/3.0, a = 2.0*b;
	vec2 start = vec2(-p,0), join1 = vec2(-p+a+b+c,d), join2 = vec2(-d,p-a-b-c);
	vec2 best = vec2(1e10,0), previous = start;
	for (int i=1;i<=12;i++) {
		float t = float(i)/12.0;
		vec2 next = cubic(start,vec2(-p+a,0),vec2(-p+a+b,0),join1,t);
		edge_distance(q,previous,next,best); previous=next;
	}
	for (int i=1;i<=12;i++) {
		float angle = mix(-3.14159265359/2.0+theta,-theta,float(i)/12.0);
		vec2 next = vec2(-r,r)+r*vec2(cos(angle),sin(angle));
		edge_distance(q,previous,next,best); previous=next;
	}
	for (int i=1;i<=12;i++) {
		vec2 next = cubic(join2,vec2(0,p-a-b),vec2(0,p-a),vec2(0,p),float(i)/12.0);
		edge_distance(q,previous,next,best); previous=next;
	}
	return sqrt(best.x) * (best.y >= 0.0 ? -1.0 : 1.0);
}

void fragment() {
	vec2 canvas = shelf_size + vec2(margin * 2.0);
	vec2 p = UV * canvas - canvas * 0.5;
	float d = figma_distance(p, shelf_size * 0.5, radius);
	float aa = max(fwidth(d), 0.85);
	float coverage = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, d);
	// Gaussian convolution of a solid edge (normal CDF), not a glow peak.
	// Figma light: radius 6 => sigma 3; dark: zero blur, spread 1.
	float z = (d - shadow_spread) / max(shadow_sigma, 0.0001);
	float t = 1.0 / (1.0 + 0.2316419 * abs(z));
	float tail = 0.39894228 * exp(-0.5 * z * z) * t * (0.31938153 + t * (-0.35656378 + t * (1.78147794 + t * (-1.82125598 + t * 1.33027443))));
	float shadow = (z >= 0.0 ? tail : 1.0 - tail) * shadow_opacity;
	if (shadow_sigma < 0.01) shadow = (1.0 - smoothstep(shadow_spread - aa * 0.5, shadow_spread + aa * 0.5, d)) * shadow_opacity;
	shadow *= 1.0 - coverage;
	float body_alpha = fill_opacity * coverage;
	vec3 body_rgb = fill_color.rgb;
	// One inner edge, sampled from the same rounded distance as the fill.
	float rim = smoothstep(-layout_scale - aa * 0.5, -layout_scale + aa * 0.5, d) * coverage;
	float rim_alpha = rim * edge_color.a;
	float a = body_alpha;
	vec3 premul = min(body_rgb + edge_color.rgb * rim_alpha, vec3(1.0)) * body_alpha;
	float out_a = a + shadow * (1.0 - a);
	if (glass_material) {
		// Figma 6307:2867 uses LINEAR_DODGE inner shadows on the top and bottom.
		// A bright, clipped rim approximates those additive highlights over the backdrop.
		float inside = max(-d, 0.0);
		float vertical = smoothstep(0.15, 0.8, abs(p.y) / max(shelf_size.y * 0.5, 0.01));
		float fine = exp(-pow(inside / max(1.5 * layout_scale, 0.01), 2.0));
		float broad = exp(-inside / max(8.0 * layout_scale, 0.01));
		float highlight = coverage * vertical * inset_strength * (0.38 * fine + 0.12 * broad);
		float glass_alpha = native_material ? highlight : body_alpha + highlight * (1.0 - body_alpha);
		COLOR = vec4(vec3(1.0), glass_alpha);
	} else {
		// Preserve the existing light and dark material paths exactly.
		COLOR = native_material ? vec4(0.0, 0.0, 0.0, shadow) : vec4(premul / max(out_a, 0.0001), out_a);
	}
}
