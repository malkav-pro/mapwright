---
phase: 01-connected-imported-terrain
plan: 05
subsystem: durable-history
tags: [csharp, sqlite, tdd, undo-redo, checkpoints, cancellation, virtualized-history]

requires:
  - phase: 01-connected-imported-terrain
    provides: Real imported project authority, fixed terrain roles, deterministic texture commands, and durable EditSession ordering from Plans 01 and 03
provides:
  - Durable history cursor, redo invalidation, and ordered Save queue state across restart
  - Bounded compatible resident deltas plus cancellable 64-command checkpoint replay from authoritative commands
  - Revision-labelled virtualized history windows, exact Undo/Redo reasons, cancellation handles, and revision timing events
affects: [01-06-rendering, 01-08-ui, 01-09-acceptance, 04-finishing-publication]

actuals:
  tokens: 27997
  tasks: 3
  commits: 6
  plan_head_before: b876f504313e23a61f52315be3a5708696004b40

tech-stack:
  added: []
  patterns:
    - Baseline, cursor, and branch tip are separate SQLite facts committed with command and materialized revision changes
    - Complete commands and the baseline are authoritative while checkpoints and compressed tile deltas are disposable acceleration
    - Edit and history publication emit ordered revision events only after durable acknowledgement

key-files:
  created:
    - src/Mapwright.Application/HistorySession.cs
    - tests/Mapwright.ContractTests/HistoryContracts.cs
    - .planning/phases/01-connected-imported-terrain/01-Q4-DECISION.md
  modified:
    - src/Mapwright.Application/Ports.cs
    - src/Mapwright.Application/EditSession.cs
    - src/Mapwright.Infrastructure/SqliteProjectRepository.cs

key-decisions:
  - "Use 64-command versioned checkpoints plus complete command replay; the measured full-snapshot alternative extrapolated beyond the separate 4 GiB acceleration budget."
  - "Keep at most 16 compatible resident tile deltas and key all acceleration by renderer version, recipe version, and immutable source identity."
  - "Use a 30-second autosave cadence for the later shell scheduler; edits remain durably committed immediately, and autosave is only an ordered verification/drain barrier."
  - "Commit a reconstructed cursor before publishing the restored snapshot; cancellation before that transaction leaves both durable cursor and visible revision unchanged."

patterns-established:
  - "Truthful queue state: Queued, Saving, Saved, and Failed derive from the ordered commit chain and durable revision rather than transient canvas state."
  - "Stable history paging: monotonically assigned command sequence breaks equal-time ties and pages never derive order from timestamps."

requirements-completed: [DOC-02, HIST-01, HIST-02, HIST-04, DURA-01]

coverage:
  - id: D1
    description: "Completed edits, cursor moves, redo invalidation, and Save drain through one ordered durable transaction/queue and survive reopen."
    requirement: HIST-01
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#history cursor and redo branch survive restart"
        status: pass
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#save drains queued commands and reports durable state"
        status: pass
      - kind: integration
        ref: "./Scripts/run-contract-tests.ps1 (29 discovered, 29 passed)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Recent compatible history uses at most 16 resident tile deltas, while evicted history replays from versioned checkpoints with real progress and atomic cancellation."
    requirement: HIST-02
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#history resident deltas are bounded and compatible; history evicted reconstruction cancellation is atomic"
        status: pass
      - kind: other
        ref: "./Scripts/run-contract-tests.ps1#HISTORY resident_undo samples=20 tiles=16 p95_ms=14.309"
        status: pass
    human_judgment: false
  - id: D3
    description: "Deleted or incompatible acceleration rebuilds from the retained baseline, commands, and immutable source identity without changing authority."
    requirement: HIST-04
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#history cache deletion and version mismatch preserve authority"
        status: pass
      - kind: e2e
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer (24 assertions)"
        status: pass
    human_judgment: false
  - id: D4
    description: "The shell can page revision-labelled action rows and display exact cursor, Undo/Redo, save queue, rebuild progress/cancellation, tile, and timing facts."
    requirement: DOC-02
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#history windows and controls are revision labelled"
        status: pass
      - kind: integration
        ref: "tests/Mapwright.ContractTests/HistoryContracts.cs#save drains queued commands and reports durable state"
        status: pass
    human_judgment: false

duration: 34 min
completed: 2026-09-22
status: complete
---

# Phase 1 Plan 05: Durable History, Save Queue, and Reconstruction Summary

**SQLite-backed cursor history now survives restart, prunes redo atomically, reconstructs evicted revisions from bounded versioned checkpoints, and exposes truthful queue and action facts to the shell.**

## Performance

- **Duration:** 34 min
- **Started:** 2026-09-22T21:43:49Z
- **Completed:** 2026-09-22T22:18:06Z
- **Tasks:** 3
- **Files modified:** 8

## Accomplishments

- Separated baseline, cursor, and branch tip in SQLite; cursor moves and replacement edits are durable, stale requests fail, equal-time commands retain sequence order, and Save waits behind every queued command.
- Selected and implemented measured 64-command checkpoints with complete command replay, a 16-tile compatible resident-delta bound, cache deletion/version fallback, real command-unit progress, and cancellation that leaves the old cursor/view intact.
- Added virtualized history windows with labels, revisions, timestamps, cursor state, measured tile counts, exact Undo/Redo reasons, live cancellation, truthful save facts, and structured revision timing events.

