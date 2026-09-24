# Phase 1: Connected Imported Terrain - Context

**Gathered:** 2026-09-22
**Status:** Ready for planning; visual design and implementation remain pending

<domain>
## Phase Boundary

Connect the real imported map, Background/Foreground texture painting, Foreground land-mask and river editing, persistent undo/redo, durable save/reopen, viewport and bounded PNG export through one authoritative document. Preserve the accepted Godot/C# architecture and correctness, durability, interaction and resource contracts. This discussion amends the earlier arbitrary terrain-layer model, coastline styling scope and brush behavior; it does not declare any feature implemented or benchmark passed.

Phase 1 owns DOC-01, DOC-02, DOC-03, IMPT-01, IMPT-02, IMPT-03, LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01, HIST-01, HIST-02, HIST-04, EXPT-01, UIIN-01, DURA-01, DURA-02 and REND-01 through REND-05. Assets/stamps, lakes/paths/text, finishing/publication and full platform acceptance remain in Phases 2–5.

The phase's DESIGN-SPEC.md is a design-agent input brief, not a separately approved GSD requirements SPEC or visual design. This context records the user's settled choices and takes precedence over any open-choice wording in that brief.
</domain>

<decisions>
## Implementation Decisions

### Import and opening
- **D-01:** Show a quick recovery review with the original preview and a short recovered/missing summary before opening the editor.
- **D-02:** Offer editable recovered terrain or original flattened appearance on every import, with neither preselected. Preserve the original preview for comparison. Explain that flattened coasts are baked pixels, not editable recovered geometry.
- **D-03:** New imports use a configured projects folder, with an option to override the location.
- **D-04:** Launch to a recent-projects screen with Open and Import, rather than automatically reopening the last map.
- **D-05:** Preserve original source bytes, raster hashes, dimensions/transforms and unsupported metadata. Map trusted source terrain to the two roles; report unmappable extras as partial recovery and offer the visual fallback. The documented visual fallback uses the locked preview as Background, with initially empty Foreground for new terrain editing; it does not create a third terrain layer.

### Terrain model and targeting
- **D-06:** Exactly two terrain roles in P0: Background below Foreground. Their roles/order are fixed; no terrain add, duplicate, remove or reorder operation. Keep visibility, lock, solo, opacity and name/role identity. This replaces the four-terrain-layer workload, not its numeric interaction/resource limits.
- **D-07:** Foreground owns the single land/coastline mask. Background has no separate editable land mask. Objects, paths and text added later sit above both terrain layers; their layers remain reorderable without an arbitrary count cap.
- **D-08:** Texture painting changes colour on the chosen terrain target; Land changes Foreground coverage. River/lake modifiers subtract from that coverage after mask painting to reveal Background and remain editable without creating additional terrain/mask layers.
- **D-09:** Selecting Land-mask or river tools automatically selects Foreground's mask itself, visibly highlighted. Returning to Texture Brush restores its previous Background/Foreground texture-painting target.
- **D-10:** A locked target blocks painting and offers an inline **Unlock** action; it is not automatically unlocked. A hidden target blocks painting and offers **Show layer**; it is not automatically revealed. Do not reroute the gesture to another layer.

### Everyday tool controls
- **D-11:** One Texture Brush with a Background/Foreground selector. The current texture and all brush settings carry across terrain target changes; no separate remembered per-layer settings.
- **D-12:** Brush size is its diameter in map/document pixels. Zoom changes the displayed footprint, not the document-space size. An internal radius representation is compatible if the UI converts explicitly.
- **D-13:** Space + drag or middle-mouse drag temporarily pans, returning to painting on release. Mouse-wheel zoom stays centred on the pointer. Text-field focus and interrupted pointer capture must respect the gesture-cancellation contract.

