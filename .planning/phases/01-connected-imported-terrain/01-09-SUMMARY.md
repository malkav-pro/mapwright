---
phase: 01-connected-imported-terrain
plan: 09
subsystem: ui
tags: [godot, gestures, terrain-editing, tdd, visual-evidence]

requires:
  - phase: 01-connected-imported-terrain/01-08
    provides: Connected native editor shell, terrain rows, and durable edit/history sessions
provides:
  - Cancellable one-command gesture lifecycle with pointer-centred navigation and scoped shortcuts
  - Native Texture Brush, Land, and River inspectors with explicit target refusal
  - Distinct D-28 coverage, texture, and Layer-opacity cues verified at 100% and 150%
affects: [01-10, terrain-editing, history, visual-uat]

actuals:
  tokens: 28983
  tasks: 3
  commits: 8
plan_head_before: 8396b2c333ec7029873b958dd78030c89dabbb9e

tech-stack:
  added: []
  patterns:
    - One transient GestureController lifecycle produces at most one durable command
    - Editing scope is expressed by semantic text plus distinct canvas or row geometry
    - Connected visual evidence is captured by isolated app processes at explicit display scales

key-files:
  created:
    - Scripts/App/GestureController.cs
    - Scripts/App/EditorControls.cs
    - .planning/phases/01-connected-imported-terrain/01-09-TASK1-RED.json
  modified:
    - Scripts/App/MapCanvas.cs
    - Scripts/App/Main.cs
    - Scripts/App/ConnectedUiSmoke.cs

key-decisions:
  - "GestureController owns transient samples, target binding, cancellation, and commit acknowledgement; MapCanvas presents it."
  - "Land coverage uses a filled footprint, Texture Brush uses unfilled ring/falloff/box/swatch geometry, and Layer opacity remains row-only."
  - "D-28 readouts lead with the affected property so their semantic identity survives constrained 150% layouts."

patterns-established:
  - "Gesture atomicity: validate target and revision at start/release, submit once, and discard the whole preview on interruption."
  - "Scope evidence matrix: capture Land, Texture, and Opacity at 100% and 150%, then inspect each frame against three explicit questions."

requirements-completed: [LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01, HIST-01, HIST-02, UIIN-01]

coverage:
  - id: D1
    description: "Pointer gestures commit exactly once or cancel without changing pixels, revision, or durable history; navigation and shortcuts retain their approved scope."
    requirement: HIST-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case Gestures (21 assertions)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Texture Brush, Land, and River expose domain-valid controls, preserve target/settings state, and refuse locked or hidden targets without rerouting."
    requirement: TERR-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case ToolPanels (17 assertions)"
        status: pass
    human_judgment: false
  - id: D3
    description: "Coverage, texture colour/intensity, and whole-layer opacity have distinct immediate cues at 100% and 150%, and overlays stay out of exports."
    requirement: UIIN-01
    verification:
      - kind: automated_ui
        ref: "./Scripts/run-connected-phase1.ps1 -Case ScopeCues (21 assertions)"
        status: pass
      - kind: automated_ui
        ref: "artifacts/ui-smoke/scope-{land,texture,opacity}-{100,150}.png"
        status: pass
    human_judgment: true
    rationale: "The connected case proves semantics, dimensions, and export exclusion; judging whether weak coverage, pale texture, and low opacity remain visually distinguishable requires inspection of all six frames."

duration: 54 min
completed: 2026-09-23
status: complete
---

# Phase 01 Plan 09: Editing Interactions and Scope Cues Summary

**Atomic terrain gestures, domain-valid native inspectors, and visually distinct coverage, texture, and Layer-opacity feedback on the connected Godot canvas**

## Performance

- **Duration:** 54 min
- **Started:** 2026-09-23T01:34:50Z
- **Completed:** 2026-09-23T02:29:00Z
- **Tasks:** 3
- **Files modified:** 6

## Accomplishments

- Added a bounded GestureController that turns pointer-down/samples/release into one durable command and discards complete previews on Escape, capture loss, window deactivation, or modal opening.
- Added native Texture Brush, Land, and River inspectors with remembered targeting, domain-derived numeric behavior, explicit locked/hidden refusal, and command-backed edits.
- Added and visually reviewed the six-frame D-28 evidence matrix: filled Land coverage, unfilled Texture geometry and identity, and row-only Layer opacity at 100% and 150%.

## Task Commits

Each task was committed atomically:

1. **Commit or cancel one complete gesture at a time — RED** - `60cf8e8` (test)
2. **Commit or cancel one complete gesture at a time — GREEN** - `b31ab74` (feat)
3. **Expose Texture Brush, Land and River controls with correct targeting** - `f6220f7` (feat)
4. **Verify the three editing scopes visually at supported scales** - `d438185` (feat)
5. **Keep scope evidence anchored and readable** - `1da2510` (fix; concurrent Plan 01-09 follow-up)
6. **Preserve compact control labels** - `96cb4a1` (fix)

