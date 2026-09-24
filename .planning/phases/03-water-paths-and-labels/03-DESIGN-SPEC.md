# Phase 3 design specification: Water, Paths and Labels

Date: 2026-09-23. Status: discussion decisions settled; ready for a design agent. This is an input brief. Visual proposals still require review.

## Assignment and standalone context

Design the detail-editing workflow for Malkav's Mapwright, an offline native Godot/C# Windows/Linux map editor for finishing an existing Inkarnate world map. Extend the existing canvas, tool selector, contextual side panel, layers/assets, undo/save and export surfaces. Use preceding visual designs when available; otherwise mark shell assumptions.

Owned IDs: IMPT-04, IMPT-05, LAYR-02, WATR-02, PATH-01, TEXT-01, TEXT-02, NOTE-02. Read 03-CONTEXT.md for the complete settled decisions. This brief supersedes earlier mandatory spline-lake handles, freeform label handles, separate object/path/text layer types, in-canvas text entry and post-import reconstruction-wizard deliverables.

Terrain remains exactly Background below Foreground with one Foreground mask. Mixed object layers containing stamps, paths and text stay above both. Every map has one object layer from setup; the last cannot be deleted, though it can be emptied. Grid is also part of the intended default map setup; Grid implementation and its detailed behavior remain Phase 4.

## Settled interactions

- **Lakes:** Land Subtract cuts Foreground coverage to reveal Background; Land Add can refill it. Alternatively paint a water texture or solid colour on Foreground without changing its mask. Reuse the edged/round Land brushes and the painting presets. No dedicated lake entity, curve handles, protected shape, disable/delete controls or lake-specific decorative effects.
- **Paths:** draw freehand with gentle automatic smoothing and conversion to editable points. Finish a stroke and stay ready for another; select a path to edit its points. Preserve supported polyline/Bezier edits, width, colour, dashes and caps.
- **Text entry:** edit content in the side panel with live canvas preview. Font, size, tracking, colour, outline and shadow remain available.
- **Label modes:** Straight; Curve with signed peak deflection from -100% to +100% (0 straight, +100 semicircle, -100 opposite semicircle); S-shape with one shared signed percentage producing two equal, opposite bends. Changing the sign reverses the S. No mandatory freeform label-handle editor or independent bend values.
- **Fonts:** curated bundled fonts first, installed fonts in a separate section, all offline. Missing-font replacement defaults to all labels using that font in the current map, with preview then Apply and undo. Preserve original face/version identity where available.
- **Tool settings:** each tool remembers its own parameters. Stamp size, brush size and text size never carry across tools. Same-tool settings still persist when switching stamp assets or Texture Brush terrain targets.
- **Layer target:** object-placing tools keep the selected object layer. If Background or Foreground is active, visibly switch to the topmost object layer. Keep hidden/locked target guards and explicit Show layer / Unlock actions. Land and Texture Brush retain their terrain targeting.
- **Notes:** create with Note tool then map click, or right-click map then Add note. Edit in the side panel. Notes are hidden by default; reveal shows pins only, selecting a pin opens its content. Image exports exclude notes by default.
- **Import:** compare original preview and reconstruction side by side with linked pan/zoom during initial import. Keep a concise supported/partial/visual-only summary and missing/unsupported content. Accept the import and continue in the saved Mapwright project. No later base-reconstruction wizard or reapplication of subsequent edits.
- **Crash recovery:** reopen the latest durable save, including acknowledged autosaves; only an unfinished unacknowledged gesture may be lost. Recovery and import comparison are distinct flows.

## Required surfaces

