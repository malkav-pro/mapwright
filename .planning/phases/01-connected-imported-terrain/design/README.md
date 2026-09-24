# Phase 1 design mockups

Seventeen annotated frames for [Connected Imported Terrain](../01-DESIGN-SPEC.md), described in [DESIGN-RESPONSE.md](../DESIGN-RESPONSE.md). Status: **proposed**. These are interface drawings, not evidence that anything works.

Live canvas (pan/zoom, comments): <https://claude.ai/artifact/8E1beuFrSWxV9tm3EvGLqF> — private; the owner shares it from the page's Share menu.

## The frames

| Screen | File | Covers |
| --- | --- | --- |
| P1-01 | `Start.dc.html` | Project entry — recent projects, open, import |
| P1-01 | `Import.dc.html` | Import review: two recovery modes, neither preselected |
| P1-01b | `ImportReport.dc.html` | Bounded rejection, degradation, the import report |
| P1-02 | `Main.dc.html` | Editor shell, 1920 × 1080 |
| P1-02b | `Compact.dc.html` | The same shell compressed to 1366 × 768 |
| P1-02c | `LandCursor.dc.html` | Coverage under the cursor, and why it is not layer opacity |
| P1-03 | `Layers.dc.html` | Grid, the reserved object area, Foreground and Background |
| P1-04 | `BrushInspector.dc.html` | Texture brush: presets, tips, stroke, texture transform |
| P1-05 | `MaskInspector.dc.html` | Land tool: Edge and Circle variants, Add and Subtract |
| P1-06 | `River.dc.html` | River centreline, width profile, bank softness |
| P1-07 | `History.dc.html` | Undo, reconstruction, and the four save states |
| P1-08 | `Export.dc.html` | PNG export: presets, frozen revision, job states |
| P1-08b | `Recovery.dc.html` | Reopening after a crash, save failure, no GPU |
| — | `Tokens.dc.html` | Colour, type, metrics, component states |
| — | `States.dc.html` | Seventeen exceptional states with their copy |
| — | `Flows.dc.html` | The six workflows end to end |
| — | `Shortcuts.dc.html` | Shortcut scopes and the numeric contract |

`canvas.json` is the index: each frame's position and size on the canvas, plus the notes drawn beside them.

## Reading the files

Each `.dc.html` is one self-contained frame at a fixed pixel size — plain HTML with inline styles, so opening one in a browser shows it. Two caveats when read that way:

- `./support.js` is the canvas runtime and is not in this folder; without it the wrapper elements (`<x-dc>`, `<helmet>`) are inert. The frame still renders, since everything visible is ordinary markup.
- The type ramp needs IBM Plex Sans, IBM Plex Mono and Spectral, loaded from Google Fonts. Offline, frames fall back to system faces and the density shifts slightly.

The live canvas is the better viewer: it lays the frames out in their groups and carries the notes.

## What these are not

No frame is a screenshot of a running application. Sample data is invented and labelled as such in DESIGN-RESPONSE §6 — *Aethermoor — West Reaches*, revisions 1,284–1,287, the Silvermere river. No size, duration or memory figure here has been measured; the resource readouts and export timings are plausible placeholders showing where real values go.

Values marked as proposed in DESIGN-RESPONSE §13 need engineering confirmation before they are built.
