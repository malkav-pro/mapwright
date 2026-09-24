# Phase 1: Connected Imported Terrain - Research

**Researched:** 2026-09-22
**Domain:** Godot .NET terrain editing, structured .ink import, durable SQLite revisions, bounded GPU rendering/export
**Confidence:** HIGH for codebase seams; MEDIUM for production feasibility

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
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

### the agent's Discretion
### Design and engineering discretion
No blanket "you decide" answer was given. The user previously commissioned design-agent briefs, so visual tokens, exact layout, icons, component dimensions and unspecified defaults may be proposed within the decisions above. Exact preset inventory, brush assets, roughness range, spacing behavior and taper curve still need design/research choices; custom tip import and pressure have not been approved. Do not reopen settled choices or describe proposals as user-approved values. The explicit engineering discretion is D-20's unstyled fallback.

### Deferred Ideas (OUT OF SCOPE)
## Deferred Ideas

- Coastline style customization, decorative fades, wave rings and isolines are beyond this P0 roadmap. Fixed styling is also deferred if D-20's fallback is needed.
- Additional terrain layers, separately masked brush layers and independent mask-layer UI are outside the approved initial model.
- Pressure remains P1; custom image-tip import was offered but not selected and is not a P0 requirement.
- Asset management/stamps, lakes/paths/text, finishing/packaging and full Windows/Linux acceptance stay in their assigned later phases. The current task continues discussions 2–5, not implementation.
</user_constraints>
<phase_requirements>
## Phase Requirements

| IDs | Description (from REQUIREMENTS.md) | Research support |
|---|---|---|
| DOC-01, DOC-02, DOC-03 | One bounded map; save/reopen with truthful durable status; reconstruct after cache deletion | Immutable aggregate, import bridge, SQLite cursor/revision and cache rebuild plan below. [VERIFIED: .planning/REQUIREMENTS.md:26-29] |
| IMPT-01, IMPT-02, IMPT-03 | Locked-preview fallback, tested editable recovered bases, preserved source/provenance, bounded rejection | Existing streaming parser and staged blob copy are starting evidence, with missing caps and transform mapping identified below. [VERIFIED: .planning/REQUIREMENTS.md:35-37] |
| LAYR-01, TERR-01, TERR-02, MASK-01, WATR-01 | Fixed two-role terrain; scoped texture, land and river edits with deterministic strokes and honest cursor | Domain remodel, separate ordered coverage/colour passes, gesture controller and renderer fixtures below. [VERIFIED: .planning/REQUIREMENTS.md:44-59] |
| HIST-01, HIST-02, HIST-04 | Durable cursor and redo invalidation; recent deltas; cancellable older replay; resolved parameter compatibility | SQLite history schema and reconstruction job below. [VERIFIED: .planning/REQUIREMENTS.md:90-93] |
| EXPT-01, UIIN-01 | Frozen-revision bounded PNG publication and responsive mouse/panel UI | Shared render graph, streaming writer adaptation and native Godot shell below. [VERIFIED: .planning/REQUIREMENTS.md:118-123] |
| DURA-01, DURA-02 | Publish blobs and transactions before acknowledgement; fresh-process recovery | Existing application ordering plus missing blob durability and child-process gates below. [VERIFIED: .planning/REQUIREMENTS.md:133-136] |
| REND-01, REND-02, REND-03, REND-04, REND-05 | One revision/graph, colour/alpha and seam correctness, correlated interaction latency, bounded memory | Render scheduler, reference renderer, benchmark and resource ledger below. [VERIFIED: .planning/REQUIREMENTS.md:140-146] |
</phase_requirements>

## Summary

The phase is a large integration and model replacement. `Main.cs` currently imports and displays a preview, and `MapCanvas.cs` paints directly into a probe GPU texture; neither uses `EditSession`. The separate domain/application/SQLite path already demonstrates commit ordering, while the export path renders a synthetic fixture. Treat those as replaceable evidence, then connect all user actions to one immutable revision and one render dependency graph. [VERIFIED: Scripts/App/Main.cs:312-352] [VERIFIED: Scripts/App/MapCanvas.cs:187-209] [VERIFIED: src/Mapwright.Application/EditSession.cs:25-45] [VERIFIED: Scripts/Export/TerrainPipelineExportProbe.cs:39-83]

