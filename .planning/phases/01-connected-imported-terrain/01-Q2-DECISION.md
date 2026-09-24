# Q2 Resolved Texture Brush Decision

Decision: Persist a version-1 resolved texture recipe with a content-addressed texture SHA-256, built-in versioned tip family, radius converted once from a validated 1–2048 document-pixel diameter, normalized hardness/opacity/flow/roughness/corner-smoothing/jitter, 0.05–1.0 diameter-relative spacing, 0.0625–16 texture scale, -180–180 degree rotation, explicit taper/PRNG/brush algorithm versions, and document-space samples anchored independently of tiles and zoom. Use a compact seven-preset capability inventory: hard round, soft round, tapered round, square/pencil, chalk irregular, grain irregular, and edged texture.

Evidence: The supplied preset reference visibly distinguishes hard and soft round tips, automatic two-ended tapered strokes, square/pencil strokes, and multiple irregular/textured families; the edged reference separately shows a firm irregular outline with Roughness and Smooth plus texture Scale and Rotation. The repository contains no brush-tip artwork to fabricate into a bundled library, so every resolved preset requires a real caller-supplied texture SHA-256 while tip geometry remains a versioned built-in algorithm. Existing seam probes use 256-pixel tiles and approximately 0.22-radius sampling; the chosen 2048-pixel maximum diameter bounds a point footprint to at most 9×9 base tiles before finite jitter support, while 0.20 default spacing matches that proven sampling order of magnitude. Contract cases validate bounds, all required families, two-ended mouse taper, seeded replay, tile-edge anchoring, target switches, coverage independence, and SQLite reopen.

## Chosen bounds and defaults

| Parameter | Accepted range | Default | Evidence/rationale |
|---|---:|---:|---|
| Diameter | 1–2048 document px | 96 px | Radius is stored as diameter/2 once; max is finite against 256-px tiles and useful on the 3780×4097 connected fixture without adopting the mockup's sample 100. |
| Hardness | 0–1 | preset-specific | Unit interval maps directly to falloff and keeps hard/soft families explicit. |
| Brush opacity | 0–1 | 1 | Independent of layer opacity and coverage. |
| Flow | 0–1 | 0.8 | Unit interval supports deterministic accumulation. |
| Spacing | 0.05–1.0 × diameter | 0.20 | Existing probe sampling uses roughly 0.22 radius; lower bound prevents unbounded dab density, upper bound remains connected. |
| Texture scale | 0.0625–16 | 1 | Symmetric four-octave range around native scale; bounded and distinct from diameter. |
| Rotation | -180–180 degrees | 0 | Explicit stored representation; no implicit UI wrapping. |
| Jitter | 0–1 × radius | 0 | Finite support can be included in invalidation; values above one were rejected as misleading outside-footprint scatter. |
| Roughness | 0–1 | 0 except irregular/edged presets | Normalized algorithm input rather than the screenshot's example value 8. |
| Corner smoothing | 0–1 | 0.5 for edged, otherwise 0 | Applies only to edged tips and means geometric corner rounding, not alpha softness or pointer stabilization. |
| Taper | None or Smooth both ends v1 | preset-specific | v1 uses a smooth 20% lead-in/out with a 0.08 radius floor so mouse endpoints narrow without vanishing. |
| Seed / versions | signed 32-bit seed; version exactly 1 | per stroke | A fixed SplitMix-style integer mapping avoids runtime `Random` implementation drift. |

## Candidate comparisons and rejections

- **Screenshot-derived defaults (size 100, roughness 8, scale 100%):** rejected as authority because D-16/D-18 and the UI contract explicitly identify them as examples.
- **Diameter up to 4096 with jitter above one radius:** rejected because a single point would span up to 17×17 base tiles before jitter and would make affected-region bounds much larger than the displayed footprint.
- **Pixel spacing:** rejected because it would change stroke density when diameter changes; diameter-relative spacing preserves preset behavior.
- **Mutable preset-name history:** rejected because library edits or missing entries would change replay. `PresetId` is retained only as provenance; every effective value is stored in the command.
- **Runtime `Random`:** rejected because implementation changes can alter sequences across runtimes. The stored random version selects a fixed integer algorithm.
- **Zero-radius tapered endpoints:** rejected because two-sample mouse strokes could disappear entirely; the chosen 0.08 floor still produces visibly narrow ends.
- **Bundling invented brush artwork:** rejected because the repository contains only `Assets/logo.png`; content-addressed caller-supplied textures satisfy asset identity without fabricating unavailable art.
