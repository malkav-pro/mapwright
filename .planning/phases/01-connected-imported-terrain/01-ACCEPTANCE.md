# Phase 1 Connected Acceptance Evidence

**Overall phase gate: PASS**

This report records observed evidence only. Export elapsed time is an observation and has no pass/fail threshold.
Scope note: this gate verifies the Phase 1 contract implemented before the uncommitted 2026-09-23 Phase 4 map-unit amendment. It does not verify that imported document geometry has a fixed 1,000-map-unit longest edge or that brush diameters use that new scale; the draft amendment needs a separate ownership/reconciliation decision.

## Execution commands

- `./Scripts/run-phase1-acceptance.ps1 -SelfTest`
- `./Scripts/run-phase1-acceptance.ps1 -Full`
- `./Scripts/run-phase1-acceptance.ps1 -ReportOnly` (regenerate this report from the saved full-run evidence without repeating measurements).
- `./Scripts/run-phase1-acceptance.ps1 -VerifyReport`
- Independent case form: `./Scripts/run-connected-phase1.ps1 -Case <name>` (the full runner launches each name in a fresh Godot process).

## Fixture and environment

- Fixture: `C:\Users\almar\Downloads\Main Continent-backup-2026-09-21T18_46_35.150Z.ink`
- Fixture bytes: 74541363
- Fixture SHA-256: `67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab`
- OS: Microsoft Windows 10.0.26200
- Processor count: 12
- Rendering API/driver: forward_plus / vulkan
- Adapter: AMD  Radeon RX 7800 XT
- Viewport: 1920 × 1080

## Connected case registry and independent invocations

Registry command: `./Scripts/run-connected-phase1.ps1 -ListCases` (public cases exclude internal `__*` sentinels).

| Case | Exit | Assertions | Elapsed (s, observation) | Verdict |
|---|---:|---:|---:|---|
| Tracer | 0 | 24 | 10.906 | PASS |
| ImportBounds | 0 | 20 | 36.356 | PASS |
| RenderReference | 0 | 64 | 4.149 | PASS |
| ExportPublication | 0 | 22 | 35.062 | PASS |
| CrashRecovery | 0 | 49 | 6.911 | PASS |
| ThemeSmoke | 0 | 19 | 0.55 | PASS |
| EntryImportUi | 0 | 51 | 4.154 | PASS |
| ShellStates | 0 | 23 | 1.086 | PASS |
| Gestures | 0 | 22 | 0.72 | PASS |
| ToolPanels | 0 | 17 | 0.534 | PASS |
| ScopeCues | 0 | 21 | 22.094 | PASS |

Unknown-case negative control: exit 2 — PASS.

## Quantitative hardware scenarios

Endpoint: input monotonic timestamp to the first `FramePostDraw` after `MapCanvas._Draw` submits the matching revision-tagged texture. Display scanout is excluded. Percentiles use nearest-rank arithmetic. No latency samples are excluded.

Targets: each run ≥60 s; input-to-visible p95 ≤50 ms and p99 ≤100 ms; frame intervals p95 ≤20 ms and p99 ≤33.3 ms; recent undo touching ≤16 resident tiles p95 ≤100 ms.

| Scenario | Cache | Seconds | n | Input p50/p95/p99 ms | Frame n | Frame p50/p95/p99 ms | Queue max | >100 ms stalls | Recent undo p95 / tiles | Resource | Verdict |
|---|---|---:|---:|---|---:|---|---:|---:|---|---|---|
| PaintAcrossTiles | Warm | 60.001 | 15 | 16.7 / 36.737 / 36.737 | 3598 | 16.666 / 16.739 / 17.017 | 1 | 0 | N/A | PASS | PASS |
| PaintAcrossTiles | Cold | 60.011 | 15 | 16.722 / 24.252 / 24.252 | 3599 | 16.666 / 16.741 / 16.908 | 1 | 0 | N/A | PASS | PASS |
| PaintAcrossTiles | Evicted | 60.003 | 15 | 26.601 / 36.621 / 36.621 | 3599 | 16.666 / 16.765 / 17.263 | 1 | 0 | N/A | PASS | PASS |
| PanZoomWhilePainting | Warm | 60.003 | 15 | 16.717 / 16.774 / 16.774 | 3599 | 16.666 / 16.741 / 16.921 | 1 | 0 | N/A | PASS | PASS |
| PanZoomWhilePainting | Cold | 60.005 | 15 | 16.705 / 17.027 / 17.027 | 3599 | 16.666 / 16.732 / 16.944 | 1 | 0 | N/A | PASS | PASS |
| PanZoomWhilePainting | Evicted | 60.01 | 15 | 23.92 / 31.826 / 31.826 | 3600 | 16.666 / 16.741 / 17.114 | 1 | 0 | N/A | PASS | PASS |
| RiverEdit | Warm | 60.008 | 15 | 16.685 / 16.739 / 16.739 | 3599 | 16.666 / 16.737 / 16.937 | 1 | 0 | N/A | PASS | PASS |
| RiverEdit | Cold | 60.016 | 15 | 16.688 / 16.921 / 16.921 | 3600 | 16.666 / 16.73 / 16.91 | 1 | 0 | N/A | PASS | PASS |
| RiverEdit | Evicted | 60.012 | 15 | 16.7 / 16.793 / 16.793 | 3600 | 16.666 / 16.742 / 16.936 | 1 | 0 | N/A | PASS | PASS |
| UndoRedo | Warm | 60 | 15 | 16.688 / 29.721 / 29.721 | 3598 | 16.666 / 16.736 / 16.944 | 1 | 0 | 27.479 / 1 | PASS | PASS |
| UndoRedo | Cold | 60.033 | 16 | 16.682 / 16.872 / 16.872 | 3600 | 16.665 / 16.723 / 16.862 | 1 | 0 | 11.933 / 1 | PASS | PASS |
| UndoRedo | Evicted | 60.013 | 15 | 16.721 / 23.96 / 23.96 | 3599 | 16.666 / 16.739 / 16.979 | 1 | 0 | 15.859 / 1 | PASS | PASS |

