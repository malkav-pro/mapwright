# Phase 4: Project Finishing and Safe Publication - Discussion Log

> **Audit trail only.** Do not use as input to planning, research or execution agents. Settled decisions are in 04-CONTEXT.md.

**Date:** 2026-09-23
**Areas discussed:** Effects and grid; PNG export; Project packages and storage; Integration JSON

## Effects and grid

### 1. Where should map finishing controls live?

| Option | Description |
|---|---|
| 1 | A dedicated Appearance panel (recommended) |
| 2 | An Effects row in the Layers panel |

**User’s choice:** A dedicated Appearance panel, opened from the toolbar, for colour adjustments, paper texture and grain.

**Notes:** User selected 1. User selected all four Phase 4 discussion areas.

### 2. How should paper texture work?

| Option | Description |
|---|---|
| 1 | Choose a paper texture and adjust its strength, scale and rotation (recommended) |
| 2 | One built-in paper texture with strength only |
| 3 | Skip paper texture for now |

**User’s choice:** Choose a paper texture and adjust strength, scale and rotation.

**Notes:** User selected 1.

### 3. Where should the Grid layer sit?

| Option | Description |
|---|---|
| 1 | Fixed at the top (recommended) |
| 2 | Reorderable among object layers, above terrain |

**User’s choice:** Fixed at the top, above terrain, stamps, paths and labels.

**Notes:** User selected 1. Grid is the default layer settled in Phase 3; it is not an extra terrain/mask layer.

### 4. Should Grid be visible when creating or importing a map?

| Option | Description |
|---|---|
| 1 | Hidden by default (recommended) |
| 2 | Visible by default |

**User’s choice:** Hidden by default; the Grid row is always available and can be shown when needed.

**Notes:** User selected 1. This establishes creation/import default visibility, not resetting the saved visibility on every reopen. Export inclusion remains a separate control.

### 5. Continue to PNG export?

| Option | Description |
|---|---|
| 1 | Move on to PNG export (recommended) |
| 2 | Discuss effects and grid further |

**User’s choice:** Move on to PNG export.

**Notes:** User selected 1.

## PNG export

### 6. How should output size be chosen?

| Option | Description |
|---|---|
| 1 | Map size by default, plus presets and custom dimensions (recommended) |
| 2 | Custom pixel dimensions only |
| 3 | Print size first |

**User’s choice:** User wants to carefully revisit the whole resolution model: map aspect ratio is set through initial grid dimensions, editing resolution sets the working canvas resolution, and export independently chooses higher or lower resolution while preserving aspect and using source assets at higher quality.

**Notes:** User did not select a preset option. Supplied Inkarnate map-creation screenshot shows editing resolution choices and aspect presets/custom columns x rows. Treat as user reference, not independently verified Inkarnate internals. Separate map layout, editing resolution and export resolution; exact preset values, resolution-axis convention, initial grid coupling, later resolution changes and brush-unit implications are not yet settled. Prior initial-import-only reconstruction decision remains; native rendering from retained assets is different from rebuilding the imported base.

### 7. Should initial columns and rows also initialize the Grid layer?

| Option | Description |
|---|---|
| 1 | Also initialize a 40 x 30 grid |
| 2 | Use those numbers only for aspect ratio |

**User’s choice:** Also initialize the grid: 40 x 30 sets both map proportions and a starting 40 x 30 grid. Later grid-spacing changes leave map shape unchanged.

**Notes:** User selected 1. Grid visibility remains hidden by default; initial grid dimensions and later grid spacing are distinct from output resolution.

### 8. Should editing resolution be changeable after creation?

| Option | Description |
|---|---|
| 1 | Changeable at any time (recommended) |
| 2 | Fixed when creating the map |

**User’s choice:** Start with the easier path and expand if needed: choose editing resolution at creation/import and keep it fixed for that project in the initial implementation; export resolution remains independent.

**Notes:** User requested the simpler initial option in response to this choice, interpreted as option 2. Defer a user-facing mid-project editing-resolution change; do not conflate this with zoom/mip/cache management or export resolution.

