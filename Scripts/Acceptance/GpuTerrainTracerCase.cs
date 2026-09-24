using System.Collections.Immutable;
using System.Collections.Concurrent;
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

namespace Mapwright.Acceptance;

public sealed class GpuTerrainTracerCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry)
    {
        registry.Register("GpuTerrainTracer", GpuTerrainTracerCase.RunAsync);
        registry.Register("GpuTerrainStrict", GpuTerrainStrictCase.RunAsync);
    }
}

public sealed record GpuTraceSampleEvidence(
    int Sequence, long Revision, long Generation,
    long InputTimestamp, long DurableTimestamp, long DispatchRecordedTimestamp,
    long DrawTimestamp, long FramePostDrawTimestamp,
    double CommitMilliseconds, double UploadMilliseconds,
    double DispatchRecordingMilliseconds, double InputToVisibleMilliseconds,
    double DrawToPostDrawMilliseconds, string CacheState);

public static class GpuTerrainTracerCase
{
    // This is a physical feasibility counterexample/proof probe, not the
    // Phase 01.1 latency verdict. An explicit incomplete verdict is emitted.
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        await using (var stream = File.OpenRead(fixture))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (hash != ConnectedAcceptance.FixtureSha256)
                throw new InvalidDataException("GPU tracer fixture does not match the pinned real map.");
        }
        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var artifactRoot = Path.Combine(
            ConnectedEvidenceRoot.Resolve(repositoryRoot, "gpu-terrain-tracer"),
            $"trial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);
        var imported = await Task.Run(() => new InkImportService().Import(fixture), cancellationToken);
        if (imported.PreviewPng is not { Length: > 0 })
            throw new InvalidDataException("Pinned real map has no flattened preview.");
        var prepared = ConnectedTerrainGraph.PrepareDisplaySource(
            imported.PreviewPng, imported.PreviewWidth, imported.PreviewHeight);
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
        var projectRoot = Path.Combine(artifactRoot, "real-map.mapwright");
        var repository = new SqliteProjectRepository(projectRoot);
        await repository.CreateImportedAsync(initial, fixture,
            [new ImportedBlobPayload(initial.ImportedSource!.PreviewBlobHash,
                 imported.PreviewPng, "flattened preview"),
             new ImportedBlobPayload(prepared.Sha256, prepared.Bytes,
                 "immutable display source")], cancellationToken);
        var project = await repository.LoadAsync(initial.ProjectId, cancellationToken);
        var graph = new ConnectedTerrainGraph(projectRoot);
        var size = MapCanvas.ResolveSamplingSize(project, 896);
        var baseFrame = graph.EvaluateRegionAtSize(project, size.X, size.Y,
            0, 0, size.X, size.Y);
        var tree = Engine.GetMainLoop() as SceneTree
            ?? throw new InvalidOperationException("GPU tracer requires a scene tree.");
        tree.Root.Size = new Vector2I(1920, 1080);
        var canvas = new MapCanvas { Name = "GpuTerrainTracerCanvas" };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        tree.Root.AddChild(canvas);
        var priorSelection = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER");
        System.Environment.SetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER", "1");
        GpuTerrainTracer? tracer = null;
        Guid epoch = Guid.Empty;
        try
        {
            epoch = canvas.BeginGpuTerrainTracer();
            tracer = new GpuTerrainTracer(size.X, size.Y, baseFrame.Rgba);
            await tracer.Ready.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            var sampleEvidence = new List<GpuTraceSampleEvidence>();
            var frameIntervals = new List<double>();
            long lastFrame = 0;
            void ObserveFrame()
            {
                var now = Stopwatch.GetTimestamp();
                if (lastFrame != 0)
                    frameIntervals.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
                lastFrame = now;
            }
            RenderingServer.FramePostDraw += ObserveFrame;
            var elapsed = Stopwatch.StartNew();
            try
            {
                await using var edit = new EditSession(project, repository, new NullRenderInvalidationQueue());
                var smoke = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_TRACER_SMOKE") == "1";
                for (var sequence = 1; sequence <= (smoke ? 1 : 15); sequence++)
                {
                    var iteration = Stopwatch.StartNew();
                    var samples = BurstSamples(edit.Current, sequence);
                    var brush = new ResolvedBrush(new string('c', 64), 96, 0.72, 0.86, 0.8,
                        0.2, 0, 1200 + sequence, 1);
                    var stroke = new PaintStroke(StrokeId.New(), samples, brush,
                        false, TerrainStrokeKind.Texture);
                    var command = new AddTextureStroke(CommandId.New(), edit.Current.Revision,
                        edit.Current.RequireRole(TerrainRole.Foreground).Id, stroke);
                    var inputTimestamp = Stopwatch.GetTimestamp();
                    var acknowledgement = await edit.ExecuteAsync(command, cancellationToken);
                    var durableTimestamp = Stopwatch.GetTimestamp();
                    project = edit.Current;
                    if (acknowledgement.Revision != project.Revision ||
                        project.RequireRole(TerrainRole.Foreground).Strokes.Length != sequence)
                        throw new InvalidDataException($"GPU tracer sequence {sequence} was not durably acknowledged.");
                    var submission = await tracer.SubmitLegacyAsync(stroke, project.Width, project.Height,
                        project.Revision, generation: sequence, cancellationToken);
                    var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void FrameHandler()
                    {
                        if (canvas.LastDrawnRevision != project.Revision ||
                            canvas.LastDrawnGeneration != submission.Generation ||
                            canvas.LastDrawnTimestamp < submission.DispatchRecordedTimestamp)
                            return;
                        completion.TrySetResult(Stopwatch.GetTimestamp());
                    }
                    RenderingServer.FramePostDraw += FrameHandler;
                    long visibleTimestamp;
                    try
                    {
                        canvas.PresentGpuTerrainTracer(epoch, submission, size);
                        visibleTimestamp = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    finally { RenderingServer.FramePostDraw -= FrameHandler; }
                    sampleEvidence.Add(new GpuTraceSampleEvidence(sequence, project.Revision,
                        submission.Generation, inputTimestamp, durableTimestamp,
                        submission.DispatchRecordedTimestamp, canvas.LastDrawnTimestamp,
                        visibleTimestamp,
                        Stopwatch.GetElapsedTime(inputTimestamp, durableTimestamp).TotalMilliseconds,
                        submission.UploadMilliseconds, submission.DispatchMilliseconds,
                        Stopwatch.GetElapsedTime(inputTimestamp, visibleTimestamp).TotalMilliseconds,
                        Stopwatch.GetElapsedTime(canvas.LastDrawnTimestamp, visibleTimestamp).TotalMilliseconds,
                        sequence == 1 ? "first GPU generation; CPU base and GPU source initialized before input clock" :
                            "warm GPU source and retained previous generation"));
                    // Preserve partial raw sequence evidence even if a later GPU gate fails.
                    await File.WriteAllTextAsync(Path.Combine(artifactRoot, "sequences.json"),
                        JsonSerializer.Serialize(sampleEvidence, new JsonSerializerOptions { WriteIndented = true }),
                        cancellationToken);
                    var remaining = TimeSpan.FromSeconds(4) - iteration.Elapsed;
                    if (!smoke && remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, cancellationToken);
                }
            }
            finally { RenderingServer.FramePostDraw -= ObserveFrame; }
            var gpuPixels = await tracer.ReadbackForValidationAsync(cancellationToken);
            var reference = graph.EvaluateRegionAtSize(project, size.X, size.Y,
                0, 0, size.X, size.Y).Rgba;
            var changed = 0;
            var mismatched = 0;
            var maximumDifference = 0;
            for (var offset = 0; offset < reference.Length; offset += 4)
            {
                if (!gpuPixels.AsSpan(offset, 4).SequenceEqual(baseFrame.Rgba.AsSpan(offset, 4))) changed++;
                for (var channel = 0; channel < 4; channel++)
                {
                    var difference = Math.Abs(gpuPixels[offset + channel] - reference[offset + channel]);
                    maximumDifference = Math.Max(maximumDifference, difference);
                    if (difference > 1) mismatched++;
                }
            }
            var evidence = new
            {
                fixtureSha256 = ConnectedAcceptance.FixtureSha256,
                projectId = project.ProjectId.Value,
                project.Revision,
                width = size.X,
                height = size.Y,
                strokeCount = sampleEvidence.Count,
                samplesPerStroke = 60,
                durationSeconds = elapsed.Elapsed.TotalSeconds,
                sequences = sampleEvidence,
                p95InputToVisibleMilliseconds = Percentile(sampleEvidence.Select(sample => sample.InputToVisibleMilliseconds), .95),
                p95FrameIntervalMilliseconds = Percentile(frameIntervals, .95),
                framesOver33Milliseconds = frameIntervals.Count(value => value > 33.3),
                changedPixels = changed,
                mismatchedChannels = mismatched,
                maximumChannelDifference = maximumDifference,
                endpoint = "matching MapCanvas._Draw followed by FramePostDraw",
                preInputPreparationExcluded = "CPU flattened base evaluation and GPU source/shader initialization occur before sequence 1 input timestamp; no full cold-state claim",
                verdict = changed > 0 && mismatched == 0 &&
                    Percentile(sampleEvidence.Select(sample => sample.InputToVisibleMilliseconds), .95) <= 50 &&
                    elapsed.Elapsed.TotalSeconds >= 60 ?
                    "legacy warm GPU tracer passed; R1 gate incomplete" : "R1 GPU tracer gate failed",
                unverified = new[] { "modern resolved texture with explicit Land coverage",
                    "60-second true cold/evicted/strict matrix",
                    "1K/4K/16K hard-edge precision", "resource retirement under queued draws" }
            };
            var evidencePath = Path.Combine(artifactRoot, "gpu-terrain-tracer.json");
            await File.WriteAllTextAsync(evidencePath,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            GD.Print($"GPU_TERRAIN_TRACER {JsonSerializer.Serialize(evidence)}");
            if (changed <= 0 || mismatched != 0 ||
                Percentile(sampleEvidence.Select(sample => sample.InputToVisibleMilliseconds), .95) > 50 ||
                elapsed.Elapsed.TotalSeconds < 60)
                throw new InvalidDataException($"R1 tracer gate failed: changed={changed}, mismatched={mismatched}, " +
                    $"max={maximumDifference}, p95={Percentile(sampleEvidence.Select(sample => sample.InputToVisibleMilliseconds), .95):F3}ms.");
            return sampleEvidence.Count;
        }
        finally
        {
            if (epoch != Guid.Empty) canvas.EndGpuTerrainTracer(epoch);
            canvas.QueueFree();
            tracer?.Dispose();
            System.Environment.SetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER", priorSelection);
        }
    }

    internal static double Percentile(IEnumerable<double> values, double fraction)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0 ? 0 : ordered[Math.Clamp((int)Math.Ceiling(fraction * ordered.Length) - 1,
            0, ordered.Length - 1)];
    }

    internal static ImmutableArray<MapPoint> BurstSamples(MapProject project, int sequence)
    {
        const int count = 60;
        var builder = ImmutableArray.CreateBuilder<MapPoint>(count);
        for (var index = 0; index < count; index++)
        {
            var progress = index / (double)(count - 1);
            var x = project.Width * (0.08 + progress * 0.84);
            var wave = Math.Sin((index + (sequence - 1) * 7) * 0.19) * project.Height * 0.08;
            builder.Add(new MapPoint(x, Math.Clamp(project.Height * 0.35 + wave, 0,
                project.Height - 1)));
        }
        return builder.MoveToImmutable();
    }
}

