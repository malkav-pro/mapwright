---
phase: 01-connected-imported-terrain
verified: 2026-09-23T10:20:47Z
status: passed
score: 5/5 roadmap success criteria verified
behavior_unverified: 0
---

# Phase 1: Connected Imported Terrain Verification

**Goal:** Recover the existing map and edit, save, reopen, undo/redo and export it from one durable authoritative document.

The final standard `./Scripts/run-phase1-acceptance.ps1 -Full` exited 0. It passed all 11 independent connected cases (332 assertions), the unknown-case negative control, all 12 ≥60-second physical-Vulkan scenario/cache rows, the import/paint/16K-export resource envelope, six reviewed D-28 scope captures, and reviewed Main/Compact shell captures. The regenerated report enumerates all 23 Phase 1 requirement IDs and all 17 design dispositions with raw-evidence hashes; `-VerifyReport` exits 0. See `01-ACCEPTANCE.md`.

## Goal achievement

| Roadmap criterion | Verdict | Evidence |
|---|---|---|
| 1. Honest, bounded import into fixed Background/Foreground roles | VERIFIED | Tracer, ImportBounds, EntryImportUi and ShellStates; both recovery choices, source identity, degradation/rejection, real flattened-import peak, and cache-deletion reopen. |
| 2. Native texture, Foreground coverage and river editing with distinct scope cues | VERIFIED | RenderReference, Gestures, ToolPanels and ScopeCues; six reviewed original-resolution D-28 captures at 100% and 150%, plus Main/Compact layout review. |
| 3. Durable save/reopen, cache-independent history and crash recovery | VERIFIED | Tracer and CrashRecovery; all three full-duration UndoRedo rows have correlated samples and resident-undo p95 below 100 ms, touching one tile. |
| 4. Shared-revision seamless 16K export, cancellation and publication | VERIFIED | RenderReference and ExportPublication; frozen-revision output, colour/alpha and seam comparison within one channel, validated 16K PNG, preserved destination on cancellation/failure, and measured export memory below budget. Export elapsed time is observational only. |
| 5. Four 60-second warm/cold/evicted scenarios meeting latency and frame targets | VERIFIED | Twelve independent 1920×1080 real-Vulkan rows, each ≥60 seconds with at least 15 correlated input-to-matching-draw samples and no exclusions. Worst input p95 was 36.74 ms against 50 ms; all frame, recent-undo, queue, stall and resource gates passed. |

**Score:** 5/5 against the Phase 1 baseline used for this implementation. All 23 mapped baseline requirement checks pass. D-20 uses the authorized unstyled generated-coast branch, so its style-distance checks are inapplicable; no active check is excused as N/A. The newer map-unit draft below is not included in this score.

## Evidence and limitations

- The pinned 74 MB `.ink` fixture was measured on an AMD Radeon RX 7800 XT using Vulkan. `artifacts/phase1-acceptance/hardware.json` preserves per-run sequence IDs, timings, queues, stalls and resource readings. The final matrix had no reported >100 ms stalls.
- REND-05 readings include a 682,881,024-byte real import-process peak, a 1,382,387,712-byte 16K export-process peak, and a 71,299,072-byte export buffer, all below their applicable limits. Raw case logs and `01-ACCEPTANCE.md` retain exact hashes and other measurements.
- An earlier complete post-fix repeat missed evicted paint p95 at 77.074 ms because its first two durable-command stages took 59.648 and 66.930 ms. The storage path was changed to avoid re-running already-checked history-schema migration SQL for every edit, and sRGB conversion was made lookup-based. A focused 60-second evicted diagnostic and two subsequent complete hardware matrices passed; the last complete run also passed its visual and report verifier. The failed repeat remains recorded in `01-14-SUMMARY.md` and `01-15-DIAGNOSTIC.md` as a repeatability warning, not silently discarded.
- Acceptance measures input to the first `FramePostDraw` after a matching revision-tagged canvas draw; physical display scanout is excluded. Passing the specified hardware run is not a guarantee against every future OS/filesystem latency outlier.

## Subsequent scope amendment awaiting reconciliation

The still-uncommitted 2026-09-23 Phase 4 discussion edits `REQUIREMENTS.md` DOC-01 and `01-DESIGN-SPEC.md` to require a fixed 1,000-map-unit longest edge and map-unit brush diameters, superseding prior map-pixel language. This acceptance gate does **not** verify that amendment: `Main.CreateFlattenedSnapshot` and `ImportProject` still construct `MapProject` with imported document width/height, and the fixture remains 7,559 × 8,192 document units. The amended DOC-01 must not be marked complete from this report. Whether to reopen Phase 1 for a coordinate-system migration or assign the cross-phase change to the ongoing Phase 4 integration work is a product/planning decision; the user's uncommitted requirement and tracker edits were preserved untouched.

## Verification metadata

- Source: Phase 1 roadmap criteria, plans 01-01 through 01-15 and their summaries, `01-UI-SPEC.md`, `01-14-VISUAL.md`, `01-ACCEPTANCE.md`, and raw artifacts.
- Checks: `./Scripts/run-contract-tests.ps1` (64/64), `./Scripts/run-phase1-acceptance.ps1 -SelfTest` (64/64), `-Full` (exit 0), and `-VerifyReport` (exit 0).
- The historical Plan 01-10 summary remains `status: halted`; it records the pre-gap-closure result and is superseded by the final post-fix evidence, not retroactively rewritten.
