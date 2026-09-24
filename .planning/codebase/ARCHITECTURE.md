---
last_mapped_commit: 0ab28bc8145d0b387b23357022045e2d09516a7d
last_mapped_at: 2026-09-22
---
# Architecture

**Analysis Date:** 2026-09-22

## Pattern Overview

**Overall:** Godot desktop shell with one connected flattened-import tracer alongside retained historical probes and an inward-dependent application core.

**Key Characteristics:**

- `project.godot` launches `Scenes/Main.tscn`, which attaches `Scripts/App/Main.cs`; this is the actual runtime composition root.
- `Mapwright.csproj` references three `src/` projects. `Scripts/App/Main.cs` now bridges `InkImportService` output into `EditSession` and `SqliteProjectRepository` for flattened projects; older `ProjectStore` and GPU probes remain isolated evidence paths.
- `docs/engine-decision.md` selects Godot .NET and save-and-restart device recovery. `docs/spec.md` describes product requirements; `docs/architecture.md` defines accepted boundaries whose complete renderer, scheduler, history and UI integration are not implemented.
- Keep `Mapwright.Core` spike types separate from `Mapwright.Domain` types: both define editing concepts, but `Scripts/Core/EditingDocument.cs` is not the domain implementation in `src/Mapwright.Domain/`.

## Layers

**Godot UI and harness:**

- Location: `Scripts/App/Main.cs`, `Scripts/App/MapCanvas.cs`.
- Contains programmatic Control-node UI, import/save actions, viewport input and command-line probe routing.
- Depends directly on `Scripts/Core/`, `Scripts/Rendering/`, `Scripts/Export/` and the three inward-dependent `src/` projects; used by `Scenes/Main.tscn`.

**Spike services and rendering:**

- `Scripts/Core/InkImportService.cs` parses gzip JSON incrementally and recovers raster checkpoints, preview and metadata into `Scripts/Core/MapDocument.cs` types.
- `Scripts/Core/ProjectStore.cs` saves imported content into staged manifest/blob directories and verifies hashes when reopening.
- `Scripts/Rendering/GlobalGpuBrushSurface.cs` owns interactive GPU textures; `Scripts/Rendering/GpuJfaTerrainRenderer.cs` implements the terrain compute pipeline using `Shaders/` resources.
- `Scripts/Rendering/ConnectedTerrainGraph.cs` evaluates immutable connected revisions for both canvas pixels and row-streamed PNG publication.
- `Scripts/Export/TerrainPipelineExportProbe.cs` combines fixture coverage, GPU tile rendering, `Scripts/Export/StreamingPngWriter.cs` and `Scripts/Export/PngValidator.cs`. It does not export the live imported/edit-session document.

**Domain:**

- Location: `src/Mapwright.Domain/MapModel.cs`, `src/Mapwright.Domain/Geometry.cs`, `src/Mapwright.Domain/Commands.cs`.
- Immutable records, stable IDs, revision-checked paint/river commands and conservative invalidation; no Godot dependency in `src/Mapwright.Domain/Mapwright.Domain.csproj`.
- Used by the application and infrastructure projects. Put document rules here, using domain geometry rather than Godot vectors.

**Application:**

- `src/Mapwright.Application/EditSession.cs` serializes edits with a semaphore and owns the published immutable project snapshot.
- `src/Mapwright.Application/Ports.cs` defines repository, render-invalidation and clock boundaries. Rendering is a port, not a implemented tile scheduler in this project.
- Depends on the domain only via `src/Mapwright.Application/Mapwright.Application.csproj`.

**Infrastructure:**

- `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` implements `IProjectRepository`, storing command payload JSON, full revision snapshot JSON and schema-v2 imported-source references transactionally in `scene.sqlite`; immutable source/preview blobs are SHA-256 verified on create and reopen.
- `src/Mapwright.Infrastructure/SystemClock.cs` supplies wall-clock time. The project references Application and Domain, with no Godot reference.
- The repository implements the initial imported-source and preview blob path. Complete semantic import, cursor history and broader asset/blob services remain future work.

## Data Flow

**Connected flattened import/save:**

1. `Scripts/App/Main.cs` calls `InkImportService.Import` through `Task.Run`.
2. `Scripts/Core/InkImportService.cs` returns recovered metadata, decoded PNG bytes and a report.
3. Recovery review leaves both modes unselected; the current connected slice enables original flattened appearance, yielding locked Background below empty Foreground.
4. `SqliteProjectRepository.CreateImportedAsync` publishes verified source/preview blobs and revision zero before the project is acknowledged.
5. `Scripts/App/MapCanvas.cs` submits a Foreground texture stroke on pointer release through `EditSession`; only the committed snapshot is rendered by `ConnectedTerrainGraph`.
6. Save verifies the existing durable revision without incrementing it; reopen reloads the SQLite revision; export evaluates that same immutable revision and validates a temporary PNG before replacement.

