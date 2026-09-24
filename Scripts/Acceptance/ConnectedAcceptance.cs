using Godot;
using Mapwright.App;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using InkImportService = Mapwright.Core.InkImportService;

namespace Mapwright.Acceptance;

public sealed record AcceptanceInputEvent(long SequenceId, long Timestamp, string Command, long BaseRevision);

public sealed record AcceptancePresentationEvent(
    long SequenceId,
    long CommittedRevision,
    long PresentedRevision,
    long Timestamp);

public sealed record AcceptancePercentiles(double P50Milliseconds, double P95Milliseconds, double P99Milliseconds);

public sealed record AcceptanceMetricResult(
    bool Passed,
    string? FailureCode,
    int SampleCount,
    int ExcludedCount,
    double? P50Milliseconds,
    double? P95Milliseconds,
    double? P99Milliseconds,
    string Endpoint,
    IReadOnlyList<long> CorrelatedSequenceIds,
    string Detail);

public static class AcceptanceMetricEngine
{
    public const string LatencyEndpoint =
        "input-monotonic-timestamp to first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture; display scanout excluded";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string EvaluateJson(string requestJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        var kind = root.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() : null;
        object result = kind switch
        {
            "latency" => EvaluateLatencyJson(root),
            "stages" => EvaluateStagesJson(root),
            "schema" => EvaluateSchema(root),
            "resources" => EvaluateResources(root),
            _ => Failure("unknown-evaluation-kind", "The self-test request kind is unknown.")
        };
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public static AcceptanceMetricResult EvaluateLatency(
        IReadOnlyList<AcceptanceInputEvent>? inputs,
        IReadOnlyList<AcceptancePresentationEvent>? presentations,
        long timestampFrequency)
    {
        if (inputs is null || presentations is null)
            return Failure("missing-samples", "Input or presentation samples are missing.");
        if (inputs.Count == 0 || presentations.Count == 0)
            return Failure("empty-samples", "Input or presentation samples are empty.");
        if (timestampFrequency <= 0)
            return Failure("invalid-clock-frequency", "The monotonic timestamp frequency is invalid.");

        var duplicateInput = inputs.GroupBy(sample => sample.SequenceId).FirstOrDefault(group => group.Count() != 1);
        var duplicatePresentation = presentations.GroupBy(sample => sample.SequenceId)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateInput is not null || duplicatePresentation is not null)
            return Failure("duplicate-sequence-id", "A sequence ID appears more than once.");

        var inputById = inputs.ToDictionary(sample => sample.SequenceId);
        var presentationById = presentations.ToDictionary(sample => sample.SequenceId);
        if (presentationById.Keys.Any(id => !inputById.ContainsKey(id)))
            return Failure("uncorrelated-samples", "A presentation has no matching input sequence ID.");
        if (inputById.Keys.Any(id => !presentationById.ContainsKey(id)))
            return Failure("dropped-input-ids", "One or more input sequence IDs were never presented.");

        var orderedInputs = inputs.OrderBy(sample => sample.Timestamp).ThenBy(sample => sample.SequenceId).ToArray();
        var values = new List<double>(orderedInputs.Length);
        var sequences = new List<long>(orderedInputs.Length);
        foreach (var input in orderedInputs)
        {
            var presented = presentationById[input.SequenceId];
            var revisionMatches = input.Command switch
            {
                "Undo" => input.BaseRevision > 0 &&
                          presented.CommittedRevision == input.BaseRevision - 1,
                "Redo" => presented.CommittedRevision == input.BaseRevision + 1,
                _ => presented.CommittedRevision > input.BaseRevision
            };
            if (!revisionMatches ||
                presented.PresentedRevision != presented.CommittedRevision ||
                presented.Timestamp < input.Timestamp)
                return Failure("uncorrelated-samples",
                    "A presentation does not identify the expected navigation/edit revision or precedes its input.");
            values.Add((presented.Timestamp - input.Timestamp) * 1000d / timestampFrequency);
            sequences.Add(input.SequenceId);
        }

        var percentiles = CalculatePercentiles(values);
        return new AcceptanceMetricResult(true, null, values.Count, 0,
            percentiles.P50Milliseconds, percentiles.P95Milliseconds, percentiles.P99Milliseconds,
            LatencyEndpoint, sequences,
            "Nearest-rank percentiles over complete one-to-one sequence/revision correlations; no samples excluded.");
    }

