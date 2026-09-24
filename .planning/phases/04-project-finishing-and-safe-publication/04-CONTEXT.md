# Phase 4: Project Finishing and Safe Publication - Context

**Gathered:** 2026-09-23
**Status:** Ready for planning; visual design and implementation remain pending

<domain>
## Phase Boundary

Deliver finishing effects, square Grid, preset PNG exports, current-state portable ZIP export, deliberate history/storage cleanup and manual integration JSON. Owned IDs: DOC-04, DOC-05, NOTE-01, HIST-03, FILT-01, EXPT-02, INTG-01.

Discussion also clarified the shared map-coordinate and resolution contract. DOC-01 is now assigned to the separately inserted Phase 1.1; its normalization/migration work is a prerequisite, not a second Phase 4 implementation. Preserve that phase's work and the original 50 uniquely owned requirement IDs. The prior design briefs are inputs, not approved visual designs or a separate GSD requirements SPEC. This discussion is not runtime acceptance evidence.
</domain>

<decisions>
## Implementation Decisions

### Appearance and Grid
- **D-01:** Use a dedicated Appearance panel opened from the toolbar for map-wide finishing controls. Preserve ordered colour adjustment, paper texture and grain; do not add an Effects layer row as the selected entry point.
- **D-02:** Paper texture offers texture selection, strength, scale and rotation. Grain stays stable in document coordinates with stored seed/parameters; exact colour-adjustment and grain widgets/defaults remain design proposals.
- **D-03:** Grid is a dedicated default layer fixed at the top, above terrain, stamps, paths and labels. It is not reorderable among object layers or an additional terrain/mask layer.
- **D-04:** Grid is hidden by default when creating/importing a map; its row remains available. This is an initial visibility default, not a rule that resets saved visibility on every reopen. Earlier fixed terrain roles, mixed object layers and protected final object layer remain.

### Map units, editing resolution and PNG export
- **D-05:** Initial columns/rows set the map proportions and initialize the starting square Grid. A 40 x 30 layout has 4:3 proportions. Later grid-spacing changes do not resize or reshape map geometry. For imports, preserve source aspect and explicit source-to-map transforms; never force source content into an unrelated preset shape.
- **D-06:** Use a fixed 1,000-map-unit longest geometric edge; the other edge follows the aspect ratio. Grid counts never set unit scale. Both 40 x 30 and 80 x 60 layouts are 1,000 x 750 units, with initial cells of 25 and 12.5 units respectively. Fractional map coordinates are valid. The earlier suggestion of 10 or 20 units per cell is not a minimum-cell-size requirement.
- **D-07:** All map-relative sizes use stable map units: brush diameter, stamp/text size, path/river widths, grid spacing, scatter distance and effect distances. Positions and proportions stay fixed across editing/export sampling. Screen-space handles and UI markers remain pixels. This explicitly supersedes earlier nominal-pixel and map-pixel wording in Phase 1/2 contexts; same-tool parameter retention and separate settings per tool remain.
- **D-08:** Editing resolution is selected at creation/import and fixed for that project in P0. Offer 1K, 2K, 3K and 4K presets, without a custom value field. User-facing mid-project editing-resolution changes are deferred; ordinary zoom, mip selection and cache management remain available.
- **D-09:** PNG export resolution is independent: presets 1K, 2K, 3K, 4K, 8K and 16K only. No custom width/height, longest-edge or physical-size-driven resolution entry in P0. Display resulting pixel dimensions and preserve aspect ratio.
- **D-10:** For editing and export, K labels measure the longest raster edge, with K = 1,024 pixels: 2K is 2,048 and 3K is 3,072. A 4:3 2K image is 2,048 x 1,536; portrait is 1,536 x 2,048. Pixel rounding for arbitrary aspect ratios must be consistent and documented; no nonuniform stretching. Maximum output axis remains 16,384 pixels.
- **D-11:** Export renders from retained source assets and authoritative geometry at target resolution: original stamp images, target-scale paths/text and retained native paint geometry/texture sources. Do not upscale the editing preview as the whole export. Imported baked raster bases retain their source-detail limit; this does not reintroduce later import-history/base reconstruction, which remains deferred.
- **D-12:** Label/grid export inclusion initially follows current canvas visibility, with export-specific override toggles. Overrides leave canvas state unchanged. Do not default to separately remembered per-project inclusion settings. Grid can be included for an export while hidden on the canvas; label inclusion does not silently unhide individual hidden objects/layers. Notes remain excluded from PNG by default.
- **D-13:** DPI is optional on the Export screen. The user allowed a static value; the agent selected 300 DPI as the initial default, not a number specifically chosen by the user. DPI affects print-size metadata, not pixel count. Do not derive DPI from grid counts alone, require physical cell-size setup or introduce VTT presets.
- **D-14:** PNG, ZIP and JSON default filenames combine map name with date and time. Use filesystem-safe formatting and collision handling; normal destination and overwrite safeguards remain. Exact formatting is a design detail.
- **D-15:** Inherited export guarantees remain: one frozen durable revision with pinned assets, editing priority, bounded buffers/tiles, progress/cancel, validation before publication and preservation of any prior destination on failure. Duration is reported, not a pass/fail threshold. The 1,000-unit model does not redefine renderer tile sizes or output-pixel error tolerances.

