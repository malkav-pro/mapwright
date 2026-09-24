#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer InputSeeds {
    ivec4 values[];
} input_seeds;

layout(set = 0, binding = 1, std430) restrict writeonly buffer OutputSeeds {
    ivec4 values[];
} output_seeds;

layout(push_constant, std430) uniform Params {
    int width;
    int height;
    int step_size;
    int padding;
} params;

bool better(ivec2 pixel, ivec2 candidate, ivec2 current) {
    if (candidate.x < 0) return false;
    if (current.x < 0) return true;
    ivec2 candidate_delta = pixel - candidate;
    ivec2 current_delta = pixel - current;
    int candidate_distance = candidate_delta.x * candidate_delta.x + candidate_delta.y * candidate_delta.y;
    int current_distance = current_delta.x * current_delta.x + current_delta.y * current_delta.y;
    return candidate_distance < current_distance ||
        (candidate_distance == current_distance &&
            (candidate.y < current.y || (candidate.y == current.y && candidate.x < current.x)));
}

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    if (pixel.x >= params.width || pixel.y >= params.height) return;
    int index = pixel.y * params.width + pixel.x;
    ivec4 best = input_seeds.values[index];
    for (int oy = -1; oy <= 1; oy++) {
        for (int ox = -1; ox <= 1; ox++) {
            ivec2 sample_pixel = pixel + ivec2(ox, oy) * params.step_size;
            if (sample_pixel.x < 0 || sample_pixel.y < 0 ||
                sample_pixel.x >= params.width || sample_pixel.y >= params.height) continue;
            ivec4 candidate = input_seeds.values[sample_pixel.y * params.width + sample_pixel.x];
            if (better(pixel, candidate.xy, best.xy)) best.xy = candidate.xy;
            if (better(pixel, candidate.zw, best.zw)) best.zw = candidate.zw;
        }
    }
    output_seeds.values[index] = best;
}
