# Phase 1 design specification: Connected Imported Terrain

Date: 2026-09-22. Status: ready for a design agent. This is an input brief; visual choices remain proposed until reviewed.

## Assignment and product context

Design the shared editor shell and the first complete terrain-editing experience for Malkav's Mapwright. This is a single-user, offline native Windows/Linux map editor built with Godot Control nodes and C#. The user is continuing an existing Inkarnate world map with local artwork. The first workflow must connect recovered Background/Foreground terrain, texture painting on both, Foreground mask/coastline editing, one editable river, undo/redo, save/reopen and PNG export.

Existing code is an evidence harness with disconnected editing, storage and rendering pieces. Its current layout is not an approved visual design. Your deliverable is a usable design specification with annotated screens and interactions, not implementation or proof of measured performance.

**Owned requirement IDs:** DOC-01, DOC-02, DOC-03, IMPT-01, IMPT-02, IMPT-03, LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01, HIST-01, HIST-02, HIST-04, EXPT-01, UIIN-01, DURA-01, DURA-02, REND-01, REND-02, REND-03, REND-04, REND-05.

## Fixed constraints and creative freedom

- Coastline amendment: Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used. Do not create a style inspector or manual coast-classification workflow. Show a fixed-style reference and unstyled fallback until engineering selects the feasible path.
- Accepted tool choices: one Texture Brush with a Background/Foreground selector; all texture/brush settings carry across targets. Size is diameter in map units. Space + drag or middle-mouse drag temporarily pans; mouse-wheel zoom is pointer-centred. Mask/river tools automatically select Foreground's mask itself with a clear highlight; returning to Texture Brush restores the previous texture-paint target.

- User amendment, 2026-09-22: exactly two terrain layers, Background below Foreground. Their roles/order are fixed, with no terrain add/duplicate/delete/reorder controls. Only Foreground owns the land/coastline mask; subtracting it reveals Background. Rivers modify that mask. Later Phase 3 clarification supports lakes through ordinary Land Subtract or Foreground water texture/colour painting, with spline-lake modifiers deferred. Mixed object layers (stamps, paths and text) introduced later are reorderable above both terrain layers. This supersedes the original four-terrain-layer brief.
- Decisions from Phase 1 discussion: launch to recent projects; imports use a configured projects folder with a location override; show a quick recovery review and let the user choose editable recovered terrain or original flattened appearance each time with neither preselected. For visual recovery, the original preview backs locked Background and Foreground starts empty; explain that baked coasts are not recovered editable geometry. Preserve the preview for comparison in both modes.

- Provide native desktop editing with mouse, keyboard, pan/zoom and resizable panels. The main working surface is the map. No account, cloud workspace, subscription, marketplace or in-app image generation.
- Define an original, coherent interface for familiar map-editing capabilities. Choose the palette, type scale, density, icons and placement; explain the choice. A restrained neutral editor around colourful map content is a starting direction, not a locked theme.
- Design primarily at 1920 × 1080, the interaction benchmark viewport. Also show how panels compress at 1366 × 768 and how the design scales at 150% display scaling as review exercises. These extra sizes do not establish minimum supported hardware or window size.
- Establish regions for project actions, tool selection, active-tool properties, layers, canvas navigation and operation status. Decide which are persistent and which are contextual. Reserve a coherent way to add later assets/text tools without showing nonfunctional future controls in the Phase 1 delivery.
- Keep texture rotation, brush footprint/size, layer opacity and later object transforms distinct. The inspector must identify what the user is editing.
- Respect Godot Control-node feasibility, bounded lists and inexpensive canvas overlays. UI mockups cannot certify driver behavior or latency.

## Required surfaces and controls

### Brush-shape reference and scope

User clarification: Land-mask editing has two brush modes, **Edged polygon** and **Round soft**. Both have Add/Subtract and brush diameter. Edged mode has Roughness and Smooth; round mode has Softness. Do not apply the full textured-tip library to Land, and do not infer a square mask brush from earlier proposed options. The edged brush is a painting footprint; this reference does not add a click-to-create polygon geometry tool.

Texture painting uses the [preset-library reference](references/brush-preset-reference.png) and additionally supports an edged variant. Its inspector distinguishes footprint size/roughness from selected texture, opacity, texture scale and texture rotation. Smooth rounds footprint corners without alpha feathering or pointer stabilization. Tapered texture presets narrow automatically at both stroke ends without pressure. Preset inventory and numeric ranges/defaults remain design/research details; screenshot values are examples, not locked defaults. P0 does not acquire pen-pressure or custom-tip import requirements from these images alone.

References: [Land edged mode](references/land-edged-brush-reference.png), [Land round mode](references/land-round-brush-reference.png), [Texture edged mode](references/texture-edged-brush-reference.png). Follow their tool distinctions and control vocabulary; they are not a requirement to clone the displayed styling or every unrelated toolbar icon.

