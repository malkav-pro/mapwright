#[compute]
#version 450

// Resamples one immutable source raster to the canvas sampling in double precision,
// exactly as ConnectedTerrainGraph does on the CPU: bilinear interpolation of
// premultiplied linear colour (or straight alpha for coverage), then quantization to
// straight sRGB RGBA8 (colour) or a float (coverage) before any composition.
// mode 0: raster already at canvas sampling (prepared display source); copy the region
// mode 1: whole-raster stretch (ResampleColour/ResampleCoverage)
// mode 2: placed raster through its map transform (SamplePlacedColour/Coverage)
// Output buffers cover one region of the canvas sampling; formulas use global pixels.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) readonly buffer Params {
    ivec4 dimensions;  // source width, source height, output width, output height
    ivec4 mode;        // mode, coverage output
    dvec4 map;         // map width, map height, offset x, offset y
    dvec4 scale;       // scale x, scale y
    ivec4 region;      // output region left, top, width, height within the canvas sampling
} params;
layout(set = 0, binding = 1, std430) readonly buffer Tables {
    double decode[256];
    double thresholds[255];
} tables;
layout(set = 0, binding = 2, std430) readonly buffer Source { uint pixels[]; } source;
layout(set = 0, binding = 3, std430) writeonly buffer Colour { uint pixels[]; } colour_output;
layout(set = 0, binding = 4, std430) writeonly buffer Coverage { float values[]; } coverage_output;

dvec4 read_input(int x, int y) {
    uint packed = source.pixels[y * params.dimensions.x + x];
    precise double a = double((packed >> 24) & 255u) / 255.0LF;
    precise dvec4 result = dvec4(
        tables.decode[packed & 255u] * a,
        tables.decode[(packed >> 8) & 255u] * a,
        tables.decode[(packed >> 16) & 255u] * a,
        a);
    return result;
}

double read_alpha(int x, int y) {
    precise double a = double((source.pixels[y * params.dimensions.x + x] >> 24) & 255u) / 255.0LF;
    return a;
}

double lerp_d(double from, double to, double amount) {
    precise double result = from + ((to - from) * amount);
    return result;
}

dvec4 lerp_pixel(dvec4 from, dvec4 to, double amount) {
    return dvec4(lerp_d(from.r, to.r, amount), lerp_d(from.g, to.g, amount),
        lerp_d(from.b, to.b, amount), lerp_d(from.a, to.a, amount));
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

uint write_straight(dvec4 pixel) {
    if (pixel.a <= 0.0LF) return 0u;
    precise double r = pixel.r / pixel.a;
    precise double g = pixel.g / pixel.a;
    precise double b = pixel.b / pixel.a;
    precise double scaled = pixel.a * 255.0LF;
    uint alpha = uint(clamp(int(roundEven(scaled)), 0, 255));
    return encode(r) | (encode(g) << 8) | (encode(b) << 16) | (alpha << 24);
}

void bilinear_setup(double x, double y, out ivec4 corners, out dvec2 weights) {
    int w = params.dimensions.x;
    int h = params.dimensions.y;
    x = clamp(x, 0.0LF, double(w - 1));
    y = clamp(y, 0.0LF, double(h - 1));
    int x0 = clamp(int(floor(x)), 0, w - 1);
    int y0 = clamp(int(floor(y)), 0, h - 1);
    corners = ivec4(x0, y0, clamp(x0 + 1, 0, w - 1), clamp(y0 + 1, 0, h - 1));
    precise double tx = x - floor(x);
    precise double ty = y - floor(y);
    weights = dvec2(clamp(tx, 0.0LF, 1.0LF), clamp(ty, 0.0LF, 1.0LF));
}

void main() {
    ivec2 local = ivec2(gl_GlobalInvocationID.xy);
    if (local.x >= params.region.z || local.y >= params.region.w) return;
    int index = local.y * params.region.z + local.x;
    ivec2 pixel = local + params.region.xy;
    int out_w = params.dimensions.z;
    int out_h = params.dimensions.w;
    bool coverage = params.mode.y != 0;
    double sx;
    double sy;
    if (params.mode.x == 0) {
        colour_output.pixels[index] = source.pixels[pixel.y * params.dimensions.x + pixel.x];
        return;
    }
    if (params.mode.x == 1) {
        if (!coverage && params.dimensions.x == out_w && params.dimensions.y == out_h) {
            colour_output.pixels[index] = source.pixels[pixel.y * params.dimensions.x + pixel.x];
            return;
        }
        precise double fx = ((double(pixel.x) + 0.5LF) * double(params.dimensions.x) / double(out_w)) - 0.5LF;
        precise double fy = ((double(pixel.y) + 0.5LF) * double(params.dimensions.y) / double(out_h)) - 0.5LF;
        sx = fx;
        sy = fy;
    } else {
        precise double px = (double(pixel.x) + 0.5LF) * params.map.x / double(out_w);
        precise double py = (double(pixel.y) + 0.5LF) * params.map.y / double(out_h);
        precise double source_x = (px - params.map.z) / params.scale.x;
        precise double source_y = (py - params.map.w) / params.scale.y;
        if (source_x < 0.0LF || source_y < 0.0LF ||
            source_x >= double(params.dimensions.x) || source_y >= double(params.dimensions.y)) {
            if (coverage) coverage_output.values[index] = 0.0;
            else colour_output.pixels[index] = 0u;
            return;
        }
        precise double fx = source_x - 0.5LF;
        precise double fy = source_y - 0.5LF;
        sx = fx;
        sy = fy;
    }
    ivec4 c;
    dvec2 t;
    bilinear_setup(sx, sy, c, t);
    if (coverage) {
        double top = lerp_d(read_alpha(c.x, c.y), read_alpha(c.z, c.y), t.x);
        double bottom = lerp_d(read_alpha(c.x, c.w), read_alpha(c.z, c.w), t.x);
        coverage_output.values[index] = float(lerp_d(top, bottom, t.y));
    } else {
        dvec4 top = lerp_pixel(read_input(c.x, c.y), read_input(c.z, c.y), t.x);
        dvec4 bottom = lerp_pixel(read_input(c.x, c.w), read_input(c.z, c.w), t.x);
        colour_output.pixels[index] = write_straight(lerp_pixel(top, bottom, t.y));
    }
}
