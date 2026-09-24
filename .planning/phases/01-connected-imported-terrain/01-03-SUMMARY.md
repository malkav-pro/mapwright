---
phase: 01-connected-imported-terrain
plan: 03
subsystem: terrain-domain
tags: [csharp, tdd, terrain-roles, texture-brush, deterministic-replay, sqlite]

requires:
  - phase: 01-connected-imported-terrain
    provides: Real flattened import, durable EditSession, SQLite revision snapshots, and connected tracer from Plan 01
provides:
  - Fixed Background/Foreground state commands with guarded painting and deterministic solo semantics
  - Versioned immutable texture recipes with content-addressed assets, seeded dabs, and resolved taper samples
  - Backward-compatible snapshot defaults and SQLite reopen contracts for new terrain and texture state
affects: [01-04-mask-river, 01-05-history, 01-06-rendering, 01-08-ui]

actuals:
  tokens: 13895
  tasks: 2
  commits: 4
  plan_head_before: 30c5d55a990fee96dd03966cb0bd82201e67647a

tech-stack:
  added: []
  patterns:
    - Fixed terrain roles are command targets while stable layer IDs remain stored identities
    - Repeated state requests return explicit non-committing DocumentChange no-ops
    - Resolved texture recipes persist every effective parameter plus versioned deterministic algorithms

key-files:
  created:
    - .planning/phases/01-connected-imported-terrain/01-Q2-DECISION.md
    - .planning/phases/01-connected-imported-terrain/01-Q3-DECISION.md
    - tests/Mapwright.ContractTests/BrushContracts.cs
  modified:
    - src/Mapwright.Domain/MapModel.cs
    - src/Mapwright.Domain/Commands.cs
    - src/Mapwright.Domain/Geometry.cs
    - src/Mapwright.Application/EditSession.cs

key-decisions:
  - "Solo filters the two-role visibility set: hidden remains hidden, lock is independent, either role may solo, and both solo restores each row's own Visible result."
  - "Texture recipes use a seven-family capability inventory backed by caller-supplied SHA-256 texture identity and bounded normalized parameters rather than screenshot sample values."
  - "Taper v1 narrows both mouse-stroke ends over 20 percent of stroke length with a 0.08 radius floor; seeded dab placement uses a fixed versioned integer algorithm."
  - "New solo and resolved-texture fields remain optional in storage format 2 so earlier snapshots reopen through explicit default semantics."

patterns-established:
  - "Guard below UI: role validation, stale revision rejection, hidden/locked paint refusal, and Foreground-only coverage are domain-command invariants."
  - "Replay data over preset lookup: preset ID is provenance while all tip, texture, transform, taper, seed, and algorithm values are immutable command data."

requirements-completed: [DOC-01, LAYR-01, TERR-01, TERR-02, HIST-04]

coverage:
  - id: D1
    description: "Exactly Background then Foreground support independent name, visibility, lock, solo, and opacity commands while prohibited role/order/coverage changes and stale requests fail closed."
    requirement: LAYR-01
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/BrushContracts.cs#brush roles expose fixed terrain state commands; solo visibility; prohibited mutations; no-op state changes"
        status: pass
      - kind: integration
        ref: "./Scripts/run-contract-tests.ps1 (20 discovered, 20 passed)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Resolved texture recipes retain required tip families, texture identity and transforms, numeric bounds, taper, seed, and algorithm versions independently of mutable preset entries."
    requirement: TERR-01
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/BrushContracts.cs#texture recipes persist every resolved replay field; deterministic seeded dabs"
        status: pass
    human_judgment: false
  - id: D3
    description: "Texture strokes keep document-space samples and anchors across tile edges and target switches, narrow both ends only when tapered, and never alter Foreground coverage."
    requirement: TERR-02
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/BrushContracts.cs#texture strokes resolve taper in document space"
        status: pass
    human_judgment: false
  - id: D4
    description: "Earlier snapshots default new state safely, while resolved recipes and samples reopen byte-for-value through SQLite and the real imported tracer remains connected."
    requirement: HIST-04
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/BrushContracts.cs#brush roles reopen snapshots written before solo state; texture strokes reopen with identical resolved recipes"
        status: pass
      - kind: e2e
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer (24 assertions)"
        status: pass
    human_judgment: false

duration: 31 min
completed: 2026-09-22
status: complete
---

# Phase 1 Plan 03: Fixed Terrain Roles and Deterministic Texture Recipes Summary

**Two fixed terrain roles now enforce guarded state and paint commands, while immutable content-addressed texture recipes replay seeded, tapered document-space strokes identically after SQLite reopen.**

## Performance

- **Duration:** 31 min
- **Started:** 2026-09-22T20:47:39Z
- **Completed:** 2026-09-22T21:18:57Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments

- Added fixed-role rename, visibility, lock, solo, and layer-opacity commands with stale-revision rejection, explicit no-op behavior, stable IDs, and guarded hidden/locked paint targets.
- Added a complete versioned texture recipe and seven required preset families, with deterministic seeded dabs, bounded texture transforms, two-ended mouse taper, and document-space tile-independent samples.
- Preserved older storage-format-2 snapshots through safe defaults and proved resolved recipes, anchors, samples, and randomness survive SQLite reopen without touching coverage.

