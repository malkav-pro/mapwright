#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(r32f, set = 0, binding = 0) uniform image2D coverage_image;
layout(rgba8, set = 0, binding = 1) uniform writeonly image2D color_image;

layout(push_constant, std430) uniform Params {
    vec4 brush;
    ivec4 document;
} params;

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (pixel.x >= params.document.x || pixel.y >= params.document.y) return;
    float previous = imageLoad(coverage_image, pixel).r;
    float normalized = distance(vec2(pixel) + vec2(0.5), params.brush.xy) / params.brush.z;
    float falloff = max(0.0, 1.0 - normalized * normalized);
    float dab = params.brush.w * falloff * falloff;
    float coverage = params.document.z > 0
        ? previous + dab * (1.0 - previous)
        : previous * (1.0 - dab);
    coverage = clamp(coverage, 0.0, 1.0);
    imageStore(coverage_image, pixel, vec4(coverage));

    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(params.document.xy);
    float grain = 0.014 * sin(float(pixel.x) * 0.173) * cos(float(pixel.y) * 0.139) +
        0.008 * sin(dot(uv, vec2(197.0, 263.0)));
    vec3 water = vec3(0.075, 0.20, 0.25) + grain;
    vec3 land = vec3(0.55, 0.49, 0.34) + grain;
    imageStore(color_image, pixel, vec4(mix(water, land, coverage), 1.0));
}
