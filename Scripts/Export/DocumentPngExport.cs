using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Mapwright.App;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using InkImportService = Mapwright.Core.InkImportService;
using InkImportPackageFactory = Mapwright.Core.InkImportPackageFactory;

namespace Mapwright.Export;

public enum DocumentExportPhase
{
    Preparing = 0,
    Rendering = 1,
    Encoding = 2,
    Validating = 3,
    Publishing = 4,
    Completed = 5
}

public sealed class ExportPublicationConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("ExportPublication", ExportPublicationConnectedCase.RunAsync);
}

public static class ExportPublicationConnectedCase
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
        var artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "export-publication"));
        if (!artifactRoot.StartsWith(repositoryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export fixture escaped the repository artifact root.");
        if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var projectRoot = Path.Combine(artifactRoot, "fixture.mapwright");
        Directory.CreateDirectory(projectRoot);

        var ledger = new RenderResourceLedger(
            16L * RenderResourceLedger.Gibibyte,
            32L * RenderResourceLedger.Gibibyte,
            512L * RenderResourceLedger.Mebibyte);
        var snapshot = Fixture(revision: 7);
        var firstTileStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueRendering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = ProceduralCapture(firstTileStarted, continueRendering);
        var progressEvents = new List<DocumentExportProgress>();
        var destination = Path.Combine(artifactRoot, "halo-crop.png");
        var exporter = new DocumentPngExport(ledger, capture, projectRoot);
        var exportTask = exporter.ExportAsync(snapshot, destination, 63, 47,
            new DocumentPngExportOptions(17, 3),
            new InlineProgress<DocumentExportProgress>(progressEvents.Add), cancellationToken);
        await firstTileStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var laterEdit = snapshot with { Revision = snapshot.Revision + 1 };
        continueRendering.TrySetResult();
        var small = await exportTask.ConfigureAwait(false);

        Check(laterEdit.Revision == 8 && small.FrozenRevision == 7,
            "A later edit changed the export's frozen revision.");
        Check(small.Validation.Passed && small.Validation.HasSrgbMetadata &&
              small.Validation.HasStraightAlpha,
            "Published PNG did not pass dimensions, CRC, inflated-length, colour and alpha validation.");
        Check(small.TilesRendered == 12 && small.PeakExportBufferBytes <= ledger.ExportBufferBudgetBytes,
            "Tile/halo export did not remain within the bounded buffer ledger.");
        using (var image = new Image())
        {
            Check(image.Load(destination) == Error.Ok, "Published halo fixture could not be decoded.");
            var pixel = image.GetPixel(17, 19);
            Check(pixel.R8 == 17 && pixel.G8 == 19 && pixel.B8 == 7 && pixel.A8 == 255,
                "Halo crop shifted document coordinates at a tile boundary.");
        }
        Check(progressEvents.Any(item => item.Phase == DocumentExportPhase.Rendering &&
                                         item.TotalUnits == 12) &&
              progressEvents.Any(item => item.Phase == DocumentExportPhase.Encoding &&
                                         item.TotalUnits == 47) &&
              progressEvents.All(item => item.FrozenRevision == 7),
            "Export progress did not report real tile/row units and the frozen revision.");

        foreach (var phase in new[]
                 {
                     DocumentExportPhase.Preparing,
                     DocumentExportPhase.Rendering,
                     DocumentExportPhase.Encoding,
                     DocumentExportPhase.Validating,
                     DocumentExportPhase.Publishing
                 })
        {
            var cancelledDestination = Path.Combine(artifactRoot, $"cancel-{phase}.png");
            var previous = System.Text.Encoding.UTF8.GetBytes($"previous-{phase}");
            await File.WriteAllBytesAsync(cancelledDestination, previous, cancellationToken)
                .ConfigureAwait(false);
            using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cancelExporter = new DocumentPngExport(ledger,
                ProceduralCapture(), projectRoot,
                (observed, _, token) =>
                {
                    if (observed == phase) source.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.CompletedTask;
                });
            await ExpectCancelled(() => cancelExporter.ExportAsync(snapshot, cancelledDestination, 32, 24,
                new DocumentPngExportOptions(8, 2), cancellationToken: source.Token));
            Check((await File.ReadAllBytesAsync(cancelledDestination, cancellationToken)
                    .ConfigureAwait(false)).SequenceEqual(previous),
                $"Cancellation during {phase} changed the prior destination.");
            Check(!Directory.EnumerateFiles(artifactRoot,
                    $".{Path.GetFileName(cancelledDestination)}.*.partial").Any(),
                $"Cancellation during {phase} left this job's temporary sibling behind.");
        }

        var failedDestination = Path.Combine(artifactRoot, "disk-full.png");
        var failedPrevious = "previous-valid-export"u8.ToArray();
        await File.WriteAllBytesAsync(failedDestination, failedPrevious, cancellationToken)
            .ConfigureAwait(false);
        var failingExporter = new DocumentPngExport(ledger, ProceduralCapture(), projectRoot,
            (phase, completed, _) => phase == DocumentExportPhase.Encoding && completed == 0
                ? ValueTask.FromException(new IOException("Injected disk-full failure."))
                : ValueTask.CompletedTask);
        await ExpectThrows<IOException>(() => failingExporter.ExportAsync(snapshot, failedDestination, 32, 24,
            new DocumentPngExportOptions(8, 2), cancellationToken: cancellationToken));
        Check((await File.ReadAllBytesAsync(failedDestination, cancellationToken)
                .ConfigureAwait(false)).SequenceEqual(failedPrevious),
            "An encoding failure changed the prior destination.");

        await ExpectThrows<InvalidOperationException>(() => exporter.ExportAsync(snapshot,
            Path.Combine(projectRoot, "scene.png"), 8, 8, cancellationToken: cancellationToken));
        Check(true, "Project/source overwrite destination was rejected before publication.");

        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        var imported = await Task.Run(() => new InkImportService().Import(fixture), cancellationToken)
            .ConfigureAwait(false);
        var package = InkImportPackageFactory.Create(imported);
        var projects = Path.Combine(artifactRoot, "projects");
        var realProjectRoot = Path.Combine(projects, "real-export.mapwright");
        var importedProject = await new ImportProject(path => new SqliteProjectRepository(path)).ExecuteAsync(
            new ImportProjectRequest(package, ImportRecoveryMode.EditableRecoveredTerrain,
                projects, "real-export.mapwright"), cancellationToken).ConfigureAwait(false);
        Check(importedProject.Published && importedProject.Project is not null,
            "The real source fixture did not publish a connected project for the 16K gate.");
        var reopened = await new SqliteProjectRepository(realProjectRoot)
            .LoadAsync(importedProject.Project!.ProjectId, cancellationToken).ConfigureAwait(false);
        var graph = new ConnectedTerrainGraph(realProjectRoot, ledger);
        var realExporter = new DocumentPngExport(graph, ledger, realProjectRoot);
        foreach (var longest in new[] { 1024, 4096 })
        {
            var (outputWidth, outputHeight) = DocumentPngExport.DimensionsForPreset(reopened, longest);
            var path = Path.Combine(artifactRoot, $"real-source-{longest}.png");
            var result = await realExporter.ExportPresetAsync(reopened, path, longest,
                new DocumentPngExportOptions(1_024, 16), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Check(result.Validation.Passed && result.Width == outputWidth &&
                  result.Height == outputHeight &&
                  result.Validation.InflatedBytes == checked((long)(outputWidth * 4 + 1) * outputHeight),
                "The target-scale real-source PNG failed aspect or inflated-scanline validation.");
            Check(result.PinnedBlobHashes.Count >= 4 &&
                  result.PeakExportBufferBytes <= ledger.ExportBufferBudgetBytes &&
                  result.PeakProcessWorkingSetBytes <= ledger.ProcessBudgetBytes,
                "The real-source export lost blob pins or exceeded its resource envelope.");
            using (var image = new Image())
            {
                Check(image.Load(path) == Error.Ok, "Target-scale PNG could not be decoded.");
                foreach (var (x, y) in new[]
                         {
                             (outputWidth / 4, outputHeight / 4),
                             (outputWidth / 2, outputHeight / 2),
                             (outputWidth * 3 / 4, outputHeight * 3 / 4)
                         })
                {
                    var expected = graph.EvaluateRegionAtSize(reopened,
                        outputWidth, outputHeight, x, y, 1, 1).Rgba;
                    var pixel = image.GetPixel(x, y);
                    Check(Math.Abs(pixel.R8 - expected[0]) <= 1 &&
                          Math.Abs(pixel.G8 - expected[1]) <= 1 &&
                          Math.Abs(pixel.B8 - expected[2]) <= 1 &&
                          Math.Abs(pixel.A8 - expected[3]) <= 1,
                        "Frozen export did not sample the target graph pixel center.");
                }
            }
            GD.Print($"EXPORT revision={result.FrozenRevision} dimensions={result.Width}x{result.Height} " +
                     $"tiles={result.TilesRendered} peak_export_buffer_bytes={result.PeakExportBufferBytes} " +
                     $"peak_process_bytes={result.PeakProcessWorkingSetBytes} " +
                     $"png_sha256={result.PngSha256} pinned_blobs={result.PinnedBlobHashes.Count}");
            File.Delete(path);
        }
        var (maximumWidth, maximumHeight) = DocumentPngExport.DimensionsForPreset(reopened, 16_384);
        var maximumTile = graph.EvaluateRegionAtSize(reopened, maximumWidth, maximumHeight,
            maximumWidth / 2, maximumHeight / 2, 1, 1);
        Check(maximumTile.Rgba.Length == 4 && maximumTile.FullWidth == maximumWidth &&
              maximumTile.FullHeight == maximumHeight,
            "A 16K longest-edge tile was not admitted within bounded graph resources.");
        return assertions;
    }

    private static FrozenDocumentCapture ProceduralCapture(
        TaskCompletionSource? started = null,
        TaskCompletionSource? proceed = null) =>
        (snapshot, width, height, cancellationToken) => ValueTask.FromResult<IFrozenDocumentRenderer>(
            new ProceduralFrozenRenderer(snapshot.ProjectId, snapshot.Revision, width, height,
                started, proceed));

    private static MapProject Fixture(long revision)
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), [], TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), [], TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Export fixture", 63, 47, revision,
            [background, foreground]);
    }

    private static async Task ExpectCancelled(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        throw new InvalidOperationException("Expected export cancellation.");
    }

    private static async Task ExpectThrows<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ProceduralFrozenRenderer(
        ProjectId projectId,
        long revision,
        int outputWidth,
        int outputHeight,
        TaskCompletionSource? started,
        TaskCompletionSource? proceed) : IFrozenDocumentRenderer
    {
        private int _renderCount;

        public ProjectId ProjectId => projectId;
        public long Revision => revision;
        public IReadOnlyCollection<string> PinnedBlobHashes => Array.Empty<string>();

        public async ValueTask<byte[]> RenderAsync(
            ExportTileRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OutputWidth != outputWidth || request.OutputHeight != outputHeight)
                throw new InvalidDataException("Procedural fixture received the wrong output dimensions.");
            if (Interlocked.Increment(ref _renderCount) == 1 && started is not null && proceed is not null)
            {
                started.TrySetResult();
                await proceed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var rgba = new byte[checked(request.Width * request.Height * 4)];
            for (var y = 0; y < request.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = 0; x < request.Width; x++)
                {
                    var offset = (y * request.Width + x) * 4;
                    rgba[offset] = (byte)((request.Left + x) % 251);
                    rgba[offset + 1] = (byte)((request.Top + y) % 251);
                    rgba[offset + 2] = (byte)(revision % 251);
                    rgba[offset + 3] = 255;
                }
            }
            return rgba;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class CrashRecoveryConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("CrashRecovery", CrashRecoveryConnectedCase.RunAsync);
}

