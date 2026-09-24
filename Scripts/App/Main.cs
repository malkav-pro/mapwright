using Godot;
using Mapwright.Application;
using Mapwright.Core;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DomainMapPoint = Mapwright.Domain.MapPoint;

namespace Mapwright.App;

public partial class Main : Control
{
    private readonly InkImportService _importer = new();
    private MapCanvas _canvas = null!;
    private Label _status = null!;
    private TextEdit _report = null!;
    private Button _saveButton = null!;
    private Button _createProjectButton = null!;
    private Button _flattenedModeButton = null!;
    private Button _editableModeButton = null!;
    private Button _reopenButton = null!;
    private Button _exportButton = null!;
    private InkImportResult? _currentImport;
    private RecoveryReviewState? _recoveryReview;
    private EditSession? _editSession;
    private string? _currentProjectDirectory;
    private ConnectedTerrainGraph? _connectedGraph;

    internal static readonly string ConfiguredInkPath =
        System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE") ??
        @"C:\Users\almar\Downloads\Main Continent-backup-2026-09-21T18_46_35.150Z.ink";

    public override void _Ready()
    {
        var arguments = OS.GetCmdlineUserArgs();
        if (arguments.Contains("--connected-list-cases", StringComparer.Ordinal))
        {
            foreach (var name in ConnectedCaseRegistry.Discover().Names) GD.Print(name);
            GetTree().Quit();
            return;
        }
        var connectedCaseIndex = Array.IndexOf(arguments, "--connected-case");
        if (connectedCaseIndex >= 0)
        {
            if (connectedCaseIndex + 1 >= arguments.Length)
            {
                GD.PrintErr("A connected case name is required.");
                GetTree().Quit(2);
                return;
            }
            RunConnectedCaseAsync(arguments[connectedCaseIndex + 1]);
            return;
        }
        var shellFrameIndex = Array.IndexOf(arguments, "--shell-frame");
        if (shellFrameIndex >= 0)
        {
            CaptureShellFrameAsync(arguments, shellFrameIndex);
            return;
        }
        var scopeFrameIndex = Array.IndexOf(arguments, "--scope-frame");
        if (scopeFrameIndex >= 0)
        {
            CaptureScopeFrameAsync(arguments, scopeFrameIndex);
            return;
        }
        var reopenChildIndex = Array.IndexOf(arguments, "--connected-reopen-child");
        if (reopenChildIndex >= 0)
        {
            RunConnectedReopenChildAsync(arguments, reopenChildIndex);
            return;
        }
        if (arguments.Contains("--tdr-device-loss", StringComparer.Ordinal))
        {
            var resultPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike", "tdr-device-loss.json");
            var result = DeviceLossProbe.Run(resultPath);
            GD.Print(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit(result.InProcessRecoveryPassed ? 0 : 1);
            return;
        }
        var crashChildIndex = Array.IndexOf(arguments, "--storage-crash-child");
        if (crashChildIndex >= 0)
        {
            CrashRecoveryProbe.RunChild(arguments[crashChildIndex + 1],
                Enum.Parse<ProjectSaveStage>(arguments[crashChildIndex + 2], ignoreCase: false));
            GetTree().Quit();
            return;
        }
        if (arguments.Contains("--probe-seams", StringComparer.Ordinal))
        {
            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike");
            var result = GpuSeamProbe.Run(outputDirectory);
            GD.Print(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GetTree().Quit(result.Passed ? 0 : 1);
            return;
        }
        if (arguments.Contains("--terrain-export-16k", StringComparer.Ordinal))
        {
            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike");
            var result = TerrainPipelineExportProbe.Run(
                Path.Combine(outputDirectory, "terrain-pipeline-16k.png"), 16_384, 2_048, 22);
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(outputDirectory, "terrain-pipeline-16k.json"), json);
            GD.Print(json);
            GetTree().Quit(result.Passed ? 0 : 1);
            return;
        }
        if (arguments.Contains("--import-memory-probe", StringComparer.Ordinal))
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var baseline = process.WorkingSet64;
            var stopwatch = Stopwatch.StartNew();
            var imported = _importer.Import(ConfiguredInkPath);
            stopwatch.Stop();
            process.Refresh();
            var result = new
            {
                passed = imported.Rasters.Count == 3 &&
                         imported.Document.Import?.CommandCounts.Values.Sum() == 3_596 &&
                         imported.Document.Import.EntityCounts.Values.Sum() == 375,
                milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                baselineWorkingSetBytes = baseline,
                currentWorkingSetBytes = process.WorkingSet64,
                peakWorkingSetBytes = process.PeakWorkingSet64,
                imported.PeakJsonTokenBufferBytes,
                imported.RetainedRasterBytes,
                rasters = imported.Rasters.Count,
                commands = imported.Document.Import?.CommandCounts.Values.Sum(),
                entities = imported.Document.Import?.EntityCounts.Values.Sum(),
                unresolvedAssets = imported.Document.Import?.UnresolvedAssetIds.Count
            };
            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike");
            Directory.CreateDirectory(outputDirectory);
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(outputDirectory, "import-memory.json"), json);
            GD.Print(json);
            GetTree().Quit(result.passed ? 0 : 1);
            return;
        }
        if (arguments.Contains("--interactive-brush-probe", StringComparer.Ordinal))
        {
            BuildInterface();
            _canvas.EnableGpuBrush(() => _canvas.StartAutomatedBrushProbe(30, result =>
            {
                var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike");
                Directory.CreateDirectory(outputDirectory);
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outputDirectory, "interactive-brush.json"), json);
                GD.Print(json);
                GetTree().Quit(result.Completed ? 0 : 1);
            }));
            return;
        }
        if (arguments.Contains("--spike-self-test", StringComparer.Ordinal))
        {
            RunAutomatedSpike();
            return;
        }
        BuildConnectedInterface();
    }

    public override void _ExitTree()
    {
        _exportCancellation?.Cancel();
        if (_historySession is not null) _historySession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_editSession is not null) _editSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void RunConnectedCaseAsync(string caseName)
    {
        var exitCode = await ConnectedCaseRegistry.Discover().DispatchAsync(caseName, CancellationToken.None);
        GetTree().Quit(exitCode);
    }

    private async void RunConnectedReopenChildAsync(string[] arguments, int argumentIndex)
    {
        try
        {
            if (argumentIndex + 5 >= arguments.Length)
                throw new ArgumentException("Connected reopen child requires project, project id, revision, evidence path and PNG path.");
            var projectDirectory = arguments[argumentIndex + 1];
            var projectId = new ProjectId(Guid.Parse(arguments[argumentIndex + 2]));
            var revision = long.Parse(arguments[argumentIndex + 3], System.Globalization.CultureInfo.InvariantCulture);
            var evidencePath = arguments[argumentIndex + 4];
            var exportPath = arguments[argumentIndex + 5];
            var repository = new SqliteProjectRepository(projectDirectory);
            var reopened = await repository.LoadRevisionAsync(projectId, revision);
            var graph = new ConnectedTerrainGraph(projectDirectory);
            var frame = graph.Evaluate(reopened);
            var sample = frame.SampleAtDocument(reopened.Width * 0.5, reopened.Height * 0.5,
                reopened.Width, reopened.Height);
            var export = graph.ExportPng(reopened, exportPath);
            var evidence = new ConnectedReopenEvidence(
                reopened.ProjectId.Value,
                reopened.Revision,
                reopened.ImportedSource!.SourceSha256,
                frame.RgbaSha256,
                sample,
                export.PngSha256,
                export.Validation.Passed,
                export.Validation.Width,
                export.Validation.Height);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print(JsonSerializer.Serialize(evidence));
            GetTree().Quit(export.Validation.Passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            GetTree().Quit(1);
        }
    }

    private void RunAutomatedSpike()
    {
        var started = DateTime.UtcNow;
        var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "spike");
        Directory.CreateDirectory(outputDirectory);
        var gpu = GpuComputeProbe.Run();
        var terrain = GpuTerrainProbe.Run(Path.Combine(outputDirectory, "gpu-terrain-tile.png"));
        var seams = GpuSeamProbe.Run(outputDirectory);
        var terrainExport = TerrainPipelineExportProbe.Run(Path.Combine(outputDirectory, "terrain-pipeline-2k.png"));
        var export = TiledExportProbe.Run(Path.Combine(outputDirectory, "gpu-tiled-16k.png"));
        var exportSafety = ExportSafetyProbe.Run(Path.Combine(outputDirectory, "existing-export-safety.png"));
        var storage = StorageProbe.Run(Path.Combine(outputDirectory, "storage-fixture"));
        var history = HistoryProbe.Run();
        var dotnetExecutable = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? throw new InvalidOperationException("DOTNET_HOST_PATH is required by the crash-recovery harness.");
        var workerDll = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "StorageCrashWorker", "bin",
            "Debug", "net8.0", "StorageCrashWorker.dll");
        var crashRecovery = CrashRecoveryProbe.Run(dotnetExecutable, workerDll,
            Path.Combine(outputDirectory, "crash-recovery"));
        var editRecovery = DurableEditRecoveryProbe.Run(dotnetExecutable, workerDll,
            Path.Combine(outputDirectory, "edit-recovery"));
        InkImportResult? imported = null;
        string? importError = null;
        try
        {
            if (File.Exists(ConfiguredInkPath)) imported = _importer.Import(ConfiguredInkPath);
            else importError = $"Fixture missing: {ConfiguredInkPath}";
        }
        catch (Exception exception)
        {
            importError = exception.ToString();
        }
        var importedStorage = imported is null
            ? null
            : RunImportedStorageProbe(Path.Combine(outputDirectory, "imported-project.mapwright"), imported);

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var report = new
        {
            startedUtc = started,
            finishedUtc = DateTime.UtcNow,
            godot = Engine.GetVersionInfo()["string"].AsString(),
            operatingSystem = OS.GetName() + " " + OS.GetVersion(),
            processorCount = System.Environment.ProcessorCount,
            workingSetBytes = process.WorkingSet64,
            peakWorkingSetBytes = process.PeakWorkingSet64,
            gpu,
            terrain,
            seams,
            terrainExport,
            export,
            exportSafety,
            storage,
            history,
            crashRecovery,
            editRecovery,
            import = imported is null ? null : new
            {
                imported.Document.Title,
                imported.Document.Width,
                imported.Document.Height,
                imported.PreviewWidth,
                imported.PreviewHeight,
                rasterCheckpoints = imported.Rasters.Count,
                commands = imported.Document.Import!.CommandCounts.Values.Sum(),
                entities = imported.Document.Import.EntityCounts.Values.Sum(),
                unresolvedAssets = imported.Document.Import.UnresolvedAssetIds.Count
            },
            importedStorage,
            importError
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var reportPath = Path.Combine(outputDirectory, "environment-and-import.json");
        File.WriteAllText(reportPath, json);
        GD.Print(json);
        GetTree().Quit(gpu.Passed && terrain.Passed && seams.Passed && terrainExport.Passed &&
            export.Passed && exportSafety.Passed &&
            storage.Passed && history.Passed && crashRecovery.Passed && editRecovery.Passed && imported is not null &&
            importedStorage?.Passed == true ? 0 : 1);
    }

    private static ImportedStorageProbeResult RunImportedStorageProbe(string destination, InkImportResult import)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            var store = new ProjectStore();
            store.SaveImportedProject(destination, import);
            var firstOpen = store.OpenProject(destination);
            var cache = Path.Combine(destination, "cache");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "discardable.bin"), "derived cache fixture");
            Directory.Delete(cache, recursive: true);
            var reopened = store.OpenProject(destination);
            var preview = reopened.Layers.OfType<RasterLayer>().Single(layer => layer.Role == "preview");
            var previewBytes = File.ReadAllBytes(Path.Combine(destination, "blobs", preview.BlobHash));
            using var image = new Image();
            var decode = image.LoadPngFromBuffer(previewBytes);
            var renderedPath = Path.Combine(Path.GetDirectoryName(destination)!, "reopened-preview.png");
            var save = decode == Error.Ok ? image.SavePng(renderedPath) : Error.Failed;
            stopwatch.Stop();
            var sourcePreserved = reopened.Import?.SourceBlobHash == reopened.Import?.SourceSha256;
            var passed = firstOpen.Layers.Count == reopened.Layers.Count && sourcePreserved &&
                         decode == Error.Ok && save == Error.Ok;
            return new ImportedStorageProbeResult(passed, reopened.Layers.Count, sourcePreserved,
                decode == Error.Ok, save == Error.Ok ? renderedPath : null, stopwatch.Elapsed.TotalMilliseconds,
                "Saved the real imported rasters and original .ink as content-addressed blobs, deleted derived cache, reopened with hash verification, decoded the stored preview, and re-encoded that preview. Recomposition from recovered layers is not tested.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ImportedStorageProbeResult(false, 0, false, false, null,
                stopwatch.Elapsed.TotalMilliseconds, exception.ToString());
        }
    }

    private void BuildInterface()
    {
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        var toolbar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 54) };
        toolbar.AddThemeConstantOverride("separation", 8);
        root.AddChild(toolbar);

        var title = new Label { Text = "  MALKAV'S MAPWRIGHT", CustomMinimumSize = new Vector2(250, 0) };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color("e7d8b5"));
        toolbar.AddChild(title);

        var importButton = new Button { Text = "Import an .ink into a new project…" };
        importButton.Pressed += ImportSample;
        toolbar.AddChild(importButton);

        _editableModeButton = new Button
        {
            Text = "Editable recovered terrain (assessment pending)",
            Disabled = true,
            TooltipText = "Later import expansion enables editable recovery after source terrain mapping is verified."
        };
        toolbar.AddChild(_editableModeButton);

        _flattenedModeButton = new Button
        {
            Text = "Original flattened appearance",
            Disabled = true,
            ToggleMode = true,
            TooltipText = "Locked embedded preview as Background; Foreground starts empty. Baked coasts remain pixels."
        };
        _flattenedModeButton.Pressed += SelectFlattenedRecovery;
        toolbar.AddChild(_flattenedModeButton);

        _createProjectButton = new Button { Text = "Create project", Disabled = true };
        _createProjectButton.Pressed += CreateFlattenedProject;
        toolbar.AddChild(_createProjectButton);

        var fitButton = new Button { Text = "Fit map" };
        fitButton.Pressed += () => _canvas.FitToView();
        toolbar.AddChild(fitButton);

        var gpuButton = new Button { Text = "Run GPU probe" };
        gpuButton.Pressed += RunGpuProbe;
        toolbar.AddChild(gpuButton);

        _saveButton = new Button { Text = "Save project", Disabled = true };
        _saveButton.Pressed += SaveProject;
        toolbar.AddChild(_saveButton);

        _reopenButton = new Button { Text = "Reopen acknowledged revision", Disabled = true };
        _reopenButton.Pressed += ReopenProject;
        toolbar.AddChild(_reopenButton);

        _exportButton = new Button { Text = "Export PNG", Disabled = true };
        _exportButton.Pressed += ExportProject;
        toolbar.AddChild(_exportButton);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(split);

        _canvas = new MapCanvas
        {
            CustomMinimumSize = new Vector2(900, 600),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        split.AddChild(_canvas);

        var inspector = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        inspector.AddThemeConstantOverride("separation", 8);
        split.AddChild(inspector);
        inspector.AddChild(new Label { Text = "Spike report" });
        _report = new TextEdit
        {
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Text = "No project is open. Import an Inkarnate .ink backup or open an existing Mapwright project. Everything stays on this machine, and your source file is never modified."
        };
        inspector.AddChild(_report);

        _status = new Label
        {
            Text = "  Initialising…",
            CustomMinimumSize = new Vector2(0, 30)
        };
        _status.AddThemeColorOverride("font_color", new Color("9fb4b8"));
        root.AddChild(_status);
    }

    private void RunGpuProbe()
    {
        _status.Text = "  Running native GPU compute probe…";
        var result = GpuComputeProbe.Run();
        _report.Text = $"GPU COMPUTE\n\nPassed: {result.Passed}\nDevice: {result.Device}\nTime: {result.Milliseconds:0.0} ms\n{result.Detail}";
        _status.Text = result.Passed
            ? $"  GPU compute ready · {result.Device} · {result.Milliseconds:0.0} ms"
            : $"  GPU compute probe failed · {result.Detail}";
    }

    private async void ImportSample()
    {
        if (!File.Exists(ConfiguredInkPath))
        {
            _status.Text = $"  Sample file not found: {ConfiguredInkPath}";
            return;
        }

        _status.Text = "  Importing and validating .ink backup…";
        try
        {
            var imported = await Task.Run(() => _importer.Import(ConfiguredInkPath));
            _currentImport = imported;
            _recoveryReview = new RecoveryReviewState();
            if (imported.PreviewPng is not null) _canvas.SetPng(imported.PreviewPng);
            _report.Text = $"""
                {imported.Report}

                Recovery mode

                Editable recovered terrain
                Source terrain mapping is assessed in the next import expansion.

                Original flattened appearance
                Background is the locked embedded preview and Foreground starts empty. Baked coasts are pixels, not editable recovered geometry.

                Select a mode before Create project becomes available. The original source is never modified.
                """;
            _flattenedModeButton.Disabled = false;
            _flattenedModeButton.ButtonPressed = false;
            _createProjectButton.Disabled = true;
            _status.Text = $"  Recovery review ready · {imported.Document.Title} · no mode selected";
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            _status.Text = $"  Import failed: {exception.Message}";
        }
    }

    private void SelectFlattenedRecovery()
    {
        if (_recoveryReview is null) return;
        _recoveryReview.Select(ImportRecoveryMode.OriginalFlattenedAppearance);
        _flattenedModeButton.ButtonPressed = true;
        _createProjectButton.Disabled = !_recoveryReview.CanCreate;
        _status.Text = "  Original flattened appearance selected · Create project is available";
    }

    private async void CreateFlattenedProject()
    {
        if (_currentImport is null || _recoveryReview?.SelectedMode != ImportRecoveryMode.OriginalFlattenedAppearance ||
            _currentImport.PreviewPng is null) return;
        try
        {
            var root = ProjectSettings.GlobalizePath("user://projects");
            Directory.CreateDirectory(root);
            var name = string.Join("-", _currentImport.Document.Title
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim().Replace(' ', '-').ToLowerInvariant();
            var destination = Path.Combine(root, $"{name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.mapwright");
            var project = CreateFlattenedSnapshot(_currentImport);
            var displaySource = ConnectedTerrainGraph.PrepareDisplaySource(
                _currentImport.PreviewPng, _currentImport.PreviewWidth, _currentImport.PreviewHeight);
            project = project with
            {
                ImportedSource = project.ImportedSource! with
                {
                    DisplaySourceBlobHash = displaySource.Sha256,
                    DisplaySourceWidth = displaySource.Width,
                    DisplaySourceHeight = displaySource.Height
                }
            };
            var repository = new SqliteProjectRepository(destination);
            _status.Text = "  Saving — publishing immutable source and initial revision";
            await repository.CreateImportedAsync(project, _currentImport.SourcePath,
                [new ImportedBlobPayload(project.ImportedSource!.PreviewBlobHash,
                    _currentImport.PreviewPng, "flattened preview"),
                 new ImportedBlobPayload(displaySource.Sha256, displaySource.Bytes,
                    "immutable display source")]);
            _editSession = new EditSession(project, repository, new NullRenderInvalidationQueue());
            _currentProjectDirectory = destination;
            _connectedGraph = new ConnectedTerrainGraph(destination);
            _canvas.BindConnectedSession(_editSession, _connectedGraph, message => _status.Text = "  " + message);
            _saveButton.Disabled = false;
            _reopenButton.Disabled = false;
            _exportButton.Disabled = false;
            _createProjectButton.Disabled = true;
            _status.Text = $"  Saved revision 0 · {destination}";
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            _status.Text = $"  Save failed: {exception.Message}";
        }
    }

    private async void SaveProject()
    {
        if (_editSession is null) return;
        try
        {
            _status.Text = "  Saving — waiting for queued commands";
            var saved = await _editSession.SaveAsync();
            _status.Text = $"  Saved revision {saved.Revision}";
        }
        catch (Exception exception)
        {
            _status.Text = $"  Save failed — {exception.Message}";
        }
    }

    private async void ReopenProject()
    {
        if (_editSession is null || _currentProjectDirectory is null) return;
        try
        {
            var projectId = _editSession.Current.ProjectId;
            await _editSession.DisposeAsync();
            var repository = new SqliteProjectRepository(_currentProjectDirectory);
            var reopened = await repository.LoadAsync(projectId, CancellationToken.None);
            _editSession = new EditSession(reopened, repository, new NullRenderInvalidationQueue());
            _connectedGraph = new ConnectedTerrainGraph(_currentProjectDirectory);
            _canvas.BindConnectedSession(_editSession, _connectedGraph, message => _status.Text = "  " + message);
            _status.Text = $"  Recovered {reopened.Title} · acknowledged revision {reopened.Revision} restored";
        }
        catch (Exception exception)
        {
            _status.Text = $"  Reopen failed — {exception.Message}";
        }
    }

    private void ExportProject()
    {
        if (_editSession is null || _connectedGraph is null) return;
        try
        {
            var exportRoot = ProjectSettings.GlobalizePath("user://exports");
            Directory.CreateDirectory(exportRoot);
            var snapshot = _editSession.Current;
            var destination = Path.Combine(exportRoot, $"{snapshot.Title}-r{snapshot.Revision}.png");
            _status.Text = $"  Exporting revision {snapshot.Revision}";
            var export = _connectedGraph.ExportPng(snapshot, destination);
            _status.Text = $"  Published revision {export.Revision} · {export.Validation.Width} × {export.Validation.Height} · {destination}";
        }
        catch (Exception exception)
        {
            _status.Text = $"  Export failed — {exception.Message}";
        }
    }

    internal static MapProject CreateFlattenedSnapshot(InkImportResult import)
    {
        if (import.PreviewPng is null || import.PreviewWidth <= 0 || import.PreviewHeight <= 0)
            throw new InvalidDataException("Flattened recovery requires the reviewed preview.");
        var sourceHash = import.Document.Import?.SourceSha256
            ?? throw new InvalidDataException("Import source hash is missing.");
        var previewHash = Convert.ToHexString(SHA256.HashData(import.PreviewPng)).ToLowerInvariant();
        var background = new TerrainLayer(LayerId.New(), "Background", true, true, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background, previewHash);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var source = new ImportedSourceReference(
            Path.GetFileName(import.SourcePath), sourceHash, sourceHash, previewHash,
            import.PreviewWidth, import.PreviewHeight, ImportRecoveryMode.OriginalFlattenedAppearance);
        var editingLongestEdge = MapUnitPolicy.ChooseImportEditingLongestEdge(
            import.Document.Width, import.Document.Height);
        var project = MapProject.CreateNormalizedFromSource(
            ProjectId.New(), MapId.New(), import.Document.Title,
            import.Document.Width, import.Document.Height, editingLongestEdge,
            [background, foreground], source);
        project.ValidateConnectedTerrain();
        return project;
    }
}

