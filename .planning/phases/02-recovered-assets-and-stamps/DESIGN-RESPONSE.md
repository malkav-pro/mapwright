# Phase 2 design response: Recovered Assets and Stamps

Date: 2026-09-23. Status: **proposed**. Amended 2026-09-24 (DOC-01 reconciliation): Phase 1.1 fixed map geometry at 1,000 map units on the longest edge, independent of editing and export pixels. Phase 2 map-relative sizes, positions, offsets and distances are therefore in **map units**, not map pixels. Frames and the tables below were converted; asset source-image dimensions remain in pixels. This answers [02-DESIGN-SPEC.md](02-DESIGN-SPEC.md), which is preserved unchanged, and follows the settled decisions in [02-CONTEXT.md](02-CONTEXT.md) (D-01 – D-21) and the Phase 3 constraints the brief forwards. Where the brief's open-choice prompts and the context disagree, the context wins. Nothing here is evidence that the application works, that any asset validates, or that any latency or memory target is met; these are the interfaces those behaviours will have to justify.

**Deliverable:** thirteen annotated frames in [design/](design/) beside this file, and on an interactive canvas at <https://claude.ai/artifact/GXGbFpuviNevGUKsPnD1Wn> — private; share it from the page's Share menu before sending it to anyone else. This document carries the decisions, contracts, tables and open questions.

Phase 2 inherits the Phase 1 shell, tokens, type ramp, numeric-entry contract, shortcut scopes and the preview / commit / save vocabulary, all unchanged. §8 lists the additions.

---

## 1. Decisions taken

| # | Decision | Why |
| --- | --- | --- |
| D1 | **Three targets, three labels, never one surface.** *library defaults* (amber), *next stamp* (cobalt), *on the map* (green). Every panel header carries its tag | The brief's hardest requirement is that a user can tell whether a property edits an asset's defaults or a placed instance. Three things are called "size". Separating them by place and labelling the place answers the question before any field is touched. Drawn in **Targets**. |
| D2 | **The browser is two surfaces.** A 390 px tool panel for *browse to place*, and a 1200 px Asset browser window for *manage* | Rapid repeated placement wants the map visible; folders, validation and pack identity want room. Both are category-first, so nothing is re-learned between them. Consistent with Phase 1's D3 — the tool's controls live beside the tool. |
| D3 | **The order list lives in the layers column**, under the selected object layer | Entity order is layer content, so it belongs where layers are. At 1920 it is a second region below the layer rows; at 1366 the column becomes Layers / Order / History tabs. |
| D4 | **Atlas pages are shown and are never an ordering control.** Each order row carries a `p1`/`p2` chip | STMP-02's contract is that explicit order survives batching across pages. A chip makes the alternation visible rather than promised, and **P2-06** draws the failure case beside the correct one. |
| D5 | **Geometry bounds and effect bounds are drawn differently everywhere** — solid cobalt vs dashed violet | The spec keeps them distinct; the design keeps them distinct on screen. Geometry decides what a click picks and where handles sit; effect bounds decide what is redrawn and what the export band must fit. |
| D6 | **Shadows are rendered, never baked**, and baked-shadow art raises a warning rather than being accepted | It is the one validation finding that is a statement about *intent*, so it is worded as a guess and offers `Dismiss`. |
| D7 | **Rename shows a live reference count** — "Used by 212 placements in 3 projects — renaming keeps every one" | ASST-01's "renames never break references" becomes something the user can see rather than something they have to trust. |
| D8 | **Placeholders are two-tier**: a 62%-opacity dashed footprint with a small marker at rest, full-strength plus the readable *name* on hover or selection | D-08. The `ink:` id stays in details, the report and the object list — the map gets a name a person recognises. 63 of them must not compete with the terrain. |
| D9 | **Scatter offers no random flip, tint or opacity**, and no disabled row for them | D-20: they were offered and not chosen. A greyed control implies a promise. The panel says plainly that they remain available per stamp. |
| D10 | **Spacing is labelled *average*, in map units, in the field's own unit line** | D-19/D-20. The one number most likely to be read as a guarantee. The panel also states that nothing reads the terrain beneath. |
| D11 | **Preview, committed and regenerated are three named states** with three colours, drawn side by side in **P2-07** | STMP-03 is about stability. Naming the third state is what stops "the scatter changed" from ever being ambiguous. |
| D12 | **Absolute and relative numeric controls are separate fields.** `Size` / `Scale by`, `X` / `Move by` | A multi-selection needs both, and one field cannot mean both. *Mixed* is italic grey with the real values printed beneath — never `0`, never blank. |
| D13 | **A refusal is never a redirect.** Hidden or locked target ⇒ refusal cursor, the reason in the panel, and an explicit `Show layer` / `Unlock layer` | Inherited from Phase 1 and required by the Phase 3 placement-target rule. Switching tools never bypasses a guard and never silently adopts another layer. |
| D14 | **The map's `Missing artwork` count is in the command bar** | 63 placements deserve better than hover-discovery. One click reaches P2-04. |
| D15 | **The Isolines overlay toggle from the Phase 1 frames is dropped**; `Coast` and `Placeholders` remain | The design index's coastline follow-up defers wave rings and isolines beyond P0. Keeping the toggle would ship a control for deferred behaviour. Flagged in §12 as a Phase 1 reconciliation, not a Phase 2 change. |

