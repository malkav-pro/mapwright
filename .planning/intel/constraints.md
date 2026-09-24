> Phase 4 follow-up (2026-09-23): current spec/04-CONTEXT.md supersede nominal-pixel geometry, unrestricted export-size UI, full-history packaging and automatic integration JSON below. Use 1,000-unit geometry with independent presets, current-state ZIP without undo history and explicit all-content JSON export; Phase 1.1 owns normalization.

> Phase 3 follow-up (2026-09-22): the current spec and 03-CONTEXT.md supersede historical separate layer types, arbitrary label-curve UI and post-import base-reconstruction requirements below. Mixed object layers, parameterized Straight/Curve/S-shape labels and initial-import-only comparison are settled; crash recovery reopens the latest durable save.

# Constraints

> Phase 3 lake amendment (2026-09-22): provide both ordinary Land-tool subtraction to create a lake by revealing Background, and water texture or solid-colour painting on Foreground. Mask lakes edit coverage; painted lakes edit colour without changing coverage. Dedicated closed-spline lake entities/modifiers are deferred. River geometry and its post-mask modifier behavior remain unchanged. This supersedes earlier P0 spline-lake wording, including the lake portion of Phase 1 D-08. See current working specification and requirements; extracted historical wording below is preserved.


> Coastline follow-up (2026-09-22): mandatory P0 coast styling controls and decorative fade/ring/isoline effects below are superseded. Prefer a fixed outer-coast style with unstyled rivers/lakes, or use the authorized unstyled fallback if separation is too involved. Current requirements govern colour/seam checks and conditional distance-field acceptance.


> Later user amendment (2026-09-22): this ingestion snapshot predates the switch to exactly Background and Foreground, with a single Foreground land/coastline mask and object/path/text layers above both. Its four-terrain-layer and arbitrary brush/raster-layer requirements are superseded by [the working specification](../../docs/spec.md) and [current requirements](../REQUIREMENTS.md). Extracted evidence and other settled contracts remain intact.

## Product scope and exclusions
- source: docs/spec.md §1
- type: nfr
- content:
  DATA_x3mggqvo_START
  Single-user, offline-first native editor for Windows and Linux to finish one existing Inkarnate world/region map. P0 recovers terrain and structure from .ink, replaces missing stamps, supports terrain/mask painting, editable rivers, curved labels, scatter, save/reopen, undo/redo and seamless PNG to 16,384 px per axis. Structured recovery preserves parameters rather than promising pixel identity. Windows is the development platform; Linux validation precedes P0 acceptance. Generation is external, with 6–10 base variants per stamp category. Accounts, cloud sync, galleries, marketplace, collaboration, subscriptions, in-app generation, commercial positioning and public asset-library development are excluded.
  DATA_x3mggqvo_END

## Resource envelope
- source: docs/spec.md §2; docs/reference/map-editor-spec-final.md §2
- type: nfr
- content:
  DATA_5az3sxh2_START
  Initial targets remain placeholders pending the recorded machine envelope: editor GPU allocation 25% of reported VRAM capped at 4 GiB, with engine/compositor headroom; decoded CPU cache 512 MiB; export CPU working buffers 512 MiB; process peak RAM at most 25% of system RAM; history acceleration disk cache 4 GiB, separate from authoritative history and sources. P0 reaches 16,384 × 16,384 without a full-size GPU framebuffer. Sparse 65,536 × 65,536 is P1 with a separate benchmark. Query device limits at startup and reduce bands/concurrency to budgets. CPU caching is not a software renderer; unavailable required GPU backend makes the P0 rendering editor unsupported, while inspection/recovery tools remain usable.
  DATA_5az3sxh2_END

## Functional priorities: Document
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_0twly1i2_START
  P0: One map per project; logical size to 16K; save, autosave, recovery; immutable source rasters
  P1: Multiple maps; sparse 64K; style remapping
  P2: Linked nested maps
  DATA_0twly1i2_END

