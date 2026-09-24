using System.Reflection;
using System.Text.Json;
using Mapwright.App;

public sealed class AcceptanceContractCases : IContractCaseProvider
{
    private const string EngineTypeName = "Mapwright.Acceptance.AcceptanceMetricEngine, Mapwright";

    public void Register(ContractRegistry registry)
    {
        registry.Add("acceptance metrics calculate nearest-rank p50 p95 p99", CalculatesPercentiles);
        registry.Add("acceptance metrics reject missing empty and uncorrelated samples", RejectsAbsentEvidence);
        registry.Add("acceptance metrics retain sequence order for timestamp ties", RetainsTieOrder);
        registry.Add("acceptance metrics correlate repeated undo redo revisions by sequence",
            CorrelatesNavigationDirections);
        registry.Add("acceptance schema requires every scenario and cache state", RequiresCompleteScenarioMatrix);
        registry.Add("acceptance resources require measured readings", RequiresMeasuredResources);
        registry.Add("acceptance stages require complete correlated readings", RequiresCorrelatedStages);
        registry.Add("connected MapUnits case is discoverable", RequiresMapUnitsCase);
        registry.Add("phase11 evidence roots reject missing stale and escaped run context", RejectsUnsafeRunRoots);
    }

    private static Task CalculatesPercentiles()
    {
        var result = Evaluate(new
        {
            kind = "latency",
            inputs = new[]
            {
                Sample(1, 100), Sample(2, 200), Sample(3, 300), Sample(4, 400)
            },
            presentations = new[]
            {
                Presentation(1, 101), Presentation(2, 202), Presentation(3, 303), Presentation(4, 404)
            }
        });
        True(result.GetProperty("passed").GetBoolean(), "A complete correlated sample set should pass.");
        Equal(4, result.GetProperty("sampleCount").GetInt32());
        Equal(2d, result.GetProperty("p50Milliseconds").GetDouble());
        Equal(4d, result.GetProperty("p95Milliseconds").GetDouble());
        Equal(4d, result.GetProperty("p99Milliseconds").GetDouble());
        Equal("input-monotonic-timestamp to first FramePostDraw after MapCanvas._Draw submitted the matching revision-tagged texture; display scanout excluded",
            result.GetProperty("endpoint").GetString());
        return Task.CompletedTask;
    }

    private static Task RejectsAbsentEvidence()
    {
        Equal("missing-samples", Failure(Evaluate(new { kind = "latency" })));
        Equal("empty-samples", Failure(Evaluate(new
        {
            kind = "latency",
            inputs = Array.Empty<object>(),
            presentations = Array.Empty<object>()
        })));
        Equal("dropped-input-ids", Failure(Evaluate(new
        {
            kind = "latency",
            inputs = new[] { Sample(1, 100), Sample(2, 200) },
            presentations = new[] { Presentation(1, 110) }
        })));
        Equal("uncorrelated-samples", Failure(Evaluate(new
        {
            kind = "latency",
            inputs = new[] { Sample(1, 100) },
            presentations = new[] { Presentation(9, 110) }
        })));
        return Task.CompletedTask;
    }

    private static Task RetainsTieOrder()
    {
        var result = Evaluate(new
        {
            kind = "latency",
            inputs = new[] { Sample(41, 100), Sample(42, 100) },
            presentations = new[] { Presentation(41, 120), Presentation(42, 120) }
        });
        True(result.GetProperty("passed").GetBoolean(), "Equal timestamps should remain valid.");
        var order = result.GetProperty("correlatedSequenceIds").EnumerateArray()
            .Select(value => value.GetInt64()).ToArray();
        True(order.SequenceEqual([41L, 42L]), "Equal timestamps reordered sequence IDs.");
        return Task.CompletedTask;
    }

