# Requirements: Malkav's Mapwright

**Defined:** 2026-09-22
**Core Value:** Finish the existing recovered world map with safe, durable editing and a seamless, bounded 16K PNG export.

## Scope and Sources

These 50 v1 requirements retain the full **P0** priority from the working specification's authoritative 16-row functional table plus its import, persistence, architecture and acceptance contracts. No PRD-derived IDs existed in the approved ingestion; these stable category-number IDs are newly derived from SPEC/ADR sources. All remain Pending because isolated probes and contract tests do not establish a complete product workflow. P0 spans all five GSD phases and is not synonymous with source Phase 0. [S1] §§1,3,13; [S4]; [S5].

Source keys used below: **[S1]** working specification; **[S2]** accepted architecture; **[S3]** final engine decision; **[S4]** recorded spike evidence; **[S5]** approved synthesis. The original specification snapshot is provenance; the working specification and later decisions govern current requirements.

**2026-09-22 user amendment:** P0 terrain is exactly Background below Foreground, with one Foreground land/coastline mask and mixed object layers (stamps, paths and text) above them. This supersedes arbitrary brush/raster layers and the four-terrain-layer workload in the ingestion snapshot. Requirement IDs and phase ownership stay stable; all numeric interaction, resource and correctness gates remain in force.

**Coastline follow-up:** prefer a fixed outer-coast style with unstyled rivers/lakes; omit generated styling if reliable separation is too involved. Style controls, decorative fades, wave rings and isolines move beyond P0. REND-02/03 remain rendering guarantees, not UI controls; distance-specific checks apply only to a distance-field path actually used. No colour/alpha, seam, durability or interaction requirement is waived.

Phase 3 lake amendment (2026-09-22): provide both ordinary Land-tool subtraction to create a lake by revealing Background, and water texture or solid-colour painting on Foreground. Mask lakes edit coverage; painted lakes edit colour without changing coverage. Dedicated closed-spline lake entities/modifiers are deferred. River geometry and its post-mask modifier behavior remain unchanged. This supersedes earlier P0 spline-lake wording, including the lake portion of Phase 1 D-08.

## v1 Requirements

### Document

- [x] **DOC-01**: User can work on one map per project with a fixed 1,000-map-unit longest edge and the other edge determined by aspect ratio, stable IDs and explicit imported transforms. Initial grid columns/rows establish proportions and starting grid; later grid spacing does not resize geometry. All map-relative sizes use stable map units. Editing resolution is chosen at creation/import and fixed in P0; export resolution is independent, both measured along the longest edge. PNG remains bounded to 16,384 px per axis. (Sources: [S1] §3 Document; §§4–5.)
- [x] **DOC-02**: User can save, autosave and reopen the current document; manual save waits for queued commands and saved/unsaved status reflects durable acknowledgement. (Sources: [S1] §3 Document; §10.)
- [x] **DOC-03**: User can delete all render/history caches, reopen and export without losing immutable source rasters, source hashes or native edits; reconstruction stays within the declared render tolerance. (Sources: [S1] §§4,8,10,13.)
- [ ] **DOC-04**: User can export a portable ZIP package of the current editable map and its required assets/data, without inherited undo/redo history, from a consistent SQLite backup and pinned current-state blobs while editing resumes. Packaging leaves working-project history intact and keeps the working project open; integrity validation and reopening succeed before publication. (Sources: [S1] §10.)
- [ ] **DOC-05**: User can see source/history/cache growth separately and retain intact current, history, export and package references through garbage collection, migration, recovery and disk-full failures. (Sources: [S1] §§8,10,14.)

### Inkarnate recovery

