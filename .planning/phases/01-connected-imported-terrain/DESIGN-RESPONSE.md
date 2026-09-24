# Phase 1 design response: Connected Imported Terrain

Date: 2026-09-22. Status: **proposed**. This answers [01-DESIGN-SPEC.md](01-DESIGN-SPEC.md), which is preserved unchanged. Where the two disagree, the design follows [REQUIREMENTS.md](../../REQUIREMENTS.md) and the [working specification](../../../docs/spec.md) as amended on 2026-09-22, not the brief — §12 lists every place that happens. Nothing here is evidence that the application works, that recovery succeeds or that any latency target is met; these are the interfaces those behaviours must be able to justify.

**Deliverable:** seventeen annotated frames, in [design/](design/) beside this file and on an interactive canvas at <https://claude.ai/artifact/8E1beuFrSWxV9tm3EvGLqF> (private; share from the page's Share menu before sending it to anyone else). Named below by screen ID. This document carries the tokens, contracts, tables and open questions.

## 1. Decisions taken

| # | Decision | Why |
| --- | --- | --- |
| D1 | **Two terrain layers**, Foreground over Background, with the land mask owned by Foreground | Set by the brief’s 2026-09-22 amendment, after Inkarnate reference screenshots. LAYR-01 and MASK-01 were amended to match on the same day, so requirement and design now agree. See §2. |
| D2 | Dark, desaturated chrome; all saturation reserved for map content | The map is the subject. Chrome colour would compete with terrain and coast. |
| D3 | Left icon rail with the **active tool's panel docked beside it**; layers on the right | Matches the mental model the user already has, and keeps the tool's own controls next to the tool that owns them. |
| D4 | One accent (cobalt `#5C6FE0`) plus four status hues, on a navy-leaning neutral ground | Green chrome was rejected by the owner on 2026-09-22. Cobalt keeps "selected" unambiguous; violet carries in-progress so it cannot be confused with it. |
| D5 | Every user-visible number is monospaced | Values line up and a changing readout does not reflow its row. |
| D6 | Desktop density: 24–30 px controls, 40 px tool buttons | Deliberately below the 44 px touch guidance; tablet input is a pointer in Phase 1. Stated as a deviation, not an oversight. |
| D7 | Three words kept apart everywhere: **preview / commit / save** | The durability requirements are meaningless if the UI blurs them. |
| D8 | No modal blocks the canvas while a job runs | Import and export *setup* are modal; import, export, cache rebuild and history reconstruction are not. |
| D9 | Import asks the recovery question every time, with neither answer preselected | The two modes differ in what stays editable; a remembered default would quietly decide that for the user. |
| D10 | Export offers 1080p / 2K / 4K / 8K / 16K presets that set the long edge | Requested on the canvas, 2026-09-22. The short edge follows the map’s proportions, so a preset cannot distort the map. |
| D11 | **No previews inside tool panels.** The map is the preview — neither the land tool nor the texture brush carries a thumbnail of its own effect | Owner’s call, 2026-09-22. A panel diagram of the cursor competes with the real cursor two inches away, and costs the space the controls want. Panel height now follows the tool: 500 px for the land tool, 700 px for the texture brush. |
| D12 | **Coverage is a cursor decoration, opacity is a layer row.** Neither appears anywhere the other does | Owner’s call, 2026-09-22. The two read alike, so separating them by place settles it where a legend would only explain it. Drawn in P1-02c. |
| D13 | **No coastline controls.** The coast renders from the mask with fixed values; nothing about stroke, fade, wave rings or isolines is exposed in P0 | Owner’s call, 2026-09-22. MASK-01 requires the user to *see* those effects, not to tune them, so the requirement still holds — but the brief’s P1-05 row asks for the controls and now needs amending. See §12. |

## 2. The layer model

The brief's 2026-09-22 amendment settles this, and the frames follow it. **Two terrain layers, fixed roles and order:**

