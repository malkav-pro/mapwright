# Phase 4 design specification: Project Finishing and Safe Publication

Date: 2026-09-23. Status: discussion decisions settled; ready for a design agent. This is an input brief; visual proposals remain subject to review.

## Assignment and standalone context

Design finishing and local export for Malkav's Mapwright, an offline native Godot/C# Windows/Linux map editor. The user is finishing an imported Inkarnate world map with managed local art. Extend the common canvas, tools, contextual side panel, layers/assets, history and durable save state. Basic shared PNG rendering, cancellation and safe publication are inherited contracts, not capabilities proven by these mockups.

Owned IDs: DOC-04, DOC-05, NOTE-01, HIST-03, FILT-01, EXPT-02, INTG-01. Read [04-CONTEXT.md](04-CONTEXT.md) for settled choices. The separately inserted Phase 1.1 owns DOC-01 normalization and compatibility; this phase provides the related preset selectors and final export workflow without duplicating that implementation.

Terrain is fixed Background below Foreground, with one Foreground mask. Mixed stamp/path/text layers sit above both; every map starts with one object layer and the last cannot be deleted. Grid is a default dedicated row fixed above all terrain and objects, initially hidden. Prior label modes, notes, tool settings and initial-import-only reconstruction remain unchanged.

## Settled coordinate and resolution model

- Initial columns/rows establish map proportions and the starting square grid. Later grid-spacing changes do not resize the map. Imports preserve source aspect through explicit source-to-map transforms; never stretch source content to fit an unrelated layout preset.
- The longest geometric edge is always 1,000 map units; the other follows aspect ratio. All map-relative sizes and positions use these stable units, including brush/stamp/text/path/river geometry, scatter spacing and effect distances. Screen-space handles remain pixels.
- A 40 x 30 map is 1,000 x 750 units with initial 25-unit cells. An 80 x 60 map has the same geometric extent with 12.5-unit cells. Grid count does not define unit scale.
- Editing resolution is chosen at creation/import and fixed for P0: 1K, 2K, 3K or 4K. No custom field or mid-project editing-resolution control.
- PNG export independently offers 1K, 2K, 3K, 4K, 8K or 16K. No custom width/height or physical-size-to-resolution workflow initially.
- K denotes 1,024 pixels on the longest raster edge. For a 4:3 map, 2K gives 2,048 x 1,536 pixels; 8K gives 8,192 x 6,144. Show calculated dimensions read-only, preserve aspect and document consistent integer rounding. Maximum axis: 16,384 pixels.
- Render export from retained originals and editable geometry at target resolution, not by enlarging the editing preview. Native paint replay uses stored geometry/source textures; baked imported rasters retain their source-detail limit. This does not reintroduce a post-import base-reconstruction wizard.

User reference: [creation, editing resolution and aspect ratio](references/map-creation-resolution-reference.png). The screenshot is a design reference, not independently verified evidence about Inkarnate internals. Earlier nominal-pixel and map-pixel size wording is superseded.

## Required surfaces

| ID / surface | Controls and behavior | Coverage |
|---|---|---|
| P4-00 Creation/import resolution extension | Aspect/initial columns-rows context; 1K/2K/3K/4K editing selector; resulting dimensions; fixed-after-creation explanation | DOC-01 inherited from Phase 1.1; EXPT-02 separation |
| P4-01 Appearance panel | Dedicated toolbar entry; ordered colour adjustment, paper texture and grain; paper selection, strength, scale and rotation; consistent preview/undo | FILT-01 |
| P4-02 Grid | Always-available topmost Grid row, hidden initially; square cell geometry in map units; visibility; propose colour/line/origin controls within existing scope | NOTE-01 |
| P4-03 PNG export | Resolution presets with calculated dimensions; label/grid inclusion initialized from canvas; optional DPI; destination and dated filename; frozen-version context | EXPT-02 |
| P4-04 Output preflight and progress | Resampled bases versus target-scale editable content; unavailable/over-budget outcomes; progress/cancel/validation/publication/completion; prior output preserved on failure | EXPT-02; inherited EXPT-01 |
| P4-05 Export project as ZIP | Current editable map and required data/assets, no inherited undo history; local destination; snapshot preparation, background export, validation and completion without switching projects | DOC-04 |
| P4-06 Project Settings > Storage | Current map/assets/sources, retained history and disposable caches separately; distinct cache cleanup and history cutoff/lost-step preview | DOC-05, HIST-03 |
| P4-07 Export > Map data (JSON) | Manual export of all entities, notes and markers, including hidden content/visibility flags; source revision/time, destination and completion | INTG-01 |

