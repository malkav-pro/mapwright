# Roadmap: Malkav's Mapwright

## Overview

Deliver the full P0 outcome: finish the existing recovered Inkarnate world map, edit it safely and export a seamless bounded 16K PNG. The existing Godot harness and engine-independent core are the starting point; the first phase connects them into a usable terrain workflow. Subsequent phases complete art replacement, cartographic detail, durable publication and Windows/Linux acceptance. Requirements derive from the working [specification](../docs/spec.md) and accepted [architecture](../docs/architecture.md); evidence comes from the [codebase map](codebase/ARCHITECTURE.md) and [spike report](../docs/spike-report.md).

## Milestones

- 🚧 **v1.0 Finish the Existing Map** — Phases 1–5; all 50 v1/P0 requirements; not started.

## Phases

GSD numbering starts at **Phase 1** for remaining delivery work. The source's **Phase 0** names its prototype investigation and is not declared complete here; **P0** is a product priority spanning this roadmap. Source roadmap numbers are not GSD phase IDs. Sequential numbering and standard granularity are used; no configuration preferences are created. Decimal phases are reserved for later inserted work.

- [x] **Phase 1: Connected Imported Terrain** - User can recover the existing map and texture-paint Background and Foreground, edit Foreground's mask and a river, undo/redo, save/reopen and export from one durable authoritative document.
- [x] **Phase 1.1: Stable Map Units** - User can edit imported maps in a fixed 1,000-map-unit coordinate space without geometry changing when editing or export pixel resolution changes.
- [ ] **Phase 2: Recovered Assets and Stamps** - User can replace missing artwork with managed local packs and compose ordered, selectable, repeatable stamp arrangements on the recovered map.
- [ ] **Phase 3: Water, Paths and Labels** - User can finish lakes, editable paths, three-mode labels, mixed object layers and notes, with simple side-by-side review during initial import.
- [ ] **Phase 4: Project Finishing and Safe Publication** - User can finish the map's appearance and publish reproducible images, integration data and portable projects while controlling retained history and storage.
- [ ] **Phase 5: Windows and Linux Completion Workflow** - User can finish the actual recovered world map through the complete offline workflow on supported Windows and Linux configurations, including physical tablet input and dependable feedback.

## Phase Details

Separate design-agent input briefs are listed in the [phase design index](phases/DESIGN-INDEX.md). They define design tasks and required controls; they are not approved visual designs or implementation plans.

### Phase 1: Connected Imported Terrain

**Design brief**: [Phase 1 design specification](phases/01-connected-imported-terrain/01-DESIGN-SPEC.md)

**Goal**: User can recover the existing map and texture-paint Background and Foreground, edit Foreground's mask and a river, undo/redo, save/reopen and export from one durable authoritative document.
**Depends on**: Nothing (first remaining phase; builds on the existing harness and core)
**Requirements**: DOC-02, DOC-03, IMPT-01, IMPT-02, IMPT-03, LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01, HIST-01, HIST-02, HIST-04, EXPT-01, UIIN-01, DURA-01, DURA-02, REND-01, REND-02, REND-03, REND-04, REND-05
**Success Criteria** (what must be TRUE):

1. User can import the real .ink without changing it, choose a locked-preview visual fallback or preserved terrain bases, and work with exactly Background below Foreground and their visibility/lock/solo/opacity controls in a map up to 16K. Terrain roles/order are fixed; unsupported extra source layers remain preserved and reported instead of becoming extra editable terrain. Oversized or unsupported input produces bounded, readable recovery outcomes.
2. User can paint anchored textures on either terrain layer and soft coverage on Foreground's single mask, edit river points/width/bank softness and see correct soft land/water edges. Prefer a fixed outer-coast style with unstyled river banks; if reliable separation is too involved, the authorized unstyled fallback satisfies P0. No coastline style controls, decorative fades, rings or isolines. Mask subtraction and water reveal Background; texture painting does not change coverage. Mouse, pan/zoom, shortcuts and resizable panels remain required.
3. User can save/reopen, undo/redo and delete all caches without losing sources or native edits; older undo shows progress/cancellation, new edits invalidate redo, and restart after interrupted commits or device loss restores every acknowledged edit without relying on post-loss saving or GPU readback.
4. User can export the same frozen revision shown by the shared viewport/render graph to a validated seamless 16K PNG with progress, responsive cancellation and preservation of the previous destination; colour, ≤1-channel tiled/reference difference and all configured resource budgets hold; a used distance-field style also meets ≤0.5-output-pixel error against its declared contour, while unused distance checks are not applicable.
5. At 1920 × 1080 on recorded hardware, all four ≥60-second interaction scenarios pass under warm/cold/evicted caches: input-to-visible p95/p99 ≤50/100 ms, frame intervals ≤20/33.3 ms, and recent undo of ≤16 resident tiles p95 ≤100 ms, with sequence-correlated results, sample accounting and attributed stalls.