```
Grid                                      square; hidden by default; export toggle
[object, path and text layers — Phase 2+, created and reordered freely]
Foreground        painted; owns the land mask
  └ land mask     land vs sea; the coastline is its 0.5 contour
      └ river     modifier of that mask (lakes later)
Background        painted; revealed where the mask is subtracted
```

The **grid** is a real layer row at the top of the stack, not a view toggle hidden in a menu: it is content that can be exported, so it belongs where a user goes to turn content on and off. It carries a visibility toggle and a settings affordance.

Note the scope edge: the grid is **NOTE-01**, which the roadmap assigns to **Phase 4**, and it is not among Phase 1’s owned IDs. The row is here because the owner asked for it on 2026-09-22. Phase 1 therefore ships the row and its visibility only; spacing, colour and the export toggle stay Phase 4, and the gear opens nothing yet rather than showing controls that do not work.

No add, duplicate, delete or reorder control exists for either terrain layer. The mask is not a row of its own — it belongs to Foreground, shown as an indented attribute under it with its own Edit affordance. The river is an indented modifier under the mask. Neither masking nor river editing creates a layer, and the frames say so in words where a user would otherwise wonder.

**Why the mask sits on Foreground and not somewhere neutral.** MASK-01 derives the coastline from *the composed 0.5 contour* — a single contour. One mask, owned by the upper layer, makes that exact, and makes "subtract to reveal Background" the single mental model for both coast shaping and terrain blending. It also makes WATR-01's "later land painting cannot close the river" structurally true rather than a rule to police: the river modifies the mask after painting, so no stroke can seal the channel.

**Three things that must stay distinguishable.** Texture colour is never really in doubt — it is the map’s own pigment. Coverage and layer opacity are, because both read as "less of this layer". The design separates them by place rather than by explanation, and P1-02c draws it:

| | Where it appears | What it does |
| --- | --- | --- |
| **Coverage** | Under the cursor only, as a tint filling the footprint | Decides land, sea and where the coast runs |
| **Layer opacity** | The layer row only, as a bar with a number | Fades all of Foreground; the coastline does not move |

Neither is ever shown anywhere else, so the two never share a surface and never need a legend to tell them apart. The tint is a cursor decoration: it leaves with the cursor, never reaches an export, and costs nothing to draw.

**Display names versus roles.** Either layer can carry a long display name — the frames use *"Treedan woodland and the upper terraces"* on Foreground. The name ellipsizes; the `FG` / `BG` role chip never truncates, at either viewport.

**LAYR-01 was amended on 2026-09-22** and now reads:

> **LAYR-01**: User works with exactly two terrain layers, Background below Foreground, with rename/visible role identity, hide, lock, solo and opacity controls. Their roles/order are fixed; P0 does not add, duplicate, remove or reorder terrain layers. Only Foreground owns the editable coastline mask.

**MASK-01 was amended the same day**, and is what the land tool is built against:

> **MASK-01**: User can add/subtract soft coverage on Foreground’s single land mask, revealing Background, and see coastline stroke, fade, wave rings and isolines derived from its composed 0.5 contour. Coverage is distinct from layer opacity; regions never reaching 0.5 have no coastline. Background has no editable coastline mask and there are no separate mask layers.

Design and requirements are aligned; nothing in §2 is a proposal any more.

## 3. Import modes

The amendment also changed how a project starts, and P1-01 follows it:

- **Neither mode is preselected.** *Create project* stays disabled until one is chosen, so the choice cannot be made by reflex. The recovery review shows the source's dimensions, version, what was recovered and what was not, beside the preview.
- **Editable recovered terrain** — Background and Foreground both arrive paintable, the recovered land mask on Foreground.
- **Original flattened appearance** — Background *is* the embedded preview, locked; Foreground starts empty. The copy states the cost plainly: its coasts are part of that picture, not geometry you can edit. Only a mask you paint yields a live coast.
- **The preview is preserved either way**, for comparison, and the original `.ink` is copied in byte-identical with its hash.
- **Location** defaults to the configured projects folder, shown read-only with the project name emphasised, plus an *Elsewhere…* override.

