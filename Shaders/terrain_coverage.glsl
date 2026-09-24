#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict writeonly buffer Coverage {
    float values[];
} coverage;

const uint WIDTH = 512;
const uint HEIGHT = 512;

float segment_distance(vec2 p, vec2 a, vec2 b) {
    vec2 ab = b - a;
    float support = dot(ab, ab);
    float t = support <= 1e-12 ? 0.0 : clamp(dot(p - a, ab) / support, 0.0, 1.0);
    return length(p - (a + ab * t));
}

void main() {
    uvec2 pixel = gl_GlobalInvocationID.xy;
    if (pixel.x >= WIDTH || pixel.y >= HEIGHT) return;
    uint index = pixel.y * WIDTH + pixel.x;
    vec2 p = vec2(pixel) + vec2(0.5);

    vec2 q = (p - vec2(256.0, 264.0)) / vec2(214.0, 184.0);
    float island = 1.0 - smoothstep(0.82, 1.02, length(q));
    island = max(island, 1.0 - smoothstep(62.0, 78.0, length(p - vec2(126.0, 205.0))));
    island = max(island, 1.0 - smoothstep(52.0, 68.0, length(p - vec2(398.0, 346.0))));

    float riverDistance = min(
        segment_distance(p, vec2(220.0, 92.0), vec2(252.0, 188.0)),
        min(segment_distance(p, vec2(252.0, 188.0), vec2(235.0, 300.0)),
            segment_distance(p, vec2(235.0, 300.0), vec2(186.0, 448.0))));
    float river = 1.0 - smoothstep(7.0, 12.0, riverDistance);
    // Coverage is scalar non-colour data. The production graph applies ordered
    // Land strokes first and river subtraction last using the same clamp rule.
    coverage.values[index] = clamp(island * (1.0 - river), 0.0, 1.0);
}
