# Phase 1: Connected Imported Terrain - Pattern Map

**Mapped:** 2026-09-22  
**Files analyzed:** 21 likely new or modified source file/seam groups  
**Analogs found:** 19 / 21

This maps implementation seams, not a mandatory file layout. Names marked **candidate** are suggested new files; the planner may consolidate them. All named analogs below are git-tracked (`git -c safe.directory=C:/Users/almar/Documents/Codex/mapwright ls-files -- <path>` returned each path). The current context, requirements, and approved `01-UI-SPEC.md` govern product behavior; existing probes show code mechanics only.

## File Classification

| New/Modified File | Role | Data Flow | Closest Tracked Analog | Match Quality |
|---|---|---|---|---|
| `src/Mapwright.Domain/MapModel.cs` | model | transform | `src/Mapwright.Domain/MapModel.cs` | exact seam |
| `src/Mapwright.Domain/Commands.cs` | model/command | event-driven | `src/Mapwright.Domain/Commands.cs` | exact seam |
| `src/Mapwright.Domain/Geometry.cs` | utility | transform | `src/Mapwright.Domain/MapModel.cs` | role-adjacent |
| `src/Mapwright.Application/Ports.cs` | provider/port | request-response | `src/Mapwright.Application/Ports.cs` | exact seam |
| `src/Mapwright.Application/EditSession.cs` | service | event-driven | `src/Mapwright.Application/EditSession.cs` | exact seam |
| `src/Mapwright.Application/HistorySession.cs` (**candidate**) | service | event-driven/batch | `src/Mapwright.Application/EditSession.cs` | role-match |
| `src/Mapwright.Application/ImportProject.cs` (**candidate**) | service | file-I/O/request-response | `Scripts/Core/InkImportService.cs`; `Scripts/Core/ProjectStore.cs` | flow-match |
| `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` | service/repository | CRUD | `src/Mapwright.Infrastructure/SqliteProjectRepository.cs` | exact seam |
| `Scripts/Core/InkImportService.cs` | service | streaming/file-I/O | `Scripts/Core/InkImportService.cs` | exact seam |
| `Scripts/Core/ProjectStore.cs` or successor blob service | service | file-I/O | `Scripts/Core/ProjectStore.cs` | exact seam; storage authority must change |
| `Scripts/App/Main.cs` | component/shell | event-driven | `Scripts/App/Main.cs` | exact seam |
| `Scripts/App/MapCanvas.cs` | component/gesture controller | event-driven | `Scripts/App/MapCanvas.cs` | exact seam; direct paint path must change |
| `Scripts/App/EditorControls.cs` (**candidate**) | component | event-driven | `Scripts/App/Main.cs` | role-match |
| `Scripts/Rendering/DocumentTileRenderer.cs` (**candidate**) | service/renderer | batch/transform | `Scripts/Rendering/GlobalGpuBrushSurface.cs`; `Scripts/Rendering/GpuJfaTerrainRenderer.cs` | role/flow-match |
| `Shaders/terrain_coverage.glsl` or replacement | shader | transform | `Shaders/terrain_coverage.glsl` | exact seam; fixture math only |
| `Shaders/terrain_sdf_color.glsl` or replacement | shader | transform | `Shaders/terrain_sdf_color.glsl` | exact seam; prohibited styling currently present |
| `Scripts/Export/DocumentPngExport.cs` (**candidate**) | service | streaming/file-I/O | `Scripts/Export/TerrainPipelineExportProbe.cs`; `Scripts/Export/StreamingPngWriter.cs` | flow-match |
| `Scripts/Export/StreamingPngWriter.cs`, `Scripts/Export/PngValidator.cs` | utility | streaming/file-I/O | same files | exact seam |
| `tests/Mapwright.ContractTests/Program.cs` | test | batch | `tests/Mapwright.ContractTests/Program.cs` | exact seam |
| connected Godot acceptance fixture (**candidate path**) | test | event-driven/batch | none | no analog |
| reusable Godot `Theme`/icon/font resources (**candidate paths**) | config/assets | file-I/O | none | no analog |

`Scenes/Main.tscn` is also modified when changing the root scene; its tracked scene-resource analog is itself (`Scenes/Main.tscn:1-12`). New component paths and test fixture filenames are deliberately provisional.