- [x] **IMPT-01**: User can import an .ink into a new project in visual recovery mode with the preview backing a locked Background and an initially empty Foreground available for new terrain edits above it, without modifying the original .ink; existing baked coasts are clearly distinguished from editable recovered coverage. (Sources: [S1] §§1,4,11.)
- [x] **IMPT-02**: User can recover Background colour and Foreground colour/coverage bases with intrinsic dimensions, source transforms, source IDs/version and baked-effect provenance; preserve all original rasters and report extra/unmappable terrain as partial recovery with a visual fallback. Native edits are separate from imported history, and untested state-changing commands stop trusted affected replay while preserving a baked fallback. (Sources: [S1] §§4–5,11.)
- [x] **IMPT-03**: User receives readable rejection or degradation for imports exceeding bounded decompression, token/allocation, JSON nesting or image limits, instead of unbounded decoding. (Sources: [S1] §11; [S2] §§8,10.)
- [ ] **IMPT-04**: User can compare an import reconstruction side by side with the original preview, with linked pan/zoom and a concise summary identifying editable, partial or visual-only content, missing assets/fonts and unsupported content. Original source data and unsupported properties remain preserved. Keep the workflow simple; a detailed reconstruction-report workspace is not required. (Sources: [S1] §11; Phase 3 user clarification.)
- [ ] **IMPT-05**: Initial import recovers tested stamp, path, text, grid and layer semantics where supported, preserving source data and visual fallbacks otherwise. After import, the saved Mapwright project is authoritative; recovery reopens its latest durably saved version, including acknowledged autosaved edits. User-facing base reconstruction from remaps, native-edit reapplication and prior-base comparison are deferred. Existing asset/font replacement and ordinary undo remain. (Sources: [S1] §§4,11; Phase 3 user simplification.)

### Layers

- [x] **LAYR-01**: User works with exactly two terrain layers, Background below Foreground, with rename/visible role identity, hide, lock, solo and opacity controls. Their roles/order are fixed; P0 does not add, duplicate, remove or reorder terrain layers. Only Foreground owns the editable coastline mask. (Sources: [S1] §§1,3–4,13; 2026-09-22 user amendment.)
- [ ] **LAYR-02**: User can create, reorder, rename, hide, lock, solo and adjust opacity on mixed object layers containing stamps, paths and text above the fixed terrain pair, without an arbitrary count cap. Every map starts with one object layer; the last cannot be deleted, though it can be emptied. Object tools keep the selected object layer, or visibly switch from a terrain selection to the topmost object layer; hidden/locked guards remain. These layers cannot move below or between Background and Foreground. (Sources: [S1] §3 Layers; §4; Phase 3 decisions.)

### Terrain

- [x] **TERR-01**: User can texture-paint Background or Foreground with a preset library and edged variant, diameter, hardness, opacity, flow, spacing, texture scale/rotation and jitter controls; edged tips have Roughness and corner-smoothing, and tapered texture presets automatically narrow at both stroke ends without pressure. Retain resolved shape, taper, brush parameters, texture identity and stroke geometry for replay; texture painting does not change Foreground land coverage. (Sources: [S1] §3 Terrain; §§4,6.)
- [x] **TERR-02**: User sees a cursor preview of size, falloff and affected region, with stable document-space texture anchoring across strokes, tiles, zoom and export scale. (Sources: [S1] §3 Terrain; §§5,7.)

### Masks

- [x] **MASK-01**: User can add/subtract coverage on Foreground's single land mask, revealing Background, with Edged polygon (Roughness and Smooth corners) or Round soft (Softness) brush modes and diameter in map units; the texture-tip library is not shared with Land. Prefer one fixed outer-coast style, with river/lake banks unstyled; if reliable separation is too involved, the authorized P0 fallback is no generated edge styling. No coastline style controls, decorative fades, wave rings or isolines are required. Coverage remains distinct from layer opacity; Background has no editable coastline mask and there are no separate mask layers. (Sources: [S1] §3 Masks; §6; 2026-09-22 user amendment.)

### Water

- [x] **WATR-01**: User can edit a river centreline, width profile and bank softness as a non-destructive modifier of Foreground's land coverage, revealing Background with live soft-boundary updates across tiles and fixed outer-coast styling only if implemented; later land painting cannot close the river. (Sources: [S1] §3 Water; §6.)
- [ ] **WATR-02**: User can create lakes either by subtracting Foreground coverage with the existing Land tool to reveal Background, or by painting a water texture or solid colour on Foreground without changing coverage. Both use the existing brush controls and durable gesture history; mask lakes can be refilled with Land Add and painted lakes can be repainted. Dedicated spline-lake entities/modifiers are deferred; river geometry is unchanged. (Sources: [S1] §§3–6; 2026-09-22 Phase 3 user amendment.)

### Stamps

- [ ] **STMP-01**: User can import PNG/WebP stamps, place and multi-select them, transform, flip, tint, change opacity and apply a basic shadow. (Sources: [S1] §3 Stamps; §9.)
- [ ] **STMP-02**: User can control stamp order through an order list; overlapping transparent stamps preserve explicit order across atlas pages, and selection accounts for transparency and expanded effect bounds. (Sources: [S1] §3 Stamps; §7.)
- [ ] **STMP-03**: User can paint a scatter brush mixing a selected asset set, with size/rotation ranges and average centre-to-centre spacing in map units. Store seed/RNG identity and resolved placements so undo/reopen/export remain stable even if the algorithm changes; area filling is deferred. (Sources: [S1] §3 Stamps; §8.)