| Screen ID / surface | Required controls and content | Requirement coverage |
| --- | --- | --- |
| P1-01 Project entry and import | Open saved local project; import `.ink` to a new project; select destination; source dimensions/version where known; progress/cancel/error; safe visual-recovery choice with original preview | DOC-01, IMPT-01, IMPT-02, IMPT-03 |
| P1-02 Editor shell | Project name, active tool/layer, editing surface, zoom/fit/pan access, undo/redo availability, save/open/export entry points, status and resizable panel boundaries | UIIN-01, DOC-01, DOC-02, REND-01 |
| P1-03 Layers panel | Background and Foreground role labels, display name, selection, visibility, lock, solo and opacity; Foreground mask association; fixed terrain order with no terrain add/reorder controls; reserve the area above them for later object layers | LAYR-01 |
| P1-04 Texture brush inspector | Preset library with tip/stroke previews and edged variant; local texture chooser; diameter, hardness, opacity, flow, spacing, texture scale/rotation and jitter; edged Roughness/Smooth and automatic taper for tapered presets; footprint/falloff/affected-region preview | TERR-01, TERR-02 |
| P1-05 Land-mask tool | Add/subtract Foreground coverage using Edged polygon (diameter, Roughness, Smooth corners) or Round soft (diameter, Softness); auto-selected Foreground mask target and highlight; preview distinct from texture colour and layer opacity; no independent mask-layer rows or coastline styling controls | MASK-01 |
| P1-06 River editing | Foreground target indicator, centreline points, width profile, bank softness, point selection/drag feedback and contextual property controls; persistent modifier selection and enable/delete affordance | WATR-01 |
| P1-07 History and save feedback | One gesture per undo step; action labels; unavailable undo/redo; older-history rebuilding with progress/cancel; unsaved/saving/saved/save-failed states | HIST-01, HIST-02, HIST-04, DOC-02, DURA-01 |
| P1-08 PNG export and recovery | Destination and basic size selection within 16K, source revision/status, progress/cancel/validation/completion/failure; restart/reopen recovery and cache rebuilding | EXPT-01, DOC-03, DURA-02, REND-05 |

The complete asset library belongs to Phase 2; provide a usable local texture chooser now. Phase 4 expands export settings, grid/label toggles and preflight detail; export progress, cancellation and previous-destination preservation already belong here.

## Core flows to prototype or storyboard

1. **Recover and paint:** choose an `.ink`, review its preview and recovered terrain, choose the starting mode without a preselected choice, create a new project in the configured folder (or override it), select Background or Foreground, pick a texture, change brush size/opacity/rotation and paint across a tile boundary without changing land coverage. In visual recovery, Background remains a locked preview base. Show the selected layer and active tool throughout.
2. **Shape a coast and river:** switch to Foreground mask subtraction to reveal Background, create soft coverage, select a river point, change width and softness, then paint nearby. Show fixed outer-coast styling only if implemented, with unstyled river banks and mouth; otherwise show the unstyled fallback. Later land painting does not close the river modifier, and neither operation creates a layer.
3. **Navigate without accidental painting:** temporarily pan/zoom during brush use, return to the previous tool, enter a numeric field and resume canvas work. Show focus, modifier-key and pointer-release behavior.
4. **Undo and preserve:** commit a stroke, undo, redo, undo again, then create a new edit. Show redo invalidation. Include an older undo requiring reconstruction, cancellation before completion and return to a coherent current state.
5. **Export while editing:** start an export from a frozen document version, continue editing a newer version, inspect progress, cancel or complete. The UI identifies which version was exported and distinguishes rendering from final validation/publication.
6. **Recover after failure:** a save fails or the process disappears during rendering. Show the next launch/reopen outcome, what was recovered and that an in-flight unacknowledged gesture may be absent. Do not depend on an error dialog being shown after device loss.

## Shared interaction contract to design

Return a component/state table covering buttons, icon buttons, tool buttons, toggles, sliders, numeric inputs, pickers, layer rows, inspectors, progress panels and alerts. Include default, hover, focused, active, disabled, invalid and busy states where applicable.

- Specify numeric entry, scrubbing/sliding, keyboard increments, units, decimal handling, invalid input, range clamping/rejection, reset and display rounding. Use percent for opacity/hardness/flow where appropriate and degrees for rotation; use the accepted diameter in map units for size. Do not invent renderer maximums. Mark unspecified defaults/ranges as proposed for engineering verification.
- Explain when a drag is a transient preview, when it commits one undoable command, and what Enter, Escape, pointer release and focus loss do. Committed document edits and cancelled previews must be distinguishable.
- Define active tool, active layer and selection as separate states. Explain incompatible tool/layer combinations and locked or hidden targets. Avoid silent edits to a different layer.
- Propose a shortcut table with scopes: canvas, text/numeric editing, inspector and modal. Undo inside an active field must have an explicit relationship to document undo. Do not hijack typing with global tool shortcuts.
- Use labelled controls/tooltips, visible keyboard focus and more than colour alone for status. Choose readable contrast and target sizes; give exact design tokens rather than adjectives.
- Saved status means durable acknowledgement. A visible live preview is not necessarily saved. Long jobs need progress and responsive cancellation; indeterminate work must not show fabricated percentages.