## Pattern Assignments

### Domain model and geometry — `src/Mapwright.Domain/MapModel.cs`, `Geometry.cs`

**Analog:** `src/Mapwright.Domain/MapModel.cs:1-3,35-81,88-103,105-148`.

Imports and value identity use `System.Collections.Immutable`, the file-scoped `Mapwright.Domain` namespace, and strongly typed record-struct IDs (`ProjectId`, `LayerId`, `StrokeId`, `RiverId`). A resolved recipe validates itself before entering immutable history:

```csharp
public sealed record ResolvedBrush(string TextureHash, double Radius, double Hardness,
    double Opacity, double Flow, double Spacing, double Rotation, int Seed, int AlgorithmVersion)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TextureHash)) throw new ArgumentException("A brush texture hash is required.");
        if (Radius <= 0) throw new ArgumentOutOfRangeException(nameof(Radius));
        if (AlgorithmVersion <= 0) throw new ArgumentOutOfRangeException(nameof(AlgorithmVersion));
    }
}
```

The actual source has the full range checks at `MapModel.cs:46-55`. `PaintStroke.Bounds` unions each sample segment with the brush radius (`:64-74`). Copy this immutable/validated structure for separate texture and coverage recipes, edge roughness/corner smoothing, taper/version, texture transform and jitter seed. Convert the UI's **diameter** to the internal radius once. Replace `MapProject.TerrainLayers` and its arbitrary `RequireLayerIndex` (`:128-147`) with fixed Background/Foreground roles and Foreground-only coverage ownership; replace the one-width river (`:105-126`) with the approved editable width profile and softness. `CoastlineStyle` at `:83-86` is historical, not a required user setting.

### Domain commands — `src/Mapwright.Domain/Commands.cs`

**Analog:** `src/Mapwright.Domain/Commands.cs:1-19,21-39,41-61,84-98`.

```csharp
public interface IEditCommand
{
    CommandId Id { get; }
    long BaseRevision { get; }
    DocumentChange Apply(MapProject project);
}
// In AddPaintStroke.Apply:
CommandGuard.RequireRevision(project, BaseRevision);
var updated = layer.Add(Stroke);
var next = project with { TerrainLayers = layers, Revision = project.Revision + 1 };
return new DocumentChange(next, new TileInvalidation(
    ImmutableHashSet.Create(LayerId), bounds, true, true, true));
```

Use command-side guards for fixed role/order, locked and hidden targets, and Foreground mask/river ownership. Split texture-colour invalidation from coverage/composite invalidation instead of copying all-true flags. `MoveRiverPoint.Apply` unions the old and new river bounds (`:49-60`); retain this rule for point, width and softness edits. The revision guard throws a typed `RevisionConflictException` (`:84-98`). No authentication pattern applies to this offline editor.

### Application ports, commit and history — `src/Mapwright.Application/Ports.cs`, `EditSession.cs`, candidate `HistorySession.cs`

**Analogs:** `Ports.cs:1-35`; `EditSession.cs:1-57`; for in-memory history only, `Scripts/Core/EditingDocument.cs:114-150,190-240`.

```csharp
await _commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
try
{
    var previous = Current;
    var change = command.Apply(previous);
    var committed = await _repository.CommitAsync(previous, command, change, cancellationToken)
        .ConfigureAwait(false);
    if (committed.CommandId != command.Id || committed.Revision != change.Project.Revision)
        throw new InvalidDataException("Repository acknowledged a different command or revision.");
    Volatile.Write(ref _current, change.Project);
    _renderQueue.Enqueue(previous.ProjectId, committed.Revision, command.Id, change.Invalidation);
    return new EditAcknowledgement(command.Id, committed.Revision, committed.CommittedAt,
        change.Invalidation);
}
finally { _commitLock.Release(); }
```

Copy the ordering and cancellation shape from `EditSession.cs:25-49`: validate → durable commit → publish snapshot → enqueue render → acknowledge. Extend `IProjectRepository`/`IRenderInvalidationQueue` through the existing port boundary (`Ports.cs:10-24`) for open/import, durable history cursor, cancellable older reconstruction, frozen revision capture and save-drain. The transient in-memory redo removal in `EditingDocument.cs:125-150` (`RemoveRange` on a new edit after undo) expresses desired semantics but cannot supply restart durability; implement its branch invalidation transactionally in SQLite. `DurableEditJournal` writes and flushes before changing its probe document (`EditingDocument.cs:190-220`), but production should have one SQLite authority.