## 4. The land tool

P1-05 was rebuilt on 2026-09-22 from the owner’s reference screens; the earlier panel was too much machinery. What survives:

- **Two variants** on a rail at the left of the panel — **Edge** (irregular coastline: Roughness 1–20 plus a Smooth checkbox) and **Circle** (a round stamp: Softness %, default 0 = hard edge, with a reset).
- **Action: Add | Subtract**, segmented, with Alt inverting it while drawing. Add grows land; Subtract reveals Background.
- **Brush size**, slider plus stepper, in document pixels.
- Nothing else. Falloff, strength and the coast effects are gone from the tool, and so is the in-panel preview — the coast is legible at full size on the map, and a thumbnail of it only cost space.

The panel is 380 × 500 rather than a full-height column. A tool panel is as tall as its tool needs; the layers column stays full height.

**Coast effects have no controls at all.** Stroke, fade, wave rings and isolines still render — MASK-01 requires the user to see them — but they are derived from the mask with fixed values, and P0 exposes no way to tune them. They were briefly given a home in Foreground’s settings; the owner removed them on 2026-09-22 as weight the job does not need. The map shows what they produce, which is the part the requirement actually asks for.

This leaves one live conflict: the brief’s P1-05 row still asks for “enabled coastline stroke/fade/wave-ring/isoline controls”. The requirement (MASK-01) says *see*, not adjust, so the design satisfies the requirement and contradicts the brief. The brief row should be amended, and engineering needs the fixed values — see §12.

The tool is named **Land tool** and takes **L**; the old “mask brush / M” wording is gone from the rail, the shortcut table and the flows.

## 5. The texture brush

Rebuilt after TERR-01 was amended. The panel now carries what the requirement names:

- **Preset library** — a named preset (texture plus tip and stroke settings) with *Save as…*. The current preset is shown with its swatch, and a preset may be **tapered**: strokes narrow at both ends by themselves, with no pressure involved. A tapered preset says so, with a taper drawn beside the line.
- **Tip variants** on the same rail pattern as the land tool — **Round** (hardness alone) and **Edged** (hardness plus **Roughness** and **corner smoothing**). The edged-only controls appear only for that tip rather than greying out.
- **Stroke**: opacity, flow, spacing.
- **Texture**: the swatch grid, then **scale**, **rotation** and **jitter**, with the stroke-local seed shown as retained for replay. Scale was missing before this pass; TERR-01 requires it.
- The subtitle states the thing that is easy to assume otherwise: **texture painting does not change land coverage**.

Missing textures are marked in the grid as well as in a warning row, since a preset can reference one.

## 6. Screens

| ID | Artboard | Covers |
| --- | --- | --- |
| P1-01 | `Start.dc.html`, `Import.dc.html` | DOC-01, IMPT-01, IMPT-02, IMPT-03 |
| P1-01b | `ImportReport.dc.html` | IMPT-02, IMPT-03 |
| P1-02 | `Main.dc.html` (1920 × 1080) | UIIN-01, DOC-01, DOC-02, REND-01 |
| P1-02b | `Compact.dc.html` (1366 × 768) | UIIN-01 under compression |
| P1-02c | `LandCursor.dc.html` | MASK-01 — coverage under the cursor, and why it cannot be mistaken for opacity |
| P1-03 | `Layers.dc.html` | LAYR-01 (as amended in §2) |
| P1-04 | `BrushInspector.dc.html` | TERR-01; TERR-02 with the cursor itself on P1-02 |
| P1-05 | `MaskInspector.dc.html` | MASK-01, REND-02, REND-03 — the land tool alone |
| P1-06 | `River.dc.html` | WATR-01 |
| P1-07 | `History.dc.html` | HIST-01, HIST-02, HIST-04, DOC-02, DURA-01 |
| P1-08 | `Export.dc.html` | EXPT-01, REND-04, REND-05 |
| P1-08b | `Recovery.dc.html` | DURA-02, DOC-03, REND-05 |
| — | `Tokens.dc.html`, `States.dc.html`, `Flows.dc.html`, `Shortcuts.dc.html` | the shared system |

