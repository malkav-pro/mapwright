---
phase: 01-connected-imported-terrain
plan: "07"
subsystem: export-recovery
tags: [png, sqlite, crash-recovery, atomic-publication, tdd]

requires:
  - phase: 01-05
    provides: Durable imported project state, immutable source blobs, and history persistence
  - phase: 01-06
    provides: ConnectedTerrainGraph frozen rendering and RenderResourceLedger limits
provides:
  - Frozen-revision 16K PNG export with pinned blobs, bounded buffers, validation, and atomic publication
  - Durable content-addressed blob publication and verified fresh-process recovery facts
  - Nine-stage forced-loss coverage for edit commits and PNG publication
affects: [01-08, verification, export, recovery]

actuals:
  tokens: 20292
  tasks: 2
  commits: 4
plan_head_before: fb3795233d56eb87347ab44f5d424850a5c5fe90

tech-stack:
  added: []
  patterns:
    - Frozen snapshot capture with pinned immutable blob leases
    - Same-directory temporary sibling followed by validated atomic replacement
    - Durable hash-verified blob staging before SQLite acknowledgement
    - Fresh-process kill-and-reopen verification at explicit crash stages

key-files:
  created:
    - Scripts/Export/DocumentPngExport.cs
    - tests/Mapwright.ContractTests/ExportRecoveryContracts.cs
  modified:
    - Scripts/Export/StreamingPngWriter.cs
    - Scripts/Export/PngValidator.cs
    - Scripts/run-connected-phase1.ps1
    - src/Mapwright.Infrastructure/SqliteProjectRepository.cs
    - Tools/StorageCrashWorker/Program.cs

key-decisions:
  - "Capture ConnectedTerrainGraph once per export and hold leases for every pinned source blob until publication completes."
  - "Require straight-alpha RGBA plus sRGB/gAMA metadata and full CRC/inflated-length validation before replacing a destination."
  - "Expose only recovery facts proven from the reopened repository; never infer the exact unacknowledged gesture."
  - "Host CrashRecovery at the existing connected-case registry seam because Infrastructure cannot depend outward on Mapwright.App."

patterns-established:
  - "Frozen export: later document edits cannot change an in-flight render or its pinned assets."
  - "Crash verification: a killed child never performs cleanup; a distinct process proves reopened state."

requirements-completed: [DOC-02, DOC-03, HIST-04, EXPT-01, DURA-01, DURA-02, REND-01, REND-03, REND-05]

coverage:
  - id: D1
    description: "A frozen connected-document revision exports as a validated 16K straight-alpha sRGB PNG with bounded resources and atomic destination preservation."
    requirement: EXPT-01
    verification:
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case ExportPublication (22 assertions)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Acknowledged edits, cursor state, blob hashes, and prior exports survive nine forced-loss stages in a fresh process."
    requirement: DURA-02
    verification:
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case CrashRecovery (49 assertions)"
        status: pass
    human_judgment: false
  - id: D3
    description: "Export and recovery contracts are enforced by the repository-wide contract suite."
    requirement: DURA-01
    verification:
      - kind: unit
        ref: "Scripts/run-contract-tests.ps1 (53 passed)"
        status: pass
    human_judgment: false

duration: 31 min
completed: 2026-09-23
status: complete
---

# Phase 01 Plan 07: Export Publication and Crash Recovery Summary

**Frozen 16K PNG publication with pinned connected-terrain inputs, full validation, atomic replacement, and nine-stage fresh-process durability recovery**

## Performance

- **Duration:** 31 min
- **Started:** 2026-09-22T23:40:52Z
- **Completed:** 2026-09-23T00:11:31Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments

- Exported an immutable connected-document snapshot at 16,384 x 16,384 through 256 bounded tiles while holding five source-blob leases, then validated and atomically published the PNG.
- Made immutable blob publication hash-verified and durable before SQLite acknowledgement, with startup checks for schema, integrity, cursor, referenced blobs, and owned staging files.
- Proved edit and export recovery through nine forced child-process losses, repeated fresh-process reopen, equal-timestamp ordering, and pinned-revision concurrency.

## Task Commits

Each TDD gate was committed atomically:

1. **Task 1 RED: Frozen export contracts** - `5116d02` (test)
2. **Task 1 GREEN: Frozen bounded PNG publication** - `6ac12a6` (feat)
3. **Task 2 RED: Crash recovery contracts** - `3b031ca` (test)
4. **Task 2 GREEN: Durable forced-loss recovery** - `5c763bb` (feat)

No refactor commit was needed; each GREEN implementation remained in its minimal tested form.

## Files Created/Modified

