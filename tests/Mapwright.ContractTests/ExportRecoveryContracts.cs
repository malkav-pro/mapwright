using System.Reflection;
using System.Collections.Immutable;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;

public sealed class ExportRecoveryContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("document export publishes a frozen bounded PNG", DocumentExportPublishesFrozenBoundedPng);
        registry.Add("map export exposes aspect-derived longest-edge presets",
            MapExportExposesAspectDerivedPresets);
        registry.Add("PNG writer declares straight alpha sRGB metadata", PngWriterDeclaresStraightAlphaSrgbMetadata);
        registry.Add("repository exposes durable staged blob recovery", RepositoryExposesDurableStagedBlobRecovery);
        registry.Add("crash worker exposes explicit interruption stages", CrashWorkerExposesExplicitInterruptionStages);
    }

    private static Task RepositoryExposesDurableStagedBlobRecovery()
    {
        var repository = typeof(SqliteProjectRepository);
        True(repository.GetMethod("PublishBlobDurablyAsync", BindingFlags.Instance | BindingFlags.Public) is not null,
            "SqliteProjectRepository must durably hash, verify, flush and publish blobs before SQLite references them.");
        True(repository.GetMethod("ReadRecoveryFactsAsync", BindingFlags.Instance | BindingFlags.Public) is not null,
            "SqliteProjectRepository must expose proven revision/cursor/blob recovery facts for restart UI.");
        return Task.CompletedTask;
    }

    private static Task CrashWorkerExposesExplicitInterruptionStages()
    {
        var stageType = typeof(SqliteProjectRepository).Assembly.GetType(
            "Mapwright.Infrastructure.StorageCrashStage",
            throwOnError: false,
            ignoreCase: false);
        True(stageType is { IsEnum: true },
            "StorageCrashWorker needs explicit blob, transaction, acknowledgement and PNG publication stages.");
        var names = Enum.GetNames(stageType!);
        foreach (var required in new[]
                 {
                     "BeforeBlobFlush", "AfterBlobFlush", "BeforeSqliteCommit",
                     "AfterSqliteCommit", "AfterAcknowledgement", "DuringPngWrite",
                     "DuringPngValidation", "BeforePngPublication", "AfterPngPublication"
                 })
            True(names.Contains(required, StringComparer.Ordinal),
                $"Storage crash stage {required} is missing.");
        return Task.CompletedTask;
    }

    private static Task DocumentExportPublishesFrozenBoundedPng()
    {
        var exportType = typeof(StreamingPngWriter).Assembly.GetType(
            "Mapwright.Export.DocumentPngExport",
            throwOnError: false,
            ignoreCase: false);
        True(exportType is not null,
            "DocumentPngExport must expose the frozen-revision bounded publication pipeline.");
        True(exportType!.GetMethod("ExportAsync", BindingFlags.Instance | BindingFlags.Public) is not null,
            "DocumentPngExport must expose an asynchronous cancellable export operation.");
        return Task.CompletedTask;
    }

    private static Task MapExportExposesAspectDerivedPresets()
    {
        True(typeof(DocumentPngExport).GetMethod("ExportPresetAsync",
                 BindingFlags.Instance | BindingFlags.Public) is not null,
            "Map-facing export must derive both output axes from a supported longest-edge preset.");
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var map = MapProject.CreateNormalizedFromGrid(ProjectId.New(), MapId.New(),
            "Export aspect", 4, 3, 1024, [background, foreground]);
        True(DocumentPngExport.DimensionsForPreset(map, 1024) == (1024, 768),
            "The 1K output must keep 4:3 map aspect.");
        True(DocumentPngExport.DimensionsForPreset(map, 4096) == (4096, 3072),
            "The 4K output must keep 4:3 map aspect.");
        True(DocumentPngExport.DimensionsForPreset(map, 16_384) == (16_384, 12_288),
            "The 16K output must keep 4:3 map aspect and the per-axis cap.");
        foreach (var unsupported in new[] { 1_000, 5_120, 16_385 })
        {
            try
            {
                DocumentPngExport.DimensionsForPreset(map, unsupported);
                throw new InvalidOperationException($"Unsupported map preset {unsupported} was admitted.");
            }
            catch (ArgumentOutOfRangeException) { }
        }
        return Task.CompletedTask;
    }

    private static Task PngWriterDeclaresStraightAlphaSrgbMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-export-red-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "metadata.png");
            using (var writer = new StreamingPngWriter(path, 1, 1))
            {
                writer.WriteRgbaRow([64, 128, 192, 128]);
                writer.Complete();
            }

            var validation = PngValidator.ValidateRgba8(path, 1, 1);
            True(validation.Passed, validation.Detail);
            True(validation.Detail.Contains("sRGB", StringComparison.Ordinal),
                "PNG validation must prove straight-alpha sRGB colour metadata, not only dimensions and CRCs.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
