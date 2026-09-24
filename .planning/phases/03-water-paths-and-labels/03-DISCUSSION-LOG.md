# Phase 3: Water, Paths and Labels - Discussion Log

> **Audit trail only.** Do not use as input to planning, research or execution agents. Decisions are captured in 03-CONTEXT.md; this log preserves alternatives and corrections.

**Date:** 2026-09-22–2026-09-23
**Phase:** 03-Water, Paths and Labels
**Areas discussed:** Drawing lakes and paths; Text and fonts; Layers and notes; Recovery comparison

## Drawing lakes and paths

### 1. How should you create a lake outline?

| Option | Description |
|---|---|
| 1 | Place points and shape curves (recommended) |
| 2 | Draw freehand and convert to curve |
| 3 | Start with an oval |

**User’s choice:** User asked why the existing shoreline-mask Land tool could not be used.

**Notes:** User challenged the dedicated spline-lake assumption rather than selecting a geometry mode.

### 2. Use Land for lakes or keep dedicated editable spline lakes too?

| Option | Description |
|---|---|
| 1 | Use Land tool for lakes; defer spline lakes (recommended) |
| 2 | Keep painted and editable spline lakes |

**User’s choice:** User noted painted lakes can use brushes with water texture/colour.

**Notes:** Assistant initially interpreted this as painted colour only. User explicitly corrected that interpretation before any lake-scope files were changed.

### 3. Clarification of the two desired lake workflows.


**User’s choice:** Both: create a lake with the Land tool and also have the option to paint it on with water texture or colour.

**Notes:** Accepted interpretation: Land Subtract edits Foreground coverage and reveals Background; water texture/solid-colour painting on Foreground changes colour without changing the mask. Dedicated spline-lake entities/modifiers deferred; river geometry unchanged. WATR-02 and current spec/design briefs amended. Earlier Phase 1 lake-modifier wording is superseded; concurrent Phase 1 planning files remain intact.

### 4. How should paths be created?

| Option | Description |
|---|---|
| 1 | Place points and shape curves |
| 2 | Draw freehand — turn stroke into editable path |
| 3 | Both |

**User’s choice:** Draw freehand, then turn the stroke into an editable path.

**Notes:** User selected 2.

### 5. How should smoothing work?

| Option | Description |
|---|---|
| 1 | Gentle automatic smoothing (recommended) |
| 2 | Preserve the stroke closely |
| 3 | Adjustable smoothing strength |

**User’s choice:** Gentle automatic smoothing: reduce small hand wobbles and produce a manageable number of editable points.

**Notes:** User selected 1.

### 6. After finishing a path, what should happen?

| Option | Description |
|---|---|
| 1 | Stay ready to draw another path (recommended) |
| 2 | Immediately edit the new path |

**User’s choice:** Stay ready to draw another path; select an existing path to edit its points.

**Notes:** User selected 1.

### 7. Continue to text and fonts?

| Option | Description |
|---|---|
| 1 | Move on to text and fonts (recommended) |
| 2 | Discuss lakes and paths further |

**User’s choice:** Move on to text and fonts.

**Notes:** User selected 1; area complete.

## Text and fonts

### 8. How would you prefer to enter and edit label text?

| Option | Description |
|---|---|
| 1 | Directly on the canvas (recommended) |
| 2 | In the side panel |
| 3 | Both |

**User’s choice:** In the side panel: select a label and edit its text in a field, with a live canvas preview.

**Notes:** User selected 2.

### 9. How should you shape a curved label?

| Option | Description |
|---|---|
| 1 | Simple bend first, detailed handles when needed (recommended) |
| 2 | Curve handles from the start |
| 3 | Simple bend only for now |

**User’s choice:** Three modes: straight; curve with peak deflection from -100% to 100%, where 100% is a circle arch; S-shape with two points using the same deflection percentage.

**Notes:** User supplied a custom three-mode model. Do not infer arbitrary Bezier-handle editing. Exact circular-arc extent at 100% and S-shape point/control semantics remain to clarify.

### 10. For Curve, what should 100% produce?

| Option | Description |
|---|---|
| 1 | A semicircle |
| 2 | A full circle |

**User’s choice:** A semicircle: half a circle; -100% bends the opposite way and 0% is straight.

**Notes:** User selected 1. Curve deflection range is -100% to +100%.

### 11. For S-shape, should the two bends be linked or independently adjustable?

| Option | Description |
|---|---|
| 1 | Linked and opposite (recommended) |
| 2 | Independently adjustable |

**User’s choice:** Linked and opposite: one percentage creates equal bends in opposite directions; changing its sign reverses the S.

**Notes:** User selected 1. No independently adjustable bend values requested.

### 12. How should the font picker work?

| Option | Description |
|---|---|
| 1 | All available fonts together (recommended) |
| 2 | Bundled fonts first |

**User’s choice:** Bundled fonts first: a curated list, with installed fonts in a separate section.

**Notes:** User selected 2. Offline font sources remain required.

### 13. When replacing a missing font, what should the default scope be?

| Option | Description |
|---|---|
| 1 | All labels using that missing font (recommended) |
| 2 | Only the selected label |

**User’s choice:** All labels using that missing font: preview the affected labels and apply the replacement together.

**Notes:** User selected 1. This is the default scope, not permission for automatic replacement without preview/apply.

### 14. Continue to layers and notes?

| Option | Description |
|---|---|
| 1 | Move on to layers and notes (recommended) |
| 2 | Discuss text and fonts further |

**User’s choice:** Move on to layers and notes.

**Notes:** User selected 1; area complete.

## Layers and notes