### Paths

- [ ] **PATH-01**: User can draw paths freehand with gentle automatic smoothing, remain ready to draw another, then select/edit their points; import supported paths and edit polyline/Bezier geometry, width, colour, dashes and caps with target-scale geometry shared by viewport and export. (Sources: [S1] §3 Paths; §§5,7,11; Phase 3 decisions.)

### Text

- [ ] **TEXT-01**: User can edit label content in the side panel with live preview and Straight, Curve or S-shape mode; Curve uses -100% to +100% peak deflection with 0% straight and magnitude 100% a semicircle, while S-shape uses one signed percentage for linked equal/opposite bends. Bundled fonts appear first, installed fonts separately; size, tracking, colour, outline and shadow remain, with tangent-aligned glyphs on curved baselines. (Sources: [S1] §3 Text; §5; Phase 3 decisions.)
- [ ] **TEXT-02**: User sees missing/substituted fonts with retained original face/version identity where available; replacement defaults to all labels using the missing font in the current map with preview, intentional Apply and undo. Shaping, rasterisation and placement match viewport and target-scale export. (Sources: [S1] §§5,11; Phase 3 decisions.)

### Grid and notes

- [ ] **NOTE-01**: Every map has a Grid row fixed above terrain and all objects, hidden initially; user can show a square grid with geometry in map units. Initial columns/rows establish proportions and starting grid without defining unit scale; later grid-spacing changes do not resize the map. PNG inclusion starts from canvas visibility and can be overridden for the export. Grid UI remains Phase 4. (Sources: [S1] §§3,5; Phase 3/4 decisions.)
- [ ] **NOTE-02**: User can create pinned notes through Note tool then map click or right-click then Add note, and read/edit them in the side panel. Notes are hidden by default; reveal shows pins only. Notes are excluded from image export by default. (Sources: [S1] §3 Grid and notes; §7; Phase 3 decisions.)

### Selection

- [ ] **SELE-01**: User can select by marquee or object list, Alt-click through overlapping alpha-aware hits, scale multiple objects individually in place, rotate the arrangement around a shared centre, duplicate and copy/paste within the same map while preserving valid identities and references. (Sources: [S1] §3 Selection; §4.)

### History

- [x] **HIST-01**: User can undo/redo with one gesture per step and persistent history/cursor across restart; cursor transitions commit before restoration and a new edit after undo transactionally discards redo. (Sources: [S1] §3 History; §8; [S2] §4.)
- [x] **HIST-02**: User can undo recent raster edits using compatible compressed before/after deltas, and undo older or evicted history through correct reconstruction with progress/cancellation; recent undo affecting at most 16 resident tiles has p95 ≤100 ms. (Sources: [S1] §§8,13.)
- [ ] **HIST-03**: In Project Settings > Storage, user can select a history-list cutoff and explicitly prune older retained history after previewing lost undo steps; create a valid baseline and preserve current/active-snapshot references. Disposable cache cleanup is separate, and its budget never silently limits authoritative history. (Sources: [S1] §§3,8; Phase 4 decisions.)
- [x] **HIST-04**: User can replay native edits using retained immutable assets, resolved parameters, coordinates, RNG version/seeds, font identity and renderer/algorithm compatibility; incompatible caches rebuild, and output-changing upgrades warn and preserve originals. (Sources: [S1] §§4,8.)

### Assets

- [ ] **ASST-01**: User can manage SHA-256-addressed assets in a shared local library across projects, browsing category-first thumbnails with pack/folder/tag filters and search, while each project retains the immutable assets required by its current and retained references; renames preserve references, derived normalisation retains originals, and metadata records title/category/style, size, anchors, transforms, family, light/shadow hints and provenance. (Sources: [S1] §3 Assets; §9.)
- [ ] **ASST-02**: User receives asset validation for duplicate/damaged files, unreasonable dimensions/profiles, stamp alpha/padding/clipping/fringes/scale/anchors/baked shadows and texture seam/scale/colour concerns; decoding obeys pixel/allocation budgets. Loose images get a category/tag import review; usable images with quality warnings import with badges, while damaged/unsupported/over-budget files are rejected. (Sources: [S1] §9.)
- [ ] **ASST-03**: User can identify unresolved art through subtle outlined placeholders with details on hover/selection and a missing-asset report. Preview replacements defaulting to all matching instances in the current map, fit original footprints without stretching, retain positions/source IDs, allow size/anchor and scope adjustments, and undo the result. Remember scoped mappings as suggestions for future import review, not automatic application. (Sources: [S1] §§3,9,11.)
- [ ] **ASST-04**: User can install a pack with a versioned pack.json, stable local IDs, assets/, thumbnails/ and LICENSES/, preserving master files and available provenance/hash/style/projection/transform/tiling/variant metadata. (Sources: [S1] §3 Assets; §9.)