**Application edit contract:**

1. A caller invokes `EditSession.ExecuteAsync` in `src/Mapwright.Application/EditSession.cs`; the contract tests exercise this path in `tests/Mapwright.ContractTests/Program.cs`.
2. `src/Mapwright.Domain/Commands.cs` checks the base revision and returns a new project plus `TileInvalidation`.
3. `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` appends command/snapshot data and advances the current revision in a WAL transaction with `synchronous=FULL`.
4. `src/Mapwright.Application/EditSession.cs` checks the durable acknowledgement, publishes `Current`, enqueues invalidation, then returns acknowledgement to its caller.

**GPU brush and export probes:**

- The historical interactive-brush probe still sends dabs directly to `GlobalGpuBrushSurface`; the connected editor path bypasses it and submits commands through `EditSession`.
- `Scripts/Export/TerrainPipelineExportProbe.cs` builds synthetic coverage per halo tile, renders bands, streams PNG rows, validates the temporary output, then replaces the destination.

**State Management:**

- `Scripts/App/Main.cs` retains the current import; `Scripts/App/MapCanvas.cs` owns mutable interaction/probe state and GPU surface handles.
- `src/Mapwright.Application/EditSession.cs` publishes immutable domain snapshots with `Volatile.Read/Write`, with one commit at a time per session.
- SQLite plus its content-addressed blob directory is authoritative for connected projects. `ProjectStore` manifests and the probe recovery journal remain legacy/probe mechanisms and are not written by the connected path.

## Key Abstractions

- **MapProject and DocumentChange:** immutable authoritative state and change/invalidation result in `src/Mapwright.Domain/MapModel.cs` and `src/Mapwright.Domain/Commands.cs`.
- **IEditCommand:** domain transformation with command ID and base revision in `src/Mapwright.Domain/Commands.cs`; use this for new application commands.
- **IProjectRepository / IRenderInvalidationQueue:** ports in `src/Mapwright.Application/Ports.cs`; keep SQL and GPU handles behind adapters.
- **InkImportResult:** recovered source/preview/raster transfer object in `Scripts/Core/MapDocument.cs`; belongs to the spike import path.
- **ConnectedTerrainGraph:** revision-keyed Godot rendering adapter used by canvas sampling and streaming PNG export.

## Entry Points

- `project.godot` → `Scenes/Main.tscn` → `Scripts/App/Main.cs`: UI startup and probe flags such as `--spike-self-test`, `--terrain-export-16k` and `--interactive-brush-probe`.
- `tests/Mapwright.ContractTests/Program.cs`: standalone asynchronous contract-test executable.
- `Tools/StorageCrashWorker/Program.cs`: child process for storage and durable-edit kill/recovery probes.
- `Scripts/run-contract-tests.ps1` and `Scripts/run-spike.ps1`: shell launchers for the respective executable paths.
- `Scripts/run-connected-phase1.ps1`: discoverable case runner for real import/edit/fresh-process reopen/export assertions.

## Error Handling

**Strategy:** exceptions in services, translated at UI/probe boundaries.

- `Scripts/App/Main.cs` catches import/save errors, writes `GD.PrintErr` and updates status text.
- `src/Mapwright.Domain/Commands.cs` throws `RevisionConflictException`; `src/Mapwright.Application/EditSession.cs` releases its commit lock in `finally` and does not publish after a repository failure.
- `Scripts/Export/TerrainPipelineExportProbe.cs` catches failures, deletes partial output and returns a failed result with details.
- `docs/engine-decision.md` requires fresh-process recovery for device loss; do not interpret `Scripts/Rendering/DeviceLossProbe.cs` as a supported in-process recovery service.

## Cross-Cutting Concerns

**Logging:** Godot console/status/report text in `Scripts/App/Main.cs`; probe JSON under `artifacts/spike`, connected evidence under `artifacts/connected-phase1`; console PASS/FAIL output in `tests/Mapwright.ContractTests/Program.cs`.

**Validation:** revision, fixed-role and geometry guards in Domain; import parsing in `InkImportService`; authoritative connected blob hashes in `SqliteProjectRepository`; legacy blob hashes in `ProjectStore`; PNG validation in `PngValidator`.

**Threading:** background import in `Scripts/App/Main.cs`; render-thread resource work and deferred callbacks in `Scripts/Rendering/GlobalGpuBrushSurface.cs`; per-session commit serialization in `src/Mapwright.Application/EditSession.cs`.

**Authentication:** no account or authentication layer in the offline runtime entry point `Scripts/App/Main.cs`; accounts are explicitly outside the scope in `docs/spec.md`.

---

*Architecture analysis: 2026-09-22*
