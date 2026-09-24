---
status: resolved
trigger: "Expand the renderer redesign to address durable-commit and strict GPU replay latency while preserving the 50 ms input-to-visible target."
created: 2026-09-23
updated: 2026-09-24
---

# Phase 1.1 durable commit and GPU replay latency

## Symptoms

- Expected: real-map acknowledged edits reach a matching `MapCanvas._Draw` and subsequent `FramePostDraw` within p95 50 ms / p99 100 ms, including durability and strict reconstruction.
- Actual: the shared-kernel strict GPU trial ran 60.039 seconds with 15/15 correlations but had p95 158.373 ms. Four commits took roughly 97–137 ms; a separate sequence spent 35.87 ms in replay and 50.493 ms total.
- Errors: no parity error at revisions 1, 8 or 15; zero final channels exceeded the one-level CPU reference tolerance. The failure is measured latency, not an exception.
- Timeline: observed after Phase 1.1 normalized geometry and the first GPU feasibility tracer on 2026-09-23. The production CPU path already failed the Phase 1.1 hardware matrix.
- Reproduction: `./Scripts/run-connected-phase1.ps1 -Case GpuTerrainStrict` on pinned Godot 4.7.2/Vulkan and the real `.ink` fixture. Raw trial: `artifacts/gpu-terrain-strict/trial-5b5354819a6b4dbd82aec2390e8c429a/gpu-terrain-strict.json`.

## Current Focus

- hypothesis: Serial durable commit plus one-output-per-stroke replay created an avoidable sum and history-growth cost; `FULL` SQLite sync and first presentation have independent tails.
- test: Keep the original clock and durability semantics, profile repository stages, use a barrier-separated two-scratch GPU replay, then overlap private candidate reconstruction with the transaction and gate display on exact acknowledgement.
- expecting: Strict legacy flattened-map p95 can pass in a fair display-baseline trial, but a long fsync still prevents a universal 50 ms guarantee.
- frame model (2026-09-24): rendering is single-threaded, so the edit starts in frame F's process step and the earliest matching `_Draw` is frame F+1 (~16.7 ms), with `FramePostDraw` after F+1 presents (~33 ms). An edit is ~33 ms if durable acknowledgement plus GPU recording finish before F+1's process step; otherwise it slips a whole frame (~50 ms). Every first-edit and fsync failure so far is this one-frame slip.
- next_action: None for Phase 01.1. Residual risk: rare storage flush tails near 45-50 ms can still fail a 15-sample row, because nearest-rank p95 is effectively the maximum. Durability semantics stay unchanged.

## Evidence

