# Phase 2 design specification: Recovered Assets and Stamps

Date: 2026-09-23. Status: synchronized with Phase 3 decisions; ready for a design agent. This is an input brief; visual choices remain proposed until reviewed.

## Assignment and standalone context

Design local asset management, missing-art replacement and stamp composition for Malkav's Mapwright, a single-user offline native Godot/C# editor for continuing an Inkarnate world map on Windows/Linux. The user supplies externally generated textures and stamps. There is no account, online marketplace or in-app generation.

The inherited editor has a dominant map canvas, tool selector, contextual properties, resizable panels, pan/zoom, undo/redo and durable save/export status. Terrain consists of exactly Background below Foreground, in fixed order; only Foreground owns the land/coastline mask. Stamps live on object layers above both, with opacity/visibility/lock/solo and explicit order. Object ordering never inserts content between or beneath the terrain pair. Do not add brush or mask layers. Integrate into the Phase 1 design if available. If it is unavailable, choose a provisional shell with these regions and explicitly list assumptions; do not require access to prior conversation to understand this brief.

**Owned requirement IDs:** STMP-01, STMP-02, STMP-03, SELE-01, ASST-01, ASST-02, ASST-03, ASST-04.

The outcome is to replace unresolved art with managed local assets, place and edit stamps, control their visual order, and scatter repeatable arrangements. A catalog rename must not change an asset's identity or break placed instances.

## Settled Phase 2 decisions

Follow [Phase 2 context](02-CONTEXT.md) and inherited [Phase 1 context](../01-connected-imported-terrain/01-CONTEXT.md). These choices supersede open-choice prompts below; visual designs remain proposed.

- Category-first thumbnails with pack/folder/tag filters and search. One shared local library; projects retain immutable copies of used assets. Loose-image imports get a quick category/tag review. Usable images import with warning badges; damaged/unsupported/over-budget files are rejected.
- Missing art uses subtle outlined footprints with warning markers and name/ID on hover or selection. Default replacement is all matching instances in the current map, with preview/count and scope override. Fit original footprints without stretching; preview size/anchor corrections. Remember scoped mappings and suggest them in future import reviews, without automatic application.
- Keep stamping after each click; Escape or another tool exits placement. Carry size, rotation, tint and opacity across asset changes. Multi-selection scaling is individual in place; rotation uses a shared centre for the arrangement. Normal click picks the topmost visible alpha-aware hit; Alt-click cycles overlaps; the object list remains available.
- Scatter is a brush mixing a selected set of assets, with independently enabled size/rotation ranges and average centre-to-centre spacing in map units. Natural variation is not guaranteed collision avoidance. Persist seeds and resolved placements. Area filling is deferred; random flip/tint/opacity variation was not selected.

## Later decisions that constrain this design

The following [Phase 3 decisions](../03-water-paths-and-labels/03-CONTEXT.md) apply to the shared editor. They refine Phase 2's interaction model; path/text creation stays in Phase 3 and Grid implementation stays in Phase 4.

- **Independent tool settings:** Stamp, Scatter, Texture Brush, Land and Text retain their own parameters. Stamp size, brush size and text size never carry across tools. Within the Stamp tool, size/rotation/tint/opacity still carry across asset choices as already settled; changing the Texture Brush's terrain target likewise keeps that tool's settings.
- **Mixed object layers:** an object layer may contain stamps, paths and text together. Do not design a stamp-only layer type or require separate types for later tools. Mixed content retains explicit entity order above the fixed Background/Foreground pair.
- **Default layer:** every map starts with one object layer alongside Background, Foreground and Grid. The last object layer cannot be deleted, though its contents may be cleared. Additional object layers remain supported; expose layer-management controls in their assigned phase.
- **Placement target:** switching to an object-placing tool keeps the selected object layer. If Background or Foreground is selected, visibly switch to the topmost object layer. Keep explicit Show layer / Unlock guards; switching tools does not bypass them or silently restore another last-used layer.
- **Import boundary:** Phase 3 adds a simple side-by-side comparison during initial import. No later terrain-base reconstruction wizard or detailed reconstruction-report workspace is required. Ordinary missing-art replacement, scoped suggestions and undo remain unchanged.

## Required surfaces and controls