| ID / surface | Required controls and feedback | Coverage |
|---|---|---|
| P3-01 Path creation and editing | Freehand preview; smoothed committed path; repeated drawing; select/edit points and tangents; cancel/undo; visible layer target | PATH-01 |
| P3-02 Lake painting | Existing Land Add/Subtract and edged/round controls; Foreground water texture/solid-colour painting; clear colour-versus-mask target feedback | WATR-02 |
| P3-03 Path properties | Width, colour, dash pattern and caps; selected-path context, reset and invalid-value states | PATH-01 |
| P3-04 Label editor and fonts | Side-panel text field, font sections/previews, size/tracking/colour/outline/shadow; missing-font replacement preview and scope | TEXT-01, TEXT-02 |
| P3-05 Label shape controls | Straight/Curve/S-shape selector; signed deflection percentage; live tangent-aligned text preview; no arbitrary curve-handle requirement | TEXT-01 |
| P3-06 Mixed layers and notes | Mixed entity layer rows, create/reorder/name/visibility/lock/solo/opacity, protected final object layer, both note-entry routes, reveal pins and note editor | LAYR-02, NOTE-02 |
| P3-07 Initial import comparison | Original/reconstructed views with linked pan/zoom; concise editable/partial/visual-only and missing-content summary | IMPT-04 |
| P3-08 Initial import acceptance | Preserve editable-versus-flattened choice with neither preselected; review current import, accept/cancel; clarify subsequent saved-project editing and recovery | IMPT-05 |

## Journeys to prototype

1. Create a mask-cut lake, refill part with Land Add, and undo. Separately paint water on Foreground, show its mask is unchanged, repaint and undo. Save/reopen/export preserve both.
2. Draw two paths successively. Select the first, edit geometry and style, then undo a completed edit. Imported unsupported properties remain preserved without implying they are editable.
3. Create/select a label, type in the side panel and choose a bundled or installed font. Switch tools and return: each tool retains its own size/settings.
4. Switch a long range name through Straight, positive/negative Curve and S-shape. Show 0%, intermediate deflection and the Curve semicircle endpoints. Keep text readable enough to assess spacing and tangent alignment; show a long-label edge case.
5. Open an import with a missing font. Preview a replacement on all affected labels and apply intentionally; retain source identity and allow undo.
6. Switch from terrain selection to an object-placing tool: select the topmost object layer visibly. Add stamps, paths and text to the same layer; switch tools while keeping that layer. Show hidden/locked targets and protection against deleting the last object layer.
7. Add notes through both entry routes, edit in the side panel, hide/reveal pins and select one. Return to a clean canvas with notes excluded from the default PNG output.
8. During initial import, compare the same region in original and reconstructed views, inspect the concise outcome, choose editable or flattened mode explicitly, accept, edit and save. Separately show reopening the latest saved project after interruption; do not ask the user to reconstruct it again.

## Controls and state details to design

- **Path states:** idle, freehand drawing, selected path, point editing, dragging, committed and cancelled. Define pointer release, Escape, Delete and any Enter/double-click behavior without making a finish gesture start another path. Path point editing is distinct from the parameterized label shapes.
- **Text focus:** ordinary typing, caret/selection and numeric entry must suppress tool shortcuts. Specify Enter/newline policy, focus changes, Escape and commit/undo boundaries. These unselected details are proposals; do not reinstate canvas text entry as a required feature.
- **Label geometry:** explain how the signed percentage maps to the visible baseline, especially intermediate values and S-shape symmetry. Geometry math, end handling and long-string spacing need renderer fixtures. A backend may use curves internally without exposing freeform handles. Preserve mode/parameters in saved state and export.
- **Font controls:** readable face previews, search/browse, long names, missing face/glyph and substitution states. Distinguish font size from object scale, tracking from word spacing, colour from opacity. No rich-text editor or cloud font downloads.
- **Layer rows:** share established controls; stamps, paths and text have explicit mixed ordering within a layer. Layers cannot move below/between terrain roles. Grid target/order is a Phase 4 decision. Imported or reopened content must retain at least one object layer without discarding imported entities.
- **Notes:** reveal must be discoverable; creating a hidden-by-default note must still expose its active editor and location. Define temporary creation feedback as a proposal. Do not show note text everywhere when the user reveals pins.
- **Import summary:** preserve source version/dimensions, IDs, unsupported data and tested semantics internally. Surface a concise explanation of editable versus baked content, missing art/fonts and partial recovery; command counts are not a fidelity score. Detailed reconstruction-report tooling is not required.
- **Import acceptance:** source .ink is unchanged and import creates a new project. Unknown state-changing operations stop trusted affected reconstruction; preserve visual fallbacks without replaying over baked bases. Existing Phase 2 asset replacement/suggestions remain, but there is no later terrain-base rebuild/reapplication flow.