The current domain's `MapProject` holds `ImmutableArray<TerrainLayer>` and one `River`; its stroke has one `ResolvedBrush` without texture-versus-mask distinction or per-point river widths. The test fixture creates `Enumerable.Range(0, 4)` terrain layers. The planner must remodel the aggregate and tests before UI integration. [VERIFIED: src/Mapwright.Domain/MapModel.cs:35-145] [VERIFIED: tests/Mapwright.ContractTests/Program.cs:135-151] Current `SqliteProjectRepository` persists command JSON and full snapshots but has no history cursor/branch tables or durable blob references. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:126-160]

**Primary recommendation:** Build the smallest real import→commit→render→reopen→export vertical path first, then expand brushes, history reconstruction, UI, and measured acceptance in waves. Keep the authorized unstyled-coast branch available until a real coast/river/mouth fixture proves reliable separation. [VERIFIED: docs/architecture.md:100-125] [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:56-67]

## Architectural Responsibility Map

| Capability | Primary tier | Secondary tier | Rationale |
|---|---|---|---|
| Import and source verification | Infrastructure | Application | Stream/validate source and publish immutable blobs; application chooses mode and creates a revision. [VERIFIED: docs/architecture.md:18-31] |
| Two terrain roles and command semantics | Domain | Application | Fixed roles, masks, targets, invalidation and replay remain engine independent. [VERIFIED: docs/architecture.md:31-51] |
| Gesture preview and immediate scope cues | Godot UI | Application | Input and transient preview are UI-owned; release sends one command. [VERIFIED: docs/architecture.md:53-67] |
| Durable history/save | Infrastructure | Application | SQLite owns atomic storage; session controls ordered acknowledgement and cursor transitions. [VERIFIED: docs/architecture.md:53-79] |
| Tiles and shared viewport/export graph | Rendering adapter | Domain | Adapter owns GPU/cache; domain specifies geometry and invalidation. [VERIFIED: docs/architecture.md:81-107] |
| PNG publication | Infrastructure | Rendering adapter | Bounded row writing and atomic destination update consume a frozen render snapshot. [VERIFIED: docs/architecture.md:107-123] |

## Project Constraints (from AGENTS.md)

No `AGENTS.md` exists in the working directory as inspected on 2026-09-22. [VERIFIED: filesystem check this session] `.planning/config.json` is also absent; Nyquist validation and security enforcement therefore default to enabled under this research workflow. [VERIFIED: filesystem check this session]

## Standard Stack

