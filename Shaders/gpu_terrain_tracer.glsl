#[compute]
#version 450

// R1 feasibility only: one legacy texture stroke over an immutable flattened
// RGBA8 source. This is intentionally not the complete terrain evaluator.
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba8, set = 0, binding = 0) uniform readonly image2D source_image;
layout(rgba8, set = 0, binding = 1) uniform writeonly image2D display_image;
layout(set = 0, binding = 2, std430) readonly buffer Samples {
    vec4 positions[];
} samples;

layout(push_constant, std430) uniform Params {
    vec4 dimensions; // raster width, raster height, map width, map height
    vec4 brush;      // radius, hardness, opacity*flow, sample count
    vec4 colour;     // legacy sRGB colour, 0..1
} params;

float srgb_to_linear(float value) {
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
}

float linear_to_srgb(float value) {
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (pixel.x >= int(params.dimensions.x) || pixel.y >= int(params.dimensions.y)) return;
    vec4 base = imageLoad(source_image, pixel);
    vec2 point = (vec2(pixel) + vec2(0.5)) /
        params.dimensions.xy * params.dimensions.zw;
    float coverage = 0.0;
    for (int index = 0; index < int(params.brush.w); index++) {
        vec2 delta = point - samples.positions[index].xy;
        float distance = length(delta) / params.brush.x;
        if (distance > 1.0) continue;
        float hardness = params.brush.y;
        float dab = distance <= hardness || hardness >= 0.999 ? 1.0 :
            clamp(1.0 - (distance - hardness) / (1.0 - hardness), 0.0, 1.0);
        coverage += dab * (1.0 - coverage);
    }
    if (coverage <= 0.0) {
        imageStore(display_image, pixel, base);
        return;
    }
    float alpha = clamp(params.brush.z * coverage, 0.0, 1.0);
    vec3 source_linear = vec3(
        srgb_to_linear(params.colour.r),
        srgb_to_linear(params.colour.g),
        srgb_to_linear(params.colour.b));
    vec3 base_linear = vec3(
        srgb_to_linear(base.r), srgb_to_linear(base.g), srgb_to_linear(base.b));
    vec3 output_linear = source_linear * alpha + base_linear * (1.0 - alpha);
    imageStore(display_image, pixel, vec4(
        linear_to_srgb(output_linear.r),
        linear_to_srgb(output_linear.g),
        linear_to_srgb(output_linear.b), 1.0));
}