## Inherited constraints and review states

Prefer one fixed outer-coast style with unstyled rivers/lakes; omit generated styling if reliable separation is too involved. No advanced coastline controls, fades, rings or isolines. Soft coverage and colour/seam correctness remain; distance checks apply only if used. Colour painting creates no coverage contour.

Completed gestures become durable before acknowledgement or dependent rendering. Active gesture cancellation creates no undo step. Viewport and export share geometry/shaping; mockups cannot prove fidelity. Preserve responsive pan/zoom, bounded work and prior-output-preserved publication. Crash recovery uses a fresh process and the latest durable project, without post-loss GPU readback or save.

Include empty/unfinished paths, invalid values, locked/hidden targets, mixed selections, empty/long text, missing glyph/font, negative/zero/extreme deflection, unsupported imported curves, partial/visual-only import, comparison loading, cancelled import, failed save and recovery with an unfinished gesture discarded.

## Deliverables and completion checklist

Write DESIGN-RESPONSE.md and place editable frames/exports or an optional prototype in design-assets/ beside this brief. Preserve the brief and refer to the settled context.

- Annotated P3-01 through P3-08 frames at 1920 × 1080 and one constrained-panel layout.
- State/interaction tables for path drawing/editing, side-panel text, label modes, layer routing, note creation and initial import acceptance.
- Inspector matrix with labels, units, defaults/ranges, reset, invalid/mixed states and proposed versus settled choices.
- Realistic sample content: long range label, short city name, multi-segment road, both lake methods and missing font. Mark illustrative content.
- Coverage mapping for all eight IDs, source references, open renderer-fixture questions and confirmation that no superseded spline-lake, freeform label or post-import rebuild UI has returned.

Completion means a reviewer can make either lake type, draw/edit a road, use all three label modes, resolve a missing font, organize mixed layers, use notes and accept an initial import without guessing what is editable.

## Boundaries and sources

Deferred: spline-lake entities, pressure, elevation/flood fill, tributary joining, repeated path assets, arbitrary-path text, independent S bends, automatic label placement, cloud fonts, post-import base reconstruction and detailed reconstruction-report tooling. Grid settings arrive in Phase 4.

[Phase context](03-CONTEXT.md) · [Roadmap](../../ROADMAP.md) · [Requirements](../../REQUIREMENTS.md) · [Working specification](../../../docs/spec.md) · [Phase 1 context](../01-connected-imported-terrain/01-CONTEXT.md) · [Phase 2 context](../02-recovered-assets-and-stamps/02-CONTEXT.md) · [Design index](../DESIGN-INDEX.md).

## Phase 4 map-unit clarification

All map-relative sizes use stable map units. The longest edge is 1,000 units and the other edge follows the aspect ratio; initial grid counts do not change that scale. Brush diameter, stamp/text/path sizes, scatter spacing and effect distances must not depend on editing or export pixels. Keep screen-space handles in pixels. This supersedes earlier map-pixel wording in inherited contexts; retained source geometry/assets use explicit transforms. Editing resolution is fixed at creation/import in P0; export resolution is independent. The finalized [Phase 4 context](../04-project-finishing-and-safe-publication/04-CONTEXT.md) and [brief](../04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md) specify editing presets 1K/2K/3K/4K and independent export presets through 16K, measured on the longest raster edge. Phase 1.1 owns normalization and legacy compatibility.