public sealed record ImportedStorageProbeResult(
    bool Passed,
    int Layers,
    bool SourceInkPreserved,
    bool PreviewDecoded,
    string? ReopenedPreviewPath,
    double Milliseconds,
    string Detail);

public sealed class RecoveryReviewState
{
    public ImportRecoveryMode? SelectedMode { get; private set; }
    public bool CanCreate => SelectedMode is not null;

    public void Select(ImportRecoveryMode mode) => SelectedMode = mode;
}

public interface IConnectedCaseProvider
{
    void Register(ConnectedCaseRegistry registry);
}

public sealed class ConnectedCaseRegistry
{
    private readonly Dictionary<string, Func<CancellationToken, Task<int>>> _handlers =
        new(StringComparer.Ordinal);

    public IReadOnlyList<string> Names => _handlers.Keys.Order(StringComparer.Ordinal).ToArray();

    public void Register(string name, Func<CancellationToken, Task<int>>? handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (handler is null) throw new InvalidOperationException($"Connected case '{name}' has no handler.");
        if (!_handlers.TryAdd(name, handler))
            throw new InvalidOperationException($"Connected case '{name}' is registered more than once.");
    }

    public async Task<int> DispatchAsync(string name, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            GD.PrintErr($"Unknown connected case '{name}'. Registered cases: {string.Join(", ", Names)}");
            return 2;
        }
        try
        {
            var assertions = await handler(cancellationToken);
            if (assertions <= 0)
            {
                GD.PrintErr($"Connected case '{name}' executed zero assertions.");
                return 3;
            }
            GD.Print($"PASS {name} ({assertions} assertions)");
            return 0;
        }
        catch (Exception exception)
        {
            GD.PrintErr($"FAIL {name}: {exception}");
            return 1;
        }
    }

    public static ConnectedCaseRegistry Discover()
    {
        var registry = new ConnectedCaseRegistry();
        var providers = typeof(Main).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IConnectedCaseProvider).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
        foreach (var providerType in providers)
        {
            var provider = Activator.CreateInstance(providerType) as IConnectedCaseProvider
                ?? throw new InvalidOperationException($"Could not instantiate connected case provider {providerType.FullName}.");
            provider.Register(registry);
        }
        return registry;
    }
}