## Functional priorities: Layers
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_mgtus7w8_START
  P0: Brush, raster, object, path, text layers; reorder, rename, hide, lock, solo, opacity; no arbitrary count cap
  P1: Groups, blend modes, adjustment layers
  P2: General layer effects
  DATA_mgtus7w8_END

## Functional priorities: Terrain
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_ox19pj0n_START
  P0: Texture brush: size, hardness, opacity, flow, spacing, rotation/jitter; stable texture anchoring; cursor preview showing size, falloff and affected region
  P1: Pen pressure; procedural noise brushes; larger footprints
  P2: Height channel, hillshade
  DATA_ox19pj0n_END

## Functional priorities: Masks
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_ltpaucu0_START
  P0: Soft coverage painting; coastline stroke, fade, wave rings and isolines from a bounded distance field
  P1: Hatching, richer bank effects
  P2: Vector coastline editing
  DATA_ltpaucu0_END

## Functional priorities: Water
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_ckfjgzky_START
  P0: Editable river (centreline + width profile + bank softness) and closed-spline lake as non-destructive coverage modifiers
  P1: Tributary junctions, fill-from-point lakes, sketch-to-river, mouth widening
  P2: Elevation-driven descent and flooding
  DATA_ckfjgzky_END

## Functional priorities: Stamps
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_khrt9pay_START
  P0: Import PNG/WebP; place, multi-select, transform, flip, tint, opacity, basic shadow; order list; seeded scatter with persisted placements
  P1: Clip modes with a tested compositing contract; flatten with retained source; align/distribute; variants; 50K-instance benchmark
  P2: Collision- or terrain-aware placement
  DATA_khrt9pay_END

## Functional priorities: Paths
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_ifr21781_START
  P0: Import supported paths; editable polyline/Bezier strokes with width, colour, dashes, caps
  P1: Taper; repeated assets along paths (walls, palisades) with spacing, phase, tangent alignment; junction snapping
  P2: Road network tools
  DATA_ifr21781_END

## Functional priorities: Text
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_ofkbc5pd_START
  P0: Straight and curved labels; installed or bundled fonts; size, tracking, colour, outline, shadow; font substitution report
  P1: Text on arbitrary paths; glow; style presets
  P2: Automatic label placement
  DATA_ofkbc5pd_END

## Functional priorities: Grid and notes
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_51bzj0rr_START
  P0: Square grid with export toggle; pinned notes hidden by default
  P1: Hex flat/pointy and isometric grids; pixels-per-cell presets with resulting size shown; note links
  P2: Universal VTT walls/doors/lights
  DATA_51bzj0rr_END

## Functional priorities: Selection
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_q3vfe95j_START
  P0: Marquee, object-list selection, multi-transform, duplicate, same-map copy/paste
  P1: Lasso, cross-map copy/paste
  P2: —
  DATA_q3vfe95j_END

## Functional priorities: History
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_of8tz0xa_START
  P0: Persistent command history and cursor; recent delta undo; older replay with progress; explicit retention
  P1: Named snapshots, optional branches
  P2: —
  DATA_of8tz0xa_END

## Functional priorities: Assets
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_q8dwhe3l_START
  P0: Managed content-hashed assets; folders, tags, search; validation; missing-asset placeholders and report; pack manifest import
  P1: Linked assets; SVG; hot reload; Wonderdraft/Dungeondraft pack readers
  P2: Community index
  DATA_q8dwhe3l_END

## Functional priorities: Filters
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_prqvjn5b_START
  P0: Ordered colour adjustment, paper texture, grain; coastline effects; stamp shadows
  P1: Blur, sharpen, bloom, LUTs, per-layer filters
  P2: Normal-mapped lighting, fog of war
  DATA_prqvjn5b_END