### Filters

- [ ] **FILT-01**: User can open a dedicated Appearance panel from the toolbar for ordered colour adjustment, paper texture and grain. Paper offers texture selection, strength, scale and rotation. Preserve any implemented fixed coast treatment and stamp shadows without restoring deferred coast controls; map-coordinate sampling and stored seeds produce consistent viewport/export appearance. (Sources: [S1] §§3,5,7; Phase 4 decisions.)

### Export

- [x] **EXPT-01**: User can export the connected document to a validated seamless streaming PNG up to 16,384 px per axis from a frozen revision with pinned assets, bounded tiles/halos/bands and no full-size GPU framebuffer; editing keeps priority, progress is visible, cancellation is responsive and failure/cancellation preserves the previous destination through temporary-sibling, flushed, validated atomic publication. (Sources: [S1] §§1–3,7–8,10,13; [S2] §§5,8.)
- [ ] **EXPT-02**: User can choose PNG longest-edge presets 1K/2K/3K/4K/8K/16K (K = 1,024 pixels), see calculated aspect-preserving dimensions, optionally set DPI (initial default 300), and override label/grid inclusion initialized from canvas visibility. No custom pixel dimensions in P0. Preview resampled bases versus target-scale native content and fail clearly for unsupported nonuniform scale or unaffordable effects; publish straight-alpha sRGB with a map-name/date/time default filename. DPI changes metadata, not output pixels. (Sources: [S1] §§3-5,7; Phase 4 decisions.)

### UI and input

- [x] **UIIN-01**: User can edit with mouse input, pan/zoom, shortcuts and resizable panels without bypassing document commands or freezing the interface for import/save work. (Sources: [S1] §3 UI/input; §§4,12; [S2] §§4,8.)
- [ ] **UIIN-02**: User can use a physical tablet as a pointer in the complete editor on supported Windows/Linux configurations, with correct pointer feedback and shortcuts; pressure is not required for P0. (Sources: [S1] §3 UI/input; §§12–13; [S3].)
- [ ] **UIIN-03**: User can follow saved/unsaved state, background-job progress, missing-data reports and responsive cancellation while completing the full map workflow, with resource budgets visible in the UI. (Sources: [S1] §§10,13.)

### Integration

- [ ] **INTG-01**: Through Export > Map data (JSON), user manually exports all map entities, notes and markers, including hidden content and visibility flags, in stable map units with saved source revision identity. Default filename includes map name/date/time; no automatic companion update on save. JSON is derived output, never authoritative storage. (Sources: [S1] §§3,10; Phase 4 decisions.)

### Durability and recovery

- [x] **DURA-01**: Every acknowledged completed edit survives restart: new blobs are hashed, validated, flushed and durably published before one SQLite WAL synchronous=FULL transaction commits commands, revision, materialised state, cursor and references; an ordered commit queue publishes/acknowledges only after commit and before dependent GPU work. (Sources: [S1] §§8,10; [S2] §§3–4,7.)
- [x] **DURA-02**: User can restart after forced termination or device loss during editing/export and recover every acknowledged command without missing references or a partial export; only the active unacknowledged gesture may be lost, and recovery requires no post-loss save, managed cleanup, GPU readback or in-process device recreation. (Sources: [S1] §§8,10,13; [S3]; [S2] §8.)

### Rendering and interaction acceptance

