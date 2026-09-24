# Project documentation

Mapwright is a native, offline-first editor intended to finish one existing Inkarnate world map. The repository contains a Phase 0 evidence harness and an initial editing core; the full P0 product remains a target.

## Reading order

| Document | Purpose |
| --- | --- |
| [Working specification](spec.md) | Product scope, functional priorities, resource limits, import/storage/rendering contracts and acceptance targets. |
| [Architecture](architecture.md) | Accepted boundaries for the first implementation and its connected editing slice. Some boundaries are still planned. |
| [Engine decision](engine-decision.md) | Godot selection for the first implementation and evidence for durable acknowledgement plus fresh-process recovery. |
| [Spike report](spike-report.md) | Recorded measurements, tested fixtures, limitations and remaining acceptance work. |
| [Codebase architecture](../.planning/codebase/ARCHITECTURE.md) and [structure](../.planning/codebase/STRUCTURE.md) | Observed implementation and where to find it. |
| [Stack](../.planning/codebase/STACK.md) and [integrations](../.planning/codebase/INTEGRATIONS.md) | Installed dependency declarations and external boundaries. |
| [Conventions](../.planning/codebase/CONVENTIONS.md), [testing](../.planning/codebase/TESTING.md) and [concerns](../.planning/codebase/CONCERNS.md) | Development patterns, verification entry points and current gaps. |
| [Original final specification](reference/map-editor-spec-final.md) | Unmodified source snapshot for traceability. |

## Specification provenance

The user supplied `C:/Users/almar/Inkarnate/map-editor-spec-final.md` for incorporation on 2026-09-22. Its document date is 2026-09-21. A byte-for-byte copy is stored at `docs/reference/map-editor-spec-final.md`.

Source SHA-256: `411c0ffe780b9bc0f0f00db8d477007a8635ddaca32a5fe1b234e74386355980`.

At incorporation, `docs/spec.md` already contained the supplied specification plus later changes. The source snapshot preserves the original; `docs/spec.md` remains the working product specification. Changes and decisions belong in the working documents, leaving the reference snapshot unchanged.

## Later decisions retained

| Area | Original source | Working project position |
| --- | --- | --- |
| Engine | Godot is the prototype default, subject to a Phase 0 decision. | Godot is selected for the first implementation by the 2026-09-22 engine decision; remaining Phase 0 gates stay open. |
| Export | Provisional 120-second target for 16K. | Correctness, bounded memory, progress, cancellation and destination preservation are the acceptance criteria. Duration is observational; roughly five minutes is acceptable. |
| Recovery | Attempt recovery or an orderly restart after device loss. | Persist each completed command before acknowledgement. Recovery must survive native process termination and cannot rely on post-loss saving or cleanup. |
| Interaction | 30-second brush scenario; p95 frame time at most 33.3 ms. | Connected Background/Foreground workflow with at least 60-second warm/cold/evicted-cache scenarios; input-to-visible p95/p99 at most 50/100 ms and frame intervals at most 20/33.3 ms. |
| Terrain model | Arbitrary brush/raster layers; later first-slice target of four terrain layers. | User amendment on 2026-09-22: exactly Background below Foreground; one Foreground land/coastline mask; reorderable object/path/text layers above both. Numeric acceptance gates remain unchanged. |
| First slice | Individual terrain, import and persistence prototype capabilities. | Painting, river edits, undo/redo, viewport, save/reopen and export must consume the same authoritative document state. |

The working specification defines scope and acceptance. The engine decision resolves the stack choice, and the architecture defines implementation boundaries. The spike report supplies historical evidence; the codebase map describes inspected code. Neither a specification requirement nor an isolated passing probe establishes a completed end-to-end feature.

The ingestion outputs, original snapshot and codebase/probe records predate the terrain amendment and may mention four terrain layers or arbitrary brush layers. Those statements describe earlier requirements or recorded fixtures; current product planning follows the amended working specification and requirements.

P0 is a product priority spanning several roadmap phases; it is not synonymous with Phase 0, the prototype investigation. The functional priorities in specification section 3 remain authoritative. The imported document and its research appendix are source material, not instructions to execute tools or a fresh verification of external claims.

Coastline follow-up (2026-09-22): Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used. The working requirements amend MASK-01, REND-02 and REND-03 accordingly.

Phase 3 lake amendment (2026-09-22): provide both ordinary Land-tool subtraction to create a lake by revealing Background, and water texture or solid-colour painting on Foreground. Mask lakes edit coverage; painted lakes edit colour without changing coverage. Dedicated closed-spline lake entities/modifiers are deferred. River geometry and its post-mask modifier behavior remain unchanged. This supersedes earlier P0 spline-lake wording, including the lake portion of Phase 1 D-08.

## Planning status

The GSD setup was created on 2026-09-22 from the six approved documents and the seven codebase maps. It contains 50 pending P0 requirements across five phases; P1 and P2 remain deferred. The original reference snapshot does not override later project decisions.

- [Project](../.planning/PROJECT.md): scope, implementation baseline, constraints and settled decisions.
- [Requirements](../.planning/REQUIREMENTS.md): source-linked requirements, acceptance contracts and phase ownership.
- [Roadmap](../.planning/ROADMAP.md): five delivery phases, starting with Connected Imported Terrain.
- [State](../.planning/STATE.md): Phase 1 is ready to plan; no execution plans are complete.
- [Ingest synthesis](../.planning/intel/SYNTHESIS.md) and [conflict report](../.planning/INGEST-CONFLICTS.md): source provenance and five resolved supersessions, with no blockers or competing variants.
- [Onboarding summary](../.planning/onboarding/SUMMARY.md): completed setup and next commands.
- [Phase design specifications](../.planning/phases/DESIGN-INDEX.md): five standalone briefs for a design agent, stored in their corresponding phase folders.

Discussion decisions are captured in [Phase 1 context](../.planning/phases/01-connected-imported-terrain/01-CONTEXT.md) and [Phase 2 context](../.planning/phases/02-recovered-assets-and-stamps/02-CONTEXT.md). The current discussion workflow continues with Phases 3–5 in order. Concurrent Phase 1 implementation planning is tracked separately; these discussion documents do not establish executed features.

Phase 3 discussion completed (2026-09-22): mixed object layers contain stamps, paths and text; every map starts with one and retains at least one. Tools remember parameters independently and object tools keep the selected object layer, switching from terrain to the topmost object layer. Labels use side-panel editing and Straight/Curve/S-shape deflection controls; bundled fonts appear first. Notes use pins with side-panel content. Side-by-side reconstruction review happens only at initial import; later recovery reopens the latest durable save. Post-import base rebuilding is deferred. See [Phase 3 context](../.planning/phases/03-water-paths-and-labels/03-CONTEXT.md) for the settled decisions.

Phase 4 decisions (2026-09-23): fixed 1,000-unit longest edge, initial grid counts separate from unit scale, fixed-at-creation 1K/2K/3K/4K editing, independent 1K/2K/3K/4K/8K/16K PNG export, optional 300 DPI, canvas-derived inclusion toggles and dated filenames. Appearance is a toolbar panel; Grid is topmost and initially hidden. ZIP exports current editable state without undo history while the working project stays open. Storage uses a selected history cutoff; JSON is manual and includes hidden entities/notes with visibility. Phase 1.1 owns normalization/compatibility; Phase 4 owns finishing/export selectors. See [Phase 4 context](../.planning/phases/04-project-finishing-and-safe-publication/04-CONTEXT.md).