Generated/processed/coalesced/dropped pointer counts and every attributed >100 ms stall are retained in `artifacts/phase1-acceptance/hardware.json`. Hardware matrix: **PASS**.

## Resource readings

- Reported VRAM: 17163091968 bytes
- Reported system RAM: 33443094528 bytes
- Engine/compositor headroom: 29758976 bytes
- GPU allocation: 29758976 bytes; measured=True
- Process peak: 1017274368 bytes; measured=True
- Decoded CPU cache: 30380672 bytes; measured=True
- Interaction-process export allocation: 0 bytes; measured=True (no export occurs in the interaction matrix)
- Connected 16K export peak buffer: 71299072 bytes; target ≤536870912
- Connected 16K export peak process working set: 1382387712 bytes; target ≤8360773632
- Connected import peak process working set: 682881024 bytes; target ≤8360773632
- History acceleration cache: 0 bytes; measured=True
- Interaction resource rows: PASS; complete REND-05 import/paint/export envelope: PASS

## Correctness, durability, publication, and coast branch

- Real import, both recovery choices, bounds, source identity, cache deletion/reopen: PASS.
- Save/reopen, cursor, redo, forced termination/device-loss recovery: PASS.
- Shared colour/alpha/tiled-reference seam gate (≤1 channel): PASS.
- D-20 coast branch: **UnstyledGeneratedEdge**. Generated coast styling is unused; distance/style precision checks are **N/A**, while colour, soft coverage, and seams remain active gates.
- Frozen concurrent edit/export, cancellation/failure destination preservation, validated seamless 16K PNG: PASS. Export case elapsed: 35.062 s (observation only; no threshold).

## D-28 reviewer matrix

Original-resolution 100% and 150% frames reviewed on 2026-09-23. The three scopes remain distinct at both scales; the 150% texture readout wraps within the canvas without covering controls.

| Scale | State | Evidence | Reviewer answer | Verdict |
|---|---|---|---|---|
| 100% | land | `artifacts/ui-smoke/scope-land-100.png` | Soft circular coverage cue and Land/Subtract/Foreground readout; the panel says coverage changes coastline and reveals Background, not texture colour or layer opacity. | PASS |
| 100% | texture | `artifacts/ui-smoke/scope-texture-100.png` | Texture colour/intensity label, swatch, dashed hardness ring and square bounds identify the texture brush and its target, not a land mask or layer opacity. | PASS |
| 100% | opacity | `artifacts/ui-smoke/scope-opacity-100.png` | Foreground layer opacity slider and 22% status readout identify whole-layer opacity, separate from Land coverage and texture colour. | PASS |
| 150% | land | `artifacts/ui-smoke/scope-land-150.png` | Soft circular coverage cue and Land/Subtract/Foreground readout; the panel says coverage changes coastline and reveals Background, not texture colour or layer opacity. | PASS |
| 150% | texture | `artifacts/ui-smoke/scope-texture-150.png` | Texture colour/intensity label, swatch, dashed hardness ring and square bounds identify the texture brush and its target, not a land mask or layer opacity. | PASS |
| 150% | opacity | `artifacts/ui-smoke/scope-opacity-150.png` | Foreground layer opacity slider and 22% status readout identify whole-layer opacity, separate from Land coverage and texture colour. | PASS |

## Design-frame dispositions (17/17)

