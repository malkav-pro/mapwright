---
phase: 01-connected-imported-terrain
plan: 12
status: complete
gap_closure: true
requirements: [TERR-02, MASK-01, WATR-01, REND-01, REND-02, REND-03, REND-04, REND-05]
---

# 01-12 — Bounded connected viewport

The connected editor canvas and hardware acceptance recorder now share `MapCanvas.ShowCommittedChange`. It presents revision-tagged 256×256 textures and renders only tiles intersecting a command's conservative document-space invalidation bounds. Initial/open/source changes rebuild the viewport. The per-edit PNG encode/decode roundtrip is removed; frozen export still uses the complete reference renderer. The graph retains verified decoded inputs under resource-ledger admission, and explicit cache eviction remains available for the evicted acceptance state.

`RenderReference` preselects intersecting stroke samples for each region and precomputes stroke texture colours and rotations. This preserves document anchoring while reducing work in dirty tiles. A contract fixture compares stitched tiles with full-frame output across a 256-pixel seam with legacy and resolved texture, soft Land coverage, and river subtraction. The suite passed 60/60 contracts, and the hardware recorder's short paint/Warm diagnostic reported a matching revision/generation and zero per-edit PNG milliseconds.

Measured on the pinned real `.ink` fixture and physical Vulkan adapter, one **short diagnostic sample** moved from 11,391.630 ms input-to-postdraw and 11,349.523 ms reference render before stroke-loop optimization to 1,557.319 ms input-to-postdraw and 1,523.847 ms reference render after it. The earlier full-frame/PNG diagnostic was 6,692.840 ms input-to-postdraw. Raw optimized evidence: `artifacts/phase1-11/diagnostic-tiled-optimized.json` (SHA-256 `622d31f89c2a5b06fe3b88ce055919b286c1830372a06a77c36bfcdc7bc176a4f`). These one-run measurements establish stage improvement only; they do **not** pass the 60-second p95 or frame gates. Plan 01-13 and 01-14 remain open, and `01-VERIFICATION.md` remains `gaps_found`.

Subsequent viewport refinement keeps export at natural preview resolution but renders the interactive canvas from a verified, resampled 896-pixel display source. Independent dirty tiles evaluate in parallel after source priming. A one-sample warm paint diagnostic measured 52.762 ms input-to-matched-postdraw (25.08 ms durable commit, 24.52 ms tile render), still above the 50 ms p95 target. Raw evidence: `artifacts/phase1-11/diagnostic-paint-parallel-896.json`, SHA-256 `e2f5f4010466911ac92ff77f3a7330e06e75b07ce690900d1206c4f71747e331`. A separate one-sample cache-state check measured 64.021 ms warm, 782.141 ms cold and 845.753 ms evicted; cold/evicted verified-source rebuilding remains the key interaction gap. Raw evidence: `artifacts/phase1-11/diagnostic-paint-caches.json`, SHA-256 `5d2a64a75e9fe7fe3c49499278c305c3fdf29064aa0364dc26a04395c1369895`. These are short diagnostics, not acceptance rows. At high map zoom, adaptive detail-level replacement is not yet implemented; display fidelity there remains to be reviewed.

A later **60-second warm paint row** on the same fixture produced 15 correlated updates and failed: input-to-draw p50 93.398 ms, p95/p99 154.920 ms. Render cost grew from 31.44 ms on stroke 1 to 146.72 ms on stroke 15 while durable commits settled near 6–7 ms. Frame intervals passed (p95 16.753 ms, p99 17.184 ms). Raw evidence: `artifacts/phase1-11/diagnostic-paint-warm-60s.json`, SHA-256 `277a10166efe64eb9bbe7ae0589f23ff5c95bc648ed71437b01557686f7a862d`. This is one full-duration row, not the complete matrix; it proves the accumulated-stroke replay gap remains.