## Functional priorities: Export
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_8wwxi5z9_START
  P0: Tiled streaming PNG; explicit size and DPI; label/grid toggles; cancellation; atomic publication
  P1: JPEG, WebP, TIFF, poster PDF, VTT presets
  P2: Layered ORA/PSD
  DATA_8wwxi5z9_END

## Functional priorities: UI/input
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_v6p97mgp_START
  P0: Mouse; pan/zoom; shortcuts; resizable panels; tablet as pointer
  P1: Pressure; dock layouts; themes; multi-window
  P2: Scripting API
  DATA_v6p97mgp_END

## Functional priorities: Integration
- source: docs/spec.md §3; docs/reference/map-editor-spec-final.md §3
- type: protocol
- content:
  DATA_jix5zkyp_START
  P0: Revision-labelled JSON of entities, notes, markers
  P1: Scribe's Tower mapping
  P2: Deeper integration
  DATA_jix5zkyp_END

## Document records and authority
- source: docs/spec.md §4; docs/reference/map-editor-spec-final.md §4
- type: schema
- content:
  DATA_b7asxtrv_START
  Authoritative scene graph, command records, persistent raster bases and assets are application data; stamps are not individual Godot nodes. Tiles, mipmaps, distances, thumbnails, history checkpoints and deltas are reconstructible. Map records contain ids, logical size, style, layers, grid, notes, effects, revision, cursor and compatibility versions. Brush layers retain colour/coverage bases with transforms, native strokes, water, mask effects and provenance; raster layers retain immutable image and transform; object layers retain ordered instances and ordering policy. Paths, text and groups carry stable geometry/style/transform/source metadata. Strokes store resolved parameters, asset hash, document geometry, pressure/opacity/flow, texture transform, RNG version/seed and algorithm version. Commands retain revision, target ids, payload, reversible data and referenced blobs. IDs are UUIDs or map-scoped ids with map UUID; cross-map copies rewrite ids/references.
  DATA_b7asxtrv_END

## Persistent raster bases and imported provenance
- source: docs/spec.md §4; docs/reference/map-editor-spec-final.md §4
- type: schema
- content:
  DATA_85r82rr4_START
  A raster base retains intrinsic dimensions and document transform and survives deletion of every cache. Higher export resolution resamples it without inventing detail; reproducible native strokes/assets can re-render. Export preflight reports resampled versus re-rendered layers. Imported history is provenance and a candidate recipe, never replayed over its baked base. Native edits form a separate sequence. Verified remapped reconstruction replaces the base at an explicit source revision and reapplies native edits; retain the old base for undo until pruning. Record whether masking/effects are baked: the sample has separate colour/coverage PNGs and effects only in the preview. P1 flattening keeps source entities for unflatten.
  DATA_85r82rr4_END

## Coordinates and resolution
- source: docs/spec.md §5; docs/reference/map-editor-spec-final.md §5
- type: schema
- content:
  DATA_ak9g8t5f_START
  Top-left origin, x right/y down, double-precision CPU coordinates; one document unit equals one nominal-resolution pixel. Imported geometry uses an explicit source transform. Export changes sampling density rather than geometry, preserves aspect ratio and rejects nonuniform scaling in P0. Brush/path/text/effect/grid dimensions are in document units; cursor outlines are screen pixels. Texture origin/scale/rotation, paper/grain/vignette coordinates and stored seeds are document-relative. DPI changes print size, not pixel count. Canvas bounds resize with an explicit anchor; content scaling is separate. Unsupported imported resize semantics require partial recovery. GPU calculations use tile-relative coordinates with explicit document origins.
  DATA_ak9g8t5f_END

## Colour pipeline
- source: docs/spec.md §5; docs/reference/map-editor-spec-final.md §5
- type: protocol
- content:
  DATA_iuqg1bnp_START
  Normalise imported assets to sRGB with defined profile handling; retain originals when deriving converted assets. Decode to linear light before premultiplication, compositing, blur and shadows. Use linear premultiplied RGBA16F intermediates where supported. Coverage and distance are non-colour data. PNG output is straight-alpha sRGB with matching metadata. Later blend modes declare calculation space and ship reference images.
  DATA_iuqg1bnp_END