public static class CrashRecoveryConnectedCase
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
        var artifactRoot = Path.Combine(repositoryRoot, "artifacts", "crash-recovery");
        if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var dotnet = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? throw new InvalidOperationException("DOTNET_HOST_PATH is required.");
        var worker = Path.Combine(repositoryRoot, "Tools", "StorageCrashWorker", "bin", "Debug",
            "net8.0", "StorageCrashWorker.dll");
        Check(File.Exists(worker), "StorageCrashWorker was not built by the connected runner.");

        foreach (var stage in Enum.GetValues<StorageCrashStage>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.Combine(artifactRoot, stage.ToString());
            var initialized = await InitializeScenarioAsync(root, cancellationToken).ConfigureAwait(false);
            var marker = Path.Combine(root, "stage.marker");
            using var child = Start(dotnet, worker, "--storage-crash-child", root, stage.ToString());
            await WaitForMarkerAsync(child, marker, cancellationToken).ConfigureAwait(false);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (stage == StorageCrashStage.BeforeBlobFlush)
            {
                var stagedBytes = "verified-owned-stage"u8.ToArray();
                var stagedHash = Convert.ToHexString(SHA256.HashData(stagedBytes)).ToLowerInvariant();
                await File.WriteAllBytesAsync(Path.Combine(root, "project.mapwright", "blobs",
                    $".{stagedHash}.stage-abandoned"), stagedBytes, cancellationToken).ConfigureAwait(false);
            }

            var first = await RunVerifierAsync(dotnet, worker, root, stage, cancellationToken)
                .ConfigureAwait(false);
            var second = await RunVerifierAsync(dotnet, worker, root, stage, cancellationToken)
                .ConfigureAwait(false);
            Check(first.Passed && second.Passed,
                $"Fresh-process recovery failed for {stage}: {first.Detail} / {second.Detail}");
            var expectedRevision = stage is StorageCrashStage.AfterSqliteCommit or
                StorageCrashStage.AfterAcknowledgement ? 1 : 0;
            Check(first.Revision == expectedRevision && first.CursorRevision == expectedRevision &&
                  first.LatestRevision == expectedRevision,
                $"{stage} recovered an intermediate command/cursor state.");
            Check(second.Revision == first.Revision && second.CursorRevision == first.CursorRevision &&
                  second.DestinationSha256 == first.DestinationSha256,
                $"Repeated recovery was not idempotent for {stage}.");
            Check(first.VerifiedBlobCount >= 2,
                $"{stage} did not verify every referenced source/preview blob.");
            if (stage == StorageCrashStage.BeforeBlobFlush)
                Check(first.CollectedOwnedStages == 1,
                    "Fresh recovery did not collect the verified exclusively-owned staged blob.");
            var shouldPublishNew = stage == StorageCrashStage.AfterPngPublication;
            Check(shouldPublishNew
                    ? first.DestinationSha256 != initialized.PreviousDestinationSha256
                    : first.DestinationSha256 == initialized.PreviousDestinationSha256,
                $"{stage} exposed a partial or unexpected export destination.");
        }

        var orderingRoot = Path.Combine(artifactRoot, "ordering");
        var ordering = await InitializeScenarioAsync(orderingRoot, cancellationToken).ConfigureAwait(false);
        var orderingRepository = new SqliteProjectRepository(Path.Combine(orderingRoot, "project.mapwright"),
            new EqualTimestampClock());
        var initial = await orderingRepository.LoadAsync(ordering.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        var rename = new RenameTerrainLayer(CommandId.New(), initial.Revision,
            TerrainRole.Foreground, "First same-time command");
        var renamed = rename.Apply(initial);
        await orderingRepository.CommitAsync(initial, rename, renamed, cancellationToken)
            .ConfigureAwait(false);
        var opacity = new SetTerrainOpacity(CommandId.New(), renamed.Project.Revision,
            TerrainRole.Foreground, 0.75);
        var changed = opacity.Apply(renamed.Project);
        await orderingRepository.CommitAsync(renamed.Project, opacity, changed, cancellationToken)
            .ConfigureAwait(false);
        var page = await orderingRepository.ReadHistoryPageAsync(ordering.ProjectId, 0, 10,
            cancellationToken).ConfigureAwait(false);
        Check(page.Count == 2 && page[0].Sequence < page[1].Sequence &&
              page[0].Revision == 1 && page[1].Revision == 2 &&
              page[0].CommittedAt == page[1].CommittedAt,
            "Equal-timestamp commands did not retain stable sequence/revision ordering.");
        var pinnedRevision = await orderingRepository.LoadRevisionAsync(ordering.ProjectId, 0,
            cancellationToken).ConfigureAwait(false);
        var currentRevision = await orderingRepository.LoadAsync(ordering.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        Check(pinnedRevision.Revision == 0 && currentRevision.Revision == 2,
            "A concurrent reader did not retain its pinned immutable revision.");

        GD.Print($"RECOVERY stages={Enum.GetValues<StorageCrashStage>().Length} " +
                 $"equal_timestamp_revisions={string.Join(',', page.Select(item => item.Revision))} " +
                 $"pinned_revision={pinnedRevision.Revision} current_revision={currentRevision.Revision}");
        return assertions;
    }

    private static async Task<InitializedScenario> InitializeScenarioAsync(
        string root,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.ink");
        var sourceBytes = "immutable-crash-source"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, sourceBytes, cancellationToken).ConfigureAwait(false);
        var previewPath = Path.Combine(root, "preview.png");
        using (var writer = new StreamingPngWriter(previewPath, 32, 24))
        {
            var row = new byte[32 * 4];
            for (var offset = 0; offset < row.Length; offset += 4)
            {
                row[offset] = 24;
                row[offset + 1] = 48;
                row[offset + 2] = 96;
                row[offset + 3] = 255;
            }
            for (var y = 0; y < 24; y++) writer.WriteRgbaRow(row);
            writer.Complete();
        }
        var preview = await File.ReadAllBytesAsync(previewPath, cancellationToken).ConfigureAwait(false);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var previewHash = Convert.ToHexString(SHA256.HashData(preview)).ToLowerInvariant();
        var imported = new ImportedSourceReference("source.ink", sourceHash, sourceHash, previewHash,
            32, 24, ImportRecoveryMode.OriginalFlattenedAppearance);
        var background = new TerrainLayer(LayerId.New(), "Background", true, true, 1,
            new CoastlineStyle(0), [], TerrainRole.Background, previewHash);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), [], TerrainRole.Foreground);
        var project = new MapProject(ProjectId.New(), MapId.New(), "Crash fixture", 32, 24, 0,
            [background, foreground], ImportedSource: imported);
        var projectRoot = Path.Combine(root, "project.mapwright");
        await new SqliteProjectRepository(projectRoot).CreateImportedAsync(project, sourcePath, preview,
            cancellationToken).ConfigureAwait(false);
        await WriteDurableTextAsync(Path.Combine(root, "project-id.txt"),
            project.ProjectId.Value.ToString("D"), cancellationToken).ConfigureAwait(false);

        var destination = Path.Combine(root, "map.png");
        using (var writer = new StreamingPngWriter(destination, 32, 24))
        {
            var row = new byte[32 * 4];
            for (var offset = 0; offset < row.Length; offset += 4)
            {
                row[offset] = 8;
                row[offset + 1] = 16;
                row[offset + 2] = 32;
                row[offset + 3] = 255;
            }
            for (var y = 0; y < 24; y++) writer.WriteRgbaRow(row);
            writer.Complete();
        }
        await using var input = File.OpenRead(destination);
        var destinationHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)
                .ConfigureAwait(false))
            .ToLowerInvariant();
        return new InitializedScenario(project.ProjectId, destinationHash);
    }

    private static Process Start(string dotnet, string worker, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = dotnet,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(worker);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start storage crash worker.");
    }

    private static async Task WaitForMarkerAsync(
        Process child,
        string marker,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!File.Exists(marker) && !child.HasExited && DateTime.UtcNow < deadline)
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        if (File.Exists(marker)) return;
        if (!child.HasExited) child.Kill(entireProcessTree: true);
        var error = await child.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"Crash worker did not reach its marker: {error}");
    }

    private static async Task<CrashVerificationOutput> RunVerifierAsync(
        string dotnet,
        string worker,
        string root,
        StorageCrashStage stage,
        CancellationToken cancellationToken)
    {
        using var verifier = Start(dotnet, worker, "--storage-verify-child", root, stage.ToString());
        var outputTask = verifier.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = verifier.StandardError.ReadToEndAsync(cancellationToken);
        await verifier.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (verifier.ExitCode != 0)
            throw new InvalidOperationException($"Fresh verifier failed ({verifier.ExitCode}): {error} {output}");
        return JsonSerializer.Deserialize<CrashVerificationOutput>(output.Trim(),
                   new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidDataException("Fresh verifier returned no JSON result.");
    }

    private static async Task WriteDurableTextAsync(
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await using var output = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write,
            FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private sealed record InitializedScenario(ProjectId ProjectId, string PreviousDestinationSha256);

    private sealed record CrashVerificationOutput(
        bool Passed,
        long Revision,
        long CursorRevision,
        long LatestRevision,
        int VerifiedBlobCount,
        int CollectedOwnedStages,
        string DestinationSha256,
        string Detail);

    private sealed class EqualTimestampClock : IApplicationClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 23, 1, 2, 3, TimeSpan.Zero);
    }
}

