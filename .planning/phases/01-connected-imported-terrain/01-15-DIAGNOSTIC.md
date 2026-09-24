---
phase: 01-connected-imported-terrain
plan: 15
status: partial
verified: 2026-09-23
---

# Plan 01-15 diagnostic: incremental viewport and source rebuild

The flattened-import viewport now maintains a revision-stamped linear and encoded accumulator. An adjacent legacy texture stroke updates only its footprint; a revision gap, changed input, unsupported composition, translucency, or eviction rebuilds or uses the reference renderer. The output remains disposable presentation state, not document authority. Import publishes a content-addressed 896-pixel display blob alongside the preserved preview; this immutable derived project source is read and hash-verified on cold/evicted updates, while decoded pixels and accumulator are genuinely discarded. If that display blob is damaged, presentation reconstructs from the preserved preview. The original preview and frozen revision remain export authority.

The rebuild stamps samples into tile-local coverage buffers in stroke order and encodes each final touched pixel once. High zoom requests a 1792-pixel display level, keeps the document point under the pointer during the replacement, and leaves natural-resolution export independent. Stage evidence records source read, SHA-256 verification, decode/copy, resampling, and accumulator work separately.

## Measured real-fixture results

Pinned input: `Main Continent-backup-2026-09-21T18_46_35.150Z.ink`, SHA-256 `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`; Godot 4.7.2 Mono, real Vulkan RX 7800 XT, 1920×1080. Correlation ends at the first `FramePostDraw` after a matching revision-tagged canvas draw, not display scanout.

| Paint diagnostic | Samples | Input-to-draw p95 | Render stage | Verdict |
|---|---:|---:|---:|---|
| Pre-incremental warm, 60 s | 15 | 154.920 ms | Grew 31.44–146.72 ms | Fail |
| Encoded incremental warm, 60 s | 15 | 42.297 ms | 8.63–19.31 ms; `append-stroke` | Pass for this row |
| Parallel source, cold/evicted, 1 s each | 1 each | 420.340 / 409.787 ms | Full preview decode and resample | Fail |
| Durable source and lazy accumulator, warm/cold/evicted, 60 s | 15 each | 43.487 / 21.816 / 155.736 ms | Evicted still replayed every prior stroke | Evicted fail |
| Stamped coverage, evicted alone, 60 s | 15 | 53.168 ms | 10.55–33.92 ms | Fail, near gate |
| Final normal paint trio, 60 s each | 15 each | **36.174 / 17.294 / 38.872 ms** | Warm 2.30–8.85, cold 2.19–7.51, evicted 7.18–30.38 ms | **All three pass** |

Earlier cold stage readings isolated 275–286 ms of PNG decode and 62–67 ms of resampling. The immutable display blob removed both from the input path: short cold/evicted source reads were about 0.6–0.7 ms and SHA-256 checks about 1.2–1.3 ms. The final normal trio used a real 60-second interval in each cache state, 15 correlated edits per row, frame p95 16.74–16.75 ms, zero >100 ms stalls, maximum process peak 1,317,384,192 bytes, and 30,380,672 decoded/presentation CPU bytes. Evicted still cleared decoded pixels and accumulator before every update. The durable import-time blob was not called or treated as a disposable render cache.

The final trio evidence is `artifacts/phase1-15/diagnostic-stamped-paint-trio-60s.json` (SHA-256 `12522be4b12c57d01afd36dbc7d57de656f53c2024a63d6e3cb768f33f531d1c`). A separate evicted-only fresh-process row measured p95 54.943 ms, and a separate warm-only run had a 108 ms durable-commit outlier. Thus this trio proves these exact rows passed once; it does not prove repeatability or replace the complete 12-row matrix.

## Correctness and remaining work

- `./Scripts/run-contract-tests.ps1`: 64 passed, 0 failed.
- `./Scripts/run-connected-phase1.ps1 -Case RenderReference`: 62 assertions passed, including 16 overlapping strokes compared to fresh reference within one channel, evicted rebuild, revision-gap and translucent-source fallback, prepared-source pixel identity, and damaged-display recovery.
- `./Scripts/run-connected-phase1.ps1 -Case Gestures`: 22 assertions passed, including preservation of the pointer's document coordinate across a rounded high-detail level change.
- `./Scripts/run-connected-phase1.ps1 -Case ExportPublication`: 22 assertions passed; frozen natural-resolution export remains independent of presentation detail.
- Original-resolution high-zoom frames were reviewed in Main (1920×1080) and Compact (1366×768); both used 1653×1792 presentation pixels and remained legible without visible seams. Files: `artifacts/ui-smoke/phase1-15-highzoom/main-highzoom.png` (SHA-256 `aea99355a5a9ae9dbc7a19f0e48af675e87cc673ff55e2ade0e82bae77753f98`) and `artifacts/ui-smoke/phase1-15-highzoom/compact-highzoom.png` (SHA-256 `8c37b8ebdf8059b44b3fc04d3c62e75141907ff94fcfefc3364f79813ff36d8b`).
- Still open: repeatability under fresh-process first-write and incidental commit stalls; full-duration pan/zoom, river and UndoRedo rows in all cache states; final complete connected, import-capacity and visual verification. Phase remains `gaps_found`.