## Text rendering contract
- source: docs/spec.md §5; docs/reference/map-editor-spec-final.md §5
- type: protocol
- content:
  DATA_6xn2a12n_START
  Record font face/version when available and report substitutions. Shaping, rasterisation and placement use identical logic in viewport and export. A viewport MSDF glyph atlas is an optional optimisation; export shapes/rasterises at target scale. Curved text follows a Bezier with tangent-aligned glyphs and stored curvature or path reference.
  DATA_6xn2a12n_END

## Coverage, coastline distance and water
- source: docs/spec.md §6; docs/reference/map-editor-spec-final.md §6
- type: protocol
- content:
  DATA_9a0szcdr_START
  Coverage in [0,1] comes from persistent bases and strokes. Coastline is its 0.5 contour; coverage independently controls terrain opacity, and translucent regions below 0.5 have no coastline. Bounded signed distance is negative on land/positive on water and clamped to enabled effect reach plus sampling support. Read neighbouring coverage and invalidate downstream distance/effect tiles. R16F requires tested precision; R32F is fallback. P0 distance error is at most 0.5 output pixel within the active band against a same-scale whole-image reference. Evaluation order: base coverage, painted masks, water modifiers, distance, coastline effects. River centreline/width/bank softness and closed-spline lakes subtract coverage before distance construction. Later land painting does not close water modifiers. Separate-distance Boolean composition is not assumed exact; P1 bake-to-mask is explicit.
  DATA_9a0szcdr_END

## Tile scheduling, ordering and dependencies
- source: docs/spec.md §7; docs/reference/map-editor-spec-final.md §7; docs/architecture.md §6
- type: protocol
- content:
  DATA_pf58m4l5_START
  Start with 512 × 512 logical residency tiles and mip levels. LRU evicts reconstructible data; pin only active jobs and shrink concurrency to budgets. Replay intersecting strokes/checkpoints using spatial lookup. Cache keys include revision, layer, scale, tile coordinates, asset hashes and renderer version. Preserve explicit layer/entity order; batch consecutive compatible runs, arrays or indexed textures, never globally regroup transparent stamps by atlas page. Spatial culling/picking uses expanded effect bounds with alpha refinement. Bounded path/text batches use the shared compositor at target scale. Each effect declares backward input and forward invalidation bounds. Sequential supports accumulate (two radius-20 blurs require 40); offset shadows are asymmetric, branches union, infinite kernels need cutoffs, global operations need prepasses or are unsupported. Coordinates/randomness are stable across tiles. P1 clip modes require tested soft-alpha compositing semantics.
  DATA_pf58m4l5_END

## Export snapshot, formats and publication
- source: docs/spec.md §§1,7; docs/architecture.md §§5,8
- type: protocol
- content:
  DATA_4fk6ylzr_START
  Freeze a revision and pin blobs while editing may continue. Render dependency/halo rectangles, crop interiors and stream scanlines to a row-streaming PNG encoder. Start at 2,048 px interiors and shrink bands; budget full-width bands, GPU intermediates, staging, encoder and queues. Fail preflight clearly if footprints cannot fit. Write a temporary sibling, flush, validate dimensions/decodability and atomically replace only on success. Failure/cancellation preserves the old destination. Restart export from the frozen revision after device recovery. Acceptance requires correctness, bounded RAM/GPU residency, progress, responsive cancellation and destination preservation; duration has no threshold and roughly five minutes for 16K is acceptable. Interactive rendering takes priority. P0 PNG supports 16,384 per axis. P1: JPEG explicit background/encoder limits; WebP maximum 16,383 per axis; bounded TIFF/BigTIFF chosen in a spike; poster PDF geometry/overlap/crop-mark fixtures. Notes default to excluded.
  DATA_4fk6ylzr_END