- [x] **REND-01**: User sees Background below masked Foreground and any implemented fixed outer-coast treatment, followed by ordered mixed object layers (stamps, paths and text) and an included Grid above them as introduced, from one authoritative immutable revision in viewport and export, using bounded residency/mips, revision-aware caches, conservative forward invalidation and backward dependency bounds; sequential supports accumulate, shadows are asymmetric and document coordinates never restart at tile edges. (Sources: [S1] §§4–7; [S2] §§3,5–6.)
- [x] **REND-02**: User sees correct colour and soft alpha through a declared sRGB-input, linear-premultiplied compositor; coverage and any used distance fields are non-colour data, and original image identity is retained for normalised derivatives. This is rendering correctness, not a user-facing control panel, and applies with or without coast styling. (Sources: [S1] §§5–6.)
- [x] **REND-03**: User sees no boundary-correlated artefacts: small tiled/whole-image reference fixtures differ by at most 1 channel value. If a distance-field styling path is used, its error is ≤0.5 output pixel in the active band at the same scale against its declared 0.5 source contour, and outer-coast styling excludes river/lake banks and mouths correctly. With the authorized unstyled fallback, unused distance/style checks are not applicable; seamless soft coverage and colour checks still pass. (Sources: [S1] §§6,13.)
- [x] **REND-04**: User can paint across tiles, pan/zoom while painting, edit river points/width and undo/redo at a 1920 × 1080 viewport with input-to-visible p95 ≤50 ms/p99 ≤100 ms and frame intervals p95 ≤20 ms/p99 ≤33.3 ms; each scenario runs ≥60 seconds with realistic bursts under warm, cold and forced-eviction caches and reports correlated updates, sample counts, p50/p95/p99, queues, worst/all >100 ms stalls with causes, RAM and GPU allocations. (Sources: [S1] §13; [S2] §9.)
- [x] **REND-05**: User can import, paint and export within recorded hardware budgets: editor GPU allocation 25% of reported VRAM capped at 4 GiB with engine/compositor headroom, decoded CPU cache 512 MiB, export CPU working buffers 512 MiB, process peak RAM ≤25% of system RAM and history acceleration disk cache 4 GiB; startup limit queries and smaller bands/concurrency enforce the configured envelope. (Sources: [S1] §2; §13.)

### Platform and complete-map acceptance

- [ ] **ACPT-01**: User can run the offline native editor and complete import/edit/save/reopen/undo/export on supported Windows and Linux configurations; unavailable required RenderingDevice backend is reported as unsupported for editing while inspection/recovery tooling remains usable. (Sources: [S1] §§1–2,12–13; [S3].)
- [ ] **ACPT-02**: User can finish the actual recovered world map with the approved external starter art and receive a seamless 16K PNG; full-workflow acceptance covers cache deletion, degraded imports, transparent ordering, sequential radius-20 blur dependency fixtures, offset shadows, corner-crossing stamps, fixed outer-coast/inland-water separation if styling is used, grain, texture sampling, boundary-crossing rivers, interrupted storage/export and fresh-process recovery within the unchanged interaction/resource/correctness gates. (Sources: [S1] §§1,9,13; [S2] §§10,12.)

## Acceptance Contracts

These clauses define how the requirements above are accepted; they do not add duplicate requirement checkboxes or separate phase ownership. A feature added later inherits the same document/history/render/export guarantees.

