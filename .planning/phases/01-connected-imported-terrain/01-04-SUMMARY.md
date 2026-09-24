---
phase: 01-connected-imported-terrain
plan: 04
subsystem: terrain-domain
tags: [csharp, tdd, land-mask, river-modifier, deterministic-geometry, sqlite]

requires:
  - phase: 01-connected-imported-terrain
    provides: Fixed terrain roles, deterministic texture recipes, durable SQLite snapshots, and connected tracer from Plans 01 and 03
provides:
  - Foreground-only Land Add/Subtract strokes with distinct Edged polygon and Round soft footprints
  - Versioned editable river modifier with per-point widths, bank softness, stable identity, and explicit renderer segments
  - Deterministic coverage composition, raster transform, contour tie, invalidation, and SQLite replay contracts
affects: [01-05-history, 01-06-rendering, 01-08-ui, 03-water-paths-and-labels]

actuals:
  tokens: 13051
  tasks: 2
  commits: 4
  plan_head_before: b876f504313e23a61f52315be3a5708696004b40

tech-stack:
  added: []
  patterns:
    - Resolved document-space geometry is persisted independently from mutable UI presets
    - Foreground coverage evaluates ordered Land strokes before the non-destructive river modifier
    - Every geometry edit rejects non-finite or out-of-range input before revision creation

key-files:
  created:
    - tests/Mapwright.ContractTests/MaskRiverContracts.cs
    - .planning/phases/01-connected-imported-terrain/01-04-TASK1-RED.json
    - .planning/phases/01-connected-imported-terrain/01-04-TASK2-RED.json
  modified:
    - src/Mapwright.Domain/MapModel.cs
    - src/Mapwright.Domain/Commands.cs
    - src/Mapwright.Domain/Geometry.cs

key-decisions:
  - "Land diameter is inclusive from 1 through 4096 document pixels; Edged Smooth changes firm polygon geometry while Round Softness is the only Land alpha falloff."
  - "Coverage is sampled at output-pixel centres, the exact 0.5 contour tie is land, and document-length raster ties use midpoint-to-even rounding."
  - "River width is inclusive from 1 through 4096 document pixels, bank softness is normalized from 0 through 1, and widths interpolate linearly between centreline points."
  - "River subtraction is evaluated after every Land stroke, so later Land Add commands cannot close an enabled channel."

patterns-established:
  - "Immutable edit snapshots: point/width/softness/toggle/deletion commands create successor rivers while preserving the prior snapshot for history."
  - "Conservative invalidation: river edits union old and new soft-bank bounds and rebuild coverage, distance, and composite dependencies."

requirements-completed: [MASK-01, WATR-01, HIST-04, REND-01]

coverage:
  - id: D1
    description: "Foreground-only Land Add/Subtract strokes persist deterministic Edged polygon and Round soft geometry without changing texture or layer opacity."
    requirement: MASK-01
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/MaskRiverContracts.cs#land strokes expose resolved footprint and composition contracts"
        status: pass
      - kind: integration
        ref: "./Scripts/run-contract-tests.ps1 (28 discovered, 28 passed)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Editable rivers retain stable identity, per-point widths, bank softness and enabled state while all point/profile/toggle/deletion edits invalidate old and new geometry."
    requirement: WATR-01
    verification:
      - kind: unit
        ref: "tests/Mapwright.ContractTests/MaskRiverContracts.cs#rivers expose editable width softness and modifier contracts"
        status: pass
      - kind: unit
        ref: "tests/Mapwright.ContractTests/MaskRiverContracts.cs#river point edits union old and new invalidation bounds"
        status: pass
    human_judgment: false
  - id: D3
    description: "Land and river geometry reopens identically through SQLite, crosses tile boundaries, and uses declared contour and raster rounding rules."
    requirement: HIST-04
    verification:
      - kind: integration
        ref: "tests/Mapwright.ContractTests/MaskRiverContracts.cs#land strokes reopen with identical resolved geometry; river subtraction remains ordered after later Land painting"
        status: pass
      - kind: e2e
        ref: "./Scripts/run-connected-phase1.ps1 -Case Tracer (24 assertions)"
        status: pass
    human_judgment: false

duration: 27 min
completed: 2026-09-23
status: complete
---

# Phase 1 Plan 04: Foreground Land Mask and Editable River Summary

**Deterministic Foreground Land strokes and a versioned per-point river modifier now compose through one replayable coverage model with explicit raster rules and conservative invalidation.**

## Performance

- **Duration:** 27 min
- **Started:** 2026-09-22T21:29:39Z
- **Completed:** 2026-09-22T21:56:33Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments

- Added persisted Land Add/Subtract gestures that target only Foreground coverage, with firm seeded Edged polygons, corner-rounding Smooth, and independently soft Round footprints.
- Added a stable river modifier with linear per-point width profiles, bank softness, enable state, explicit renderer segments, and atomic create/point/width/softness/toggle/delete commands.
- Defined document-to-output pixel-centre transforms, inclusive numeric boundaries, a land-at-0.5 contour tie, midpoint-to-even length rounding, tile-crossing fixtures, and SQLite reopen evidence.
- Proved enabled river subtraction remains last in coverage composition, including after a later Land Add and after durable reopen.