The grid row in P1-03 covers **NOTE-01**, which belongs to Phase 4 and is drawn here at the owner’s request — row and visibility only.

Sample data is invented and labelled as such: *Aethermoor — West Reaches*, revisions 1,284–1,287, `aethermoor-west.ink`, the Silvermere river, the Foreground display name *Treedan woodland and the upper terraces*, textures *heath-scrub-02 / moor-grass-01 / scree-fine / pine-dense / tidal-sand*. No real file, size or duration in these frames is measured.

## 7. Region dimensions and resizing

| Region | Size | Behaviour |
| --- | --- | --- |
| Title strip | 34 px | Fixed. Merges into the command bar below 1500 px. |
| Command bar | 44 px | Groups collapse into `File` and `Overlays` menus below 1500 px. |
| Tool rail | 56 px (48 px compressed) | Fixed width, always visible. |
| Tool panel | 318 px wide, drag 260–420 | Height follows the tool: the land tool needs about 500 px, the texture brush more. Collapses with Tab; the tool stays active. |
| Canvas | remainder, min 480 px | Never smaller than the panels allow. |
| Layers column | 340 px, drag 280–520 | Object, path and text layers fill the reserved area above the two terrain layers from Phase 2. |
| Status bar | 30 px (28 px compressed) | Fixed. |
| Splitter | 4 px visual, 8 px grab | Hover lightens; drag is live, not ghosted. |

At 150% display scaling every value above scales with the OS factor; the type ramp is in points, so nothing is re-laid-out — the canvas simply gets less room, and the 1366 rules apply earlier.

## 8. Tokens

Colour, type, spacing and every component's default / hover / focused / active / disabled / invalid / busy state are drawn in `Tokens.dc.html`. Summary:

- **Surfaces** `#0D0F16` app · `#12141D` bar · `#161923` panel · `#1D2130` panel header · `#252A3A` control · `#2F3447` hairline · `#3B4159` control border
- **Text** `#ECEDF3` (15.0:1) · `#ADB3C6` (8.4:1) · `#8990A6` (5.5:1), measured on the panel ground `#161923`. No text below 11 px anywhere.
- **Accent** `#5C6FE0`, foreground `#080A14`, selected fill `#23294A` with `#C6CFFF` text. **Focus ring** `#93A6FF`, 2 px, offset 2 px, on every focusable control including the canvas.
- **Status** `#5FAE7F` durable · `#D9A23F` pending · `#E2685A` failure · `#9D7BE0` working / frozen revision. Always paired with a word. Working is violet rather than blue so that a running job never reads as a selection.
- **Type** Spectral 600 for the product mark and dialog titles; IBM Plex Sans 400/600 for UI; IBM Plex Mono for every number. Ramp 24 / 13 / 12 / 11 px plus an 11 px 600 uppercase section label.
- **Metrics** 4 px base · radius 3 control, 5 panel · rows 46–52 px · fields 26 px · bar buttons 30 px · dialog buttons 34 px.

## 9. Interaction contract

**Preview, commit, save.** A drag shows a live preview: discardable, never in history, never in an export. On pointer release (or Enter, or focus loss for a field) it becomes exactly one undoable command and the revision advances. Only after the commit is on disk and acknowledged does the save chip read *Saved*. Esc during a drag cancels with nothing committed. A pointer that leaves the canvas mid-stroke still ends the stroke on release; a stroke never resumes because the pointer returned.