    private static Task RequiresCompleteScenarioMatrix()
    {
        var complete = new List<object>();
        foreach (var scenario in new[] { "PaintAcrossTiles", "PanZoomWhilePainting", "RiverEdit", "UndoRedo" })
        foreach (var cache in new[] { "Warm", "Cold", "Evicted" })
            complete.Add(new { scenario, cache, durationSeconds = 60.0, assertionCount = 1 });
        True(Evaluate(new { kind = "schema", runs = complete }).GetProperty("passed").GetBoolean(),
            "The complete four-by-three matrix should pass schema validation.");
        complete.RemoveAt(0);
        Equal("missing-scenario-cache-run", Failure(Evaluate(new { kind = "schema", runs = complete })));
        complete.Add(new { scenario = "Unknown", cache = "Warm", durationSeconds = 60.0, assertionCount = 1 });
        Equal("unknown-scenario-cache-run", Failure(Evaluate(new { kind = "schema", runs = complete })));
        complete.RemoveAt(complete.Count - 1);
        complete.Add(new { scenario = "PaintAcrossTiles", cache = "Warm", durationSeconds = 60.0, assertionCount = 0 });
        Equal("short-or-zero-assertion-run", Failure(Evaluate(new { kind = "schema", runs = complete })));
        return Task.CompletedTask;
    }

    private static Task RequiresMeasuredResources()
    {
        Equal("missing-resource-readings", Failure(Evaluate(new
        {
            kind = "resources",
            reportedVramBytes = 0,
            reportedSystemRamBytes = 32L * 1024 * 1024 * 1024,
            gpuAllocationBytes = 0,
            processPeakBytes = 1234,
            decodedCpuBytes = 123,
            exportBufferBytes = 456,
            historyAccelerationBytes = 789
        })));
        True(Evaluate(new
        {
            kind = "resources",
            reportedVramBytes = 8L * 1024 * 1024 * 1024,
            reportedSystemRamBytes = 32L * 1024 * 1024 * 1024,
            gpuAllocationBytes = 64L * 1024 * 1024,
            processPeakBytes = 512L * 1024 * 1024,
            decodedCpuBytes = 128L * 1024 * 1024,
            exportBufferBytes = 64L * 1024 * 1024,
            historyAccelerationBytes = 32L * 1024 * 1024
        }).GetProperty("passed").GetBoolean(), "Measured in-budget resource readings should pass.");
        return Task.CompletedTask;
    }

    private static Task CorrelatesNavigationDirections()
    {
        var inputs = new[]
        {
            new { sequenceId = 11L, timestamp = 100L, command = "Undo", baseRevision = 1L },
            new { sequenceId = 12L, timestamp = 200L, command = "Redo", baseRevision = 0L },
            new { sequenceId = 13L, timestamp = 300L, command = "Undo", baseRevision = 1L }
        };
        var presentations = new[]
        {
            new { sequenceId = 11L, committedRevision = 0L, presentedRevision = 0L, timestamp = 120L },
            new { sequenceId = 12L, committedRevision = 1L, presentedRevision = 1L, timestamp = 220L },
            new { sequenceId = 13L, committedRevision = 0L, presentedRevision = 0L, timestamp = 320L }
        };
        var valid = Evaluate(new { kind = "latency", inputs, presentations });
        True(valid.GetProperty("passed").GetBoolean(), "Repeated Undo/Redo must correlate by sequence.");
        Equal(3, valid.GetProperty("sampleCount").GetInt32());
        var stale = presentations.ToArray();
        stale[2] = new { sequenceId = 13L, committedRevision = 1L,
            presentedRevision = 1L, timestamp = 320L };
        Equal("uncorrelated-samples", Failure(Evaluate(new { kind = "latency", inputs,
            presentations = stale })));
        return Task.CompletedTask;
    }