    public static AcceptancePercentiles CalculatePercentiles(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) throw new InvalidOperationException("Percentiles require at least one sample.");
        if (values.Any(value => !double.IsFinite(value) || value < 0))
            throw new InvalidDataException("Percentile samples must be finite non-negative values.");
        var ordered = values.Order().ToArray();
        return new AcceptancePercentiles(
            NearestRank(ordered, 0.50), NearestRank(ordered, 0.95), NearestRank(ordered, 0.99));
    }

    private static object EvaluateLatencyJson(JsonElement root)
    {
        if (!root.TryGetProperty("inputs", out var inputElement) ||
            !root.TryGetProperty("presentations", out var presentationElement))
            return Failure("missing-samples", "Input or presentation samples are missing.");
        var inputs = inputElement.EnumerateArray().Select(value => new AcceptanceInputEvent(
            value.GetProperty("sequenceId").GetInt64(), value.GetProperty("timestamp").GetInt64(),
            value.TryGetProperty("command", out var command) ? command.GetString() ?? "self-test" : "self-test",
            value.TryGetProperty("baseRevision", out var revision) ? revision.GetInt64() : 0)).ToArray();
        var presentations = presentationElement.EnumerateArray().Select(value => new AcceptancePresentationEvent(
            value.GetProperty("sequenceId").GetInt64(), value.GetProperty("committedRevision").GetInt64(),
            value.GetProperty("presentedRevision").GetInt64(), value.GetProperty("timestamp").GetInt64())).ToArray();
        var frequency = root.TryGetProperty("timestampFrequency", out var frequencyElement)
            ? frequencyElement.GetInt64()
            : 1000;
        return EvaluateLatency(inputs, presentations, frequency);
    }

    private static object EvaluateStagesJson(JsonElement root)
    {
        if (!root.TryGetProperty("presentations", out var presented) ||
            !root.TryGetProperty("stages", out var stageValues))
            return Failure("missing-stage-readings", "Presentation or stage records are missing.");
        var presentations = presented.EnumerateArray().Select(value => new AcceptancePresentationEvent(
            value.GetProperty("sequenceId").GetInt64(), value.GetProperty("committedRevision").GetInt64(),
            value.GetProperty("presentedRevision").GetInt64(), value.GetProperty("timestamp").GetInt64())).ToArray();
        var stages = stageValues.EnumerateArray().Select(value => new AcceptanceStageEvidence(
            value.GetProperty("sequenceId").GetInt64(), value.GetProperty("revision").GetInt64(),
            value.GetProperty("drawGeneration").GetInt64(),
            value.TryGetProperty("commitMilliseconds", out var commit) && commit.ValueKind != JsonValueKind.Null
                ? commit.GetDouble() : null,
            value.GetProperty("referenceRenderMilliseconds").GetDouble(),
            value.GetProperty("pngEncodeMilliseconds").GetDouble(),
            value.GetProperty("textureUploadMilliseconds").GetDouble(),
            value.GetProperty("drawToPostDrawMilliseconds").GetDouble())).ToArray();
        var (passed, code) = ValidateStages(presentations, stages);
        return passed ? Success("Every presentation has one complete, revision-matched stage record.")
            : Failure(code!, "Stage readings are missing, invalid, or uncorrelated.");
    }

    public static (bool Passed, string? FailureCode) ValidateStages(
        IReadOnlyList<AcceptancePresentationEvent> presentations,
        IReadOnlyList<AcceptanceStageEvidence> stages)
    {
        if (presentations.Count == 0 || stages.Count == 0)
            return (false, "missing-stage-readings");
        if (presentations.GroupBy(value => value.SequenceId).Any(group => group.Count() != 1) ||
            stages.GroupBy(value => value.SequenceId).Any(group => group.Count() != 1))
            return (false, "duplicate-stage-sequence");
        if (presentations.Count != stages.Count)
            return (false, "uncorrelated-stage-readings");
        var byId = stages.ToDictionary(value => value.SequenceId);
        foreach (var presented in presentations)
        {
            if (!byId.TryGetValue(presented.SequenceId, out var stage) ||
                stage.Revision != presented.PresentedRevision || stage.DrawGeneration <= 0)
                return (false, "uncorrelated-stage-readings");
            var values = new[] { stage.CommitMilliseconds, stage.ReferenceRenderMilliseconds,
                stage.PngEncodeMilliseconds, stage.TextureUploadMilliseconds,
                stage.DrawToPostDrawMilliseconds };
            if (values.Any(value => value is null || !double.IsFinite(value.Value) || value.Value < 0))
                return (false, "missing-stage-readings");
        }
        return (true, null);
    }

    private static object EvaluateSchema(JsonElement root)
    {
        if (!root.TryGetProperty("runs", out var runsElement))
            return Failure("missing-scenario-cache-run", "Scenario/cache runs are missing.");
        var expectedScenarios = new HashSet<string>(
            ["PaintAcrossTiles", "PanZoomWhilePainting", "RiverEdit", "UndoRedo"], StringComparer.Ordinal);
        var expectedCaches = new HashSet<string>(["Warm", "Cold", "Evicted"], StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in runsElement.EnumerateArray())
        {
            var scenario = run.GetProperty("scenario").GetString() ?? string.Empty;
            var cache = run.GetProperty("cache").GetString() ?? string.Empty;
            if (!expectedScenarios.Contains(scenario) || !expectedCaches.Contains(cache))
                return Failure("unknown-scenario-cache-run", $"Unknown run {scenario}/{cache}.");
            if (!keys.Add($"{scenario}/{cache}"))
                return Failure("duplicate-scenario-cache-run", $"Duplicate run {scenario}/{cache}.");
            if (run.GetProperty("durationSeconds").GetDouble() < 60 ||
                run.GetProperty("assertionCount").GetInt32() <= 0)
                return Failure("short-or-zero-assertion-run", $"Run {scenario}/{cache} lacks duration or assertions.");
        }
        var expected = from scenario in expectedScenarios from cache in expectedCaches select $"{scenario}/{cache}";
        if (expected.Any(key => !keys.Contains(key)))
            return Failure("missing-scenario-cache-run", "At least one required scenario/cache run is missing.");
        return Success("All four scenarios are present once in warm, cold, and evicted cache states.");
    }

    private static object EvaluateResources(JsonElement root)
    {
        long Value(string name) => root.TryGetProperty(name, out var element) ? element.GetInt64() : 0;
        bool Measured(string name, long value) => root.TryGetProperty(name, out var element)
            ? element.GetBoolean()
            : value > 0;
        var vram = Value("reportedVramBytes");
        var ram = Value("reportedSystemRamBytes");
        var gpu = Value("gpuAllocationBytes");
        var process = Value("processPeakBytes");
        var decoded = Value("decodedCpuBytes");
        var export = Value("exportBufferBytes");
        var history = Value("historyAccelerationBytes");
        var headroom = Value("engineCompositorHeadroomBytes");
        if (vram <= 0 || ram <= 0 ||
            !Measured("gpuMeasured", gpu) || !Measured("processMeasured", process) ||
            !Measured("decodedMeasured", decoded) || !Measured("exportMeasured", export) ||
            !Measured("historyMeasured", history))
            return Failure("missing-resource-readings", "One or more capacity/allocation readings are unavailable.");
        var gpuBudget = Math.Max(0, Math.Min(vram / 4, RenderResourceLedger.MaximumGpuBytes) - headroom);
        var within = gpu <= gpuBudget && decoded <= RenderResourceLedger.MaximumDecodedCpuBytes &&
                     export <= RenderResourceLedger.MaximumExportBufferBytes && process <= ram / 4 &&
                     history <= RenderResourceLedger.MaximumHistoryAccelerationBytes;
        return within
            ? Success("Every measured allocation is within its declared resource envelope.")
            : Failure("resource-budget-exceeded", "A measured allocation exceeds its resource envelope.");
    }

    private static AcceptanceMetricResult Failure(string code, string detail) => new(
        false, code, 0, 0, null, null, null, LatencyEndpoint, Array.Empty<long>(), detail);

    private static object Success(string detail) => new
    {
        passed = true,
        failureCode = (string?)null,
        sampleCount = 1,
        excludedCount = 0,
        p50Milliseconds = (double?)null,
        p95Milliseconds = (double?)null,
        p99Milliseconds = (double?)null,
        endpoint = LatencyEndpoint,
        correlatedSequenceIds = Array.Empty<long>(),
        detail
    };

    private static double NearestRank(double[] ordered, double percentile)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(percentile * ordered.Length));
        return ordered[rank - 1];
    }
}