**Three independent states.** *Active tool* (rail), *active surface* (layers panel, echoed in the tool panel header) and *selection* (river points) are separate and separately displayed. Incompatible combinations refuse rather than redirect: with the texture brush active and no usable paint surface — Background locked, Foreground hidden — the canvas shows a refusal cursor and paints nowhere. Painting never silently lands on a surface the user did not choose.

**The brush cursor** is the only preview of the brush, drawn on the canvas in document space so it scales with zoom and never overstates coverage: the outer ring is the size (a diameter), the dashed inner ring is where hardness starts to fall off, and the dashed box is the affected region. A readout beside it carries size, hardness and texture angle. Below roughly 6 px on screen the rings collapse to a crosshair and the readout carries the numbers alone. The land tool uses the same cursor without the hardness ring.

**Numeric entry.** Slider plus a monospaced field with its unit; the label scrubs on drag; a reset control returns the default. Arrow keys step 1, Shift 10, Alt 0.1 where decimals exist. Enter commits and clamps, Esc restores the last committed value, Tab commits and moves. An out-of-range value turns the field red, keeps focus and commits nothing. `Ctrl+Z` inside a focused field undoes *text*, not the document — the only exception to document undo, and it is stated in the panel itself. Full field/unit/range table in `Shortcuts.dc.html`; ranges are proposals for engineering to confirm, and no renderer maximum is invented.

**Units decided.** Brush size is a **diameter** in document pixels. Hardness, opacity, flow, spacing, strength, falloff and bank softness are integer percent (stored 0–1). Texture rotation is degrees and **wraps** (370 → 10); jitter is ± degrees and clamps. River width and all coast distances are document pixels. Zoom shows one decimal below 100%, none above, and is not an undoable command.

**Shortcut scopes.** Canvas / text-and-numeric / inspector-and-list / modal, plus exactly four application-wide bindings (`Ctrl+S`, `Ctrl+Z`, `Ctrl+Shift+Z`, `Ctrl+Shift+E`). Single-letter tool keys never fire while a field has focus. Full table in `Shortcuts.dc.html`.

**Export sizing.** Presets set the long edge — 1080p 1920, 2K 2560, 4K 3840, 8K 7680, 16K 16384 — and the short edge follows the document’s proportions, so no preset can distort the map; 16K on a square document is the 1:1 case with no resampling. Custom takes both fields, clamped to 16,384 per axis. The estimated output size sits beside the tiling summary.

**Progress and cancellation.** Anything over roughly 400 ms shows progress and a Cancel that responds within one frame budget. Determinate work states its real units (band 14 of 32); indeterminate work — PNG validation — shows an indeterminate bar and **no invented percentage**. Cancelling history reconstruction returns to the current revision with nothing half-applied.

## 10. Exceptional states

All seventeen states are in `States.dc.html` with their screen, exact user-facing copy and next action: empty project · oversized `.ink` · corrupt/unsupported `.ink` · partial recovery · no paintable layer · locked imported raster · both terrain layers hidden · missing texture · invalid numeric value · no undo available · older undo rebuilding · queued save · save failed (disk full) · export cancelled or failed with the previous output intact · cache rebuild · fresh process after a crash · render backend unavailable. The brief lists fifteen; the extra two split its “unsupported/oversized/corrupt” into the two different outcomes they actually produce.

**Resource pressure** appears as two quiet monospaced readouts in the status bar (decoded cache, editor GPU allocation) that change appearance only when a budget is being enforced — "cache full — evicting distant tiles", "export working set reduced". Normal controls never mention VRAM. Budgets are listed in Project ▸ Storage. Detailed management is Phase 4.

## 11. Godot handoff

