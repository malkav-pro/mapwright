using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Mapwright.Application;
using Mapwright.Domain;
using Microsoft.Data.Sqlite;

namespace Mapwright.Infrastructure;

public enum StorageCrashStage
{
    BeforeBlobFlush = 0,
    AfterBlobFlush = 1,
    BeforeSqliteCommit = 2,
    AfterSqliteCommit = 3,
    AfterAcknowledgement = 4,
    DuringPngWrite = 5,
    DuringPngValidation = 6,
    BeforePngPublication = 7,
    AfterPngPublication = 8
}

public sealed record RepositoryRecoveryFacts(
    ProjectId ProjectId,
    int SchemaVersion,
    long AcknowledgedRevision,
    long CursorRevision,
    long LatestRevision,
    int VerifiedBlobCount,
    int CollectedOwnedStagedBlobs,
    string BannerText);

public sealed record DurableCommitStageProfile(
    long Revision,
    double OpenAndPragmasMilliseconds,
    double SchemaMilliseconds,
    double BeginMilliseconds,
    double HistoryReadMilliseconds,
    double RedoCollapseMilliseconds,
    double CommandInsertMilliseconds,
    double RevisionInsertMilliseconds,
    double CheckpointMilliseconds,
    double HeadUpdateMilliseconds,
    double HistoryUpdateMilliseconds,
    double TransactionCommitMilliseconds,
    double TotalMilliseconds,
    long StartedTimestamp,
    long CommittedTimestamp);

