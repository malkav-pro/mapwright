#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer Coverage {
    float values[];
} coverage;

layout(set = 0, binding = 1, std430) restrict readonly buffer Seeds {
    ivec4 values[];
} seeds;

layout(set = 0, binding = 2, std430) restrict writeonly buffer OutputPixels {
    uint values[];
} output_pixels;

layout(set = 0, binding = 3, std430) restrict writeonly buffer OutputDistance {
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

const float BAND = 22.0;

void main() {
    ivec2 local = ivec2(gl_GlobalInvocationID.xy);
    if (local.x >= params.output_width || local.y >= params.output_height) return;
    ivec2 input_pixel = local + ivec2(params.interior_x, params.interior_y);
    int input_index = input_pixel.y * params.input_width + input_pixel.x;
    float center = coverage.values[input_index];
    bool inside = center >= 0.5;
    ivec4 nearest_seeds = seeds.values[input_index];
    ivec2 nearest_opposite = inside ? nearest_seeds.zw : nearest_seeds.xy;
    float nearest = nearest_opposite.x < 0 ? BAND :
        min(BAND, length(vec2(input_pixel - nearest_opposite)));
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
    // Q5 selected the unstyled branch. Distance remains diagnostic output for
    // the legacy seam probe, but never affects product colour or claims contour
    // correctness for the inactive style path.

    uint output_index = uint(local.y * params.output_width + local.x);
    output_pixels.values[output_index] = packUnorm4x8(vec4(clamp(color, 0.0, 1.0), 1.0));
    output_distance.values[output_index] = signed_distance;
}
