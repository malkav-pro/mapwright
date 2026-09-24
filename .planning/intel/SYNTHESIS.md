> Phase 4 follow-up (2026-09-23): current spec/04-CONTEXT.md supersede nominal-pixel geometry, unrestricted export-size UI, full-history packaging and automatic integration JSON below. Use 1,000-unit geometry with independent presets, current-state ZIP without undo history and explicit all-content JSON export; Phase 1.1 owns normalization.

> Phase 3 follow-up (2026-09-22): the current spec and 03-CONTEXT.md supersede historical separate layer types, arbitrary label-curve UI and post-import base-reconstruction requirements below. Mixed object layers, parameterized Straight/Curve/S-shape labels and initial-import-only comparison are settled; crash recovery reopens the latest durable save.

# Document synthesis

> Phase 3 lake amendment (2026-09-22): provide both ordinary Land-tool subtraction to create a lake by revealing Background, and water texture or solid-colour painting on Foreground. Mask lakes edit coverage; painted lakes edit colour without changing coverage. Dedicated closed-spline lake entities/modifiers are deferred. River geometry and its post-mask modifier behavior remain unchanged. This supersedes earlier P0 spline-lake wording, including the lake portion of Phase 1 D-08. See current working specification and requirements; extracted historical wording below is preserved.


> Coastline follow-up (2026-09-22): mandatory P0 coast styling controls and decorative fade/ring/isoline effects below are superseded. Prefer a fixed outer-coast style with unstyled rivers/lakes, or use the authorized unstyled fallback if separation is too involved. Current requirements govern colour/seam checks and conditional distance-field acceptance.


> Later user amendment (2026-09-22): this ingestion snapshot predates the switch to exactly Background and Foreground, with a single Foreground land/coastline mask and object/path/text layers above both. Its four-terrain-layer and arbitrary brush/raster-layer requirements are superseded by [the working specification](../../docs/spec.md) and [current requirements](../REQUIREMENTS.md). Extracted evidence and other settled contracts remain intact.

Mode: new. Six classifications and their source documents were consumed: 2 ADR, 3 SPEC, 0 PRD, 1 DOC, 0 UNKNOWN. Precedence is ADR > SPEC > PRD > DOC; no per-document overrides exist. Existing codebase maps are preserved.

## Outputs and counts

- [decisions.md](decisions.md): 2 separate ADR entries; 0 classifier-locked decisions (locked source paths: absent).
- [requirements.md](requirements.md): 0 PRD-derived requirements; requirement IDs: absent.
- [constraints.md](constraints.md): 38 entries — 2 api-contract, 5 schema, 4 nfr, 27 protocol; includes all 16 functional-priority rows with their separate P0/P1/P2 values.
- [context.md](context.md): 5 source-attributed topics.
- [INGEST-CONFLICTS.md](../INGEST-CONFLICTS.md): 0 blockers, 0 competing variants, 5 auto-resolved supersessions.

## Source authority and decision status

The approved set is docs/engine-decision.md and docs/spike-report.md (ADR); docs/spec.md, docs/architecture.md and docs/reference/map-editor-spec-final.md (SPEC); docs/README.md (DOC).

The engine ADR's source status is **Final for the first implementation**. The spike decision also says Godot 4 .NET is final for the first implementation. Neither uses Accepted, so both classifications have locked=false and the required decisions schema represents them as proposed. That schema label is not a product decision to reconsider the engine: Godot is selected, with no parallel Rust + wgpu implementation. Only a demonstrated engine constraint that cannot be contained behind the renderer boundary justifies stack reconsideration. Engine selection does not close Phase 0. Sources: docs/engine-decision.md, Decision and Interaction milestone; docs/spike-report.md, Decision; corresponding classification JSON files.

Architecture's own prose status is accepted for the first implementation, but its classification is SPEC, not a locked ADR. It defines production boundaries; it is not proof those boundaries are implemented. Source: docs/architecture.md, Status and §1.

The reference snapshot is archival and unchanged. docs/spec.md explicitly incorporates later project decisions; docs/README.md records its authority and five supersessions. Unchanged constraints retain both source references. Archived engine, export-duration, recovery, interaction and isolated-slice targets are replaced in active intel with the documented current contracts and retained in the conflict report as provenance. This resolution uses explicit supersession and ADR precedence, not timestamps or arbitrary tie-breaking. Sources: docs/spec.md introduction; docs/README.md, Specification provenance and Later decisions retained; docs/reference/map-editor-spec-final.md §§1,8,13.

## Product scope retained for downstream requirements

The outcome is to finish one existing world/region map in a single-user offline editor: recover the .ink terrain/structure, replace missing art, paint terrain and masks, edit rivers and curved labels, scatter stamps, save/reopen, undo/redo and export seamless PNG to 16K. Windows is the development platform and Linux validation is required before P0 acceptance. In-app generation, accounts, cloud/social/marketplace/collaboration/subscription features and commercial/public-library work are out of scope. Source: docs/spec.md §1.

P0 is a product priority across multiple phases; it is not the Phase 0 investigation. The constraints preserve the authoritative §3 rows for Document, Layers, Terrain, Masks, Water, Stamps, Paths, Text, Grid and notes, Selection, History, Assets, Filters, Export, UI/input and Integration. They retain P0 features beyond the connected first slice, including lakes, curved labels, seeded placements, asset-pack manifests, revision-labelled integration JSON and cancellation. P1 and P2 stay separate; source roadmap prose does not promote them. There is no PRD in this input, so these SPEC requirements must be derived downstream rather than fabricated as PRD entries. Sources: docs/spec.md §§1,3,13; docs/README.md, Later decisions retained.