---

## 2. Screens

| ID | Frame | Size | Covers |
| --- | --- | --- | --- |
| P2-00 | `Main.dc.html` | 1920 × 1080 | The shell with the Stamp tool active: ghost on canvas, object layers populated, order list, placeholder footprints at rest |
| P2-00b | `Compact.dc.html` | 1366 × 768 | The same shell compressed — merged bars, 48 px rail, 4-up grid, tabbed right column |
| P2-01 | `Browser.dc.html` | 1200 × 900 | ASST-01 |
| P2-02 | `AssetDetails.dc.html` | 1200 × 940 | ASST-01, ASST-02 |
| P2-03 | `Import.dc.html` | 1200 × 1000 | ASST-02, ASST-04 |
| P2-04 | `MissingArt.dc.html` | 1200 × 980 | ASST-03 |
| P2-05 | `Placement.dc.html` | 1240 × 1000 | STMP-01 |
| P2-06 | `Order.dc.html` | 1240 × 1020 | SELE-01, STMP-02 |
| P2-07 | `Scatter.dc.html` | 1240 × 900 | STMP-03 |
| — | `Targets.dc.html` | 1560 × 940 | The three targets, per-tool memory, layer aiming, the protected default layer, mixed object layers |
| — | `States.dc.html` | 1560 × 1500 | Seventeen states with exact copy; the catalogue/document undo split |
| — | `Controls.dc.html` | 1600 × 1400 | Control matrix, shortcuts, component specifications |
| — | `Journeys.dc.html` | 1740 × 1340 | The six journeys with commit / cancel boundaries, plus requirement coverage |

**Sample data is invented and labelled as such.** Project *Aethermoor — West Reaches*, revisions 1,284–1,309. Pack *Aethermoor Starter — Highlands 0.4.2*. Families `pine-highland`, `ridge-peak`, `oak-broadleaf`, `cottage-thatch`, `tower-round`. Source ids `ink:tree-oak-11`, `ink:mtn-range-03`, `ink:tree-pine-07`, `ink:town-keep-02`, `ink:ship-galleon-01`, `ink:rune-stone-05`. Long name: *"Highland pine, mature with broken upper crown and exposed root plate"*. No file size, hash, count or duration in these frames has been measured. The 238 stamps and 7,559 × 8,192 source dimensions are the specification's own fixture observations (§11), not new claims.

---

## 3. Three targets, and why it is the spine of the phase

A user can set three different numbers called "size" in this phase, and confusing them is the failure the brief singles out.

| Target | Where | Scope | Undo |
| --- | --- | --- | --- |
| **library defaults** | Asset details (P2-02), browser detail column | Every project, now and later | None — a catalogue operation |
| **next stamp** | Placement settings (P2-05), the stamp tool panel | The next click only | None — tool state |
| **on the map** | Selected stamp (P2-05), order list (P2-06) | Those placements | One document command each |