The measured ledger contains eight commits: the six Plan 01-09 task/follow-up commits above plus concurrent render-capacity work (`363adf8`) merged by `4439685`. The render commits were preserved and are not claimed as Plan 01-09 task work.

## Files Created/Modified

- `Scripts/App/GestureController.cs` - Bounded transient gesture, target validation, pan/zoom, shortcut routing, and revision/presentation identity.
- `Scripts/App/EditorControls.cs` - Texture/Land/River state, numeric fields, native panels, targeting refusal, and D-28 semantic descriptors.
- `Scripts/App/MapCanvas.cs` - Connected command construction, cancellation, navigation, and single-Control scope rendering.
- `Scripts/App/Main.cs` - Scope-frame capture routing.
- `Scripts/App/ConnectedUiSmoke.cs` - Gesture, panel, and scope connected cases plus deterministic evidence capture.
- `.planning/phases/01-connected-imported-terrain/01-09-TASK1-RED.json` - Validated intentional RED evidence for the gesture behavior.

## Decisions Made

- Transient gesture state belongs outside the authoritative project; only a validated release produces a command and history row.
- Tool selection retains the Texture target, while Land and River bind explicitly to Foreground coverage/mask semantics.
- Scope readouts put `Coverage`, `Texture colour/intensity`, or `Layer opacity` first, backed by non-colour geometry or row placement.
- Tiny brush rings collapse below six screen pixels to a crosshair while keeping the numeric readout.

## TDD Gate Compliance

- **RED:** `60cf8e8` added the `Gestures` behavior case before GestureController existed; the target case failed for the missing controller and the normalized evidence record returned `RED_EVIDENCE_OK`.
- **GREEN:** `b31ab74` implemented the minimal controller/canvas lifecycle and made all 21 behavior assertions pass.
- **REFACTOR:** No separate refactor commit was required; later tasks extended the already-green public behavior without changing the RED/GREEN contract.

## D-28 Visual Review

All six final frames were inspected at original resolution, not inferred from constants or process exits.

| Scale | State | Active tool/control | Target | Affected property | Result |
|---|---|---|---|---|---|
| 100% | Weak Land coverage | Land tool · Subtract | Foreground | Coverage | PASS — translucent filled footprint, outline/centre, and explicit readout |
| 100% | Pale texture | Texture Brush | Foreground | Texture colour/intensity | PASS — unfilled outer ring, dashed falloff/box, swatch, and explicit readout |
| 100% | Low opacity | Layers row | Foreground | Layer opacity · 22% | PASS — row/status only; no canvas brush cue |
| 150% | Weak Land coverage | Land tool · Subtract | Foreground | Coverage | PASS — semantic lead text and filled geometry remain readable |
| 150% | Pale texture | Texture Brush | Foreground | Texture colour/intensity | PASS — semantic lead text, unfilled geometry, and swatch remain readable |
| 150% | Low opacity | Layers row | Foreground | Layer opacity · 22% | PASS — target, percentage, and row-only placement remain readable |

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- The first scope-capture attempt ran under the parent process's honest inspection-only state and therefore had no MapCanvas. The final capture path uses separate app processes with the selected Vulkan device and retains a CPU-backed evidence fallback without weakening normal startup admission.
- Six sequential capture children exceeded the connected-run time envelope. The same isolated matrix is now captured concurrently, and the final case passes all 21 assertions.
- Concurrent render-capacity commits entered the branch during execution, followed by the Plan-relevant `1da2510` scope-readability fix. All were preserved; the render commits are excluded from Plan 01-09 ownership and the relevant UI fix was independently tested before close-out.
- Offline restore continued to emit the existing `NU1900` vulnerability-feed warning, and Godot continued to emit the existing Windows root-certificate-store warning; builds and connected cases passed.

## User Setup Required

None - no external service configuration required.

## Verification

- `./Scripts/run-connected-phase1.ps1 -Case Gestures` — PASS, 21 assertions.
- `./Scripts/run-connected-phase1.ps1 -Case ToolPanels` — PASS, 17 assertions.
- `./Scripts/run-connected-phase1.ps1 -Case ScopeCues` — PASS, 21 assertions and six valid PNG captures.
- `./Scripts/run-connected-phase1.ps1 -ListCases` — PASS; Gestures, ToolPanels, and ScopeCues discovered.
- `git diff --check` — PASS.
- D-28 visual review — PASS after original-resolution inspection of all 100% and 150% frames.

## Next Phase Readiness

- The connected editor now exposes the gesture sequence/revision/presentation identifiers needed by Plan 01-10 latency measurement.
- No Plan 01-09 blocker remains.

## Self-Check: PASSED

- All six Plan 01-09 created/modified artifacts exist.
- RED and GREEN TDD commits plus Task 2 and Task 3 commits are present.
- All task acceptance criteria and plan-level connected verification passed.
- Shared `STATE.md` and `ROADMAP.md` were not modified.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
