---
phase: 01-connected-imported-terrain
plan: 02
subsystem: import
tags: [ink-v3, provenance, bounded-decoding, cache-recovery, sqlite, godot]

requires:
  - phase: 01-01
    provides: connected document/repository pipeline and real `.ink` tracer fixture
  - phase: 01-03
    provides: fixed Background/Foreground terrain-role invariants and deterministic texture state
provides:
  - honest editable and flattened `.ink` recovery modes producing one native project revision
  - measured Q1 mapping decision with strict verified-raster allow-list and visual-only fallback
  - bounded compressed, expanded, token, base64, PNG, and aggregate import limits
  - pinned, idempotent disposable-cache deletion with source/blob/native-revision preservation
affects: [01-04, imported-terrain, project-reopen, export, verification]

actuals:
  tokens: 22540
  tasks: 2
  commits: 4
  plan_head_before: b876f504313e23a61f52315be3a5708696004b40

tech-stack:
  added: []
  patterns:
    - strict import mapping allow-list with explicit visual-only degradation
    - streaming allocation budgets enforced before buffer growth
    - disposable-cache pinning and canonical allow-list deletion

key-files:
  created:
    - src/Mapwright.Application/ImportProject.cs
    - .planning/phases/01-connected-imported-terrain/01-Q1-DECISION.md
    - .planning/phases/01-connected-imported-terrain/01-02-TASK1-RED.json
    - .planning/phases/01-connected-imported-terrain/01-02-TASK2-RED.json
  modified:
    - Scripts/App/Main.cs
    - Scripts/Core/InkImportService.cs
    - Scripts/Core/MapDocument.cs
    - Scripts/Core/ProjectStore.cs
    - src/Mapwright.Domain/MapModel.cs
    - src/Mapwright.Infrastructure/SqliteProjectRepository.cs
    - tests/Mapwright.ContractTests/ImportContracts.cs

key-decisions:
  - "Trust editable raster recovery only for mapping profile ink-v3-full-canvas-terrain-v1; otherwise preserve the preview as locked Background and report visual-only degradation."
  - "Persist source raster descriptors, immutable blobs, mapping profile, warnings, and replay cutoff instead of guessing unsupported command semantics."
  - "Delete only canonical cache/tiles and cache/history directories, refusing deletion while a reader is pinned."

patterns-established:
  - "Honest recovery: recovery labels follow verified source evidence, never optimistic inference."
  - "Bound before allocation: compressed, expanded, token, base64, image, and aggregate budgets reject before unbounded growth."
  - "Cache safety: exact disposable roots plus read pins make deletion idempotent without touching authoritative data."

requirements-completed: [DOC-01, DOC-03, IMPT-01, IMPT-02, IMPT-03]

coverage:
  - id: D1
    description: "Both recovery choices initialize one authoritative native project revision while preserving the original source package."
    requirement: DOC-01
    verification:
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case ImportBounds"
        status: pass
      - kind: unit
        ref: "tests/Mapwright.ContractTests/ImportContracts.cs#import project publishes all immutable blobs through SQLite"
        status: pass
    human_judgment: false
  - id: D2
    description: "Verified rasters map deterministically to fixed terrain roles; untrusted sources degrade to a locked preview without invented editability."
    requirement: IMPT-02
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/ImportContracts.cs#import project maps verified or flattened recovery"
        status: pass
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case Tracer"
        status: pass
    human_judgment: false
  - id: D3
    description: "Source IDs, hashes, transforms, baked-effect provenance, warnings, extras, and replay cutoff survive persistence and reopen."
    requirement: IMPT-01
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/ImportContracts.cs#import project persists source descriptors and replay cutoff"
        status: pass
    human_judgment: false
  - id: D4
    description: "Hostile compressed, expanded, token, nesting, base64, PNG, pixel, aggregate, and malformed inputs fail with bounded readable outcomes."
    requirement: IMPT-03
    verification:
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case ImportBounds"
        status: pass
      - kind: unit
        ref: "tests/Mapwright.ContractTests/ImportContracts.cs#import bounds reject hostile sources before allocation"
        status: pass
    human_judgment: false
  - id: D5
    description: "Disposable cache deletion is pinned, canonical, repeatable, and rebuilds the same committed rendering without source or native-command loss."
    requirement: DOC-03
    verification:
      - kind: integration
        ref: "Scripts/run-connected-phase1.ps1 -Case ImportBounds"
        status: pass
    human_judgment: false

duration: 27 min
completed: 2026-09-23
status: complete
---

# Phase 01 Plan 02: Honest Import Recovery and Bounded Cache Rebuild Summary

**Measured `.ink` recovery now produces one provenance-preserving native project through either verified editable rasters or a locked visual fallback, with hostile-input budgets and safe disposable-cache rebuilds.**

## Performance

- **Duration:** 27 min
- **Started:** 2026-09-22T21:45:52Z
- **Completed:** 2026-09-22T22:12:08Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments

- Added editable and flattened recovery choices that preserve original `.ink` bytes, source descriptors, immutable raster blobs, warnings, extras, and replay cutoff in one committed project revision.
- Recorded Q1 from the 74,541,363-byte real fixture: only `ink-v3-full-canvas-terrain-v1` is trusted for editable raster recovery; all other input keeps the source preview as a locked Background with an empty Foreground.
- Enforced compressed, expanded, JSON token/depth, base64, PNG dimension/pixel, and aggregate retained-byte limits before dangerous allocation.
- Added exact-root, pinned disposable-cache deletion and proved repeated deletion/reopen/export preserves the same revision and rendering hash.