| Screen ID / surface | Required controls and content | Requirement coverage |
| --- | --- | --- |
| P2-01 Asset browser | Local collections/folders, text search, tags, category/style filters, thumbnails, clear selection, empty/no-results/loading states; browse stamp and texture types without confusing placement with paint | ASST-01 |
| P2-02 Asset details and validation | Title, tags/category/style, family, default size, anchor, allowed transforms, light/shadow hints and provenance; validation warnings with inspectable examples | ASST-01, ASST-02 |
| P2-03 File/pack import | Local PNG/WebP and pack selection, discovered assets, duplicate/damaged/oversized outcomes, progress/cancel, available pack identity/version/license metadata, completion destination | ASST-02, ASST-04 |
| P2-04 Missing artwork | Unresolved source ID, affected count/instances, labelled geometry-preserving placeholder, choose replacement, preview anchor/scale adjustments, apply scope and result | ASST-03 |
| P2-05 Placement and transform | Placement ghost, asset selection, position/scale/rotation, flips, tint, opacity, basic shadow controls, transform handles and numeric inspector | STMP-01 |
| P2-06 Selection and object order | Marquee and object-list selection, multi-selection, duplicate, same-map copy/paste, explicit ordered rows and reordering feedback | SELE-01, STMP-02 |
| P2-07 Scatter | Selected asset set, stored seed, size/rotation ranges, average centre-to-centre spacing in map units, transient brush-stroke preview, commit and stable resolved placements; no area-fill mode | STMP-03 |

Choose exact layout and tokens in harmony with Phase 1. The interface should make thumbnail browsing and rapid repeated placement efficient while leaving map context visible. Provide a dense and a compact panel composition; use 1920 × 1080 as the main frame and a 1366 × 768 review frame, not as a newly mandated minimum window size.

## Required user journeys

1. Import a local starter pack; inspect a duplicate, a damaged image and a stamp with suspicious padding; understand what was imported and what needs attention; filter the library and place a usable stamp.
2. Select an unresolved mountain placeholder; locate all uses of its source ID; preview a replacement with an anchor/scale correction; choose whether the supported operation affects one instance or the indicated source mapping. Clearly distinguish the available scopes and resulting affected count. Undo the replacement.
3. Place a tree, rotate/scale/flip it with handles and exact inputs, adjust tint/opacity/shadow, and stamp again. Placement settings carry to the next placement and across assets; distinguish those settings from edits to existing selected instances. Switch to Texture Brush and back to demonstrate independent remembered tool sizes. Starting from a terrain selection visibly targets the topmost object layer.
4. Select several overlapping transparent stamps by canvas and object list, reorder them and inspect the changed overlap. Undo without changing unrelated objects.
5. Marquee-select, add/remove from selection, scale each object in place, rotate the arrangement around a shared centre, duplicate and copy/paste on the same map. Show mixed property values and a locked-layer conflict.
6. Paint a forest scatter preview with a selected asset set, size/rotation ranges and average spacing in map units, commit once, save/reopen and see the same placements. Reopening, zooming, thumbnail refresh or moving a selection must not silently rerandomize it.

## Detailed interaction decisions to return

- **Browser selection versus map selection:** make it evident whether a property edits an asset's defaults or a placed instance. Choose what single-click, double-click and drag do; provide an alternative to dragging.
- **Transforms:** define scale units, aspect-ratio behavior, rotation units/pivot, flip axes, drag handles, snapping/modifiers if proposed, reset and numeric validation. Do not introduce alignment/distribution tools as required P0 scope. Proposed conveniences must be labelled.
- **Multi-selection:** show mixed values without implying zero; define absolute versus relative changes for size, rotation and opacity. Scaling is individual in place and rotation is grouped; specify the shared rotation-centre convention and which objects are excluded by locking. A completed transform is one undo step; Escape cancels an uncommitted preview.
- **Picking and order:** distinguish layer order from mixed entity order within an object layer; later paths and labels share the same ordering model. The top row's relationship to frontmost drawing must be explicit. Define picking through transparent padding and partially transparent overlaps, plus object-list access to obscured objects. Selection outlines/handles should remain legible without changing exported art.
- **Missing art:** keep names/source IDs available in details but use understandable labels in the main UI. Make replacement effects previewable and scoped. A missing thumbnail is not the same state as a missing original asset.
- **Validation:** demonstrate alpha/padding/clipping/fringe, baked-shadow, family-scale/anchor, damaged/unsupported-profile/dimension and texture-seam warnings. Opaque texture alpha is normal. A warning is an inspection aid, not a claim of seamlessness or visual quality.
- **Packs:** a pack contains a versioned manifest, stable local IDs, assets, thumbnails and license metadata. Installing copies/manages assets without changing master files. Show unresolved validation outcomes and available attribution without inventing purchasing or publishing flows.
- **Scatter:** use the selected asset set, size/rotation ranges and average centre-to-centre spacing in map units; mark unspecified numerical defaults as proposed. Explicitly separate preview changes, committed placements and regeneration. Do not imply collision-aware or terrain-aware placement.
- **Lists:** specify thumbnail loading placeholders, scroll position, selection persistence, search debounce behavior and tag reset. Use a long-library design fixture to demonstrate browsing, not a new 50K-instance performance promise.