Each panel header carries its tag, so the answer arrives before a field is read. Two consequences are stated in the frames rather than left to be discovered:

- **Editing an asset's default size never changes a placed stamp.** A placement records its own size; the shared library cannot reach into a project's history. There is no cascade and none is offered.
- **`Ctrl+Z` after renaming an asset undoes the last *map* edit,** because a rename is not in map history. The tag on the panel is what makes that predictable rather than alarming. `States` §"Which of these create a document undo step" lists both sides in full.

**Per-tool memory.** Stamp 50 map units, Texture brush 31, Land 125, Scatter brush 225 — each tool keeps its own everything, and no tool ever adopts another's value. The single exception is *within* the Stamp tool: choosing another asset keeps size, rotation, tint, opacity, flips and shadow (D-12). The ghost reflects the result before the click. **Targets** draws the table; J3 walks B → S → B and shows both numbers unchanged.

**Layer aiming.** An object layer stays selected across tool switches. If Foreground or Background is selected when an object-placing tool activates, the target moves to the topmost object layer *visibly* — layers panel selection, status bar and panel header change together. Hidden or locked target: refusal cursor, nothing placed, the reason in the panel, and `Show layer` / `Unlock layer` as the explicit next action. Switching tools neither bypasses a guard nor restores some other last-used layer.

**The protected object layer.** Every map starts with one object layer beside Grid, Foreground and Background, so the Stamp tool always has somewhere to aim. The last object layer's `Delete` is disabled with its reason in text beside it — "The last object layer cannot be deleted. Clear its contents instead." Clearing is an ordinary undoable command. Object layers hold **mixed content**: there is no stamp-only layer type, and Phase 3's paths and labels take rows in the same order list.

---

## 4. The library

**Category first** (D-01). Trees, Mountains, Buildings, Terrain, Textures — a horizontal strip, exactly one active, counts on each chip. Pack, folder, tag and style are filters beneath it; search is a 240 px field that filters after 180 ms idle and never blocks the grid. Textures sit in the same strip with a note that they paint on terrain rather than place as objects, which is where the brief's "do not confuse placement with paint" is answered — the distinction is in the category, not a separate window.

**One shared library, project-local copies** (D-02). The browser states it: placing an asset copies its bytes into *this* project, so reopening, undo replay, export and packaging never depend on the library still holding it. The consequence is drawn as two states that read alike in words and must not look alike on screen:

- **No thumbnail** — neutral grey, an image glyph, "The asset is here and places normally." A cache state.
- **Original file missing** — red, dashed, "The stored bytes are gone from the library. It cannot be placed. Projects already using it keep their own copy and are unaffected." Placement disabled with that reason on the button.

**Identity is the hash, never the label.** The detail column shows stored-bytes hash, original-bytes hash and local id together with the note that sRGB normalisation derived the stored hash and the original is retained unchanged. Folders, tags, titles and categories are views. Rename carries the live reference count (D7).

**Metadata shown in full**, per ASST-01: title, category, tags, style, pixel dimensions, default size, anchor, allowed transforms, family and variant position, light direction and shadow hint, tiling, provenance, attribution. **A field the pack omits is left empty, not guessed** — P2-03's metadata-coverage list marks per-file attribution present on only 14 of 184 rather than inventing it.

**Loading, and keeping your place.** Thumbnails arrive behind you; a tile shows a spinner and "Loading…". Scroll position, grid selection, map selection and viewport all survive a background rescan. The footer names the three budgets. Nothing in the library ever blocks map interaction, and no copy anywhere suggests a network — the work is hashing and decoding, and it says so.

---

## 5. Validation

A warning is an **inspection aid**. It never blocks placement, never requires acknowledgement, and never certifies seamlessness or style consistency. Three outcomes are refusals, and they are red rather than amber: damaged, over the per-file pixel budget, and — importantly — *not* unsupported profile, which is a warning because sRGB is assumed and the original retained.