## History and determinism
- source: docs/spec.md §8; docs/reference/map-editor-spec-final.md §8
- type: protocol
- content:
  DATA_278zxf9e_START
  History plus cursor determines authoritative state; one gesture is one step. Undo first updates authoritative state. Recent raster gestures retain compressed before/after deltas tagged with layer state, resolution and renderer version; incompatible/evicted acceleration replays checkpoints with progress and cancellation. New edits after undo discard the redo branch in P0. Commands, immutable sources and persistent cursor/history survive cache/renderer changes. Acceleration budget does not cap authoritative history; pruning is explicit, reports lost undo and creates a baseline. Disk-full preserves the last committed revision. Persist resolved brush parameters, RNG version/seeds, assets, font identity, coordinates, compatibility version and resolved scatter placements. Cross-GPU bit identity is not promised; renderer-specific tolerances apply. Output-changing renderer upgrades warn and preserve originals.
  DATA_278zxf9e_END

## Device loss and durable recovery
- source: docs/spec.md §8; docs/engine-decision.md, Device-loss contract; docs/architecture.md §8
- type: protocol
- content:
  DATA_jsk1plst_START
  Completed commands reach durable on-disk storage before acknowledgement and dependent GPU submission. CPU document state and the command journal are authoritative; GPU resources are disposable. On device/submission failure stop scheduling and avoid cleanup that assumes usable Vulkan handles. Native termination is possible on the tested Windows/AMD configuration; recovery cannot depend on post-loss saving, managed code, orderly cleanup or in-process recreation. A fresh process reopens the last acknowledged revision, replays history and rebuilds caches. Only the active unacknowledged gesture may be discarded. Forced termination during editing/export must lose no acknowledged command, corrupt no project, publish no partial export and perform no post-loss GPU readback.
  DATA_jsk1plst_END

## Managed assets and pack schema
- source: docs/spec.md §9; docs/reference/map-editor-spec-final.md §9
- type: schema
- content:
  DATA_x5tiinao_START
  P0 assets are managed SHA-256-addressed copies; normalised derivatives retain original identity. Missing assets retain geometry/source ids in labelled placeholders. Metadata includes title/category/style tags, size/anchor/transforms, shadow/light hints, variant family and provenance. SQLite indexes search/tags; decoded data, thumbnails, mipmaps and atlases are budgeted caches. Bound decoding by dimensions/allocation. Validate stamp alpha/padding/clipping/fringes/scale/anchors/baked shadows, texture seams/scale/colour, and duplicates/damage/dimensions/profiles; warnings do not certify quality. Packs contain versioned pack.json with stable local ids, assets/, thumbnails/, LICENSES/ and optional authorship/licence/hash/attribution/style/projection/transform/tiling/variant metadata. Installation preserves masters. P1 linked assets require explicit changed-hash updates and collect-assets packaging; SVG disables external resources. Generated starter art remains external and is checked at map/export scale.
  DATA_x5tiinao_END

## SQLite and blob commit protocol
- source: docs/spec.md §10; docs/reference/map-editor-spec-final.md §10; docs/architecture.md §7
- type: protocol
- content:
  DATA_lwdwo1xg_START
  scene.sqlite owns document/revisions/history cursor/references; immutable blobs hold source rasters, strokes, assets and metadata. Cache tiles/history are disposable; preview, manifest.json and integration map.json are derived and revision-labelled. Write new blobs to same-filesystem temporaries, hash/validate/flush/close, publish immutable hashes durably including required directory metadata, then append commands/revisions/materialised state/cursor/references in one SQLite transaction. P0 WAL uses synchronous=FULL. Acknowledge only after commit; manual save waits for queued commands and autosave delay is visible. Derived updates are asynchronous. Trace current/history/export/package references for GC. Disk-full cannot alter the prior revision. Package a SQLite backup with pinned blobs, validate and reopen before publishing. Recovery validates authoritative references and discards incomplete/cache files. Never replace an open project for autosave. Network/cloud-synced directories are excluded from P0 storage. Spike JSONL/manifest stores are evidence, not the product store.
  DATA_lwdwo1xg_END