| Frame/file | Disposition | Applicable state/flow evidence | Exception reason / visual result |
|---|---|---|---|
| Start.dc.html | Compared | EntryImportUi | Interaction assertions; visual composition pending frame-level reviewer sign-off. |
| Import.dc.html | Compared | EntryImportUi | Two unselected recovery modes and source-preserving review asserted. |
| ImportReport.dc.html | Compared | EntryImportUi, ImportBounds | Bounded degradation/rejection and preserved preview asserted. |
| Main.dc.html | Compared | ShellStates; `artifacts/ui-smoke/phase1-main-shell-lod896.png` | 1920×1080 original-resolution review PASS: fitted, centered map; essential layers, tools and status visible without clipping. |
| Compact.dc.html | Compared | ShellStates; `artifacts/ui-smoke/phase1-compact-shell-lod896.png` | 1366×768 original-resolution review PASS: fitted map and reachable compact controls with no observed overlap. |
| LandCursor.dc.html | Compared | ScopeCues; D-28 six-frame matrix | Original-resolution land, texture and opacity reviewer rows PASS at both scales. |
| Layers.dc.html | Superseded in part | ShellStates | UI-SPEC removes the mockup grid/reserved object area from Phase 1; exact Foreground/Background rows remain compared. |
| BrushInspector.dc.html | Compared | ToolPanels, ScopeCues | Domain-valid controls and texture-specific cue asserted. |
| MaskInspector.dc.html | Superseded in part | ToolPanels, ScopeCues | UI-SPEC names Edged polygon/Round soft and removes coast controls; active Land scope compared. |
| River.dc.html | Compared | ToolPanels, hardware RiverEdit | Point/width/bank semantics exercised; generated banks remain unstyled. |
| History.dc.html | Compared | ShellStates, CrashRecovery, hardware UndoRedo | Durable cursor/rebuild/save state evidence. |
| Export.dc.html | Superseded in part | ExportPublication, ShellStates | Phase 4 DPI/grid/label controls excluded; frozen revision/publication flow compared. |
| Recovery.dc.html | Compared | CrashRecovery, ShellStates | Fresh-process recovery and backend failure state asserted. |
| Tokens.dc.html | Compared | ThemeSmoke | Offline fonts, palette, metrics, focus and component states asserted. |
| States.dc.html | Compared | EntryImportUi, ShellStates, ToolPanels | Applicable Phase 1 empty/loading/error/blocked states asserted. |
| Flows.dc.html | Compared | All 11 connected cases | Import/edit/save/reopen/history/export/recovery flows independently exercised. |
| Shortcuts.dc.html | Compared | Gestures, ToolPanels | Canvas/application/field routing and cancellation asserted. |

## Requirement evidence (23/23 IDs enumerated)

| Requirement | Connected evidence | Verdict |
|---|---|---|
| DOC-01 | Tracer, ImportBounds | PASS |
| DOC-02 | Tracer, CrashRecovery, ShellStates | PASS |
| DOC-03 | ImportBounds, RenderReference, ExportPublication | PASS |
| IMPT-01 | Tracer, ImportBounds, EntryImportUi | PASS |
| IMPT-02 | ImportBounds, EntryImportUi | PASS |
| IMPT-03 | ImportBounds | PASS |
| LAYR-01 | Tracer, ShellStates, ToolPanels | PASS |
| TERR-01 | Gestures, ToolPanels, ScopeCues | PASS |
| TERR-02 | RenderReference, ScopeCues, hardware PaintAcrossTiles | PASS |
| MASK-01 | RenderReference, ToolPanels, ScopeCues | PASS |
| WATR-01 | RenderReference, ToolPanels, hardware RiverEdit | PASS |
| HIST-01 | CrashRecovery, ShellStates, Gestures, hardware UndoRedo | PASS |
| HIST-02 | ShellStates, hardware UndoRedo | PASS |
| HIST-04 | Tracer, ImportBounds, CrashRecovery | PASS |
| EXPT-01 | ExportPublication, CrashRecovery, ShellStates | PASS |
| UIIN-01 | ThemeSmoke, EntryImportUi, ShellStates, Gestures, ToolPanels, ScopeCues | PASS |
| DURA-01 | Tracer, CrashRecovery | PASS |
| DURA-02 | CrashRecovery | PASS |
| REND-01 | Tracer, RenderReference, ExportPublication | PASS |
| REND-02 | RenderReference | PASS |
| REND-03 | RenderReference, ExportPublication | PASS |
| REND-04 | hardware four-by-three matrix | PASS |
| REND-05 | RenderReference, ExportPublication, hardware resource ledger | PASS |

## Raw evidence hashes

