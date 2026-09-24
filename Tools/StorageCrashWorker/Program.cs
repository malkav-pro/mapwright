using System.Security.Cryptography;
using System.Text.Json;
using Mapwright.Core;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;
using Mapwright.Rendering;

if (args.Length == 3 && args[0] == "--edit-crash-child")
{
    DurableEditRecoveryProbe.RunChild(Path.GetFullPath(args[1]), args[2]);
    return 0;
}

if (args.Length == 3 && args[0] == "--storage-crash-child" &&
    Enum.TryParse<StorageCrashStage>(args[2], out var storageStage))
{
    await StorageCrashWorker.RunStageAsync(Path.GetFullPath(args[1]), storageStage);
    return 0;
}

if (args.Length == 3 && args[0] == "--storage-verify-child" &&
    Enum.TryParse<StorageCrashStage>(args[2], out var verifyStage))
{
    var result = await StorageCrashWorker.VerifyAsync(Path.GetFullPath(args[1]), verifyStage);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return result.Passed ? 0 : 1;
}

if (args.Length != 2 || !Enum.TryParse<ProjectSaveStage>(args[1], out var stage))
{
    Console.Error.WriteLine("Usage: StorageCrashWorker <root> <ProjectSaveStage> | " +
                            "--storage-crash-child <root> <StorageCrashStage> | " +
                            "--storage-verify-child <root> <StorageCrashStage>");
    return 2;
}

CrashRecoveryProbe.RunChild(Path.GetFullPath(args[0]), stage);
return 0;

public sealed record StorageVerificationResult(
    bool Passed,
    long Revision,
    long CursorRevision,
    long LatestRevision,
    int VerifiedBlobCount,
    int CollectedOwnedStages,
    string DestinationSha256,
    string Detail);

