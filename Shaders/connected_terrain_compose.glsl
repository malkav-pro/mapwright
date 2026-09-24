#[compute]
#version 450

// Production connected-terrain evaluator. One invocation owns one output pixel and
// evaluates the complete ConnectedTerrainGraph.RenderReferenceCore composition in
// IEEE double precision, in the same operation order as the CPU oracle:
// Background (legacy then resolved texture strokes, layer opacity), then Foreground
// (legacy then resolved texture strokes, base/legacy coverage, ordered Land strokes,
// river subtraction, coverage x opacity), then straight-alpha sRGB encoding.
// `precise` forbids FMA contraction so products and sums round as on the CPU.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct Stroke {
    ivec4 info;    // kind (0 legacy texture, 1 resolved texture, 2 land), layer (0 bg, 1 fg), sample start, sample count
    ivec4 extra;   // land: shape (0 edged, 1 round), operation (0 add, 1 subtract), seed; resolved: hash[3], hash[4]
    dvec4 bounds;  // conservative map-space left, top, right, bottom
    dvec4 p0;      // texture: radius, hardness, opacity, flow; land: radius, roughness, corner smoothing, softness
    dvec4 p1;      // legacy: colour r, g, b; resolved: base r, g, b, texture scale; land: apothem
    dvec4 p2;      // resolved: anchor x, anchor y, cos(rotation), sin(rotation)
};

struct RiverSegment {
    dvec4 ends;    // start x, start y, end x, end y
    dvec4 widths;  // start width, end width, bank softness, unused
};

layout(set = 0, binding = 0, std430) readonly buffer Params {
    dvec4 map;         // map width, map height, canvas width, canvas height
    dvec4 opacity;     // background opacity, foreground opacity, tau, polygon sector
    ivec4 flags;       // draw background, draw foreground, has background, has foreground
    ivec4 flags2;      // has coverage, legacy foreground compatibility, stroke count, river segment count
    ivec4 region;      // output region left, top, width, height within the canvas sampling
} params;
layout(set = 0, binding = 1, std430) readonly buffer Tables {
    double decode[256];
    double thresholds[255];
} tables;
layout(set = 0, binding = 2, std430) readonly buffer Background { uint pixels[]; } background;
layout(set = 0, binding = 3, std430) readonly buffer Foreground { uint pixels[]; } foreground;
layout(set = 0, binding = 4, std430) readonly buffer Coverage { float values[]; } coverage_input;
layout(set = 0, binding = 5, std430) readonly buffer Strokes { Stroke items[]; } strokes;
layout(set = 0, binding = 6, std430) readonly buffer Samples { dvec4 items[]; } samples;
layout(set = 0, binding = 7, std430) readonly buffer River { RiverSegment items[]; } river;
layout(rgba8, set = 0, binding = 8) uniform writeonly image2D output_image;

const double PI_D = 3.141592653589793LF;
const double HALF_PI_HI = 1.5707963267948966LF;
const double HALF_PI_LO = 6.123233995736766e-17LF;

// ---- double transcendental functions (~1e-16 relative; the CPU uses the CRT) ----

double sin_poly(double x) {
    precise double x2 = x * x;
    precise double r = -7.647163731819816e-13LF;
    r = r * x2 + 1.6059043836821613e-10LF;
    r = r * x2 - 2.505210838544172e-08LF;
    r = r * x2 + 2.7557319223985893e-06LF;
    r = r * x2 - 1.984126984126984e-04LF;
    r = r * x2 + 8.333333333333333e-03LF;
    r = r * x2 - 1.6666666666666666e-01LF;
    precise double result = x + x * x2 * r;
    return result;
}

double cos_poly(double x) {
    precise double x2 = x * x;
    precise double r = 4.779477332387385e-14LF;
    r = r * x2 - 1.1470745597729725e-11LF;
    r = r * x2 + 2.08767569878681e-09LF;
    r = r * x2 - 2.755731922398589e-07LF;
    r = r * x2 + 2.48015873015873e-05LF;
    r = r * x2 - 1.388888888888889e-03LF;
    r = r * x2 + 4.1666666666666664e-02LF;
    r = r * x2 - 0.5LF;
    precise double result = 1.0LF + x2 * r;
    return result;
}

// Returns quadrant and reduced argument in [-pi/4, pi/4] (Cody-Waite, two-part pi/2).
double reduce_half_pi(double x, out int quadrant) {
    double k = floor(x / HALF_PI_HI + 0.5LF);
    quadrant = int(mod(k, 4.0LF));
    precise double r = fma(-k, HALF_PI_HI, x);
    r = fma(-k, HALF_PI_LO, r);
    return r;
}

