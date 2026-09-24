# Phase 2 design frames

Thirteen annotated frames for [Recovered Assets and Stamps](../02-DESIGN-SPEC.md), described in [DESIGN-RESPONSE.md](../DESIGN-RESPONSE.md). Status: **proposed**. These are interface drawings, not evidence that anything works.

Live canvas (pan/zoom, comments): <https://claude.ai/artifact/GXGbFpuviNevGUKsPnD1Wn> — private; the owner shares it from the page's Share menu.

## The frames

| Screen | File | Covers |
| --- | --- | --- |
| P2-00 | `Main.dc.html` | Editor shell with the Stamp tool active, 1920 × 1080 |
| P2-00b | `Compact.dc.html` | The same shell at 1366 × 768 |
| P2-01 | `Browser.dc.html` | Asset browser — category first, filters, search, empty and loading states |
| P2-02 | `AssetDetails.dc.html` | One asset's metadata and every validation finding with an example |
| P2-03 | `Import.dc.html` | Pack and loose-image import: manifest, outcomes, progress, cancel |
| P2-04 | `MissingArt.dc.html` | Unresolved art: placeholders, fit preview, replacement scope |
| P2-05 | `Placement.dc.html` | Placement ghost, transform handles, the two panels and their targets |
| P2-06 | `Order.dc.html` | Marquee, picking, multi-transform, explicit order, mixed values |
| P2-07 | `Scatter.dc.html` | The scatter brush: asset set, spacing, ranges, seed, three states |
| — | `Targets.dc.html` | The three targets, per-tool memory, layer aiming, the default layer |
| — | `States.dc.html` | Seventeen states with their copy; the catalogue/document undo split |
| — | `Controls.dc.html` | Control matrix, shortcuts, component specifications |
| — | `Journeys.dc.html` | The six journeys with commit and cancel boundaries |

`canvas.json` is the index: each frame's position and size on the canvas, plus the notes drawn beside them.

## Reading the files

Each `.dc.html` is one self-contained frame at a fixed pixel size — plain HTML with inline styles, so opening one in a browser shows it. Two caveats when read that way:

- `./support.js` is the canvas runtime and is not in this folder; without it the wrapper elements (`<x-dc>`, `<helmet>`) are inert. The frame still renders, since everything visible is ordinary markup.
- The type ramp needs IBM Plex Sans, IBM Plex Mono and Spectral from Google Fonts. Offline, frames fall back to system faces and the density shifts slightly.

The live canvas is the better viewer: it lays the frames out in their groups and carries the notes.

## What these are not

No frame is a screenshot of a running application. Sample data is invented and labelled as such in DESIGN-RESPONSE §2 — *Aethermoor — West Reaches*, the *Aethermoor Starter — Highlands 0.4.2* pack, the `pine-highland` and `ridge-peak` families, source ids like `ink:mtn-range-03`. No hash, file size, count, budget or duration here has been measured; the resource readouts are plausible placeholders showing where real values go. The 238 stamps and 7,559 × 8,192 source dimensions come from the working specification's own fixture observations.

Values marked *proposed* — including every bound marked **P** on the control matrix — need confirmation before they are built. DESIGN-RESPONSE §13 lists them.
