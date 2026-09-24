---
phase: "1"
slug: "connected-imported-terrain"
status: approved
shadcn_initialized: false
preset: none
created: "2026-09-22"
regenerated_from_mockups: "2026-09-22"
---

# Phase 1 — UI Design Contract

> Visual and interaction contract for Connected Imported Terrain. Regenerated from the local mockups and approved by `gsd-ui-checker`.

---

## Source Authority and Mockup Reconciliation

This contract replaces the provisional visual choices in the earlier `01-UI-SPEC.md`. Source precedence is:

1. `01-CONTEXT.md` locked decisions D-01 through D-28.
2. `REQUIREMENTS.md` and `ROADMAP.md` Phase 1 scope and acceptance.
3. `DESIGN-RESPONSE.md` and the local `design/*.dc.html` frames as the visual and interaction baseline where they do not mark values as proposed, sample, unmeasured, future-phase, or open.
4. The prior UI-SPEC only where not superseded.

### Local design provenance

| Contract area | Authoritative local evidence |
|---------------|------------------------------|
| Overall shell and 1920×1080 composition | `design/Main.dc.html` |
| 1366×768 compression | `design/Compact.dc.html` |
| Project entry and import review | `design/Start.dc.html`, `design/Import.dc.html`, `design/ImportReport.dc.html` |
| Terrain rows and fixed role identity | `design/Layers.dc.html` |
| Texture brush | `design/BrushInspector.dc.html` |
| Land tool and D-28 coverage cue | `design/MaskInspector.dc.html`, `design/LandCursor.dc.html` |
| River editing | `design/River.dc.html` |
| History and durable save feedback | `design/History.dc.html` |
| Export and restart recovery | `design/Export.dc.html`, `design/Recovery.dc.html` |
| Tokens and component states | `design/Tokens.dc.html` |
| Exceptional states and copy | `design/States.dc.html` |
| End-to-end behavior | `design/Flows.dc.html` |
| Keyboard and numeric interaction | `design/Shortcuts.dc.html` |
| Frame index and annotations | `design/README.md`, `design/canvas.json`, `DESIGN-RESPONSE.md` |

### Explicit reconciliation decisions

- D-19/D-20 supersede all mockup and design-response references to coastline stroke/fade/wave-ring/isoline controls or overlays. Phase 1 exposes none. The renderer may show one fixed **outer-coast** treatment only if engineering verifies separation from river/lake banks; otherwise the authorized unstyled fallback is the contract. Rivers remain decoratively unstyled.
- D-28 is implemented with spatial separation: **coverage is communicated at the Land cursor/footprint; whole-layer opacity is controlled and read only on the layer row; texture colour/intensity is communicated by Texture Brush identity, its swatch/name, and its texture-specific cursor feedback.** None may be labelled as another.
- The mockup grid row and DPI field are Phase 4 scope and must not ship as active Phase 1 controls. No disabled or inert future-feature row is shown.
- Sample project names, paths, revisions, texture names, counts, file sizes, durations, resource readings, tile/band/halo values, and recovery details are invented examples. Use live values only; do not turn them into requirements.
- Brush ceilings, river-width ceilings, roughness ranges, preset count/assets, autosave cadence, fixed coast values, terrain mapping heuristics, and export rounding remain engineering/product questions. They are listed under **Open Engineering/Product Decisions** and may not be hard-coded from a frame.
- Plain mouse-wheel zoom from D-13 takes precedence over the mockups’ `Ctrl+wheel` labels. `Space+drag` and middle-mouse drag remain temporary pan gestures.

No phase `RESEARCH.md` exists. The inspected project is Godot 4.7.2 .NET with one native `Control` root; the existing `Main.cs` UI is an evidence harness, not a design system or approved shell.

---

## Design System

| Property | Value |
|----------|-------|
| Tool | none — repository-owned Godot `Theme` resources and native `Control` composition |
| Preset | not applicable; this is Godot/C#, not React/Next/Vite |
| Component library | Godot 4.7 native `Control` nodes wrapped in shared project components |
| Icon library | Repository-owned 24-grid SVG line set, 1.7–2px strokes; no runtime icon package |
| Font | IBM Plex Sans for UI, IBM Plex Mono for every comparable/typeable number, Spectral 600 for product/display titles only; vendor fonts for offline use |

There is no installed UI package or registry to enumerate, so Component Inventory is intentionally omitted.

### Design character