### 9. Which dimension do resolution labels such as 2K describe?

| Option | Description |
|---|---|
| 1 | The longest edge (recommended) |
| 2 | Always the width |

**User’s choice:** The longest edge for both editing and export: a 4:3 landscape at 2K is 2048 x 1536; portrait is 1536 x 2048.

**Notes:** User selected 1. Exact preset inventory remains open.

### 10. Should brush size cover the same proportion of maps with different editing resolutions?

| Option | Description |
|---|---|
| 1 | Same proportion using consistent map units (recommended) |
| 2 | Editing-resolution pixels |

**User’s choice:** All sizes should be in map units, including brushes and stamps.

**Notes:** User generalized this to all map-relative sizes. Use stable map units for brush/stamp/text/path/grid/scatter/effect geometry, independent of editing and export sampling. This supersedes earlier map-pixel wording; screen UI dimensions remain screen pixels. The numerical calibration of one map unit is still open.

### 11. Should units be derived from the initial grid or use a fixed map scale?

| Option | Description |
|---|---|
| 1 | Grid-derived units (discussed at 20 units per initial cell) |
| 2 | Fixed scale (example: 1000 units along the longest edge) |

**User’s choice:** Fixed scale, with 1000 map units along the longest edge to start with.

**Notes:** User rejected one-unit-per-cell as too coarse, considered 10 or 20 units per cell, requested pros/cons, then explicitly chose fixed 1000-longest-edge scale. The earlier 10/20-cell suggestion is not a minimum constraint. Other edge follows aspect ratio; 40 x 30 gives 1000 x 750 units and 25-unit initial cells. Fractional map coordinates remain valid. Source geometry/raster mappings use explicit transforms; retained originals are unchanged.

### 12. Which editing-resolution choices should the first version offer?

| Option | Description |
|---|---|
| 1 | 1K, 2K, 3K and 4K (recommended) |
| 2 | 1K, 2K and 4K |
| 3 | Presets plus a custom resolution |

**User’s choice:** 1K, 2K, 3K and 4K, matching the supplied reference.

**Notes:** User selected 1. Longest-edge convention already settled; no custom editing-resolution field selected.

### 13. Which export-resolution choices should the first version offer?

| Option | Description |
|---|---|
| 1 | Presets only (recommended for the first version) |
| 2 | Presets plus a custom longest-edge value |

**User’s choice:** Presets only: 1K, 2K, 3K, 4K, 8K and 16K.

**Notes:** User selected 1. No custom resolution field initially; preserve aspect ratio. Retained source assets and editable geometry are rendered at target scale; imported baked rasters retain their source detail limit.

### 14. How should label and grid inclusion behave?

| Option | Description |
|---|---|
| 1 | Separate export settings, initially labels on and grid off, remembered per project (recommended) |
| 2 | Follow the canvas, with export overrides |

**User’s choice:** Follow current canvas visibility initially, with export toggles to override it.

**Notes:** User selected 2. Do not instead persist a separate per-project inclusion default. Export overrides do not alter canvas visibility or reveal individually hidden content unless explicitly specified later; notes remain excluded from image export by default.

### 15. Should the first version include optional print/DPI controls?

| Option | Description |
|---|---|
| 1 | An optional Print section: set DPI and see physical size (recommended) |
| 2 | No print controls yet |

**User’s choice:** DPI should be an optional setting on the Export screen. User is comfortable with calculation from grid size or a static number.

**Notes:** User explicitly wants an optional Export-screen DPI setting and permits a static default. Agent chose 300 DPI as the initial default within that discretion; the user did not specifically choose the number. DPI changes print metadata/physical-size interpretation, not the selected output pixel count. Grid-only DPI calculation is not introduced because it would also require a physical cell size; VTT pixels-per-cell presets remain deferred. Do not require a separate print workflow or a grid-based calculation.

### 16. Continue to project packages and storage?

| Option | Description |
|---|---|
| 1 | Move on to project packages and storage |
| 2 | Discuss resolution or export further |

**User’s choice:** Move on to project packages and storage.

**Notes:** User selected 1; resolution/export discussion complete for now.

