# Malkav's Mapwright

## What This Is

Malkav's Mapwright is a single-user, offline native map editor for finishing the user's existing Inkarnate world map. It recovers terrain and structure, supports terrain/mask painting, rivers and lakes, stamps, paths and straight/curved labels, and preserves durable history while exporting a seamless PNG up to 16,384 px per axis. Windows is the development platform; Linux acceptance is required. [Source: specification §§1,3](../docs/spec.md).

## Core Value

Finish the existing recovered world map with safe, durable editing and a seamless, bounded 16K PNG export.

## Requirements

### Validated

No complete P0 product requirement is marked validated. Existing core contracts and isolated probes provide implementation evidence, not a connected accepted editor.

### Active

- Connect recovered Background/Foreground terrain, texture painting on both, Foreground mask/coastline and river editing, undo/redo, save/reopen, viewport and export through one authoritative document.
- Retain the full P0 scope across all 16 functional subsystems, including lakes, curved text, asset packs, layers, filters, import reports, retention, safe packaging and integration JSON.
- Replace missing artwork with managed external assets and complete the actual map using the full Windows/Linux workflow.
- Meet the exact resource, interaction, correctness and recovery contracts in [REQUIREMENTS.md](REQUIREMENTS.md).

### Out of Scope

- Accounts, cloud sync, galleries, marketplace, collaboration, subscriptions, commercial positioning and public asset-library development: this is a personal offline editor.
- In-app image generation: starter art is generated externally; the intended small set uses 6–10 base variants per stamp category.
- P1 and P2 features: explicitly deferred in [REQUIREMENTS.md](REQUIREMENTS.md), including sparse 64K, pressure, richer formats, linked assets and scripting.
- Network-share/cloud-synced project storage and a CPU software-renderer fallback: excluded from the P0 contract.

[Source: specification §§1–3,9–10](../docs/spec.md).

## Context

The approved ingestion covers six documents with 0 blockers, 0 competing-variant warnings and 5 documented auto-resolutions. There was no PRD and no pre-existing requirement ID set; the 50 stable v1 IDs are derived from the working SPEC functional table and technical contracts. The working specification governs scope, the engine decision resolves the stack, architecture defines boundaries, and the spike report records fixture-specific historical evidence. The unchanged reference snapshot preserves provenance and cannot restore superseded targets. [Sources: synthesis](intel/SYNTHESIS.md), [document guide](../docs/README.md), [conflict resolutions](INGEST-CONFLICTS.md).

After ingestion, the user amended P0 on 2026-09-22 to exactly two fixed terrain roles: Background below Foreground, with one Foreground land/coastline mask and all mixed object layers (stamps, paths and text) above them. There are no independently masked extra terrain layers in P0. Earlier four-terrain-layer acceptance wording is superseded by the two-layer workload; historic probe records are preserved. Numeric latency/resource/correctness gates and the five-phase/50-ID structure remain unchanged.

The inspected repository already has a Godot shell, import/manifest storage and GPU/export probes, plus engine-independent Domain, Application and Infrastructure projects with a custom executable contract-test runner. EditSession commits through SQLite before publishing a snapshot and render invalidation. However, the shell still imports a preview through spike services, brush dabs go directly to GPU state, and export uses a fixture rather than the live imported edit session. These are disconnected paths, not a build-from-zero project or a completed Phase 0. [Sources: architecture map](codebase/ARCHITECTURE.md), [concerns map](codebase/CONCERNS.md).

Current implementation pins are Godot.NET.Sdk 4.7.2, C# 12/.NET 8, SDK 8.0.425 with latestPatch roll-forward, and Microsoft.Data.Sqlite 8.0.31. These inspected settings supersede the older specification's open questions about existing engine/.NET pins and SQLite binding; they do not prove production platform acceptance. The existing StreamingPngWriter is a probe implementation to integrate/qualify, not an instruction to reopen the stack. Path tessellation, full text fixtures and supported benchmark configurations still need implementation-level resolution. [Source: stack map](codebase/STACK.md).

Recorded Windows evidence includes terrain/JFA export and isolated commit/recovery probes. It does not establish correlated interaction latency for the connected Background/Foreground workflow, full semantic import, visible evicted undo, all dependency/ordering fixtures, physical tablet behaviour, Linux acceptance or percentage-of-installed-memory budgets. Tests and benchmarks were not rerun for this planning setup. [Sources: spike report](../docs/spike-report.md), [concerns map](codebase/CONCERNS.md).

Coastline follow-up (2026-09-22): Prefer one fixed outer-coast style with unstyled river/lake banks. If reliable separation is too involved, ship without generated edge styling; this fallback is authorized. Coastline style controls, decorative fades, wave rings and isolines are deferred beyond P0. Soft coverage, editable bank softness, colour/alpha and seam correctness remain required. Distance-field checks apply only where that path is used. This supersedes mandatory P0 coast stroke/fade/ring/isoline controls. Historical probe effects are evidence, not a requirement to ship their UI.

Phase 3 lake amendment (2026-09-22): provide both ordinary Land-tool subtraction to create a lake by revealing Background, and water texture or solid-colour painting on Foreground. Mask lakes edit coverage; painted lakes edit colour without changing coverage. Dedicated closed-spline lake entities/modifiers are deferred. River geometry and its post-mask modifier behavior remain unchanged. This supersedes earlier P0 spline-lake wording, including the lake portion of Phase 1 D-08.