public sealed record AcceptanceStall(long SequenceId, double Milliseconds, string Cause);

public sealed record AcceptanceStageEvidence(
    long SequenceId,
    long Revision,
    long DrawGeneration,
    double? CommitMilliseconds,
    double ReferenceRenderMilliseconds,
    double PngEncodeMilliseconds,
    double TextureUploadMilliseconds,
    double DrawToPostDrawMilliseconds)
{
    public string? AccelerationMode { get; init; }
    public ConnectedTerrainGraph.ViewportSourceStages? SourceStages { get; init; }
}

public sealed record AcceptanceResourceEvidence(
    long ReportedVramBytes,
    long ReportedSystemRamBytes,
    long EngineCompositorHeadroomBytes,
    long GpuAllocationBytes,
    bool GpuMeasured,
    long ProcessPeakBytes,
    bool ProcessMeasured,
    long DecodedCpuBytes,
    bool DecodedMeasured,
    long ExportBufferBytes,
    bool ExportMeasured,
    long HistoryAccelerationBytes,
    bool HistoryMeasured,
    bool Passed,
    string? FailureCode);

public sealed record AcceptanceRunEvidence(
    string Scenario,
    string Cache,
    double DurationSeconds,
    int AssertionCount,
    AcceptanceMetricResult InputToVisible,
    AcceptancePercentiles? FrameIntervals,
    int FrameSampleCount,
    int GeneratedPointerSamples,
    int ProcessedPointerSamples,
    int CoalescedPointerSamples,
    int DroppedPointerSamples,
    int MaximumQueueDepth,
    IReadOnlyList<AcceptanceStall> StallsOver100Milliseconds,
    double? RecentUndoP95Milliseconds,
    int RecentUndoMaximumTiles,
    AcceptanceResourceEvidence Resources,
    string CacheAction,
    bool Passed,
    string Detail)
{
    public IReadOnlyList<AcceptanceStageEvidence> Stages { get; init; } = [];
}

public sealed record AcceptanceHardwareEvidence(
    string FixturePath,
    long FixtureBytes,
    string FixtureSha256,
    string OperatingSystem,
    int ProcessorCount,
    string RenderingDriver,
    string RenderingMethod,
    string VideoAdapter,
    int ViewportWidth,
    int ViewportHeight,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<AcceptanceRunEvidence> Runs,
    bool Passed,
    string Detail);

public sealed class ConnectedAcceptanceCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("__AcceptanceHardware", ConnectedAcceptance.RunHardwareAsync);
}