REND-02 and REND-03 are colour/alpha and seam correctness throughout canvas/export, not inspector controls. A used distance-field style also requires its conditional distance/reference checks; unused style checks are not applicable, not claimed as passed.

Accepted target/history behavior: locked targets block painting and offer **Unlock**; hidden targets block painting and offer **Show layer**, without automatic changes. Keep Undo/Redo visible and the history panel collapsible. Escape cancels a whole uncommitted stroke/property drag with no undo step. Older-history rebuilding allows pan/zoom but blocks editing, with progress/Cancel. Crash reopening uses a brief recovery banner with details, not a blocking acknowledgement dialog. See [Phase 1 context](01-CONTEXT.md) for all settled choices.

## Required exceptional states

Empty project; no compatible editable layer; locked imported raster; all layers hidden; missing texture; unsupported/oversized/corrupt `.ink`; partial recovery; invalid numeric value; no undo; cache rebuild; queued save; disk-full/save failure; cancelled/failed export with previous output intact; render backend unavailable; fresh-process recovery after a crash. Describe the next usable action for each and provide important copy.

Initial resource targets are 25% of reported VRAM capped at 4 GiB, 512 MiB decoded CPU cache, 512 MiB export working buffers, process peak RAM at most 25% of system RAM and 4 GiB history acceleration cache. Define how resource pressure affects feedback without exposing GPU internals in normal controls. Detailed resource management is finished later.

At the benchmark viewport, required input-to-visible p95/p99 is ≤50/100 ms; frame intervals ≤20/33.3 ms; recent undo for ≤16 resident tiles p95 ≤100 ms. Each paint/navigation/river/undo scenario runs ≥60 seconds under warm/cold/evicted caches. Designs should avoid blocking interactions, delayed decorative transitions and constant expensive preview work. Performance remains an implementation test; export duration has no pass/fail cap.

## Deliverables and completion checklist

Write `DESIGN-RESPONSE.md` and put editable frames/exports or an optional interactive prototype in `design-assets/` beside this file. Preserve this brief.

- Annotated P1-01 through P1-08 screens, including a complete editor composition with realistic map content and the two terrain roles. Keep role identity visible even with long display names. Label any invented sample data.
- Shared tokens, region dimensions/resizing behavior, typography/icon choices, component variants, field units/default proposals and scoped shortcut table for all later phases.
- Flow/state diagrams for the six workflows; a matrix connecting exceptional states to a screen, message and next action.
- A Godot-oriented handoff describing layout containers, scrolling, focus and overlay behavior without implementing the application.
- Requirement-to-frame coverage for every owned ID; explain how the design supports nonvisual correctness/recovery requirements without claiming to prove them.
- Explicit unresolved design decisions and engineering questions. Choose a recommended design for each ordinary layout choice rather than returning only questions.

Completion means a reviewer can walk from import through painting, Foreground mask/river editing, undo/save and export without guessing control behavior. The Background/Foreground editor, save truthfulness, invalid targets and restart recovery must be visible in the deliverables.

## Reference material

[Roadmap](../../ROADMAP.md) · [Requirements](../../REQUIREMENTS.md) · [Project](../../PROJECT.md) · [Working specification §§2–8,10–13](../../../docs/spec.md) · [Architecture](../../../docs/architecture.md) · [Design index](../DESIGN-INDEX.md).

P0 here is the product priority; this is GSD Phase 1. Pressure, arbitrary docking, themes, multi-window, 64K documents and additional export formats are deferred. Later phase designs inherit this shell and its interaction vocabulary.

## Phase 4 map-unit clarification

All map-relative sizes use stable map units. The longest edge is 1,000 units and the other edge follows the aspect ratio; initial grid counts do not change that scale. Brush diameter, stamp/text/path sizes, scatter spacing and effect distances must not depend on editing or export pixels. Keep screen-space handles in pixels. This supersedes earlier map-pixel wording in inherited contexts; retained source geometry/assets use explicit transforms. Editing resolution is fixed at creation/import in P0; export resolution is independent. The finalized [Phase 4 context](../04-project-finishing-and-safe-publication/04-CONTEXT.md) and [brief](../04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md) specify editing presets 1K/2K/3K/4K and independent export presets through 16K, measured on the longest raster edge. Phase 1.1 owns normalization and legacy compatibility.