Phase 3 discussion completed (2026-09-22): mixed object layers contain stamps, paths and text; every map starts with one and retains at least one. Tools remember parameters independently and object tools keep the selected object layer, switching from terrain to the topmost object layer. Labels use side-panel editing and Straight/Curve/S-shape deflection controls; bundled fonts appear first. Notes use pins with side-panel content. Side-by-side reconstruction review happens only at initial import; later recovery reopens the latest durable save. Post-import base rebuilding is deferred. See [Phase 3 context](phases/03-water-paths-and-labels/03-CONTEXT.md) for the settled decisions.

Phase 4 decisions (2026-09-23): fixed 1,000-unit longest edge, initial grid counts separate from unit scale, fixed-at-creation 1K/2K/3K/4K editing, independent 1K/2K/3K/4K/8K/16K PNG export, optional 300 DPI, canvas-derived inclusion toggles and dated filenames. Appearance is a toolbar panel; Grid is topmost and initially hidden. ZIP exports current editable state without undo history while the working project stays open. Storage uses a selected history cutoff; JSON is manual and includes hidden entities/notes with visibility. Phase 1.1 owns normalization/compatibility; Phase 4 owns finishing/export selectors. See [Phase 4 context](phases/04-project-finishing-and-safe-publication/04-CONTEXT.md).

## Constraints

- **Architecture:** Godot Control-node UI and C# controllers; engine-independent domain/application state, commands, history and import; SQLite/blob infrastructure and RenderingDevice/GLSL adapters behind ports. One ordered writer, immutable revisions and bounded workers keep authority separate from UI/GPU state. Shipping code must not depend on Spike. [Architecture §§2–6](../docs/architecture.md).
- **Durability:** Persist hashed blobs and a SQLite WAL synchronous=FULL transaction before acknowledging edits or dependent GPU work. Fresh-process recovery must survive native termination without post-loss saving or GPU readback; only an active unacknowledged gesture may be discarded. [Specification §§8,10](../docs/spec.md), [engine decision](../docs/engine-decision.md).
- **Import:** One-way recovery never changes the .ink. Preserve original bytes, dimensions/transforms, source identity and unsupported properties; report partial/visual fallback honestly and never replay imported history over a baked base. [Specification §§4,11](../docs/spec.md).
- **Rendering/export:** Shared revision-based compositor, explicit effect bounds, bounded residency and streaming PNG. Export freezes a revision, pins blobs and validates a flushed temporary sibling before atomic replacement. Cancellation/failure preserves the previous destination. Duration has no threshold. [Specification §§5–7,13](../docs/spec.md).
- **Acceptance:** At 1920 × 1080, input-to-visible p95/p99 ≤50/100 ms; frame intervals ≤20/33.3 ms; recent undo of ≤16 resident tiles p95 ≤100 ms. All four scenarios run ≥60 seconds under warm/cold/evicted caches with sequence-correlated evidence. Resource and seam limits remain exact in REQUIREMENTS.md; the initial memory envelope must be checked against recorded hardware. [Specification §§2,13](../docs/spec.md).
- **Platform:** Windows-first development and Linux acceptance with a required RenderingDevice backend; physical tablets act as pointers in P0. File inspection/recovery remains usable when GPU editing is unsupported. [Specification §§2–3,12–13](../docs/spec.md).

## Key Decisions

<decisions>

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use Godot 4 .NET for the first implementation; no parallel Rust + wgpu stack | Final engine ADR selects Godot; failed targets require attribution, and only an uncontainable demonstrated engine constraint warrants reconsideration | Settled for first implementation; acceptance still open |
| Preserve accepted inward dependency boundaries | Domain rules cannot depend on Godot nodes/vectors, SQL, filesystem, JSON, threading or GPU types; adapters remain replaceable | Settled architecture contract |
| Make SQLite revisions and immutable content-hashed blobs authoritative | Cached pixels, previews, manifests and integration JSON must be reconstructible and revision-labelled | Settled persistence contract |
| Persist before acknowledgement; recover in a fresh process after device loss | Recorded Windows/AMD TDR can terminate native code, so post-loss saving/recreation is unreliable | Settled recovery contract |
| Keep full P0 scope; use a connected terrain slice first | The first useful milestone must join the existing pieces without removing lakes, curved text or other P0 capabilities | Settled delivery scope |
| Use Background and Foreground as the only P0 terrain layers | User requested a simpler starting model, with the land/coastline mask on Foreground and objects above both | Settled 2026-09-22; supersedes arbitrary brush/raster layers and the four-terrain-layer workload |
| Prioritise bounded correct export over speed | Interactive editing and recoverability matter more than elapsed export duration | No export-duration pass/fail gate |
| Treat installed version pins as the existing baseline | Godot 4.7.2, SDK 8.0.425 and SQLite binding 8.0.31 are present in inspected project files | Recorded implementation baseline |

</decisions>

Sources: [engine decision](../docs/engine-decision.md), [architecture](../docs/architecture.md), [working specification](../docs/spec.md), [stack map](codebase/STACK.md). Intel's classifier label `proposed` reflects its literal `Accepted` matching rule; it does not reopen the final engine decision or accepted architecture.

## Evolution

After each phase, move only verified product outcomes to Validated, update constraints/decisions with evidence and keep requirement traceability aligned. GSD starts at Phase 1 for remaining work; source Phase 0 and priority P0 are different concepts. No phase is complete and no phase plans exist at initialization.

---
*Last updated: 2026-09-22 after approved document ingestion and planning setup.*