Every finding in the specification's catalogue has an inspectable example figure in P2-02:

| Finding | What the figure shows |
| --- | --- |
| Alpha present / absent | Opaque rectangle behind the subject. Never warned for a texture |
| Excess padding | Dashed opaque bounds inside a large transparent border, over a checkerboard |
| Clipped content | Opaque pixels meeting two edges, marked red |
| Coloured fringe | A white matte halo against dark terrain |
| Family scale or anchor | Four silhouettes against the family median line, the outlier amber |
| Baked shadow | Inverted alpha with the offset lobe at 315° called out |
| Texture seam | Half-offset tiling with the discontinuity marked |
| Duplicate hash | Named, reuses the existing asset, no second copy |
| Damaged | "decoding stopped at row 1,842 of 2,048" |
| Unreasonable dimensions | "184 Mpx over the 64 Mpx per-file limit" |
| Unsupported profile | "assumed sRGB, converted, original retained" |

**Budgets are named, not implied.** 64 Mpx per file, 256 MiB decoded resident, 128 MiB thumbnails, 4 concurrent decodes dropping toward 1 under pressure and never rising past 4 to catch up. Findings are cached against the asset hash and the check-set version, so `Re-run` only recomputes what changed.

---

## 6. Missing artwork

**Default scope is all matching instances in this map** (D-06), with the affected count on the radio *and* on the Apply button — "Replace 9 instances", never "Replace all". Two alternatives: the selected instance, or the current map selection. A fourth is shown as explicitly not offered, with the reason: other projects are never in scope, because a replacement is a document edit in *this* map's history.

**Fit, never stretch** (D-07). Three panes side by side: the original footprint (1,840 × 1,320, ratio 1.39), the fitted result (1,840 × 869, ratio 2.12 kept), and the stretched version in red as what is never done. Size and anchor-Y adjustments are relative to **each instance's own transform**, previewed before Apply, and preserved as intentional if the art is replaced again later (D-10).

**Uncertainty is reported, not papered over.** 2 of the 9 instances have no recorded bounds; they are named, shown at the family default, and the size control is offered as the correction. Mapwright does not invent exact original dimensions and present them as recovered data.

**Preserved for every instance:** its own position and rotation, its own footprint size, tint, opacity, flips and shadow, its place in the order list, its source id, and its import provenance. A common replacement respects each transform and does not collapse the nine onto one position or size.

**Remembered mappings are suggestions** (D-09). Accepted choices are offered at future import reviews, marked *remembered*, within the same source schema and style scope — never applied on their own. Installing a pack changes nothing in the map. Remembering a mapping and committing a replacement are distinct operations with distinct undo behaviour: the replacement is one undo step, the memory survives undoing it.

---

## 7. Placement, selection and scatter

**Placement repeats** (D-11). Each click commits one stamp and placement stays active; `Esc` or another tool leaves it, and leaving undoes nothing already placed. The ghost carries every current setting including the shadow.

**Browser gestures, with a keyboard route.** Click selects and shows defaults; double-click selects *and* arms the Stamp tool; drag-to-canvas places one and stays in placement. Dragging is a convenience only — arrow keys move grid focus, `Enter` arms placement, `F6` reaches the canvas, `Enter` places at the cursor.

**Transforms.** Size is the long edge in map units. Rotation is degrees clockwise, wrapping at 360, about the **anchor** for a single stamp so a tree pivots at its trunk. Corner handles keep the aspect ratio by default with `Shift` releasing the lock; edge handles scale one axis. Nonuniform scale is allowed while editing and refused at export preflight by name. An asset's declared rotation limit clamps and says so; a rotation-locked asset shows no rotation handle; a mirror-disallowed asset refuses the flip. Alignment and distribution are not here.