### Brush shapes and presets
- **D-14:** Land has two modes: **Edged polygon** and **Round soft**. Both support Add/Subtract and diameter. Edged has Roughness and Smooth; round has Softness. Do not copy the rich texture-tip library into Land or introduce a square mask mode from an earlier rejected suggestion.
- **D-15:** **Smooth rounds sharp corners of the edged footprint while retaining a firm, irregular outline.** It is neither alpha softness nor hand-movement stabilization. The reference describes a painting footprint, not a click-to-place polygon geometry tool.
- **D-16:** Texture painting has a brush preset library with tip icons and stroke previews, using the supplied hard/soft round, tapered, square/pencil and irregular/textured examples as its design reference. It also supports an edged variant. The exact inventory/assets and numerical defaults/ranges remain design/research details; do not claim exact replication of another product's algorithms.
- **D-17:** Tapered texture presets automatically narrow at both stroke ends with mouse input. Pressure support is not required. Non-tapered presets remain available; this does not apply automatic taper to every Land stroke.
- **D-18:** Distinguish footprint size/roughness, texture scale, texture rotation, opacity and the existing hardness/flow/spacing/jitter controls. Screenshot values such as size 100 or roughness 8 are examples, not selected defaults. Persist resolved shape/preset, roughness/smoothing, taper and randomness parameters needed to reproduce committed strokes; the existing deterministic-history contract still applies.

### Coastline styling amendment
- **D-19:** Prefer one fixed outer-coast style; rivers and lakes should have no decorative bank styling. Do not ship coastline style controls, decorative fades, wave rings or isolines in the first implementation.
- **D-20:** The user explicitly authorizes dropping generated styling entirely if reliably distinguishing coast from inland water is too involved. Do not add manual classification tools or extra mask layers to avoid this fallback. Record the implemented branch and reason during verification.
- **D-21:** Soft-mask editing and bank softness remain. Existing baked styling in an imported preview is preserved, not promised removable. River mouths, lake edges, ambiguous imported coverage and tile crossings must be considered before claiming reliable separation. Sea connectivity alone cannot classify a river bank.
- **D-22:** REND-02 is colour/alpha correctness and REND-03 is seamless/reference correctness, not a set of UI controls. Keep these guarantees with either styling branch. A distance-field path actually used must meet the amended contour/distance contract; unused distance/style checks are not applicable, not reported as passed.

### Save, history and interruptions
- **D-23:** Undo/Redo buttons stay visible; the action list lives in a collapsible history panel.
- **D-24:** Escape during a brush stroke or property drag cancels the entire uncommitted gesture, restores its previous state and adds no undo step. A completed gesture remains one undo step.
- **D-25:** Older-history reconstruction shows progress and Cancel. Pan/zoom remain available for inspection while editing is temporarily blocked; completion/cancellation leaves a coherent document/view.
- **D-26:** After a crash, open the recovered map with a brief recovery banner explaining that saved edits were restored and an unfinished gesture may be missing, with details available. Do not demand acknowledgement of a blocking recovery dialog or invent exact loss information that is unknown.
- **D-27:** Inherited contracts remain settled: persist completed edits before acknowledgement, manual Save drains pending commits, history/cursor survives restart, a new edit after undo invalidates redo, and a frozen export can run while later editing continues. Cancellation/failure preserves the previous export destination. No post-crash GPU readback or post-loss saving is required.

### Editing-scope comprehension
- **D-28:** Foreground coverage/mask strength, texture colour/intensity and whole-layer opacity can all make a layer appear weaker, so the editor must distinguish which of those three effects the user is viewing or changing without relying on the removed panel legend or on colour alone. The distinction must be available on the canvas or in its immediate editing state, not hidden behind another panel. The incoming visual designs may choose the exact HUD, labels, glyphs, hatching, badges and spatial feedback; those details are not locked until the designs are reconciled, provided the resulting treatment preserves this semantic distinction and remains testable at supported UI scales.

### Design and engineering discretion
No blanket "you decide" answer was given. The user previously commissioned design-agent briefs, so visual tokens, exact layout, icons, component dimensions and unspecified defaults may be proposed within the decisions above. Exact preset inventory, brush assets, roughness range, spacing behavior and taper curve still need design/research choices; custom tip import and pressure have not been approved. Do not reopen settled choices or describe proposals as user-approved values. The explicit engineering discretion is D-20's unstyled fallback.
</decisions>

<canonical_refs>
## Canonical References

Read these before planning or implementing; paths are repository-relative.