double sin_d(double x) {
    int q;
    double r = reduce_half_pi(x, q);
    if (q == 0) return sin_poly(r);
    if (q == 1) return cos_poly(r);
    if (q == 2) return -sin_poly(r);
    return -cos_poly(r);
}

double cos_d(double x) {
    int q;
    double r = reduce_half_pi(x, q);
    if (q == 0) return cos_poly(r);
    if (q == 1) return -sin_poly(r);
    if (q == 2) return -cos_poly(r);
    return sin_poly(r);
}

// atan on [0, inf): reduce to [0, 1], halve twice, then an odd series.
double atan_d(double x) {
    bool invert = x > 1.0LF;
    precise double z = invert ? 1.0LF / x : x;
    z = z / (1.0LF + sqrt(1.0LF + z * z));
    z = z / (1.0LF + sqrt(1.0LF + z * z));
    precise double z2 = z * z;
    precise double sum = 0.0LF;
    for (int n = 27; n >= 1; n -= 2) sum = 1.0LF / double(n) - z2 * sum;
    precise double result = 4.0LF * z * sum;
    return invert ? (HALF_PI_HI - result) + HALF_PI_LO : result;
}

double atan2_d(double y, double x) {
    if (x == 0.0LF) {
        if (y > 0.0LF) return HALF_PI_HI;
        if (y < 0.0LF) return -HALF_PI_HI;
        return 0.0LF;
    }
    double a = atan_d(abs(y / x));
    if (x < 0.0LF) a = PI_D - a;
    return y < 0.0LF ? -a : a;
}

// C# `%` on doubles: truncated remainder, sign of the dividend.
double cs_mod(double a, double b) {
    double q = trunc(a / b);
    precise double r = fma(-b, q, a);
    if (a >= 0.0LF) { if (r < 0.0LF) r += b; else if (r >= b) r -= b; }
    else { if (r > 0.0LF) r -= b; else if (r <= -b) r += b; }
    return r;
}

// ---- colour, matching LinearPixel ----

dvec4 from_straight(uint packed, double alpha) {
    double a = clamp(alpha, 0.0LF, 1.0LF);
    precise dvec4 result = dvec4(
        tables.decode[packed & 255u] * a,
        tables.decode[(packed >> 8) & 255u] * a,
        tables.decode[(packed >> 16) & 255u] * a,
        a);
    return result;
}

dvec4 read_input(uint packed) {
    precise double alpha = double((packed >> 24) & 255u) / 255.0LF;
    return from_straight(packed, alpha);
}

dvec4 with_opacity(dvec4 pixel, double opacity) {
    double factor = clamp(opacity, 0.0LF, 1.0LF);
    precise dvec4 result = pixel * factor;
    return result;
}

dvec4 over(dvec4 source, dvec4 destination) {
    precise double retained = 1.0LF - source.a;
    precise dvec4 result = source + destination * retained;
    return result;
}

uint encode(double value) {
    double linear = clamp(value, 0.0LF, 1.0LF);
    int lower = 0;
    int upper = 255;
    while (lower < upper) {
        int middle = (lower + upper) >> 1;
        double threshold = tables.thresholds[middle];
        if (linear < threshold || (linear == threshold && (middle & 1) == 0)) upper = middle;
        else lower = middle + 1;
    }
    return uint(lower);
}

vec4 write_straight(dvec4 pixel) {
    if (pixel.a <= 0.0LF) return vec4(0.0);
    precise double r = pixel.r / pixel.a;
    precise double g = pixel.g / pixel.a;
    precise double b = pixel.b / pixel.a;
    precise double scaled = pixel.a * 255.0LF;
    uint alpha = uint(clamp(int(roundEven(scaled)), 0, 255));
    return vec4(float(encode(r)), float(encode(g)), float(encode(b)), float(alpha)) / 255.0;
}

// ---- coverage ----

double falloff(double distance, double hardness) {
    if (distance > 1.0LF) return 0.0LF;
    if (distance <= hardness || hardness >= 0.999LF) return 1.0LF;
    precise double value = 1.0LF - ((distance - hardness) / (1.0LF - hardness));
    return clamp(value, 0.0LF, 1.0LF);
}

double texture_coverage(Stroke stroke, dvec2 point) {
    precise double coverage = 0.0LF;
    int start = stroke.info.z;
    int end = start + stroke.info.w;
    for (int index = start; index < end; index++) {
        dvec4 sample_item = samples.items[index];
        precise double radius = stroke.info.x == 1 ? stroke.p0.x * sample_item.z : stroke.p0.x;
        precise double dx = point.x - sample_item.x;
        precise double dy = point.y - sample_item.y;
        if (abs(dx) > radius || abs(dy) > radius) continue;
        precise double squared = dx * dx + dy * dy;
        precise double distance = sqrt(squared) / radius;
        if (distance > 1.0LF) continue;
        double dab = falloff(distance, stroke.p0.y);
        coverage += dab * (1.0LF - coverage);
    }
    return coverage;
}

