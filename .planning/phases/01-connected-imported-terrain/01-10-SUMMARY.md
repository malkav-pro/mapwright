---
phase: 01-connected-imported-terrain
plan: 10
subsystem: acceptance-testing
tags: [godot, dotnet, vulkan, performance, evidence, tdd]

requires:
  - phase: 01-connected-imported-terrain
    provides: connected import, rendering, durability, UI, history, and export cases from plans 01-01 through 01-09
provides:
  - Fail-closed acceptance metric engine with sequence/revision correlation and nearest-rank percentiles
  - Independent 11-case connected runner and real-Vulkan four-by-three hardware recorder
  - Hashed Phase 1 evidence report with honest failed and unverified dispositions
affects: [phase-1-verification, performance-remediation, capacity-instrumentation, visual-acceptance]

actuals:
  tokens: 24023
  tasks: 2
  commits: 6
  plan_head_before: 11cf0a26a352382680f474d780efbf2da9ea9fa0

tech-stack:
  added: []
  patterns:
    - Fail-closed evidence evaluation for missing, empty, dropped, duplicate, and uncorrelated samples
    - One fresh Godot process per public connected case plus a real-Vulkan hardware process
    - Raw evidence hashes and explicit pass/fail/unverified report dispositions

key-files:
  created:
    - Scripts/Acceptance/ConnectedAcceptance.cs
    - Scripts/run-phase1-acceptance.ps1
    - tests/Mapwright.ContractTests/AcceptanceContracts.cs
    - .planning/phases/01-connected-imported-terrain/01-10-TASK1-RED.json
    - .planning/phases/01-connected-imported-terrain/01-ACCEPTANCE.md
  modified: []

key-decisions:
  - "The captured full-run post-draw times were proxy observations because a matching canvas draw was not confirmed. The follow-up runner now requires MapCanvas._Draw to submit the matching revision and generation before FramePostDraw can close a sample; the full matrix still needs rerunning for causal evidence."
  - "A one-run real-Vulkan smoke after the draw-generation fix completed a causally matched sample at 7,672.076 ms p95; it is not a replacement for the required 12-run matrix and still fails the interaction threshold."
  - "Missing, empty, dropped, duplicate, revision-mismatched, or uncorrelated samples remain failed/unverified."
  - "D-20 uses the authorized UnstyledGeneratedEdge branch; unused coast distance/style checks are N/A, while colour, alpha, and seams remain gated."
  - "Export duration is reported as an observation only and never used as a threshold."
  - "Phase 1 remains incomplete because quantitative interaction/history gates fail and import-capacity plus Main/Compact visual evidence are unverified."

patterns-established:
  - "Evidence before verdict: raw JSON/log/capture hashes accompany every summarized result."
  - "Capacity claims require both a measured byte count and an explicit measurement-provenance flag."

requirements-completed: []
requirements-evaluated: [DOC-01, DOC-02, DOC-03, IMPT-01, IMPT-02, IMPT-03, LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01, HIST-01, HIST-02, HIST-04, EXPT-01, UIIN-01, DURA-01, DURA-02, REND-01, REND-02, REND-03, REND-04, REND-05]

coverage:
  - id: D1
    description: Fail-closed correlated metric, scenario-schema, and measured-resource contracts
    verification:
      - kind: unit
        ref: "./Scripts/run-contract-tests.ps1 — 58 passed, 0 failed"
        status: pass
      - kind: unit
        ref: "tests/Mapwright.ContractTests/AcceptanceContracts.cs"
        status: pass
    human_judgment: false
  - id: D2
    description: Eleven required connected cases independently discovered and invoked with substantive assertions
    verification:
      - kind: e2e
        ref: "01-ACCEPTANCE.md#connected-case-registry-and-independent-invocations — 11/11 passed, 286 assertions, unknown exit 2"
        status: pass
    human_judgment: false
  - id: D3
    description: Four scenarios in warm, cold, and evicted cache states measured for at least 60 seconds on real Vulkan hardware
    requirement: REND-04
    verification:
      - kind: e2e
        ref: "./Scripts/run-phase1-acceptance.ps1 -VerifyReport"
        status: fail
    human_judgment: false
  - id: D4
    description: D-28 editing-scope cues reviewed at 100% and 150% scale
    requirement: UIIN-01
    verification:
      - kind: manual_procedural
        ref: "artifacts/ui-smoke/scope-{land,texture,opacity}-{100,150}.png"
        status: pass
    human_judgment: true
    rationale: Original-resolution visual legibility and semantic separation require visual judgment; all six captures passed, while Main/Compact full-frame comparison remains unverified.
  - id: D5
    description: Complete import, paint, export capacity envelope with measured provenance
    requirement: REND-05
    verification:
      - kind: e2e
        ref: "01-ACCEPTANCE.md#resource-readings"
        status: unknown
    human_judgment: true
    rationale: Interaction and export allocations passed, but no measured import process peak exists, so the complete capacity claim is unverified.
  - id: D6
    description: Hashed Phase 1 acceptance report covering 23 requirement IDs and all 17 design dispositions
    verification:
      - kind: other
        ref: "structural audit — cases=11, runs=12, requirements=23, frames=17, D28=6"
        status: pass
      - kind: e2e
        ref: "./Scripts/run-phase1-acceptance.ps1 -VerifyReport — 13 findings"
        status: fail
    human_judgment: true
    rationale: Report completeness is automated, but unresolved quantitative and visual/capacity evidence intentionally prevents acceptance.

