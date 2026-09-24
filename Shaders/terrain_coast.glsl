#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer Coverage {
    float values[];
} coverage;

layout(set = 0, binding = 1, std430) restrict writeonly buffer OutputColor {
    vec4 values[];
} output_color;

const int WIDTH = 512;
const int HEIGHT = 512;
void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (pixel.x >= WIDTH || pixel.y >= HEIGHT) return;
    int index = pixel.y * WIDTH + pixel.x;
    float center = read_coverage(pixel);
    // Q5 selected D-20's unstyled generated-edge branch. Merged coverage cannot
    // prove that a boundary is an outer coast rather than a river/lake bank.
    vec2 uv = (vec2(pixel) + vec2(0.5)) / vec2(WIDTH, HEIGHT);
    float grain = 0.018 * sin(float(pixel.x) * 0.37 + float(pixel.y) * 0.61);
    vec3 water = vec3(0.075, 0.20, 0.25) + grain;
    vec3 land = vec3(0.55, 0.49, 0.34) + grain;
    vec3 color = mix(water, land, center);

    output_color.values[index] = vec4(clamp(color, 0.0, 1.0), 1.0);
}
