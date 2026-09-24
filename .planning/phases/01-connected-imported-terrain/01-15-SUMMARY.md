---
phase: 01-connected-imported-terrain
plan: 15
status: complete
gap_closure: true
requirements: [TERR-02, MASK-01, REND-01, REND-02, REND-04, REND-05]
---

# 01-15 — Incremental and recoverable viewport

The connected canvas now presents an imported flattened map from a revision-stamped, disposable accumulator. Adjacent texture edits update only their footprint; source/visibility changes, revision gaps, unsupported compositions, and eviction reconstruct from immutable project data or fall back to the full reference. Evicted reconstruction stamps samples into tile-local coverage buffers in stroke order and encodes final touched pixels once. A content-addressed display blob is published at import, loaded and hash-verified on cold/evicted updates, and reconstructed from the original preview if damaged. The display blob is durable derived source, not a retained decoded/render cache or export authority.

The canvas selects a 1792-pixel presentation level when the 896-pixel fit view is enlarged enough. It retains the old tiles until replacement frames are ready, adjusts pan/zoom to preserve the pointer's document coordinate, and leaves natural-resolution frozen export unchanged. Original-resolution Main and Compact high-zoom captures were reviewed as legible with no visible seams.

On the pinned 74 MB real `.ink` fixture and physical RX 7800 XT/Vulkan adapter, a normal sequence of three 60-second paint rows passed the unchanged thresholds: warm p95 36.174 ms, cold 17.294 ms, evicted 38.872 ms, each with 15 correlated edits, frame p95 about 16.7 ms, and zero >100 ms stalls. Evidence: `artifacts/phase1-15/diagnostic-stamped-paint-trio-60s.json` (SHA-256 `12522be4b12c57d01afd36dbc7d57de656f53c2024a63d6e3cb768f33f531d1c`). This is a paint-only diagnostic sequence, **not** the complete 12-row Phase 1 acceptance gate. Separate fresh-process/isolated cache rows exposed first-write outliers; their failures remain visible in `01-15-DIAGNOSTIC.md`.

Verification: 64/64 contracts, RenderReference 62 assertions (including 16 overlapping strokes, revision-gap/transparency fallbacks and damaged-display recovery), Gestures 22 assertions, and ExportPublication 22 assertions. The full connected suite, renewed import peak, other nine full-duration hardware rows, and final Phase 1 verifier remain Plan 01-14 work. Phase status remains `gaps_found`.