## Inkarnate recovery protocol
- source: docs/spec.md §11; docs/reference/map-editor-spec-final.md §11; docs/architecture.md §8
- type: protocol
- content:
  DATA_2yzfffrv_START
  Import creates a new project and never writes the .ink. Visual recovery keeps the preview as a locked raster base with new layers above; structured recovery uses persistent bases, tested native entities, placeholders and a remap table. Outcomes are supported structured, partial structured or visual-only. Preserve source/version/dimensions, original ids, unsupported properties and provenance; command coverage does not establish visual parity. Reports include command support/counts, unresolved ids/counts, replacements, font substitutions, approximated effects/affected layers and preview versus reconstruction. Only tested stroke/texture/resize/entity semantics are replayed. Unknown commands may be skipped only if independent of later state; unknown resize/transform/ordering stops trusted affected replay and keeps baked fallback. Remaps scope schema/style/asset type/id with scale/anchor adjustments; reconstruction is previewable and explicit. Bound decompression, JSON nesting and images. Sample observations are fixtures, not universal schema guarantees.
  DATA_2yzfffrv_END

## Acceptance gates and evidence requirements
- source: docs/spec.md §13; docs/architecture.md §§10,12
- type: nfr
- content:
  DATA_16wxvhle_START
  The next slice connects the real imported map, four visible terrain layers, textured painting, coastline effects, editable river, undo/redo, durable save/reopen, viewport and export to one authoritative document. At 1920 × 1080 on recorded hardware: input-to-visible p95 ≤50 ms/p99 ≤100 ms; frame intervals p95 ≤20 ms/p99 ≤33.3 ms; recent undo of ≤16 resident tiles p95 ≤100 ms. Run each of boundary-crossing paint, pan/zoom while painting, river point/width edits and undo/redo for ≥60 seconds under warm/cold/evicted caches with realistic bursts. Report p50/p95/p99, worst/all >100 ms stalls with causes, queues, RAM/GPU allocations, correctness and generated/processed/coalesced/dropped samples. Correlate input sequence to the update containing it; generic frame callbacks are insufficient and excluded presentation latency must be stated. Isolate validation readbacks. Other gates: cache-deletion source/re-render preservation; resource budgets; validated cancellable export; tiled/whole ≤1 channel difference without seam artefacts and ≤0.5 px distance error; sequential blur/shadow/corner-stamp/river/global-coordinate fixtures; transparent atlas ordering; interruption at storage boundaries; fresh-process recovery; degraded-import fixtures; evicted undo and redo invalidation. Physical tablet and Linux validation remain required. These are targets, not implementation claims.
  DATA_16wxvhle_END

## First-implementation stack boundary
- source: docs/spec.md §12; docs/engine-decision.md
- type: api-contract
- content:
  DATA_pr1j77i0_START
  Godot 4 .NET is selected for the first implementation. UI uses Control nodes and C# controllers; engine-independent C# services own document/history/import with versioned serialisation. RenderingDevice/GLSL, explicit resources and a tile scheduler render the map. Storage is SQLite through a maintained .NET binding with migrations and hashed blobs. Text/shaping must pass fixtures and access font files; paths need target-scale tessellation/dash rules. Image decoding and genuinely row-streaming PNG encoding are selected implementations; workers use bounded queues. RenderingDevice backends are required; compatibility rendering is not a fallback. Rust + wgpu is not maintained in parallel; only a demonstrated engine constraint that cannot be contained behind the renderer boundary warrants reconsideration.
  DATA_pr1j77i0_END

