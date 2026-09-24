#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer Coverage {
    float values[];
} coverage;

layout(set = 0, binding = 1, std430) restrict writeonly buffer Seeds {
    ivec4 values[];
} seeds;

layout(push_constant, std430) uniform Params {
    int width;
    int height;
} params;

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (pixel.x >= params.width || pixel.y >= params.height) return;
    int index = pixel.y * params.width + pixel.x;
    bool inside = coverage.values[index] >= 0.5;
    seeds.values[index] = inside ? ivec4(pixel, -1, -1) : ivec4(-1, -1, pixel);
}