**Plans**: 9/10 plans executed

Plans:
**Wave 1**

- [x] 01-01-PLAN.md — Connected source→edit→durable reopen→viewport/PNG tracer

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 01-03-PLAN.md — Fixed two-role texture painting and deterministic recipes

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 01-02-PLAN.md — Honest two-mode bounded .ink recovery and provenance
- [x] 01-04-PLAN.md — Foreground Land mask and river modifier commands
- [x] 01-05-PLAN.md — Durable history, save drain and cancellable replay

**Wave 4** *(blocked on Wave 3 completion)*

- [x] 01-06-PLAN.md — Shared colour-correct bounded renderer and coast branch

**Wave 5** *(blocked on Wave 4 completion)*

- [x] 01-07-PLAN.md — Frozen PNG publication and fresh-process recovery

**Wave 6** *(blocked on Wave 5 completion)*

- [x] 01-08-PLAN.md — Approved native project entry and editor shell

**Wave 7** *(blocked on Wave 6 completion)*

- [x] 01-09-PLAN.md — Cancellable tool gestures and immediate editing-scope cues

**Wave 8** *(blocked on Wave 7 completion)*

- [x] 01-10-PLAN.md — Connected real-map and hardware acceptance evidence
  - Verified 2026-09-23 on the pixel-space baseline; see [01-VERIFICATION.md](phases/01-connected-imported-terrain/01-VERIFICATION.md) (5/5, worst p95 36.74 ms) and [01-ACCEPTANCE.md](phases/01-connected-imported-terrain/01-ACCEPTANCE.md). All 23 inherited gates re-passed on 2026-09-24 in Phase 1.1 Full run `20260924T144810622Z-45631c4ea584424d9c035c60f5ab8996` with the GPU renderer.

**UI hint**: yes

### Phase 01.1: Stable Map Units (INSERTED)

**Goal:** User can import and edit a map whose longest geometric edge is exactly 1,000 stable map units, with aspect-preserving shorter edge, explicit source-to-map transform and independent editing/export pixel resolutions.
**Requirements**: DOC-01
**Depends on:** Phase 1
**Success Criteria** (what must be TRUE):

1. New and imported documents store map geometry in double-precision map units, with a 1,000-unit longest edge; grid counts and raster dimensions do not redefine that geometry.
2. Imported raster bases and reconstructed editable geometry use an explicit source-to-map transform, preserving source aspect ratio and visual alignment.
3. Brush diameters and other map-relative geometry remain stable when editing, display or export pixel resolution changes; screen-space controls remain in screen pixels.
4. Existing Phase 1 projects reopen without silently reinterpreting their stored pixel-space geometry; migration or explicit legacy handling is tested.
5. Viewport and PNG output of the same revision agree across supported raster resolutions, with Phase 1 durability and bounded-rendering contracts preserved.

**Plans:** 5/5 plans executed in 3 waves, plus redesign plans 06-09 (completed 2026-09-24)

Plans:

**Wave 1**

- [x] 01.1-01-PLAN.md — Normalized import-to-reopen-to-output tracer

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 01.1-02-PLAN.md — Safe legacy project conversion or explicit refusal
- [x] 01.1-03-PLAN.md — Stable map-unit editing and gesture cues
- [x] 01.1-04-PLAN.md — Bounded target-resolution rendering and PNG export

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 01.1-05-PLAN.md — Connected map-unit and hardware acceptance
  - Verified 2026-09-24 in Full run `20260924T144810622Z-45631c4ea584424d9c035c60f5ab8996` (`VERIFY_REPORT PASS`); see [01.1-ACCEPTANCE.md](phases/01.1-normalize-document-geometry-to-1-000-map-units-with-independ/01.1-ACCEPTANCE.md).

**Renderer redesign** *(unblocks Plan 05)*