Use compact native-desktop chrome around a dominant, colourful map canvas. Chrome is dark, desaturated, square-edged, and quiet. Selection uses cobalt; running work uses violet so it never reads as selection. Status always combines colour with text and, where space permits, a shape/icon. Do not use ornamental gradients, decorative animation, glass effects, or shadows that compete with map colour.

### Shared primitives

Implement reusable Godot components for: primary and secondary buttons; icon/tool buttons; segmented controls; labelled switches; slider-plus-numeric fields; preset/texture tiles; terrain rows; nested mask/modifier rows; inspector sections; tabs; inline alerts; non-modal job chips; determinate/indeterminate progress; import/export setup dialogs; and focusable splitters.

---

## Spacing Scale

Declared values are multiples of four:

| Token | Value | Usage |
|-------|-------|-------|
| xs | 4px | Icon gaps, slider tracks, tight inset |
| sm | 8px | Compact control/row gaps |
| md | 16px | Panel padding and form groups |
| lg | 24px | Major section/dialog padding |
| xl | 32px | Large card and project-entry gaps |
| 2xl | 48px | Screen-region separation |
| 3xl | 64px | Empty-state breathing room |

Exceptions: control dimensions are not spacing tokens. Use 3px control radius, 5px panel/dialog radius, 1px dividers, 4px visible splitters with an 8px effective grab area, 26px fields, 30px command-bar buttons, 34px dialog buttons, 24px minimum inline icon hit areas, and 40px tool buttons. At OS scaling these dimensions scale together.

---

## Typography

Use exactly four sizes and two weights in Phase 1:

| Role | Size | Weight | Line Height |
|------|------|--------|-------------|
| Caption / section | 11px | 400 or 600 | 1.45 |
| Dense row / numeric | 12px | 400 or 600 | 1.40 |
| Body / panel title | 13px | 400 or 600 | 1.50 |
| Display | 24px | 600 | 1.20 |

- IBM Plex Sans is the default face; IBM Plex Mono is mandatory for dimensions, coordinates, percentages, degrees, revision IDs, counts, paths when compared, and live resource values.
- Spectral 600 is limited to the product mark and display/dialog titles. UI controls remain IBM Plex Sans.
- No rendered text is below 11px. The 9–10px annotations in mockups must be promoted to 11px in implementation.
- Sentence case for actions and labels. Preserve the full nouns **Background**, **Foreground**, **Coverage**, **Texture**, and **Layer opacity** in accessible labels even where `BG`/`FG` role chips are also shown.

---

## Color

| Role | Value | Usage |
|------|-------|-------|
| Dominant (60%) | `#0D0F16` | App ground, canvas surround, dialog ground |
| Secondary (30%) | `#161923` | Panels, sidebars, rows; use `#12141D` for bars and `#1D2130` for headers |
| Accent (10%) | `#5C6FE0` | One primary action, selected tool, selected editable target, active tab edge, measured selection/progress track |
| Destructive | `#E2685A` | Destructive or failed states only |

Accent reserved for: the selected tool, selected layer/target, the enabled primary CTA, active segmented choice, active tab edge, and selected slider fill. Do not use cobalt to mean background work.

Supporting tokens:

| Role | Value | Contract |
|------|-------|----------|
| App bar | `#12141D` | Top/status bars |
| Panel header | `#1D2130` | Inspector/list headers |
| Control | `#252A3A` | Secondary buttons and neutral inputs |
| Hairline | `#2F3447` | Dividers and panel borders |
| Control border | `#3B4159` | Inputs and neutral controls |
| Primary text | `#ECEDF3` | Primary copy; at least 4.5:1 |
| Secondary text | `#ADB3C6` | Labels/supporting copy |
| Muted text | `#8990A6` | Metadata; never below 11px |
| Selected fill/text | `#23294A` / `#C6CFFF` | Selected row/tool plus border or icon cue |
| Focus ring | `#93A6FF` | 2px ring with 2px separation on every focusable control, including canvas |
| Durable/success | `#5FAE7F` | Acknowledged save, verified publication; always with words |
| Pending/warning | `#D9A23F` | Queued save or caution; warning border may use `#C98A45` |
| Working/frozen revision | `#9D7BE0` | Running jobs and frozen export identity; always with words |

Map textures and the translucent D-28 coverage tint are content/feedback colours, not chrome tokens. Status meaning may never rely on colour alone.

---

## Copywriting Contract