| Contract | Exact acceptance |
|----------|------------------|
| Connected first slice | The real imported map, visible Background and Foreground, texture painting on both, Foreground mask/coastline effects, one editable Foreground river, undo/redo, durable save/reopen, viewport and export all consume one authoritative document. The existing preview, GPU brush and synthetic export probes do not satisfy this. |
| Interaction | At 1920 × 1080 on recorded hardware: input-to-visible p95 ≤50 ms and p99 ≤100 ms; frame-interval p95 ≤20 ms and p99 ≤33.3 ms. Recent undo touching ≤16 resident tiles: p95 ≤100 ms. |
| Scenarios and evidence | Each of boundary-crossing painting, pan/zoom while painting, river point/width edits with coastline updates and undo/redo runs ≥60 seconds with realistic pointer/pen rates and bursts, repeated under warm, cold and forced-eviction caches. Report p50/p95/p99, worst stall and every stall >100 ms with attributed cause, queue depth, RAM/GPU allocations, visible correctness, and generated/processed/coalesced/dropped samples. |
| Latency endpoint | Correlate each input/edit sequence ID to the rendered update containing it. Generic frame callbacks do not prove this. Use monotonic frame timestamps, state the exact endpoint and excluded presentation latency, and isolate correctness readbacks from interactive work. Local diagnostics are opt-in. |
| Resource envelope | Initial targets: editor GPU allocation 25% of reported VRAM, capped at 4 GiB with Godot/compositor headroom; decoded CPU cache 512 MiB; export CPU working buffers 512 MiB; total process peak RAM ≤25% of system RAM including import/export; history acceleration disk cache 4 GiB, separate from sources and authoritative history. Record the actual GPU/driver/VRAM/RAM/OS, account for engine/driver overhead and qualify these source-described initial targets. No percentage gate is already proven by a probe's absolute byte count. |
| Bounded execution | Query device limits at startup; begin with 512 × 512 raster-pixel residency tiles at the selected scale and mip levels and 2,048 px export interiors, shrinking bands/concurrency to fit all buffers, staging, encoder and queues. Pin active jobs and snapshot references, evict reconstructible data, and preflight unfit effect footprints. No full-size 16K framebuffer. |
| Geometry and colour | Top-left origin, x right/y down, double-precision CPU document coordinates and tile-relative GPU math with explicit origins. Map geometry has a fixed 1,000-unit longest edge with aspect-scaled shorter edge; map units are independent of source, editing and export pixels. Initial grid counts do not set unit scale. Editing/export presets measure the longest raster edge; export changes sampling density, preserving geometry and map-scale widths/effects. sRGB assets retain originals when normalised, compositing uses linear premultiplied intermediates (RGBA16F where supported), coverage/distance is non-colour, and PNG is straight-alpha sRGB. |
| Coastline/reference correctness | Evaluate base coverage → painted masks → water modifiers → final soft coverage. Prefer one fixed outer-coast style, excluding river/lake banks and mouths through verified boundary identity; otherwise use the authorized unstyled fallback. Always require tiled/whole ≤1 channel-value difference and no boundary-correlated artefacts. Any used distance field must declare its 0.5 source contour, meet ≤0.5 output-pixel error in the active band, and bound sampling/effect reach; R16F must prove tolerance or use R32F. A discrete-seed oracle alone does not establish contour precision. Unused distance/style checks are explicitly not applicable, not reported as tested. |
| Dependency and ordering fixtures | Include two sequential radius-20 blurs (40-pixel combined support), asymmetric offset shadows, corner-crossing stamps, fixed coast treatment if used, grain, continuous texture sampling, a boundary-crossing river and transparent stamps alternating atlas pages against an unbatched order reference. Blur fixtures do not promote the P1 blur feature. Decorative coastline ring fixtures are deferred with those effects; if fixed coast styling is used, test unstyled river/lake banks and mouths. Bounds propagate backward for inputs and forward for invalidation; branches union and global/infinite-support effects require defined prepasses/cutoffs or rejection. |
| Durable command ordering | Validate the base revision, durably publish immutable blobs, commit commands/revision/materialised state/cursor/references in SQLite WAL synchronous=FULL, publish the immutable snapshot, acknowledge command/revision, then enqueue dependent render work. Transient gesture preview is unacknowledged. Validation/disk failure cannot advance the revision. |
| Crash and device recovery | Interrupt before/after blob publication, transaction commit and derived-file update; reopen the last acknowledged revision with no missing references. Native device-loss termination must lose no acknowledged command, corrupt no project, preserve the previous export and publish no partial output, without post-loss saving/readback or reliable managed cleanup. Export restarts from its frozen revision after recovery. |
| History and references | Cache/renderer changes may invalidate acceleration but cannot invalidate retained commands/sources/cursor. New edits discard redo; explicit pruning reports lost undo and creates a baseline. Garbage collection traces current state, retained history, exports and packaging snapshots. Disk-full preserves the prior commit. Cross-GPU bit identity is not promised; declared renderer tolerances govern. |
| Import degradation | Missing assets/fonts, unknown state-changing commands, differing cache resolutions and baked effects produce correct supported/partial/visual-only reports without double painting. Only tested semantics replay; preserve unsupported/source metadata and fallback bases. Structured recovery preserves supported parameters, not pixel identity. |
| Export publication | Freeze revision/assets while editing can continue; use the shared effect graph and target-scale path/text work, crop halos and stream rows. Validate dimensions/decodability after flushing a temporary sibling and atomically replace only on success. Progress/cancellation and preservation of the previous destination are gates. **Duration has no threshold**; roughly five minutes for 16K is acceptable and time is recorded only. |
| Packaging and derived data | Take a consistent SQLite backup/snapshot, pin required current-state blobs, resume editing, build a current-editable-state ZIP without inherited undo/redo, then validate and reopen before publication. Keep the working project/history intact and open. Never replace an open project directory for autosave. Preview/manifest are asynchronous revision-labelled derived data; integration JSON is generated only on explicit export. Local supported filesystems only; network/cloud-synced directories are excluded. |
| Platform and boundaries | Godot .NET and accepted inward dependencies stay in force; shipping code has no Spike dependency. Validate physical tablet-as-pointer input and supported Windows/Linux configurations on real hardware; software GPU evidence does not establish real-driver behaviour. Unsupported required GPU backend does not imply a CPU renderer. |

