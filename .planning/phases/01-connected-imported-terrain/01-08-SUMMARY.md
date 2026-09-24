---
phase: 01-connected-imported-terrain
plan: 08
subsystem: ui
tags: [godot, desktop-ui, import, history, export, recovery, offline-fonts]

requires:
  - phase: 01-02
    provides: bounded immutable .ink import and recovery review data
  - phase: 01-05
    provides: durable EditSession save queue and two-role terrain model
  - phase: 01-06
    provides: connected terrain graph and resource-ledger facts
  - phase: 01-07
    provides: virtualized HistorySession, frozen export, and recovery facts
provides:
  - approved offline Godot theme, typography, focus states, and accessible icon atlas
  - explicit recent/open/import entry with two unselected recovery modes and canonical destinations
  - responsive durable editor shell with two terrain rows, bounded History, frozen export, recovery, and storage views
affects: [01-09, phase-1-uat, desktop-shell, editing-controls]

actuals:
  tokens: 26932
  tasks: 3
  commits: 3
  plan_head_before: 07bc8de9f8d8109b3ed25d3c393e8402043d331f

tech-stack:
  added: [IBM Plex Sans, IBM Plex Mono, Spectral SemiBold, SVG icon atlas]
  patterns: [connected-case UI smoke, durable-session-bound shell, fail-closed backend inspection, frozen-revision job identity]

key-files:
  created:
    - Scripts/App/EditorTheme.cs
    - Scripts/App/ConnectedUiSmoke.cs
    - Assets/UI/Icons.svg
    - Assets/UI/Fonts/IBMPlexSans-Variable.ttf
    - Assets/UI/Fonts/IBMPlexMono-Variable.ttf
    - Assets/UI/Fonts/Spectral-SemiBold.ttf
    - Assets/UI/Fonts/OFL.txt
  modified:
    - Scripts/App/Main.cs
    - Scenes/Main.tscn

key-decisions:
  - "Commit the approved OFL fonts locally and load them directly so the visual system remains deterministic and offline in headless and desktop runs."
  - "Bind save, layer, History, recovery, storage, and export presentation to existing durable application services instead of keeping parallel UI-only state."
  - "Keep MASK-01 semantics explicit: Land owns Foreground coverage, Texture Brush owns texture colour/intensity, and Layer opacity exists only on terrain rows."
  - "Fail closed to an inspection/recovery shell when Godot cannot report usable render-resource facts; never invent GPU values or enable unsupported work."

patterns-established:
  - "Connected UI cases: each production UI surface has a discoverable nonzero-assertion smoke case."
  - "Responsive shell policy: 1920 desktop metrics and a scrollable compressed policy for 1366×768 or 150% scaling."
  - "Job identity: import, save, export, History, and cache work retain distinct targets and frozen revisions."

requirements-completed: [DOC-01, DOC-02, IMPT-01, IMPT-02, IMPT-03, LAYR-01, HIST-01, HIST-02, EXPT-01, UIIN-01, DURA-02, REND-05]

coverage:
  - id: D1
    description: "Approved offline theme, typography, focus states, and accessible icon atlas are installed."
    requirement: UIIN-01
    verification:
      - kind: automated_ui
        ref: "./Scripts/run-connected-phase1.ps1 -Case ThemeSmoke (19 assertions)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Project entry and source-preserving import review expose recent/open/import, two unselected modes, canonical destinations, cancellation, and independent job identity."
    requirement: IMPT-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case EntryImportUi (51 assertions)"
        status: pass
    human_judgment: false
  - id: D3
    description: "The editor shell exposes exactly two terrain rows, durable save/History, frozen export, recovery, cache, backend, and live-resource state without future-phase controls."
    requirement: HIST-01
    verification:
      - kind: integration
        ref: "./Scripts/run-connected-phase1.ps1 -Case ShellStates (23 assertions)"
        status: pass
    human_judgment: false
  - id: D4
    description: "Desktop, compact, and 150% layouts preserve reachable controls, approved copy, focusable regions, and the MASK-01 coverage/texture/opacity distinction."
    requirement: UIIN-01
    verification:
      - kind: automated_ui
        ref: "artifacts/ui-smoke/shell-1920x1080.png"
        status: pass
      - kind: automated_ui
        ref: "artifacts/ui-smoke/shell-1366x768.png"
        status: pass
      - kind: automated_ui
        ref: "artifacts/ui-smoke/shell-150-percent.png"
        status: pass
    human_judgment: true
    rationale: "Frame capture proves the rendered states exist, but layout adequacy and visual fidelity still require visual judgment."