## Inward dependency boundaries
- source: docs/architecture.md §2
- type: api-contract
- content:
  DATA_5keri568_START
  Mapwright.Domain owns deterministic state/value objects/commands/invalidation and has no Godot, filesystem, JSON, SQL, threading or GPU types. Mapwright.Application orchestrates use cases and defines transaction/blob/clock/render/export/telemetry ports and acknowledgement. Infrastructure implements SQLite/blob/import/recovery/PNG ports without Godot. Rendering.Godot owns scheduler/GPU/shaders/viewport/export and reconstructs from frozen revisions. Godot is composition root/UI and contains no document or persistence rules. Spike hosts historical/destructive probes and is not referenced by the shipping application. Godot vectors, Rids, nodes, SQL rows and storage DTOs never enter domain.
  DATA_5keri568_END

## Authoritative revision and command schema
- source: docs/architecture.md §3
- type: schema
- content:
  DATA_uvtg6h9i_START
  MapProject is the aggregate root; ProjectRevision denotes an immutable logical state. Stable ids identify maps/layers/strokes/rivers/assets. TerrainLayer stores visibility/lock/opacity, persistent bases, ordered strokes and coast style. PaintStroke stores sampled document-space path, resolved parameters, immutable texture, seed and algorithm version. River stores centreline/width/target layer and subtracts coverage before distance construction. EditCommand has immutable command id, base revision and affected bounds. DocumentChange contains next state plus conservative invalidation. Commands are semantic history; tiles/distances/mipmaps/undo deltas are acceleration only.
  DATA_uvtg6h9i_END

## Edit acknowledgement and cursor transitions
- source: docs/architecture.md §4
- type: protocol
- content:
  DATA_xtchrk36_START
  UI input builds a preview gesture and immutable command; validate its base revision, commit durably, publish the in-memory snapshot, acknowledge command id/revision, then enqueue invalidated tiles. One transaction appends command/reversible data, updates materialised state/cursor and advances revision. An active gesture can preview with transient GPU state but is not authoritative. Undo/redo commits its new cursor/revision before visible restoration; editing after undo invalidates redo transactionally. Validation/disk failure cannot advance or acknowledge a revision.
  DATA_xtchrk36_END

## Snapshots and bounded concurrency
- source: docs/architecture.md §§5,6
- type: protocol
- content:
  DATA_6o53iy6h_START
  Application services expose immutable DocumentSnapshot instances. Every render job captures one revision and never reads mutable state. A single ordered commit queue serialises revisions; UI produces requests and consumes results. Coalesce invalidation by revision/tile; bound workers and prioritise interaction over export/cache generation. Export freezes revision/assets while editing continues. Cooperative cancellation is checked between bounded jobs outside GPU dispatches. First-slice rendering proceeds from raster/strokes/river through halo coverage, bounded distance, effects and ordered four-layer composite to viewport/export. Domain changes supply affected bounds plus downstream reach; the renderer does not invent invalidation. Interactive rendering uses the global RenderingDevice/direct textures without routine correctness readback; export may use isolated local device and staged asynchronous readback.
  DATA_6o53iy6h_END

## Local observability and verification boundary
- source: docs/architecture.md §§9,10,12
- type: nfr
- content:
  DATA_am3qcdml_START
  Gestures, commands, revisions, tile jobs and presented updates carry correlation ids. Production telemetry is local and opt-in under project diagnostics. Record sample accounting, commit latency, queue depth, correlated rendered-update latency, monotonic frame intervals, attributed >100 ms stalls, RAM and explicit GPU allocations; label frame callbacks that are not presentation proof. Test domain determinism/invalidation/undo branches, application commit-before-render/revision conflicts/snapshot isolation/cancellation, infrastructure crashes/migrations/integrity/disk-full/import bounds/publication and renderer reference fixtures/resource budgets. Fakes stay at declared ports; domain tests do not inspect Godot nodes. Slice completion includes forced-termination recovery of every acknowledged command and no shipping dependency on Spike.
  DATA_am3qcdml_END
