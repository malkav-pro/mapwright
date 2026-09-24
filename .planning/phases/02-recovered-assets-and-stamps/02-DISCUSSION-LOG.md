# Phase 2: Recovered Assets and Stamps - Discussion Log

> **Audit trail only.** Do not use as input to planning, research or execution agents.
> Decisions are captured in 02-CONTEXT.md; this log preserves the alternatives considered.

**Date:** 2026-09-22
**Phase:** 02-recovered-assets-and-stamps
**Areas discussed:** asset library; missing-art replacement; stamp placement and selection; scatter.

## Asset library

### How should you browse assets by default?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Category first (recommended) | Yes |
| 2 | Folder first |  |
| 3 | Pack first |  |

**User's choice:** Category first: Trees, Mountains, Buildings, Textures and similar categories, with thumbnails and filters for packs, folders and tags.

### Should imported assets be available across projects or only in the current project?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Across all Mapwright projects (recommended) | Yes |
| 2 | Only in the current project |  |

**User's choice:** Across all Mapwright projects in one shared local library; each project retains the assets it uses for reliable reopening and packaging.

### When importing loose images without pack metadata, how should they be categorized?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Quick import review (recommended) | Yes |
| 2 | Use folder names |  |
| 3 | Import as Uncategorized |  |

**User's choice:** Quick import review: choose a category and optional tags for the batch before adding it.

### How should usable images with warnings be imported?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Import with warning badges (recommended) | Yes |
| 2 | Require review before importing |  |

**User's choice:** Import with warning badges; let the user use them and inspect issues when needed. Damaged or unsupported files are rejected.

### Next area: missing-art replacement, or discuss the asset library further?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: missing-art replacement | Yes |
| 2 | Discuss the asset library further |  |

**User's choice:** Next area: missing-art replacement.

## Missing-art replacement

### If the same missing asset appears many times, what should the default replacement scope be?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | All matching instances in the current map (recommended) | Yes |
| 2 | Only the selected instance |  |

**User's choice:** All matching instances in the current map, with preview and affected count; scope can change before applying and replacement is undoable.

### If replacement artwork has different proportions, how should it fit?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Fit original footprint, preserve proportions (recommended) | Yes |
| 2 | Fill original footprint exactly by stretching |  |
| 3 | Use replacement default size |  |

**User's choice:** Fit the original footprint while preserving replacement proportions and map position; preview size and anchor adjustments without stretching.

### How prominent should missing-art placeholders be?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Subtle outlined footprints (recommended) | Yes |
| 2 | Checkerboard boxes with visible Missing asset label |  |

**User's choice:** Subtle outlined footprints with a warning marker; show asset name or ID on hover or selection.

### Should replacement choices be remembered for future imports?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Remember and suggest (recommended) | Yes |
| 2 | Keep project-specific |  |
| 3 | Apply remembered matches automatically |  |

**User's choice:** Remember and suggest matching replacements during import review for acceptance or adjustment; do not apply silently.

### Next area: stamp placement and selection, or discuss replacement behavior further?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: stamp placement and selection | Yes |
| 2 | Discuss replacement behavior further |  |

**User's choice:** Next area: stamp placement and selection.

## Stamp placement and selection

### After placing a stamp, what should happen?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Keep stamping (recommended) | Yes |
| 2 | Select the new object |  |

**User's choice:** Keep stamping: each click places another copy; Escape or choosing another tool exits placement.

### When choosing a different stamp asset, should placement settings carry over, reset or be remembered separately?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Carry over (recommended) | Yes |
| 2 | Reset to new asset defaults |  |
| 3 | Remember separately for each asset |  |

**User's choice:** Carry size, rotation, tint, opacity and other placement settings across asset changes.

### How should multi-selected stamps scale and rotate?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Transform arrangement together (recommended) |  |
| 2 | Transform each around its own centre |  |
| 3 | Offer both modes |  |

**User's choice:** Scaling: individually in place. Rotation: as a group around a shared centre.

**Notes:** User gave a mixed rule instead of choosing a numbered option. Do not implement group scaling or individual rotation as the default.

### How should a stamp hidden beneath overlapping stamps be selected?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Alt-click cycles through stack (recommended) | Yes |
| 2 | Open a picker at pointer |  |

**User's choice:** Alt-click cycles through the stack; normal click selects the topmost visible hit and the object list remains available.

### Next area: scatter, or discuss stamp behavior further?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Next area: scatter | Yes |
| 2 | Discuss stamp behavior further |  |

**User's choice:** Next area: scatter.

## Scatter

### How should you create a scatter of stamps?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Paint with a scatter brush (recommended) | Yes |
| 2 | Fill a drawn area |  |
| 3 | Both |  |

**User's choice:** Paint with a scatter brush first; filling a drawn area can be considered later.

**Notes:** User explicitly deferred area filling.

### What should each scatter stroke use?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Selected set of assets (recommended) | Yes |
| 2 | One asset at a time |  |

**User's choice:** A selected set of assets, mixed as the user paints.

### Which scatter variation controls should be offered initially?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Size and rotation ranges, individually enabled (recommended) |  |
| 2 | Size, rotation and random horizontal flipping |  |
| 3 | Those controls plus tint and opacity variation |  |

**User's choice:** Size and rotation ranges, plus average distance between stamps.

**Notes:** User chose size/rotation and added average spacing; additional random flip/tint/opacity controls were not selected.

### How should scatter spacing be measured?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Map pixels between stamp centres (recommended) | Yes |
| 2 | Relative to stamp size |  |

**User's choice:** Average map-pixel distance between stamp centres, independent of zoom.

### Save Phase 2 decisions and continue to Phase 3, or explore more Phase 2 decisions?

| Option | Description | Selected |
| --- | --- | --- |
| 1 | Save Phase 2 decisions and continue to Phase 3 | Yes |
| 2 | Explore more Phase 2 decisions |  |

**User's choice:** Save Phase 2 decisions and continue to Phase 3.

## Design and engineering discretion

No blanket delegation was given. Visual details and unspecified defaults remain within the existing design-agent assignment. The mixed scaling/rotation rule, replacement-review behavior and scatter scope are settled.

## Deferred Ideas

- Scatter area filling: explicitly deferred for later consideration.
- Random scatter flipping/tint/opacity was offered but not selected. Existing P1/P2 boundaries remain.


## Amendment — 2026-09-24

The spacing choice above was recorded in map pixels, before Phase 1.1 normalized geometry (DOC-01). The decision keeps its meaning: an average centre-to-centre distance on the map, independent of zoom. Its unit is now **map units**, the fixed 1,000-unit document scale, so the result does not change with editing or export resolution. This record is otherwise preserved as discussed.