## Project packages and storage

### 17. What should a portable project package include?

| Option | Description |
|---|---|
| 1 | Complete project including source data and undo history (recommended) |
| 2 | Only current state and required assets, without undo history |
| 3 | Choose when packaging |

**User’s choice:** Only the current state: editable map and required assets, without undo history, for a smaller package.

**Notes:** User selected 2. Package starts with no inherited undo/redo; working-project history remains intact. Retain all authoritative data, source references and native paint/entity state needed for the current editable map, not merely a flattened image. No package-only historical blobs or redo states required; packaging must not silently prune the working project. Snapshot consistency, validation and safe publication remain.

### 18. Which control should clean up undo history in the working project?

| Option | Description |
|---|---|
| 1 | Choose a cutoff in the history list (recommended) |
| 2 | Keep the most recent N actions |
| 3 | Clear all history |

**User’s choice:** Choose a cutoff in the history list and preview the undo steps older than that point that will be removed.

**Notes:** User selected 1. Existing explicit pruning, lost-step preview and current-state/reference safety guarantees remain; no automatic history deletion.

### 19. Where should storage information and cleanup controls live?

| Option | Description |
|---|---|
| 1 | Project Settings > Storage (recommended) |
| 2 | Alongside the History panel |

**User’s choice:** Project Settings > Storage: current map/assets, undo history and disposable cache usage, with separate cleanup actions.

**Notes:** User selected 1. Cache clearing and authoritative history pruning are distinct actions.

### 20. What happens after creating a portable package?

| Option | Description |
|---|---|
| 1 | Stay in working project, with completion and Show in folder / Open packaged copy actions (recommended) |
| 2 | Open packaged copy automatically |

**User’s choice:** A portable package is a form of export: create a ZIP and continue working in the existing project. User selected 1 (stay in working project).

**Notes:** User explicitly selected ZIP format and export semantics. Never automatically open/switch to the packaged copy or replace the current project. Completion and Show in folder remain appropriate; any opening of the copy is a separate explicit user action, not part of normal export completion.

### 21. Continue to integration JSON?

| Option | Description |
|---|---|
| 1 | Move on to integration JSON |
| 2 | Discuss packages and storage further |

**User’s choice:** Move on to integration JSON.

**Notes:** User selected 1.

## Integration JSON

### 22. What should integration JSON export by default?

| Option | Description |
|---|---|
| 1 | All map entities and notes (recommended) |
| 2 | Only currently visible entities, notes optional |
| 3 | Choose content types each time |

**User’s choice:** All map entities and notes, including hidden content, with visibility recorded.

**Notes:** User selected 1. All map content is exported as structured data; note exclusion from PNG does not apply to JSON.

### 23. When should integration JSON be generated?

| Option | Description |
|---|---|
| 1 | Manual Export > Map data (JSON) (recommended) |
| 2 | Automatically on save as a companion file |

**User’s choice:** Manually through Export > Map data (JSON).

**Notes:** User selected 1. Do not automatically generate or update a companion JSON on save. Existing revision-labelled consistent export contract remains.

### 24. Which default naming style should PNG, ZIP and JSON exports use?

| Option | Description |
|---|---|
| 1 | Map name, confirm replacement if file exists (recommended) |
| 2 | Map name plus date and time |

**User’s choice:** Map name plus date and time, keeping separate versions by default.

**Notes:** User selected 2. Applies to all three output types. Use filesystem-safe date/time and handle collisions safely; default naming does not waive destination or overwrite safeguards.

### 25. Finalize Phase 4 and move to Phase 5?

| Option | Description |
|---|---|
| 1 | Finalize Phase 4 and move to Phase 5 (recommended) |
| 2 | Revisit Phase 4 |

**User’s choice:** Finalize Phase 4 context and design brief, then move to Phase 5.

**Notes:** User selected 1; all areas complete.

## Agent discretion

User allowed a static DPI default; agent chose 300. Other unspecified design details remain proposals within the settled context.

## Deferred ideas

- User-facing changes to editing resolution after map creation/import; reconsider if needed.