### SQLite repository — `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`

**Analog:** same file `:1-24,28-50,53-107,110-160,163-213`.

```csharp
await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
var persistedRevision = await ReadCurrentRevisionAsync(connection, (SqliteTransaction)transaction,
    previous.ProjectId, cancellationToken).ConfigureAwait(false);
if (persistedRevision != previous.Revision)
    throw new RevisionConflictException(previous.Revision, persistedRevision);
// Parameterized command/revision inserts and compare-and-update current cursor.
await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
return new DurableCommit(command.Id, change.Project.Revision, committedAt);
```

The file uses `Microsoft.Data.Sqlite`, JSON serialization, parameterized SQL, `await using`, WAL and `synchronous=FULL` (`:110-123`). Current `schema_info`, `revisions`, `commands`, `current_project` (`:126-160`) have no history cursor/redo/checkpoint/blob reference model; evolve with migration/versioning. The current full `snapshot_json` every revision (`:175-188`) is a prototype, not a measured long-session policy. Keep source/blob publication durable before acknowledging a transaction that references it. Database failures propagate; the session leaves its prior snapshot unpublished.

### Import and source blobs — `Scripts/Core/InkImportService.cs`, `ProjectStore.cs`, candidate `ImportProject.cs`

**Analogs:** `InkImportService.cs:1-63,65-90,125-149,296-319,339-418`; `ProjectStore.cs:14-101,103-128`; `Scripts/Core/MapDocument.cs:45-76`.

```csharp
using var source = File.OpenRead(sourcePath);
var sourceHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
source.Position = 0;
using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: false);
using var tokenizer = new StreamingJsonTokenizer(gzip);
var payload = ParseRoot(tokenizer);
```

The importer records source hash, version, command/entity counts, raster checkpoints, preview and unresolved IDs (`InkImportService.cs:23-62`; `MapDocument.cs:45-76`). `ParseLayer` retains layer ID, image role, dimensions and head transaction ID (`InkImportService.cs:125-149`). Preserve that provenance while mapping only **verified** rasters/transforms to fixed Background/Foreground; report extras and leave the original preview available. The current parser skips unknown fields (`:65-90`) and uses a growing token buffer/base64 allocation (`:296-318,398-418`), so add explicit compressed, expanded, token, raster and document bounds before allocation. Do not treat parsing as complete source recovery.

```csharp
var sourceHash = StoreBlob(blobs, import.SourcePath);
if (!string.Equals(sourceHash, document.Import?.SourceSha256, StringComparison.Ordinal))
    throw new InvalidDataException("The import source no longer matches the hash recorded during recovery.");
// Existing prototype publishes a staged directory only after all files exist.
Directory.Move(staging, projectDirectory);
```

`ProjectStore.cs:19-73` supplies staging/content-addressed SHA-256 mechanics, and `:82-100` verifies blobs on open. Adapt this to the one authoritative SQLite project; the prototype manifest and arbitrary raster/preview layers (`:37-61`) must not become a second authority or a third terrain layer. The flat mode uses locked-preview Background plus empty Foreground only after the user chooses that mode. Existing exceptions at import/store boundaries should become concise UI errors without raw stack traces.

### Godot shell, canvas, controls — `Scripts/App/Main.cs`, `MapCanvas.cs`, candidate `EditorControls.cs`, `Scenes/Main.tscn`

**Analogs:** `Main.cs:1-18,237-300,312-357`; `MapCanvas.cs:28-40,113-169,171-205`; `Scenes/Main.tscn:1-12`.

```csharp
public partial class Main : Control
{
    private void BuildInterface()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(split);
    }
}
```

`Main.cs:237-300` shows native Godot `Control` composition and signal wiring (`Button.Pressed += ...`). Build the approved title/command/tool/canvas/layers/history/status layout and reusable states from UI-SPEC; the current sample-import and probe buttons are evidence harness controls. `Main.cs:312-335` shows background import with `Task.Run`, status updates and exception handling. Put the recent-projects entry and unselected two-mode import review ahead of project creation.