public sealed class TracerConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("Tracer", ConnectedTracer.RunAsync);
}

public sealed class ImportBoundsConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ImportBounds", ImportBoundsConnectedCase.RunAsync);
}

public sealed class ZeroAssertionConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("__ZeroAssertionMustFail__", _ => Task.FromResult(0));
}

public static class ImportBoundsConnectedCase
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var artifactRoot = Path.GetFullPath(ConnectedEvidenceRoot.Resolve(repositoryRoot, "import-bounds"));
        if (!artifactRoot.StartsWith(repositoryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ImportBounds artifact root escaped the worktree.");
        if (Directory.Exists(artifactRoot))
        {
            if (System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID") is not null)
                throw new InvalidOperationException("This Phase 01.1 ImportBounds run already has evidence.");
            Directory.Delete(artifactRoot, recursive: true);
        }
        Directory.CreateDirectory(artifactRoot);

        var sourcePath = Main.ConfiguredInkPath;
        var sourceHashBefore = await HashFileAsync(sourcePath, cancellationToken);
        var sourceLengthBefore = new FileInfo(sourcePath).Length;
        var defaults = ImportBounds.Default;

        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumCompressedBytes = sourceLengthBefore - 1
        }).Import(sourcePath), "compressed-size");
        Check(true, "Compressed-size budget did not reject before parsing.");

        var expandedPath = WriteGzip("expanded.ink",
            "{\"future\":[" + string.Join(',', Enumerable.Repeat("0", 1024)) + "]}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumExpandedBytes = 128,
            MaximumJsonTokenBytes = 64
        }).Import(expandedPath), "expanded-size");
        Check(true, "Expanded-byte budget did not reject.");

        var tokenPath = WriteGzip("tokens.ink", "{\"a\":1,\"b\":2,\"c\":3}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumJsonTokens = 3
        }).Import(tokenPath), "json-token-count");
        Check(true, "JSON token budget did not reject.");

        var depthPath = WriteGzip("depth.ink", "{\"a\":[[[[0]]]]}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumJsonDepth = 2
        }).Import(depthPath), "malformed-source");
        Check(true, "JSON nesting budget did not reject.");

        var oversizedBase64Path = WriteGzip("base64.ink",
            "{\"preview\":\"data:image/png;base64," + new string('A', 64) + "\"}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumBase64Characters = 32
        }).Import(oversizedBase64Path), "base64-size");
        Check(true, "Base64 budget did not reject before rental.");

        var dimensionPng = MinimalPngHeader(101, 1);
        var dimensionPath = WriteGzip("dimension.ink",
            $"{{\"preview\":\"data:image/png;base64,{Convert.ToBase64String(dimensionPng)}\"}}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumImageDimension = 100
        }).Import(dimensionPath), "image-dimensions");
        Check(true, "Image dimension budget did not reject.");

        var pixelsPng = MinimalPngHeader(8, 8);
        var pixelsPath = WriteGzip("pixels.ink",
            $"{{\"preview\":\"data:image/png;base64,{Convert.ToBase64String(pixelsPng)}\"}}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumImagePixels = 63
        }).Import(pixelsPath), "image-pixels");
        Check(true, "Image pixel budget did not reject.");

        var aggregatePng = Convert.ToBase64String(MinimalPngHeader(1, 1));
        var aggregatePath = WriteGzip("aggregate.ink",
            "{\"scene\":{\"normSceneSize\":{\"w\":1,\"h\":1}}," +
            $"\"preview\":\"data:image/png;base64,{aggregatePng}\"," +
            "\"layers\":[{\"layerId\":\"layer-bg\",\"layerImages\":[{" +
            $"\"canvasName\":\"brush\",\"image\":\"data:image/png;base64,{aggregatePng}\"}}]}}]}}");
        ExpectRejected(() => new InkImportService(defaults with
        {
            MaximumDecodedImageBytes = 40,
            MaximumRetainedRasterBytes = 40
        }).Import(aggregatePath), "aggregate-raster-size");
        Check(true, "Aggregate retained-raster budget did not reject.");

        var malformedPath = Path.Combine(artifactRoot, "malformed.ink");
        await File.WriteAllBytesAsync(malformedPath, [31, 139, 8, 0, 1, 2, 3], cancellationToken);
        ExpectRejected(() => new InkImportService().Import(malformedPath), "malformed-source");
        Check(true, "Malformed input did not produce a bounded readable rejection.");

        var imported = await Task.Run(() => new InkImportService().Import(sourcePath), cancellationToken);
        var package = InkImportPackageFactory.Create(imported);
        var projectsRoot = Path.Combine(artifactRoot, "projects");
        var projectDirectory = Path.Combine(projectsRoot, "real-editable.mapwright");
        var result = await new ImportProject(path => new SqliteProjectRepository(path)).ExecuteAsync(
            new ImportProjectRequest(package, ImportRecoveryMode.EditableRecoveredTerrain,
                projectsRoot, "real-editable.mapwright"), cancellationToken);
        Check(result.Published && result.RecoveryLevel == ImportRecoveryLevel.Editable,
            "The measured real fixture did not produce verified editable recovery.");
        Check(result.Project!.ImportedSource!.TrustedReplayCommandCount == 0,
            "Untested source commands crossed the trusted replay cutoff.");
        Check(result.Project.ImportedSource.ResolvedRasterReferences.Length == 3,
            "The real raster descriptors were not all retained.");
        var flattened = Main.CreateFlattenedSnapshot(imported);
        var displaySource = ConnectedTerrainGraph.PrepareDisplaySource(
            imported.PreviewPng!, imported.PreviewWidth, imported.PreviewHeight);
        flattened = flattened with
        {
            ImportedSource = flattened.ImportedSource! with
            {
                DisplaySourceBlobHash = displaySource.Sha256,
                DisplaySourceWidth = displaySource.Width,
                DisplaySourceHeight = displaySource.Height
            }
        };
        var flattenedDirectory = Path.Combine(projectsRoot, "real-flattened.mapwright");
        var flattenedRepository = new SqliteProjectRepository(flattenedDirectory);
        await flattenedRepository.CreateImportedAsync(flattened, sourcePath,
            [new ImportedBlobPayload(flattened.ImportedSource!.PreviewBlobHash,
                imported.PreviewPng!, "flattened preview"),
             new ImportedBlobPayload(displaySource.Sha256, displaySource.Bytes,
                "immutable display source")], cancellationToken);
        var flattenedReopened = await flattenedRepository.LoadAsync(flattened.ProjectId,
            cancellationToken);
        Check(flattenedReopened.ImportedSource?.DisplaySourceBlobHash == displaySource.Sha256,
            "Measured flattened import lost its immutable display source identity.");
        using (var importProcess = Process.GetCurrentProcess())
        {
            importProcess.Refresh();
            var peak = importProcess.PeakWorkingSet64;
            Check(peak > 0, "The real import process peak was not measurable.");
            GD.Print($"IMPORT_CAPACITY peak_process_bytes={peak} peak_process_measured=true " +
                     $"source_sha256={sourceHashBefore}");
        }

        var graph = new ConnectedTerrainGraph(projectDirectory);
        var reopenedBefore = await new SqliteProjectRepository(projectDirectory)
            .LoadAsync(result.Project.ProjectId, cancellationToken);
        var frameBefore = graph.Evaluate(reopenedBefore);
        var exportBefore = graph.ExportPng(reopenedBefore, Path.Combine(artifactRoot, "before.png"));
        var tiles = Path.Combine(projectDirectory, "cache", "tiles");
        var history = Path.Combine(projectDirectory, "cache", "history");
        Directory.CreateDirectory(tiles);
        Directory.CreateDirectory(history);
        await File.WriteAllTextAsync(Path.Combine(tiles, "0001.bin"), "adjacent-a", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tiles, "0002.bin"), "adjacent-b", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(history, "r0000.bin"), "stable-revision-zero", cancellationToken);
        var store = new ProjectStore();
        using (store.PinDisposableCacheEntry(projectDirectory, Path.Combine("cache", "tiles", "0001.bin")))
        {
            var refused = store.DeleteDisposableCaches(projectDirectory);
            Check(refused.RefusedForPinnedReader && Directory.Exists(tiles),
                "Cache deletion removed a pinned reader entry.");
        }
        var deleted = store.DeleteDisposableCaches(projectDirectory);
        Check(deleted.Deleted && !Directory.Exists(tiles) && !Directory.Exists(history),
            "Disposable cache directories were not deleted exactly.");
        var repeated = store.DeleteDisposableCaches(projectDirectory);
        Check(repeated.Deleted && repeated.DeletedDirectories == 0,
            "Repeated cache deletion was not idempotent.");

        var reopenedAfter = await new SqliteProjectRepository(projectDirectory)
            .LoadAsync(result.Project.ProjectId, cancellationToken);
        var frameAfter = graph.Evaluate(reopenedAfter);
        var exportAfter = graph.ExportPng(reopenedAfter, Path.Combine(artifactRoot, "after.png"));
        Check(reopenedAfter.Revision == reopenedBefore.Revision &&
              frameAfter.RgbaSha256 == frameBefore.RgbaSha256 &&
              exportAfter.PngSha256 == exportBefore.PngSha256,
            "Cache-free reopen/export changed revision or rendering identity.");
        Check(File.Exists(Path.Combine(projectDirectory, "blobs", result.Project.ImportedSource.SourceBlobHash)) &&
              result.Project.ImportedSource.ResolvedRasterReferences.All(raster =>
                  File.Exists(Path.Combine(projectDirectory, "blobs", raster.BlobHash))),
            "Cache deletion removed a pinned source or raster blob.");

        var sourceHashAfter = await HashFileAsync(sourcePath, cancellationToken);
        Check(sourceLengthBefore == new FileInfo(sourcePath).Length && sourceHashBefore == sourceHashAfter,
            "Hostile-input and cache tests changed the original source.");
        return assertions;

        string WriteGzip(string name, string json)
        {
            var path = Path.Combine(artifactRoot, name);
            using var file = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None);
            using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
            writer.Write(json);
            return path;
        }
    }

    private static void ExpectRejected(Action action, string code)
    {
        try
        {
            action();
        }
        catch (ImportRejectedException exception) when (exception.Code == code)
        {
            return;
        }
        throw new InvalidOperationException($"Expected ImportRejectedException '{code}'.");
    }

    private static byte[] MinimalPngHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        signature.CopyTo(bytes, 0);
        WriteInt32(bytes.AsSpan(16, 4), width);
        WriteInt32(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static void WriteInt32(Span<byte> bytes, int value)
    {
        bytes[0] = (byte)(value >> 24);
        bytes[1] = (byte)(value >> 16);
        bytes[2] = (byte)(value >> 8);
        bytes[3] = (byte)value;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}

public static class ConnectedTracer
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var artifactRoot = Path.GetFullPath(ConnectedEvidenceRoot.Resolve(repositoryRoot, "connected-phase1"));
        if (!artifactRoot.StartsWith(repositoryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connected artifact root escaped the worktree.");
        if (Directory.Exists(artifactRoot))
        {
            if (System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID") is not null)
                throw new InvalidOperationException("This Phase 01.1 connected run already has evidence.");
            Directory.Delete(artifactRoot, recursive: true);
        }
        Directory.CreateDirectory(artifactRoot);

        var sourcePath = Main.ConfiguredInkPath;
        Check(File.Exists(sourcePath), $"Real .ink fixture is missing: {sourcePath}");
        var sourceBytesBefore = new FileInfo(sourcePath).Length;
        var sourceHashBefore = await HashFileAsync(sourcePath, cancellationToken);
        var imported = await Task.Run(() => new InkImportService().Import(sourcePath), cancellationToken);
        foreach (var editingLongestEdge in MapUnitPolicy.EditingLongestEdges.Where(edge => edge is 1024 or 4096))
        {
            var firstGrid = MapUnitPolicy.FromGrid(40, 30, editingLongestEdge);
            var secondGrid = MapUnitPolicy.FromGrid(80, 60, editingLongestEdge);
            Check(firstGrid.Width == 1000 && firstGrid.Height == 750 &&
                  secondGrid.Width == 1000 && secondGrid.Height == 750 &&
                  firstGrid.Width / 40 == 25 && secondGrid.Width / 80 == 12.5,
                "Grid counts or editing pixels changed 4:3 map bounds or initial cell geometry.");
        }
        Check(imported.PreviewPng is { Length: > 0 }, "The real import did not provide a flattened preview.");
        Check(string.Equals(sourceHashBefore, imported.Document.Import?.SourceSha256, StringComparison.Ordinal),
            "Importer source identity disagrees with the fixture hash.");

        var review = new RecoveryReviewState();
        Check(!review.CanCreate && review.SelectedMode is null,
            "Recovery review must begin with neither mode selected and Create project disabled.");
        review.Select(ImportRecoveryMode.OriginalFlattenedAppearance);
        Check(review.CanCreate, "Selecting flattened recovery must enable project creation.");

        var projectDirectory = Path.Combine(artifactRoot, "tracer-project.mapwright");
        var initial = Main.CreateFlattenedSnapshot(imported);
        Check(initial.StorageFormatVersion == 3 && Math.Max(initial.Width, initial.Height) == 1000 &&
              Math.Abs(initial.Width / initial.Height -
                  imported.Document.Width / imported.Document.Height) < 1e-9,
            "Flattened import did not preserve source aspect in normalized map units.");
        var initialSource = initial.ImportedSource!;
        Check(Math.Abs(initialSource.SourceSceneWidth * initialSource.SourceToMapScale * 0.5 -
                  initial.Width * 0.5) < 1e-9 &&
              Math.Abs(initialSource.SourceSceneHeight * initialSource.SourceToMapScale * 0.5 -
                  initial.Height * 0.5) < 1e-9 &&
              initial.EditingPixelWidth > 0 && initial.EditingPixelHeight > 0,
            "Source center or persisted editing sampling disagrees with normalized geometry.");
        var editingViewport = MapCanvas.ResolveSamplingSize(initial, 4096);
        Check(editingViewport.X == initial.EditingPixelWidth &&
              editingViewport.Y == initial.EditingPixelHeight,
            "Canvas sampled the preview dimensions instead of persisted editing pixels.");
        Check(initial.TerrainLayers.Length == 2 &&
              initial.TerrainLayers[0].Role == TerrainRole.Background && initial.TerrainLayers[0].Locked &&
              initial.TerrainLayers[1].Role == TerrainRole.Foreground && !initial.TerrainLayers[1].Locked,
            "Flattened recovery must create locked Background below empty Foreground.");
        var repository = new SqliteProjectRepository(projectDirectory);
        await repository.CreateImportedAsync(initial, sourcePath, imported.PreviewPng!, cancellationToken);
        var reopenedInitial = await repository.LoadAsync(initial.ProjectId, cancellationToken);
        Check(reopenedInitial.Revision == initial.Revision && reopenedInitial.Width == initial.Width &&
              reopenedInitial.Height == initial.Height &&
              reopenedInitial.ImportedSource?.SourceSha256 == sourceHashBefore,
            "Normalized flattened project did not reopen at its exact revision and source identity.");
        Check(!File.Exists(Path.Combine(projectDirectory, "manifest.json")),
            "The legacy manifest must not become a second authority.");
        Check(File.Exists(Path.Combine(projectDirectory, "scene.sqlite")),
            "The authoritative SQLite database was not created.");

        var graph = new ConnectedTerrainGraph(projectDirectory);
        var beforeFrame = graph.Evaluate(initial);
        var center = new DomainMapPoint(initial.Width * 0.5, initial.Height * 0.5);
        var foreground = initial.RequireRole(TerrainRole.Foreground);
        var stroke = new PaintStroke(StrokeId.New(), [center],
            new ResolvedBrush(new string('c', 64), Math.Max(32, initial.Width / 24), 0.72, 0.86, 0.8,
                0.2, 0, 1201, 1), false, TerrainStrokeKind.Texture);
        await using var session = new EditSession(initial, repository, new NullRenderInvalidationQueue());
        var acknowledgement = await session.ExecuteAsync(new AddTextureStroke(
            CommandId.New(), initial.Revision, foreground.Id, stroke), cancellationToken);
        Check(acknowledgement.Revision == 1 && session.Current.Revision == 1,
            "The Foreground texture gesture was not durably acknowledged as revision 1.");
        var saveOne = await session.SaveAsync(cancellationToken);
        var saveTwo = await session.SaveAsync(cancellationToken);
        Check(saveOne.Revision == acknowledgement.Revision && saveTwo.Revision == acknowledgement.Revision,
            "Repeated Save with no queued command changed the revision.");

        var committed = session.Current;
        var viewportFrame = graph.Evaluate(committed);
        var beforeSample = beforeFrame.SampleAtDocument(center.X, center.Y, initial.Width, initial.Height);
        var viewportSample = viewportFrame.SampleAtDocument(center.X, center.Y, initial.Width, initial.Height);
        Check(beforeSample != viewportSample, "The acknowledged texture stroke did not change the viewport sample.");

        var concurrencyDirectory = Path.Combine(artifactRoot, "concurrency-project.mapwright");
        var concurrencyRepository = new SqliteProjectRepository(concurrencyDirectory);
        var concurrencyInitial = CreateConcurrencyProject();
        await concurrencyRepository.CreateAsync(concurrencyInitial, cancellationToken);
        var concurrencyForeground = concurrencyInitial.RequireRole(TerrainRole.Foreground);
        var first = new AddTextureStroke(CommandId.New(), concurrencyInitial.Revision, concurrencyForeground.Id,
            stroke with { Id = StrokeId.New() });
        var second = new AddTextureStroke(CommandId.New(), concurrencyInitial.Revision, concurrencyForeground.Id,
            stroke with { Id = StrokeId.New(), Samples = [new DomainMapPoint(64, 64)] });
        await concurrencyRepository.CommitAsync(concurrencyInitial, first, first.Apply(concurrencyInitial), cancellationToken);
        var conflictObserved = false;
        try
        {
            await concurrencyRepository.CommitAsync(concurrencyInitial, second, second.Apply(concurrencyInitial), cancellationToken);
        }
        catch (RevisionConflictException)
        {
            conflictObserved = true;
        }
        Check(conflictObserved, "Two commits on one base revision did not yield one revision conflict.");
        var concurrencyReopened = await concurrencyRepository.LoadAsync(concurrencyInitial.ProjectId, cancellationToken);
        Check(concurrencyReopened.Revision == concurrencyInitial.Revision + 1,
            "Concurrent commit fixture did not retain exactly one successor.");

        var evidencePath = Path.Combine(artifactRoot, "fresh-process-evidence.json");
        var exportPath = Path.Combine(artifactRoot, "tracer-export.png");
        var child = await RunReopenChildAsync(repositoryRoot, projectDirectory, committed.ProjectId,
            committed.Revision, evidencePath, exportPath, cancellationToken);
        Check(child.ExitCode == 0, $"Fresh-process reopen failed: {child.Error}");
        var reopenedEvidence = JsonSerializer.Deserialize<ConnectedReopenEvidence>(
            await File.ReadAllTextAsync(evidencePath, cancellationToken))
            ?? throw new InvalidDataException("Fresh-process evidence was empty.");
        Check(reopenedEvidence.ProjectId == committed.ProjectId.Value && reopenedEvidence.Revision == committed.Revision,
            "Fresh process evaluated a different project identity or revision.");
        Check(reopenedEvidence.RgbaSha256 == viewportFrame.RgbaSha256 && reopenedEvidence.Sample == viewportSample,
            "Viewport and fresh-process export did not share one render evaluation.");
        Check(reopenedEvidence.PngValid && File.Exists(exportPath),
            "Fresh-process PNG was not published and validated.");

        var wrongRevision = await RunReopenChildAsync(repositoryRoot, projectDirectory, committed.ProjectId,
            committed.Revision + 99, Path.Combine(artifactRoot, "wrong-revision.json"),
            Path.Combine(artifactRoot, "wrong-revision.png"), cancellationToken);
        Check(wrongRevision.ExitCode != 0, "A missing/different revision unexpectedly succeeded.");

        var previewBlob = Path.Combine(projectDirectory, "blobs", committed.ImportedSource!.PreviewBlobHash);
        var quarantinedPreview = previewBlob + ".missing-test";
        File.Move(previewBlob, quarantinedPreview);
        try
        {
            var missingBlob = await RunReopenChildAsync(repositoryRoot, projectDirectory, committed.ProjectId,
                committed.Revision, Path.Combine(artifactRoot, "missing-blob.json"),
                Path.Combine(artifactRoot, "missing-blob.png"), cancellationToken);
            Check(missingBlob.ExitCode != 0, "A project with a missing source blob unexpectedly reopened.");
        }
        finally
        {
            File.Move(quarantinedPreview, previewBlob);
        }

        var invalidRegistry = new ConnectedCaseRegistry();
        invalidRegistry.Register("one", _ => Task.FromResult(1));
        var duplicateRejected = false;
        try { invalidRegistry.Register("one", _ => Task.FromResult(1)); }
        catch (InvalidOperationException) { duplicateRejected = true; }
        Check(duplicateRejected, "Duplicate connected case names were not rejected.");
        var unimplementedRejected = false;
        try { invalidRegistry.Register("missing", null); }
        catch (InvalidOperationException) { unimplementedRejected = true; }
        Check(unimplementedRejected, "An unimplemented connected case was accepted.");
        var zeroRegistry = new ConnectedCaseRegistry();
        zeroRegistry.Register("zero", _ => Task.FromResult(0));
        Check(await zeroRegistry.DispatchAsync("zero", cancellationToken) != 0,
            "A zero-assertion connected case succeeded.");
        Check(await zeroRegistry.DispatchAsync("unknown", cancellationToken) != 0,
            "An unregistered connected case succeeded.");

        var sourceHashAfter = await HashFileAsync(sourcePath, cancellationToken);
        var sourceBytesAfter = new FileInfo(sourcePath).Length;
        Check(sourceBytesBefore == sourceBytesAfter && sourceHashBefore == sourceHashAfter,
            "The original .ink fixture changed during connected execution.");

        var finalEvidence = new ConnectedTracerEvidence(
            assertions,
            sourcePath,
            sourceBytesAfter,
            sourceHashAfter,
            committed.ProjectId.Value,
            committed.Revision,
            initial.TerrainLayers.Select(layer => layer.Role.ToString()).ToArray(),
            viewportFrame.RgbaSha256,
            viewportSample,
            reopenedEvidence.PngSha256,
            exportPath,
            reopenedEvidence.PngWidth,
            reopenedEvidence.PngHeight);
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "tracer-evidence.json"),
            JsonSerializer.Serialize(finalEvidence, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        GD.Print(JsonSerializer.Serialize(finalEvidence, new JsonSerializerOptions { WriteIndented = true }));
        return assertions;
    }

    private static MapProject CreateConcurrencyProject()
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Concurrency", 256, 256, 0,
            [background, foreground]);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task<ChildResult> RunReopenChildAsync(
        string repositoryRoot,
        string projectDirectory,
        ProjectId projectId,
        long revision,
        string evidencePath,
        string exportPath,
        CancellationToken cancellationToken)
    {
        var godot = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GODOT_PATH");
        if (string.IsNullOrWhiteSpace(godot) || !File.Exists(godot))
            throw new FileNotFoundException("MAPWRIGHT_GODOT_PATH must name the pinned Godot executable.", godot);
        var start = new ProcessStartInfo
        {
            FileName = godot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--path");
        start.ArgumentList.Add(repositoryRoot);
        start.ArgumentList.Add("--");
        start.ArgumentList.Add("--connected-reopen-child");
        start.ArgumentList.Add(projectDirectory);
        start.ArgumentList.Add(projectId.Value.ToString("D"));
        start.ArgumentList.Add(revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(evidencePath);
        start.ArgumentList.Add(exportPath);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the fresh-process reopen worker.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (!string.IsNullOrWhiteSpace(output)) GD.Print(output);
        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error)) GD.PrintErr(error);
        return new ChildResult(process.ExitCode, output, error);
    }
}

public sealed class NullRenderInvalidationQueue : IRenderInvalidationQueue
{
    public void Enqueue(ProjectId projectId, long revision, CommandId commandId, TileInvalidation invalidation)
    {
    }
}

public sealed record ConnectedReopenEvidence(
    Guid ProjectId,
    long Revision,
    string SourceSha256,
    string RgbaSha256,
    ConnectedPixel Sample,
    string PngSha256,
    bool PngValid,
    int PngWidth,
    int PngHeight);

public sealed record ConnectedTracerEvidence(
    int Assertions,
    string FixturePath,
    long FixtureBytes,
    string FixtureSha256,
    Guid ProjectId,
    long Revision,
    string[] TerrainRoles,
    string ViewportRgbaSha256,
    ConnectedPixel ViewportSample,
    string ExportPngSha256,
    string ExportPath,
    int ExportWidth,
    int ExportHeight);

public sealed record ChildResult(int ExitCode, string Output, string Error);