duration: 1h 22m
completed: 2026-09-23
status: halted
---

# Phase 01 Plan 10: Connected Acceptance Closure Summary

**Fail-closed connected acceptance now records real-map, independent-case, visual, and Vulkan evidence while preserving measured Phase 1 failures instead of promoting them to passes.**

## Performance

- **Duration:** 1h 22m
- **Started:** 2026-09-23T02:41:19Z
- **Completed:** 2026-09-23T04:03:49Z
- **Tasks:** 2 executed; Task 1 complete, Task 2 halted on acceptance failures
- **Files created:** 5 production/evidence files plus this summary

## Accomplishments

- Added a TDD-backed metric engine that rejects missing, empty, dropped, duplicate, revision-mismatched, and uncorrelated samples; preserves sequence order for equal timestamps; and calculates nearest-rank p50/p95/p99.
- Added a runner that enumerates the exact 11 public cases, invokes each in a fresh Godot process, rejects unknown names, and records 12 real-Vulkan scenario/cache runs against the pinned 74,541,363-byte `.ink` fixture.
- Recorded 11/11 connected cases passing with 286 assertions, six D-28 captures passing visual review, and all 17 design frames receiving explicit evidence-backed dispositions.
- Preserved the actual failed result: interaction p95 values measured from 3.39 s to 23.28 s, undo/redo input-to-visible was uncorrelated, recent undo p95 measured 131.51–142.27 ms, import peak capacity was unmeasured, and Main/Compact dedicated visual captures were absent.

## Task Commits

1. **Task 1 RED: failing acceptance contracts** — `8010ab6`
2. **Task 1 GREEN: correlated hardware recorder and runner** — `a2bee91`
3. **Evidence correction: real export-capacity reporting** — `3997770`
4. **Evidence correction: serialize measurement provenance** — `fc82eb3`
5. **Evidence correction: fail closed on incomplete capacity** — `316cec8`
6. **Task 2: executed acceptance evidence report** — `c639695`

## Files Created

- `Scripts/Acceptance/ConnectedAcceptance.cs` — metric evaluator, resource/schema contracts, real-map hardware recorder, and internal acceptance case.
- `Scripts/run-phase1-acceptance.ps1` — self-test, independent connected-case orchestration, Vulkan full run, report validation, and hashed evidence generation.
- `tests/Mapwright.ContractTests/AcceptanceContracts.cs` — missing/empty/uncorrelated, ordering, percentile, schema, and resource provenance contracts.
- `.planning/phases/01-connected-imported-terrain/01-10-TASK1-RED.json` — validated intentional RED evidence.
- `.planning/phases/01-connected-imported-terrain/01-ACCEPTANCE.md` — measured results, visual answers, 23 requirement rows, 17 design dispositions, and raw evidence hashes.

## TDD Gate Compliance

- **RED:** `8010ab6` — the five targeted tests failed because `AcceptanceMetricEngine` did not exist; `gsd_run check tdd-red-evidence` returned `RED_EVIDENCE_OK`.
- **GREEN:** `a2bee91` — all acceptance contracts and the full canonical suite passed.
- **REFACTOR/fixes:** `3997770`, `fc82eb3`, and `316cec8` corrected evidence provenance/reporting; the canonical suite remained 58/58.

## Decisions Made