**Multi-selection, the mixed rule** (D-13, D-14). **Scaling resizes each stamp about its own anchor** — bigger trees, unchanged spacing. **Rotation turns the arrangement about one shared centre**, proposed as the bounding-box centre of the unlocked selection, computed once at drag start so the pivot cannot drift; each stamp also turns by the same angle so the group reads as rigid. Locked objects are excluded from the selection, from the transform and from the shared centre, and the count says so — "3 of 4 selected. ridge-peak-c is on a locked layer and was left out." No persistent group is created; deselecting leaves three independent objects.

**Picking** (D-15). A normal click takes the topmost visible alpha-aware hit — nearest to front whose actual pixels are opaque enough under the pointer. Transparent padding never intercepts a click as artwork. `Alt-click` steps down the stack of hits, showing the position each time and wrapping at the bottom. A fully obscured object is reachable from the order list, which is also the keyboard route. Selection outlines and handles never reach an export.

**Order.** Row 001 is drawn last: the top of the list is the front of the picture, stated in the panel. Layer order stacks whole layers above the fixed Background/Foreground pair, and nothing can be placed between or beneath that pair. Entity order is the list within one layer, shared with Phase 3's paths and labels. Batching is permitted only for a consecutive run that happens to share an atlas page; the renderer never sorts the list by page, and P2-06 draws the failure beside the correct result.