duration: 1h 7m
completed: 2026-09-23
status: complete
---

# Phase 01 Plan 08: Connected Desktop Shell Summary

**Offline Godot project entry, recovery review, and responsive durable editor shell backed by real import, save, History, export, recovery, storage, and render-resource state**

## Performance

- **Duration:** 1h 7m
- **Started:** 2026-09-23T00:01:00Z
- **Completed:** 2026-09-23T01:08:00Z
- **Tasks:** 3
- **Files modified:** 9

## Accomplishments

- Installed the approved offline typography, colour, component-state, focus, and SVG icon system with deterministic local font assets.
- Replaced automatic fixture startup with explicit recent/open/import entry and a source-preserving two-mode recovery review that publishes only after a durable initial revision.
- Connected a responsive native editor shell to the existing EditSession, HistorySession, DocumentPngExport, recovery, cache, and resource-ledger services while preserving the three-way MASK-01 distinction.

## Task Commits

Each task was committed atomically:

1. **Install the approved offline Godot visual system** - `d325539` (feat)
2. **Open and import through an explicit recovery review** - `8fbc5f1` (feat)
3. **Show durable project, layer, history and export state in the editor shell** - `2aa524f` (feat)

## Files Created/Modified

- `Scripts/App/EditorTheme.cs` - Approved palette, local typography, density, component states, focus treatment, and ThemeSmoke registration.
- `Scripts/App/ConnectedUiSmoke.cs` - Production entry/import/shell UI plus EntryImportUi and ShellStates integration cases and deterministic frame capture.
- `Scripts/App/Main.cs` - Routes normal startup to the connected UI, frame capture, and coordinated session/job disposal.
- `Scenes/Main.tscn` - Establishes the focusable 1024×640 minimum application surface.
- `Assets/UI/Icons.svg` - Accessible 24-grid symbol atlas for Phase 1 actions and states.
- `Assets/UI/Fonts/*` - Pinned OFL IBM Plex Sans/Mono and Spectral SemiBold resources with license/provenance.

## Decisions Made

- Font assets are committed under OFL and loaded directly by Godot, keeping headless verification and desktop use offline and deterministic.
- The shell reads and mutates durable application sessions; it does not mirror revision, save, History, recovery, or export truth in a second UI state model.
- Coverage remains attached to the Land cursor/Foreground land mask, texture colour and intensity remain attached to Texture Brush, and whole-layer opacity remains a row-only control.
- Missing usable backend measurements produce an honest inspection/recovery surface with live RAM facts and disabled editing/export, not guessed resource data.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

- Godot's headless resource loader does not import new TTFs during the smoke run. The theme now uses `FontFile.LoadDynamicFont` against the committed files, preserving the planned offline asset contract.
- This Windows host reports no usable device-memory value through Godot even with the Vulkan device present. The captured UI therefore exercised the required inspection-only exceptional state. No resource value was invented.
- Offline restore emitted the existing `NU1900` vulnerability-feed warning, and Godot emitted the existing Windows root-certificate-store warning; builds and all connected cases passed.

## User Setup Required

None - no external service configuration required.

## Verification

- `./Scripts/run-connected-phase1.ps1 -Case ThemeSmoke` — PASS, 19 assertions.
- `./Scripts/run-connected-phase1.ps1 -Case EntryImportUi` — PASS, 51 assertions.
- `./Scripts/run-connected-phase1.ps1 -Case ShellStates` — PASS, 23 assertions.
- `./Scripts/run-connected-phase1.ps1 -ListCases` — PASS; ThemeSmoke, EntryImportUi, and ShellStates discovered.
- Captured and inspected `artifacts/ui-smoke/shell-1920x1080.png`, `shell-1366x768.png`, and `shell-150-percent.png` against UI-SPEC.

## Next Phase Readiness

- Ready for 01-09 to complete the on-canvas D-28 cursor rendering and interaction details using the shell's explicit coverage/texture/Layer-opacity state.
- No blocker remains for the next plan.

## Self-Check: PASSED

- All seven created files and both modified files exist.
- All three task commits exist on the agent worktree branch.
- Task acceptance gates and plan-level smoke commands pass.
- STATE.md and ROADMAP.md were not modified in this worktree.

---
*Phase: 01-connected-imported-terrain*
*Completed: 2026-09-23*
