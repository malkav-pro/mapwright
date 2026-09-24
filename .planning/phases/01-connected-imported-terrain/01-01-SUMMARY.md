---
phase: 01-connected-imported-terrain
plan: 01
subsystem: connected-terrain
tags: [godot, sqlite, ink-import, content-addressed-blobs, png, contract-tests]

requires: []
provides:
  - Real flattened .ink import through one schema-v2 SQLite project and immutable blob store
  - Durable Foreground texture command with idempotent Save and fresh-process reopen
  - Shared revision-keyed viewport and row-streamed PNG evaluation
  - Discoverable connected and contract case registries with fail-closed dispatch
affects: [01-02-editable-import, 01-03-brushes, 01-05-history, 01-06-rendering, 01-07-export]

actuals:
  tokens: 29066
  tasks: 2
  commits: 2
  plan_head_before: 7fed116a5c03240542fe4b3ec14bd9c338cc88ca

tech-stack:
  added: []
  patterns:
    - SQLite revision snapshots reference verified SHA-256 source and preview blobs
    - Connected case and contract providers register through reflection-discovered interfaces
    - One immutable render frame supplies both canvas sampling and streaming PNG rows

key-files:
  created:
    - Scripts/Rendering/ConnectedTerrainGraph.cs
    - Scripts/run-connected-phase1.ps1
  modified:
    - Scripts/App/Main.cs
    - Scripts/App/MapCanvas.cs
    - src/Mapwright.Domain/MapModel.cs
    - src/Mapwright.Domain/Commands.cs
    - src/Mapwright.Application/EditSession.cs
    - src/Mapwright.Infrastructure/SqliteProjectRepository.cs
    - tests/Mapwright.ContractTests/Program.cs

key-decisions:
  - "Schema version 2 stores the imported source identity and preview reference in SQLite while content-addressed blobs preserve and verify the bytes."
  - "The flattened tracer fixes Background below Foreground, keeps Background locked, and allows River to be absent instead of inventing dummy geometry."
  - "The tracer uses the authorized unstyled rendering branch; reliable outer-coast classification remains gated on the later Q5 evidence fixture."
  - "Connected and contract runners discover providers reflectively and fail on unknown, duplicate, unimplemented, or zero-assertion cases."

patterns-established:
  - "Durable publish order: persist source/blob references and revision before exposing acknowledgement or render work."
  - "Revision identity: reopen and export require an exact project ID and revision rather than silently falling back to current state."

requirements-completed: [DOC-01, DOC-02, IMPT-01, LAYR-01, TERR-01, EXPT-01, DURA-01, REND-01]

coverage:
  - id: D1
    description: "A real flattened .ink import accepts one durable Foreground texture gesture, reopens in a fresh process, and produces matching viewport and PNG evaluation."
    requirement: DOC-01
    verification:
      - kind: e2e
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer (24 assertions)"
        status: pass
    human_judgment: false
  - id: D2
    description: "One schema-v2 SQLite authority preserves and verifies the original source and preview blobs while rejecting missing blobs and wrong revisions."
    requirement: DURA-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer#source/revision/blob failure gates"
        status: pass
    human_judgment: false
  - id: D3
    description: "Contract and connected runners discover named cases and fail closed on absent handlers, duplicates, unknown names, and zero assertions."
    requirement: REND-01
    verification:
      - kind: integration
        ref: "./Scripts/run-contract-tests.ps1 (10 discovered, 10 passed)"
        status: pass
      - kind: other
        ref: "./Scripts/run-connected-phase1.ps1 -Case __UnknownCaseMustFail__ (exit 2)"
        status: pass
    human_judgment: false
  - id: D4
    description: "The import review presents the preserved preview and flattened-mode warning before project creation, and canvas release submits the gesture through EditSession."
    requirement: IMPT-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer#recovery-selection-and-command-order"
        status: pass
    human_judgment: true
    rationale: "The state transition is asserted, but visual legibility and interaction quality of the native Godot controls require later UI UAT."

duration: 31 min
completed: 2026-09-22
status: complete
---

# Phase 1 Plan 01: Connected Tracer Summary

**Real flattened `.ink` import now crosses verified blob storage, durable SQLite editing, exact-revision reopen, shared viewport evaluation, and validated PNG publication.**

## Performance

- **Duration:** 31 min
- **Started:** 2026-09-22T20:03:45Z
- **Completed:** 2026-09-22T20:35:08Z
- **Tasks:** 2
- **Files modified:** 14

## Accomplishments

- Connected the real 74,541,363-byte `.ink` fixture to one schema-v2 SQLite project with a locked flattened Background, empty Foreground, and SHA-256-verified source/preview blobs.
- Routed a Foreground texture gesture through `EditSession`, preserved revision 1 across repeated Save, reopened that exact revision in a fresh Godot process, and matched the viewport RGBA hash before publishing a validated 3780×4097 PNG.
- Added fail-closed connected-case and contract-provider discovery; the final runs reported 24 connected assertions and 10 discovered/10 passing contracts.
- Added recovery review controls that begin unselected, explain baked flattened coasts, and enable project creation only after flattened mode is selected; visual UAT remains for the later UI verification pass.

