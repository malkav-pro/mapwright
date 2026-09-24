# Phase 2: Recovered Assets and Stamps - Context

**Gathered:** 2026-09-22
**Status:** Ready for planning; visual design and implementation remain pending
**Amended:** 2026-09-24 (DOC-01 reconciliation): Phase 1.1 fixed map geometry at 1,000 map units on the longest edge, independent of editing and export pixels. Phase 2 map-relative sizes, positions, offsets and distances are therefore in **map units**, not map pixels. Example values from the pixel-era draft were converted at the recovered map's scale (8,192 px long edge = 1,000 units, ×0.1220703125).

<domain>
## Phase Boundary

Add a shared local asset library, managed asset/pack import, missing-art replacement, stamp placement/transforms/selection and a repeatable scatter brush to the durable editor. Phase ownership remains STMP-01, STMP-02, STMP-03, SELE-01, ASST-01, ASST-02, ASST-03 and ASST-04.

Inherit the current Phase 1 context: fixed Background/Foreground terrain, Foreground-only land mask, object content above terrain, honest editing-target feedback, explicit lock/show actions, gesture cancellation, durable history and frozen export. Phase 1 implementation planning is separate ongoing work; this document records Phase 2 choices, not implementation completion. The design brief is an input to a design agent, not an approved visual design or independent GSD requirements SPEC.
</domain>

<decisions>
## Implementation Decisions

### Asset library and import
- **D-01:** Category-first browsing with thumbnails: Trees, Mountains, Buildings, Textures and similar categories. Pack, folder and tag filters plus search remain available.
- **D-02:** One shared local library serves all Mapwright projects. Each project retains the immutable assets it uses so reopening, historical replay and packaging do not depend on the library still containing them. No cloud service or live external-file references are introduced.
- **D-03:** Loose images without pack metadata get a quick import review: choose a category and optional tags for the batch before adding them. Do not silently infer categories from folders or make Uncategorized the default workflow.
- **D-04:** Usable images with quality warnings import with warning badges and inspectable details. Warnings such as transparent padding, baked shadows or possible seams do not require acknowledgement before use. Damaged, unsupported or over-budget inputs are rejected with readable outcomes.
- **D-05:** Existing managed SHA-256 identity, retained original/derived hashes, versioned pack manifests, available metadata/provenance and bounded decoding remain required. Library titles/folders are not asset identity. Full library indexing may be shared, while current/history/export/package references stay durable within their project.

### Missing-art replacement
- **D-06:** Default replacement scope is all matching instances in the current map. Show an affected count and preview, allow scope changes before Apply, and support undo. A replacement does not update every project that uses the shared library.
- **D-07:** Fit replacement art inside each original footprint while preserving the replacement's aspect ratio and the instance's map position. Do not stretch it. Preview explicit size/anchor adjustments before applying. If source bounds are unavailable, report the uncertainty and expose the adjustment rather than inventing exact original dimensions.
- **D-08:** Missing-art placeholders are subtle outlined footprints with a small warning marker. Show the asset name or source ID on hover/selection, with object-list/report access for discovery and keyboard use.
- **D-09:** Remember accepted replacement choices and suggest matching replacements during future import reviews; the user accepts or changes them. Do not automatically apply remembered mappings. Match within the source schema/style/asset-type/ID scope from the specification, not bare IDs across unrelated sources.
- **D-10:** Preserve original source IDs, geometry and provenance. A reviewed common replacement must respect each instance's transform, not collapse all instances to one uniform position/size. Remembered suggestions and a committed map replacement are distinct operations.

### Stamp placement and selection
- **D-11:** Placement remains active after each click for repeated stamping. Escape or another tool exits placement. Escape during an active uncommitted gesture still cancels that gesture per Phase 1; leaving placement does not undo previously committed stamps.
- **D-12:** Carry current placement settings, including size, rotation, tint and opacity, when choosing another stamp asset. Do not reset to each asset's defaults or maintain separate per-asset placement settings. The placement ghost must reflect the result before clicking.
- **D-13:** Multi-selection scaling resizes each stamp individually in place. It does not spread or contract the arrangement around a group centre.
- **D-14:** Multi-selection rotation rotates the arrangement around a shared centre. It does not rotate each stamp in place as the default. This temporary selection behavior does not add persistent groups.
- **D-15:** Normal click selects the topmost visible alpha-aware hit. Alt-click cycles through overlapping hits. Keep the object list available to reach obscured objects; transparent padding must not intercept selection as opaque artwork.
- **D-16:** Existing controls for flips, tint, opacity, basic shadows, exact transforms, marquee selection, duplicate and same-map copy/paste remain. Explicit object order must survive batching across atlas pages. Follow inherited visibility/lock protection and one-gesture undo semantics.