Match existing shell/tokens. Appearance is a panel, not an Effects layer row. Other unspecified layouts may be proposed. Ordinary Save, PNG, ZIP and JSON should have understandable purposes without surfacing storage internals.

## Settled export and storage behavior

- PNG label/grid inclusion starts from current canvas visibility; toggles override only that export. Do not use independently remembered inclusion defaults. A hidden Grid may be included by its export toggle; label inclusion does not silently unhide individual hidden entities/layers. Notes remain excluded from PNG by default.
- DPI is optional on the Export screen, initially 300 (agent-selected static default permitted by the user). Changing DPI changes print-size metadata, not selected pixel dimensions. Physical size may be displayed as information; do not calculate DPI from grid counts alone or require a physical cell size.
- PNG, ZIP and JSON default filenames use map name plus date/time. Choose filesystem-safe formatting and collision handling. Normal local destination/overwrite protections remain.
- ZIP is an export: create it and continue in the current project. Show completion and Show in folder; never automatically switch to the copy. A later explicit Open is separate.
- ZIP retains current editability and all data/assets required for the current map; omit inherited undo/redo and history-only dependencies. Preserve authoritative strokes/entities needed for current content rather than flattening the map. The copy starts from a valid baseline and can accumulate new history. Working-project history is untouched.
- Use a consistent backup/snapshot with pinned references, let editing resume, and validate/reopen the exported package before publication. Validation must not switch the user's visible project. ZIP creation is not ordinary Save or a full-history backup.
- Storage lives in Project Settings > Storage. Distinguish sources/current map, retained undo history and disposable caches. History pruning selects a cutoff in the history list, previews exactly which older steps are lost, requires deliberate confirmation and creates a valid baseline. Cache budgets never silently erase history.
- JSON is manual through Export > Map data (JSON), with no automatic companion update on save. Include all entities, notes and markers, even hidden content, with visibility flags and stable map-unit coordinates. Keep source revision identity; JSON is derived output, never authoritative storage.

## Journeys to prototype

1. Create a 40 x 30 layout at 2K editing; show the 1,000 x 750-unit geometry and initial Grid. Paint/place content, then export at 8K with unchanged proportions and positions. Include a portrait example and an imported map with preserved source aspect.
2. Open Appearance from the toolbar, adjust colour, choose paper and change its strength/scale/rotation, then adjust grain and effect order. Undo a completed change. Keep any fixed coast treatment and stamp shadows consistent without adding deferred coast controls.
3. Show the initially hidden topmost Grid, change spacing without resizing content, and include/exclude it independently for one PNG export. Verify export overrides leave editor visibility unchanged.
4. Choose a PNG preset, inspect calculated pixels, optionally change DPI and see that pixels stay fixed. Review a resampled imported base and re-rendered native items. Show an actionable over-budget preflight result without publishing partial output.
5. Export a frozen revision while continuing to edit. Show cancel and successful validation/publication, a dated filename, source-version context and preservation of an existing destination on failure.
6. Export current project as ZIP, observe that no undo history is included, keep editing the original, and receive completion only after integrity/reopen validation. Separately open the copy intentionally, verify current content/editability and start new history.
7. Inspect source/history/cache usage, clear only disposable caches, then choose a history cutoff and review lost steps and estimated reclaimable space before confirming or cancelling. Current content and active snapshots remain valid.
8. Manually export JSON with a hidden entity and hidden note, verify their visibility flags and source revision are included, then edit further and explain that the earlier JSON still represents its exported version.

## Details for the design agent