## Task Commits

Each task followed an explicit RED/GREEN gate and was committed atomically:

1. **Task 1 RED: Fixed terrain role contracts** - `181c1e6` (test)
2. **Task 1 GREEN: Fixed terrain role state and guards** - `92952f0` (feat)
3. **Task 2 RED: Resolved texture recipe contracts** - `3d621a0` (test)
4. **Task 2 GREEN: Deterministic resolved texture recipes** - `edf011c` (feat)

**Plan metadata:** committed with this summary.

## TDD Gate Compliance

| Task | RED evidence | GREEN verification | Refactor |
|------|--------------|--------------------|----------|
| Fixed terrain roles | `01-03-TASK1-RED.json` → `RED_EVIDENCE_OK` | 16/16 contracts plus 24-assertion connected tracer | Not needed |
| Resolved texture recipes | `01-03-TASK2-RED.json` → `RED_EVIDENCE_OK` | 20/20 contracts plus 24-assertion connected tracer | Not needed |

Both target tests failed on planned behavior assertions before production edits; both GREEN commits passed the complete suite.

## Files Created/Modified

- `.planning/phases/01-connected-imported-terrain/01-Q2-DECISION.md` - Measured texture bounds, inventory, taper, and deterministic replay decision.
- `.planning/phases/01-connected-imported-terrain/01-Q3-DECISION.md` - Solo/visibility truth table and rejected alternatives.
- `.planning/phases/01-connected-imported-terrain/01-03-TASK1-RED.json` - Verified Task 1 intentional RED evidence.
- `.planning/phases/01-connected-imported-terrain/01-03-TASK2-RED.json` - Verified Task 2 intentional RED evidence.
- `src/Mapwright.Domain/MapModel.cs` - Terrain solo state, resolved texture recipe/catalog, deterministic dabs, taper samples, and compatibility defaults.
- `src/Mapwright.Domain/Commands.cs` - Fixed terrain state commands, paint guards, explicit no-ops, and resolved texture command targeting a role.
- `src/Mapwright.Domain/Geometry.cs` - Finite document-coordinate validation and distance calculation for taper resolution.
- `src/Mapwright.Application/EditSession.cs` - Non-committing, non-rendering acknowledgement path for repeated state requests.
- `tests/Mapwright.ContractTests/BrushContracts.cs` - Role, paint-target, bounds, replay, tile-edge, target-switch, and reopen contracts.

## Decisions Made

- Interpreted solo as a persisted visibility filter, not a destructive visibility edit or last-click-wins mode; both solo buttons may be active and hidden rows remain hidden.
- Chose normalized, finite texture parameter domains tied to document/tile geometry and rejected screenshot examples as defaults.
- Kept preset identity as provenance only; replay uses stored resolved values and a real SHA-256 asset identity.
- Kept storage format 2 and added optional defaulted fields so old revisions are read rather than rewritten in place.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Prevented explicit no-op commands from reaching durable storage or render scheduling**
- **Found during:** Task 1 (Enforce two terrain roles and paint-target guards)
- **Issue:** `DocumentChange.IsNoOp` alone would still flow through `EditSession`, where the repository correctly requires a successor revision and would reject or fabricate a commit.
- **Fix:** Added an `EditSession` short-circuit that acknowledges the unchanged revision without calling the repository or renderer.
- **Files modified:** `src/Mapwright.Application/EditSession.cs`, `tests/Mapwright.ContractTests/BrushContracts.cs`
- **Verification:** `brush roles report no-op state changes` asserted unchanged revision and zero repository/render events.
- **Committed in:** `92952f0`

---

**Total deviations:** 1 auto-fixed (1 missing critical).
**Impact on plan:** The fix is necessary to make the plan's idempotency contract true through the existing application boundary; it adds no new product scope.

## Issues Encountered

- The first restore inherited an MSBuild/NuGet scratch lock path from a sibling worktree process. Verification was isolated with build-server reuse disabled; no sibling files or processes were modified.
- Offline restore continued to emit the pre-existing `NU1900` vulnerability-feed advisory; cached pinned packages restored and all builds/tests passed.
- Headless Godot continued to emit the Windows root-certificate-store warning; the offline connected tracer completed all 24 assertions.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for later mask, history, renderer, and UI plans to consume fixed role-state commands and resolved texture recipes.
- The Q3 under-Background composition branch remains intentionally gated in Plan 06; this plan records only solo semantics.
- Renderer evaluation of new resolved texture strokes remains assigned to the later rendering plan; the persistent domain and command contract is complete.

## Self-Check: PASSED

- All seven key created/modified implementation artifacts exist.
- Task commits `181c1e6`, `92952f0`, `3d621a0`, and `edf011c` exist on the worktree branch in RED/GREEN order.
- Both Q2/Q3 decision artifacts contain their required decision and evidence lines.
- `./Scripts/run-contract-tests.ps1` reported all 20 discovered contracts passing.
- `./Scripts/run-connected-phase1.ps1 -Case Tracer` reported 24 assertions and a validated 3780×4097 PNG.
- Coverage classification found four deliverables, all backed by passing automated evidence with no schema errors.
- Stub scan found no TODO, FIXME, placeholder, skipped-test, or unfinished implementation marker.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-22*