Sources: [S1] §§2,4–13; [S2] §§2–10,12; [S3]; [S4] limitations. Existing source maps identify the current implementation baseline; no new test or benchmark run is claimed.

## P0 Functional Coverage

Every authoritative functional row is preserved. Import, safe packaging, rendering, durability and acceptance requirements additionally capture technical contracts outside that table.

| Source §3 subsystem | v1 coverage |
|---------------------|-------------|
| Document | DOC-01, DOC-02, DOC-03; DURA-01, DURA-02 |
| Layers | LAYR-01, LAYR-02 |
| Terrain | TERR-01, TERR-02 |
| Masks | MASK-01 |
| Water | WATR-01, WATR-02 |
| Stamps | STMP-01, STMP-02, STMP-03 |
| Paths | PATH-01 |
| Text | TEXT-01, TEXT-02 |
| Grid and notes | NOTE-01, NOTE-02 |
| Selection | SELE-01 |
| History | HIST-01, HIST-02, HIST-03, HIST-04 |
| Assets | ASST-01, ASST-02, ASST-03, ASST-04 |
| Filters | FILT-01; MASK-01, STMP-01 |
| Export | EXPT-01, EXPT-02 |
| UI/input | UIIN-01, UIIN-02, UIIN-03 |
| Integration | INTG-01 |

## Deferred P1 Requirements

These source P1 features are tracked separately and are not in the v1 roadmap. Promotion requires an explicit scope/roadmap change; existing source roadmap prose does not promote them. [S1] §3 and related technical sections.

| Subsystem | Deferred P1 scope |
|-----------|-------------------|
| Document | Multiple maps; sparse 65,536 × 65,536 with its own benchmark; style remapping. |
| Layers | Groups, blend modes and adjustment layers. |
| Terrain | Pen pressure, procedural noise brushes and larger footprints. |
| Masks | Coastline styling controls, decorative fades, wave rings, isolines, hatching and richer bank effects. Fixed outer-coast styling is also deferred if the authorized unstyled P0 fallback is selected. |
| Water | Dedicated closed-spline lake modifiers; tributary junctions, fill-from-point lakes, sketch-to-river and mouth widening; explicit bake-to-mask. |
| Stamps | Scatter by filling a drawn area; tested soft-alpha clip modes, flattening with retained source/unflatten, align/distribute, variants and a 50K-instance benchmark with specified visibility/overdraw. |
| Paths | Taper, repeated assets along paths with spacing/phase/tangent alignment, and junction snapping. |
| Text | Text on arbitrary paths, freeform label-curve editing, independent S-bend controls, glow and style presets. |
| Grid and notes | Flat/pointy hex and isometric grids; pixels-per-cell presets showing resulting dimensions; note links. |
| Selection | Lasso and cross-map copy/paste. |
| History | Named snapshots and optional branches. |
| Assets | Linked assets with explicit changed-hash updates and collect-assets packaging; SVG with external resources disabled; hot reload; Wonderdraft/Dungeondraft pack readers. |
| Filters | Blur, sharpen, bloom, LUTs and per-layer filters. |
| Export | JPEG with explicit background/encoder limits, WebP at most 16,383 per axis, bounded TIFF/BigTIFF, poster PDF and VTT presets. |
| UI/input | Pressure, dock layouts, themes and multi-window. |
| Integration | Scribe's Tower mapping. |

Additional Phase 3 deferral: post-import base reconstruction from changed asset mappings and reapplication of later native edits. Initial import comparison remains P0; detailed reconstruction-report UI is not required.

Additional Phase 4 deferrals: mid-project editing-resolution changes, custom pixel dimensions, physical-size-driven output, full-history ZIP variants and automatic integration JSON on save.

## Deferred P2 Requirements

These optional source P2 features are distinct from P1 and from current delivery. [S1] §3.

| Subsystem | Deferred P2 scope |
|-----------|-------------------|
| Document | Linked nested maps. |
| Layers | General layer effects. |
| Terrain | Height channel and hillshade. |
| Masks | Vector coastline editing. |
| Water | Elevation-driven descent and flooding. |
| Stamps | Collision- or terrain-aware placement. |
| Paths | Road network tools. |
| Text | Automatic label placement. |
| Grid and notes | Universal VTT walls, doors and lights. |
| Selection / History | No additional P2 items in the authoritative functional table. |
| Assets | Community index. |
| Filters | Normal-mapped lighting and fog of war. |
| Export | Layered ORA/PSD. |
| UI/input | Scripting API. |
| Integration | Deeper integration. |