| Element | Copy |
|---------|------|
| Primary project-entry CTA | **Import an .ink into a new project…** |
| Secondary project-entry CTA | **Open project folder…** |
| Empty state heading | **No project is open** |
| Empty state body | **Import an Inkarnate .ink backup or open an existing Mapwright project. Everything stays on this machine, and your source file is never modified.** |
| Generic open/import error | **This file could not be opened. Nothing was written and your source file is untouched. Review the details, then choose another file or try again.** |
| Cache deletion confirmation | **Delete render and history acceleration caches? Sources, hashes, native edits, and authoritative history stay intact. The next open and export will rebuild disposable data.** Actions: **Delete caches**, **Keep caches** |

### Required state copy

Use live values in braces; do not substitute mockup sample data.

| Situation | Required wording and action |
|-----------|-----------------------------|
| Import mode choice | Heading **Recovery mode**. Cards **Editable recovered terrain** and **Original flattened appearance**; neither preselected. Primary action **Create project** stays disabled until a mode is chosen. Flattened copy must say Background is the locked embedded preview, Foreground starts empty, and baked coasts are pixels rather than editable geometry. |
| Partial recovery | **Imported with degradation. {Recovered summary}. {Skipped summary}. You can paint and export now; skipped content remains listed in the import report and visible in the preserved preview.** Actions **Open the project**, **See report**. |
| Oversized input | **This map declares {width} × {height} pixels. Mapwright edits documents up to 16,384 × 16,384, so it was not opened. Nothing was written and your file is untouched.** Actions **Import preview only** when safe, **Choose another file**. |
| Locked target | **{Role} is locked. Unlock {Role} to paint it.** Inline action **Unlock**. Never reroute. |
| Hidden target | **{Role} is hidden. Show {Role} to paint it.** Inline action **Show layer**. Never reroute. |
| No paintable layer | **The selected target cannot be painted. Choose Show layer or Unlock; the stroke was not applied anywhere.** |
| Texture tool semantic reminder | **Painting on {role} · texture painting does not change land coverage.** |
| Missing texture | **{texture} is missing. Strokes keep their recorded identity and use a marked placeholder until it is found.** Actions **Locate…**, **Choose another texture**. |
| Empty History | Heading **No edits yet**. Body **Committed strokes and property changes will appear here. Choose a tool, then edit the canvas to create the first undoable step.** Action **Choose Texture Brush**. |
| Empty texture chooser | Heading **No local textures available**. Body **Locate a texture file to make it available in this project. Painting stays disabled until a usable texture is selected.** Action **Locate texture…**. |
| Save states | Exactly **Unsaved — {count} commands queued**, **Saving — waiting for queued commands**, **Saved {time}**, **Save failed — {cause}**. `Saved` means durable acknowledgement. |
| History rebuild | **Rebuilding an older step** plus real units, current revision, and **Cancel history rebuild**. Supporting copy: **Cancelling leaves you exactly at {current revision}; nothing is half-applied.** |
| Export rendering | **Exporting {frozen revision}** plus real units and **Cancel export**. State that editing may continue on a newer revision. |
| Export validating | **Validating** and **No percentage available**; never invent progress. |
| Export cancelled/failed | **Nothing was published. Your previous {filename} is untouched.** Add the concrete cause and next action. |
| Export success | **Published** plus revision, dimensions, and destination. Duration may be reported but is never a pass/fail promise. |
| Recovered restart banner | **{Project} was recovered. The last acknowledged revision and undo position were restored. An unfinished gesture may be missing.** Actions **Continue editing**, **View recovery details**. This is non-blocking. |
| Backend unavailable | **Graphics backend unavailable. The rendering editor cannot open, but your project is untouched. Inspection and source recovery remain available.** Actions **Open inspection view**, **Copy diagnostics**. |

Deleting a river point or river modifier is undoable and must not open a confirmation dialog; perform it as one command and announce **River deleted — Undo available**. Export overwrite is protected by temporary-sibling validation and atomic publication, so show an overwrite warning rather than a destructive confirmation.

---

## Information Architecture and Layout

### Project entry and import

- Launch to recent projects with **Open project folder…** and **Import an .ink into a new project…**; never auto-open the last map.
- Recent rows show name, last durable revision/time, path, and a readable missing-folder state. Long names/path text ellipsize with the full value in tooltip/accessibility text.
- Import setup is a two-column 1100×740 reference surface: 460px source/preview column and flexible recovery/project column. Preserve the preview in either mode.
- Display source file, dimensions/version when known, concise recovered/unresolved summary, immutable-source statement, two unselected mode cards, project name, configured projects-folder destination, and **Elsewhere…**.
- Import setup may be modal; running import is inline/cancellable and must state that nothing publishes until commit.

### Editor shell at 1920×1080