### Scatter
- **D-17:** Implement scatter as a brush dragged across the map. Filling a drawn area is deferred for later consideration.
- **D-18:** The user selects a set of assets; the brush mixes that set while painting. It is not limited to one stamp or an automatically chosen whole category.
- **D-19:** Offer independently enabled size and rotation ranges, plus average centre-to-centre spacing in map units. Spacing is independent of zoom, of editing and export resolution, and does not automatically scale as a percentage of stamp size.
- **D-20:** Spacing describes an average with natural variation, not guaranteed non-overlap or a minimum-distance collision solver. Random flipping, tint and opacity variation were offered but not chosen for this initial scatter feature; manual stamp controls remain available.
- **D-21:** Retain the seed/RNG compatibility identity and resolved asset choices, placements, scales and angles. Committed scatter is stable across undo/redo, reopen, viewport refresh and export, including after algorithm upgrades. Each completed scatter gesture is one durable undoable operation; Escape cancels its uncommitted preview.

### Design and engineering discretion
No blanket delegation was given. Existing design briefs allow visual layout, tokens and unspecified defaults to be proposed within these decisions. Exact stamp-size units, shared-centre convention, hit-cycle feedback, category vocabulary, asset weighting, scatter preview details and numerical defaults were not individually selected. Choose/document practical implementations without changing the accepted scale-versus-rotation distinction or adding deferred capabilities. Preserve asset transform restrictions and use clear previews if a selected range cannot apply to a particular asset.
</decisions>

<canonical_refs>
## Canonical References

Read these before planning or implementing; paths are repository-relative.

### Scope and inherited decisions
- `docs/spec.md` §§3–4,7–11 — asset identity, instance geometry, deterministic history, remap scope, pack manifests and storage.
- `docs/architecture.md` — inward dependencies, immutable snapshots, bounded work and commit ordering.
- `docs/engine-decision.md` — settled Godot stack and fresh-process recovery.
- `docs/README.md` — source precedence and historical evidence boundaries.
- `.planning/PROJECT.md` — project purpose and accepted constraints.
- `.planning/REQUIREMENTS.md` — Phase 2 IDs and shared acceptance contracts.
- `.planning/ROADMAP.md` — phase ownership and dependencies.
- `.planning/phases/01-connected-imported-terrain/01-CONTEXT.md` — inherit current Phase 1 decisions, including tool/target distinctions and on-canvas scope comprehension.
- `.planning/phases/02-recovered-assets-and-stamps/02-DESIGN-SPEC.md` — input brief and required design surfaces; this context resolves its open decisions.

### Existing implementation
- `.planning/codebase/STACK.md`, `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/CONVENTIONS.md` — inspected baseline and established patterns; maps predate current user amendments.
- `Scripts/Core/MapDocument.cs`, `Scripts/Core/InkImportService.cs` — existing import metadata and unresolved source-asset reporting.
- `src/Mapwright.Domain/MapModel.cs` — existing brush asset hashes and terrain records; not a complete stamp/library domain.
- `src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` — shared edit/revision/storage boundaries.
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- The importer already collects unresolved asset IDs and reports their count. It supplies source identities to preserve; it does not establish the complete missing-art replacement workflow.
- Existing content-hash, immutable revision and commit-before-publish patterns apply to new asset references and stamp edits. A shared catalog must not become a runtime dependency for reconstructing a project's acknowledged revisions.

### Established Patterns
- Store domain asset/instance IDs, geometry, transforms and resolved scatter data outside Godot scene nodes. Use stable IDs and rewrite copied entity identities while retaining valid asset references.
- Thumbnail, decoded-pixel and atlas work remains bounded and disposable. Import/catalog work must not freeze map interaction.
- Render transparent instances in explicit order; global regrouping by texture page is not allowed. Geometry/effect bounds and alpha-aware picking remain distinct.

### Integration Points
- Connect library selection and placement ghosts to commands over the shared editor document; asset defaults and placed-instance properties must have distinct targets.
- Copy/retain used immutable asset blobs before committing map references. Apply replacements transactionally with undo and map-scoped affected counts.
- Resolve a scatter gesture into persisted instances and one command; generic seed-only regeneration is insufficient for long-term stability.
- Preserve concurrent Phase 1 plans/context additions; do not treat their presence as implemented or validated behavior.
</code_context>

<specifics>
## Specific Ideas

The user specifically wants multi-selection **scaling individually in place but rotation as a group**. Scatter uses a chosen mixture of assets, size/rotation ranges and **average spacing in map units**. Area-fill scatter should be considered later, not built alongside the brush now.
</specifics>

<deferred>
## Deferred Ideas

- Scatter by filling a drawn area: explicitly deferred by the user.
- Random flipping/tint/opacity scatter variation was not selected; do not promote it from the question's alternatives.
- Collision-/terrain-aware distribution, persistent groups, alignment/distribution, linked asset files, hot reload, SVG and a 50K-instance benchmark remain outside this phase's P0 scope.
- Full semantic import reconstruction and font substitution belong to Phase 3. Portable package publication belongs to Phase 4, but project-local retention of used assets is required now.
</deferred>

---
*Phase: 02-recovered-assets-and-stamps*
*Context gathered: 2026-09-22*