- [x] 01.1-06-PLAN.md — Run-isolated acceptance evidence (R0)
- [x] 01.1-07-PLAN.md — Real-map GPU tracer (R1 probe; halted, superseded)
- [x] 01.1-08-PLAN.md — Strict GPU reconstruction (R1 probe; halted, superseded)
- [x] 01.1-09 — Production fp64 GPU terrain and export, session-held durable storage (R2-R5); executed from [01.1-RENDERER-REDESIGN.md](phases/01.1-normalize-document-geometry-to-1-000-map-units-with-independ/01.1-RENDERER-REDESIGN.md) at user direction. See [01.1-09-SUMMARY.md](phases/01.1-normalize-document-geometry-to-1-000-map-units-with-independ/01.1-09-SUMMARY.md)

### Phase 2: Recovered Assets and Stamps

**Design brief**: [Phase 2 design specification](phases/02-recovered-assets-and-stamps/02-DESIGN-SPEC.md)

**Goal**: User can replace missing artwork with managed local packs and compose ordered, selectable, repeatable stamp arrangements on the recovered map.
**Depends on**: Phase 1.1
**Requirements**: STMP-01, STMP-02, STMP-03, SELE-01, ASST-01, ASST-02, ASST-03, ASST-04
**Success Criteria** (what must be TRUE):

1. User can browse a shared managed library through category-first thumbnails and folder/tag/pack filters, retain used assets in each project, inspect warning badges and metadata, and rename assets without breaking references.
2. User can import a versioned asset-pack manifest with stable local IDs and retained metadata while master files remain unchanged and decoding/cache allocations stay bounded.
3. User can locate missing artwork from labelled placeholders and reports, replace it while preserving geometry/source identity, and place PNG/WebP stamps with transforms, flips, tint, opacity and basic shadows.
4. User can marquee-select, use an object list or Alt-click through overlaps, scale selected stamps individually in place, rotate them as an arrangement, duplicate and copy/paste within the map; an explicit stamp order list produces the same transparent overlap across atlas pages as the unbatched reference.
5. User can paint a scatter brush with a selected asset set, size/rotation ranges and average spacing in map units, and get the same persisted placements after undo/reopen/export or a scattering-algorithm change; area filling is deferred.

**Plans**: TBD
**UI hint**: yes

### Phase 3: Water, Paths and Labels

**Design brief**: [Phase 3 design specification](phases/03-water-paths-and-labels/03-DESIGN-SPEC.md)

**Goal**: User can finish lakes, editable paths, three-mode labels, mixed object layers and notes, with simple side-by-side review during initial import.
**Depends on**: Phase 2
**Requirements**: IMPT-04, IMPT-05, LAYR-02, WATR-02, PATH-01, TEXT-01, TEXT-02, NOTE-02
**Success Criteria** (what must be TRUE):

1. User can create lakes with Land-tool subtraction to reveal Background or paint water texture/solid colour on Foreground while preserving its mask. Both support existing brushes, ordinary repainting, gesture undo/redo, save/reopen and export. No dedicated spline-lake object or bank styling is required; editable river modifiers remain unchanged.
2. User can draw paths freehand with gentle smoothing, remain ready to draw another, and edit supported imported/new polyline/Bezier points, widths, colours, dashes and caps with matching viewport/export geometry.
3. User can edit label content in the side panel, choose Straight, Curve or linked S-shape deflection modes, and control fonts, size, tracking, colour, outline and shadow. Bundled fonts appear first; missing-font replacement previews all matching labels by default, and shaping/placement match export.
4. User can manage mixed stamp/path/text object layers above fixed Background/Foreground with reorder, rename, hide, lock, solo and opacity. Every map starts with one object layer and cannot delete the last. Tools keep independent settings and follow the settled terrain-to-topmost-object routing; notes support both creation routes and pin-only reveal with side-panel content, hidden and image-excluded by default.
5. User can compare import reconstruction side by side with the original preview, with linked pan/zoom, a concise summary and supported editable content or preserved visual fallbacks. Reconstruction is limited to initial import. Crash recovery reopens the latest durably saved Mapwright project; ordinary asset/font replacement and undo remain.

**Plans**: TBD
**UI hint**: yes

### Phase 4: Project Finishing and Safe Publication

**Design brief**: [Phase 4 design specification](phases/04-project-finishing-and-safe-publication/04-DESIGN-SPEC.md)