| Region | Contract |
|--------|----------|
| Title strip | 34px fixed. Product/project name left; revision and document dimensions visible without taking canvas space. |
| Command bar | 44px fixed. **Open project**, **Save project**, **Import .ink**, **Export PNG**; persistent Undo/Redo; durable save chip. No coast/ring/isoline controls. |
| Tool rail | 56px fixed; 40px buttons. Phase 1 tools: Pan, Texture Brush, Land, River, Sample Texture, Project Storage. No disabled future tools. |
| Tool panel | 318px default, resizable 260–420px, scrollable, collapsible with Tab while tool remains active. Height follows content; do not force decorative empty sections. |
| Canvas | All remaining space, minimum 480px. Owns pan/zoom, cursors, river handles, non-modal job chip, scale/coordinate feedback, and immediate D-28 state. |
| Layers/History column | 340px default, resizable 280–520px. Tabs **Layers** and **History**. Phase 1 Layers contains only Foreground, its mask/river attributes, and Background; no grid or future-layer placeholders. |
| Status bar | 30px fixed. Operation/save state; coordinates; active role/tool; live resource readings only when available; zoom/fit. |
| Splitters | 4px visible, 8px grab; live drag, keyboard reachable, preserve session sizes. |

Use a `VBoxContainer` for title/command/work/status and chained `HSplitContainer`s for rail/tool/canvas/layers. Tool and layers contents use `ScrollContainer` with follow-focus. Canvas overlays are `_draw()` operations on one focusable `Control`, with no per-pointer scene-node allocation.

### Compression at 1366×768 and 150% scale

- Below 1500px, merge title and command bars into a 38px bar. Collapse project commands into **File** and nonessential view actions into a menu.
- Rail becomes 48px with 36px visual buttons; effective hit areas continue scaling with the OS.
- Tool panel defaults to 252px and layers/history to 272px. Lower-priority numeric groups may collapse into labelled disclosure sections; no control disappears.
- Terrain rows become one line: role chip never truncates; display name ellipsizes; opacity number remains visible.
- Status bar becomes 28px; retain save state, active role/tool, and zoom. A job chip loses explanation before losing revision, progress unit, or its context-specific cancellation action: **Cancel import**, **Cancel history rebuild**, **Cancel cache rebuild**, or **Cancel export**.
- At 150% display scaling, scale theme metrics and fonts together. Apply compressed rules sooner and scroll panels; never shrink text below 11px or remove focus cues.

---

## D-28 Editing-Scope Contract

The user must be able to distinguish coverage, texture colour/intensity, and whole-layer opacity on the canvas or in its immediate editing state without opening another panel and without a legend.

| Scope | Immediate presentation | Semantic invariant |
|-------|------------------------|--------------------|
| Foreground coverage | Land cursor fills only its footprint with a translucent accent tint, has a high-contrast outline/centre, and carries `Land tool · Add/Subtract · Foreground` plus diameter/mode. The Foreground land-mask attribute is highlighted in Layers. Tint disappears with the cursor and never exports. | Add/Subtract changes Foreground coverage, reveals Background, and can move the coastline. It does not paint texture and is not layer opacity. |
| Texture colour/intensity | Texture Brush cursor is an unfilled high-contrast outer diameter ring, dashed inner hardness/falloff ring, dashed affected-region box, centre point, and readout containing diameter, hardness, rotation, and selected texture/preset name. Tool header says `Painting on {role} · texture painting does not change land coverage`; selected swatch/name stays visible. | The stroke changes texture colour/intensity on the selected terrain target. It does not alter coverage or whole-layer opacity. |
| Whole-layer opacity | Opacity exists only in each terrain row as a bar plus monospaced percentage and accessible label `{role} layer opacity`. During drag/type, no coverage tint or brush affected-region fill appears; status copy reads `Layer opacity · {role} · {value}%`. | Opacity fades the whole terrain layer and does not move the coastline, change the mask, or deposit texture. |

Coverage tint and layer opacity never share a control surface. Texture feedback never uses the coverage tint. Tool, target, and affected property are always recoverable from explicit text plus spatial feedback, not colour alone. Land and River select Foreground’s mask; returning to Texture Brush restores the previous Background/Foreground texture target.

At very small on-screen footprints (approximately under 6px), collapse the rings to a crosshair but keep the numeric/semantic readout. This threshold is presentation guidance, not a renderer limit.

---

## Surface and Component Contracts

### Layers and fixed terrain model