## Task Commits

Each TDD task was committed as RED then GREEN:

1. **Task 1 RED: Map verified source terrain or locked preview into one project** - `3221225` (`test`)
2. **Task 1 GREEN: Map verified source terrain or locked preview into one project** - `eaef828` (`feat`)
3. **Task 2 RED: Bound import and cache-recovery edge cases** - `3e6c2a1` (`test`)
4. **Task 2 GREEN: Bound import and cache-recovery edge cases** - `afd98fa` (`feat`)

No refactor commit was necessary; the GREEN implementations were left in their minimal tested form.

## Files Created/Modified

- `src/Mapwright.Application/ImportProject.cs` - Recovery-mode use case, canonical destination checks, import bounds, and readable rejection type.
- `Scripts/Core/InkImportService.cs` - Bounded streaming gzip/JSON/base64/PNG decoder with provenance descriptors.
- `Scripts/Core/MapDocument.cs` - Converts import packages into fixed-role native documents.
- `Scripts/Core/ProjectStore.cs` - Pins disposable readers and limits idempotent deletion to canonical tile/history caches.
- `Scripts/App/Main.cs` - Registers the connected `ImportBounds` integration case and real-fixture recovery path.
- `src/Mapwright.Domain/MapModel.cs` - Persists import source descriptors and recovery metadata in domain snapshots.
- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` - Saves, validates, and reopens imported provenance and immutable blobs.
- `tests/Mapwright.ContractTests/ImportContracts.cs` - Recovery, persistence, hostile-input, and destination-safety contracts.
- `.planning/phases/01-connected-imported-terrain/01-Q1-DECISION.md` - Measured mapping/fallback disposition for the real `.ink` fixture.

## Decisions Made

- Editable recovery is an allow-listed claim: the exact measured mapping profile is trusted; unknown or incomplete mappings are visual-only.
- The original preview is authoritative for flattened appearance, while editable source rasters remain separate in deterministic source order.
- Cache deletion operates only on exact canonical disposable roots and fails while any relevant reader is pinned.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created the absent plan-listed application use case**

- **Found during:** Task 1 (Map verified source terrain or locked preview into one project)
- **Issue:** `src/Mapwright.Application/ImportProject.cs` was listed by the plan but did not exist, and there was no equivalent use case to extend.
- **Fix:** Created the use case at the planned path while retaining the existing inward dependency boundaries.
- **Files modified:** `src/Mapwright.Application/ImportProject.cs`
- **Verification:** 26/26 contract cases and both connected cases pass.
- **Committed in:** `eaef828`, extended by `afd98fa`

**2. [Rule 3 - Blocking] Registered connected verification in the actual composition root**

- **Found during:** Task 2 (Bound import and cache-recovery edge cases)
- **Issue:** The plan required an `ImportBounds` adapter but did not list the registry's actual source file; `ConnectedCaseRegistry` is composed in `Scripts/App/Main.cs`.
- **Fix:** Added the adapter to the existing registry without introducing framework concerns into the Application layer.
- **Files modified:** `Scripts/App/Main.cs`
- **Verification:** `run-connected-phase1.ps1 -ListCases` lists `ImportBounds`; the case passes 18 assertions.
- **Committed in:** `afd98fa`

**3. [Rule 2 - Missing Critical] Persisted the full provenance model across domain and SQLite boundaries**

- **Found during:** Task 1 (Map verified source terrain or locked preview into one project)
- **Issue:** The existing domain snapshot and repository could not retain per-raster source descriptors, mapping profile, warnings, replay cutoff, or every referenced immutable blob.
- **Fix:** Extended the domain snapshot and SQLite serialization/validation so reopen cannot silently lose or invent imported terrain provenance.
- **Files modified:** `src/Mapwright.Domain/MapModel.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`, `Scripts/Core/MapDocument.cs`
- **Verification:** Persistence and immutable-blob contracts pass; `Tracer` reopens and exports the real fixture with 24 assertions.
- **Committed in:** `eaef828`

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 missing critical). **Impact:** All changes were necessary to fulfill the planned import/persistence contracts; no unrelated feature scope was added.

## Issues Encountered

- Initial hostile fixtures crossed an earlier token/aggregate budget than the assertion intended; fixture sizes were narrowed so each test isolates its named limit while retaining fail-before-allocation behavior.
- Offline NuGet vulnerability-feed warnings (`NU1900`) and a Windows Godot root-certificate warning remained environmental; restore/build/tests completed successfully.
- Expected negative-path child-process exceptions appear in `Tracer` output for missing revisions/blobs and unknown/zero-assertion cases; the harness asserts these failures and the overall case passes.

## TDD Gate Compliance

| Task | RED | Evidence | GREEN | Result |
| --- | --- | --- | --- | --- |
| Map verified source terrain or locked preview | `3221225` | `01-02-TASK1-RED.json` → `RED_EVIDENCE_OK` | `eaef828` | Pass |
| Bound import and cache-recovery edge cases | `3e6c2a1` | `01-02-TASK2-RED.json` → `RED_EVIDENCE_OK` | `afd98fa` | Pass |

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Honest connected import and cache recovery are complete and ready for plan 01-04 integration/verification work.
- No implementation blocker remains. The existing offline vulnerability-feed warning can be rechecked when registry network access is available.

## Self-Check: PASSED

- All four TDD commits exist after recorded base `b876f504313e23a61f52315be3a5708696004b40`.
- Every file named in `key-files.created` exists.
- Task acceptance criteria and plan verification commands pass.
- No stubs, skipped tests, or unrun verification commands remain.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