**Duplicate and paste** land just in front of the frontmost source row, offset by a small map-unit distance (provisionally 4 units, about 32 px at the recovered map's 8,192 px scale), selected and ready to drag. Copies get new entity identities and keep the same asset references — a duplicate is a second placement, never a second copy of the bytes. Same-map only in P0.

**Scatter** (D-17 – D-21). A brush dragged across the map, mixing a chosen set of assets. No area fill. Controls: the asset set, brush width, **average centre-to-centre spacing in map units**, and size and rotation ranges each with **its own switch**. No random flip, tint or opacity, and no disabled row for them. Per-asset rotation clamps are applied, the clamping assets are named, and the preview shows the clamped result rather than the requested one.

Three states, three colours, drawn together:

- **Preview** — during the drag, 50% opacity, not in the order list, not in history, never in an export. `Esc` discards the whole stroke.
- **Committed** — on release, one command holding resolved placements, each with its asset, position, scale and angle written down. They are ordinary stamps: selectable, movable, deletable, reorderable.
- **Regenerated** — `Re-roll` on a committed group, a separate command with its own undo step, which states the count first. **The only thing that ever moves committed placements.**

The frame lists eight things that cannot move them: save and reopen, undo then redo, zoom and pan, cache eviction, thumbnail or atlas rebuild, selecting or moving part of the group, export at any resolution, and a future Mapwright with a different algorithm. The seed and RNG identity are kept *beside* the resolved placements so a draw can be reproduced or explained, not so it has to be recomputed — a seed alone would not survive an algorithm change.

---

## 8. Tokens: reused and added

**Reused from Phase 1 unchanged.** Every surface, text, accent, focus and status colour. The type ramp — Spectral 600 for display, IBM Plex Sans 400/500/600 for UI, IBM Plex Mono for every user-visible number. The 4 px base metric, radii 3 (control) and 5 (panel), 26 px fields, 30 px bar buttons, 40 px tool buttons, 4 px splitters with an 8 px grab. The 2 px `#93A6FF` focus ring at 2 px offset on every focusable control including the canvas. The numeric-entry contract in full. The four application-wide shortcuts. The preview / commit / save vocabulary.

**Added.**

| Addition | Spec |
| --- | --- |
| Target tags | `library defaults` amber `#E2CFA8` on `#221E17`/`#4A4030` · `next stamp` `#93A6FF` on `#23294A`/`#5C6FE0` · `on the map` `#5FAE7F` on `#14201A`/`#2C4437`. 10 px mono, 2 px radius, in the panel header |
| Asset card | 104 px tile (panel 66, compact 54), radius 3, border 1 px `#3B4159`, selected 2 px `#5C6FE0`. Warning badge 16 px circle `#D9A23F` on `#0D0F16`. Two-line 10 px label, ellipsis at two lines |
| Filter chip / tag | 20 px, radius 10, 11 px label. Off `#252A3A`/`#3B4159`, on `#23294A`/`#5C6FE0` with a ✕. `Reset n` as an 18 px text button |
| Validation row | 2 px left border in the status hue, 7 px dot, 12 px title, actions right, 10/1.5 body, inline 150 × 106 example figure |
| Atlas page chip | 22 px wide, 9 px mono. `p2` violet `#9D7BE0` on `#1E1A2A`/`#3A3252`; others neutral. Informational only |
| Ordered object row | 36 px (compact 32): 11 px grip, 26 px mono index, 20 px thumb, name, page chip, 34 px opacity right. Selected 2 px left `#5C6FE0`. Drop indicator 2 px `#5C6FE0` |
| Mixed numeric field | *Mixed* italic `#8990A6`, real values beneath in 10 px mono, relative twin field below with focused border `#93A6FF`. Invalid `#1A1416`/`#E2685A` |
| Transform handles | 8 × 8 px `#ECEDF3` on `#0D0F16`. Geometry box 1.6 px solid `#5C6FE0`; effect box 1.2 px dashed `#9D7BE0`. Rotate 6 px circle on a 22 px stem. Anchor 4 px dot plus crosshair `#93A6FF` |
| Placeholder footprint | Rest 1.2 px dashed `#C9BFA4` at 62% plus a 6 px marker. Hover 1.6 px `#E2CFA8` plus the name plate. Anchor dot 2.8 px. Never the `ink:` id on the map |
| Replacement preview | Three panes: original `#C9BFA4` dashed, fitted `#5FAE7F` solid, stretched `#E2685A` solid. Dimensions and ratio under each |

`States.dc.html` and `Controls.dc.html` are the normative copies of the state text and the control matrix.

---

## 9. Region dimensions and the compact composition

| Region | 1920 × 1080 | 1366 × 768 |
| --- | --- | --- |
| Title strip | 34 px | merged into the command bar |
| Command bar | 44 px | 40 px; File and Overlays become menus |
| Tool rail | 56 px, 40 px buttons | 48 px, 36 px buttons |
| Tool panel | 390 px (5-up grid, full placement group) | 268 px (4-up grid, 2-column fields, collapsed shadow group) |
| Canvas | remainder, min 480 px | 764 px |
| Right column | 352 px: layer rows *and* the order list stacked | 286 px: Layers / Order / History as tabs |
| Status bar | 30 px | 28 px |

No control is removed at 1366; the numeric fields drop their sliders and keep the field, unit and label scrubbing. `Tab` collapses the tool panel without changing tool. 1366 × 768 is a review size, not a declared minimum window. At 150% display scaling every value scales with the OS factor and the 1366 rules apply earlier. Desktop density carries Phase 1's stated deviation from 44 px touch guidance; tablet input is a pointer, and pressure stays deferred.

---

## 10. Godot handoff

- **Browser grid** — a virtualised item container, never one node per asset. Thumbnail requests are issued for visible tiles plus a small margin and cancelled on scroll-away; a tile renders its loading state from the item, not from a spawned node.
- **Order list** — virtualised the same way, one row per visible object over 418 (and far more). Drag-reorder operates on the model index; the drop indicator is a `_draw()` line, not an inserted node.
- **Scene graph is application data.** Stamp instances, transforms, order and resolved scatter placements live in the domain model outside Godot scene types, per the architecture. Copy and paste rewrite entity identities while retaining asset references.
- **Picking** — an R-tree or loose quadtree over geometry bounds gives candidates; an alpha test against the decoded asset decides. Geometry bounds and expanded effect bounds are stored separately; render bounds include shadows.
- **Overlays** — selection outlines, handles, the placement ghost, the scatter brush ring and placeholder footprints are `_draw()` calls on the canvas `Control` in document space, so they scale with zoom. No per-frame allocation and no tween on anything that follows the pointer.
- **Import and hashing** run off the interactive path with bounded concurrency. Assets become available incrementally; thumbnails follow. A cancelled import keeps what completed, which requires each asset to be published whole before its reference is committed.
- **Scatter commit** resolves the stroke into persisted instances inside one command. Seed-only regeneration is explicitly insufficient.
- **Focus** — explicit `focus_neighbour` wiring per scope; `F6` cycles canvas → tool panel → right column → status. Single-letter tool keys are handled in `_unhandled_key_input` so a focused `LineEdit` consumes them first.
- **Asset copies** are written into the project's blob store under their hashes before any map reference to them is committed. The shared catalogue is never a runtime dependency for reconstructing an acknowledged revision.

---

## 11. Requirement coverage

| ID | Where | Note |
| --- | --- | --- |
| ASST-01 | P2-01, P2-02 | Shared local library; category-first with pack, folder, tag and style filters plus search; SHA-256 identity with the original retained and the derived hash shown; full metadata including anchors, allowed transforms, family, light and shadow hints and provenance; rename with a live reference count; folders and titles explicitly not identity |
| ASST-02 | P2-02, P2-03 | Every catalogued finding with an inspectable example; opaque texture alpha never warned; refusals visually and verbally separate from warnings; per-file pixel, resident-decode, thumbnail and concurrency budgets named; findings cached against hash and check version |
| ASST-03 | P2-04 | Two-tier labelled footprints preserving geometry; report with source id, count, layers and recorded name; scoped replacement defaulting to all matching instances in this map with counts on the control and the button; fit without stretch, previewed; size and anchor adjustments relative to each instance; source ids, geometry, appearance, order and provenance preserved; unavailable bounds reported rather than invented; remembered mappings suggested only |
| ASST-04 | P2-03 | Versioned `pack.json` with schema support stated; stable local IDs with the mapping shown; `assets/`, `thumbnails/`, `LICENSES/` counts; declared metadata listed with gaps marked; masters never written; bad-manifest outcome with the loose-image alternative and what it costs |
| STMP-01 | P2-05, P2-00 | PNG/WebP placement ghost reflecting the result; repeated stamping with `Esc` to exit; settings carried across asset changes and distinguished from instance edits; handles plus numeric inspector; flips honouring declared mirroring; tint in linear light; opacity; rendered shadow with its bounds growth shown |
| STMP-02 | P2-06, P2-00 | Explicit order list, front at the top and said so; atlas-page chips; the unbatched draw-order reference and its failure drawn; consecutive-run batching only; alpha-aware picking through transparent padding; geometry and effect bounds distinct |
| STMP-03 | P2-07 | Chosen asset set mixed while painting; independently enabled size and rotation ranges; average centre-to-centre spacing in map units, independent of zoom, stamp size and editing/export resolution; seed and RNG identity retained; resolved placements persisted; preview, committed and regenerated separated; eight stability conditions listed; per-asset clamping previewed |
| SELE-01 | P2-06 | Marquee and object list; add and remove from selection; mixed values shown as *Mixed* with the real values; individual in-place scaling and grouped rotation about a shared centre; locked objects excluded with a count that says so; duplicate and same-map copy/paste with new entity identities and retained asset references |

**State coverage.** All sixteen states the brief lists are in `States.dc.html`, drawn as seventeen: the brief's "missing file" splits into *no thumbnail* (a cache state) and *original file missing* (the asset is unusable), because the two read alike in words and must not look alike. Every state names its limit or cause, carries a focusable next action or says none is needed, keeps its reason in text rather than only a tooltip, and leaves the map editable.

---

## 12. Where this design differs from the brief

1. **The Isolines overlay toggle is gone** from the shell (D15). The design index's coastline follow-up defers wave rings and isolines beyond P0; keeping the Phase 1 toggle would ship a control for deferred behaviour. `Coast` and a new `Placeholders` toggle remain. This is a **Phase 1 reconciliation**, and the Phase 1 frames and their §13 open question 2 should be amended to match rather than this frame being changed back.
2. **"Unsupported profile" is a warning, not a rejection.** The brief groups "damaged/unsupported/over-budget" as rejected. An unsupported *colour profile* is recoverable — sRGB is assumed, the conversion is recorded, the original bytes are retained — so refusing the file would lose usable art. Damaged and over-budget files are rejected as specified. If the brief means "unsupported *format*", that is also rejected and the wording should be tightened.
3. **A bad pack manifest offers no partial import.** The brief asks for unresolved validation outcomes; this design declines to import a manifest-less pack's files as pack assets, because without stable local IDs a later correct install cannot reconcile them. Importing the same files as loose images stays available and states what is lost.
4. **The brief's "clear selection" is in the browser toolbar, not per-card.** One control that says what it does, rather than a second meaning for clicking a selected tile.

Nothing else departs from the brief or the context. Every settled decision D-01 – D-21 is implemented as written, and the four Phase 3 constraints (independent tool settings, mixed object layers, the protected default layer, placement targeting) are drawn in `Targets.dc.html`.

---

## 13. Open questions

**For the owner**

1. **Category vocabulary.** Five proposed: Trees, Mountains, Buildings, Terrain, Textures. The context named "Trees, Mountains, Buildings, Textures and similar". *Terrain* is the fifth — scatter-scale ground detail like scrub and rocks that are objects rather than paint. Confirm the set and whether it is fixed or user-extensible.
2. **Scatter asset weighting.** Drawn as even by default with a per-asset option, both marked proposed. Weighting was not specified. Even-only is simpler and may be enough.
3. **Nonuniform scale.** Currently allowed while editing and refused at export preflight. Refusing it at edit time would be stricter and simpler to explain, at the cost of a conversion that is sometimes useful mid-work.
4. **Placeholder rest opacity.** 62% proposed. "Subtle" was the instruction; the right number depends on how 63 of them look over real terrain at working zoom.

**For engineering**

5. **Every bound marked *P* on the control matrix.** Stamp size 1–1,000 map units; shadow offset 0–62.5 and blur 0–31.25; scatter brush 8–2,000; spacing 2–1,000 with 51 default; size range 10–400%; relative scale 1–1,000%. (Converted from the map-pixel draft at ×0.1220703125.) All depend on tile size, halo budget and atlas capacity.
6. **The shared rotation-centre convention.** Proposed as the bounding-box centre of the unlocked selection, fixed at drag start. Confirm against how the transform command is recorded, since the pivot has to be replayed exactly.
7. **Hit-cycle feedback.** `Alt-click` shows "2 of 3" with the stack listed and wraps at the bottom. Whether the list persists between clicks, and for how long, is undecided.
8. **Search debounce** at 180 ms and **duplicate offset** at 4 map units are both guesses. The keyboard **nudge** step (drawn as "1 px · Shift 10") is unsettled: screen pixels (zoom-relative) or map units (fixed)?
9. **Decode budgets.** 64 Mpx per file, 256 MiB resident, 128 MiB thumbnails, 4 concurrent decodes. Plausible placeholders showing where real numbers go; they need to come from the same accounting as the Phase 1 resource budgets.
10. **Remembered-mapping match scope.** "Within the same source schema, style, asset type and ID scope" follows the specification. The exact key needs defining before the suggestion can be trusted not to misfire across unrelated sources.
11. **Atlas page exposure.** The `p1`/`p2` chip assumes the page assignment is stable enough to display. If it churns as the atlas repacks, the chip becomes noise and should show a generated group id instead.
12. **Clearing a layer's contents** is drawn as one undoable command over 418 objects. Confirm that is within the command-size budget, or whether it needs chunking with progress.

---

## 14. What is not here

Linked assets, hot reload, SVG, third-party pack readers (Wonderdraft, Dungeondraft), clip modes, flatten and unflatten, alignment and distribution, collision- or terrain-aware distribution, persistent groups, area-fill scatter, cross-map copy/paste, random scatter flip/tint/opacity, and any 50K-instance performance claim. Paths, labels, notes and lakes are Phase 3; the initial-import side-by-side comparison belongs there too. Grid settings, colour adjustment, paper and grain, export options and packaging are Phase 4. Phase 5 reviews the whole thing on Windows and Linux.

Phase 3 inherits this phase's order list, picking model, selection behaviour, mixed object layers and the three-target labelling, and adds path and text rows to the same list.