| Artifact | SHA-256 |
|---|---|
| `artifacts/phase1-acceptance/registry.json` | `d9c7242ef2f5747dae43e2ff5c6a004654ee18d105cb046352c6dafa91c8f924` |
| `artifacts/phase1-acceptance/connected-cases.json` | `cbda034185347568dffede58e08a435f1dedfd1703ced5e7f54f975b30207c8e` |
| `artifacts/phase1-acceptance/hardware.json` | `38948fe1dda4e4d27d5ed9bf3ac5ed7a84e2f27854f7fd8357ae7af3f533e366` |
| `artifacts/ui-smoke/scope-cues-manifest.json` | `0c8b925866a195ad853489c10d235574f6b9022bfab12531ac0871b2f7304378` |
| `artifacts/phase1-acceptance/case-Tracer.log` | `a5ff52ec9ffe72a7904ad61e42962a8087e2f4068fe27753c1bfe950444b493e` |
| `artifacts/phase1-acceptance/case-ImportBounds.log` | `360c91d115bfd67d445bba64a61a26007a0e57ec6affe0c9f1611deefc17ff69` |
| `artifacts/phase1-acceptance/case-RenderReference.log` | `ff93803ef6a58005953748ad0caff0ceee5ecd2cd226eded8cafb96b288c8389` |
| `artifacts/phase1-acceptance/case-ExportPublication.log` | `2122387a1a08531ecfff8c95679d9bfaab74cadf8761824afd217bea6dc8d2b3` |
| `artifacts/phase1-acceptance/case-CrashRecovery.log` | `b8c677f53425570646a3fe81077d1908f559db70b65621bdfa38eb7fdde36508` |
| `artifacts/phase1-acceptance/case-ThemeSmoke.log` | `7ea9e003c426125c633bbee2b2ce53546f89a0294314ee8f64873326286dd0ba` |
| `artifacts/phase1-acceptance/case-EntryImportUi.log` | `6ed2cd0b4aedc23d3ec477b600af41a6c129866784a8e3092f475bab01120456` |
| `artifacts/phase1-acceptance/case-ShellStates.log` | `c5b06d921fe4b8b070aef508f8897c76a44df778b77a76a857ec7d6c9cc65307` |
| `artifacts/phase1-acceptance/case-Gestures.log` | `6f9836ed233efa7124b029571aa9f3b6b397a5708700cbdd531de6f0c2f4a6e5` |
| `artifacts/phase1-acceptance/case-ToolPanels.log` | `470797d6891c71ff9f790957c0db86936e1da38768c483f0b32e261052bc2bb2` |
| `artifacts/phase1-acceptance/case-ScopeCues.log` | `b3745474cc7c2d309c9886e4721e3f5e10412f106a121751da646aaafab759fd` |
| `artifacts/ui-smoke/scope-land-100.png` | `5f44054219a6be2fcd354f529472be842f9f8298732eb4b3376c7fb865fdeaa4` |
| `artifacts/ui-smoke/scope-texture-100.png` | `44e2b6b7201592a58828e47235d8232caa1b006ddb9eab27d79cb999691c10be` |
| `artifacts/ui-smoke/scope-opacity-100.png` | `34b1fb30fba299a65c8fc7813a2cfdbdb69dc9818d3935e4f85373bc9c15e050` |
| `artifacts/ui-smoke/scope-land-150.png` | `d432087fa1d3564c2bf351cd1812181c316cc1b0cfcf98f03f2fa4a4276a4346` |
| `artifacts/ui-smoke/scope-texture-150.png` | `172f4b398e66ab859d7a63206babd1f2df93e2aa9c6c8082e3564e89aabfd0b1` |
| `artifacts/ui-smoke/scope-opacity-150.png` | `fcc21481b620bcdc8c3b315832ec638589d51317c7d3a79e0228073c882c6493` |
| `artifacts/ui-smoke/phase1-main-shell-lod896.png` | `a1cf49ff868f06d2c4eb82dfaa2be9b0bf2df7362290a9c8eb6cbe6744e106de` |
| `artifacts/ui-smoke/phase1-compact-shell-lod896.png` | `d7a506e7d624eaab439041005a53f3c511a9231a4ef4f469533f48fc9c8213a8` |
| `artifacts/ui-smoke/phase1-15-highzoom/main-highzoom.png` | `aea99355a5a9ae9dbc7a19f0e48af675e87cc673ff55e2ade0e82bae77753f98` |
| `artifacts/ui-smoke/phase1-15-highzoom/compact-highzoom.png` | `8c37b8ebdf8059b44b3fc04d3c62e75141907ff94fcfefc3364f79813ff36d8b` |

## Final disposition

All connected, quantitative, resource, D-28 reviewer and Main/Compact visual gates passed. High-zoom Main/Compact captures are hashed above as supplemental visual evidence.