**Goal**: User can finish the map's appearance and publish reproducible images, integration data and portable projects while controlling retained history and storage.
**Depends on**: Phase 3
**Requirements**: DOC-04, DOC-05, NOTE-01, HIST-03, FILT-01, EXPT-02, INTG-01
**Success Criteria** (what must be TRUE):

1. User can open Appearance from the toolbar for ordered colour adjustments, selectable paper with strength/scale/rotation and stable grain, preserving existing coast/shadow behavior without adding deferred coast controls.
2. User can manage a topmost initially hidden square Grid, use independent longest-edge editing/export presets with stable 1,000-unit geometry, and export PNG with canvas-derived label/grid overrides, optional DPI and honest resampling/re-rendering preflight; existing colour/resource/publication gates hold.
3. In Project Settings > Storage, user sees source/current-map, history and cache usage separately, prunes via a selected history cutoff with a lost-step preview/new baseline, and retains valid current and active-job references through cleanup/recovery/disk-full conditions.
4. User exports a validated portable ZIP of current editable state and required assets without inherited undo/redo, while continuing in the original project with its history intact; snapshot/reopen validation precedes publication.
5. User manually exports all entities, notes and markers, including hidden content and visibility, as revision-labelled JSON. PNG, ZIP and JSON use map-name/date/time default filenames; saving does not automatically update integration JSON.

**Plans**: TBD
**UI hint**: yes

### Phase 5: Windows and Linux Completion Workflow

**Design brief**: [Phase 5 design specification](phases/05-windows-and-linux-completion-workflow/05-DESIGN-SPEC.md)

**Goal**: User can finish the actual recovered world map through the complete offline workflow on supported Windows and Linux configurations, including physical tablet input and dependable feedback.
**Depends on**: Phase 4
**Requirements**: UIIN-02, UIIN-03, ACPT-01, ACPT-02
**Success Criteria** (what must be TRUE):

1. User can run the native offline editor through import/edit/save/reopen/undo/export on supported Windows and Linux configurations; missing required RenderingDevice support produces a clear unsupported-editor result while inspection/recovery remains available.
2. User can use a physical tablet as a pointer with correct feedback and shortcuts in the complete workflow; the documented P0 acceptance does not depend on pressure support.
3. User can see saved/unsaved state, resource budgets and missing-data reports and follow or cancel background work responsively throughout the full editor.
4. User can finish the actual recovered map with approved external starter art and produce the seamless 16K PNG while the complete acceptance corpus passes unchanged interaction, memory, seam, dependency, transparent-ordering, degraded-import, history and interruption/restart contracts on recorded supported configurations.

**Plans**: TBD
**UI hint**: yes

## Acceptance Rules

The exact shared contracts in [REQUIREMENTS.md](REQUIREMENTS.md#acceptance-contracts) apply from the first connected slice and remain in force as features are added. Export duration is an observation, **never a pass/fail gate**. Phase 5 validates the complete product; it does not defer durability, bounded export or correlated interaction acceptance from Phase 1. Sequential-blur fixtures validate dependency composition without promoting P1 blur UI. Every phase adds its features through the same durable command, history and shared-rendering contracts.

Keep the accepted Godot stack and inward dependency boundaries. Rendering/storage adapters stay behind application ports; the shipping application must not depend on historical/destructive Spike code. A failed target requires attribution before reconsidering the engine. [Sources: architecture §§2,10–12](../docs/architecture.md), [engine decision](../docs/engine-decision.md).

## Progress

Execution order: 1 → 1.1 → 2 → 3 → 4 → 5. Phase 1.1 closes the document-unit amendment before Phase 2.

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Connected Imported Terrain | 10/10 | Complete | 2026-09-23 |
| 1.1. Stable Map Units | 9/9 | Complete | 2026-09-24 |
| 2. Recovered Assets and Stamps | 0/TBD | Not started | - |
| 3. Water, Paths and Labels | 0/TBD | Not started | - |
| 4. Project Finishing and Safe Publication | 0/TBD | Not started | - |
| 5. Windows and Linux Completion Workflow | 0/TBD | Not started | - |

**Coverage:** 50/50 v1 requirements assigned exactly once; 0 orphaned, 0 duplicated; all Pending.

---
*Created: 2026-09-22 from the approved six-document synthesis and existing codebase evidence.*