- `Scripts/Export/DocumentPngExport.cs` - Frozen graph capture, bounded rendering, progress/cancellation, validation, publication, and connected probes.
- `Scripts/Export/StreamingPngWriter.cs` - Streaming RGBA PNG output with sRGB and gAMA metadata.
- `Scripts/Export/PngValidator.cs` - CRC, metadata, dimensions, scanline filters, decompressed length, and cancellation validation.
- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` - Durable blob publication, explicit crash stages, and proven recovery facts.
- `Tools/StorageCrashWorker/Program.cs` - Kill-stage child and independent verification-process modes.
- `Scripts/run-connected-phase1.ps1` - Builds the crash worker used by connected recovery cases.
- `tests/Mapwright.ContractTests/ExportRecoveryContracts.cs` - Export and recovery API/metadata contracts.
- `.planning/phases/01-connected-imported-terrain/01-07-TASK1-RED.json` - Validated RED evidence for export behavior.
- `.planning/phases/01-connected-imported-terrain/01-07-TASK2-RED.json` - Validated RED evidence for recovery behavior.

## Decisions Made

- The export captures one immutable `ConnectedTerrainGraph` frame and leases all referenced blobs, so later edits remain interactive without changing the frozen output.
- PNG publication requires type-6 RGBA, straight alpha, sRGB/gAMA, valid CRCs, valid filters, exact inflated length, and no trailing bytes before atomic replace.
- Startup recovery reports only facts verified from SQLite and content-addressed blobs; it does not fabricate details about an unacknowledged gesture.
- The connected-case provider lives beside `DocumentPngExport`, the actual `Mapwright.App` registry seam, while crash-stage and repository APIs remain in Infrastructure.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created the absent plan-listed contract file**
- **Found during:** Task 1
- **Issue:** `tests/Mapwright.ContractTests/ExportRecoveryContracts.cs` did not exist.
- **Fix:** Added the file using the existing reflection-driven contract-test conventions.
- **Files modified:** `tests/Mapwright.ContractTests/ExportRecoveryContracts.cs`
- **Verification:** Both RED evidence gates validated, followed by 53/53 passing contracts.
- **Committed in:** `5116d02`, `3b031ca`

**2. [Rule 3 - Blocking] Built the standalone crash worker in the connected runner**
- **Found during:** Task 2
- **Issue:** The root Godot project intentionally excludes `Tools/**`, so the required connected command did not produce a worker executable.
- **Fix:** Restored and built `Tools/StorageCrashWorker/StorageCrashWorker.csproj` before connected-case discovery/execution.
- **Files modified:** `Scripts/run-connected-phase1.ps1`
- **Verification:** `CrashRecovery` launched killed children and independent verifiers for all nine stages.
- **Committed in:** `5c763bb`

**3. [Rule 3 - Blocking] Registered CrashRecovery at the actual dependency-safe registry seam**
- **Found during:** Task 2
- **Issue:** The plan named `SqliteProjectRepository.cs` as the registration site, but Infrastructure cannot reference the outward `Mapwright.App` connected-case registry without violating project boundaries.
- **Fix:** Kept recovery stages and APIs in Infrastructure and registered the provider in `DocumentPngExport.cs`, where connected providers are discoverable.
- **Files modified:** `Scripts/Export/DocumentPngExport.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`
- **Verification:** `-ListCases` contains `CrashRecovery`; the case passed 49 assertions.
- **Committed in:** `5c763bb`

---

**Total deviations:** 3 auto-fixed (3 blocking). **Impact:** The changes preserve existing dependency direction and make the exact plan verification commands executable; no feature scope was added.

## Issues Encountered

- NuGet vulnerability-feed lookup emitted `NU1900` warnings because the sandbox could not reach `api.nuget.org`; all required packages were already restored, and builds and tests completed successfully.
- Godot emitted a Windows root-certificate-store warning during headless connected runs; neither case performs network access, and both completed successfully.

## TDD Gate Compliance

| Task | RED evidence | RED commit | GREEN verification | GREEN commit | Status |
|---|---|---|---|---|---|
| Frozen export | `RED_EVIDENCE_OK`; 2 intentional target failures | `5116d02` | 53 contracts + 22 connected assertions | `6ac12a6` | Pass |
| Crash recovery | `RED_EVIDENCE_OK`; 2 intentional target failures | `3b031ca` | 53 contracts + 49 connected assertions | `5c763bb` | Pass |

## Verification Results

- `Scripts/run-contract-tests.ps1` - PASS: 53 passed, 0 failed.
- `Scripts/run-connected-phase1.ps1 -ListCases` - PASS: includes `ExportPublication` and `CrashRecovery`.
- `Scripts/run-connected-phase1.ps1 -Case ExportPublication` - PASS: 22 assertions; 16K, 256 tiles, peak export buffer 71,299,072 bytes, output 190,110,153 bytes, SHA-256 `9d739a1f4923d0602bc38507f71ea71ad67221b9e7d2b52ca121b1edaacf6faf`, five pinned blobs.
- `Scripts/run-connected-phase1.ps1 -Case CrashRecovery` - PASS: 49 assertions across nine stages; stable equal-timestamp revisions `1,2`; pinned revision `0` while current revision reached `2`.
- `git diff --check` - PASS.
- Stub/skipped-test scan - PASS: no matches in changed production and test files.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Export publication and durable recovery contracts are ready for Plan 01-08.
- Requirement completion remains deferred by the shared-ID gate until all sibling Phase 1 plans declaring these IDs have summaries.
- No blockers remain.

## Self-Check: PASSED

- All six plan key files exist.
- All four RED/GREEN commits exist and match the TDD scopes.
- Both task acceptance criteria and the plan-level verification commands pass.
- `STATE.md` and `ROADMAP.md` were intentionally left untouched for the orchestrator.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