- Used a private `__AcceptanceHardware` case so the public registry remains exactly the required 11 cases.
- Used the real `MapCanvas`, shared `ConnectedTerrainGraph.RenderReference`, durable SQLite history/session path, first matching `FramePostDraw`, the pinned fixture, and physical Forward+ Vulkan on an AMD Radeon RX 7800 XT.
- Kept export elapsed time observational. The measured final export case took 37.868 seconds, but no verdict depends on that duration.
- Left the plan and Phase 1 halted because the evidence does not support completion.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Preserved all resource measurement flags in raw JSON**

- **Found during:** Task 2 first full run
- **Issue:** Resource admission used explicit measurement flags, but the serialized evidence retained only `gpuMeasured`.
- **Fix:** Added process, decoded-cache, export, and history measurement flags to `AcceptanceResourceEvidence`, then reran the entire full suite.
- **Files modified:** `Scripts/Acceptance/ConnectedAcceptance.cs`, `Scripts/run-phase1-acceptance.ps1`
- **Verification:** Final `hardware.json` contains all five `*Measured: true` fields for every run; contract suite passes 58/58.
- **Committed in:** `fc82eb3`

**2. [Rule 1 - Bug] Separated interaction-process zero export allocation from the real 16K export peak**

- **Found during:** Task 2 report audit
- **Issue:** A zero interaction-process allocation could be misread as the export case's measured buffer.
- **Fix:** Parsed and reported the connected export's 71,299,072-byte buffer and 1,927,512,064-byte process peak separately.
- **Files modified:** `Scripts/run-phase1-acceptance.ps1`
- **Verification:** Evidence report names both measurements and their distinct scopes.
- **Committed in:** `3997770`

**3. [Rule 2 - Missing Critical] Prevented incomplete capacity evidence from passing REND-05**

- **Found during:** Task 2 capacity audit
- **Issue:** Interaction and export allocations passed, but import peak process memory was not measured.
- **Fix:** REND-05 now remains `FAIL/UNVERIFIED` without a measured import peak; report generation also records commands, exact targets, and artifact hashes.
- **Files modified:** `Scripts/run-phase1-acceptance.ps1`
- **Verification:** Report explicitly records the missing import peak and does not claim complete capacity.
- **Committed in:** `316cec8`

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 missing critical evidence guard). **Impact:** Evidence became stricter; no product behavior or acceptance threshold was weakened.

## Issues Encountered

- The final full runner and `-VerifyReport` intentionally exit 1. Every one of the 12 hardware runs fails REND-04; undo/redo also lacks correlated input-to-visible samples and exceeds the 100 ms recent-undo target.
- Dedicated 1920×1080 Main and 1366×768 Compact layout captures were not produced, so those frame-level visual comparisons remain unverified despite passing ShellStates interactions.
- Complete REND-05 remains unverified because import peak process memory is absent. Interaction and 16K export allocations that were measured are within their envelopes.
- Offline NuGet vulnerability lookup emitted NU1900 warnings; local pinned packages restored and all 58 contracts passed.

## Verification Results

- `./Scripts/run-contract-tests.ps1` — PASS: 58 passed, 0 failed, 58 discovered.
- `./Scripts/run-phase1-acceptance.ps1 -SelfTest` — PASS in 8.57 seconds.
- Final `./Scripts/run-phase1-acceptance.ps1 -Full` — FAIL by design after complete evidence emission.
- `./Scripts/run-phase1-acceptance.ps1 -VerifyReport` — FAIL: 13 findings (12 hardware rows plus remaining unverified evidence).
- Structural report audit — PASS: 11 cases, 286 assertions, 12 runs, 23 requirement IDs, 17 design frames, and six D-28 visual rows.

## User Setup Required

None — the run used pinned local Godot/.NET tools and the specified local fixture.

## Next Phase Readiness

Phase 1 is **not complete**. Remediation or fresh evidence is required for:

- sequence-correlated interaction latency within 50/100 ms p95/p99;
- recent resident undo within 100 ms p95 and correlated undo/redo presentation evidence;
- measured import peak capacity for the complete REND-05 envelope;
- dedicated Main and Compact original-resolution visual captures.

No requirement was marked complete, and shared `STATE.md`, `ROADMAP.md`, and `REQUIREMENTS.md` were not changed.

## Self-Check: FAILED

All declared files and six production/evidence commits exist, the worktree was clean before summary creation, and structural evidence checks pass. The self-check remains **FAILED** because the plan's quantitative and completeness acceptance criteria do not pass; this summary is intentionally `status: halted`.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