public sealed class SqliteProjectRepository : IProjectRepository, IImportedProjectRepository, ICommandRehearsal,
    IConnectionHoldingRepository
{
    public const int HistoryCheckpointInterval = 64;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        // The reflection resolver the serializer would otherwise assign on first use;
        // explicit so command metadata can be prepared before the first commit.
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly string _databasePath;
    private readonly IApplicationClock _clock;
    private int _historySchemaReady;

    /// <summary>Diagnostic only. Observer failures cannot undo an acknowledged commit.</summary>
    public Action<DurableCommitStageProfile>? CommitProfileObserver { get; set; }

    public SqliteProjectRepository(string projectDirectory, IApplicationClock? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ProjectDirectory = Path.GetFullPath(projectDirectory);
        _databasePath = Path.Combine(ProjectDirectory, "scene.sqlite");
        _clock = clock ?? new SystemClock();
        // Build reflection metadata for command payloads off the edit path, so a
        // session's first durable commit does not pay it. Durability is unchanged.
        _ = Task.Run(() =>
        {
            try { PrepareCommandSerialization(); }
            catch (Exception exception)
            {
                Debug.WriteLine($"Command serialization preparation failed; first commit pays it: {exception}");
            }
        });
    }

    private static readonly Lazy<double> CommandSerializationPreparation = new(() =>
    {
        var started = Stopwatch.GetTimestamp();
        foreach (var type in typeof(IEditCommand).Assembly.GetTypes())
            if (type is { IsClass: true, IsAbstract: false } && typeof(IEditCommand).IsAssignableFrom(type))
                JsonOptions.GetTypeInfo(type);
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Idempotent. Returns the one-time metadata preparation cost in milliseconds;
    /// callers that measure edits may await it and report it as setup.
    /// </summary>
    public static double PrepareCommandSerialization() => CommandSerializationPreparation.Value;

    /// <summary>Serializes exactly as a commit would, then discards the payload.</summary>
    public void RehearseSerialization(IEditCommand command) =>
        _ = JsonSerializer.Serialize(command, command.GetType(), JsonOptions);

    public string ProjectDirectory { get; }

    public async Task<string> PublishBlobDurablyAsync(
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "blobs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        var destination = BlobPath(hash);
        if (File.Exists(destination))
        {
            ValidateBlob(hash);
            return hash;
        }

        var staged = Path.Combine(Path.GetDirectoryName(destination)!,
            $".{hash}.stage-{Guid.NewGuid():N}");
        try
        {
            await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            ValidateFileHash(staged, hash, "staged blob");
            File.Move(staged, destination, overwrite: false);
            FlushDirectoryBestEffort(Path.GetDirectoryName(destination)!);
            ValidateBlob(hash);
            return hash;
        }
        catch
        {
            if (File.Exists(staged)) File.Delete(staged);
            throw;
        }
    }

    public async Task<RepositoryRecoveryFacts> ReadRecoveryFactsAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var collected = await CollectOwnedStagedBlobsAsync(cancellationToken).ConfigureAwait(false);
        var project = await LoadAsync(projectId, cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryStateAsync(projectId, cancellationToken).ConfigureAwait(false);
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT version FROM schema_info;";
        var schemaVersion = Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false));
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        var integrityResult = Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false));
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite integrity check failed: {integrityResult}");
        var hashes = ReferencedBlobHashes(project).ToArray();
        foreach (var hash in hashes) ValidateBlob(hash);
        return new RepositoryRecoveryFacts(projectId, schemaVersion, project.Revision,
            history.CursorRevision, history.LatestRevision, hashes.Length, collected,
            $"{project.Title} was recovered. The last acknowledged revision and undo position were restored. " +
            "An unfinished gesture may be missing.");
    }

    public async Task CreateAsync(MapProject initial, CancellationToken cancellationToken = default)
    {
        if (File.Exists(_databasePath)) throw new IOException($"Project already exists: {ProjectDirectory}");
        Directory.CreateDirectory(ProjectDirectory);
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "blobs"));
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "cache", "tiles"));
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "cache", "history"));

        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);

        var connection = scope.Connection;
        await CreateSchemaAsync(connection, initial.StorageFormatVersion, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await InsertRevisionAsync(connection, (SqliteTransaction)transaction, initial, _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await InsertCheckpointAsync(connection, (SqliteTransaction)transaction, initial, _clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
        await using var current = connection.CreateCommand();
        current.Transaction = (SqliteTransaction)transaction;
        current.CommandText = """
            INSERT INTO current_project(project_id, revision)
            VALUES ($project_id, $revision);
            """;
        current.Parameters.AddWithValue("$project_id", initial.ProjectId.Value.ToString("D"));
        current.Parameters.AddWithValue("$revision", initial.Revision);
        await current.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await InsertHistoryStateAsync(connection, (SqliteTransaction)transaction, initial.ProjectId,
            initial.Revision, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _historySchemaReady, 1);
    }

    public async Task CreateImportedAsync(
        MapProject initial,
        string sourcePath,
        ReadOnlyMemory<byte> previewPng,
        CancellationToken cancellationToken = default)
    {
        var preview = initial.ImportedSource
            ?? throw new InvalidDataException("An imported project requires source provenance.");
        await CreateImportedAsync(initial, sourcePath,
            [new ImportedBlobPayload(preview.PreviewBlobHash, previewPng, "flattened preview")],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateImportedAsync(
        MapProject initial,
        string sourcePath,
        IReadOnlyCollection<ImportedBlobPayload> blobs,
        CancellationToken cancellationToken = default)
    {
        initial.ValidateConnectedTerrain();
        var imported = initial.ImportedSource
            ?? throw new InvalidDataException("An imported project requires source provenance.");
        if (initial.Revision != 0)
            throw new InvalidDataException("An imported project must begin at revision zero.");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The immutable import source is unavailable.", sourcePath);
        if (File.Exists(_databasePath) || Directory.Exists(ProjectDirectory))
            throw new IOException($"Project already exists: {ProjectDirectory}");

        Directory.CreateDirectory(ProjectDirectory);
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "blobs"));
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "cache", "tiles"));
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "cache", "history"));
        try
        {
            var sourceHash = await StoreFileBlobAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceHash, imported.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The import source changed after recovery review.");
            foreach (var blob in blobs)
            {
                var blobHash = await PublishBlobDurablyAsync(blob.Bytes, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(blobHash, blob.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The {blob.Purpose} does not match its reviewed hash.");
            }
            var requiredHashes = imported.ResolvedRasterReferences.Select(raster => raster.BlobHash)
                .Append(imported.PreviewBlobHash)
                .Concat(imported.DisplaySourceBlobHash is null
                    ? Enumerable.Empty<string>() : [imported.DisplaySourceBlobHash])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var suppliedHashes = blobs.Select(blob => blob.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requiredHashes.Any(hash => !suppliedHashes.Contains(hash)))
                throw new InvalidDataException("One or more preserved import blobs were not supplied for publication.");

            await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);

            var connection = scope.Connection;
            await CreateSchemaAsync(connection, initial.StorageFormatVersion, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await InsertRevisionAsync(connection, (SqliteTransaction)transaction, initial, _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            await InsertCheckpointAsync(connection, (SqliteTransaction)transaction, initial, _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            await InsertImportedSourceAsync(connection, (SqliteTransaction)transaction, initial.ProjectId,
                imported, cancellationToken).ConfigureAwait(false);
            await using var current = connection.CreateCommand();
            current.Transaction = (SqliteTransaction)transaction;
            current.CommandText = "INSERT INTO current_project(project_id, revision) VALUES ($project_id, 0);";
            current.Parameters.AddWithValue("$project_id", initial.ProjectId.Value.ToString("D"));
            await current.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await InsertHistoryStateAsync(connection, (SqliteTransaction)transaction, initial.ProjectId,
                initial.Revision, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _historySchemaReady, 1);
        }
        catch
        {
            if (Directory.Exists(ProjectDirectory)) Directory.Delete(ProjectDirectory, recursive: true);
            throw;
        }
    }

    public async Task<MapProject> LoadAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _historySchemaReady, 1);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cursor_revision FROM history_state WHERE project_id = $project_id;";
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        var revisionValue = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project {projectId.Value} does not exist in {ProjectDirectory}.");
        var baseline = await LoadBaselineSnapshotAsync(connection, projectId, cancellationToken).ConfigureAwait(false);
        var reconstruction = await ReconstructRevisionAsync(projectId, Convert.ToInt64(revisionValue),
            HistoryCompatibility.For(baseline), null, cancellationToken).ConfigureAwait(false);
        return reconstruction.Project;
    }

    public async Task<MapProject> LoadRevisionAsync(
        ProjectId projectId,
        long revision,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        var state = await ReadHistoryStateWithoutTransactionAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        if (revision == state.BaselineRevision)
        {
            var authoritativeBaseline = await LoadBaselineSnapshotAsync(connection, projectId, cancellationToken)
                .ConfigureAwait(false);
            await ValidateLoadedProjectAsync(connection, authoritativeBaseline, cancellationToken)
                .ConfigureAwait(false);
            return authoritativeBaseline;
        }
        var baseline = await LoadBaselineSnapshotAsync(connection, projectId, cancellationToken).ConfigureAwait(false);
        var reconstruction = await ReconstructRevisionAsync(projectId, revision,
            HistoryCompatibility.For(baseline), null, cancellationToken).ConfigureAwait(false);
        return reconstruction.Project;
    }

    public async Task<MapProject> LoadResidentRevisionAsync(
        ProjectId projectId,
        long revision,
        MapProject verifiedCurrent,
        CancellationToken cancellationToken = default)
    {
        if (verifiedCurrent.ProjectId != projectId ||
            Math.Abs(verifiedCurrent.Revision - revision) != 1)
            throw new InvalidOperationException("Resident load requires an adjacent verified project revision.");
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        var state = await ReadHistoryStateWithoutTransactionAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        if (revision != state.BaselineRevision)
            return await LoadRevisionAsync(projectId, revision, cancellationToken).ConfigureAwait(false);

        var baseline = await LoadBaselineSnapshotAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        if (baseline.ProjectId != verifiedCurrent.ProjectId ||
            baseline.ImportedSource != verifiedCurrent.ImportedSource ||
            !ReferencedBlobHashes(baseline).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(ReferencedBlobHashes(verifiedCurrent)))
            throw new InvalidDataException("Resident baseline source identities differ from the verified revision.");
        // The active revision was already verified on load/commit. Check durable schema
        // and source metadata here without rehashing the same immutable 74 MB blob on
        // each adjacent undo; normal load/reconstruction still performs full hashes.
        await ValidateLoadedProjectAsync(connection, baseline, cancellationToken, verifyBlobs: false)
            .ConfigureAwait(false);
        return baseline;
    }

    public async Task<DurableCommit> CommitAsync(
        MapProject previous,
        IEditCommand command,
        DocumentChange change,
        CancellationToken cancellationToken)
    {
        if (command.BaseRevision != previous.Revision || change.Project.Revision != previous.Revision + 1)
            throw new InvalidDataException("Command, previous state and next revision are inconsistent.");
        if (change.Project.ProjectId != previous.ProjectId)
            throw new InvalidDataException("A commit cannot change project identity.");

        var profileStart = Stopwatch.GetTimestamp();
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        var opened = Stopwatch.GetTimestamp();
        // Loaded/new repositories have already migrated history. Avoid executing the
        // DDL/INSERT migration on every stroke while retaining the first-use path for
        // callers that provide an external snapshot without loading it here.
        if (Volatile.Read(ref _historySchemaReady) == 0)
        {
            await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _historySchemaReady, 1);
        }
        var schemaReady = Stopwatch.GetTimestamp();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var began = Stopwatch.GetTimestamp();
        var history = await ReadHistoryStateAsync(connection, (SqliteTransaction)transaction,
            previous.ProjectId, cancellationToken).ConfigureAwait(false);
        var historyRead = Stopwatch.GetTimestamp();
        if (history.CursorRevision != previous.Revision)
            throw new RevisionConflictException(previous.Revision, history.CursorRevision);

        if (history.CursorRevision < history.LatestRevision)
        {
            await CollapseRedoTipAsync(connection, (SqliteTransaction)transaction, previous.ProjectId,
                history.CursorRevision, cancellationToken).ConfigureAwait(false);
        }
        var redoCollapsed = Stopwatch.GetTimestamp();

        var committedAt = _clock.UtcNow;
        await InsertCommandAsync(connection, (SqliteTransaction)transaction, previous.ProjectId, command,
            change.Project.Revision, committedAt, change.Invalidation, cancellationToken).ConfigureAwait(false);
        var commandInserted = Stopwatch.GetTimestamp();
        await InsertRevisionAsync(connection, (SqliteTransaction)transaction, change.Project, committedAt,
            cancellationToken, storeSnapshot: false).ConfigureAwait(false);
        var revisionInserted = Stopwatch.GetTimestamp();
        if ((change.Project.Revision - history.BaselineRevision) % HistoryCheckpointInterval == 0)
        {
            await InsertCheckpointAsync(connection, (SqliteTransaction)transaction, change.Project, committedAt,
                cancellationToken).ConfigureAwait(false);
        }
        var checkpointDone = Stopwatch.GetTimestamp();

        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE current_project
            SET revision = $next_revision
            WHERE project_id = $project_id AND revision = $previous_revision;
            """;
        update.Parameters.AddWithValue("$next_revision", change.Project.Revision);
        update.Parameters.AddWithValue("$project_id", previous.ProjectId.Value.ToString("D"));
        update.Parameters.AddWithValue("$previous_revision", previous.Revision);
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new RevisionConflictException(previous.Revision, history.CursorRevision);
        var headUpdated = Stopwatch.GetTimestamp();

        await using var historyUpdate = connection.CreateCommand();
        historyUpdate.Transaction = (SqliteTransaction)transaction;
        historyUpdate.CommandText = """
            UPDATE history_state
            SET cursor_revision = $next_revision, latest_revision = $next_revision
            WHERE project_id = $project_id AND cursor_revision = $previous_revision;
            """;
        historyUpdate.Parameters.AddWithValue("$next_revision", change.Project.Revision);
        historyUpdate.Parameters.AddWithValue("$project_id", previous.ProjectId.Value.ToString("D"));
        historyUpdate.Parameters.AddWithValue("$previous_revision", previous.Revision);
        if (await historyUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new RevisionConflictException(previous.Revision, history.CursorRevision);
        var historyUpdated = Stopwatch.GetTimestamp();

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var transactionCommitted = Stopwatch.GetTimestamp();
        if (CommitProfileObserver is { } observer)
        {
            static double Ms(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            try
            {
                observer(new DurableCommitStageProfile(change.Project.Revision,
                    Ms(profileStart, opened), Ms(opened, schemaReady), Ms(schemaReady, began),
                    Ms(began, historyRead), Ms(historyRead, redoCollapsed),
                    Ms(redoCollapsed, commandInserted), Ms(commandInserted, revisionInserted),
                    Ms(revisionInserted, checkpointDone), Ms(checkpointDone, headUpdated),
                    Ms(headUpdated, historyUpdated), Ms(historyUpdated, transactionCommitted),
                    Ms(profileStart, transactionCommitted), profileStart, transactionCommitted));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Commit profile observer failed after durable commit: {exception}");
            }
        }
        return new DurableCommit(command.Id, change.Project.Revision, committedAt);
    }

    public async Task<HistoryState> ReadHistoryStateAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT baseline_revision, cursor_revision, latest_revision
            FROM history_state
            WHERE project_id = $project_id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException($"Project {projectId.Value} has no history state.");
        return new HistoryState(projectId, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    public async Task<HistoryCursorCommit> MoveHistoryCursorAsync(
        ProjectId projectId,
        long expectedCursorRevision,
        long targetRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryStateAsync(connection, (SqliteTransaction)transaction, projectId,
            cancellationToken).ConfigureAwait(false);
        if (history.CursorRevision != expectedCursorRevision)
            throw new RevisionConflictException(expectedCursorRevision, history.CursorRevision);
        if (targetRevision < history.BaselineRevision || targetRevision > history.LatestRevision)
            throw new ArgumentOutOfRangeException(nameof(targetRevision));

        await using (var exists = connection.CreateCommand())
        {
            exists.Transaction = (SqliteTransaction)transaction;
            exists.CommandText = """
                SELECT COUNT(*) FROM revisions
                WHERE project_id = $project_id AND revision = $target_revision;
                """;
            exists.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
            exists.Parameters.AddWithValue("$target_revision", targetRevision);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
                throw new KeyNotFoundException($"Revision {targetRevision} does not exist.");
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE history_state
                SET cursor_revision = $target_revision
                WHERE project_id = $project_id AND cursor_revision = $expected_revision;

                UPDATE current_project
                SET revision = $target_revision
                WHERE project_id = $project_id AND revision = $expected_revision;
                """;
            update.Parameters.AddWithValue("$target_revision", targetRevision);
            update.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
            update.Parameters.AddWithValue("$expected_revision", expectedCursorRevision);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var committedAt = _clock.UtcNow;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new HistoryCursorCommit(CommandId.New(), expectedCursorRevision, targetRevision, committedAt);
    }

    public async Task<IReadOnlyList<HistoryEntry>> ReadHistoryPageAsync(
        ProjectId projectId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, command_id, base_revision, revision, command_type, committed_utc,
                   tile_count, delta_bytes
            FROM commands
            WHERE project_id = $project_id
            ORDER BY sequence ASC
            LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var entries = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var commandType = reader.GetString(4);
            entries.Add(new HistoryEntry(
                reader.GetInt64(0),
                new CommandId(Guid.Parse(reader.GetString(1))),
                reader.GetInt64(2),
                reader.GetInt64(3),
                commandType,
                HumanizeCommandType(commandType),
                DateTimeOffset.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt32(6),
                reader.GetInt64(7)));
        }
        return entries;
    }

    public async Task<HistoryWindow> ReadHistoryWindowAsync(
        ProjectId projectId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var state = await ReadHistoryStateAsync(connection, (SqliteTransaction)transaction, projectId,
            cancellationToken).ConfigureAwait(false);

        int total;
        await using (var count = connection.CreateCommand())
        {
            count.Transaction = (SqliteTransaction)transaction;
            count.CommandText = "SELECT COUNT(*) FROM commands WHERE project_id = $project_id;";
            count.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        var entries = new List<HistoryEntry>();
        await using (var page = connection.CreateCommand())
        {
            page.Transaction = (SqliteTransaction)transaction;
            page.CommandText = """
                SELECT sequence, command_id, base_revision, revision, command_type, committed_utc,
                       tile_count, delta_bytes
                FROM commands
                WHERE project_id = $project_id
                ORDER BY sequence ASC
                LIMIT $limit OFFSET $offset;
                """;
            page.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
            page.Parameters.AddWithValue("$limit", limit);
            page.Parameters.AddWithValue("$offset", offset);
            await using var reader = await page.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                entries.Add(ReadHistoryEntry(reader));
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new HistoryWindow(offset, total, state, entries);
    }

    public async Task<HistoryEntry?> ReadHistoryEntryAsync(
        ProjectId projectId,
        long revision,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, command_id, base_revision, revision, command_type, committed_utc,
                   tile_count, delta_bytes
            FROM commands
            WHERE project_id = $project_id AND revision = $revision;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$revision", revision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadHistoryEntry(reader)
            : null;
    }

    public async Task<HistoryReconstructionResult> ReconstructRevisionAsync(
        ProjectId projectId,
        long revision,
        HistoryCompatibility compatibility,
        IProgress<HistoryReconstructionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        compatibility.Validate();
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        var state = await ReadHistoryStateWithoutTransactionAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        if (revision < state.BaselineRevision || revision > state.LatestRevision)
            throw new ArgumentOutOfRangeException(nameof(revision));

        var baseline = await LoadBaselineSnapshotAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        var checkpoint = await ReadCompatibleCheckpointAsync(connection, projectId, revision, compatibility,
            cancellationToken).ConfigureAwait(false);
        var start = checkpoint?.Project ?? baseline;
        var startRevision = start.Revision;
        var mismatch = checkpoint is null && await HasCheckpointAsync(connection, projectId, revision,
            cancellationToken).ConfigureAwait(false);

        var commands = await ReadCommandsAsync(connection, projectId, startRevision, revision, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new HistoryReconstructionProgress(0, commands.Count, revision));
        var reconstructed = start;
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reconstructed = commands[index].Apply(reconstructed).Project;
            progress?.Report(new HistoryReconstructionProgress(index + 1, commands.Count, revision));
            if ((index + 1) % HistoryCheckpointInterval == 0) await Task.Yield();
        }

        if (reconstructed.Revision != revision)
            throw new InvalidDataException(
                $"History replay ended at revision {reconstructed.Revision}, expected {revision}.");
        await ValidateLoadedProjectAsync(connection, reconstructed, cancellationToken).ConfigureAwait(false);
        return new HistoryReconstructionResult(reconstructed, commands.Count, checkpoint is not null, mismatch);
    }

    public async Task DeleteHistoryAccelerationAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var connection = scope.Connection;
        await EnsureHistorySchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        _ = await ReadHistoryStateWithoutTransactionAsync(connection, projectId, cancellationToken)
            .ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM history_checkpoints WHERE project_id = $project_id;";
            command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var historyCache = Path.GetFullPath(Path.Combine(ProjectDirectory, "cache", "history"));
        var projectRoot = Path.GetFullPath(ProjectDirectory) + Path.DirectorySeparatorChar;
        if (!historyCache.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("History cache path escaped the project directory.");
        if (Directory.Exists(historyCache))
        {
            foreach (var file in Directory.EnumerateFiles(historyCache, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
            }
            foreach (var directory in Directory.EnumerateDirectories(historyCache, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
                Directory.Delete(directory, recursive: false);
        }
        Directory.CreateDirectory(historyCache);
    }

    private static readonly AsyncLocal<SqliteProjectRepository?> HeldConnectionOwner = new();
    private readonly SemaphoreSlim _heldGate = new(1, 1);
    private SqliteConnection? _heldConnection;
    private int _holdCount;

    /// <summary>
    /// Keeps one connection open while an editing session is active. Every repository
    /// operation then shares it (exclusively, in order) instead of opening and closing
    /// its own. Closing the last connection to a WAL database checkpoints and deletes
    /// the WAL, so per-operation connections paid that synchronous work on every edit.
    /// Commits remain `synchronous=FULL` WAL commits; acknowledgement is unchanged.
    /// The connection closes when the last hold is released.
    /// </summary>
    public IConnectionHold HoldConnection()
    {
        Interlocked.Increment(ref _holdCount);
        // Open the session connection and its WAL/shared-memory files now, off the edit
        // path, so the first edit does not pay connection and journal-file setup.
        return new ConnectionHold(this, Task.Run(PrepareHeldConnectionAsync));
    }

    private async Task PrepareHeldConnectionAsync()
    {
        try
        {
            if (!File.Exists(_databasePath)) return;
            await using var scope = await OpenAsync(CancellationToken.None).ConfigureAwait(false);
            await using var command = scope.Connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
            // A new session's WAL starts empty; its first durable appends pay file-system
            // allocation behind a synchronous flush (measured 35-48 ms). Rewriting the
            // unused user_version with its own value performs those first FULL-synchronous
            // WAL commits here, when the project opens, and changes no data.
            command.CommandText = $"PRAGMA user_version = {version};";
            for (var warm = 0; warm < 2; warm++)
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or
                                          InvalidOperationException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Held connection preparation deferred to first use: {exception.Message}");
        }
    }

    private sealed class ConnectionHold(SqliteProjectRepository owner, Task prepared) : IConnectionHold
    {
        private int _released;

        public Task Prepared => prepared;

        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _released, 1) == 0
            ? owner.ReleaseHoldAsync()
            : ValueTask.CompletedTask;
    }

    private async ValueTask ReleaseHoldAsync()
    {
        if (Interlocked.Decrement(ref _holdCount) != 0) return;
        await _heldGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _holdCount) == 0 && _heldConnection is { } connection)
            {
                _heldConnection = null;
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _heldGate.Release();
        }
    }

    private sealed class ConnectionScope(
        SqliteConnection connection, bool ownsConnection, SemaphoreSlim? gate, bool clearsOwner)
        : IAsyncDisposable
    {
        public SqliteConnection Connection => connection;

        // Deliberately not async: clearing the AsyncLocal must affect the caller's flow.
        public ValueTask DisposeAsync()
        {
            if (clearsOwner) HeldConnectionOwner.Value = null;
            gate?.Release();
            return ownsConnection ? connection.DisposeAsync() : ValueTask.CompletedTask;
        }
    }

    // Deliberately not async: marking the owner must persist in the caller's flow so
    // nested repository calls (Load -> Reconstruct) reuse the scope instead of waiting
    // on the gate they already hold.
    private Task<ConnectionScope> OpenAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _holdCount) == 0)
            return OpenOwnedAsync(cancellationToken, clearsOwner: false);
        if (ReferenceEquals(HeldConnectionOwner.Value, this) && _heldConnection is { } nested)
            return Task.FromResult(new ConnectionScope(nested, false, null, false));
        HeldConnectionOwner.Value = this;
        return AcquireHeldAsync(cancellationToken);
    }

    private async Task<ConnectionScope> OpenOwnedAsync(CancellationToken cancellationToken, bool clearsOwner) =>
        new(await OpenConnectionAsync(cancellationToken).ConfigureAwait(false), true, null, clearsOwner);

    private async Task<ConnectionScope> AcquireHeldAsync(CancellationToken cancellationToken)
    {
        await _heldGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var gateHeld = true;
        try
        {
            if (Volatile.Read(ref _holdCount) == 0)
            {
                _heldGate.Release();
                gateHeld = false;
                return await OpenOwnedAsync(cancellationToken, clearsOwner: true).ConfigureAwait(false);
            }
            _heldConnection ??= await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = false;
            return new ConnectionScope(_heldConnection, false, _heldGate, true);
        }
        catch
        {
            if (gateHeld) _heldGate.Release();
            throw;
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, int formatVersion,
        CancellationToken cancellationToken)
    {
        if (formatVersion is not (2 or 3))
            throw new InvalidDataException($"Unsupported connected project format {formatVersion}.");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_info(
                version INTEGER NOT NULL
            ) STRICT;
            INSERT INTO schema_info(version) VALUES ($format_version);

            CREATE TABLE revisions(
                revision INTEGER PRIMARY KEY,
                project_id TEXT NOT NULL,
                committed_utc TEXT NOT NULL,
                snapshot_json TEXT NOT NULL
            ) STRICT;

            CREATE TABLE commands(
                sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                command_id TEXT NOT NULL UNIQUE,
                project_id TEXT NOT NULL,
                base_revision INTEGER NOT NULL,
                revision INTEGER NOT NULL UNIQUE,
                command_type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                committed_utc TEXT NOT NULL,
                tile_count INTEGER NOT NULL DEFAULT 0,
                delta_bytes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED
            ) STRICT;

            CREATE TABLE current_project(
                project_id TEXT PRIMARY KEY,
                revision INTEGER NOT NULL,
                FOREIGN KEY(revision) REFERENCES revisions(revision)
            ) STRICT;

            CREATE TABLE history_state(
                project_id TEXT PRIMARY KEY,
                baseline_revision INTEGER NOT NULL,
                cursor_revision INTEGER NOT NULL,
                latest_revision INTEGER NOT NULL,
                FOREIGN KEY(baseline_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(cursor_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(latest_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                CHECK(cursor_revision <= latest_revision)
            ) STRICT;

            CREATE TABLE history_checkpoints(
                revision INTEGER PRIMARY KEY,
                project_id TEXT NOT NULL,
                renderer_version TEXT NOT NULL,
                recipe_version INTEGER NOT NULL,
                source_identity TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(revision) REFERENCES revisions(revision) ON DELETE CASCADE
            ) STRICT;

            CREATE TABLE imported_source(
                project_id TEXT PRIMARY KEY,
                source_file_name TEXT NOT NULL,
                source_sha256 TEXT NOT NULL,
                source_blob_hash TEXT NOT NULL,
                preview_blob_hash TEXT NOT NULL,
                preview_width INTEGER NOT NULL,
                preview_height INTEGER NOT NULL,
                recovery_mode INTEGER NOT NULL
            ) STRICT;
            """;
        command.Parameters.AddWithValue("$format_version", formatVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureHistorySchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history_state(
                project_id TEXT PRIMARY KEY,
                baseline_revision INTEGER NOT NULL,
                cursor_revision INTEGER NOT NULL,
                latest_revision INTEGER NOT NULL,
                FOREIGN KEY(baseline_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(cursor_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                FOREIGN KEY(latest_revision) REFERENCES revisions(revision) DEFERRABLE INITIALLY DEFERRED,
                CHECK(cursor_revision <= latest_revision)
            ) STRICT;

            INSERT OR IGNORE INTO history_state(project_id, baseline_revision, cursor_revision, latest_revision)
            SELECT project_id, revision, revision, revision FROM current_project;

            CREATE TABLE IF NOT EXISTS history_checkpoints(
                revision INTEGER PRIMARY KEY,
                project_id TEXT NOT NULL,
                renderer_version TEXT NOT NULL,
                recipe_version INTEGER NOT NULL,
                source_identity TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(revision) REFERENCES revisions(revision) ON DELETE CASCADE
            ) STRICT;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCommandMetadataColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureCommandMetadataColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(commands);";
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("tile_count"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE commands ADD COLUMN tile_count INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!columns.Contains("delta_bytes"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE commands ADD COLUMN delta_bytes INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertHistoryStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectId projectId,
        long revision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO history_state(project_id, baseline_revision, cursor_revision, latest_revision)
            VALUES ($project_id, $revision, $revision, $revision);
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$revision", revision);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCheckpointAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MapProject project,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var compatibility = HistoryCompatibility.For(project);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO history_checkpoints(
                revision, project_id, renderer_version, recipe_version,
                source_identity, snapshot_json, created_utc)
            VALUES (
                $revision, $project_id, $renderer_version, $recipe_version,
                $source_identity, $snapshot_json, $created_utc);
            """;
        command.Parameters.AddWithValue("$revision", project.Revision);
        command.Parameters.AddWithValue("$project_id", project.ProjectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$renderer_version", compatibility.RendererVersion);
        command.Parameters.AddWithValue("$recipe_version", compatibility.RecipeVersion);
        command.Parameters.AddWithValue("$source_identity", compatibility.SourceIdentity);
        command.Parameters.AddWithValue("$snapshot_json", JsonSerializer.Serialize(project, JsonOptions));
        command.Parameters.AddWithValue("$created_utc", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HistoryState> ReadHistoryStateWithoutTransactionAsync(
        SqliteConnection connection,
        ProjectId projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT baseline_revision, cursor_revision, latest_revision
            FROM history_state
            WHERE project_id = $project_id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException($"Project {projectId.Value} has no history state.");
        return new HistoryState(projectId, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<MapProject> LoadBaselineSnapshotAsync(
        SqliteConnection connection,
        ProjectId projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.snapshot_json
            FROM history_state h
            JOIN revisions r ON r.revision = h.baseline_revision
            WHERE h.project_id = $project_id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The authoritative history baseline is missing.");
        return DeserializeProject(json);
    }

    private static async Task<CheckpointRead?> ReadCompatibleCheckpointAsync(
        SqliteConnection connection,
        ProjectId projectId,
        long targetRevision,
        HistoryCompatibility compatibility,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision, snapshot_json
            FROM history_checkpoints
            WHERE project_id = $project_id
              AND revision <= $target_revision
              AND renderer_version = $renderer_version
              AND recipe_version = $recipe_version
              AND source_identity = $source_identity
            ORDER BY revision DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$target_revision", targetRevision);
        command.Parameters.AddWithValue("$renderer_version", compatibility.RendererVersion);
        command.Parameters.AddWithValue("$recipe_version", compatibility.RecipeVersion);
        command.Parameters.AddWithValue("$source_identity", compatibility.SourceIdentity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var revision = reader.GetInt64(0);
        var project = DeserializeProject(reader.GetString(1));
        if (project.Revision != revision)
            throw new InvalidDataException($"Checkpoint {revision} contains revision {project.Revision}.");
        return new CheckpointRead(project);
    }

    private static async Task<bool> HasCheckpointAsync(
        SqliteConnection connection,
        ProjectId projectId,
        long targetRevision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM history_checkpoints
            WHERE project_id = $project_id AND revision <= $target_revision;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$target_revision", targetRevision);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    private static async Task<List<IEditCommand>> ReadCommandsAsync(
        SqliteConnection connection,
        ProjectId projectId,
        long afterRevision,
        long targetRevision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT command_type, payload_json
            FROM commands
            WHERE project_id = $project_id
              AND revision > $after_revision
              AND revision <= $target_revision
            ORDER BY sequence ASC;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$after_revision", afterRevision);
        command.Parameters.AddWithValue("$target_revision", targetRevision);
        var commands = new List<IEditCommand>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            commands.Add(DeserializeCommand(reader.GetString(0), reader.GetString(1)));
        return commands;
    }

    internal static IEditCommand DeserializeCommand(string commandType, string json) => commandType switch
    {
        nameof(AddPaintStroke) => DeserializeCommand<AddPaintStroke>(json),
        nameof(AddTextureStroke) => DeserializeCommand<AddTextureStroke>(json),
        nameof(AddResolvedTextureStroke) => DeserializeCommand<AddResolvedTextureStroke>(json),
        nameof(AddLandStroke) => DeserializeCommand<AddLandStroke>(json),
        nameof(RenameTerrainLayer) => DeserializeCommand<RenameTerrainLayer>(json),
        nameof(SetTerrainVisibility) => DeserializeCommand<SetTerrainVisibility>(json),
        nameof(SetTerrainLock) => DeserializeCommand<SetTerrainLock>(json),
        nameof(SetTerrainSolo) => DeserializeCommand<SetTerrainSolo>(json),
        nameof(SetTerrainOpacity) => DeserializeCommand<SetTerrainOpacity>(json),
        nameof(MoveRiverPoint) => DeserializeCommand<MoveRiverPoint>(json),
        nameof(SetRiverWidth) => DeserializeCommand<SetRiverWidth>(json),
        nameof(CreateRiver) => DeserializeCommand<CreateRiver>(json),
        nameof(InsertRiverPoint) => DeserializeCommand<InsertRiverPoint>(json),
        nameof(DeleteRiverPoint) => DeserializeCommand<DeleteRiverPoint>(json),
        nameof(SetRiverPointWidth) => DeserializeCommand<SetRiverPointWidth>(json),
        nameof(SetAllRiverWidths) => DeserializeCommand<SetAllRiverWidths>(json),
        nameof(SetRiverBankSoftness) => DeserializeCommand<SetRiverBankSoftness>(json),
        nameof(SetRiverEnabled) => DeserializeCommand<SetRiverEnabled>(json),
        nameof(DeleteRiver) => DeserializeCommand<DeleteRiver>(json),
        _ => throw new InvalidDataException($"Unsupported retained command type '{commandType}'.")
    };

    private static T DeserializeCommand<T>(string json) where T : IEditCommand =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} command is empty.");

    private sealed record CheckpointRead(MapProject Project);

    private static async Task<HistoryState> ReadHistoryStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectId projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT baseline_revision, cursor_revision, latest_revision
            FROM history_state
            WHERE project_id = $project_id;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new KeyNotFoundException($"Project {projectId.Value} has no history state.");
        return new HistoryState(projectId, reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task CollapseRedoTipAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectId projectId,
        long cursorRevision,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE history_state
            SET latest_revision = cursor_revision
            WHERE project_id = $project_id AND cursor_revision = $cursor_revision;

            DELETE FROM commands
            WHERE project_id = $project_id AND revision > $cursor_revision;

            DELETE FROM revisions
            WHERE project_id = $project_id AND revision > $cursor_revision;
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$cursor_revision", cursorRevision);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ReadCurrentRevisionAsync(SqliteConnection connection,
        SqliteTransaction transaction, ProjectId projectId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM current_project WHERE project_id = $project_id;";
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Project {projectId.Value} is not initialized.");
        return Convert.ToInt64(value);
    }

    private static async Task InsertRevisionAsync(SqliteConnection connection, SqliteTransaction transaction,
        MapProject project, DateTimeOffset committedAt, CancellationToken cancellationToken,
        bool storeSnapshot = true)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO revisions(revision, project_id, committed_utc, snapshot_json)
            VALUES ($revision, $project_id, $committed_utc, $snapshot_json);
            """;
        command.Parameters.AddWithValue("$revision", project.Revision);
        command.Parameters.AddWithValue("$project_id", project.ProjectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$committed_utc", committedAt.ToString("O"));
        command.Parameters.AddWithValue("$snapshot_json",
            storeSnapshot ? JsonSerializer.Serialize(project, JsonOptions) : string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertCommandAsync(SqliteConnection connection, SqliteTransaction transaction,
        ProjectId projectId, IEditCommand edit, long revision, DateTimeOffset committedAt,
        TileInvalidation invalidation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO commands(
                command_id, project_id, base_revision, revision, command_type,
                payload_json, committed_utc, tile_count, delta_bytes)
            VALUES (
                $command_id, $project_id, $base_revision, $revision, $command_type,
                $payload_json, $committed_utc, $tile_count, 0);
            """;
        command.Parameters.AddWithValue("$command_id", edit.Id.Value.ToString("D"));
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$base_revision", edit.BaseRevision);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$command_type", edit.GetType().Name);
        command.Parameters.AddWithValue("$payload_json", JsonSerializer.Serialize(edit, edit.GetType(), JsonOptions));
        command.Parameters.AddWithValue("$committed_utc", committedAt.ToString("O"));
        command.Parameters.AddWithValue("$tile_count", CountTiles(invalidation.Bounds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertImportedSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectId projectId,
        ImportedSourceReference source,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO imported_source(
                project_id, source_file_name, source_sha256, source_blob_hash,
                preview_blob_hash, preview_width, preview_height, recovery_mode)
            VALUES (
                $project_id, $source_file_name, $source_sha256, $source_blob_hash,
                $preview_blob_hash, $preview_width, $preview_height, $recovery_mode);
            """;
        command.Parameters.AddWithValue("$project_id", projectId.Value.ToString("D"));
        command.Parameters.AddWithValue("$source_file_name", source.SourceFileName);
        command.Parameters.AddWithValue("$source_sha256", source.SourceSha256);
        command.Parameters.AddWithValue("$source_blob_hash", source.SourceBlobHash);
        command.Parameters.AddWithValue("$preview_blob_hash", source.PreviewBlobHash);
        command.Parameters.AddWithValue("$preview_width", source.PreviewWidth);
        command.Parameters.AddWithValue("$preview_height", source.PreviewHeight);
        command.Parameters.AddWithValue("$recovery_mode", (int)source.RecoveryMode);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateLoadedProjectAsync(
        SqliteConnection connection,
        MapProject project,
        CancellationToken cancellationToken,
        bool verifyBlobs = true)
    {
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT version FROM schema_info;";
        var version = Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        if (version != project.StorageFormatVersion)
            throw new InvalidDataException($"Schema version {version} does not match snapshot format {project.StorageFormatVersion}.");

        if (project.ImportedSource is null) return;
        project.ValidateConnectedTerrain();
        await using var source = connection.CreateCommand();
        source.CommandText = """
            SELECT source_sha256, source_blob_hash, preview_blob_hash
            FROM imported_source WHERE project_id = $project_id;
            """;
        source.Parameters.AddWithValue("$project_id", project.ProjectId.Value.ToString("D"));
        await using var reader = await source.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("The imported source reference is missing from SQLite.");
        if (!string.Equals(reader.GetString(0), project.ImportedSource.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1), project.ImportedSource.SourceBlobHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(2), project.ImportedSource.PreviewBlobHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite source references disagree with the immutable revision.");

        if (!verifyBlobs) return;
        ValidateBlob(project.ImportedSource.SourceBlobHash);
        ValidateBlob(project.ImportedSource.PreviewBlobHash);
        foreach (var raster in project.ImportedSource.ResolvedRasterReferences)
            ValidateBlob(raster.BlobHash);
        foreach (var layer in project.TerrainLayers.Where(layer => layer.SourceBlobHash is not null))
            ValidateBlob(layer.SourceBlobHash!);
        foreach (var layer in project.TerrainLayers.Where(layer => layer.CoverageSourceBlobHash is not null))
            ValidateBlob(layer.CoverageSourceBlobHash!);
    }

    private async Task<string> StoreFileBlobAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
        source.Position = 0;
        var destination = BlobPath(hash);
        if (!File.Exists(destination))
        {
            var temporary = Path.Combine(Path.GetDirectoryName(destination)!,
                $".{hash}.stage-{Guid.NewGuid():N}");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            ValidateFileHash(temporary, hash, "staged source blob");
            File.Move(temporary, destination, overwrite: false);
            FlushDirectoryBestEffort(Path.GetDirectoryName(destination)!);
        }
        ValidateBlob(hash);
        return hash;
    }

    private void ValidateBlob(string hash)
    {
        var path = BlobPath(hash);
        if (!File.Exists(path)) throw new InvalidDataException($"Missing source blob {hash}.");
        ValidateFileHash(path, hash, "source blob");
    }

    private string BlobPath(string hash) => Path.Combine(ProjectDirectory, "blobs", hash);

    private async Task<int> CollectOwnedStagedBlobsAsync(CancellationToken cancellationToken)
    {
        var blobDirectory = Path.Combine(ProjectDirectory, "blobs");
        if (!Directory.Exists(blobDirectory)) return 0;
        var collected = 0;
        foreach (var path in Directory.EnumerateFiles(blobDirectory, ".*.stage-*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            if (name.Length < 1 + 64 + ".stage-".Length || name[0] != '.' ||
                !name.AsSpan(65).StartsWith(".stage-", StringComparison.Ordinal))
                continue;
            var expectedHash = name.Substring(1, 64);
            if (!expectedHash.All(character => char.IsAsciiHexDigit(character))) continue;
            try
            {
                await using var owned = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(owned, cancellationToken)
                        .ConfigureAwait(false))
                    .ToLowerInvariant();
                if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)) continue;
            }
            catch (IOException)
            {
                continue;
            }
            File.Delete(path);
            collected++;
        }
        if (collected > 0) FlushDirectoryBestEffort(blobDirectory);
        return collected;
    }

    private static IEnumerable<string> ReferencedBlobHashes(MapProject project)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? hash)
        {
            if (hash is { Length: 64 }) hashes.Add(hash.ToLowerInvariant());
        }
        if (project.ImportedSource is not null)
        {
            Add(project.ImportedSource.SourceBlobHash);
            Add(project.ImportedSource.PreviewBlobHash);
            Add(project.ImportedSource.DisplaySourceBlobHash);
            foreach (var raster in project.ImportedSource.ResolvedRasterReferences) Add(raster.BlobHash);
        }
        foreach (var layer in project.TerrainLayers)
        {
            Add(layer.SourceBlobHash);
            Add(layer.CoverageSourceBlobHash);
            foreach (var stroke in layer.ResolvedTextureStrokes) Add(stroke.Recipe.TextureSha256);
        }
        return hashes.Order(StringComparer.Ordinal);
    }

    private static void ValidateFileHash(string path, string expectedHash, string description)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{description} hash mismatch for {expectedHash}.");
    }

    private static void FlushDirectoryBestEffort(string directory)
    {
        try
        {
            using var handle = File.OpenHandle(directory, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            RandomAccess.FlushToDisk(handle);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or
                                          PlatformNotSupportedException)
        {
            // Individual blob files were flushed. Some .NET/filesystem combinations do not expose
            // a flushable directory handle.
        }
    }

    private static HistoryEntry ReadHistoryEntry(SqliteDataReader reader)
    {
        var commandType = reader.GetString(4);
        return new HistoryEntry(
            reader.GetInt64(0),
            new CommandId(Guid.Parse(reader.GetString(1))),
            reader.GetInt64(2),
            reader.GetInt64(3),
            commandType,
            HumanizeCommandType(commandType),
            DateTimeOffset.Parse(reader.GetString(5), null,
                System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetInt32(6),
            reader.GetInt64(7));
    }

    private static int CountTiles(MapBounds bounds)
    {
        const double tileSize = 512;
        var width = Math.Max(0, bounds.Right - bounds.Left);
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        if (width == 0 && height == 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(width / tileSize)) *
               Math.Max(1, (int)Math.Ceiling(height / tileSize));
    }

    private static string HumanizeCommandType(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType)) return "Edit";
        var label = new System.Text.StringBuilder(commandType.Length + 8);
        for (var index = 0; index < commandType.Length; index++)
        {
            var character = commandType[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(commandType[index - 1]))
                label.Append(' ');
            label.Append(character);
        }
        return label.ToString();
    }

    private static MapProject DeserializeProject(string json) =>
        JsonSerializer.Deserialize<MapProject>(json, JsonOptions)
        ?? throw new InvalidDataException("Stored project snapshot is empty.");
}