| Component | Pinned version / source | Phase use |
|---|---|---|
| Godot .NET SDK | `Godot.NET.Sdk/4.7.2` [VERIFIED: Mapwright.csproj:1] | Native `Control` UI, global RenderingDevice viewport adapter and optional local export device. The Godot global device drives screen drawing; RenderingDevice is unavailable in headless or Compatibility rendering. [CITED: https://docs.godotengine.org/en/stable/classes/class_renderingdevice.html] |
| .NET / C# | `net8.0`, C# `12` [VERIFIED: Mapwright.csproj:3-9] | Domain, application, import and storage. Portable `8.0.425` SDK is present locally. [VERIFIED: local version probe 2026-09-22] |
| Microsoft.Data.Sqlite | `8.0.31` [VERIFIED: src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj:9] | Existing SQLite repository, transaction/history expansion. Transactions atomically group statements; SQLite permits one writer at a time. [CITED: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions] |
| Built-in PNG implementation | `StreamingPngWriter`, `PngValidator` [VERIFIED: Scripts/Export/StreamingPngWriter.cs:7-52] [VERIFIED: Scripts/Export/PngValidator.cs:14-30] | Adapt the existing row writer/validator to frozen document export after proving colour and publication semantics. |
| Existing contract runner | `tests/Mapwright.ContractTests` console executable [VERIFIED: tests/Mapwright.ContractTests/Program.cs:1-27] | Extend with meaningful domain, repository and application contracts. |

No new external package is recommended or installed in this phase; no package legitimacy audit is triggered. The existing pin is a project constraint, not a claim that it is the latest registry release. [VERIFIED: Mapwright.csproj:1-20] [VERIFIED: src/Mapwright.Infrastructure/Mapwright.Infrastructure.csproj:1-13]

## Architecture Patterns

### System Architecture Diagram

```text
.ink / project folder
  → bounded import + hash/preview/report
  → choose editable bases OR locked-preview Background
  → source/blob publication → SQLite initial revision
  → immutable DocumentSnapshot
      ├→ Godot gesture preview → validated command → ordered durable commit
      │   → published snapshot → tile invalidation → viewport tiles
      ├→ history cursor/reconstruction → published snapshot → viewport tiles
      └→ frozen revision + pinned assets → same render graph → bounded PNG rows
          → flush + validate → atomic destination replace
```

The render graph must evaluate Background colour, Foreground colour, Foreground base coverage, ordered mask strokes, then river subtraction before final compositing. Optional fixed outer-coast styling follows only if bank identity survives river mouths/import ambiguity; otherwise omit generated style. [VERIFIED: docs/architecture.md:90-107] [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:56-67]

### Recommended Project Structure and integration seams

| Work package | Existing seam | Required outcome |
|---|---|---|
| Domain model/commands | `src/Mapwright.Domain/MapModel.cs`, `Commands.cs`, `Geometry.cs` | Replace unconstrained terrain array/one global river with fixed roles, separate texture/coverage histories, river width profile/softness, stable ids and explicit commands. Quote current values: `ImmutableArray<TerrainLayer> TerrainLayers`, `River River`, `double Width`. [VERIFIED: src/Mapwright.Domain/MapModel.cs:105-145] |
| Application orchestration | `src/Mapwright.Application/EditSession.cs`, `Ports.cs` | Add open/import/save/history/export use cases and immutable snapshot capture; preserve commit→publish→enqueue order. [VERIFIED: src/Mapwright.Application/EditSession.cs:25-45] |
| Authoritative store | `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` | Evolve schema/versioning, cursor/redo, blob references, checkpoints, source verification and recovery; avoid a second authoritative manifest. Current table names are `schema_info`, `revisions`, `commands`, `current_project`. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:126-160] |
| Import bridge | `Scripts/Core/InkImportService.cs`, `MapDocument.cs`, `ProjectStore.cs` | Reuse parser evidence behind infrastructure port; map tested source rasters to exactly two roles, preserve extra metadata/source, report partial result and publish only after mode choice. [VERIFIED: Scripts/Core/InkImportService.cs:13-61] [VERIFIED: Scripts/Core/ProjectStore.cs:14-82] |
| Godot shell | `Scripts/App/Main.cs`, `MapCanvas.cs`, `Scenes/Main.tscn` | Replace evidence harness UI with approved `01-UI-SPEC.md` native controls; bind tool controllers to application commands and status. [VERIFIED: Scripts/App/Main.cs:237-352] |
| Renderer | `Scripts/Rendering/GlobalGpuBrushSurface.cs`, `GpuJfaTerrainRenderer.cs`, `Shaders/*` | Keep probe math as fixtures; implement revision-keyed, bounded scheduler and shared viewport/export graph. Global device can display textures directly; local device export must use bounded staging. [VERIFIED: Scripts/App/MapCanvas.cs:170-186] [CITED: https://docs.godotengine.org/en/stable/classes/class_renderingdevice.html] |
| Export | `Scripts/Export/StreamingPngWriter.cs`, `PngValidator.cs`, `TerrainPipelineExportProbe.cs` | Turn synthetic fixture export into cancelable frozen-revision export with progress and previous-destination preservation. [VERIFIED: Scripts/Export/TerrainPipelineExportProbe.cs:21-99] |

### Key implementation rules

1. Commit resolved stroke recipes, including document-space samples, exact tip/shape, roughness/corner smoothing or softness, taper curve/version, texture identity and transforms, opacity/flow/spacing/jitter, seed and algorithm version. A preset name alone is mutable. Current `ResolvedBrush` only quotes `TextureHash, Radius, Hardness, Opacity, Flow, Spacing, Rotation, Seed, AlgorithmVersion`; a migration is required. [VERIFIED: src/Mapwright.Domain/MapModel.cs:35-57] [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:49-54]
2. Treat brush size as displayed diameter in document pixels; convert to radius internally once. Use double document positions and tile-relative shader coordinates. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:39-54] [VERIFIED: docs/spec.md:124-139]
3. Make each gesture a transient preview state machine: pointer-down→samples/provisional pixels→release→one command; Escape/capture loss/window deactivation/modal→discard preview and restore base snapshot. Pan/zoom bypasses edit creation; focused fields own typing/undo. Godot sends Control input through `_gui_input`; route shortcuts by focus scope. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-UI-SPEC.md:310-346] [CITED: https://docs.godotengine.org/en/stable/tutorials/inputs/inputevent.html]
4. Enforce lock, visibility, role order and Foreground-only mask/river ownership in domain validation as well as the UI. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:33-40]
5. Use a revision-aware tile key and forward invalidation/backward dependency bounds. Seed no randomness from tile origin; texture anchoring is document-space. [VERIFIED: docs/spec.md:161-176]
6. Export captures revision and referenced blobs before work starts; jobs run below interactive priority, render with the same graph, stream cropped bands, flush/validate a sibling temporary file, then replace destination. [VERIFIED: docs/spec.md:176-186] The current synthetic exporter already follows a temporary sibling and `File.Move(temporary, destination, overwrite: true)` path, but lacks cancellation and connected snapshots. [VERIFIED: Scripts/Export/TerrainPipelineExportProbe.cs:32-99]