- Define units, labels, focus, reset, numeric validation and mixed/disabled states. Mark unspecified defaults/ranges as proposed, especially effect widgets and Grid appearance. Do not introduce guessed renderer maximums.
- Keep map units, source pixels, editing pixels, output pixels and screen pixels distinct. Preset changes must not alter persistent geometry. Legacy-project compatibility is a Phase 1.1 engineering obligation, not a silent UI reinterpretation.
- Effects use document coordinates and stable seeds; repainting the UI must not randomize grain or restart patterns at tile boundaries. Map-relative effect support scales with rendering density; buffer/tile budgets remain raster-pixel/byte contracts.
- Preflight explains existing raster detail versus editable content rendered at output scale. Preserve source originals; never promise detail that the source does not contain.
- Output progress may distinguish preparing, rendering/packing, validating, publishing, cancelling, complete and failed when observable. Use indeterminate progress where totals are unknown. No time threshold turns a long 16K export into a failure.
- Explain current version/time plainly. Do not mark Saved before durable acknowledgement or Complete before publication. Long work yields to editing and exposes cancellation where supported.
- Storage separates budget, actual usage and reclaimable estimates. The history acceleration budget is 4 GiB, not a limit that deletes authoritative history. Pins may delay reclamation; deleting caches and pruning undo are separate actions.
- JSON schema/formatting and archive layout are engineering details. Use .zip for the user-selected package format. Application-specific integration mapping and asset-library bundling beyond current map dependencies are not new requirements.

## Failure states and inherited guarantees

Cover invalid aspect/grid values, unavailable paper/source, disabled effects, font/art gaps, arbitrary-aspect pixel rounding, insufficient destination space/permission, filename collision, unsupported output size, unaffordable effect footprint, cancelled/failed export, failed validation, packaging interruption, missing authoritative data, migration failure, no prunable history and references pinned by active work.

Completed changes commit before acknowledgement. Exports use immutable revisions and bounded resources, preserve the previous destination until a flushed/validated replacement is published, and never depend on GPU readback after a crash. Recovery reopens the latest durably saved project in a fresh process. Colour/seam gates and input latency/resource limits remain unchanged. Prefer one fixed outer-coast style with unstyled inland banks or the authorized no-generated-style fallback; no coast-style controls, fades, rings or isolines.

## Deliverables

Write DESIGN-RESPONSE.md and place editable frames/exports or an optional prototype in design-assets/ beside this brief. Preserve this input and link the settled context.

- Annotated P4-00 through P4-07 frames at 1920 x 1080 plus a constrained-panel layout.
- State/flow tables for the eight journeys, including frozen output version, continuing edits, validation, ZIP completion and deliberate pruning.
- Control matrix covering map units, resolution presets, calculated pixels, DPI, Grid and effect parameters; distinguish settled choices from proposed defaults.
- Copy for source-detail limits, export overrides, no-history ZIP, history loss, cache cleanup, safe failure and manual JSON/source version.
- Coverage of all seven owned IDs plus inherited DOC-01/EXPT-01 behavior, with rendering/storage claims awaiting implementation evidence clearly identified.

Completion means a reviewer can distinguish map geometry from raster resolution, finish the map, export PNG/ZIP/JSON, continue in the current project and clean storage without confusing caches, history and current content.

## Boundaries and sources

Deferred: mid-project editing-resolution changes, custom pixel dimensions, physical-size-driven output, full-history package variants, automatic JSON updates, extra image formats, VTT presets, hex/isometric grids, advanced filters/coast controls, named history snapshots and application-specific integration mapping.

[Phase context](04-CONTEXT.md) · [Roadmap](../../ROADMAP.md) · [Requirements](../../REQUIREMENTS.md) · [Working specification](../../../docs/spec.md) · [Architecture](../../../docs/architecture.md) · [Phase 1.1 context](../01.1-normalize-document-geometry-to-1-000-map-units-with-independ/01.1-CONTEXT.md) · [Phase 3 context](../03-water-paths-and-labels/03-CONTEXT.md) · [Design index](../DESIGN-INDEX.md).