- Exactly two terrain rows, Foreground above Background. Roles/order are fixed. There are no terrain add, duplicate, delete, drag, or reorder affordances.
- Each row exposes visibility, stable `FG`/`BG` role chip plus accessible full role, user-editable display name, solo, lock, and layer opacity. The role chip never truncates.
- Foreground owns an indented **Land mask** attribute with **Edit**; the river is an indented enabled/disabled modifier under it. Neither is a layer.
- Flattened-import Background carries **locked base** and **baked coasts** text tags. Baked coasts are pixels, not editable coverage.
- Locked or hidden target gestures refuse at the canvas and provide **Unlock** or **Show layer**. Do not auto-change state or paint the other role.
- Solo semantics beyond showing the chosen terrain role without the other are unresolved. Do not add a mask-only solo mode unless separately decided.

### Texture Brush

- Header: **Texture brush**, shortcut `B`, active target selector **Background / Foreground**, lock/visibility state, and semantic reminder.
- Preset picker includes name, texture/tip identity, tip/stroke preview, **Save preset as…**, and a visible tapered marker when applicable. Exact inventory/count/assets are open.
- Tip selector: **Round** and **Edged**. Both include diameter and hardness. Edged additionally shows Roughness and **Smooth corners**. Tapered presets narrow both stroke ends without pressure.
- Stroke group: **Brush opacity**, Flow, Spacing. Texture group: local texture chooser, Scale, Rotation, Jitter, and read-only indication that a stroke-local seed is retained for replay.
- Missing textures are marked both on their tile and in an inline warning. The Phase 1 chooser is local/recent only; folders/tags/search/marketplace belong to Phase 2.
- No in-panel effect thumbnail. The map cursor is the footprint/falloff/affected-region preview.

### Land tool

- Header: **Land tool**, shortcut `L`, fixed target **Foreground’s land mask**.
- Exactly two modes using icon plus label: **Edged polygon** and **Round soft**.
- Shared controls: Add/Subtract and diameter in document pixels. Hold Alt to invert Add/Subtract only while drawing.
- Edged polygon adds Roughness and **Smooth corners**. Smooth rounds sharp footprint corners while keeping a firm irregular edge; it is not alpha softness or pointer stabilization.
- Round soft adds Softness. It does not share texture tips/presets.
- No strength/falloff control beyond the mode-defined Softness, no square mode, no mask layers, no coastline style controls, no coast preview toggles, no wave rings, and no isolines.

### River

- Header names the selected river and **Modifier of Foreground land mask**; include labelled enable switch.
- Canvas shows centreline, points, a larger selected point, width handles, and live channel/bank-softness feedback. River banks are unstyled.
- Inspector shows target, point list/count, selected point position, per-point width, bank softness, width profile, **Apply width to all**, **Delete point**, and undoable **Delete river**.
- One drag is one command on release; Escape restores the prior point/width/softness. Land painting composes before the river modifier and cannot close the channel.

### History and durable save

- Undo/Redo remain visible when disabled; tooltip names the next action or explains no action exists.
- History is virtualized. Rows show action label, durable revision, timestamp, current cursor, and relevant tile/count metadata only when live data exists.
- New edits after undo discard redo transactionally; the UI may briefly explain which redo steps were discarded after the new edit commits.
- Older reconstruction is a non-modal working panel with real units and **Cancel history rebuild**. Pan/zoom remains available; editing is blocked until completion/cancellation.
- Save indicator uses the four exact state names in Copywriting Contract. Green/durable is forbidden while any command is queued.

### Export and recovery

- Phase 1 export setup includes destination, width/height within 16,384 per axis, aspect-preserving presets only if implementation confirms their rounding behavior, frozen revision, overwrite warning, and **Start export**. Exclude DPI, grid, labels, effects, and Phase 4 preflight controls.
- Export job is a non-modal canvas chip/panel. Name real phases such as Preparing, Rendering, Encoding, Validating, Publishing; show percent only from known completed/total units. Frozen revision and **Cancel export** remain visible under compression.
- Use temporary sibling output; only validated atomic publication replaces an existing destination. Success/failure/cancel copy must match the actual publication state.
- Restart recovery opens the editor and presents a brief non-blocking banner. Details may open a sheet/panel listing only what the journal/source verification proves. Do not invent an exact lost gesture, cause, command count, or timestamp when unknown.
- Cache rebuild is non-modal. If editing is safe while visible tiles sharpen, say so; otherwise state inspection-only and block edits. Copy must follow the actual implementation branch.

---

## Component and Interaction States