### Coast feasibility branch

Plan a bounded spike early on the real import plus fixtures: coastline adjoining open sea, inland river, lake-like cut, mouth touching coast, soft imported coverage and tile crossings. Compare region classifications/appearance against a whole-image reference at export scale. If coast-only decoration cannot be established reliably within tile budgets, record D-20's unstyled branch and remove all style/distance work from product rendering. Retain soft coverage, bank softness, colour/alpha, seams and all resource/interaction gates. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:56-67] Existing `terrain_coverage.glsl` computes `clamp(island - river, 0.0, 1.0)` before `terrain_sdf_color.glsl` applies shore/ring effects, so that shader cannot prove a coast-only result. [VERIFIED: Shaders/terrain_coverage.glsl:28-32] [VERIFIED: Shaders/terrain_sdf_color.glsl:48-67]

## Don't Hand-Roll

| Problem | Use |
|---|---|
| Atomic command/history state | SQLite transactions through existing `Microsoft.Data.Sqlite`; keep WAL `synchronous=FULL` and one writer. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:69-123] [CITED: https://www.sqlite.org/wal.html] |
| PNG framing, CRC and validation | Extend existing `StreamingPngWriter` and `PngValidator`; do not invent a second encoder. [VERIFIED: Scripts/Export/StreamingPngWriter.cs:7-52] [VERIFIED: Scripts/Export/PngValidator.cs:14-30] |
| UI controls, layout, focus and keyboard navigation | Native Godot `Control`, `HSplitContainer`, `ScrollContainer`, shared theme/components from UI spec. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-UI-SPEC.md:69-94] |
| Image identity | SHA-256 content addressing and original bytes, with derived decodes disposable. [VERIFIED: Scripts/Core/ProjectStore.cs:105-137] |

## Common Pitfalls

