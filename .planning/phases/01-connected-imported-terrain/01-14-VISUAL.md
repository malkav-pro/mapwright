# 01-14 Main and Compact visual evidence

Status: **visual subtask complete**. The separate full quantitative verdict is in `01-ACCEPTANCE.md` and `01-VERIFICATION.md`.

The connected editor shell was captured from the pinned real `.ink` fixture at original resolution after the canvas had completed layout and a matching frame draw. The first captures exposed a startup fit defect: the map stayed small and corner-anchored after the shell resized. `MapCanvas` now refits when its container changes size. The revised images were inspected at original resolution against `01-UI-SPEC.md`, `design/Main.dc.html` and `design/Compact.dc.html`.

| Frame | Evidence | SHA-256 | Review |
|---|---|---|---|
| Main 1920×1080 | `artifacts/ui-smoke/phase1-main-shell-lod896.png` | `a1cf49ff868f06d2c4eb82dfaa2be9b0bf2df7362290a9c8eb6cbe6744e106de` | Pass at fitted zoom: map centered and fully fitted; Foreground/Background, separate opacity controls, Land mask and river state, tool rail, save/recovery/status controls visible without clipping. |
| Compact 1366×768 | `artifacts/ui-smoke/phase1-compact-shell-lod896.png` | `d7a506e7d624eaab439041005a53f3c511a9231a4ef4f469533f48fc9c8213a8` | Pass at fitted zoom: compressed header, rail and layer panel remain reachable; map centered and fully fitted; no overlapping controls observed. |

`./Scripts/run-connected-phase1.ps1 -Case ShellStates` passed 23 assertions after the fit change. These captures do not certify brush-specific canvas scope cues; those retain the separate D-28 six-frame evidence. They also do not substitute for the full hardware acceptance matrix.

The revised captures above use the later 896-pixel display-resolution viewport. The map remains legible at fitted zoom. After adaptive viewport detail levels were implemented, `artifacts/ui-smoke/phase1-15-highzoom/main-highzoom.png` and `compact-highzoom.png` were reviewed at original resolution: map details remain legible with no visible tile seams. Their hashes are in `01-ACCEPTANCE.md`.