public sealed record DocumentExportProgress(
    DocumentExportPhase Phase,
    long CompletedUnits,
    long TotalUnits,
    long FrozenRevision);

public sealed record DocumentPngExportOptions(int TileInterior = 2_048, int Halo = 0)
{
    public void Validate()
    {
        if (TileInterior is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(TileInterior));
        if (Halo is < 0 or > 2_048) throw new ArgumentOutOfRangeException(nameof(Halo));
    }
}

public sealed record ExportTileRequest(
    int OutputWidth,
    int OutputHeight,
    int Left,
    int Top,
    int Width,
    int Height,
    int InteriorLeft,
    int InteriorTop,
    int InteriorWidth,
    int InteriorHeight);

public interface IFrozenDocumentRenderer : IAsyncDisposable
{
    ProjectId ProjectId { get; }
    long Revision { get; }
    IReadOnlyCollection<string> PinnedBlobHashes { get; }
    ValueTask<byte[]> RenderAsync(ExportTileRequest request, CancellationToken cancellationToken);
}

public delegate ValueTask<IFrozenDocumentRenderer> FrozenDocumentCapture(
    MapProject snapshot,
    int outputWidth,
    int outputHeight,
    CancellationToken cancellationToken);

public delegate ValueTask DocumentExportCheckpoint(
    DocumentExportPhase phase,
    long completedUnits,
    CancellationToken cancellationToken);

