#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer Coverage {
    float values[];
} coverage;

layout(set = 0, binding = 1, std430) restrict writeonly buffer OutputPixels {
    uint values[];
} output_pixels;

layout(set = 0, binding = 2, std430) restrict writeonly buffer OutputDistance {
    float values[];
} output_distance;

layout(push_constant, std430) uniform Params {
    int input_width;
    int input_height;
    int interior_x;
    int interior_y;
    int output_width;
    int output_height;
    int global_origin_x;
    int global_origin_y;
    int document_width;
    int document_height;
} params;

const int BAND = 22;

float read_coverage(ivec2 p) {
    p = clamp(p, ivec2(0), ivec2(params.input_width - 1, params.input_height - 1));
    return coverage.values[p.y * params.input_width + p.x];
}

void main() {
    ivec2 local = ivec2(gl_GlobalInvocationID.xy);
    if (local.x >= params.output_width || local.y >= params.output_height) return;
    ivec2 input_pixel = local + ivec2(params.interior_x, params.interior_y);
    float center = read_coverage(input_pixel);
    bool inside = center >= 0.5;
    float nearest = float(BAND);

    for (int y = -BAND; y <= BAND; y++) {
        for (int x = -BAND; x <= BAND; x++) {
            float d2 = float(x * x + y * y);
            if (d2 >= nearest * nearest) continue;
            bool sample_inside = read_coverage(input_pixel + ivec2(x, y)) >= 0.5;
            if (sample_inside != inside) nearest = sqrt(d2);
        }
    }

    float signed_distance = inside ? -nearest : nearest;
    ivec2 global_pixel = ivec2(params.global_origin_x, params.global_origin_y) + local;
    vec2 document_uv = (vec2(global_pixel) + vec2(0.5)) /
        vec2(params.document_width, params.document_height);
    float texture_a = sin(float(global_pixel.x) * 0.173) * cos(float(global_pixel.y) * 0.139);
    float texture_b = sin(dot(document_uv, vec2(197.0, 263.0)));
    float grain = 0.014 * texture_a + 0.008 * texture_b;
    vec3 water = vec3(0.075, 0.20, 0.25) + grain;
    vec3 land = vec3(0.55, 0.49, 0.34) + grain;
    vec3 color = mix(water, land, center);

    float shore = 1.0 - smoothstep(0.5, 1.8, abs(signed_distance));
    color = mix(color, vec3(0.90, 0.76, 0.45), shore * 0.85);
    if (!inside && signed_distance < float(BAND)) {
        float ring = 1.0 - smoothstep(0.45, 1.15, abs(mod(signed_distance + 1.5, 6.0) - 3.0));
        float fade = 1.0 - smoothstep(4.0, float(BAND), signed_distance);
        color = mix(color, vec3(0.55, 0.72, 0.72), ring * fade * 0.52);
    }

    uint index = uint(local.y * params.output_width + local.x);
    output_pixels.values[index] = packUnorm4x8(vec4(clamp(color, 0.0, 1.0), 1.0));
    output_distance.values[index] = signed_distance;
}