### Portable ZIP and storage
- **D-16:** Treat portable packaging as an export to a ZIP file. Stay in the working project afterward; show completion/Show in folder. Never automatically open the copy or replace the open project. Opening the exported copy is a separate explicit action.
- **D-17:** ZIP contains the current editable map and required assets/data, without inherited undo/redo history. The exported copy starts from the packaged current state as its baseline and may accumulate new history. Creating the ZIP does not prune working-project history.
- **D-18:** No undo history does not mean a flattened image or removal of native paint/entity state needed for current rendering/editability. Retain all current authoritative data and source/provenance references required by the current project state; omit history-only dependencies. A consistent backup/snapshot, pinned current-state references, integrity validation and reopening must succeed before publishing.
- **D-19:** Put storage information and cleanup in Project Settings > Storage. Distinguish current map/assets and sources, retained undo history and disposable caches, with separate cleanup actions and clear consequences.
- **D-20:** History pruning selects a cutoff in the history list and previews the older undo steps that will be removed. Explicit confirmation and a new valid baseline remain required. The cache budget never silently prunes authoritative history; current content and active export/package references survive cleanup.

### Integration JSON
- **D-21:** Generate JSON manually through Export > Map data (JSON). Do not automatically create or refresh a companion integration JSON on save.
- **D-22:** Include all map entities, notes and markers, including hidden content, with visibility state recorded. Do not introduce a visible-only default or per-type checklist. Note exclusion from PNG does not imply exclusion from JSON.
- **D-23:** JSON is derived, non-authoritative output labelled with its frozen saved source revision. Use the settled map-unit coordinate convention. Later edits may make that export older; show usable version/time context without requiring users to understand internal IDs. Schema details and formatting remain engineering/design discretion; application-specific mapping remains deferred.

### Design and engineering discretion
The user expressly allowed a static DPI default; 300 DPI was selected by the agent. No blanket delegation was given otherwise. Choose/propose unspecified layouts, numerical ranges, initial editing/export preset selection, date formatting, Grid colour/line/origin controls, effect widgets and progress presentation within the settled behavior. Handle arbitrary aspect rounding and source transforms without changing geometry or source bytes. Do not add custom output dimensions, physical-size-driven pixel calculations, mid-project resolution changes or automatic JSON updates as required P0 controls. Do not reinterpret existing persisted pixel-space projects as normalized units; Phase 1.1 owns tested compatibility.
</decisions>

<canonical_refs>
## Canonical References