### 15. When choosing a tool needing a different layer type, how should the target change?

| Option | Description |
|---|---|
| 1 | Switch to the last-used compatible layer (recommended) |
| 2 | Ask to choose or create a compatible layer |

**User’s choice:** Each tool should keep track of its parameters independently. Stamp size, brush size and text size should not carry over.

**Notes:** User supplied an independent tool-settings requirement rather than choosing layer routing. Preserve separate parameter state per tool; this does not supersede shared Texture Brush settings across Background/Foreground targets.

### 16. Should switching tools restore the last-used compatible layer or keep the current layer selected?

| Option | Description |
|---|---|
| 1 | Restore that tool’s last-used compatible layer (recommended) |
| 2 | Keep the current layer selected |

**User’s choice:** Keep the current layer selected, but if the selected layer is Foreground or Background, switch to the topmost object layer.

**Notes:** User selected 2 with an explicit terrain-target exception. Interpret this in the context of tools that place objects, paths or text; existing Land/Texture Brush terrain targeting remains. Whether object layers mix stamps, paths and text requires clarification against earlier typed-layer wording. No silent unlock/show authorized.

### 17. Should an object layer be able to contain stamps, paths and text together?

| Option | Description |
|---|---|
| 1 | Yes, mixed object layers (recommended) |
| 2 | Keep separate layer types |

**User’s choice:** Yes, mixed object layers: all three can use the selected object layer.

**Notes:** User selected 1. Supersedes earlier separate typed-layer wording. Updated LAYR-02, current specification, architecture, roadmap and design briefs. Terrain roles remain fixed; tools retain independent parameters.

### 18. If no object layer exists when you first place an item, should one be created automatically or explicitly?

| Option | Description |
|---|---|
| 1 | Create an object layer automatically on first placement (recommended) |
| 2 | Ask to create a layer first |

**User’s choice:** One object layer should be created automatically for every map, just like the Foreground, Background and Grid layers.

**Notes:** User chose a stronger default: initial map setup creates one object layer, not lazy creation on first placement. Grid layer is part of the intended default setup; Grid implementation remains Phase 4. No deletion or grid-order policy inferred.

### 19. Should the last remaining object layer be protected from deletion?

| Option | Description |
|---|---|
| 1 | Yes (recommended) |
| 2 | No, recreate one when needed |

**User’s choice:** Yes: it can be emptied, but every map always retains at least one object layer.

**Notes:** User selected 1.

### 20. How should you add a pinned note?

| Option | Description |
|---|---|
| 1 | Note tool, then click the map (recommended) |
| 2 | Right-click the map then Add note |
| 3 | Both methods |

**User’s choice:** Both Note tool then click the map, and right-click the map then Add note; edit in the side panel.

**Notes:** User selected 3.

### 21. When you reveal notes, what should appear on the map?

| Option | Description |
|---|---|
| 1 | Pins only (recommended) |
| 2 | Pins with short previews |
| 3 | Full note cards |

**User’s choice:** Pins only: select a pin to read or edit its note in the side panel.

**Notes:** User selected 1. Existing default-hidden and export-excluded note behavior remains.

### 22. Continue to import recovery and comparison?

| Option | Description |
|---|---|
| 1 | Move on to import recovery and comparison (recommended) |
| 2 | Discuss layers and notes further |

**User’s choice:** Move on to import recovery and comparison.

**Notes:** User selected 1; area complete.

## Recovery comparison

### 23. How would you prefer to compare the original preview with the reconstructed map?

| Option | Description |
|---|---|
| 1 | Side by side (recommended) |
| 2 | Draggable divider |
| 3 | Toggle between them |

**User’s choice:** Can we just implement recovery from the last saved version? This whole thing sounds like a big complication.

**Notes:** User rejected comparison complexity and requested recovery from the last saved version. Simplified P0 to reopen latest durable Mapwright save, including acknowledged autosaves, preserving existing durability guarantees. Initial .ink import and a concise summary remain; dedicated comparison and post-import base reconstruction deferred. Asset/font replacement and ordinary undo remain.

### 24. Clarification of import comparison versus saved-project recovery.


**User’s choice:** For import reconstruction a side by side comparison should work.

**Notes:** User clarified that side-by-side comparison is wanted for import reconstruction. Supersedes the assistant interpretation that all import comparison should be deferred. Crash recovery still reopens the latest durable save. Scope of initial-import-only versus post-import reconstruction needs clarification.

### 25. Should reconstruction happen only during initial import or also later?

| Option | Description |
|---|---|
| 1 | Only during initial import (recommended) |
| 2 | Also later, preserving subsequent edits |

**User’s choice:** Only during initial import: review the side-by-side comparison, accept it, then edit the saved Mapwright project.

**Notes:** User selected 1. Post-import base reconstruction is deferred. Crash recovery is separate and reopens latest durable save, including acknowledged autosaves; ordinary asset/font replacement and undo remain.

### 26. Finalize Phase 3 and move to Phase 4?

| Option | Description |
|---|---|
| 1 | Finalize Phase 3 and move to Phase 4 (recommended) |
| 2 | Revisit Phase 3 |

**User’s choice:** Finalize Phase 3 context and design brief, then move to Phase 4.

**Notes:** User selected 1; all four areas complete.

## Agent discretion

No blanket delegation; unspecified design/engineering details remain proposals within the settled context. Earlier intermediate interpretations are superseded by later explicit clarifications.

## Deferred ideas

- Dedicated closed-spline lake entities/modifiers beyond P0.
- Post-import base reconstruction from changed asset mappings and reapplication of subsequent native edits.