uvec3 legacy_colour(Stroke stroke) { return uvec3(stroke.p1.xyz); }

uvec3 resolved_colour(Stroke stroke, dvec2 point) {
    precise double local_x = point.x - stroke.p2.x;
    precise double local_y = point.y - stroke.p2.y;
    precise double rotated_x = (local_x * stroke.p2.z) + (local_y * stroke.p2.w);
    precise double rotated_y = (-local_x * stroke.p2.w) + (local_y * stroke.p2.z);
    precise double wave_a = (rotated_x / stroke.p1.w) * 0.071LF + double(stroke.extra.x);
    precise double wave_b = (rotated_y / stroke.p1.w) * 0.053LF + double(stroke.extra.y);
    precise double wave = sin_d(wave_a) * cos_d(wave_b);
    precise double scaled = wave * 14.0LF;
    int variation = int(roundEven(scaled));
    return uvec3(
        uint(clamp(int(stroke.p1.x) + variation, 0, 255)),
        uint(clamp(int(stroke.p1.y) + variation, 0, 255)),
        uint(clamp(int(stroke.p1.z) + variation, 0, 255)));
}

dvec4 apply_texture_strokes(dvec4 layer, int role, dvec2 point) {
    int count = params.flags2.z;
    // The oracle applies every legacy stroke before every resolved stroke.
    for (int pass = 0; pass < 2; pass++) {
        for (int index = 0; index < count; index++) {
            Stroke stroke = strokes.items[index];
            if (stroke.info.x != pass || stroke.info.y != role) continue;
            if (point.x < stroke.bounds.x || point.x > stroke.bounds.z ||
                point.y < stroke.bounds.y || point.y > stroke.bounds.w) continue;
            double stroke_coverage = texture_coverage(stroke, point);
            if (stroke_coverage <= 0.0LF) continue;
            uvec3 colour = pass == 0 ? legacy_colour(stroke) : resolved_colour(stroke, point);
            precise double alpha = stroke.p0.z * stroke.p0.w * stroke_coverage;
            layer = over(from_straight(colour.r | (colour.g << 8) | (colour.b << 16), alpha), layer);
        }
    }
    return layer;
}

double noise(int seed, int index) {
    uint state = uint(seed) ^ (uint(index) * 0x9E3779B9u);
    state ^= state >> 16;
    state *= 0x7FEB352Du;
    state ^= state >> 15;
    state *= 0x846CA68Bu;
    state ^= state >> 16;
    return (double(state & 0x00FFFFFFu) / double(0x007FFFFFu)) - 1.0LF;
}

double lerp_d(double from, double to, double amount) {
    precise double result = from + ((to - from) * amount);
    return result;
}

double land_offset_coverage(Stroke stroke, dvec2 offset) {
    precise double squared = (offset.x * offset.x) + (offset.y * offset.y);
    double distance = sqrt(squared);
    double radius = stroke.p0.x;
    if (stroke.extra.x == 0) {
        double tau = params.opacity.z;
        double sector = params.opacity.w;
        double angle = atan2_d(offset.y, offset.x);
        precise double shifted = angle + (sector * 0.5LF);
        precise double local = abs(cs_mod(cs_mod(shifted, sector) + sector, sector) - (sector * 0.5LF));
        precise double polygon_radius = stroke.p1.x / cos_d(local);
        precise double normalized = cs_mod(cs_mod(angle, tau) + tau, tau) / sector;
        double vertex_floor = floor(normalized);
        int vertex = int(vertex_floor);
        int next = (vertex + 1) % 12;
        precise double t = normalized - vertex_floor;
        precise double rough_radius = radius * (1.0LF + (stroke.p0.y * 0.18LF *
            lerp_d(noise(stroke.extra.z, vertex), noise(stroke.extra.z, next), t)));
        double edged = min(polygon_radius, rough_radius);
        return distance <= lerp_d(edged, radius, stroke.p0.z) ? 1.0LF : 0.0LF;
    }
    if (distance > radius) return 0.0LF;
    double softness = stroke.p0.w;
    if (softness == 0.0LF) return 1.0LF;
    precise double inner = radius * (1.0LF - softness);
    if (distance <= inner) return 1.0LF;
    precise double ramp = (radius - distance) / (radius - inner);
    return clamp(ramp, 0.0LF, 1.0LF);
}