## Out of Scope

| Feature or promise | Reason |
|--------------------|--------|
| Accounts, cloud sync, galleries, marketplace, collaboration and subscriptions | Single-user offline purpose. |
| In-app image generation, commercial positioning and public asset-library development | Artwork is external; the outcome is the user's existing map. |
| Full Inkarnate parity or pixel-identical structured import | Only tested supported semantics are reconstructed; unsupported data is preserved/reported. |
| Parallel Rust + wgpu implementation | Godot .NET is final for first implementation; only demonstrated uncontainable engine constraints justify reconsideration. |
| CPU software-renderer fallback, network-share/cloud-synced authoritative projects | Outside the P0 rendering/storage contract. |
| Unlimited instantaneous undo in fixed storage | Persistent history has budgeted acceleration and explicit pruning. |
| Export-duration pass/fail target | Correctness, recoverability and bounded resources take priority. |

## Traceability

Every v1 ID has exactly one delivery owner. Cross-phase prerequisites and ongoing regression obligations do not duplicate ownership.

| Requirement | Phase | Status |
|-------------|-------|--------|
| DOC-01 | Phase 1.1 | Complete |
| DOC-02 | Phase 1 | Complete |
| DOC-03 | Phase 1 | Complete |
| DOC-04 | Phase 4 | Pending |
| DOC-05 | Phase 4 | Pending |
| IMPT-01 | Phase 1 | Complete |
| IMPT-02 | Phase 1 | Complete |
| IMPT-03 | Phase 1 | Complete |
| IMPT-04 | Phase 3 | Pending |
| IMPT-05 | Phase 3 | Pending |
| LAYR-01 | Phase 1 | Complete |
| LAYR-02 | Phase 3 | Pending |
| TERR-01 | Phase 1 | Complete |
| TERR-02 | Phase 1 | Complete |
| MASK-01 | Phase 1 | Complete |
| WATR-01 | Phase 1 | Complete |
| WATR-02 | Phase 3 | Pending |
| STMP-01 | Phase 2 | Pending |
| STMP-02 | Phase 2 | Pending |
| STMP-03 | Phase 2 | Pending |
| PATH-01 | Phase 3 | Pending |
| TEXT-01 | Phase 3 | Pending |
| TEXT-02 | Phase 3 | Pending |
| NOTE-01 | Phase 4 | Pending |
| NOTE-02 | Phase 3 | Pending |
| SELE-01 | Phase 2 | Pending |
| HIST-01 | Phase 1 | Complete |
| HIST-02 | Phase 1 | Complete |
| HIST-03 | Phase 4 | Pending |
| HIST-04 | Phase 1 | Complete |
| ASST-01 | Phase 2 | Pending |
| ASST-02 | Phase 2 | Pending |
| ASST-03 | Phase 2 | Pending |
| ASST-04 | Phase 2 | Pending |
| FILT-01 | Phase 4 | Pending |
| EXPT-01 | Phase 1 | Complete |
| EXPT-02 | Phase 4 | Pending |
| UIIN-01 | Phase 1 | Complete |
| UIIN-02 | Phase 5 | Pending |
| UIIN-03 | Phase 5 | Pending |
| INTG-01 | Phase 4 | Pending |
| DURA-01 | Phase 1 | Complete |
| DURA-02 | Phase 1 | Complete |
| REND-01 | Phase 1 | Complete |
| REND-02 | Phase 1 | Complete |
| REND-03 | Phase 1 | Complete |
| REND-04 | Phase 1 | Complete |
| REND-05 | Phase 1 | Complete |
| ACPT-01 | Phase 5 | Pending |
| ACPT-02 | Phase 5 | Pending |

**Coverage:**

- v1 requirements: 50 total
- Mapped exactly once: 50
- Unmapped: 0
- Duplicate phase assignments: 0
- Complete: 0
- P0 functional subsystem rows retained: 16/16

[S1]: ../docs/spec.md
[S2]: ../docs/architecture.md
[S3]: ../docs/engine-decision.md
[S4]: ../docs/spike-report.md
[S5]: intel/SYNTHESIS.md

---
*Last updated: 2026-09-22 after approved synthesis, requirement derivation and roadmap mapping.*