public static class ConnectedAcceptance
{
    public const string FixtureSha256 = "67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab";
    private static readonly JsonSerializerOptions EvidenceJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static async Task<int> RunHardwareAsync(CancellationToken cancellationToken)
    {
        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        var outputPath = System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_OUTPUT")
            ?? Path.Combine(ProjectSettings.GlobalizePath("res://"), "artifacts", "phase1-acceptance", "hardware.json");
        var duration = ReadDuration();
        var repositoryRoot = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        outputPath = Path.GetFullPath(outputPath);
        if (!outputPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Acceptance evidence must remain inside the isolated worktree.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var started = DateTimeOffset.UtcNow;
        await using var source = File.OpenRead(fixture);
        var fixtureHash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(fixtureHash, FixtureSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The pinned 74 MB .ink fixture identity changed.");

        var imported = new InkImportService().Import(fixture);
        if (imported.PreviewPng is not { Length: > 0 })
            throw new InvalidDataException("The real fixture has no preserved preview.");
        var initial = Main.CreateFlattenedSnapshot(imported);
        var displaySource = ConnectedTerrainGraph.PrepareDisplaySource(
            imported.PreviewPng, imported.PreviewWidth, imported.PreviewHeight);
        initial = initial with
        {
            ImportedSource = initial.ImportedSource! with
            {
                DisplaySourceBlobHash = displaySource.Sha256,
                DisplaySourceWidth = displaySource.Width,
                DisplaySourceHeight = displaySource.Height
            }
        };
        var tree = Engine.GetMainLoop() as SceneTree
            ?? throw new InvalidOperationException("The acceptance case requires the Godot scene tree.");
        tree.Root.Size = new Vector2I(1920, 1080);
        var canvas = new MapCanvas { Name = "AcceptanceMapCanvas" };
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        tree.Root.AddChild(canvas);

        var runs = new List<AcceptanceRunEvidence>();
        try
        {
            var combinations = from scenario in Enum.GetValues<AcceptanceScenario>()
                from cache in Enum.GetValues<AcceptanceCacheState>()
                select (scenario, cache);
            var selectedScenario = System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_SCENARIO");
            if (!string.IsNullOrWhiteSpace(selectedScenario))
            {
                if (!string.Equals(System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_SMOKE"),
                        "1", StringComparison.Ordinal) ||
                    !Enum.TryParse<AcceptanceScenario>(selectedScenario, true, out var parsedScenario) ||
                    !Enum.IsDefined(parsedScenario))
                    throw new InvalidOperationException("Scenario selection is valid only for a named smoke run.");
                combinations = combinations.Where(pair => pair.scenario == parsedScenario);
            }
            var selectedCache = System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_CACHE");
            if (!string.IsNullOrWhiteSpace(selectedCache))
            {
                if (!string.Equals(System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_SMOKE"),
                        "1", StringComparison.Ordinal) ||
                    !Enum.TryParse<AcceptanceCacheState>(selectedCache, true, out var parsedCache) ||
                    !Enum.IsDefined(parsedCache))
                    throw new InvalidOperationException("Cache selection is valid only for a named smoke run.");
                combinations = combinations.Where(pair => pair.cache == parsedCache);
            }
            var maximumRuns = ReadMaximumRuns();
            foreach (var (scenario, cache) in combinations.Take(maximumRuns))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runRoot = Path.Combine(Path.GetDirectoryName(outputPath)!, "projects",
                    scenario.ToString(), cache.ToString());
                EnsureOwnedDirectory(runRoot, Path.GetDirectoryName(outputPath)!);
                var repository = new SqliteProjectRepository(runRoot);
                repository.CommitProfileObserver = profile => GD.Print(
                    $"COMMIT_PROFILE rev={profile.Revision} open={profile.OpenAndPragmasMilliseconds:F2} " +
                    $"read={profile.HistoryReadMilliseconds:F2} insert={profile.CommandInsertMilliseconds:F2} " +
                    $"commit={profile.TransactionCommitMilliseconds:F2} total={profile.TotalMilliseconds:F2}");
                var runInitial = initial with
                {
                    ProjectId = ProjectId.New(),
                    MapId = MapId.New()
                };
                await repository.CreateImportedAsync(runInitial, fixture,
                    [new ImportedBlobPayload(runInitial.ImportedSource!.PreviewBlobHash,
                        imported.PreviewPng, "flattened preview"),
                     new ImportedBlobPayload(displaySource.Sha256, displaySource.Bytes,
                        "immutable display source")], cancellationToken);
                var project = await repository.LoadAsync(runInitial.ProjectId, cancellationToken);
                runs.Add(await RunScenarioAsync(canvas, repository, runRoot, project,
                    scenario, cache, duration, cancellationToken));
                GD.Print($"ACCEPTANCE_RUN scenario={scenario} cache={cache} duration={runs[^1].DurationSeconds:F3} " +
                         $"samples={runs[^1].InputToVisible.SampleCount} passed={runs[^1].Passed}");
            }
        }
        finally
        {
            canvas.QueueFree();
        }

        var evidence = new AcceptanceHardwareEvidence(
            fixture,
            new FileInfo(fixture).Length,
            fixtureHash,
            RuntimeInformation.OSDescription,
            System.Environment.ProcessorCount,
            RenderingServer.GetCurrentRenderingDriverName(),
            RenderingServer.GetCurrentRenderingMethod(),
            RenderingServer.GetVideoAdapterName(),
            1920,
            1080,
            started,
            DateTimeOffset.UtcNow,
            runs,
            runs.Count == 12 && runs.All(run => run.Passed),
            "Each run used the real imported preview, durable commands/history, the shared terrain reference graph, " +
            "and the first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture.");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, EvidenceJson), cancellationToken);
        GD.Print(JsonSerializer.Serialize(evidence, EvidenceJson));
        return evidence.Passed ? 12 : throw new InvalidOperationException(
            "One or more hardware/cache scenarios failed; evidence was written without promotion.");
    }

    private static async Task<AcceptanceRunEvidence> RunScenarioAsync(
        MapCanvas canvas,
        SqliteProjectRepository repository,
        string projectRoot,
        MapProject initial,
        AcceptanceScenario scenario,
        AcceptanceCacheState cache,
        double requiredSeconds,
        CancellationToken cancellationToken)
    {
        var inputs = new List<AcceptanceInputEvent>();
        var presentations = new List<AcceptancePresentationEvent>();
        var stages = new Dictionary<long, AcceptanceStageEvidence>();
        var frameIntervals = new List<double>();
        var frameGate = new object();
        var lastFrame = 0L;
        void FrameObserver()
        {
            var now = Stopwatch.GetTimestamp();
            lock (frameGate)
            {
                if (lastFrame != 0) frameIntervals.Add(Stopwatch.GetElapsedTime(lastFrame, now).TotalMilliseconds);
                lastFrame = now;
            }
        }
        RenderingServer.FramePostDraw += FrameObserver;
        var elapsed = Stopwatch.StartNew();
        var sequence = 0L;
        var generated = 0;
        var processed = 0;
        var coalesced = 0;
        var dropped = 0;
        var maxQueue = 0;
        var undoDurations = new List<double>();
        var undoTiles = 0;
        var graph = new ConnectedTerrainGraph(projectRoot);
        canvas.BindConnectedGraph(graph);
        string cacheAction;
        try
        {
            cacheAction = cache switch
            {
                AcceptanceCacheState.Warm => "Immutable import-time display blob verified; decoded display pixels, accumulator and canvas tile textures retained for the run.",
                AcceptanceCacheState.Cold => "Decoded display pixels and accumulator absent until the first measured update; the immutable import-time display blob is read and hash-verified on that update.",
                _ => "Owned render/history cache directories, decoded display pixels and accumulator evicted before each measured update; the immutable import-time display blob remains project source and is read and hash-verified again."
            };
            if (cache == AcceptanceCacheState.Evicted) EvictOwnedCaches(projectRoot);
            if (cache == AcceptanceCacheState.Warm)
            {
                await PresentOnNextFrameAsync(canvas, initial, null, cancellationToken);
            }
            lock (frameGate)
            {
                frameIntervals.Clear();
                lastFrame = 0;
            }
            elapsed.Restart();
            var current = initial;

            if (scenario == AcceptanceScenario.UndoRedo)
            {
                await using (var edit = new EditSession(current, repository, new NullRenderInvalidationQueue()))
                {
                    // Mirror MapCanvas.BindConnectedSession: discarded, never-committed rehearsal
                    // while the opened project's storage finishes preparing.
                    await edit.RehearseAsync(canvas.RehearsalCommands(edit.Current));
                    await edit.Prepared;
                    var command = TextureCommand(current, 0, 64);
                    await edit.ExecuteAsync(command, cancellationToken);
                    current = edit.Current;
                }
                await using var history = new HistorySession(current, repository, new NullRenderInvalidationQueue());
                await history.Prepared;
                var tile = new HistoryTileDelta(0, 0, ImmutableArray.Create((byte)1), ImmutableArray.Create((byte)2));
                history.RememberResidentDelta(new HistoryResidentDelta(current.Revision - 1, current.Revision,
                    HistoryCompatibility.For(current), [tile]));
                await PresentRevisionAsync(canvas, current, null, ++sequence, "setup", inputs,
                    presentations, stages, null, cancellationToken);
                // Seeded history and its first full viewport are setup, not measured navigation.
                // Restart only after the setup revision has reached the correlated draw endpoint.
                lock (frameGate)
                {
                    frameIntervals.Clear();
                    lastFrame = 0;
                }
                elapsed.Restart();
                while (elapsed.Elapsed.TotalSeconds < requiredSeconds)
                {
                    var undo = history.Current.Revision > 0;
                    var inputTimestamp = Stopwatch.GetTimestamp();
                    var baseRevision = history.Current.Revision;
                    var operation = Stopwatch.StartNew();
                    var result = undo
                        ? await history.UndoAsync(cancellationToken)
                        : await history.RedoAsync(cancellationToken);
                    operation.Stop();
                    undoDurations.Add(operation.Elapsed.TotalMilliseconds);
                    undoTiles = Math.Max(undoTiles, result.TilesRestored);
                    if (cache == AcceptanceCacheState.Evicted)
                    {
                        EvictOwnedCaches(projectRoot);
                        graph.EvictDecodedCache();
                    }
                    await PresentRevisionAsync(canvas, history.Current, result.Invalidation,
                        ++sequence, undo ? "Undo" : "Redo", inputs,
                        presentations, stages, (inputTimestamp, baseRevision, operation.Elapsed.TotalMilliseconds),
                        cancellationToken);
                    generated += 1;
                    processed += 1;
                    maxQueue = Math.Max(maxQueue, 1);
                    await DelayBurstAsync(elapsed, requiredSeconds, cancellationToken);
                }
            }
            else
            {
                await using var edit = new EditSession(current, repository, new NullRenderInvalidationQueue());
                edit.SaveStateChanged += state => maxQueue = Math.Max(maxQueue, state.QueuedCommands);
                // Mirror MapCanvas.BindConnectedSession: discarded, never-committed rehearsal
                // while the opened project's storage finishes preparing.
                await edit.RehearseAsync(canvas.RehearsalCommands(edit.Current));
                await edit.Prepared;
                while (elapsed.Elapsed.TotalSeconds < requiredSeconds)
                {
                    var burst = BurstSamples(edit.Current, sequence, scenario);
                    generated += burst.Length;
                    processed += burst.Length;
                    coalesced += Math.Max(0, burst.Length - 1);
                    if (scenario == AcceptanceScenario.PanZoomWhilePainting)
                    {
                        canvas.Gestures.SetView(0, 0, 1);
                        canvas.Gestures.ZoomAt(960, 540, sequence % 2 == 0 ? 1.05 : 1 / 1.05);
                        canvas.Gestures.BeginTemporaryPan(960, 540, "Middle");
                        canvas.Gestures.MoveTemporaryPan(968, 546);
                        canvas.Gestures.EndTemporaryPan();
                    }
                    var command = ScenarioCommand(edit.Current, scenario, sequence, burst);
                    var inputTimestamp = Stopwatch.GetTimestamp();
                    var baseRevision = edit.Current.Revision;
                    var commit = Stopwatch.StartNew();
                    var acknowledgement = await edit.ExecuteAsync(command, cancellationToken);
                    commit.Stop();
                    if (acknowledgement.Revision != edit.Current.Revision)
                        throw new InvalidDataException("Acceptance command acknowledgement lost its revision.");
                    if (cache == AcceptanceCacheState.Evicted)
                    {
                        EvictOwnedCaches(projectRoot);
                        graph.EvictDecodedCache();
                    }
                    await PresentRevisionAsync(canvas, edit.Current, acknowledgement.Invalidation,
                        ++sequence, command.GetType().Name, inputs,
                        presentations, stages, (inputTimestamp, baseRevision, commit.Elapsed.TotalMilliseconds),
                        cancellationToken);
                    await DelayBurstAsync(elapsed, requiredSeconds, cancellationToken);
                }
            }
        }
        finally
        {
            RenderingServer.FramePostDraw -= FrameObserver;
        }

        elapsed.Stop();
        if (inputs.Count > 0 && inputs[0].Command == "setup")
        {
            var setupId = inputs[0].SequenceId;
            inputs.RemoveAt(0);
            presentations.RemoveAll(sample => sample.SequenceId == setupId);
            stages.Remove(setupId);
        }
        var latency = AcceptanceMetricEngine.EvaluateLatency(inputs, presentations, Stopwatch.Frequency);
        var stageEvidence = AcceptanceMetricEngine.ValidateStages(presentations, stages.Values.ToArray()).Passed;
        AcceptancePercentiles? framePercentiles;
        lock (frameGate)
            framePercentiles = frameIntervals.Count == 0 ? null : AcceptanceMetricEngine.CalculatePercentiles(frameIntervals);
        var stalls = latency.Passed
            ? inputs.Select(input => (input, presentation: presentations.Single(value => value.SequenceId == input.SequenceId)))
                .Select(pair => new
                {
                    pair.input.SequenceId,
                    Milliseconds = Stopwatch.GetElapsedTime(pair.input.Timestamp, pair.presentation.Timestamp).TotalMilliseconds
                })
                .Where(value => value.Milliseconds > 100)
                .Select(value => new AcceptanceStall(value.SequenceId, value.Milliseconds,
                    AttributeCause(stages[value.SequenceId])))
                .Concat(frameIntervals.Where(value => value > 100)
                    .Select(value => new AcceptanceStall(0, value,
                        "FramePostDraw interval / application or OS scheduling")))
                .ToArray()
            : Array.Empty<AcceptanceStall>();
        var hardware = RenderResourceLedger.CaptureStartup();
        var device = RenderingServer.GetRenderingDevice();
        var gpuUsage = device is null ? 0 : checked((long)device.GetMemoryUsage(RenderingDevice.MemoryType.Total));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var resourcesJson = AcceptanceMetricEngine.EvaluateJson(JsonSerializer.Serialize(new
        {
            kind = "resources",
            reportedVramBytes = hardware.ReportedVramBytes,
            reportedSystemRamBytes = hardware.ReportedSystemRamBytes,
            engineCompositorHeadroomBytes = hardware.EngineCompositorHeadroomBytes,
            gpuAllocationBytes = gpuUsage,
            gpuMeasured = gpuUsage > 0,
            processPeakBytes = process.PeakWorkingSet64,
            processMeasured = process.PeakWorkingSet64 > 0,
            decodedCpuBytes = graph.CachedDecodedBytes,
            decodedMeasured = graph.CachedDecodedBytes > 0,
            exportBufferBytes = 0,
            exportMeasured = true,
            historyAccelerationBytes = DirectoryBytes(Path.Combine(projectRoot, "cache", "history")),
            historyMeasured = true
        }));
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resourceRoot = resourcesDocument.RootElement;
        var resourcePassed = resourceRoot.GetProperty("passed").GetBoolean();
        var resourceFailure = resourceRoot.GetProperty("failureCode").ValueKind == JsonValueKind.Null
            ? null
            : resourceRoot.GetProperty("failureCode").GetString();
        var resources = new AcceptanceResourceEvidence(
            hardware.ReportedVramBytes, hardware.ReportedSystemRamBytes,
            hardware.EngineCompositorHeadroomBytes, gpuUsage, gpuUsage > 0,
            process.PeakWorkingSet64, process.PeakWorkingSet64 > 0,
            graph.CachedDecodedBytes, graph.CachedDecodedBytes > 0,
            0, true,
            DirectoryBytes(Path.Combine(projectRoot, "cache", "history")), true,
            resourcePassed, resourceFailure);
        double? recentUndoP95 = undoDurations.Count == 0
            ? null
            : AcceptanceMetricEngine.CalculatePercentiles(undoDurations).P95Milliseconds;
        var thresholdsPass = latency.Passed && latency.P95Milliseconds <= 50 && latency.P99Milliseconds <= 100 &&
                             framePercentiles is not null && framePercentiles.P95Milliseconds <= 20 &&
                             framePercentiles.P99Milliseconds <= 33.3 &&
                             (recentUndoP95 is null || recentUndoP95 <= 100) && resourcePassed && stageEvidence &&
                             elapsed.Elapsed.TotalSeconds >= requiredSeconds;
        return new AcceptanceRunEvidence(scenario.ToString(), cache.ToString(), elapsed.Elapsed.TotalSeconds,
            inputs.Count, latency, framePercentiles, frameIntervals.Count,
            generated, processed, coalesced, dropped, maxQueue, stalls, recentUndoP95, undoTiles,
            resources, cacheAction, thresholdsPass,
            thresholdsPass ? "All measured thresholds passed." :
                "One or more latency, frame, undo, resource, stage, sample, or duration gates failed/unverified.")
        {
            Stages = stages.Values.OrderBy(value => value.SequenceId).ToArray()
        };
    }

    private static async Task PresentRevisionAsync(
        MapCanvas canvas,
        MapProject project,
        TileInvalidation? invalidation,
        long sequence,
        string command,
        List<AcceptanceInputEvent> inputs,
        List<AcceptancePresentationEvent> presentations,
        Dictionary<long, AcceptanceStageEvidence> stages,
        (long Timestamp, long BaseRevision, double CommitMilliseconds)? timing,
        CancellationToken cancellationToken)
    {
        var inputTimestamp = timing?.Timestamp ?? Stopwatch.GetTimestamp();
        var baseRevision = timing?.BaseRevision ?? Math.Max(0, project.Revision - 1);
        var presented = await PresentOnNextFrameAsync(canvas, project, invalidation, cancellationToken);
        if (presented.Update.Revision != project.Revision)
            throw new InvalidDataException("Canvas update revision differs from the committed revision.");
        inputs.Add(new AcceptanceInputEvent(sequence, inputTimestamp, command, baseRevision));
        presentations.Add(new AcceptancePresentationEvent(sequence, project.Revision, project.Revision, presented.Timestamp));
        stages[sequence] = new AcceptanceStageEvidence(sequence, project.Revision, presented.Update.Generation,
            timing?.CommitMilliseconds, presented.Update.ReferenceRenderMilliseconds, 0,
            presented.Update.TextureUploadMilliseconds, presented.DrawToPostDrawMilliseconds)
        {
            AccelerationMode = presented.Update.AccelerationMode,
            SourceStages = presented.Update.SourceStages
        };
    }

    private static async Task<(long Timestamp, ConnectedCanvasUpdate Update, double DrawToPostDrawMilliseconds)> PresentOnNextFrameAsync(
        MapCanvas canvas,
        MapProject project,
        TileInvalidation? invalidation,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(long Timestamp, double DrawToPostDrawMilliseconds)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = true;
        long expectedGeneration = -1;
        void Handler()
        {
            if (canvas.LastDrawnRevision != project.Revision ||
                canvas.LastDrawnGeneration != expectedGeneration)
                return;
            if (connected)
            {
                connected = false;
                RenderingServer.FramePostDraw -= Handler;
            }
            var timestamp = Stopwatch.GetTimestamp();
            completion.TrySetResult((timestamp,
                Stopwatch.GetElapsedTime(canvas.LastDrawnTimestamp, timestamp).TotalMilliseconds));
        }
        RenderingServer.FramePostDraw += Handler;
        try
        {
            var update = canvas.ShowCommittedChange(project, invalidation);
            expectedGeneration = update.Generation;
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return (result.Timestamp, update,
                result.DrawToPostDrawMilliseconds);
        }
        finally
        {
            if (connected)
            {
                connected = false;
                RenderingServer.FramePostDraw -= Handler;
            }
        }
    }

    private static IEditCommand ScenarioCommand(
        MapProject project,
        AcceptanceScenario scenario,
        long sequence,
        ImmutableArray<MapPoint> samples)
    {
        if (scenario is AcceptanceScenario.PaintAcrossTiles or AcceptanceScenario.PanZoomWhilePainting)
            return TextureCommand(project, sequence, 96, samples);
        if (project.River is null)
        {
            var foreground = project.RequireRole(TerrainRole.Foreground);
            var points = ImmutableArray.Create(
                new MapPoint(project.Width * 0.2, project.Height * 0.4),
                new MapPoint(project.Width * 0.5, project.Height * 0.55),
                new MapPoint(project.Width * 0.8, project.Height * 0.45));
            return new CreateRiver(CommandId.New(), project.Revision,
                River.Create(RiverId.New(), foreground.Id, points, [40d, 64d, 48d], 0.35));
        }
        return sequence % 2 == 0
            ? new MoveRiverPoint(CommandId.New(), project.Revision, 1,
                new MapPoint(project.Width * 0.5, project.Height * (0.50 + (sequence % 5) * 0.01)))
            : new SetRiverPointWidth(CommandId.New(), project.Revision, 1, 48 + sequence % 24);
    }

    private static IEditCommand TextureCommand(
        MapProject project,
        long sequence,
        double radius,
        ImmutableArray<MapPoint>? supplied = null)
    {
        var samples = supplied ?? ImmutableArray.Create(new MapPoint(project.Width * 0.5, project.Height * 0.5));
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var brush = new ResolvedBrush(new string('c', 64), radius, 0.72, 0.86, 0.8,
            0.2, 0, unchecked((int)(1201 + sequence)), 1);
        return new AddTextureStroke(CommandId.New(), project.Revision, foreground.Id,
            new PaintStroke(StrokeId.New(), samples, brush, false, TerrainStrokeKind.Texture));
    }

    private static ImmutableArray<MapPoint> BurstSamples(
        MapProject project,
        long sequence,
        AcceptanceScenario scenario)
    {
        const int count = 60;
        var builder = ImmutableArray.CreateBuilder<MapPoint>(count);
        for (var index = 0; index < count; index++)
        {
            var progress = index / (double)(count - 1);
            var x = project.Width * (0.08 + progress * 0.84);
            var wave = Math.Sin((index + sequence * 7) * 0.19) * project.Height * 0.08;
            var y = project.Height * (scenario == AcceptanceScenario.RiverEdit ? 0.5 : 0.35) + wave;
            builder.Add(new MapPoint(x, Math.Clamp(y, 0, project.Height - 1)));
        }
        return builder.MoveToImmutable();
    }

    private static void EvictOwnedCaches(string projectRoot)
    {
        foreach (var relative in new[] { Path.Combine("cache", "tiles"), Path.Combine("cache", "history") })
        {
            var target = Path.GetFullPath(Path.Combine(projectRoot, relative));
            if (!target.StartsWith(Path.GetFullPath(projectRoot) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cache eviction target escaped the acceptance project.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }

    private static void EnsureOwnedDirectory(string target, string owner)
    {
        var fullTarget = Path.GetFullPath(target);
        var fullOwner = Path.GetFullPath(owner).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(fullOwner, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Acceptance output escaped its owned artifact directory.");
        if (Directory.Exists(fullTarget)) Directory.Delete(fullTarget, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
    }

    private static async Task DelayBurstAsync(
        Stopwatch elapsed,
        double requiredSeconds,
        CancellationToken cancellationToken)
    {
        if (elapsed.Elapsed.TotalSeconds >= requiredSeconds) return;
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(4, requiredSeconds - elapsed.Elapsed.TotalSeconds)),
            cancellationToken);
    }

    private static string AttributeCause(AcceptanceStageEvidence stages)
    {
        var costs = new (string Name, double Milliseconds)[]
        {
            ("durable command/history", stages.CommitMilliseconds ?? 0),
            ("shared reference render", stages.ReferenceRenderMilliseconds),
            ("PNG encode", stages.PngEncodeMilliseconds),
            ("texture decode/upload", stages.TextureUploadMilliseconds),
            ("canvas draw-to-postdraw", stages.DrawToPostDrawMilliseconds)
        };
        var worst = costs.MaxBy(value => value.Milliseconds);
        return $"{worst.Name} stage ({worst.Milliseconds:F3} ms)";
    }

    private static long DirectoryBytes(string path) => Directory.Exists(path)
        ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
        : 0;

    private static double ReadDuration()
    {
        var raw = System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_DURATION_SECONDS");
        if (raw is null) return 60;
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException("Acceptance duration is invalid.");
        var smoke = string.Equals(System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_SMOKE"),
            "1", StringComparison.Ordinal);
        if (value < (smoke ? 1 : 60))
            throw new InvalidOperationException("Full acceptance duration must be at least 60 seconds per scenario/cache run.");
        return value;
    }

    private static int ReadMaximumRuns()
    {
        if (!string.Equals(System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_SMOKE"),
                "1", StringComparison.Ordinal)) return 12;
        var raw = System.Environment.GetEnvironmentVariable("MAPWRIGHT_ACCEPTANCE_MAX_RUNS");
        return int.TryParse(raw, out var value) ? Math.Clamp(value, 1, 12) : 1;
    }
}

public enum AcceptanceScenario
{
    PaintAcrossTiles,
    PanZoomWhilePainting,
    RiverEdit,
    UndoRedo
}

public enum AcceptanceCacheState
{
    Warm,
    Cold,
    Evicted
}