public static class StorageCrashWorker
{
    public static async Task RunStageAsync(string root, StorageCrashStage stage)
    {
        var projectRoot = Path.Combine(root, "project.mapwright");
        var projectId = new ProjectId(Guid.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "project-id.txt"))));
        var repository = new SqliteProjectRepository(projectRoot, new CrashClock());
        switch (stage)
        {
            case StorageCrashStage.BeforeBlobFlush:
                SignalAndWait(root, stage);
                return;
            case StorageCrashStage.AfterBlobFlush:
                await repository.PublishBlobDurablyAsync("durable-new-blob"u8.ToArray());
                SignalAndWait(root, stage);
                return;
            case StorageCrashStage.BeforeSqliteCommit:
                await repository.PublishBlobDurablyAsync("durable-before-commit"u8.ToArray());
                SignalAndWait(root, stage);
                return;
            case StorageCrashStage.AfterSqliteCommit:
            case StorageCrashStage.AfterAcknowledgement:
            {
                // An editing session holds the connection; the process dies with the WAL
                // open and unchecked-pointed, exactly as a crash during editing would.
                await using var hold = repository.HoldConnection();
                var current = await repository.LoadAsync(projectId, CancellationToken.None);
                var command = new RenameTerrainLayer(CommandId.New(), current.Revision,
                    TerrainRole.Foreground, $"Foreground recovered {stage}");
                var change = command.Apply(current);
                await repository.CommitAsync(current, command, change, CancellationToken.None);
                if (stage == StorageCrashStage.AfterSqliteCommit)
                {
                    SignalAndWait(root, stage);
                    return;
                }
                WriteDurableText(Path.Combine(root, "acknowledged.txt"),
                    change.Project.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                SignalAndWait(root, stage);
                return;
            }
            case StorageCrashStage.DuringPngWrite:
            case StorageCrashStage.DuringPngValidation:
            case StorageCrashStage.BeforePngPublication:
            case StorageCrashStage.AfterPngPublication:
                await RunExportStageAsync(root, projectRoot, projectId, stage);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    public static async Task<StorageVerificationResult> VerifyAsync(string root, StorageCrashStage stage)
    {
        try
        {
            var projectRoot = Path.Combine(root, "project.mapwright");
            var projectId = new ProjectId(Guid.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, "project-id.txt"))));
            var repository = new SqliteProjectRepository(projectRoot);
            var facts = await repository.ReadRecoveryFactsAsync(projectId);
            var destination = Path.Combine(root, "map.png");
            var validation = PngValidator.ValidateRgba8(destination, 32, 24);
            if (!validation.Passed) throw new InvalidDataException(validation.Detail);
            await using var input = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input)).ToLowerInvariant();
            var acknowledged = File.Exists(Path.Combine(root, "acknowledged.txt"))
                ? long.Parse(await File.ReadAllTextAsync(Path.Combine(root, "acknowledged.txt")),
                    System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            if (facts.AcknowledgedRevision < acknowledged || facts.CursorRevision < acknowledged)
                throw new InvalidDataException("An acknowledged revision or cursor was lost.");
            return new StorageVerificationResult(true, facts.AcknowledgedRevision, facts.CursorRevision,
                facts.LatestRevision, facts.VerifiedBlobCount, facts.CollectedOwnedStagedBlobs, hash,
                facts.BannerText);
        }
        catch (Exception exception)
        {
            return new StorageVerificationResult(false, 0, 0, 0, 0, 0, string.Empty,
                $"{stage}: {exception}");
        }
    }

    private static async Task RunExportStageAsync(
        string root,
        string projectRoot,
        ProjectId projectId,
        StorageCrashStage stage)
    {
        var project = await new SqliteProjectRepository(projectRoot)
            .LoadAsync(projectId, CancellationToken.None);
        var phase = stage switch
        {
            StorageCrashStage.DuringPngWrite => DocumentExportPhase.Encoding,
            StorageCrashStage.DuringPngValidation => DocumentExportPhase.Validating,
            StorageCrashStage.BeforePngPublication => DocumentExportPhase.Publishing,
            StorageCrashStage.AfterPngPublication => DocumentExportPhase.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };
        var ledger = new RenderResourceLedger(16L * RenderResourceLedger.Gibibyte,
            32L * RenderResourceLedger.Gibibyte, 512L * RenderResourceLedger.Mebibyte);
        FrozenDocumentCapture capture = (snapshot, width, height, _) =>
            ValueTask.FromResult<IFrozenDocumentRenderer>(new CrashFrozenRenderer(
                snapshot.ProjectId, snapshot.Revision, width, height));
        var exporter = new DocumentPngExport(ledger, capture, projectRoot,
            (observed, _, _) =>
            {
                if (observed == phase) SignalAndWait(root, stage);
                return ValueTask.CompletedTask;
            });
        await exporter.ExportAsync(project, Path.Combine(root, "map.png"), 32, 24,
            new DocumentPngExportOptions(8, 2));
    }

    private static void SignalAndWait(string root, StorageCrashStage stage)
    {
        WriteDurableText(Path.Combine(root, "stage.marker"), stage.ToString());
        Thread.Sleep(TimeSpan.FromMinutes(10));
    }

    private static void WriteDurableText(string path, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    private sealed class CrashClock : Mapwright.Application.IApplicationClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class CrashFrozenRenderer(
        ProjectId projectId,
        long revision,
        int outputWidth,
        int outputHeight) : IFrozenDocumentRenderer
    {
        public ProjectId ProjectId => projectId;
        public long Revision => revision;
        public IReadOnlyCollection<string> PinnedBlobHashes => Array.Empty<string>();

        public ValueTask<byte[]> RenderAsync(ExportTileRequest request, CancellationToken cancellationToken)
        {
            if (request.OutputWidth != outputWidth || request.OutputHeight != outputHeight)
                throw new InvalidDataException("Crash export dimensions changed.");
            var bytes = new byte[checked(request.Width * request.Height * 4)];
            for (var index = 0; index < bytes.Length; index += 4)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytes[index] = 40;
                bytes[index + 1] = 80;
                bytes[index + 2] = (byte)(revision + 120);
                bytes[index + 3] = 255;
            }
            return ValueTask.FromResult(bytes);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