| Component | Required states and behavior |
|-----------|------------------------------|
| Primary/secondary button | Default, hover, focused, pressed, disabled, busy. Busy retains width and verb. Disabled has an adjacent reason or tooltip. |
| Tool/icon button | Default, hover, focused, selected, disabled. Selected uses fill + border + icon/name context; tooltip includes shortcut. |
| Segmented control | Default, hover, focused, selected, disabled. Text labels remain visible for target and Add/Subtract. |
| Switch | Off, on, focused, disabled; always labelled in text and reflected in row/status wording. |
| Slider + numeric field | Default, hover, focused, transient drag, invalid, disabled. One shared value; release commits once; Escape reverts. |
| Preset/texture tile | Empty, loading, populated, missing, focused, selected. Preview never replaces name/accessibility text. |
| Terrain row | Default, selected target, tool-required target, hidden, locked, solo, long-name, baked-fallback. Role remains visible. |
| Inspector section | Expanded/collapsed, unavailable with reason, busy, validation error. Header identifies tool/target. |
| Progress/job panel | Indeterminate, determinate, cancelling, cancelled, failed, complete. Working uses violet and words. |
| Alert/banner | Information, durable success, degraded warning, failure, recovery, blocked-target action. Inline in the owning surface. |
| Dialog | Initial focus, focus trap, enabled/disabled primary action, cancelling/busy, validation error. Only setup/confirmation dialogs may block. |

Alerts state what happened and what to do next. Hover is never the only route to required information. Focus ring applies to canvas, splitters, rows, fields, tiles, and every button.

---

## Numeric Input and Gesture Contract

- Pair editable numeric values with a monospaced field and, where continuous adjustment helps, a slider. Labels may scrub; one scrub is one command.
- Arrow keys step 1; Shift+Arrow steps 10; Alt+Arrow steps 0.1 only where the domain permits decimals.
- Enter commits a valid value and returns focus to the canvas where appropriate. Tab commits valid content and moves within the panel. Escape restores the last committed value.
- Invalid text stays visible, field stays focused, red error treatment and concise allowed-range copy appear, and no document command is created. Clamp on Enter only where the field’s documented behavior says so; do not silently clamp while typing.
- `Ctrl+Z` inside a focused field edits field text, not document history. Tool letters never fire from a field.
- Percent controls explicitly defined as percentages use 0–100 display. Brush diameter and river width use document pixels. Texture rotation uses degrees and may wrap only after engineering confirms the domain representation. Layer opacity is always labelled with **Layer**.
- Size/width/roughness/scale/spacing/jitter bounds and all initial defaults come from the validated domain model. The UI must not hard-code the proposed 1–4096, 1–2048, or 1–20 mockup ranges.
- Brush strokes, river edits, and property drags are transient previews. Pointer release submits one command. Escape, lost capture, deactivated window, or a newly opened modal cancels the complete uncommitted gesture and restores prior state.

---

## Input and Shortcut Scope

| Action | Shortcut | Scope |
|--------|----------|-------|
| Texture Brush / Land / River / Pan | `B` / `L` / `R` / `H` | Canvas only; inert in fields/modals |
| Temporary pan | `Space`+drag or middle-mouse drag | Canvas; restores previous tool on release |
| Pointer-centred zoom | Mouse wheel | Canvas; plain wheel per D-13 |
| Pan by wheel | Use scroll-wheel panning only if it does not conflict with D-13 zoom; default to no wheel-pan binding | Canvas |
| Diameter down/up | `[` / `]` | Canvas with brush-capable tool |
| Invert Land Add/Subtract | Hold `Alt` | Land gesture only |
| Cancel gesture | `Esc` | Cancels current preview before any surface close action |
| Delete selected river point | `Delete` | River canvas/point list |
| Save project / Undo / Redo / Export PNG | `Ctrl+S` / `Ctrl+Z` / `Ctrl+Shift+Z` / `Ctrl+Shift+E` | Application except modal/text overrides |
| Field text undo | `Ctrl+Z` | Focused text/numeric field only |
| Focus regions | `F6` | Canvas → tool panel → layers/history → status |
| Collapse tool panel | `Tab` only when canvas/panel scope owns it; never steal Tab from focus traversal | Editor |

Do not implement the mockup’s `Ctrl+wheel` requirement. Do not make `L` both a tool shortcut and a lock toggle in the same focus scope; row lock is activated by its labelled button.

---

## Exceptional-State Matrix