Read before planning or implementing; paths are repository-relative. Current spec/requirements and this context supersede older pixel-size and package-history descriptions. Prior phase contexts retain other settled decisions. Phase 1.1 owns normalization and compatibility. The PNG reference illustrates the user's desired separation of aspect and editing resolution, not independently verified internals of Inkarnate.

- `docs/spec.md`
- `docs/architecture.md`
- `docs/engine-decision.md`
- `docs/README.md`
- `.planning/PROJECT.md`
- `.planning/REQUIREMENTS.md`
- `.planning/ROADMAP.md`
- `.planning/phases/01-connected-imported-terrain/01-CONTEXT.md`
- `.planning/phases/02-recovered-assets-and-stamps/02-CONTEXT.md`
- `.planning/phases/03-water-paths-and-labels/03-CONTEXT.md`
- `.planning/phases/04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md`
- `.planning/codebase/STACK.md`
- `.planning/codebase/ARCHITECTURE.md`
- `.planning/codebase/CONVENTIONS.md`
- `.planning/phases/04-project-finishing-and-safe-publication/references/map-creation-resolution-reference.png`
- `.planning/phases/01.1-normalize-document-geometry-to-1-000-map-units-with-independ/01.1-CONTEXT.md`
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable assets
- Reuse the durable edit session, immutable revisions, SQLite backup boundary and content-hashed assets for consistent exports and ZIP snapshots.
- Existing shared viewport/export work and the streaming PNG path provide the integration seam for independent sampling and output preflight; importing at low editing quality must not replace retained originals.
- The separately planned Phase 1.1 explicitly handles normalized units, source-to-map transforms and legacy-project compatibility. Read its current context/source when planning Phase 4 selectors.

### Established patterns
- Domain/Application remain independent of Godot, storage and GPU adapters. Persistent commands/assets define current content; caches are disposable.
- Completed changes commit before acknowledgement. Background export pins a revision and yields to editing. Normal save and manual JSON export are distinct triggers.
- Excluding undo from a ZIP is an exported snapshot policy, not permission to remove authoritative current strokes or prune the working project.

### Integration points
- Common toolbar/inspector for Appearance; default Grid row and common visibility controls; export entry points for PNG, ZIP and JSON; Project Settings for Storage.
- Use map-unit geometry with explicit map-to-raster transforms for tile residency, effects and output pixels; no assumed 1:1 unit/pixel relationship.
- Earlier codebase maps describe a starting point. Phase 1 and Phase 1.1 work is progressing separately; inspect live implementations during research rather than relying on stale probe descriptions. No build, benchmark or physical-device acceptance was run for this documentation task.
</code_context>

<specifics>
## Specific Ideas

- User supplied a map-creation screenshot with editing-resolution buttons and aspect presets/custom initial columns and rows. It is saved in the phase's references folder.
- User explicitly wanted to think through layout, working resolution and export quality before choosing UI controls. They selected 1,000 units rather than grid-derived units after a pros/cons comparison.
- Example: 40 x 30 initial cells produces a 1,000 x 750-unit map; 2K editing gives 2,048 x 1,536 pixels and 8K export gives 8,192 x 6,144. Geometry and grid counts stay unchanged.
- User described the ZIP workflow as a form of export: create the ZIP and continue working.
</specifics>

<deferred>
## Deferred Ideas

- User-facing changes to editing resolution after creation/import; revisit if needed.
- Custom editing/export pixel dimensions and print-size-driven resolution input were not selected for P0.
- Automatic integration JSON on save and full-history package variants were offered but not selected for P0.
- Existing P1 deferrals remain: VTT pixels-per-cell/physical grid presets, other image formats, hex/isometric grids, advanced coast/filter controls, named history snapshots and application-specific JSON mapping.
</deferred>

---
*Phase: 04-project-finishing-and-safe-publication*
*Context gathered: 2026-09-23*