public sealed record DocumentPngExportResult(
    string Destination,
    ProjectId ProjectId,
    long FrozenRevision,
    int Width,
    int Height,
    int TilesRendered,
    long PeakExportBufferBytes,
    long PeakProcessWorkingSetBytes,
    string PngSha256,
    PngValidationResult Validation,
    IReadOnlyCollection<string> PinnedBlobHashes);

public sealed class DocumentPngExport
{
    public const int MaximumDimension = 16_384;

    public static (int Width, int Height) DimensionsForPreset(
        MapProject snapshot, int longestEdge)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.ValidateConnectedTerrain();
        if (snapshot.StorageFormatVersion != 3 ||
            !MapUnitPolicy.ExportLongestEdges.Contains(longestEdge))
            throw new ArgumentOutOfRangeException(nameof(longestEdge),
                "Map export requires normalized geometry and a supported longest-edge preset.");
        var shortEdge = checked((int)Math.Round(
            Math.Min(snapshot.Width, snapshot.Height) / Math.Max(snapshot.Width, snapshot.Height) *
            longestEdge, MidpointRounding.ToEven));
        if (shortEdge < 1 || shortEdge > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(longestEdge));
        return snapshot.Width >= snapshot.Height
            ? (longestEdge, shortEdge) : (shortEdge, longestEdge);
    }

    private readonly RenderResourceLedger _ledger;
    private readonly FrozenDocumentCapture _capture;
    private readonly string? _projectDirectory;
    private readonly DocumentExportCheckpoint? _checkpoint;

    public DocumentPngExport(
        ConnectedTerrainGraph graph,
        RenderResourceLedger ledger,
        string projectDirectory,
        DocumentExportCheckpoint? checkpoint = null)
        : this(ledger, CreateGraphCapture(graph, projectDirectory), projectDirectory, checkpoint)
    {
    }

    public DocumentPngExport(
        RenderResourceLedger ledger,
        FrozenDocumentCapture capture,
        string? projectDirectory = null,
        DocumentExportCheckpoint? checkpoint = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _projectDirectory = projectDirectory is null ? null : Path.GetFullPath(projectDirectory);
        _checkpoint = checkpoint;
    }

    /// <summary>
    /// Production exporter: evaluates the frozen revision with the GPU terrain kernels when
    /// a RenderingDevice with fp64 compute exists, otherwise with the CPU reference graph.
    /// Both are pixel-equivalent (GpuTerrainParity / GpuExportParity gates).
    /// </summary>
    public static DocumentPngExport ForProject(
        ConnectedTerrainGraph graph,
        RenderResourceLedger ledger,
        string projectDirectory,
        DocumentExportCheckpoint? checkpoint = null)
    {
        var capture = ConnectedGpuTerrainRenderer.CanCreate(out _)
            ? CreateGpuCapture(projectDirectory)
            : CreateGraphCapture(graph, projectDirectory);
        return new DocumentPngExport(ledger, capture, projectDirectory, checkpoint);
    }

    /// <summary>
    /// Frozen GPU capture. Each export tile is evaluated in bounded sub-tiles (at most
    /// <see cref="GpuExportSubTile"/> square, two readbacks in flight) so export never
    /// holds a full-document buffer or saturates the device during interactive edits.
    /// </summary>
    public static FrozenDocumentCapture CreateGpuCapture(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        return async (snapshot, outputWidth, outputHeight, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pins = PinnedBlobLease.Open(root, snapshot);
            ConnectedGpuTerrainRenderer? renderer = null;
            try
            {
                renderer = ConnectedGpuTerrainRenderer.CreateForExport()
                    ?? throw new NotSupportedException("GPU export requires a RenderingDevice.");
                // Frozen sources: verified immutable rasters for this revision and sampling.
                var graph = new ConnectedTerrainGraph(root);
                var sources = await Task.Run(() => graph.PrepareGpuSources(snapshot, outputWidth, outputHeight),
                    cancellationToken).ConfigureAwait(false);
                return new GpuFrozenRenderer(snapshot, renderer, sources, outputWidth, outputHeight, pins);
            }
            catch
            {
                renderer?.Dispose();
                pins.Dispose();
                throw;
            }
        };
    }

    public const int GpuExportSubTile = 512;

    private sealed class GpuFrozenRenderer(
        MapProject snapshot,
        ConnectedGpuTerrainRenderer renderer,
        GpuTerrainSources sources,
        int outputWidth,
        int outputHeight,
        PinnedBlobLease pins) : IFrozenDocumentRenderer
    {
        public ProjectId ProjectId => snapshot.ProjectId;
        public long Revision => snapshot.Revision;
        public IReadOnlyCollection<string> PinnedBlobHashes => pins.Hashes;

        public async ValueTask<byte[]> RenderAsync(ExportTileRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.OutputWidth != outputWidth || request.OutputHeight != outputHeight)
                throw new InvalidDataException("Frozen tile dimensions differ from the captured output size.");
            var tile = new byte[checked(request.Width * request.Height * 4)];
            var pending = new Queue<(GpuTerrainRegion Region, Task<byte[]> Pixels)>();
            async Task DrainOneAsync()
            {
                var (region, task) = pending.Dequeue();
                var pixels = await task.ConfigureAwait(false);
                for (var row = 0; row < region.Height; row++)
                    pixels.AsSpan(row * region.Width * 4, region.Width * 4).CopyTo(tile.AsSpan(
                        ((region.Top - request.Top + row) * request.Width + region.Left - request.Left) * 4));
            }
            for (var top = request.Top; top < request.Top + request.Height; top += GpuExportSubTile)
            for (var left = request.Left; left < request.Left + request.Width; left += GpuExportSubTile)
            {
                var region = new GpuTerrainRegion(left, top,
                    Math.Min(GpuExportSubTile, request.Left + request.Width - left),
                    Math.Min(GpuExportSubTile, request.Top + request.Height - top));
                pending.Enqueue((region, renderer.RenderRegionAsync(snapshot, sources, region, cancellationToken)));
                if (pending.Count >= 2) await DrainOneAsync().ConfigureAwait(false);
            }
            while (pending.Count > 0) await DrainOneAsync().ConfigureAwait(false);
            return tile;
        }

        public ValueTask DisposeAsync()
        {
            renderer.Dispose();
            pins.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    public Task<DocumentPngExportResult> ExportPresetAsync(
        MapProject snapshot, string destination, int longestEdge,
        DocumentPngExportOptions? options = null,
        IProgress<DocumentExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (width, height) = DimensionsForPreset(snapshot, longestEdge);
        return ExportAsync(snapshot, destination, width, height, options, progress, cancellationToken);
    }

    public async Task<DocumentPngExportResult> ExportAsync(
        MapProject snapshot,
        string destination,
        int width,
        int height,
        DocumentPngExportOptions? options = null,
        IProgress<DocumentExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (width is < 1 or > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
        options ??= new DocumentPngExportOptions();
        options.Validate();

        var output = ValidateDestination(destination);
        var outputDirectory = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("Export destination has no parent directory.");
        Directory.CreateDirectory(outputDirectory);
        RecheckPublicationTarget(output);
        var temporary = Path.Combine(outputDirectory,
            $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.partial");
        var process = Process.GetCurrentProcess();
        var peakWorkingSet = SampleWorkingSet(process);
        var peakExportBuffer = 0L;
        var tilesRendered = 0;

        try
        {
            await CheckpointAsync(DocumentExportPhase.Preparing, 0, cancellationToken).ConfigureAwait(false);
            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Preparing, 0, 1,
                snapshot.Revision));
            await using var frozen = await _capture(snapshot, width, height, cancellationToken)
                .ConfigureAwait(false);
            if (frozen.ProjectId != snapshot.ProjectId || frozen.Revision != snapshot.Revision)
                throw new InvalidDataException("The captured renderer does not identify the requested immutable revision.");
            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Preparing, 1, 1,
                frozen.Revision));

            var bandHeight = _ledger.FitBandHeight(width, options.TileInterior, options.Halo, 4, 1);
            if (bandHeight <= 0)
                throw new RenderBudgetExceededException("No export band fits the configured 512 MiB buffer envelope.");
            bandHeight = Math.Min(bandHeight, options.TileInterior);
            var totalTiles = checked((long)Math.Ceiling(width / (double)options.TileInterior) *
                                     (long)Math.Ceiling(height / (double)bandHeight));
            var row = new byte[checked(width * 4)];

            using (var writer = new StreamingPngWriter(temporary, width, height))
            {
                for (var interiorTop = 0; interiorTop < height; interiorTop += bandHeight)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var interiorHeight = Math.Min(bandHeight, height - interiorTop);
                    var tiles = new List<RenderedTile>();
                    long bandBytes = row.Length;
                    try
                    {
                        for (var interiorLeft = 0; interiorLeft < width; interiorLeft += options.TileInterior)
                        {
                            var interiorWidth = Math.Min(options.TileInterior, width - interiorLeft);
                            var left = Math.Max(0, interiorLeft - options.Halo);
                            var top = Math.Max(0, interiorTop - options.Halo);
                            var right = Math.Min(width, checked(interiorLeft + interiorWidth + options.Halo));
                            var bottom = Math.Min(height, checked(interiorTop + interiorHeight + options.Halo));
                            var request = new ExportTileRequest(width, height, left, top,
                                right - left, bottom - top, interiorLeft, interiorTop,
                                interiorWidth, interiorHeight);
                            await CheckpointAsync(DocumentExportPhase.Rendering, tilesRendered,
                                cancellationToken).ConfigureAwait(false);
                            var rgba = await frozen.RenderAsync(request, cancellationToken).ConfigureAwait(false);
                            if (rgba.Length != checked(request.Width * request.Height * 4))
                                throw new InvalidDataException("The frozen renderer returned an invalid tile buffer.");
                            bandBytes = checked(bandBytes + rgba.LongLength);
                            _ledger.AdmitOrThrow(0, 0, bandBytes, bandBytes, 0,
                                "Document PNG export band");
                            peakExportBuffer = Math.Max(peakExportBuffer, bandBytes);
                            tiles.Add(new RenderedTile(request, rgba));
                            tilesRendered++;
                            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
                            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Rendering,
                                tilesRendered, totalTiles, frozen.Revision));
                        }

                        for (var localY = 0; localY < interiorHeight; localY++)
                        {
                            var rowOffset = 0;
                            foreach (var tile in tiles)
                            {
                                var sourceY = checked(interiorTop + localY - tile.Request.Top);
                                var sourceX = checked(tile.Request.InteriorLeft - tile.Request.Left);
                                var bytes = checked(tile.Request.InteriorWidth * 4);
                                tile.Rgba.AsSpan(
                                        checked((sourceY * tile.Request.Width + sourceX) * 4), bytes)
                                    .CopyTo(row.AsSpan(rowOffset, bytes));
                                rowOffset += bytes;
                            }
                            if (rowOffset != row.Length)
                                throw new InvalidDataException("Halo crop did not assemble one complete export row.");
                            var completedRows = checked(interiorTop + localY);
                            await CheckpointAsync(DocumentExportPhase.Encoding, completedRows,
                                cancellationToken).ConfigureAwait(false);
                            writer.WriteRgbaRow(row);
                            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Encoding,
                                completedRows + 1L, height, frozen.Revision));
                        }
                    }
                    finally
                    {
                        tiles.Clear();
                    }
                }
                writer.Complete();
            }

            await CheckpointAsync(DocumentExportPhase.Validating, 0, cancellationToken)
                .ConfigureAwait(false);
            var validation = PngValidator.ValidateRgba8(temporary, width, height, cancellationToken,
                (completed, total) => progress?.Report(new DocumentExportProgress(
                    DocumentExportPhase.Validating, completed, total, frozen.Revision)));
            if (!validation.Passed) throw new InvalidDataException(validation.Detail);
            if (!validation.HasSrgbMetadata || !validation.HasStraightAlpha)
                throw new InvalidDataException("PNG colour/alpha metadata was not proven.");

            await CheckpointAsync(DocumentExportPhase.Publishing, 0, cancellationToken)
                .ConfigureAwait(false);
            RecheckPublicationTarget(output);
            PublishAtomically(temporary, output);
            FlushDirectoryBestEffort(outputDirectory);
            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Publishing, 1, 1,
                frozen.Revision));

            await CheckpointAsync(DocumentExportPhase.Completed, 1, cancellationToken)
                .ConfigureAwait(false);
            await using var published = File.OpenRead(output);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(published, cancellationToken)
                    .ConfigureAwait(false))
                .ToLowerInvariant();
            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
            progress?.Report(new DocumentExportProgress(DocumentExportPhase.Completed, 1, 1,
                frozen.Revision));
            return new DocumentPngExportResult(output, frozen.ProjectId, frozen.Revision, width, height,
                tilesRendered, peakExportBuffer, peakWorkingSet, hash, validation,
                frozen.PinnedBlobHashes.ToArray());
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private string ValidateDestination(string destination)
    {
        var output = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document export requires a .png destination.");
        if (_projectDirectory is not null && IsWithin(output, _projectDirectory))
            throw new InvalidOperationException("Export cannot overwrite the project database or immutable blobs.");
        return output;
    }

    private void RecheckPublicationTarget(string output)
    {
        if (_projectDirectory is null) return;
        var parent = new DirectoryInfo(Path.GetDirectoryName(output)!);
        var resolvedParent = parent.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? parent.FullName;
        if (IsWithin(resolvedParent, _projectDirectory))
            throw new InvalidOperationException("Export destination resolves inside the project directory.");
        if (!File.Exists(output)) return;
        var file = new FileInfo(output);
        var resolvedFile = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        if (resolvedFile is not null && IsWithin(resolvedFile, _projectDirectory))
            throw new InvalidOperationException("Export destination link resolves inside the project directory.");
    }

    private static bool IsWithin(string candidate, string root)
    {
        var canonicalCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return string.Equals(canonicalCandidate, canonicalRoot, StringComparison.OrdinalIgnoreCase) ||
               canonicalCandidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask CheckpointAsync(
        DocumentExportPhase phase,
        long completed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_checkpoint is not null)
            await _checkpoint(phase, completed, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void PublishAtomically(string temporary, string destination)
    {
        if (File.Exists(destination))
            File.Replace(temporary, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(temporary, destination, overwrite: false);
    }

    private static void FlushDirectoryBestEffort(string directory)
    {
        try
        {
            using var handle = File.OpenHandle(directory, FileMode.Open, System.IO.FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            RandomAccess.FlushToDisk(handle);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or
                                          PlatformNotSupportedException)
        {
            // The PNG file itself was flushed by StreamingPngWriter. Directory handles are not
            // flushable through every supported .NET/filesystem combination.
        }
    }

    private static long SampleWorkingSet(Process process)
    {
        process.Refresh();
        return process.WorkingSet64;
    }

    private static FrozenDocumentCapture CreateGraphCapture(
        ConnectedTerrainGraph graph,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var root = Path.GetFullPath(projectDirectory);
        return (snapshot, outputWidth, outputHeight, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pins = PinnedBlobLease.Open(root, snapshot);
            try
            {
                IFrozenDocumentRenderer renderer = new GraphFrozenRenderer(
                    snapshot, graph, outputWidth, outputHeight, pins);
                return ValueTask.FromResult(renderer);
            }
            catch
            {
                pins.Dispose();
                throw;
            }
        };
    }

    private sealed record RenderedTile(ExportTileRequest Request, byte[] Rgba);

    private sealed class GraphFrozenRenderer(
        MapProject snapshot,
        ConnectedTerrainGraph graph,
        int outputWidth,
        int outputHeight,
        PinnedBlobLease pins) : IFrozenDocumentRenderer
    {
        public ProjectId ProjectId => snapshot.ProjectId;
        public long Revision => snapshot.Revision;
        public IReadOnlyCollection<string> PinnedBlobHashes => pins.Hashes;

        public ValueTask<byte[]> RenderAsync(ExportTileRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.OutputWidth != outputWidth || request.OutputHeight != outputHeight)
                throw new InvalidDataException("Frozen tile dimensions differ from the captured output size.");
            var frame = graph.EvaluateRegionAtSize(snapshot, outputWidth, outputHeight,
                request.Left, request.Top, request.Width, request.Height);
            cancellationToken.ThrowIfCancellationRequested();
            if (frame.ProjectId != snapshot.ProjectId || frame.Revision != snapshot.Revision)
                throw new InvalidDataException("The graph tile changed frozen project identity.");
            return ValueTask.FromResult(frame.Rgba);
        }

        public ValueTask DisposeAsync()
        {
            pins.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PinnedBlobLease : IDisposable
    {
        private readonly List<FileStream> _streams;

        private PinnedBlobLease(List<FileStream> streams, IReadOnlyCollection<string> hashes)
        {
            _streams = streams;
            Hashes = hashes;
        }

        public IReadOnlyCollection<string> Hashes { get; }

        public static PinnedBlobLease Open(string projectDirectory, MapProject snapshot)
        {
            var hashes = ReferencedBlobHashes(snapshot).ToArray();
            var streams = new List<FileStream>(hashes.Length);
            try
            {
                foreach (var hash in hashes)
                {
                    var path = Path.Combine(projectDirectory, "blobs", hash);
                    var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read,
                        1024 * 1024, FileOptions.SequentialScan);
                    var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (!string.Equals(hash, actual, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Pinned blob hash mismatch for {hash}.");
                    stream.Position = 0;
                    streams.Add(stream);
                }
                return new PinnedBlobLease(streams, hashes);
            }
            catch
            {
                foreach (var stream in streams) stream.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var stream in _streams) stream.Dispose();
        }

        private static IEnumerable<string> ReferencedBlobHashes(MapProject snapshot)
        {
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? hash)
            {
                if (hash is { Length: 64 }) hashes.Add(hash.ToLowerInvariant());
            }

            if (snapshot.ImportedSource is not null)
            {
                Add(snapshot.ImportedSource.SourceBlobHash);
                Add(snapshot.ImportedSource.PreviewBlobHash);
                Add(snapshot.ImportedSource.DisplaySourceBlobHash);
                foreach (var raster in snapshot.ImportedSource.ResolvedRasterReferences) Add(raster.BlobHash);
            }
            foreach (var layer in snapshot.TerrainLayers)
            {
                Add(layer.SourceBlobHash);
                Add(layer.CoverageSourceBlobHash);
                foreach (var stroke in layer.ResolvedTextureStrokes) Add(stroke.Recipe.TextureSha256);
            }
            return hashes.Order(StringComparer.Ordinal);
        }
    }
}