- **Containers.** Shell is a `VBoxContainer` (title / command / work / status); work is an `HSplitContainer` chain — rail (fixed `custom_minimum_size`) → tool panel → canvas → layers. Splitters are Godot's own, restyled to 4 px with an 8 px grab margin; minimum sizes come from §4 so a drag cannot collapse a region to nothing.
- **Scrolling.** Tool panel and layers column are `ScrollContainer` with `follow_focus` on, so keyboard navigation cannot leave the focused row off-screen. Lists are bounded: layer rows are few and fixed; history is virtualised with a windowed item list, never a full node per command.
- **Canvas.** One `Control` owning pan/zoom, drawing through `RenderingDevice`. Overlays — brush ring, tile boundary, river points, scale bar — are `_draw()` calls on that Control, not scene nodes, and are cheap: no per-frame allocation, no tween on anything that follows the pointer.
- **Focus.** Explicit `focus_neighbour` wiring per scope; `F6` cycles canvas → tool panel → layers → status. The canvas itself is focusable and shows the same 2 px ring. Tool-letter shortcuts are handled in `_unhandled_key_input` so a focused `LineEdit` consumes them first — this is what makes "typing never triggers a tool" true rather than aspirational.
- **Overlays.** The export/recovery job chip is a `PanelContainer` anchored inside the canvas Control, not a popup: it must never take focus or block input. Import and export setup are the only `Window`/modal surfaces.
- **Cursor.** Brush ring is drawn in document space and scales with zoom; below ~6 px on screen it collapses to a crosshair and the readout carries the size.

## 12. Requirement coverage

| Requirement | Where | Note |
| --- | --- | --- |
| DOC-01 | P1-01, P1-02 | 16,384² stated in the title strip; document-space coordinates in the status bar. |
| DOC-02 | P1-02, P1-07 | Four save states; *Saved* means acknowledged, not rendered. |
| DOC-03 | P1-08b | Delete-all-caches with what survives spelled out. |
| IMPT-01 | P1-01, P1-03 | Two modes, neither preselected; flattened mode locks the preview into Background; source untouched and hashed; a <em>baked coasts</em> tag on the Background row keeps imported pixels distinguishable from editable coverage in the editor, not only at import. |
| IMPT-02 | P1-01, P1-01b | Preserve-bases option; provenance for baked effects; native edits separate from imported history; baked fallback marked in the stack. |
| IMPT-03 | P1-01b | Rejection and degradation cards, each naming the limit and the next action. |
| LAYR-01 | P1-03 | Two fixed terrain layers, long display name with an untruncatable role chip, hide / lock / solo / opacity per layer, coastline mask on Foreground only. Matches the amended wording. |
| TERR-01 | P1-04 | Preset library with tapered presets; Round and Edged tips, the latter with roughness and corner smoothing; diameter, hardness, opacity, flow, spacing; texture scale, rotation and jitter; texture identity and stroke-local seed recorded for replay; subtitle states that painting does not change land coverage. |
| TERR-02 | P1-02 | The cursor is drawn on the canvas — outer ring (size), inner ring (hardness), dashed box (affected region) — with its anatomy specified in §8 rather than mirrored in a panel diagram. Document-space anchoring stated in the tool panel. |
| MASK-01 | P1-05, P1-02c | Add/Subtract on Foreground’s single mask, Edge and Circle variants, subtracting reveals Background; stroke, fade, rings and isolines rendered from the 0.5 contour with no exposed controls; coverage kept distinct from layer opacity by appearing only under the cursor, drawn in P1-02c. |
| WATR-01 | P1-06 | Foreground target; centreline, per-point width, width profile, bank softness, enable/delete; the channel is Background showing through rather than a water layer; painting over the bank shown not closing it. |
| HIST-01 | P1-07 | One gesture per step, labelled; discarded redo shown struck through; cursor persists across restart. |
| HIST-02 | P1-07 | Resident-delta boundary drawn; older steps replay with progress and cancellation. |
| HIST-04 | P1-04, P1-07 | Resolved parameters, texture identity, RNG seed retained; missing texture keeps its recorded identity. |
| EXPT-01 | P1-08 | Resolution presets plus custom within 16K; frozen revision named on the job; rendering and validation distinguished; temporary sibling; previous destination preserved on cancel and failure. |
| UIIN-01 | P1-02, P1-02b | Mouse, pan/zoom, scoped shortcuts, resizable panels; no interaction blocked by import or save. |
| DURA-01 | P1-07 | Queued/saving/saved/failed; the chip cannot be green while work is queued. |
| DURA-02 | P1-08b | Next-launch recovery sheet; one unacknowledged gesture absent; no dialog attempted at the moment of loss. |
| REND-01 | P1-02 | One shared viewport; overlays are cheap and non-blocking. |
| REND-02, REND-03 | P1-02 | Coast effects and isolines update from the mask and are read on the map at working zoom; they are rendering behaviour, not controls. |
| REND-04, REND-05 | P1-08, P1-08b | Export uses the same render graph as the viewport; caches are disposable and rebuild visibly. |