```csharp
case InputEventMouseButton wheel when wheel.Pressed &&
    wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
    var oldZoom = _zoom;
    _zoom = Math.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f), 0.01f, 16f);
    var worldUnderPointer = (wheel.Position - _pan) / oldZoom;
    _pan = wheel.Position - worldUnderPointer * _zoom;
    QueueRedraw();
    AcceptEvent();
    break;
```

`MapCanvas.cs:133-169` has pointer-centred wheel zoom and middle-button pan. Keep that coordinate transformation; extend temporary Space pan and focused-field shortcut ownership. `_Draw` uses one canvas node (`:171-184`), a useful basis for D-28 cursor overlays. **Replace** direct `_brushSurface.PaintDab` in `PaintAt` (`:190-199`) with cancelable gesture preview and one application command on release; Escape/capture loss/window deactivation restores the committed snapshot. `_pendingVisibleDabs` drained at frame callback (`:201-205`) is uncorrelated probe timing, not the REND-04 acceptance metric. The scene's root script hookup is `Scenes/Main.tscn:3-12`.

### Renderer and shaders — candidate `DocumentTileRenderer.cs`, `Shaders/*`

**Analogs:** `GlobalGpuBrushSurface.cs:21-35,37-67,69-87,120-135`; `GpuJfaTerrainRenderer.cs:25-50,52-104,122-170`; `terrain_coverage.glsl:19-36`; `terrain_sdf_color.glsl:22-69`.

```csharp
RenderingServer.CallOnRenderThread(Callable.From(InitializeOnRenderThread));
_rd = RenderingServer.GetRenderingDevice()
    ?? throw new NotSupportedException("Global RenderingDevice unavailable.");
// Compile checked shader, create uniform set/pipeline, expose display RID.
_displayTexture = RenderingServer.TextureRdCreate(_colorTexture);
Callable.From(() => _ready(_displayTexture)).CallDeferred();
```

Use render-thread ownership and RID cleanup from `GlobalGpuBrushSurface.cs:37-67,120-135` for viewport work. `GpuJfaTerrainRenderer.cs:25-50` explicitly records allocated buffers; `:52-104` shows bounded tile inputs, document origin in push constants, submit/sync/readback. Its local-device synchronous path is an export probe, not the interactive scheduler. New tile keys must include revision and graph inputs; use document-space texture anchoring, bounded halos and sequence-to-presented-revision timing. If the unstyled D-20 branch is selected, do not run unused distance passes.

The shader currently computes `clamp(island - river, 0.0, 1.0)` (`terrain_coverage.glsl:25-35`), then treats every resulting boundary as shore/ring (`terrain_sdf_color.glsl:42-69`). This paints river banks and includes decorative rings forbidden by D-19. Keep coverage subtraction ordering, but replace output with correct Background/Foreground colour and alpha composition. A fixed coast effect is conditional on verified coast identity at mouths, inland water and tile seams. No UI style controls.

### Export — candidate `DocumentPngExport.cs`, existing writer/validator

**Analogs:** `TerrainPipelineExportProbe.cs:21-99`; `StreamingPngWriter.cs:18-64`; `PngValidator.cs:18-38,73-119`.

```csharp
var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
using (var png = new StreamingPngWriter(temporary, size, size))
{
    // Render bounded bands/tiles; compose one RGBA row at a time.
    png.WriteRgbaRow(row);
    png.Complete();
}
var validation = PngValidator.ValidateRgba8(temporary, size, size);
if (!validation.Passed) throw new InvalidDataException(validation.Detail);
File.Move(temporary, destination, overwrite: true);
```

The probe's nested band/tile loop and halo crop are at `TerrainPipelineExportProbe.cs:39-76`. The writer validates dimensions/row length and flushes to disk (`StreamingPngWriter.cs:18-53`); validator checks signature, CRC, dimensions and full inflated scanline length with bounded buffers (`PngValidator.cs:18-119`). Adapt the probe to a frozen authoritative revision and pinned blobs, cancellable progress, the same viewport render graph, and publish only after validation. Current fixture coverage and coast rings (`TerrainPipelineExportProbe.cs:54-59,89`) must be replaced. On failure/cancel, delete only the job's sibling temporary and leave the previous destination untouched.