## Task Commits

Each task was committed atomically:

1. **Task 1: Prove the real flattened import through durable edit, reopen, viewport and PNG** - `6ab14ac` (feat)
2. **Task 2: Adapt the contract runner to the two-role connected baseline** - `0ab28bc` (test)

**Plan metadata:** committed with this summary.

## Files Created/Modified

- `Scripts/Rendering/ConnectedTerrainGraph.cs` - Evaluates one immutable revision for canvas samples and bounded PNG rows.
- `Scripts/run-connected-phase1.ps1` - Uses pinned runtimes and the byte-identical configured fixture to dispatch named connected cases.
- `Scripts/App/Main.cs` - Adds recovery review, connected project creation, case registry, real tracer, save/reopen/export, and fresh-process worker.
- `Scripts/App/MapCanvas.cs` - Converts pointer-release gestures into durable texture commands and renders acknowledged snapshots.
- `src/Mapwright.Domain/MapModel.cs` - Defines fixed terrain roles, optional river, import references, stroke kinds, and storage format version 2.
- `src/Mapwright.Domain/Commands.cs` - Adds texture-only commands and optional-river guards.
- `src/Mapwright.Application/EditSession.cs` - Adds revision-preserving durable Save verification.
- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` - Publishes and validates imported blobs/source references and loads exact revisions.
- `tests/Mapwright.ContractTests/Program.cs` - Replaces four-layer fixtures with two roles and discovers provider classes.
- `Scripts/run-contract-tests.ps1` - Resolves pinned read-only runtimes from the source checkout while keeping transient state in the worktree.

## Decisions Made

- Made SQLite plus its blob directory authoritative for connected projects; the legacy `ProjectStore` manifest is not written on this path.
- Kept the imported flattened preview locked as Background and did not invent a mandatory river or additional terrain identity.
- Used no generated coastline styling in this tracer because coast-versus-inland-bank separation is not yet proven; Q5 retains the explicit evidence gate.
- Required exact project/revision selection and hash verification on every connected reopen, including the child-process export worker.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added an explicit idempotent Save application seam**
- **Found during:** Task 1
- **Issue:** The plan required repeated Save to preserve the revision, but `EditSession` exposed only mutating command execution.
- **Fix:** Added `SaveAsync` to serialize behind pending commits, reload the durable project, verify the same revision, and return without creating a command.
- **Files modified:** `src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Application/Ports.cs`
- **Verification:** Connected tracer repeated Save at revision 1; contract runner repeated Save at revision 7.
- **Committed in:** `6ab14ac`

**2. [Rule 3 - Blocking] Resolved pinned runtimes from the source checkout in isolated worktrees**
- **Found during:** Task 2
- **Issue:** The isolated worktree intentionally has no `.tools` directory, so the existing contract runner could not execute its required verification.
- **Fix:** Resolve the shared Git checkout, use its pinned Godot/.NET/NuGet assets read-only, and keep CLI home/temp/cache state under ignored worktree artifacts.
- **Files modified:** `Scripts/run-contract-tests.ps1`, `Scripts/run-connected-phase1.ps1`
- **Verification:** Both required runners restored, built, and exited successfully from this worktree.
- **Committed in:** `0ab28bc` (contract runner); connected runner was included in `6ab14ac`.

---

**Total deviations:** 2 auto-fixed (1 missing critical, 1 blocking).
**Impact on plan:** Both changes enforce stated durability and real-fixture verification requirements without widening product scope.

## Issues Encountered

- Offline restore emitted the pre-existing `NU1900` vulnerability-feed advisory; all pinned packages restored from the available cache and builds/tests passed.
- Headless Godot emitted a Windows root-certificate-store warning; the offline connected cases did not use network trust and completed successfully.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for `01-02`: exact project/revision and blob contracts, fixed roles, runner discovery, and real source evidence are available.
- Editable recovered terrain remains deliberately unavailable until the real-role mapping decision in Plan 02; the flattened route is the only connected import mode in this tracer.
- Visual UAT for the import review/canvas controls and later full renderer/resource acceptance remain open by design.

## Self-Check: PASSED

- `Scripts/Rendering/ConnectedTerrainGraph.cs` and `Scripts/run-connected-phase1.ps1` exist.
- Task commits `6ab14ac` and `0ab28bc` exist on the worktree branch.
- `./Scripts/run-contract-tests.ps1` reported 10 passed, 0 failed, 10 discovered.
- `./Scripts/run-connected-phase1.ps1 -Case Tracer` reported 24 assertions and a validated 3780×4097 PNG.
- Unknown case exited 2; wrong revision, missing blob, duplicate, unimplemented, and zero-assertion gates all failed closed.
- Fixture remained byte-identical at SHA-256 `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-22*
