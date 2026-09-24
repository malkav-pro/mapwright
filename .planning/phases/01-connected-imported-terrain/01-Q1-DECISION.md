# Q1 — Real `.ink` terrain mapping decision

Decision: Accept `ink-v3-full-canvas-terrain-v1` only for source version 3 when exactly one `layer-bg/brush`, one `layer-fg/brush`, and one `layer-fg/mask` checkpoint share the preview dimensions and the explicit full-canvas transform; map them to Background colour, Foreground colour, and Foreground alpha coverage respectively. Preserve every other raster and all source commands as reportable provenance, replay zero source commands natively, and fall back to the locked flattened preview plus empty Foreground whenever the strict mapping is not demonstrated.

Evidence: The real 74,541,363-byte fixture has SHA-256 `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`, source version 3, document size 7559.1416309012875 × 8192, and a 3780 × 4097 RGBA preview (`54549d4c9b8cb6c762f7e271edcdf2c7cc797567c6617ecf7d0ae0493cfc760c`). Source order is `layer-bg/brush` head 3245 (`10769382d6e8fa54bc6f4e0e47176ad1165da6acbde0426b963a3a091678734a`), `layer-fg/brush` head 3456 (`10a5dafd46df47086c2893930e4019f33772d1df9103595ac505ec516891ec6a`), then `layer-fg/mask` head 3155 (`ca118412f11fced305d5b5b7f671a2730a35c5400634fca61d697d9f4e14be58`). All are 3780 × 4097, so their explicit transform is origin (0,0), scale (7559.1416309012875/3780, 8192/4097). The mask RGB channels are uniformly zero while alpha ranges 0–255; using its alpha to composite Foreground over Background reduced mean preview RGB error from 42.3578 (Background only) and 21.6822 (unmasked Foreground over Background) to 8.1277, with 51.2377% of preview pixels exact despite stamps, paths, text, and other baked objects. Inverting alpha raised mean RGB error to 55.9105. The source contains 3,596 commands across 18 command types and 375 observed entities; none of those state-changing command payloads has a proven native replay mapping, so the trusted replay cutoff is zero and the raster checkpoint heads are provenance rather than replay instructions.

## Accepted candidate

- Source version: exactly `3`.
- Background colour: `layer-bg/brush`, straight-alpha sRGB RGBA.
- Foreground colour: `layer-fg/brush`, straight-alpha sRGB RGBA.
- Foreground coverage: alpha channel of `layer-fg/mask`; RGB is ignored.
- Transform: full-canvas origin `(0,0)` with independent document/pixel scale recorded per raster.
- Ordering: numeric source order, then ordinal source ID and role as a deterministic tie break.
- Baked effects: checkpoint and flattened preview identity are retained; no effect is guessed removable or replayed.

## Rejected candidates

- Background-only visual: rejected for editable recovery; mean preview RGB error 42.3578.
- Foreground without mask: rejected; mean preview RGB error 21.6822 and no recoverable coastline coverage.
- Mask RGB or inverted-alpha coverage: rejected; mask RGB is zero and inverted alpha produced mean RGB error 55.9105.
- Command-history replay after a checkpoint head: rejected until individual state-changing command semantics are tested; source bytes and command inventory remain inspectable.
- Heuristic layer-name or order-only mapping: rejected. Any missing, duplicate, differently sized, differently transformed, or different-version candidate degrades honestly to the visual fallback.