## State and feedback coverage

Provide empty library, empty search, loading thumbnails, duplicate asset, import cancelled, bad pack manifest, missing file, oversized decode rejected, validation warning, no selection, one selected instance, mixed multi-selection, hidden/locked target layer, missing original art, completed replacement, unsaved placement and save failure. Include keyboard focus, disabled-state reason and an available next action.

Show precise destructive scope for replacing art; preserve undo where supported. Catalog operations and document edits have different persistence effects: explain which create document undo steps. Keep a user's selections and map position stable during library refresh.

Ordinary editing remains responsive during hashing, decoding and import. Show progressive availability and honest progress; do not require all thumbnails to load before map use. Assets and metadata are local; background work must not look like cloud synchronization. Default pointer feedback should work with a tablet acting as a pointer; pressure remains deferred.

## Deliverables and review checklist

Write `DESIGN-RESPONSE.md` and place editable frames, exports or an optional interactive prototype in `design-assets/` beside this file. Preserve this brief.

- Annotated P2-01 through P2-07 frames using sample mountains/trees/buildings/textures, long names, transparent padding and unresolved placeholders. Label illustrative artwork/data.
- Six user journeys with screen transitions and undo/commit/cancel boundaries.
- Component specifications for asset cards, filters/tags, validation rows, replacement preview, ordered object rows, transform handles and mixed-value numeric controls; reuse Phase 1 tokens and identify additions.
- Control matrix listing label, target, units, proposed bounds/defaults, enabled conditions, keyboard/modifier behavior, preview and commit semantics.
- Requirement-to-frame coverage for all eight owned IDs and a state-coverage checklist. Include important copy for duplicates, missing art and cancelled/failed import.
- Demonstrate accepted replacement scope, repeated-placement settings, independent tool sizes, terrain-to-topmost-object targeting, the default protected object layer, individual scaling/group rotation and scatter controls; propose only unspecified preview/commit details and identify engineering assumptions.

Completion means a reviewer can import a pack, replace missing art, compose an ordered multi-selection and commit a repeatable scatter without confusing asset defaults with instance properties.

## Boundaries and sources

P0 includes managed copies, validation, pack manifests, ordered stamps and seeded scatter. Linked assets/hot reload, SVG, third-party pack readers, clip modes, flatten/unflatten, alignment/distribution and 50K-instance benchmarking are deferred. Phase 3 adds initial-import-only side-by-side comparison and a concise summary; post-import base reconstruction and detailed reconstruction-report tooling are deferred. Later phases inherit these asset/selection controls.

[Roadmap](../../ROADMAP.md) · [Requirements](../../REQUIREMENTS.md) · [Working specification §§3,7–9,11](../../../docs/spec.md) · [Phase 1 brief](../01-connected-imported-terrain/01-DESIGN-SPEC.md) · [Design index](../DESIGN-INDEX.md).

## Phase 4 map-unit clarification

All map-relative sizes use stable map units. The longest edge is 1,000 units and the other edge follows the aspect ratio; initial grid counts do not change that scale. Brush diameter, stamp/text/path sizes, scatter spacing and effect distances must not depend on editing or export pixels. Keep screen-space handles in pixels. This supersedes earlier map-pixel wording in inherited contexts; retained source geometry/assets use explicit transforms. Editing resolution is fixed at creation/import in P0; export resolution is independent. The finalized [Phase 4 context](../04-project-finishing-and-safe-publication/04-CONTEXT.md) and [brief](../04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md) specify editing presets 1K/2K/3K/4K and independent export presets through 16K, measured on the longest raster edge. Phase 1.1 owns normalization and legacy compatibility.