public sealed class StrictGpuSampleEvidence
{
    public int Sequence { get; init; }
    public long Revision { get; set; }
    public long Generation { get; set; }
    public long InputTimestamp { get; set; }
    public long? DurableTimestamp { get; set; }
    public long? SourceReadVerifiedTimestamp { get; set; }
    public long? GpuReadyTimestamp { get; set; }
    public long? DispatchRecordedTimestamp { get; set; }
    public long? DrawTimestamp { get; set; }
    public long? FramePostDrawTimestamp { get; set; }
    public double SourceReadVerifyMilliseconds { get; set; }
    public double SourceUploadMilliseconds { get; set; }
    public double ShaderCompileMilliseconds { get; set; }
    public double OutputUploadMilliseconds { get; set; }
    public double DispatchRecordingMilliseconds { get; set; }
    public double InputToVisibleMilliseconds { get; set; }
    public double CommandPreparationMilliseconds { get; set; }
    public double EditSessionOverheadMilliseconds { get; set; }
    public double QueueAndApplyMilliseconds { get; set; }
    public double PostCommitDeliveryMilliseconds { get; set; }
    public DurableCommitStageProfile? RepositoryCommitStages { get; set; }
    public long EstimatedGpuBytes { get; set; }
    public long ProcessPeakWorkingSetBytes { get; set; }
    public int? ParityChangedPixels { get; set; }
    public int? ParityChannelsOverOne { get; set; }
    public int? ParityMaximumDifference { get; set; }
    public string Status { get; set; } = "started";
    public string? Failure { get; set; }
}

