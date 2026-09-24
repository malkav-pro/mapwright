---
phase: 01-connected-imported-terrain
plan: 14
status: complete
gap_closure: true
requirements: [HIST-02, UIIN-01, REND-04, REND-05]
---

# 01-14 — Final connected acceptance

Main and Compact were captured at their specified original resolutions and reviewed against the Phase 1 UI contract. The six D-28 land-coverage, texture-colour and layer-opacity frames were also reviewed at 100% and 150%: each scope has a distinct cursor, panel or immediate-status cue. High-zoom Main/Compact captures supplement the fit review. See `01-14-VISUAL.md` and the hashed artifacts in `01-ACCEPTANCE.md`.

The final full run exercised all 11 connected cases independently (332 assertions) and the 12 physical-Vulkan scenario/cache rows for at least 60 seconds each. All connected cases, the unknown-case negative control, all hardware rows, recent-undo thresholds, and the import/paint/16K-export resource envelope passed. PaintAcrossTiles/Evicted input-to-visible p95 was 36.621 ms against the unchanged 50 ms target. The published report enumerates all 23 Phase 1 requirement IDs and dispositions for all 17 design files. Export elapsed time remains an observation, not a gate.

The first complete post-fix run produced passing raw measurements but exited 1 because the report generator still emitted placeholder visual-review verdicts. The next `-Full` run exposed a genuine PaintAcrossTiles/Evicted miss at 77.074 ms p95: its first two durable-command stages took 59.648 and 66.930 ms. Subsequent work removed repeated history-schema DDL/INSERT from a previously checked repository and replaced per-pixel sRGB power functions with a monotonic threshold lookup. A focused 60-second evicted diagnostic passed, then two complete 12-row matrices passed. The later run had all 11 cases, 12 hardware rows, reviewed image hashes, and `-VerifyReport` passing; `-Full` exited 0. The earlier miss remains in the diagnostic record rather than being erased. See `01-ACCEPTANCE.md` for final raw hashes and `01-VERIFICATION.md` for the phase-level verdict.
