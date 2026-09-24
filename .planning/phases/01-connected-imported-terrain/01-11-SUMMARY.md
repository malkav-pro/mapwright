---
phase: 01-connected-imported-terrain
plan: 11
subsystem: acceptance-diagnostics
tags: [godot, vulkan, performance, import-capacity, evidence]
requires:
  - phase: 01-connected-imported-terrain
    provides: halted 01-10 hardware and resource evidence
provides:
  - Per-sequence durable/render/PNG/upload/draw stage measurements tied to the matched canvas generation
  - Measured real-import process peak with source-hash provenance
  - First causal stage breakdown for bounded renderer work
affects: [01-12, 01-13, 01-14, phase-1-verification]
actuals:
  tokens: 6100
  tasks: 2
  commits: 2
tech-stack:
  added: []
  patterns: [fail-closed per-stage acceptance evidence]
key-files:
  created: [.planning/phases/01-connected-imported-terrain/01-11-DIAGNOSTIC.md]
  modified: [Scripts/Acceptance/ConnectedAcceptance.cs, Scripts/App/MapCanvas.cs, Scripts/App/Main.cs, Scripts/run-phase1-acceptance.ps1, tests/Mapwright.ContractTests/AcceptanceContracts.cs]
key-decisions:
  - "Bounded rendering and removal of per-edit PNG encode are the next measured targets; commit and draw-to-postdraw are much smaller."
  - "The one-sample diagnostic is not promoted to the required twelve-run, sixty-second matrix."
requirements-completed: []
requirements-evaluated: [HIST-02, REND-04, REND-05]
coverage:
  - id: D1
    description: Per-sequence stage timing and correlation validation
    requirement: REND-04
    verification:
      - kind: unit
        ref: "./Scripts/run-phase1-acceptance.ps1 -SelfTest — 59 passed, 0 failed"
        status: pass
    human_judgment: false
  - id: D2
    description: Real import peak and short hardware bottleneck diagnostic
    requirement: REND-05
    verification:
      - kind: e2e
        ref: "./Scripts/run-connected-phase1.ps1 -Case ImportBounds — 19 assertions; artifacts/phase1-11/diagnostic.json"
        status: pass
    human_judgment: false
duration: 20min
completed: 2026-09-23
status: complete
---

# Phase 1 Plan 11: Causal performance diagnostic

The first matched warm-paint update spent 5,797.765 ms rendering the full reference frame and 830.983 ms encoding PNG, versus 26.923 ms in durable commit, 32.415 ms in texture creation, and 1.517 ms from draw to postdraw. Its 6,692.840 ms end-to-end result fails REND-04; one sample is not a qualifying run. The real import peak was 552,529,920 bytes with the pinned source hash, below the recorded 8,360,773,632-byte process budget. Full REND-05 remains open until consolidated acceptance reruns.

## Accomplishments

- Added complete, sequence/revision/generation-correlated stage records to raw hardware JSON; missing or mismatched stage records fail.
- Added source-hash-bound `IMPORT_CAPACITY` process-peak output and fail-closed report parsing.
- Wrote [01-11-DIAGNOSTIC.md](01-11-DIAGNOSTIC.md) with a hash of the raw Vulkan sample.

## Task commits

1. Stage attribution and contract — `b4e0888`.
2. Import peak and diagnostic — `3bca617`.

## Verification

- Acceptance self-test: 59 passed, 0 failed.
- ImportBounds connected case: 19 assertions, exit 0.
- One real-Vulkan short diagnostic produced a matched stage record; the hardware case exits 1 by design because it is not a complete matrix and is far above target.

## Deviations from plan

- MapCanvas gained `LastDrawnTimestamp` so draw-to-postdraw is measured rather than inferred from upload start. This adds a file to Task 1, with no change in render semantics.

## Issues and next work

The full reference render and PNG encode dominate. Plan 01-12 can now target bounded tiles and direct texture presentation. Plan 01-13 still must address undo p95 and revision-correlation logic. Phase 1 remains `gaps_found`; no requirement was marked complete.

## Self-Check: PASSED

Both scoped tasks, source assertions, test commands, raw evidence and task commits exist. This plan's completion does not imply Phase 1 acceptance.