// Returns the segment projection point and amount, as MapGeometry.ProjectPointOnSegment.
dvec3 project_on_segment(dvec2 point, dvec2 start, dvec2 end) {
    precise double x = end.x - start.x;
    precise double y = end.y - start.y;
    precise double length_squared = (x * x) + (y * y);
    if (length_squared == 0.0LF) return dvec3(start, 0.0LF);
    precise double dot_value = ((point.x - start.x) * x) + ((point.y - start.y) * y);
    double amount = clamp(dot_value / length_squared, 0.0LF, 1.0LF);
    precise double px = start.x + (x * amount);
    precise double py = start.y + (y * amount);
    return dvec3(px, py, amount);
}

double land_coverage(Stroke stroke, dvec2 point) {
    int start = stroke.info.z;
    int count = stroke.info.w;
    if (count == 1) {
        precise dvec2 offset = point - samples.items[start].xy;
        return land_offset_coverage(stroke, offset);
    }
    double result = 0.0LF;
    for (int index = start + 1; index < start + count; index++) {
        dvec3 centre = project_on_segment(point, samples.items[index - 1].xy, samples.items[index].xy);
        precise dvec2 offset = point - centre.xy;
        result = max(result, land_offset_coverage(stroke, offset));
    }
    return result;
}

double apply_coverage(double current, double footprint, int operation) {
    precise double composed = operation == 0
        ? current + (footprint * (1.0LF - current))
        : current * (1.0LF - footprint);
    return clamp(composed, 0.0LF, 1.0LF);
}

double river_subtraction(dvec2 point) {
    double subtraction = 0.0LF;
    for (int index = 0; index < params.flags2.w; index++) {
        RiverSegment segment = river.items[index];
        dvec3 projection = project_on_segment(point, segment.ends.xy, segment.ends.zw);
        precise double width = segment.widths.x + ((segment.widths.y - segment.widths.x) * projection.z);
        precise double radius = width * 0.5LF;
        precise double dx = point.x - projection.x;
        precise double dy = point.y - projection.y;
        precise double squared = (dx * dx) + (dy * dy);
        double distance = sqrt(squared);
        double segment_coverage;
        if (distance <= radius) segment_coverage = 1.0LF;
        else if (segment.widths.z == 0.0LF) segment_coverage = 0.0LF;
        else {
            precise double outer = radius * (1.0LF + segment.widths.z);
            precise double ramp = (outer - distance) / (outer - radius);
            segment_coverage = clamp(ramp, 0.0LF, 1.0LF);
        }
        subtraction = max(subtraction, segment_coverage);
    }
    return subtraction;
}

void main() {
    ivec2 local = ivec2(gl_GlobalInvocationID.xy);
    if (local.x >= params.region.z || local.y >= params.region.w) return;
    // Inputs and output are region-sized; geometry uses global canvas pixel centres.
    int index = local.y * params.region.z + local.x;
    ivec2 pixel = local + params.region.xy;
    precise double point_x = (double(pixel.x) + 0.5LF) * params.map.x / params.map.z;
    precise double point_y = (double(pixel.y) + 0.5LF) * params.map.y / params.map.w;
    dvec2 point = dvec2(point_x, point_y);
    dvec4 result = dvec4(0.0LF);

    if (params.flags.x != 0) {
        dvec4 layer = params.flags.z != 0 ? read_input(background.pixels[index]) : dvec4(0.0LF);
        layer = apply_texture_strokes(layer, 0, point);
        result = over(with_opacity(layer, params.opacity.x), result);
    }

    if (params.flags.y != 0) {
        dvec4 layer = params.flags.w != 0 ? read_input(foreground.pixels[index]) : dvec4(0.0LF);
        layer = apply_texture_strokes(layer, 1, point);
        double coverage = params.flags2.x != 0 ? double(coverage_input.values[index]) : 0.0LF;
        if (params.flags2.y != 0) coverage = 1.0LF;
        for (int stroke_index = 0; stroke_index < params.flags2.z; stroke_index++) {
            Stroke stroke = strokes.items[stroke_index];
            if (stroke.info.x != 2) continue;
            double footprint = 0.0LF;
            if (point.x >= stroke.bounds.x && point.x <= stroke.bounds.z &&
                point.y >= stroke.bounds.y && point.y <= stroke.bounds.w)
                footprint = land_coverage(stroke, point);
            coverage = apply_coverage(coverage, footprint, stroke.extra.y);
        }
        if (params.flags2.w > 0) coverage = apply_coverage(coverage, river_subtraction(point), 1);
        precise double layer_alpha = coverage * params.opacity.y;
        result = over(with_opacity(layer, layer_alpha), result);
    }

    imageStore(output_image, local, write_straight(result));
}
