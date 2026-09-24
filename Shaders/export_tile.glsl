#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict writeonly buffer OutputPixels {
    uint values[];
} output_pixels;

layout(push_constant, std430) uniform Params {
    int origin_x;
    int origin_y;
    int tile_width;
    int tile_height;
    int output_width;
    int output_height;
} params;

float segment_distance(vec2 p, vec2 a, vec2 b) {
    vec2 ab = b - a;
    float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 0.0001), 0.0, 1.0);
    return length(p - (a + ab * t));
}

void main() {
    ivec2 local = ivec2(gl_GlobalInvocationID.xy);
    if (local.x >= params.tile_width || local.y >= params.tile_height) return;
    ivec2 pixel = ivec2(params.origin_x, params.origin_y) + local;
    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(params.output_width, params.output_height);

    vec2 q = (uv - vec2(0.51, 0.51)) / vec2(0.41, 0.37);
    float land_distance = (length(q) - 0.88) * 1200.0;
    land_distance = min(land_distance, (length((uv - vec2(0.24, 0.39)) / vec2(0.14, 0.12)) - 0.75) * 800.0);
    land_distance = min(land_distance, (length((uv - vec2(0.76, 0.65)) / vec2(0.12, 0.10)) - 0.72) * 800.0);

    float river_distance = min(
        segment_distance(uv, vec2(0.46, 0.16), vec2(0.51, 0.37)),
        min(segment_distance(uv, vec2(0.51, 0.37), vec2(0.47, 0.61)),
            segment_distance(uv, vec2(0.47, 0.61), vec2(0.36, 0.88)))) * float(params.output_width);
    float river_half_width = 0.008 * float(params.output_width);
    float river_signed = river_distance - river_half_width;
    float signed_distance = max(land_distance, -river_signed);
    float coverage = 1.0 - smoothstep(-1.5, 1.5, signed_distance);

    float grain = 0.015 * sin(float(pixel.x) * 0.073 + float(pixel.y) * 0.117);
    vec3 water = vec3(0.07, 0.19, 0.24) + grain;
    vec3 land = vec3(0.57, 0.50, 0.34) + grain;
    vec3 color = mix(water, land, coverage);
    float shore = 1.0 - smoothstep(0.5, 2.0, abs(signed_distance));
    color = mix(color, vec3(0.91, 0.77, 0.46), shore * 0.85);
    if (signed_distance > 0.0 && signed_distance < 34.0) {
        float ring = 1.0 - smoothstep(0.6, 1.4, abs(mod(signed_distance + 2.0, 9.0) - 4.5));
        color = mix(color, vec3(0.50, 0.68, 0.70), ring * (1.0 - signed_distance / 34.0) * 0.48);
    }

    uint index = uint(local.y * params.tile_width + local.x);
    output_pixels.values[index] = packUnorm4x8(vec4(clamp(color, 0.0, 1.0), 1.0));
}