### Current requirements and architecture
- `docs/spec.md` §§1,3–8,10–13 — working scope, both user amendments, brushes and exact contracts.
- `docs/architecture.md` — dependency boundaries, authoritative snapshots, commit protocol and optional style path.
- `docs/engine-decision.md` — settled Godot choice and fresh-process recovery.
- `.planning/PROJECT.md` — context, inherited decisions and current scope.
- `.planning/REQUIREMENTS.md` — stable IDs, current MASK-01/REND-02/REND-03, traceability and conditional acceptance.
- `.planning/ROADMAP.md` — five-phase ownership and success criteria.
- `docs/README.md` — precedence and provenance; earlier ingestion/probe wording does not override later user amendments.
- `docs/reference/map-editor-spec-final.md` — unchanged original provenance, not authority over subsequent changes.
- `.planning/phases/01-connected-imported-terrain/01-DESIGN-SPEC.md` — required design surfaces and reference links; visual proposals remain unapproved.

### User-supplied visual references
- `.planning/phases/01-connected-imported-terrain/references/brush-preset-reference.png` — texture-preset library vocabulary and tip/stroke previews.
- `.planning/phases/01-connected-imported-terrain/references/land-edged-brush-reference.png` — Land Add/Subtract, diameter, Roughness and Smooth.
- `.planning/phases/01-connected-imported-terrain/references/land-round-brush-reference.png` — Land Add/Subtract, diameter and Softness.
- `.planning/phases/01-connected-imported-terrain/references/texture-edged-brush-reference.png` — edged texture painting and distinct texture controls.

### Existing implementation and evidence
- `.planning/codebase/STACK.md`, `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/CONVENTIONS.md` — inspected stack, integration gaps and conventions; pre-amendment product targets are historical.
- `docs/spike-report.md` — fixture-specific evidence, not completed editor acceptance.
- `Scripts/App/Main.cs`, `Scripts/App/MapCanvas.cs` — current shell and direct GPU brush path.
- `Scripts/Core/InkImportService.cs`, `Scripts/Core/ProjectStore.cs` — existing recovery and manifest/blob prototype services.
- `src/Mapwright.Domain/MapModel.cs` — existing terrain, brush and river records; they still need the amended model.
- `src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` — durable edit/session and repository contracts to connect.
- `Shaders/terrain_coverage.glsl`, `Shaders/terrain_sdf_seed.glsl`, `Shaders/terrain_sdf_color.glsl` — prototype combines coverage and styles all boundaries; not proof of coast-only styling.
</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- The shell, importer and source-preservation code offer starting points, but the canvas currently shows the imported preview and direct GPU brush results separately from the authoritative application session.
- EditSession and SQLite repository demonstrate commit-before-publish ordering. Their domain/application ports are the integration boundary, not permission to bypass storage for responsive painting.
- Existing GPU and streaming export probes offer fixtures to qualify; they do not export the live connected editor document or implement the final brush library.

### Established Patterns
- Immutable revisions, a single writer, engine-independent document types, retained source blobs and reconstructible caches remain mandatory.
- Parameterized seeded strokes must reopen/replay consistently. Shape, smoothing and taper additions need resolved persisted data, not mutable preset names alone.
- Separate water geometry offers a possible way to identify inland banks. Current merged-mask shader output does not prove the distinction or its cost.

### Integration Points
- Connect UI/gesture commands to EditSession, repository, renderer invalidation and one shared viewport/export graph.
- Enforce fixed terrain roles and lock/visibility targeting in commands as well as the UI; preserve unsupported import data when mapping source roles.
- Keep transient brush/property previews cancelable; durable commands become the only acknowledged authoritative edits.
</code_context>

<specifics>
## Specific Ideas

The user wants the simple Background/Foreground relationship and Land-tool distinctions shown in their screenshots. They explicitly rejected sharing the textured preset library with Land. The images establish behavior and control vocabulary, not a pixel-perfect UI clone or an instruction to implement every visible unrelated toolbar icon. Automatic taper is a mouse behavior, not a pressure requirement.
</specifics>

<deferred>
## Deferred Ideas

- Coastline style customization, decorative fades, wave rings and isolines are beyond this P0 roadmap. Fixed styling is also deferred if D-20's fallback is needed.
- Additional terrain layers, separately masked brush layers and independent mask-layer UI are outside the approved initial model.
- Pressure remains P1; custom image-tip import was offered but not selected and is not a P0 requirement.
- Asset management/stamps, lakes/paths/text, finishing/packaging and full Windows/Linux acceptance stay in their assigned later phases. The current task continues discussions 2–5, not implementation.
</deferred>

---
*Phase: 01-connected-imported-terrain*
*Context gathered: 2026-09-22*