- timestamp: 2026-09-23
  source: `01.1-08-SUMMARY.md`
  finding: shared-kernel strict p95 158.373 ms; four durable-commit outliers; sequence 13 replay 35.87 ms, total 50.493 ms.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-bfa2a06dbae146628cfd928486be04a1/gpu-terrain-strict.json`
  finding: added repository stage profiling; SQLite `FULL` transaction commit alone reached 78.33 and 44.27 ms, independent of source/GPU replay.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-7273f4a93fe244ef84cb43b7c39d2a0a/gpu-terrain-strict.json`
  finding: two-scratch barrier-separated GPU replay held each stroke-history recording to roughly 2–6 ms, but first blank-canvas presentation made p95 92.919 ms; parity was unchanged.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-7d10cf3c27414428a8d89f959baab277/gpu-terrain-strict.json`
  finding: private candidate work overlapping the transaction reduced strokes 2–15 to roughly 33–34 ms, while blank-canvas first edit left p95 at 58.302 ms.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-a8e36ed85c8847deb237b54ad92762ca/gpu-terrain-strict.json`
  finding: a display-only imported baseline before the run gave a 60.070-second, 15-edit narrow legacy pass: p95 48.8243 ms, frame p95 16.9501 ms, no missing/stalls and sentinel parity within one channel value.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-4de9b78ba977401f86132be65dfee615/gpu-terrain-strict.json`
  finding: an independent 60.087-second, 15-edit repeat passed p95 48.8427 ms and frame p95 16.9118 ms, with no missing/stalls and the same sentinel parity.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-e97287655eba4923bff2fd1636006975/gpu-terrain-strict.json`
  finding: a third 60.184-second, 15-edit repeat failed p95 56.5693 ms because its first edit took 56.5693 ms; all later edits took about 33–35 ms. SQLite transaction commit stayed below 4 ms, isolating first-edit scheduling/preparation rather than fsync for this failure.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-7ec4e5f0b49f40b2a768f324f6a98890/gpu-terrain-strict.json`
  finding: after aligning the clock with the original GPU case (after synthetic command construction, before projection/commit/source), the first edit still took 52.4144 ms and p95 failed. Earlier broader-clock trials remain unchanged.
- timestamp: 2026-09-23
  source: `artifacts/gpu-terrain-strict/trial-a63043f202054456842ba977bb0d20c3/gpu-terrain-strict.json`
  finding: starting commit before a second independent `Apply` regressed the first edit to 58.4018 ms; duplicate cold applications contended. This variant was reverted.
- timestamp: 2026-09-24
  source: `artifacts/gpu-terrain-strict/trial-6e9380836ad340bbaae11beab1407095/gpu-terrain-strict.json`
  finding: `EditSession` now applies each command once and shares the projected `DocumentChange` (`PreparedEdit`) before its durable commit, replacing the tracer's duplicate `Apply`. Explicit reflection resolver plus pre-built command JSON metadata cut the first `CommandInsert` from ~9.8 to 6.6 ms. The first edit's durable acknowledgement came at 23.8 ms, still after the F+1 boundary: p95 54.55 ms, failed.
- timestamp: 2026-09-24
  source: `artifacts/gpu-terrain-strict/trial-d6cbcdc921874618b2b8c3cbb65f1fac/gpu-terrain-strict.json`, `trial-17e371199f00450b91130334f1007648`
  finding: with a discarded, never-committed command rehearsal (pure `Apply` and payload serialization, as `MapCanvas.BindConnectedSession` now does for the current tools; 10.7 ms reported as setup), the first edit was acknowledged at 13.5 ms and presented at 33.29 ms. Two 60-second, 15-edit passes at p95 34.1371 and 34.1264 ms, frame p95 ≈17.1 ms, parity 0 channels over one.
- timestamp: 2026-09-24
  source: `artifacts/gpu-terrain-strict/trial-79a815d1a8dd4779b5a191eba79933e4/gpu-terrain-strict.json`
  finding: the same code failed p95 149.81 ms on the fsync tail: `TransactionCommit` 79.83 and 45.78 ms on sequences 1–2, plus 40.49 and 38.45 ms between transaction commit and acknowledgement. Each commit opens a `Pooling=false` WAL connection, and closing the last connection checkpoints and deletes the WAL, so every edit re-creates the WAL and checkpoint-syncs.
- timestamp: 2026-09-24
  source: `trial-f975876bd25b44bd89e0ccb453614560`, `trial-c4d1ac145c8744f6a8bdfbe11d9ef090`, `trial-1857feb44144402d957a302a50c6f171` (diagnostic `MAPWRIGHT_GPU_STRICT_WAL_KEEPER=1`)
  finding: holding one idle connection so the WAL is never closed gave 45/45 transaction commits at ≤4.77 ms (without the keeper: max 79.83 ms) and post-commit ≤3.51 ms. Two passes (p95 33.8572, 34.4359 ms). One failure (p95 50.066 ms) was the first edit acknowledged at 17.57 ms, missing F+1 by about 1 ms with no fsync tail. The margin on the first edit remains thin. Three trials per arm cannot bound an intermittent tail, so this is diagnostic only.
- timestamp: 2026-09-24
  source: `artifacts/gpu-terrain-strict/trial-4bc62131911a4cecaf57ad00a098ec5a/gpu-terrain-strict.json`; `CrashRecovery` connected case (49 assertions)
  finding: implemented `SqliteProjectRepository.HoldConnection()`, held by `EditSession`/`HistorySession` for their lifetime, and removed the diagnostic keeper. All repository operations share the held connection in order, so the WAL is never checkpointed or deleted per edit. Median transaction commit 1.03 ms (max 2.65 ms); repository total median 1.41 ms. p50 16.67 ms, p95/p99 33.56 ms: edits 2–15 are presented in the same frame because the commit now finishes before the GPU work. Crash-after-commit and crash-after-acknowledgement children now die holding the connection, and fresh-process recovery passes.
- timestamp: 2026-09-24
  source: `artifacts/hardware-smoke/matrix-gpu-1.json` .. `matrix-gpu-4.json`; Full run `20260924T135914138Z-232c760722d046f0964980cf57f52ca9`
  finding: with the production fp64 GPU renderer, canvas work per edit is under 1 ms and every remaining miss was a durable-commit tail. Smoke rows failed at 50.04 ms (48.0/35.4 ms first commits after a fresh import) and 51.77 ms (47.5 ms first commit). Session preparation now opens the held connection and performs two no-op FULL-synchronous `user_version` rewrites so a new WAL's first appends happen at project open. The next smoke passed 12/12 (max p95 21.5 ms). The verified Full run passed 12/12 at p95 16.73-25.97 ms.

## Eliminated

- hypothesis: GPU colour output is incorrect on the legacy flattened path.
  reason: independent parity at revisions 1, 8 and 15 found no channel difference greater than one.
- hypothesis: Shader recompilation is the only cause of strict latency failure.
  reason: pixel-free shared-kernel run still failed p95 at 158.373 ms.
- hypothesis: The first-edit miss is GPU or draw scheduling.
  reason: recording finishes by ~10–13 ms and warm draws are always at F+1; the miss follows only a durable acknowledgement after the F+1 boundary (cold `Apply`, JSON metadata, first insert, connection open/close).
- hypothesis: The new passing run proves `FULL` SQLite commits are bounded below 50 ms.
  reason: an earlier profiled run measured a 78.33 ms transaction commit; the passing run merely did not encounter that tail.

## Resolution

- root_cause: serial durable commit, per-stroke GPU output allocation/submission, and blank-canvas first presentation combined with variable `FULL` SQLite sync latency.
- fix: private acknowledgement-gated GPU preparation concurrent with the unchanged durable transaction; two scratch textures with compute barriers; pre-existing imported baseline as a display-only lease. No SQLite durability change.
- fix (2026-09-24): single shared `PreparedEdit` from `EditSession`; prepared command JSON metadata; discarded command rehearsal when a session binds. No durability, threshold or workload change. The WAL-retaining write connection is proposed, not yet implemented.
- verification (2026-09-24): aligned-clock strict trials: 4 passes and 2 failures across 6 runs. One failure was the fsync/WAL tail without the keeper; the other was a first edit that missed the frame boundary by about 1 ms with the keeper. 81/81 phase self-test, RenderReference 66 and MapUnits 37 assertions pass.
- verification: two 15-edit/60-second broader-clock narrow legacy passes, one broader-clock failure, and two aligned-clock failures; 79/79 phase self-tests and 66 reference-render assertions. Reliable first-edit gate, broader acceptance and fsync-tail stability pending. R1 remains halted.
- files_changed: `Scripts/Acceptance/GpuTerrainTracerCase.cs`, `Scripts/Rendering/GpuTerrainTracer.cs`, `Scripts/App/MapCanvas.cs`, `src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Application/Ports.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`, `tests/Mapwright.ContractTests/HistoryContracts.cs`, `01.1-RENDERER-REDESIGN.md`.