**Non-visual requirements.** DURA-01, DURA-02, REND-04 and the latency targets cannot be shown by a mockup. What the design does is refuse to *claim* them: the save chip reports acknowledgement rather than appearance, the export names the revision it froze, the recovery sheet lists what the journal proved and what it could not, and no screen depends on code running after device loss. If the implementation cannot keep these promises, the copy on these screens is wrong and must change — that is the point of writing it this precisely.

## 13. Open questions

1. **The brief’s P1-05 row versus what P1-05 now is.** The row asks for “enabled coastline stroke/fade/wave-ring/isoline controls” and for a “preview that distinguishes coverage from texture colour and layer opacity”. On the owner’s instruction the panel has neither. MASK-01 says *see*, not adjust, and the map shows the coast at full size, so the requirements themselves are satisfied — but the brief row should be amended so the two stop disagreeing.
2. **The fixed coast values.** With no controls, stroke, fade, ring gap and isoline interval become constants. The frames drew 6 px / 104 px / 72 px / 48 px in document pixels. Deriving them from document size instead would give a 4K and a 16K map proportionate coasts; that is the better default, but it is a product call, not mine. Whatever is chosen must stay inside the bounded distance field’s reach — engineering to confirm.
3. **Grid scope.** NOTE-01 is a Phase 4 requirement drawn in a Phase 1 frame at the owner’s request. Either the roadmap moves NOTE-01 (or its visibility half) into Phase 1, or the row ships with its settings disabled and the phase plan says so.
4. **Brush size ceiling.** Proposed 1–4096 document px, slider logarithmic, field linear. Depends on tile size and the halo budget.
5. **Land tool ranges.** Roughness is drawn as 1–20 and Softness as 0–100%, both proposed. Confirm against whatever the edge generator actually accepts.
6. **Solo semantics.** Soloing Foreground is drawn as showing it without Background beneath. Whether a “show the mask alone” view is also wanted is unresolved; the mask row currently offers Edit, not solo.
7. **What lies under Background.** With Base Colour gone from the stack, it is unstated what shows where Background itself is unpainted — a project colour, or transparency that reaches the PNG. Needs a decision.
8. **Autosave cadence.** The design reports *queued* honestly but proposes no interval. Needs a number from the durability work.
9. **Import report persistence.** Assumed reopenable from File ▸ Import report for the life of the project.
10. **Terrain-to-layer mapping.** How a source’s terrain content splits between Background and Foreground on import is assumed to follow its own draw order. Confirm against real `.ink` data.
11. **Export preset rounding.** A preset sets the long edge exactly and the short edge rounds to a whole pixel, which can shift the aspect by a fraction of a pixel on odd ratios. The alternative is nudging the long edge instead.

## 14. What is not here

Pressure, dock rearrangement, themes, multi-window, 64K documents, additional export formats, label toggles, asset browsing, stamps, paths and text. The grid is a visible row but its settings are not here either — see open question 4. Phase 2 inherits this shell, its tokens, its scopes and the reserved area above the two terrain layers; Phase 4 expands export and storage; Phase 5 reviews the whole thing on both platforms.
