using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Mapwright.App;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using InkImportService = Mapwright.Core.InkImportService;

namespace Mapwright.Acceptance;

public sealed class GpuExportParityCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("GpuExportParity", GpuExportParityCase.RunAsync);
}

/// <summary>
/// Frozen GPU export gate on the pinned real map with legacy, resolved, Land and river
/// edits: 1K and 4K presets exported by both CPU and GPU captures must decode to pixels
/// within one channel value; the 16K GPU export must validate and match CPU oracle
/// anchors evaluated at 16K sampling. Export duration is an observation only.
/// </summary>
public static class GpuExportParityCase
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
            assertions++;
        }
        Check(ConnectedGpuTerrainRenderer.CanCreate(out var reason), $"GPU export unavailable: {reason}");
        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trialRoot = Path.Combine(ConnectedEvidenceRoot.Resolve(repositoryRoot, "gpu-export-parity"),
            $"trial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(trialRoot);
        var imported = await Task.Run(() => new InkImportService().Import(fixture), cancellationToken);
        var prepared = ConnectedTerrainGraph.PrepareDisplaySource(
            imported.PreviewPng!, imported.PreviewWidth, imported.PreviewHeight);
        var initial = Main.CreateFlattenedSnapshot(imported);
        initial = initial with
        {
            ImportedSource = initial.ImportedSource! with
            {
                DisplaySourceBlobHash = prepared.Sha256,
                DisplaySourceWidth = prepared.Width,
                DisplaySourceHeight = prepared.Height
            }
        };
        var projectRoot = Path.Combine(trialRoot, "real-map.mapwright");
        var repository = new SqliteProjectRepository(projectRoot);
        await repository.CreateImportedAsync(initial, fixture,
            [new ImportedBlobPayload(initial.ImportedSource!.PreviewBlobHash, imported.PreviewPng!, "flattened preview"),
             new ImportedBlobPayload(prepared.Sha256, prepared.Bytes, "immutable display source")],
            cancellationToken);
        var project = Edited(await repository.LoadAsync(initial.ProjectId, cancellationToken));
        Check(project.StorageFormatVersion == 3, "Export parity requires a normalized document.");

        var ledger = RenderResourceLedger.FromSnapshot(RenderResourceLedger.CaptureStartup());
        var observations = new List<object>();
        foreach (var edge in new[] { 1024, 4096 })
        {
            var cpuPath = Path.Combine(trialRoot, $"cpu-{edge}.png");
            var gpuPath = Path.Combine(trialRoot, $"gpu-{edge}.png");
            var cpuTimer = Stopwatch.StartNew();
            var cpu = await new DocumentPngExport(new ConnectedTerrainGraph(projectRoot), ledger, projectRoot)
                .ExportPresetAsync(project, cpuPath, edge, cancellationToken: cancellationToken);
            cpuTimer.Stop();
            var gpuTimer = Stopwatch.StartNew();
            var gpu = await new DocumentPngExport(ledger, DocumentPngExport.CreateGpuCapture(projectRoot), projectRoot)
                .ExportPresetAsync(project, gpuPath, edge, cancellationToken: cancellationToken);
            gpuTimer.Stop();
            Check(cpu.Width == gpu.Width && cpu.Height == gpu.Height && gpu.Validation.Passed,
                $"{edge}: GPU export dimensions or validation differ.");
            var (over, maximum, exact) = ComparePngs(cpuPath, gpuPath);
            Check(over == 0, $"{edge}: {over} channels differ by more than one (max {maximum}).");
            observations.Add(new
            {
                edge, gpu.Width, gpu.Height, cpuSeconds = cpuTimer.Elapsed.TotalSeconds,
                gpuSeconds = gpuTimer.Elapsed.TotalSeconds, identicalPng = cpu.PngSha256 == gpu.PngSha256,
                exactChannels = exact, channelsOverOne = over, maximumDifference = maximum
            });
            GD.Print($"GPU_EXPORT {edge} cpu={cpuTimer.Elapsed.TotalSeconds:F2}s gpu={gpuTimer.Elapsed.TotalSeconds:F2}s " +
                     $"identical={cpu.PngSha256 == gpu.PngSha256} over1={over} max={maximum}");
        }

        // Cancellation mid-render: the previous destination and no partial output survive.
        var guarded = Path.Combine(trialRoot, "guarded.png");
        File.Copy(Path.Combine(trialRoot, "gpu-1024.png"), guarded);
        var guardedHash = Sha256(guarded);
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var cancelled = false;
            try
            {
                await new DocumentPngExport(ledger, DocumentPngExport.CreateGpuCapture(projectRoot), projectRoot)
                    .ExportPresetAsync(project, guarded, 4096,
                        progress: new InlineProgress<DocumentExportProgress>(update =>
                        {
                            if (update.Phase == DocumentExportPhase.Rendering && update.CompletedUnits >= 1)
                                cancel.Cancel();
                        }),
                        cancellationToken: cancel.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            Check(cancelled, "GPU export did not observe cancellation during rendering.");
        }
        // Outstanding asynchronous readbacks complete and release after cancellation.
        for (var frame = 0; frame < 6; frame++) await WaitForFrameAsync();
        Check(Sha256(guarded) == guardedHash, "A cancelled GPU export changed the previous destination.");
        Check(!Directory.EnumerateFiles(trialRoot, ".guarded.png.*").Any(),
            "A cancelled GPU export left partial output behind.");

        // Frozen revision: newer acknowledged revisions keep rendering interactively on the
        // same device while the export runs; the export must still equal revision N.
        var frozenPath = Path.Combine(trialRoot, "gpu-frozen-4096.png");
        using var interactive = ConnectedGpuTerrainRenderer.TryCreate(out var interactiveReason)
            ?? throw new NotSupportedException(interactiveReason);
        var interactiveGraph = new ConnectedTerrainGraph(projectRoot);
        var canvasSize = MapCanvas.ResolveSamplingSize(project, 896);
        var concurrentFrames = new List<double>();
        long previousFrame = 0;
        void ObserveConcurrent()
        {
            var now = Stopwatch.GetTimestamp();
            if (previousFrame != 0) concurrentFrames.Add(Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds);
            previousFrame = now;
        }
        var exportTask = new DocumentPngExport(ledger, DocumentPngExport.CreateGpuCapture(projectRoot), projectRoot)
            .ExportPresetAsync(project, frozenPath, 4096, cancellationToken: cancellationToken);
        var newer = project;
        var interactiveGenerations = 0L;
        RenderingServer.FramePostDraw += ObserveConcurrent;
        try
        {
            while (!exportTask.IsCompleted)
            {
                var foreground = newer.RequireRole(TerrainRole.Foreground);
                newer = newer with
                {
                    Revision = newer.Revision + 1,
                    TerrainLayers = [newer.RequireRole(TerrainRole.Background), foreground with
                    {
                        Strokes = foreground.Strokes.Add(new PaintStroke(StrokeId.New(),
                            [new MapPoint(newer.Width * 0.3, newer.Height * 0.3),
                             new MapPoint(newer.Width * 0.7, newer.Height * 0.6)],
                            new ResolvedBrush(new string('e', 64), 60, 0.7, 0.9, 0.8, 0.2, 0,
                                (int)interactiveGenerations, 1), false, TerrainStrokeKind.Texture))
                    }]
                };
                var generation = interactive.Render(newer,
                    interactiveGraph.PrepareGpuSources(newer, canvasSize.X, canvasSize.Y), ++interactiveGenerations);
                interactive.RetireBefore(generation.Generation);
                await WaitForFrameAsync();
            }
        }
        finally { RenderingServer.FramePostDraw -= ObserveConcurrent; }
        var frozenResult = await exportTask;
        Check(interactiveGenerations > 0, "No interactive generation rendered during the frozen export.");
        Check(frozenResult.FrozenRevision == project.Revision, "The export did not keep its frozen revision.");
        var (frozenOver, frozenMaximum, _) = ComparePngs(Path.Combine(trialRoot, "cpu-4096.png"), frozenPath);
        Check(frozenOver == 0, $"Concurrent edits changed the frozen export ({frozenOver} channels, max {frozenMaximum}).");
        var concurrentOrdered = concurrentFrames.Order().ToArray();
        double ConcurrentPercentile(double fraction) => concurrentOrdered.Length == 0 ? 0 :
            concurrentOrdered[Math.Clamp((int)Math.Ceiling(fraction * concurrentOrdered.Length) - 1, 0,
                concurrentOrdered.Length - 1)];
        observations.Add(new
        {
            scenario = "4096 GPU export while newer revisions render interactively",
            interactiveGenerations,
            frozenRevision = frozenResult.FrozenRevision,
            newestInteractiveRevision = newer.Revision,
            frameIntervalP95 = ConcurrentPercentile(.95),
            frameIntervalP99 = ConcurrentPercentile(.99)
        });
        GD.Print($"GPU_EXPORT_FROZEN generations={interactiveGenerations} frozen={frozenResult.FrozenRevision} " +
                 $"newest={newer.Revision} frameP95={ConcurrentPercentile(.95):F2} frameP99={ConcurrentPercentile(.99):F2}");

        // 16K: GPU export end to end, then CPU oracle anchors at the same sampling.
        var bigPath = Path.Combine(trialRoot, "gpu-16384.png");
        var frameIntervals = new List<double>();
        long lastFrame = 0;
        void ObserveFrame()
        {
            var now = Stopwatch.GetTimestamp();
            if (lastFrame != 0) frameIntervals.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
            lastFrame = now;
        }
        RenderingServer.FramePostDraw += ObserveFrame;
        var bigTimer = Stopwatch.StartNew();
        DocumentPngExportResult big;
        try
        {
            big = await new DocumentPngExport(ledger, DocumentPngExport.CreateGpuCapture(projectRoot), projectRoot)
                .ExportPresetAsync(project, bigPath, 16384, cancellationToken: cancellationToken);
        }
        finally { RenderingServer.FramePostDraw -= ObserveFrame; }
        bigTimer.Stop();
        Check(big.Validation.Passed && Math.Max(big.Width, big.Height) == 16384, "16K GPU export did not validate.");
        Check(big.PeakExportBufferBytes <= ledger.ExportBufferBudgetBytes, "16K GPU export exceeded its buffer budget.");
        var oracle = new ConnectedTerrainGraph(projectRoot);
        var sources = new ConnectedTerrainGraph(projectRoot).PrepareGpuSources(project, big.Width, big.Height);
        using var anchorRenderer = ConnectedGpuTerrainRenderer.CreateForExport()!;
        var anchorOver = 0L;
        var anchors = new[]
        {
            new GpuTerrainRegion(0, 0, 256, 256),
            new GpuTerrainRegion(big.Width / 3, big.Height / 3, 256, 256),
            new GpuTerrainRegion(big.Width / 2 - 128, (int)(big.Height * 0.35) - 128, 256, 256),
            new GpuTerrainRegion(big.Width - 256, big.Height - 256, 256, 256)
        };
        foreach (var anchor in anchors)
        {
            var cpuPixels = oracle.EvaluateRegionAtSize(project, big.Width, big.Height,
                anchor.Left, anchor.Top, anchor.Width, anchor.Height).Rgba;
            var gpuPixels = await anchorRenderer.RenderRegionAsync(project, sources, anchor, cancellationToken);
            for (var index = 0; index < cpuPixels.Length; index++)
                if (Math.Abs(cpuPixels[index] - gpuPixels[index]) > 1) anchorOver++;
        }
        Check(anchorOver == 0, $"16K anchors differ from the CPU oracle in {anchorOver} channels.");
        var ordered = frameIntervals.Order().ToArray();
        double Percentile(double fraction) => ordered.Length == 0 ? 0 :
            ordered[Math.Clamp((int)Math.Ceiling(fraction * ordered.Length) - 1, 0, ordered.Length - 1)];
        observations.Add(new
        {
            edge = 16384, big.Width, big.Height, gpuSeconds = bigTimer.Elapsed.TotalSeconds,
            big.PeakExportBufferBytes, big.PeakProcessWorkingSetBytes, big.PngSha256,
            anchorChannelsOverOne = anchorOver,
            frameIntervalP95 = Percentile(.95), frameIntervalP99 = Percentile(.99), frames = ordered.Length
        });
        GD.Print($"GPU_EXPORT 16384 gpu={bigTimer.Elapsed.TotalSeconds:F2}s frameP95={Percentile(.95):F2} " +
                 $"frameP99={Percentile(.99):F2} peakBuffer={big.PeakExportBufferBytes} anchorsOver1={anchorOver}");
        await File.WriteAllTextAsync(Path.Combine(trialRoot, "gpu-export-parity.json"),
            JsonSerializer.Serialize(new { fixtureSha256 = ConnectedAcceptance.FixtureSha256, observations },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        foreach (var png in Directory.EnumerateFiles(trialRoot, "*.png")) File.Delete(png);
        return assertions;
    }

    private static MapProject Edited(MapProject project)
    {
        var random = new Random(97);
        var background = project.RequireRole(TerrainRole.Background);
        var foreground = project.RequireRole(TerrainRole.Foreground);
        ImmutableArray<MapPoint> Path(int count)
        {
            var builder = ImmutableArray.CreateBuilder<MapPoint>(count);
            var x = project.Width * (0.2 + random.NextDouble() * 0.6);
            var y = project.Height * (0.2 + random.NextDouble() * 0.6);
            for (var index = 0; index < count; index++)
            {
                x = Math.Clamp(x + (random.NextDouble() - 0.5) * 60, 0, project.Width - 1e-6);
                y = Math.Clamp(y + (random.NextDouble() - 0.5) * 60, 0, project.Height - 1e-6);
                builder.Add(new MapPoint(x, y));
            }
            return builder.MoveToImmutable();
        }
        for (var index = 0; index < 6; index++)
            foreground = foreground with
            {
                Strokes = foreground.Strokes.Add(new PaintStroke(StrokeId.New(), Path(40),
                    new ResolvedBrush(new string('c', 64), 48, index % 2 == 0 ? 1 : 0.72, 0.86, 0.8, 0.2, 0,
                        1200 + index, 1), false, TerrainStrokeKind.Texture))
            };
        var presets = TexturePresetCatalog.Inventory;
        for (var index = 0; index < presets.Length; index++)
        {
            var recipe = TexturePresetCatalog.Resolve(presets[index].Id,
                project.ImportedSource!.PreviewBlobHash, 300 + index, 80);
            var samples = Path(25);
            var stroke = TexturePaintStroke.Create(StrokeId.New(), samples, recipe, samples[0]);
            if (index % 2 == 0) background = background with { TextureStrokes = background.ResolvedTextureStrokes.Add(stroke) };
            else foreground = foreground with { TextureStrokes = foreground.ResolvedTextureStrokes.Add(stroke) };
        }
        for (var index = 0; index < 6; index++)
            foreground = foreground with
            {
                LandStrokes = foreground.ResolvedLandStrokes.Add(new LandStroke(StrokeId.New(), Path(8),
                    index % 2 == 0
                        ? ResolvedLandBrush.FromDiameter(LandShape.EdgedPolygon, 70, 0.6, 0.2, 0, 50 + index)
                        : ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 90, 0, 0, 0.4, 60 + index),
                    index % 3 == 2 ? LandOperation.Subtract : LandOperation.Add))
            };
        var river = River.Create(RiverId.New(), foreground.Id,
            [new MapPoint(project.Width * 0.1, project.Height * 0.5), new MapPoint(project.Width * 0.5, project.Height * 0.55),
             new MapPoint(project.Width * 0.9, project.Height * 0.45)], [12d, 30d, 18d], 0.35);
        return project with { TerrainLayers = [background, foreground], River = river };
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static async Task WaitForFrameAsync()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static (long Over, int Maximum, long Exact) ComparePngs(string first, string second)
    {
        using var a = new Image();
        using var b = new Image();
        if (a.LoadPngFromBuffer(File.ReadAllBytes(first)) != Error.Ok ||
            b.LoadPngFromBuffer(File.ReadAllBytes(second)) != Error.Ok)
            throw new InvalidDataException("An exported PNG could not be decoded.");
        a.Convert(Image.Format.Rgba8);
        b.Convert(Image.Format.Rgba8);
        var left = a.GetData();
        var right = b.GetData();
        if (left.Length != right.Length) throw new InvalidDataException("Exported PNG sizes differ.");
        long over = 0, exact = 0;
        var maximum = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var difference = Math.Abs(left[index] - right[index]);
            if (difference == 0) exact++;
            if (difference > 1) over++;
            maximum = Math.Max(maximum, difference);
        }
        return (over, maximum, exact);
    }
}