| Failure | Prevention and warning sign |
|---|---|
| Preview/GPU state masquerades as saved state | Render only committed snapshots for export and acknowledged UI; transient preview labelled separately. Warning: export differs from visible committed revision. [VERIFIED: Scripts/App/MapCanvas.cs:187-209] |
| Four-layer fixture drives model | Rewrite fixture to two fixed roles, then test command rejection for prohibited role/order/target actions. Warning: `Enumerable.Range(0, 4)` remains in tests. [VERIFIED: tests/Mapwright.ContractTests/Program.cs:135-151] |
| Soft mask confused with opacity/texture | Keep D-28 spatial feedback and separate command types; test visual and semantic states at 100%/150% scale. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-UI-SPEC.md:224-246] |
| Unbounded import despite streaming tokenizer | `Fill` doubles buffer for one token and `DecodePng` rents based on encoded length; validate compressed/decompressed/token/pixel/aggregate limits before allocation. Quote current `MaxDepth = 256` only as current behavior, not sufficient protection. [VERIFIED: Scripts/Core/InkImportService.cs:296-318] [VERIFIED: Scripts/Core/InkImportService.cs:339-418] |
| Full snapshots/history growth | Current repository writes `snapshot_json` every revision; measure and plan checkpoints/replay/retention without using the disposable 4 GiB cache as a history cap. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:135-160] |
| Misleading latency metric | Current `FramePostDraw` drains all pending timestamps without checking which revision was drawn; carry sequence/revision IDs to the tile actually presented, then report endpoint and exclusion. [VERIFIED: Scripts/App/MapCanvas.cs:201-205] |
| Mistaking probe seams for final correctness | The probe oracle is discrete opposite-class pixel centres; any used style path requires a 0.5 contour reference and output-pixel tolerance. If unstyled, mark only distance/style tests not applicable. [VERIFIED: Scripts/Rendering/GpuSeamProbe.cs:113-116] [VERIFIED: .planning/REQUIREMENTS.md:142-145] |
| GPU loss cleanup assumed to run | Fresh process is recovery boundary; store must already contain all acknowledged edits and old export destination. [VERIFIED: docs/engine-decision.md:19-43] |
| SQLite WAL/backup mishandled | WAL is persistent data; never copy an open `scene.sqlite` alone. Phase 4 packaging owns online backup, but Phase 1 recovery must keep WAL alongside live project. [CITED: https://www.sqlite.org/wal.html] [CITED: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup] |

## Code Examples

Current safe application seam (illustrative call sequence, not a new API):

```csharp
var change = command.Apply(previous);
var committed = await repository.CommitAsync(previous, command, change, cancellationToken);
Volatile.Write(ref _current, change.Project);
renderQueue.Enqueue(previous.ProjectId, committed.Revision, command.Id, change.Invalidation);
return new EditAcknowledgement(command.Id, committed.Revision, committed.CommittedAt,
    change.Invalidation);
```

All names/values in this skeleton are copied from the opened source; preserve the actual revision/id checks surrounding it. [VERIFIED: src/Mapwright.Application/EditSession.cs:31-45]

Current SQLite mode declaration:

```csharp
pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
```

The statement is present in `OpenAsync`; SQLite documents the WAL FULL sync behavior. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:110-123] [CITED: https://www.sqlite.org/wal.html]

## Runtime State Inventory

| Category | Items found | Action required |
|---|---|---|
| Stored data | Current prototype projects can be created under Godot `user://projects`; `SaveRecoveredProject` calls `ProjectStore.SaveImportedProject`. The production SQLite root is a separate store. [VERIFIED: Scripts/App/Main.cs:337-351] [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:17-34] | Define one-time opening/conversion for any prototype project directory; do not overwrite it. Determine whether user has such projects before migration. |
| Live service config | No external service is part of the offline accepted architecture. [VERIFIED: docs/architecture.md:14-31] | None for Phase 1. |
| OS-registered state | No OS registration is part of the current runtime entry in `project.godot`/scripts inspected. [VERIFIED: .planning/codebase/ARCHITECTURE.md:82-91] | None identified; do not rename unrelated OS state. |
| Secrets/env vars | `MAPWRIGHT_INK_FIXTURE` selects the probe fixture; `DOTNET_HOST_PATH` is used by crash worker. [VERIFIED: Scripts/App/Main.cs:20-22] [VERIFIED: Scripts/App/Main.cs:154-156] | Keep probe settings separate from product project choice. |
| Build artifacts | Existing `.tools` SDK, Godot binary and NuGet cache are local runtime dependencies. [VERIFIED: Scripts/run-contract-tests.ps1:1-17] [VERIFIED: Scripts/run-spike.ps1:1-21] | Rebuild shipping project after domain/schema changes; preserve probe artifacts for comparison. |

## Environment Availability

| Dependency | Needed for | Available | Version / evidence | Fallback |
|---|---|---|---|---|
| Portable .NET SDK | build/contracts | yes | `8.0.425` executable and version probe this session. [VERIFIED: local probe 2026-09-22] | — |
| Portable Godot .NET | UI/GPU probe | yes, file present | `Godot_v4.7.2-stable_mono_win64_console.exe` exact executable path comes from runner. [VERIFIED: Scripts/run-spike.ps1:7-12] [VERIFIED: local file probe 2026-09-22] | Unsupported backend shows inspection/recovery; no CPU editor promised. |
| Real .ink fixture | import mapping | yes, file present at the runner's configured path | Existing default fixture path is source-defined in `run-spike.ps1`; bytes were not re-imported in this research. [VERIFIED: Scripts/run-spike.ps1:1-12] [VERIFIED: local file probe 2026-09-22] | Synthetic malformed/oversized fixtures for bounds tests. |
| NuGet online vulnerability feed | package audit | unavailable during contract run | Restore emitted `NU1900` while all six contracts passed. [VERIFIED: contract run 2026-09-22] | Use pinned cached dependencies; separately run security audit when network is available. |

## Validation Architecture

The Nyquist setting is absent because `.planning/config.json` is absent, so validation is enabled under the workflow. [VERIFIED: filesystem check this session]

| Property | Value |
|---|---|
| Framework | Existing framework-free .NET 8 console contract runner; Godot runtime probe executables. [VERIFIED: tests/Mapwright.ContractTests/Program.cs:1-27] |
| Quick run | `./Scripts/run-contract-tests.ps1` — six existing contracts passed in this session, exit 0; NuGet vulnerability feed warned `NU1900`. [VERIFIED: Scripts/run-contract-tests.ps1:1-29] [VERIFIED: contract run 2026-09-22] |
| Full current probe | `./Scripts/run-spike.ps1 -InkFixture '<actual .ink path>'` — runs historical probes, not Phase 1 acceptance. [VERIFIED: Scripts/run-spike.ps1:1-42] |
| Phase 1 gate | Extend the contract runner and add connected Godot/child-process fixtures; run real import/edit/restart/export plus 60-second four-scenario hardware measurements. [VERIFIED: .planning/REQUIREMENTS.md:148-172] |

| Requirement group | Test type and required assertion | Existing? |
|---|---|---|
| DOC/IMPT | Integration: mode choice, two-role mapping, source hash/transforms, extra metadata, hostile size/token limits, cache deletion and reopen. | No connected test. [VERIFIED: Scripts/Core/InkImportService.cs:13-61] |
| LAYR/TERR/MASK/WATR/HIST-04 | Domain/property tests: forbidden targets/order, deterministic replay, stroke bounds, seeded tip/taper, modifier order and river old/new invalidation. | Partial only; current fixture has four layers. [VERIFIED: tests/Mapwright.ContractTests/Program.cs:1-74] |
| HIST/DURA | SQLite and child-process: commit/ack ordering, cursor/restart/redo invalidation, blob failure points, disk-full, older reconstruction cancellation. | Commit/reopen partial; no cursor schema. [VERIFIED: tests/Mapwright.ContractTests/Program.cs:75-133] [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:126-160] |
| REND-01/02/03/EXPT-01 | GPU/reference: soft alpha, linear-premultiplied compositing, tiled/whole ≤1 channel, continuity, frozen export while editing, validate/cancel/preserve destination. Conditional style contour/mouth check if style branch selected. | Synthetic seam/export probes only. [VERIFIED: Scripts/Rendering/GpuSeamProbe.cs:22-120] [VERIFIED: Scripts/Export/TerrainPipelineExportProbe.cs:21-99] |
| UIIN/REND-04/05 | Connected scenario: 1920×1080, 60 seconds each × warm/cold/evicted, sequence-correlated p50/p95/p99, queue/sample/stall/resource ledger; inspect D-28 at 100% and 150%. | No; present probe is 30 seconds and callback-based. [VERIFIED: Scripts/App/Main.cs:95-112] [VERIFIED: Scripts/App/MapCanvas.cs:201-224] |

**Sampling:** per domain/storage task run contract runner; per renderer/UI wave run connected fixtures; phase gate runs full contract suite, real import/restart/16K export and four correlated acceptance scenarios on recorded hardware. [VERIFIED: .planning/ROADMAP.md:43-51]

**Wave 0 gaps:** add fixed-two-role fixture and test registration; bounded import fixtures; deterministic brush reference; real SQLite history/blob interruption worker; shared tiled/whole and colour oracle; sequence-to-visible instrumentation. No extra test framework package is necessary. [VERIFIED: tests/Mapwright.ContractTests/Program.cs:1-27]

## Security Domain

This is an offline native editor with untrusted imported files and user-selected paths. OWASP ASVS is web-oriented; the applicable categories are adapted to file parsing and local publication, not used to invent account/session requirements. [VERIFIED: docs/spec.md:11-32] [CITED: https://devguide.owasp.org/en/06-verification/01-guides/03-asvs/]

| ASVS category | Applies | Phase control |
|---|---|---|
| V2 Authentication | no | No accounts in accepted scope. [VERIFIED: docs/spec.md:11-32] |
| V3 Session management | no | No network session. [VERIFIED: docs/spec.md:11-32] |
| V4 Access control | local filesystem | Validate chosen project/export paths and avoid overwriting the source/project unexpectedly. [VERIFIED: .planning/REQUIREMENTS.md:35-37] |
| V5 Validation, sanitization and encoding | yes | Bounded gzip/JSON/base64/PNG decode, dimensions, nesting, paths and checked arithmetic before allocation. [VERIFIED: Scripts/Core/InkImportService.cs:296-318] |
| V6 Stored cryptography | hash integrity only | Use standard SHA-256 APIs already in source; hash is identity/integrity, not secrecy. [VERIFIED: Scripts/Core/ProjectStore.cs:105-137] |

Threat tests: decompression/token/pixel bombs; malformed PNG dimensions; path traversal through import metadata/project names; symlink/junction destination surprises; stale/corrupt blob hashes; partial export publication; SQL input through persisted metadata. Use parameterized SQL as the current repository does. [VERIFIED: Scripts/Core/InkImportService.cs:13-61] [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:35-107]

## State of the Art and feasibility

The project has selected Godot 4 .NET and SQLite; no engine or storage comparison remains in scope. [VERIFIED: docs/engine-decision.md:1-17] The real uncertainty is whether the current probes can be converted into one bounded, responsive document graph. Current evidence does not prove import terrain mapping, colour/alpha fidelity, coast-only styling, live document export, evicted history, correlated input-to-visible latency or combined resource budgets. [VERIFIED: .planning/STATE.md:45-63] [VERIFIED: docs/spike-report.md:1-20] Product decisions for exact preset inventory, taper curve, numeric bounds, Background-underlay appearance, solo semantics, autosave cadence and export rounding remain open; do not lift sample mockup values. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-UI-SPEC.md:393-408]

## Assumptions Log

| # | Claim | Risk if wrong |
|---|---|---|
| A1 | [ASSUMED] A compact checkpoint plus command replay can meet older-history cancellation and 4 GiB acceleration budget on the real map. | May require different checkpoint density/index and explicit progress units. |
| A2 | [ASSUMED] Existing decoded source raster semantics can map at least one trustworthy Background/Foreground base without double painting. | Editable recovery may need to report more partial content and rely on visual mode. |
| A3 | [ASSUMED] A local GPU tile scheduler can meet all combined interaction/resource gates on the available device. | Requires bounded measurement and attribution; engine constraint cannot be inferred from probes. |
| A4 | [ASSUMED] Fixed outer-coast styling can be classified reliably after soft river subtraction. | Use D-20 unstyled fallback if feasibility fixture fails. |

## Execution Decision Gates (unresolved until measured)

Planning has assigned each uncertainty to an explicit, ordered evidence gate. The executor must record the selected option and supporting measurements in the named decision artifact before implementing the dependent behavior. Each artifact uses nonempty `Decision:` and `Evidence:` lines; Q3 uses `Solo Decision:`/`Solo Evidence:` and `Underlay Decision:`/`Underlay Evidence:`. These entries are not answers or claimed results. A gate without its evidence and recorded choice is unresolved and must fail the owning task's verification.

1. **Q1 → Plan 02, `01-Q1-DECISION.md`, before editable import.** Inspect real raster checkpoints, IDs/order, transforms, dimensions and preview correspondence. Choose verified role mapping per content item or report partial recovery and retain the flattened visual fallback; record tested/rejected mappings. [VERIFIED: Scripts/Core/InkImportService.cs:125-169]
2. **Q2 → Plan 03, `01-Q2-DECISION.md`, before persistent brush recipes.** Compare candidate bounds/defaults, preset inventory/assets and taper curves against references, renderer limits and deterministic replay tests. Record the chosen measured values and rejected candidates; no screenshot example becomes a default. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:43-54]
3. **Q3 → Plans 03 and 06, `01-Q3-DECISION.md`, before solo commands and empty-Background composition.** Record a tested solo truth table, then compare transparent and project-colour underlays in viewport/export pixel oracles and select a documented semantic. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-UI-SPEC.md:393-404]
4. **Q4 → Plan 05, `01-Q4-DECISION.md`, before checkpoint/replay policy.** Measure real-map replay, cache growth and cancellation for candidate intervals. Choose retained full snapshots only if within all budgets or periodic checkpoints plus command replay; record version compatibility and rebuild policy. [VERIFIED: src/Mapwright.Infrastructure/SqliteProjectRepository.cs:135-160]
5. **Q5 → Plan 06, `01-Q5-DECISION.md`, before generated coast style.** Test real-import and synthetic open-coast/inland-bank/mouth/tile fixtures, contour tolerance and budgets. Select proven fixed outer-coast treatment or D-20's unstyled fallback; record measurements and reason. [VERIFIED: .planning/phases/01-connected-imported-terrain/01-CONTEXT.md:56-67]

## Sources

### Primary
- Opened repo files: `01-CONTEXT.md`, `01-UI-SPEC.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `docs/spec.md`, `docs/architecture.md`, `docs/engine-decision.md`, domain/application/infrastructure/Godot/render/export source listed above. [VERIFIED: local reads 2026-09-22]
- [Godot RenderingDevice](https://docs.godotengine.org/en/stable/classes/class_renderingdevice.html) and [input dispatch](https://docs.godotengine.org/en/stable/tutorials/inputs/inputevent.html). [CITED: official Godot documentation]
- [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [SQLite WAL](https://www.sqlite.org/wal.html), [OWASP ASVS overview](https://devguide.owasp.org/en/06-verification/01-guides/03-asvs/). [CITED: official documentation]

## Metadata

**Confidence breakdown:** stack HIGH (opened pins and executable probe); architecture HIGH for current seams and MEDIUM for new connected design; pitfalls HIGH for observed gaps and MEDIUM for feasibility risks. [VERIFIED: local reads/probes 2026-09-22]

**Research date:** 2026-09-22  
**Valid until:** 2026-10-22 for codebase findings; recheck official API pages if toolchain changes.