## Task Commits

Each task followed an explicit RED/GREEN gate and was committed atomically:

1. **Task 1 RED: Foreground Land mask contract** - `deeaf6d` (test)
2. **Task 1 GREEN: Deterministic Land strokes and coverage composition** - `be1f9aa` (feat)
3. **Task 2 RED: Editable river modifier contract** - `fa2fbeb` (test)
4. **Task 2 GREEN: Per-point river geometry and commands** - `74b42a5` (feat)

**Plan metadata:** committed with this summary.

## TDD Gate Compliance

| Task | RED evidence | GREEN verification | Refactor |
|------|--------------|--------------------|----------|
| Foreground Land mask | `01-04-TASK1-RED.json` → `RED_EVIDENCE_OK` | 24/24 contracts | Not needed |
| Editable river modifier | `01-04-TASK2-RED.json` → `RED_EVIDENCE_OK` | 28/28 contracts plus 24-assertion connected tracer | Not needed |

Both target tests failed on planned missing-behavior assertions before production edits, and both GREEN commits passed the complete suite.

## Files Created/Modified

- `tests/Mapwright.ContractTests/MaskRiverContracts.cs` - Land/river footprint, ordering, boundary, invalidation, tile-crossing, deletion, and reopen contracts.
- `.planning/phases/01-connected-imported-terrain/01-04-TASK1-RED.json` - Verified intentional RED evidence for the Land contract.
- `.planning/phases/01-connected-imported-terrain/01-04-TASK2-RED.json` - Verified intentional RED evidence for the river contract.
- `src/Mapwright.Domain/MapModel.cs` - Resolved Land recipes/strokes, coverage math, river profiles/segments, and modifier-last evaluation.
- `src/Mapwright.Domain/Commands.cs` - Foreground Land command and atomic river create/edit/toggle/delete operations.
- `src/Mapwright.Domain/Geometry.cs` - Segment projection and declared document/output raster transform.

## Decisions Made

- Chose a 1–4096 document-pixel domain for both Land diameter and river width, aligning bounded geometry with the 16K document ceiling rather than adopting the UI mockup's proposed 2048-pixel river limit.
- Kept Edged alpha binary: Roughness varies the seeded outline and Smooth rounds it toward the radius; only Round Softness creates fractional coverage.
- Declared output pixel centres as the sampling transform, `coverage >= 0.5` as the contour tie, and midpoint-to-even as the document-length raster rounding policy.
- Used normalized bank softness as an outer falloff proportional to local half-width, with linear interpolation of widths along each centreline segment.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Located the absent plan-listed mask/river test equivalent and created the intended provider**
- **Found during:** Task 1 read-first gate
- **Issue:** `tests/Mapwright.ContractTests/MaskRiverContracts.cs` was absent; the existing coverage/river baseline lived in `tests/Mapwright.ContractTests/Program.cs`.
- **Fix:** Read the baseline fixture and reflection-discovered provider contract in `Program.cs`, then created the plan-named `MaskRiverContracts.cs` provider for the new TDD fixtures.
- **Files modified:** `tests/Mapwright.ContractTests/MaskRiverContracts.cs`
- **Verification:** Contract discovery found 28 cases and all 28 passed.
- **Committed in:** `deeaf6d`, expanded in `be1f9aa`, `fa2fbeb`, and `74b42a5`

---

**Total deviations:** 1 auto-fixed (1 blocking).
**Impact on plan:** The deviation restored the exact planned test artifact without changing product scope or removing the existing baseline cases.

## Issues Encountered

- The sandbox initially denied the pinned .NET child process access to its workspace-local NuGet configuration scratch directory. Re-running the same project script with the required process permission produced the intentional RED result and all later GREEN/final passes.
- The first SQLite replay assertion compared `ImmutableArray` record identity instead of its elements; the assertion was corrected to compare stroke ID, recipe, operation, and every persisted sample.

## Known Stubs

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Ready for Plan 05 history work to persist cursor/branch operations over native Land and river commands.
- Ready for Plan 06 rendering to consume `ResolvedLandStrokes`, `River.GeometrySegments()`, the modifier-last coverage oracle, and the declared raster transform.
- No renderer/UI surface was added here; visual cursor feedback and connected mask/river GPU evaluation remain assigned to their later plans.

## Self-Check: PASSED

- All six plan-created/modified task artifacts exist; the previously absent `MaskRiverContracts.cs` now exists.
- Task commits `deeaf6d`, `be1f9aa`, `fa2fbeb`, and `74b42a5` exist in RED/GREEN order.
- Both RED evidence records return `RED_EVIDENCE_OK`.
- `./Scripts/run-contract-tests.ps1` reported 28 passed, 0 failed, 28 discovered.
- `./Scripts/run-connected-phase1.ps1 -Case Tracer` reported 24 assertions and a validated 3780×4097 PNG.
- Stub scan found only intentional nullable compatibility/default fields; no TODO, FIXME, placeholder, skipped test, or UI-flowing hardcoded empty value was introduced.
- No tracked file was deleted, and no unplanned network, authentication, file-access, or schema trust boundary was introduced.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