## Task Commits

Each task was committed atomically; TDD tasks have separate RED and GREEN commits:

1. **Task 1 RED: Durable cursor and save queue contracts** - `53e0b3a` (test)
2. **Task 1 GREEN: Durable cursor and ordered Save queue** - `34ee9af` (feat)
3. **Task 2 RED: Reconstruction, delta, cancellation, and compatibility contracts** - `a8bbdfa` (test)
4. **Task 2 Q4 gate: Measured checkpoint policy** - `8a005a3` (docs)
5. **Task 2 GREEN: Bounded resident and evicted reconstruction** - `8c9e92b` (feat)
6. **Task 3: Revision-labelled history facts** - `459b208` (feat)

**Plan metadata:** committed with this summary.

## TDD Gate Compliance

| Task | RED evidence | GREEN verification | Refactor |
|---|---|---|---|
| Durable cursor/save queue | `01-05-TASK1-RED.json` → `RED_EVIDENCE_OK` | 23/23 contracts passed | Not needed |
| Resident/evicted reconstruction | `01-05-TASK2-RED.json` → `RED_EVIDENCE_OK` | 28/28 task contracts; 29/29 final contracts passed | Not needed |

Both RED phases failed on the named planned behavior while all previously existing contracts remained green.

## Files Created/Modified

- `src/Mapwright.Application/HistorySession.cs` - Durable Undo/Redo, bounded resident deltas, cancellable reconstruction, action controls, and revision events.
- `src/Mapwright.Application/Ports.cs` - History state, entries/windows, compatibility, delta, reconstruction, queue, and timing contracts.
- `src/Mapwright.Application/EditSession.cs` - Ordered command/Save queue, truthful state, 30-second cadence contract, and revision publication events.
- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` - Cursor/tip schema, transactional redo pruning, stable paging, tile metadata, checkpoints, command replay, compatibility, and cache deletion.
- `tests/Mapwright.ContractTests/HistoryContracts.cs` - Restart, branching, queue, Q4, resident/evicted, cancellation, cache, paging, action, and timing contracts.
- `.planning/phases/01-connected-imported-terrain/01-Q4-DECISION.md` - Fixture identity, candidate measurements, selected cadence, budget rationale, and compatibility policy.

## Decisions Made

- Selected a 64-command checkpoint interval: measured snapshot acceleration was 829,250 bytes at 256 commands with a 63-command replay in 6.080 ms, versus 42,619,231 bytes for retained full snapshots.
- Kept command payloads, source identities, and the initial baseline authoritative. All later checkpoints and resident deltas may be deleted or rejected by compatibility without history loss.
- Chose a 30-second autosave cadence for later shell scheduling. It does not defer command durability; it schedules an ordered Save verification barrier when appropriate.
- Reported reconstruction in completed/total command units and exposed mismatch fallback and cancellation rather than inventing percentage or silently reusing stale pixels.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created two plan-listed source artifacts that were absent from the baseline**
- **Found during:** Mandatory `read_first` gate before Task 1
- **Issue:** `src/Mapwright.Application/HistorySession.cs` and `tests/Mapwright.ContractTests/HistoryContracts.cs` were listed as source files but did not exist; the legacy `Scripts/Core/EditingDocument.cs` probe was not an architecture-compatible substitute.
- **Fix:** Created engine-independent application history orchestration and a discoverable contract provider at the planned paths, while leaving the legacy probe untouched.
- **Files modified:** `src/Mapwright.Application/HistorySession.cs`, `tests/Mapwright.ContractTests/HistoryContracts.cs`
- **Verification:** All 29 contracts and the 24-assertion connected tracer passed.
- **Committed in:** `34ee9af`, `8c9e92b`, `459b208`

---

**Total deviations:** 1 auto-fixed (1 blocking plan/source discrepancy).
**Impact on plan:** The planned artifacts were created at their specified boundaries; no alternate architecture or scope was introduced.

## Issues Encountered

- The first sandboxed contract run could not access the runner's isolated NuGet configuration directory. The same repository-owned runner was rerun with approved filesystem access; the baseline 20/20 contracts passed before edits.
- Connected-tracer output includes deliberate child-process wrong-revision and missing-blob exceptions used by its fail-closed assertions; the tracer itself completed successfully with 24 assertions and a validated 3,780 × 4,097 PNG.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Durable history, save facts, and revision-labelled shell queries are ready for renderer and UI integration.
- Plan 09 still owns the full hardware p95 gate; this plan supplies a repeatable 16-tile distribution and recorded Q4 policy evidence, not a claim of final hardware acceptance.
- No blockers remain for subsequent Phase 1 plans.

## Self-Check: PASSED

- All planned implementation and test artifacts exist, including the created `HistorySession.cs`, `HistoryContracts.cs`, and Q4 decision record.
- Six production/TDD/decision commits exist after plan base `b876f504313e23a61f52315be3a5708696004b40`.
- Both RED evidence records validate as `RED_EVIDENCE_OK`.
- Exact plan verification reported 29 passed, 0 failed, 29 discovered; the connected tracer reported 24 assertions and validated PNG output.
- Q4 contains nonempty `Decision:` and `Evidence:` lines; stub scan found no TODO, FIXME, placeholder, skipped-test, or unfinished implementation marker.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-22*