### Contract tests — `tests/Mapwright.ContractTests/Program.cs`

**Analog:** same file `:1-31,33-67,69-131,133-197`.

```csharp
var tests = new (string Name, Func<Task> Run)[]
{
    ("durable commit precedes render scheduling and acknowledgement", CommitPrecedesRenderAndAcknowledgement),
    ("failed commit is neither published nor rendered", FailedCommitIsNotPublished)
};
// Assertions include the exact event order:
Equal("commit-start,commit-durable,render-enqueued,acknowledged", string.Join(',', events));
```

Use the existing framework-free async contract list, explicit assertions and recording repository/renderer (`:171-193`). `SqliteCommitReopens` (`:102-131`) tests a new process/repository instance. Rewrite the four-layer fixture (`:133-140`) to exactly two fixed roles and split texture/coverage commands. Add durable cursor/redo, cancelled gesture, source hash/bounds, frozen export, colour/alpha and tile seam contracts. Historical tests that assert coastline reach and all-pass invalidation (`:33-45`) must follow the chosen styling branch and actual dependency graph. Connected Godot/UI/performance acceptance needs a new fixture; no existing test is an exact analog.

## Shared Patterns

### Revision and publication ordering

**Sources:** `src/Mapwright.Domain/Commands.cs:84-98`, `src/Mapwright.Application/EditSession.cs:25-49`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs:69-107`. Apply to every edit, history cursor transition and visible save state: validate base revision, transact durable command/cursor/blob references, publish immutable snapshot, schedule revision-keyed tiles, then acknowledge. Failed storage leaves the previous state visible and authoritative. The UI's “Saved” wording follows the durable acknowledgement, never GPU preview.

### Error and validation boundary

**Sources:** `MapModel.cs:46-55`, `Commands.cs:49-53,84-98`, `SqliteProjectRepository.cs:75-85`, `InkImportService.cs:285-319`, `Main.cs:320-334`. Domain rejects invalid targets/values; repository rejects stale revisions; import rejects malformed/bounded input; shell catches and translates failures to the approved inline copy. Do not reproduce `exception.ToString()` from probes in user-facing surfaces.

### File identity and atomic publication

**Sources:** `Scripts/Core/ProjectStore.cs:19-35,69-100,103-128`; `Scripts/Export/TerrainPipelineExportProbe.cs:31-33,80-97`. SHA-256 content identity and post-write verification apply to source/raster blobs. Staged project creation and validated sibling output apply to import/export. Ensure the authoritative SQLite transaction references only durably published blobs; the old manifest is prototype data, not another current document.

### Rendering resource ownership

**Sources:** `Scripts/Rendering/GlobalGpuBrushSurface.cs:21-35,37-67,120-135`; `Scripts/Rendering/GpuJfaTerrainRenderer.cs:25-50,161-170`. Create/free RenderingDevice resources on the correct device/thread, compile-check shaders, measure buffer allocations, and use bounded tiles. Use the global device for viewport display; local export device can only be used through bounded staging and the shared document graph.

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| connected Godot acceptance fixture (**candidate path**) | test | event-driven/batch | Existing tests are contract and synthetic probe runners; none drives real import → edit → reopen → export and correlated visible revision. |
| reusable Godot `Theme`/icon/font resources (**candidate paths**) | config/assets | file-I/O | Current UI uses inline theme overrides (`Main.cs:237-300`); no established shared Theme/icon/font resource architecture. Follow approved UI-SPEC. |

## Metadata

**Analog search scope:** tracked `src/Mapwright.*`, `Scripts/Core`, `Scripts/App`, `Scripts/Rendering`, `Scripts/Export`, `Shaders`, `Scenes`, and `tests/Mapwright.ContractTests`.  
**Files scanned:** 20 source/scene/shader files read, plus repository file inventory and tracked-source checks.  
**Pattern extraction date:** 2026-09-22.  
**Known limitations:** Current analogs are prototypes or probes; they establish mechanics, not phase acceptance. Filename proposals and exact decomposition remain planner decisions. No source files were edited.