| State | Surface | Contract / next action |
|-------|---------|------------------------|
| No project / no recents | Project entry | Empty copy plus Open and Import CTAs. |
| Import loading | Import setup | Stable preview region, real phase/units when known, **Cancel import**; nothing published until commit. |
| Oversized input | Import outcome | Reject before unsafe decode where possible; quote declared dimensions/limit and offer safe preview-only path only when supported. |
| Corrupt/unsupported input | Import outcome | Name the parsing/decompression problem without raw stack traces; nothing written; choose another/export again/copy details. |
| Partial recovery | Review/report/editor | Preserve preview and unsupported metadata; show recovered/skipped summary, report link, and baked-fallback tags. |
| Locked target | Canvas + Layers | Refusal cursor, **Unlock**. No reroute. |
| Hidden target / both hidden | Canvas + Layers | Refusal cursor, **Show layer** / **Show all**. No invisible painting. |
| Missing texture | Texture inspector | Mark tile, keep identity, placeholder, Locate/choose actions. |
| Invalid numeric | Field | Preserve typed value, inline error, no command. |
| No undo/redo | Command bar + History | Visible disabled controls and explanatory tooltip/empty copy. |
| Older undo rebuilding | History + canvas job | Real progress, **Cancel history rebuild**, current revision named; pan/zoom only. |
| Queued/save failure | Command/status bar + inline alert | Honest queue or cause; Retry/alternate safe action; prior durable revision remains identified. |
| Export cancelled/failed | Canvas job/export surface | Previous destination intact; retry/change folder/details. |
| Cache rebuild | Canvas job/banner | Explain disposable nature and whether editing is currently safe. |
| Fresh-process recovery | Non-blocking editor banner + details | Restored acknowledged revision; unfinished gesture may be absent; report link. |
| Render backend unavailable | Entry/recovery shell | Inspection/source recovery available; editing/export disabled. |
| Resource pressure | Status/job panel | Use live readings and outcome-focused copy such as **Cache full — evicting distant tiles** or **Export working set reduced**; never claim unmeasured values. |

---

## UI Considerations

> This complete section is retained for the post-verification probe to replace idempotently. Empty/error copy is defined above; this table records state coverage and testable behavior.

The UI state probe identified 76 applicable element/state checks. The eight category rows below group their explicit resolutions: 76 resolved, 0 backstop, 0 unresolved. The acceptance behavior remains the text in this contract, not the probe's category count.

| Category | Element(s) | Status | Resolution / Reason |
|----------|------------|--------|---------------------|
| empty | Recent projects, History, texture chooser, import report | ✅ covered | Zero recents shows the documented project-entry copy and both CTAs; empty History shows **No edits yet**, the first-step guidance, and **Choose Texture Brush**; an empty texture chooser shows **No local textures available**, explains why painting is disabled, and offers **Locate texture…**; an empty report explains the next usable action. |
| loading | Import preview, recent projects, texture preview, history reconstruction, cache/export jobs | ✅ covered | Preserve region geometry; use phase text plus determinate units only when total work is real, otherwise indeterminate progress; use the matching label **Cancel import**, **Cancel history rebuild**, **Cancel cache rebuild**, or **Cancel export** where cancellation is safe. |
| error | Project open/import, texture resolution, numeric commit, save, history rebuild, export, backend startup | ✅ covered | Keep the prior usable/durable state, state the concrete problem, and expose retry/details or a safe alternate action without hiding the canvas unnecessarily. |
| populated | Recent projects, textures/presets, terrain rows, History, report rows | ✅ covered | Use bounded scroll regions and consistent row/tile anatomy; fixed terrain roles remain pinned and role-labelled. |
| partial | Import recovery, fallback terrain, incomplete source/texture metadata | ✅ covered | Show supported/partial/missing summary beside the preserved preview; retain source identity/unsupported data and mark baked fallback at the affected row. |
| overflow | Recent projects, project/layer names, paths, preset library, History, inspectors, reports | ✅ covered | Scroll only the owning region; names/paths ellipsize with full accessible text; prose wraps; virtualize History; never cover the canvas cursor/job state. |
| zero-one-many | Recent projects, import issues, river points, History, background jobs | ✅ covered | Use grammatical singular/plural live copy; one job is summarized inline, multiple jobs use a count/details path; lists remain bounded or virtualized. |
| long-text | Project/layer/texture names, paths, errors, recovery report, buttons, D-28 readouts | ✅ covered | Preserve role/tool/scope nouns and action labels; ellipsize optional names/paths, wrap reports/errors, and expose full text in tooltip/accessibility metadata. |

---

## Open Engineering/Product Decisions

These are not implementation requirements and must be resolved or explicitly deferred in planning:

| Question | Planner constraint |
|----------|--------------------|
| Fixed outer-coast style or D-20 unstyled fallback? | Choose after feasibility evidence. No style controls in either branch; record chosen branch/reason at verification. If styled, verify river/lake banks and mouths remain unstyled. |
| Fixed coast values and reach | Do not adopt mockup 6/104/72/48px examples. Values/reach must come from the verified rendering branch and bounded resource model. |
| Brush/river/roughness/scale/spacing/jitter bounds and defaults | Derive from domain/renderer validation; do not hard-code mockup examples. |
| Exact texture preset inventory/assets and taper curve | Research/design task; preserve required capabilities and persisted resolved parameters without promising a named count. |
| What renders beneath unpainted Background | Decide project colour versus transparency and ensure viewport/export agree. |
| Foreground solo and mask-only inspection | Basic terrain solo exists; mask-only solo is not approved and must not be inferred. |
| Autosave cadence | Durability work must choose cadence; UI reports queue truthfully without promising an interval. |
| Import report lifetime/reopen route | Confirm persistence before promising a permanent File-menu route. |
| Source terrain-to-role mapping | Validate against real `.ink` data; preserve/report extras rather than guessing. |
| Export aspect rounding and preset availability | Presets are optional until rounding is specified. Width/height/max/frozen revision remain required. |

---

## Registry Safety

| Registry | Blocks Used | Safety Gate |
|----------|-------------|-------------|
| None | None | Not applicable — native Godot project; no shadcn or third-party UI registry |

---

## Implementation Boundaries

- UI events submit authoritative application commands; transient canvas previews never become saved/exported state by appearance alone.
- Viewport and export consume the same immutable revision/render graph. Colour/alpha, seam, resource, latency, and conditional distance checks are verification obligations, not user controls.
- Import/export setup may be modal. Running import, save, history reconstruction, cache rebuild, export, and recovery feedback are non-modal wherever the requirements allow inspection/editing.
- History list is virtualized. Canvas overlays allocate no nodes per pointer update and use no decorative tweening.
- Resource readings display live measured/configured values only. Mockup numbers are not defaults or evidence.
- Do not expose pen pressure, custom tip import, arbitrary terrain layers, additional mask layers, coastline styling, wave rings, isolines, grid/DPI/label settings, asset management, object/path/text tools, accounts/cloud, marketplace, themes, docking, or multi-window behavior in Phase 1.

---

## Verification Checklist

- [ ] Start screen opens to recent projects with Open and Import; last project is not auto-opened.
- [ ] Import review preserves the source preview, shows bounded outcomes, requires one of two unselected modes, and defaults destination to the configured projects folder with override.
- [ ] Editor shows exactly Foreground over Background; no Phase 4 grid row, future layer placeholder, or terrain add/reorder affordance ships.
- [ ] D-28 passes at 100% and 150% scale: coverage is at the Land cursor, opacity is on the layer row, and texture feedback is explicitly texture-specific.
- [ ] No coastline control, coast preview toggle, ring, or isoline appears. River banks remain unstyled; outer-coast branch matches verification choice.
- [ ] Texture, Land, and River panels contain the scoped controls above and no proposed numeric ceiling is hard-coded without domain evidence.
- [ ] Locked/hidden targets refuse and offer Unlock/Show layer without rerouting.
- [ ] Mouse wheel zoom is pointer-centred; Space+drag/middle-drag pans temporarily; tool shortcuts do not fire in fields.
- [ ] Escape cancels a whole uncommitted stroke/property drag; pointer release commits one undo step.
- [ ] Save status reflects durable acknowledgement; older history reconstruction leaves pan/zoom and **Cancel history rebuild** available.
- [ ] Export names its frozen revision, reports real progress only, preserves the prior destination on cancel/failure, and excludes Phase 4 settings.
- [ ] Recovery is non-blocking and claims only journal/source facts actually known.
- [ ] 1920×1080, 1366×768, and 150% scale preserve save state, active tool/target semantics, focus, and reachable controls.
- [ ] Every icon-only control has an accessible name, tooltip where helpful, visible focus, and the prescribed hit area.

---

## Checker Sign-Off

- [x] Dimension 1 Copywriting: PASS
- [x] Dimension 2 Visuals: PASS
- [x] Dimension 3 Color: PASS
- [x] Dimension 4 Typography: PASS
- [x] Dimension 5 Spacing: PASS
- [x] Dimension 6 Registry Safety: PASS
- [x] Dimension 7 Inventory Provenance: PASS (not applicable because Tool is none and inventory is omitted)

**Approval:** verified 2026-09-22; 7/7 dimensions passed, 0 recommendations.