    private static Task RequiresCorrelatedStages()
    {
        var presentation = new[] { Presentation(7, 120) };
        var stage = new
        {
            sequenceId = 7, revision = 7, drawGeneration = 3,
            commitMilliseconds = (double?)2, referenceRenderMilliseconds = 4d,
            pngEncodeMilliseconds = 3d, textureUploadMilliseconds = 1d,
            drawToPostDrawMilliseconds = 1d
        };
        var valid = Evaluate(new { kind = "stages", presentations = presentation, stages = new[] { stage } });
        True(valid.GetProperty("passed").GetBoolean(), "Complete stage readings should pass.");
        Equal("uncorrelated-stage-readings", Failure(Evaluate(new
        {
            kind = "stages", presentations = presentation,
            stages = new[] { stage with { sequenceId = 8 } }
        })));
        Equal("missing-stage-readings", Failure(Evaluate(new
        {
            kind = "stages", presentations = presentation,
            stages = new[] { stage with { commitMilliseconds = (double?)null } }
        })));
        return Task.CompletedTask;
    }

    private static Task RequiresMapUnitsCase()
    {
        var registry = ConnectedCaseRegistry.Discover();
        True(registry.Names.Contains("MapUnits", StringComparer.Ordinal),
            "MapUnits connected acceptance case is missing from the registry.");
        return Task.CompletedTask;
    }

    private static Task RejectsUnsafeRunRoots()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "mapwright-evidence-" + Guid.NewGuid().ToString("N"));
        var runId = "20260923T120000000Z-" + Guid.NewGuid().ToString("N");
        var runRoot = Path.Combine(repositoryRoot, "artifacts", "phase11-acceptance", "runs", runId);
        try
        {
            Equal(Path.Combine(repositoryRoot, "artifacts", "ui-smoke"),
                ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", null, null, false));
            Throws(() => ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", null, null, true));
            Throws(() => ConnectedEvidenceRoot.Resolve(repositoryRoot, "../ui-smoke", "visual", null, null, false));
            Throws(() => ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", runId,
                Path.Combine(repositoryRoot, "artifacts", "ui-smoke"), true));
            Throws(() => ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", runId,
                runRoot, true));

            Directory.CreateDirectory(runRoot);
            var lease = Path.Combine(runRoot, ".active-run");
            File.WriteAllText(lease, runId);
            Equal(Path.Combine(runRoot, "visual"),
                ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", runId, runRoot, true));
            File.Delete(lease);
            Throws(() => ConnectedEvidenceRoot.Resolve(repositoryRoot, "ui-smoke", "visual", runId,
                runRoot, true));
        }
        finally
        {
            if (Directory.Exists(repositoryRoot)) Directory.Delete(repositoryRoot, recursive: true);
        }
        return Task.CompletedTask;
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Unsafe evidence root was accepted.");
    }

    private static object Sample(long sequenceId, long timestamp) => new { sequenceId, timestamp };
    private static object Presentation(long sequenceId, long timestamp) =>
        new { sequenceId, committedRevision = sequenceId, presentedRevision = sequenceId, timestamp };

    private static string Failure(JsonElement result)
    {
        True(!result.GetProperty("passed").GetBoolean(), "Invalid evidence unexpectedly passed.");
        True(result.GetProperty("p50Milliseconds").ValueKind == JsonValueKind.Null,
            "Invalid evidence produced a percentile.");
        return result.GetProperty("failureCode").GetString() ?? string.Empty;
    }

    private static JsonElement Evaluate(object request)
    {
        var type = Type.GetType(EngineTypeName);
        True(type is not null,
            "AcceptanceMetricEngine is absent; the recorder cannot distinguish invalid evidence from a pass.");
        var method = type!.GetMethod("EvaluateJson", BindingFlags.Public | BindingFlags.Static);
        True(method is not null, "AcceptanceMetricEngine.EvaluateJson is absent.");
        var response = method!.Invoke(null, [JsonSerializer.Serialize(request)]) as string;
        True(!string.IsNullOrWhiteSpace(response), "Acceptance metric evaluation returned no result.");
        using var document = JsonDocument.Parse(response!);
        return document.RootElement.Clone();
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T? actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }
}