/// <summary>
/// Every revision starts from the immutable prepared blob, not a previous GPU
/// output. Source read, verification, GPU upload and every stroke replay are
/// after the per-revision input timestamp. The previous RID is display-only.
/// </summary>
public static class GpuTerrainStrictCase
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        await using (var stream = File.OpenRead(fixture))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (actual != ConnectedAcceptance.FixtureSha256)
                throw new InvalidDataException("Strict GPU tracer fixture is not the pinned real map.");
        }
        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trialRoot = Path.Combine(ConnectedEvidenceRoot.Resolve(repositoryRoot, "gpu-terrain-strict"),
            $"trial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(trialRoot);
        var imported = await Task.Run(() => new InkImportService().Import(fixture), cancellationToken);
        if (imported.PreviewPng is not { Length: > 0 })
            throw new InvalidDataException("Pinned real map has no flattened preview.");
        var prepared = ConnectedTerrainGraph.PrepareDisplaySource(
            imported.PreviewPng, imported.PreviewWidth, imported.PreviewHeight);
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
        var commitProfiles = new ConcurrentDictionary<long, DurableCommitStageProfile>();
        repository.CommitProfileObserver = profile => commitProfiles[profile.Revision] = profile;
        await repository.CreateImportedAsync(initial, fixture,
            [new ImportedBlobPayload(initial.ImportedSource!.PreviewBlobHash,
                 imported.PreviewPng, "flattened preview"),
             new ImportedBlobPayload(prepared.Sha256, prepared.Bytes,
                 "immutable display source")], cancellationToken);
        var project = await repository.LoadAsync(initial.ProjectId, cancellationToken);
        var size = MapCanvas.ResolveSamplingSize(project, 896);
        if (size.X != prepared.Width || size.Y != prepared.Height)
            throw new InvalidDataException("Strict source and visible raster dimensions disagree.");
        var sourceHash = project.ImportedSource?.DisplaySourceBlobHash
            ?? throw new InvalidDataException("Durable strict source identity is missing.");
        var graph = new ConnectedTerrainGraph(projectRoot);
        var tree = Engine.GetMainLoop() as SceneTree
            ?? throw new InvalidOperationException("Strict GPU tracer requires a scene tree.");
        tree.Root.Size = new Vector2I(1920, 1080);
        var canvas = new MapCanvas { Name = "GpuTerrainStrictCanvas" };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        tree.Root.AddChild(canvas);
        var previousSelection = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER");
        System.Environment.SetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER", "1");
        var epoch = canvas.BeginGpuTerrainTracer();
        GpuTerrainTracer? front = null;
        var sequences = new List<StrictGpuSampleEvidence>();
        var frames = new List<double>();
        long lastFrame = 0;
        void ObserveFrame()
        {
            var now = Stopwatch.GetTimestamp();
            if (lastFrame != 0)
                frames.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
            lastFrame = now;
        }
        var hardware = RenderResourceLedger.CaptureStartup();
        var ledger = RenderResourceLedger.FromSnapshot(hardware);
        var freshKernelEachInput = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_STRICT_FRESH_KERNEL") == "1";
        GpuTerrainKernel? sharedKernel = null;
        var kernelSetupStart = Stopwatch.GetTimestamp();
        double kernelSetupMilliseconds;
        double serializationSetupMilliseconds;
        double rehearsalMilliseconds = 0;
        try
        {
            if (!freshKernelEachInput)
            {
                sharedKernel = new GpuTerrainKernel();
                await sharedKernel.Ready.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            kernelSetupMilliseconds = Stopwatch.GetElapsedTime(kernelSetupStart).TotalMilliseconds;
            // Opening a repository starts this in the background; ensure it finished
            // before timing and report its one-time cost as declared setup.
            serializationSetupMilliseconds = SqliteProjectRepository.PrepareCommandSerialization();
            // An editor already displays the imported revision before its first
            // stroke. Keep that initial front only for display; each measured
            // candidate still rereads, verifies, uploads and replays from source.
            var baselinePixels = GpuTerrainTracer.ReadVerifiedPreparedSource(
                projectRoot, sourceHash, size.X, size.Y);
            front = new GpuTerrainTracer(size.X, size.Y, baselinePixels, sharedKernel);
            await front.Ready.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            var baselineSubmission = await front.SubmitSourceDisplayAsync(project.Revision, 1, cancellationToken);
            var baselineVisible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void BaselineFrame()
            {
                if (canvas.LastDrawnRevision == project.Revision &&
                    canvas.LastDrawnGeneration == 1 &&
                    canvas.LastDrawnTimestamp >= baselineSubmission.DispatchRecordedTimestamp)
                    baselineVisible.TrySetResult();
            }
            RenderingServer.FramePostDraw += BaselineFrame;
            try
            {
                canvas.PresentGpuTerrainTracer(epoch, baselineSubmission, size);
                await baselineVisible.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            finally { RenderingServer.FramePostDraw -= BaselineFrame; }
        }
        catch
        {
            canvas.EndGpuTerrainTracer(epoch);
            canvas.QueueFree();
            front?.Dispose();
            sharedKernel?.Dispose();
            System.Environment.SetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER", previousSelection);
            throw;
        }
        RenderingServer.FramePostDraw += ObserveFrame;
        var run = Stopwatch.StartNew();
        try
        {
            await using var edit = new EditSession(project, repository, new NullRenderInvalidationQueue());
            // The editor rehearses discarded stroke commands when a session binds
            // (MapCanvas.BindConnectedSession). Mirror that with a representative,
            // never-committed command before the timed run; report it as setup.
            var rehearsalSamples = GpuTerrainTracerCase.BurstSamples(project, 0);
            rehearsalMilliseconds = await edit.RehearseAsync(
            [
                new AddTextureStroke(CommandId.New(), project.Revision,
                    project.RequireRole(TerrainRole.Foreground).Id,
                    new PaintStroke(StrokeId.New(), rehearsalSamples,
                        new ResolvedBrush(new string('c', 64), 96, 0.72, 0.86, 0.8, 0.2, 0, 1200, 1),
                        false, TerrainStrokeKind.Texture))
            ]);
            if (edit.Current.Revision != project.Revision)
                throw new InvalidDataException("Rehearsal must not change durable state.");
            var smoke = System.Environment.GetEnvironmentVariable("MAPWRIGHT_GPU_STRICT_SMOKE") == "1";
            for (var sequence = 1; sequence <= (smoke ? 1 : 15); sequence++)
            {
                var iteration = Stopwatch.StartNew();
                var sample = new StrictGpuSampleEvidence
                {
                    Sequence = sequence
                };
                GpuTerrainTracer? candidate = null;
                try
                {
                    var samples = GpuTerrainTracerCase.BurstSamples(edit.Current, sequence);
                    var brush = new ResolvedBrush(new string('c', 64), 96, 0.72, 0.86, 0.8,
                        0.2, 0, 1200 + sequence, 1);
                    var stroke = new PaintStroke(StrokeId.New(), samples, brush,
                        false, TerrainStrokeKind.Texture);
                    var previous = edit.Current;
                    var command = new AddTextureStroke(CommandId.New(), previous.Revision,
                        previous.RequireRole(TerrainRole.Foreground).Id, stroke);
                    // Match the inherited GPU case: gesture samples and the
                    // command already exist when the edit clock begins.
                    sample.InputTimestamp = Stopwatch.GetTimestamp();
                    // Prepare a private candidate while the transaction is in flight.
                    // EditSession applies the command once and shares that projected
                    // change before its durable commit; a speculative texture is never
                    // handed to the canvas until a matching acknowledgement arrives.
                    var preparedEdit = new TaskCompletionSource<(PreparedEdit Edit, long At)>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var executeStarted = Stopwatch.GetTimestamp();
                    var commitTask = Task.Run(async () =>
                    {
                        var acknowledged = await edit.ExecuteAsync(command,
                            prepared => preparedEdit.TrySetResult((prepared, Stopwatch.GetTimestamp())),
                            cancellationToken);
                        return (Acknowledgement: acknowledged, CompletedAt: Stopwatch.GetTimestamp());
                    }, cancellationToken);

                    // Nothing from `front` is passed to this fresh renderer.
                    var readStart = Stopwatch.GetTimestamp();
                    var sourcePixels = GpuTerrainTracer.ReadVerifiedPreparedSource(
                        projectRoot, sourceHash, size.X, size.Y);
                    sample.SourceReadVerifiedTimestamp = Stopwatch.GetTimestamp();
                    sample.SourceReadVerifyMilliseconds = Stopwatch.GetElapsedTime(
                        readStart, sample.SourceReadVerifiedTimestamp.Value).TotalMilliseconds;
                    candidate = new GpuTerrainTracer(size.X, size.Y, sourcePixels, sharedKernel);
                    await candidate.Ready.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    sample.GpuReadyTimestamp = Stopwatch.GetTimestamp();
                    sample.SourceUploadMilliseconds = candidate.SourceUploadMilliseconds;
                    sample.ShaderCompileMilliseconds = candidate.ShaderCompileMilliseconds;

                    // Rendering is single-threaded: an awaited suspension here would
                    // defer the continuation to the next process frame. Take the shared
                    // projection synchronously; its wait stays inside the edit clock.
                    var firstDone = Task.WhenAny(preparedEdit.Task, commitTask);
                    if (!firstDone.Wait(TimeSpan.FromSeconds(30), cancellationToken))
                        throw new TimeoutException($"Strict sequence {sequence} was not prepared.");
                    if (!preparedEdit.Task.IsCompleted)
                    {
                        commitTask.GetAwaiter().GetResult();
                        throw new InvalidDataException($"Strict sequence {sequence} committed without a prepared change.");
                    }
                    var (shared, preparedAt) = preparedEdit.Task.Result;
                    if (shared.CommandId != command.Id || shared.BaseRevision != previous.Revision)
                        throw new InvalidDataException("Prepared change does not belong to the measured command.");
                    var projected = shared.Change.Project;
                    sample.CommandPreparationMilliseconds = Stopwatch.GetElapsedTime(
                        executeStarted, preparedAt).TotalMilliseconds;

                    var projectedStrokes = projected.RequireRole(TerrainRole.Foreground).Strokes;
                    if (projectedStrokes.Any(item => item.Kind != TerrainStrokeKind.Texture))
                        throw new NotSupportedException("Strict tracer does not silently omit non-texture commands.");
                    var submission = await candidate.SubmitLegacyReplayAsync(projectedStrokes,
                        projected.Width, projected.Height, projected.Revision, sequence + 1, cancellationToken);
                    sample.Generation = submission.Generation;
                    sample.OutputUploadMilliseconds = submission.UploadMilliseconds;
                    sample.DispatchRecordingMilliseconds = submission.DispatchMilliseconds;
                    sample.DispatchRecordedTimestamp = submission.DispatchRecordedTimestamp;
                    var (acknowledgement, acknowledgedAt) = await commitTask;
                    sample.DurableTimestamp = acknowledgedAt;
                    project = edit.Current;
                    sample.Revision = project.Revision;
                    if (!commitProfiles.TryGetValue(project.Revision, out var commitProfile))
                        throw new InvalidDataException($"Strict sequence {sequence} has no repository stage profile.");
                    sample.RepositoryCommitStages = commitProfile;
                    sample.EditSessionOverheadMilliseconds = Stopwatch.GetElapsedTime(
                        executeStarted, acknowledgedAt).TotalMilliseconds - commitProfile.TotalMilliseconds;
                    sample.QueueAndApplyMilliseconds = Stopwatch.GetElapsedTime(
                        executeStarted, commitProfile.StartedTimestamp).TotalMilliseconds;
                    sample.PostCommitDeliveryMilliseconds = Stopwatch.GetElapsedTime(
                        commitProfile.CommittedTimestamp, acknowledgedAt).TotalMilliseconds;
                    if (acknowledgement.CommandId != command.Id ||
                        acknowledgement.Revision != projected.Revision ||
                        project.Revision != projected.Revision ||
                        !project.RequireRole(TerrainRole.Foreground).Strokes.SequenceEqual(projectedStrokes) ||
                        projectedStrokes.Length != sequence ||
                        sample.Generation != sequence + 1 || submission.Revision != project.Revision)
                        throw new InvalidDataException("Speculative strict replay does not match the durable acknowledgement.");
                    var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void FrameHandler()
                    {
                        if (canvas.LastDrawnRevision != sample.Revision ||
                            canvas.LastDrawnGeneration != sample.Generation ||
                            canvas.LastDrawnTimestamp < sample.DispatchRecordedTimestamp)
                            return;
                        completion.TrySetResult(Stopwatch.GetTimestamp());
                    }
                    RenderingServer.FramePostDraw += FrameHandler;
                    try
                    {
                        canvas.PresentGpuTerrainTracer(epoch, submission, size);
                        sample.FramePostDrawTimestamp = await completion.Task.WaitAsync(
                            TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    finally { RenderingServer.FramePostDraw -= FrameHandler; }
                    sample.DrawTimestamp = canvas.LastDrawnTimestamp;
                    sample.InputToVisibleMilliseconds = Stopwatch.GetElapsedTime(
                        sample.InputTimestamp, sample.FramePostDrawTimestamp.Value).TotalMilliseconds;
                    // Each lease holds one source and at most two scratch images.
                    sample.EstimatedGpuBytes = checked((long)size.X * size.Y * 4 *
                        (3 + (front is null ? 0 : 3)));
                    sample.ProcessPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
                    sample.Status = "presented";

                    // Sentinel parity is deliberately after FramePostDraw, never
                    // inside input-to-visible or as a source for the next generation.
                    if (sequence is 1 or 8 or 15)
                    {
                        RenderingServer.FramePostDraw -= ObserveFrame;
                        try
                        {
                            var gpu = await candidate.ReadbackForValidationAsync(cancellationToken);
                            var oracle = graph.EvaluateRegionAtSize(project, size.X, size.Y,
                                0, 0, size.X, size.Y).Rgba;
                            var changed = 0;
                            var bad = 0;
                            var maximum = 0;
                            for (var offset = 0; offset < oracle.Length; offset += 4)
                            {
                                if (!gpu.AsSpan(offset, 4).SequenceEqual(sourcePixels.AsSpan(offset, 4))) changed++;
                                for (var channel = 0; channel < 4; channel++)
                                {
                                    var difference = Math.Abs(gpu[offset + channel] - oracle[offset + channel]);
                                    maximum = Math.Max(maximum, difference);
                                    if (difference > 1) bad++;
                                }
                            }
                            sample.ParityChangedPixels = changed;
                            sample.ParityChannelsOverOne = bad;
                            sample.ParityMaximumDifference = maximum;
                            if (changed == 0 || bad != 0)
                                throw new InvalidDataException($"Strict sequence {sequence} parity failed: changed={changed}, channels={bad}, max={maximum}.");
                        }
                        finally
                        {
                            lastFrame = 0;
                            RenderingServer.FramePostDraw += ObserveFrame;
                        }
                    }
                    var oldFront = front;
                    front = candidate;
                    candidate = null;
                    oldFront?.Dispose();
                }
                catch (Exception exception)
                {
                    sample.Status = sample.FramePostDrawTimestamp is null ? "failed-or-stalled-before-visible" :
                        "failed-after-visible-validation";
                    sample.Failure = exception.ToString();
                    candidate?.Dispose();
                    throw;
                }
                finally
                {
                    sequences.Add(sample);
                    await File.WriteAllTextAsync(Path.Combine(trialRoot, "sequences.json"),
                        JsonSerializer.Serialize(sequences, new JsonSerializerOptions { WriteIndented = true }),
                        cancellationToken);
                }
                var remaining = TimeSpan.FromSeconds(4) - iteration.Elapsed;
                if (!smoke && remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, cancellationToken);
            }

            var latencies = sequences.Select(item => item.InputToVisibleMilliseconds).ToArray();
            var missing = sequences.Count(item => item.FramePostDrawTimestamp is null);
            var stalls = sequences.Count(item => item.InputToVisibleMilliseconds > 100);
            var resourcePass = sequences.All(item => item.EstimatedGpuBytes <= ledger.GpuBudgetBytes &&
                item.ProcessPeakWorkingSetBytes <= ledger.ProcessBudgetBytes);
            var complete = sequences.Count == 15 && run.Elapsed.TotalSeconds >= 60 && missing == 0;
            var latencyPass = GpuTerrainTracerCase.Percentile(latencies, .95) <= 50 &&
                GpuTerrainTracerCase.Percentile(latencies, .99) <= 100;
            var framePass = GpuTerrainTracerCase.Percentile(frames, .95) <= 20 &&
                GpuTerrainTracerCase.Percentile(frames, .99) <= 33.3;
            var evidence = new
            {
                fixtureSha256 = ConnectedAcceptance.FixtureSha256,
                sourceSha256 = sourceHash,
                projectId = project.ProjectId.Value,
                width = size.X,
                height = size.Y,
                windowWidth = 1920,
                windowHeight = 1080,
                durationSeconds = run.Elapsed.TotalSeconds,
                sequences,
                inputToVisible = new
                {
                    p50 = GpuTerrainTracerCase.Percentile(latencies, .5),
                    p95 = GpuTerrainTracerCase.Percentile(latencies, .95),
                    p99 = GpuTerrainTracerCase.Percentile(latencies, .99)
                },
                frameInterval = new
                {
                    p50 = GpuTerrainTracerCase.Percentile(frames, .5),
                    p95 = GpuTerrainTracerCase.Percentile(frames, .95),
                    p99 = GpuTerrainTracerCase.Percentile(frames, .99)
                },
                missing, stalls,
                gpuBudgetBytes = ledger.GpuBudgetBytes,
                processBudgetBytes = ledger.ProcessBudgetBytes,
                maximumEstimatedGpuBytes = sequences.Max(item => item.EstimatedGpuBytes),
                maximumProcessPeakBytes = sequences.Max(item => item.ProcessPeakWorkingSetBytes),
                hardware,
                kernelPolicy = freshKernelEachInput ? "fresh shader and pipeline per input" :
                    "shader and pipeline initialized once before the run; no source or render pixels retained in kernel",
                kernelSetupMilliseconds,
                kernelCompileMilliseconds = sharedKernel?.CompileMilliseconds,
                serializationSetupMilliseconds,
                rehearsalMilliseconds,
                connectionPolicy = "EditSession holds one repository connection for the run (production behaviour); commits remain synchronous=FULL WAL commits",
                rehearsalPolicy = "one discarded, never-committed AddTextureStroke Apply and payload serialization before the timed run, matching MapCanvas session binding; no pixels, textures or durable rows",
                commandPreparationPolicy = "EditSession applies each command once and shares that projected change before its durable commit; preparation is measured from ExecuteAsync to the shared change",
                endpoint = "input before durable commit and immutable source read through all GPU replay to matching _Draw and FramePostDraw",
                sourcePolicy = "read and SHA-verify immutable MWDS blob after each input; fresh global-device source/render textures; previous front retained only as display lease",
                initialDisplayPolicy = "imported revision zero was presented before timed edits as a display-only front; no front RID or pixels enter a candidate",
                shaderCachePolicy = "Godot process-wide compiled shader cache may remain; setup/compile time is reported separately when a shared kernel is selected",
                passed = complete && latencyPass && framePass && resourcePass && stalls == 0,
                unverified = new[] { "modern resolved texture and Land", "1K/4K/16K hard-edge precision",
                    "GPU export", "driver-visible allocation accounting and queued-draw retirement stress" }
            };
            await File.WriteAllTextAsync(Path.Combine(trialRoot, "gpu-terrain-strict.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            GD.Print($"GPU_TERRAIN_STRICT {JsonSerializer.Serialize(new
            {
                evidence.projectId, evidence.durationSeconds, evidence.inputToVisible,
                evidence.frameInterval, evidence.missing, evidence.stalls,
                evidence.maximumEstimatedGpuBytes, evidence.maximumProcessPeakBytes,
                evidence.passed, trialRoot
            })}");
            if (!evidence.passed)
                throw new InvalidDataException("Strict GPU reconstruction failed at least one unchanged physical gate; raw evidence was preserved.");
            return sequences.Count;
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(trialRoot, "failure.json"),
                JsonSerializer.Serialize(new { reason = exception.ToString(), completed = sequences.Count,
                    durationSeconds = run.Elapsed.TotalSeconds },
                    new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
            throw;
        }
        finally
        {
            RenderingServer.FramePostDraw -= ObserveFrame;
            canvas.EndGpuTerrainTracer(epoch);
            canvas.QueueFree();
            front?.Dispose();
            sharedKernel?.Dispose();
            System.Environment.SetEnvironmentVariable("MAPWRIGHT_GPU_TERRAIN_TRACER", previousSelection);
        }
    }
}