## Next acceptance milestone and architecture

Connect the real imported map, four visible terrain layers, texture painting, coastline effects, one editable river, undo/redo, save/reopen, viewport and export to the same authoritative document. Immutable snapshots and a single ordered commit queue feed the shared rendering graph. SQLite/blob transactions make every completed command durable before acknowledgement; the spike JSONL and manifest stores are temporary evidence. Domain/application boundaries exclude Godot and storage implementation types; the shipping application must not depend on Spike. Sources: docs/spec.md §§10,13; docs/architecture.md §§1–8,12.

At 1920 × 1080 on recorded hardware, require input-to-visible p95/p99 ≤50/100 ms, frame intervals ≤20/33.3 ms, and recent undo affecting ≤16 resident tiles at p95 ≤100 ms. Each of paint, pan/zoom while painting, river point/width edits and undo/redo runs ≥60 seconds under warm/cold/evicted caches. Measurements correlate sequence ids with the rendered update containing them and include stalls, sample accounting, queue and memory evidence. Export requires correctness, bounded resources, progress, cancellation and previous-destination preservation; duration has no gate. Device-loss recovery must survive native termination without post-loss saving or GPU readback. Sources: docs/spec.md §§7,8,13; docs/spike-report.md, Next acceptance milestone.

## Recorded evidence and remaining acceptance

The spike report records Windows 11 build 26200, AMD Radeon RX 7800 XT, Vulkan 1.4.349, Godot 4.7.2 .NET and .NET SDK 8.0.425. These are reported test conditions, not a new verification or a resolved production pin. Its terrain/JFA 16K fixture validated output in 96.92 seconds with process peak 1,861,361,664 bytes and explicit renderer buffers 191,107,136 bytes. Its implemented JFA/river tiled fixture matched whole-image output exactly. The 0.124 px discrete-oracle distance result does not prove precision relative to the interpolated 0.5 contour. Sources: docs/spike-report.md, Platform, Runtime and Measured results.

Streaming import recovered 3 rasters, 3,596 commands and 375 entities using a 64 MiB maximum token buffer; the isolated reported peak includes the Godot/Vulkan baseline. Prototype sources survived cache deletion/reopen/hash verification. Forced termination demonstrated isolated acknowledged-command replay and export destination preservation; TDR required a fresh process. These observations establish only their tested fixtures. The specification's transaction counts are different quantities from parsed command counts and do not establish a contradiction. Sources: docs/spike-report.md, Measured results; docs/spec.md §11.

The report leaves connected four-layer interaction, visible tile restoration, export progress/cancellation, full semantic import, multi-revision SQLite replacement, blur/shadow/stamp dependency fixtures, transparency ordering, physical tablet input, Linux and percentage-of-installed-memory/driver-overhead budgets open or partial. Earlier one-texture 30-second throughput did not establish correlated input-to-visible latency. A passing isolated probe is not P0 or end-to-end product acceptance. Sources: docs/spike-report.md, Gate status, Required versus implemented recovery and Known limitations.

The source migration order is domain/application contracts, SQLite/blob recovery tests, bounded scheduler/renderer snapshots, controller/view separation, connected editing/save/reopen, shared export, measured interaction and removal of shipping probe dependencies only after replacement tests. The source's 3–4 week Phase 0 allocation is an investigation budget, not a delivery promise; the earlier 9–14 month estimate is historical. Remaining source-listed choices include engine/.NET pins, streaming PNG implementation, SQLite binding, tessellation and benchmark hardware; recorded probe versions do not automatically resolve them. Sources: docs/architecture.md §11; docs/spec.md §§13,14.

## Reference graph and extraction boundary

A normalized, three-colour DFS checked every classification cross_refs edge within the approved six-document set, resolving source-relative and repository-relative references and deduplicating edges. No cycles, self-dependencies, UNKNOWN classifications or traversal-depth overflow were found; the cap was 50. README links to the working spec, architecture, ADRs and snapshot; the working spec links to the engine ADR and snapshot; the remaining approved nodes have no internal outgoing references. Historical draft names, existing codebase maps and the external original source path are outside this ingest graph. Source: all six .planning/intel/classifications/*.json files.

The research appendix remains historical source context, not newly verified external facts or additional product requirements. Reproduction commands and source-document workflow directions were treated as data; no probes or external research were run. Source: docs/spec.md Appendix A; docs/spike-report.md, Reproduction; docs/README.md, Later decisions retained.

## Planning setup created

Following explicit routing approval on 2026-09-22, [PROJECT.md](../PROJECT.md), [REQUIREMENTS.md](../REQUIREMENTS.md), [ROADMAP.md](../ROADMAP.md) and [STATE.md](../STATE.md) were created. The setup derives 50 P0 requirement IDs from the mixed specification and technical contracts, each assigned once across five delivery phases. The zero PRD-derived count above describes the extraction input, not the resulting planning requirements. All requirements remain Pending; Phase 1 is ready to plan.

The planning setup also incorporates the existing codebase maps as implementation evidence. They identify the current engine/SDK/SQLite pins and the disconnected UI/core paths, resolving the distinction between historical source open questions and the actual repository baseline. The original six-source classifications and conflict decisions remain recorded above.
