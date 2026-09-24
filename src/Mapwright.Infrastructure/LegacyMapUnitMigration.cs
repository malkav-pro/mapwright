using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Mapwright.Domain;
using Microsoft.Data.Sqlite;

namespace Mapwright.Infrastructure;

public enum LegacyProjectOpenStatus
{
    Current,
    Migrated,
    Refused
}

public enum LegacyMigrationStage
{
    AfterBackup,
    AfterBlobCopy,
    BeforePublication
}

public sealed record LegacyProjectOpenResult(
    LegacyProjectOpenStatus Status,
    string ProjectDirectory,
    string OriginalProjectDirectory,
    ProjectId? ProjectId,
    long? CursorRevision,
    long? LatestRevision,
    string Reason)
{
    public bool CanOpen => Status != LegacyProjectOpenStatus.Refused;
}

/// <summary>Copies format-2 authority into a verified format-3 sibling. No source connection is writable.</summary>
public static class LegacyMapUnitMigration
{
    private const long MaximumDatabaseBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumBlobBytes = 4L * 1024 * 1024 * 1024;
    private const string OriginFile = "migration-origin.json";

    public static async Task<LegacyProjectOpenResult> OpenAsync(
        string projectDirectory, CancellationToken cancellationToken = default,
        Action<LegacyMigrationStage>? checkpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        var source = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        LegacyProjectOpenResult Refuse(string reason) => new(
            LegacyProjectOpenStatus.Refused, source, source, null, null, null,
            $"{reason} The original remains at {source}. Open it with a compatible build or repair a copy and retry.");

        if (!Directory.Exists(source)) return Refuse("Project folder is missing.");
        if (IsLink(source)) return Refuse("Linked project folders are not supported for migration.");
        if (File.Exists(Path.Combine(source, "manifest.json")))
            return Refuse("Prototype manifest projects are unsupported by the connected editor.");
        var database = Path.Combine(source, "scene.sqlite");
        if (!File.Exists(database)) return Refuse("Project SQLite database is missing.");
        if (IsLink(database)) return Refuse("Linked SQLite authority is not supported for migration.");
        if (new FileInfo(database).Length > MaximumDatabaseBytes)
            return Refuse("Project SQLite database exceeds the migration limit.");

        try
        {
            var identity = await ReadIdentityAsync(database, cancellationToken).ConfigureAwait(false);
            if (identity.Version == 3)
            {
                var repository = new SqliteProjectRepository(source);
                var project = await repository.LoadAsync(identity.ProjectId, cancellationToken).ConfigureAwait(false);
                var state = await repository.ReadHistoryStateAsync(identity.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                return new LegacyProjectOpenResult(LegacyProjectOpenStatus.Current, source, source,
                    project.ProjectId, state.CursorRevision, state.LatestRevision,
                    $"Revision {state.CursorRevision} recovered.");
            }
            if (identity.Version != 2) return Refuse($"Unsupported SQLite format {identity.Version}.");

            var sibling = Path.Combine(Path.GetDirectoryName(source)!,
                Path.GetFileNameWithoutExtension(source) + "-normalized.mapwright");
            if (PathsOverlap(source, sibling)) return Refuse("Migration destination overlaps the original.");
            if (Directory.Exists(sibling))
                return await ReadExistingSiblingAsync(source, sibling, identity, cancellationToken)
                    .ConfigureAwait(false) ?? Refuse($"Normalized destination already exists: {sibling}.");

            var stage = sibling + ".stage-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            try
            {
                var stagedDatabase = Path.Combine(stage, "scene.sqlite");
                var sourceBuilder = new SqliteConnectionStringBuilder
                    { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
                var stageBuilder = new SqliteConnectionStringBuilder
                    { DataSource = stagedDatabase, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
                using (var original = new SqliteConnection(sourceBuilder.ToString()))
                using (var copy = new SqliteConnection(stageBuilder.ToString()))
                {
                    await original.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await copy.OpenAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    original.BackupDatabase(copy);
                }
                cancellationToken.ThrowIfCancellationRequested();
                checkpoint?.Invoke(LegacyMigrationStage.AfterBackup);
                await CopyBlobsAsync(source, stage, cancellationToken).ConfigureAwait(false);
                checkpoint?.Invoke(LegacyMigrationStage.AfterBlobCopy);
                var converted = await ConvertAuthorityAsync(stagedDatabase, identity, cancellationToken)
                    .ConfigureAwait(false);
                var stagedRepository = new SqliteProjectRepository(stage);
                var reopened = await stagedRepository.LoadAsync(identity.ProjectId, cancellationToken)
                    .ConfigureAwait(false);
                var latest = await stagedRepository.LoadRevisionAsync(identity.ProjectId,
                    converted.LatestRevision, cancellationToken).ConfigureAwait(false);
                if (reopened.Revision != converted.CursorRevision ||
                    latest.Revision != converted.LatestRevision ||
                    reopened.StorageFormatVersion != 3 || latest.StorageFormatVersion != 3)
                    throw new InvalidDataException("Migrated cursor or redo replay did not verify.");

                var marker = new MigrationOrigin(source, identity.ProjectId.Value,
                    converted.CursorRevision, converted.LatestRevision);
                await File.WriteAllTextAsync(Path.Combine(stage, OriginFile),
                    JsonSerializer.Serialize(marker), cancellationToken).ConfigureAwait(false);
                checkpoint?.Invoke(LegacyMigrationStage.BeforePublication);
                if (Directory.Exists(sibling))
                    throw new IOException("Normalized destination appeared before publication.");
                Directory.Move(stage, sibling);
                return new LegacyProjectOpenResult(LegacyProjectOpenStatus.Migrated, sibling, source,
                    identity.ProjectId, converted.CursorRevision, converted.LatestRevision,
                    $"Verified normalized copy at revision {converted.CursorRevision}; redo ends at {converted.LatestRevision}. Original retained at {source}.");
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
        }
        catch (OperationCanceledException) { return Refuse("Migration was cancelled before publication."); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                          SqliteException or JsonException or ArgumentException or OverflowException)
        {
            return Refuse($"Migration refused: {exception.Message}");
        }
    }

    private static async Task<LegacyProjectOpenResult?> ReadExistingSiblingAsync(
        string source, string sibling, DatabaseIdentity identity, CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(sibling, OriginFile);
        if (!File.Exists(markerPath) || IsLink(sibling)) return null;
        var marker = JsonSerializer.Deserialize<MigrationOrigin>(
            await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false));
        if (marker is null || !string.Equals(marker.SourcePath, source, StringComparison.OrdinalIgnoreCase) ||
            marker.ProjectId != identity.ProjectId.Value) return null;
        var repository = new SqliteProjectRepository(sibling);
        var project = await repository.LoadAsync(identity.ProjectId, cancellationToken).ConfigureAwait(false);
        var state = await repository.ReadHistoryStateAsync(identity.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project.StorageFormatVersion != 3 || state.CursorRevision < state.BaselineRevision ||
            state.LatestRevision < state.CursorRevision) return null;
        _ = await repository.LoadRevisionAsync(identity.ProjectId, state.LatestRevision,
            cancellationToken).ConfigureAwait(false);
        return new LegacyProjectOpenResult(LegacyProjectOpenStatus.Migrated, sibling, source,
            identity.ProjectId, state.CursorRevision, state.LatestRevision,
            $"Verified normalized copy reopened at revision {state.CursorRevision}. Original retained at {source}.");
    }

    private static async Task<DatabaseIdentity> ReadIdentityAsync(
        string database, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
            { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false)), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SQLite integrity check failed.");
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT version FROM schema_info LIMIT 1), project_id FROM current_project LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
            throw new InvalidDataException("SQLite schema or project identity is missing.");
        return new DatabaseIdentity(reader.GetInt32(0), new ProjectId(Guid.Parse(reader.GetString(1))));
    }

    private static async Task CopyBlobsAsync(string source, string stage, CancellationToken cancellationToken)
    {
        var sourceBlobs = Path.Combine(source, "blobs");
        var destination = Path.Combine(stage, "blobs");
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(sourceBlobs)) return;
        if (IsLink(sourceBlobs)) throw new InvalidDataException("Linked blob directories are unsupported.");
        long total = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(sourceBlobs))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path) || IsLink(path))
                throw new InvalidDataException("The blob directory contains a linked or non-file entry.");
            var hash = Path.GetFileName(path);
            if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"Unsupported immutable blob name {hash}.");
            total = checked(total + new FileInfo(path).Length);
            if (total > MaximumBlobBytes)
                throw new InvalidDataException("Immutable blobs exceed the migration limit.");
            await using var input = File.OpenRead(path);
            await using var output = new FileStream(Path.Combine(destination, hash), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();
            await using var verify = File.OpenRead(Path.Combine(destination, hash));
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken)
                .ConfigureAwait(false));
            if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Immutable blob {hash} failed hash verification.");
        }
    }

    private static async Task<ConvertedHistory> ConvertAuthorityAsync(
        string database, DatabaseIdentity identity, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
            { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        async Task<List<object[]>> RowsAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            var rows = new List<object[]>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }
            return rows;
        }
        var states = await RowsAsync("SELECT baseline_revision,cursor_revision,latest_revision FROM history_state;")
            .ConfigureAwait(false);
        if (states.Count != 1) throw new InvalidDataException("History authority is missing or ambiguous.");
        var baselineRevision = Convert.ToInt64(states[0][0]);
        var cursor = Convert.ToInt64(states[0][1]);
        var latest = Convert.ToInt64(states[0][2]);
        if (baselineRevision < 0 || cursor < baselineRevision || latest < cursor)
            throw new InvalidDataException("History cursor and latest revision are inconsistent.");
        var revisions = await RowsAsync("SELECT revision,project_id,snapshot_json FROM revisions ORDER BY revision;")
            .ConfigureAwait(false);
        var baselineRow = revisions.SingleOrDefault(row => Convert.ToInt64(row[0]) == baselineRevision);
        if (baselineRow is null || string.IsNullOrWhiteSpace(Convert.ToString(baselineRow[2])))
            throw new InvalidDataException("The authoritative baseline snapshot is missing.");
        if (revisions.Count != latest - baselineRevision + 1 ||
            revisions[0][0] is not long first || first != baselineRevision)
            throw new InvalidDataException("Retained revisions are incomplete.");
        var oldBaseline = DeserializeProject(Convert.ToString(baselineRow[2])!);
        if (oldBaseline.StorageFormatVersion != 2 || oldBaseline.ProjectId != identity.ProjectId ||
            oldBaseline.Revision != baselineRevision)
            throw new InvalidDataException("Baseline format, identity or revision disagrees with SQLite.");
        oldBaseline.ValidateConnectedTerrain();
        var geometry = MapUnitPolicy.FromSource(oldBaseline.Width, oldBaseline.Height,
            MapUnitPolicy.ChooseImportEditingLongestEdge(oldBaseline.Width, oldBaseline.Height));
        var scale = geometry.SourceToMapScale;
        var convertedBaseline = ConvertProject(oldBaseline, geometry);

        foreach (var row in revisions)
        {
            var revision = Convert.ToInt64(row[0]);
            if (revision != baselineRevision + revisions.IndexOf(row) ||
                !string.Equals(Convert.ToString(row[1]), identity.ProjectId.Value.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Revision order or project identity is ambiguous.");
            var json = Convert.ToString(row[2]);
            if (string.IsNullOrWhiteSpace(json)) continue;
            var old = DeserializeProject(json);
            if (old.StorageFormatVersion != 2 || old.ProjectId != identity.ProjectId ||
                old.Revision != revision || old.Width != oldBaseline.Width || old.Height != oldBaseline.Height)
                throw new InvalidDataException($"Snapshot {revision} disagrees with the legacy baseline.");
            var converted = ConvertProject(old, geometry);
            await UpdateAsync("UPDATE revisions SET snapshot_json=$value WHERE revision=$key;",
                revision, JsonSerializer.Serialize(converted, SqliteProjectRepository.JsonOptions))
                .ConfigureAwait(false);
        }

        var commands = await RowsAsync("SELECT sequence,command_id,project_id,base_revision,revision,command_type,payload_json FROM commands ORDER BY sequence;")
            .ConfigureAwait(false);
        if (commands.Count != latest - baselineRevision)
            throw new InvalidDataException("Retained command count does not span baseline to redo tip.");
        var replay = convertedBaseline;
        long sequence = 0;
        foreach (var row in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextSequence = Convert.ToInt64(row[0]);
            var baseRevision = Convert.ToInt64(row[3]);
            var revision = Convert.ToInt64(row[4]);
            if (nextSequence <= sequence || baseRevision != replay.Revision ||
                revision != baseRevision + 1 ||
                !string.Equals(Convert.ToString(row[2]), identity.ProjectId.Value.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Retained command sequence or revision is ambiguous.");
            sequence = nextSequence;
            var oldCommand = SqliteProjectRepository.DeserializeCommand(Convert.ToString(row[5])!,
                Convert.ToString(row[6])!);
            if (!string.Equals(oldCommand.Id.Value.ToString("D"), Convert.ToString(row[1]),
                    StringComparison.OrdinalIgnoreCase) || oldCommand.BaseRevision != baseRevision)
                throw new InvalidDataException("Retained command payload identity differs from SQLite.");
            var converted = ConvertCommand(oldCommand, scale);
            replay = converted.Apply(replay).Project;
            if (replay.Revision != revision) throw new InvalidDataException("Converted replay skipped a revision.");
            var stored = revisions[checked((int)(revision - baselineRevision))];
            if (!string.IsNullOrWhiteSpace(Convert.ToString(stored[2])))
            {
                var expected = ConvertProject(DeserializeProject(Convert.ToString(stored[2])!), geometry);
                if (JsonSerializer.Serialize(replay, SqliteProjectRepository.JsonOptions) !=
                    JsonSerializer.Serialize(expected, SqliteProjectRepository.JsonOptions))
                    throw new InvalidDataException($"Converted snapshot {revision} differs from replay.");
            }
            await UpdateAsync("UPDATE commands SET payload_json=$value,tile_count=0,delta_bytes=0 WHERE sequence=$key;",
                sequence, JsonSerializer.Serialize(converted, converted.GetType(),
                    SqliteProjectRepository.JsonOptions)).ConfigureAwait(false);
        }
        if (replay.Revision != latest) throw new InvalidDataException("Redo tip could not be replayed.");
        var currentRows = await RowsAsync("SELECT project_id,revision FROM current_project;")
            .ConfigureAwait(false);
        if (currentRows.Count != 1 || Convert.ToInt64(currentRows[0][1]) != cursor ||
            !string.Equals(Convert.ToString(currentRows[0][0]), identity.ProjectId.Value.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Current revision differs from history cursor.");
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE schema_info SET version=3; DELETE FROM history_checkpoints;";
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConvertedHistory(cursor, latest);

        async Task UpdateAsync(string sql, long key, string value)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = sql;
            update.Parameters.AddWithValue("$key", key);
            update.Parameters.AddWithValue("$value", value);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Migration update changed an unexpected number of rows.");
        }
    }

    private static MapProject DeserializeProject(string json) =>
        JsonSerializer.Deserialize<MapProject>(json, SqliteProjectRepository.JsonOptions)
        ?? throw new InvalidDataException("Legacy snapshot is empty.");

    private static MapProject ConvertProject(MapProject old, NormalizedMapGeometry geometry)
    {
        var scale = geometry.SourceToMapScale;
        var layers = old.TerrainLayers.Select(layer => layer with
        {
            Coastline = layer.Coastline with { EffectReach = Length(layer.Coastline.EffectReach, scale) },
            Strokes = layer.Strokes.Select(stroke => stroke with
            {
                Samples = stroke.Samples.Select(point => Point(point, scale)).ToImmutableArray(),
                Brush = stroke.Brush with { Radius = Length(stroke.Brush.Radius, scale) }
            }).ToImmutableArray(),
            TextureStrokes = layer.ResolvedTextureStrokes.Select(stroke => Texture(stroke, scale)).ToImmutableArray(),
            LandStrokes = layer.ResolvedLandStrokes.Select(stroke => Land(stroke, scale)).ToImmutableArray()
        }).ToImmutableArray();
        var imported = old.ImportedSource is null ? null : old.ImportedSource with
        {
            SourceSceneWidth = old.Width,
            SourceSceneHeight = old.Height,
            SourceToMapScale = scale,
            RasterReferences = old.ImportedSource.ResolvedRasterReferences.Select(raster => raster with
            {
                Transform = raster.Transform with
                {
                    OffsetX = Length(raster.Transform.OffsetX, scale),
                    OffsetY = Length(raster.Transform.OffsetY, scale),
                    ScaleX = Length(raster.Transform.ScaleX, scale),
                    ScaleY = Length(raster.Transform.ScaleY, scale)
                }
            }).ToImmutableArray()
        };
        var result = old with
        {
            Width = geometry.Width,
            Height = geometry.Height,
            StorageFormatVersion = 3,
            EditingPixelWidth = geometry.EditingPixelWidth,
            EditingPixelHeight = geometry.EditingPixelHeight,
            TerrainLayers = layers,
            River = old.River is null ? null : ConvertRiver(old.River, scale),
            ImportedSource = imported,
            InitialGrid = old.InitialGrid is null ? null : old.InitialGrid with
            {
                CellWidth = Length(old.InitialGrid.CellWidth, scale),
                CellHeight = Length(old.InitialGrid.CellHeight, scale)
            }
        };
        result.ValidateConnectedTerrain();
        return result;
    }

    private static IEditCommand ConvertCommand(IEditCommand command, double scale) => command switch
    {
        AddPaintStroke edit => edit with { Stroke = Paint(edit.Stroke, scale) },
        AddTextureStroke edit => edit with { Stroke = Paint(edit.Stroke, scale) },
        AddResolvedTextureStroke edit => edit with { Stroke = Texture(edit.Stroke, scale) },
        AddLandStroke edit => edit with { Stroke = Land(edit.Stroke, scale) },
        MoveRiverPoint edit => edit with { Position = Point(edit.Position, scale) },
        SetRiverWidth edit => edit with { Width = CheckedWidth(edit.Width, scale) },
        CreateRiver edit => edit with { River = ConvertRiver(edit.River, scale) },
        InsertRiverPoint edit => edit with
            { Position = Point(edit.Position, scale), Width = CheckedWidth(edit.Width, scale) },
        SetRiverPointWidth edit => edit with { Width = CheckedWidth(edit.Width, scale) },
        SetAllRiverWidths edit => edit with { Width = CheckedWidth(edit.Width, scale) },
        RenameTerrainLayer or SetTerrainVisibility or SetTerrainLock or SetTerrainSolo or
            SetTerrainOpacity or DeleteRiverPoint or SetRiverBankSoftness or SetRiverEnabled or
            DeleteRiver => command,
        _ => throw new InvalidDataException($"Unsupported retained command type {command.GetType().Name}.")
    };

    private static PaintStroke Paint(PaintStroke stroke, double scale) => stroke with
    {
        Samples = stroke.Samples.Select(point => Point(point, scale)).ToImmutableArray(),
        Brush = stroke.Brush with { Radius = Length(stroke.Brush.Radius, scale) }
    };

    private static TexturePaintStroke Texture(TexturePaintStroke stroke, double scale)
    {
        var result = stroke with
        {
            Samples = stroke.Samples.Select(sample => sample with
                { Position = Point(sample.Position, scale) }).ToImmutableArray(),
            TextureAnchor = Point(stroke.TextureAnchor, scale),
            Recipe = stroke.Recipe with
                { Radius = CheckedDiameter(stroke.Recipe.Radius * 2, scale,
                    TextureBrushLimits.MinimumDiameter, TextureBrushLimits.MaximumDiameter) * 0.5 }
        };
        result.Validate();
        return result;
    }

    private static LandStroke Land(LandStroke stroke, double scale)
    {
        var result = stroke with
        {
            Samples = stroke.Samples.Select(point => Point(point, scale)).ToImmutableArray(),
            Recipe = stroke.Recipe with
                { Radius = CheckedDiameter(stroke.Recipe.Radius * 2, scale,
                    LandBrushLimits.MinimumDiameter, LandBrushLimits.MaximumDiameter) * 0.5 }
        };
        result.Validate();
        return result;
    }

    private static River ConvertRiver(River river, double scale)
    {
        var result = river with
        {
            Points = river.Points.Select(point => Point(point, scale)).ToImmutableArray(),
            Width = CheckedWidth(river.Width, scale),
            WidthProfile = river.ResolvedWidthProfile with
            {
                Widths = river.ResolvedWidthProfile.Widths.Select(width => CheckedWidth(width, scale))
                    .ToImmutableArray()
            }
        };
        result.Validate();
        return result;
    }

    private static double CheckedWidth(double old, double scale) =>
        CheckedDiameter(old, scale, RiverLimits.MinimumWidth, RiverLimits.MaximumWidth);

    private static double CheckedDiameter(double old, double scale, double minimum, double maximum)
    {
        var converted = Length(old, scale);
        if (converted < minimum || converted > maximum)
            throw new InvalidDataException(
                $"Converted geometric size {converted:R} is outside supported map-unit limits [{minimum}, {maximum}]; no value was clamped.");
        return converted;
    }

    private static MapPoint Point(MapPoint point, double scale) =>
        new(Length(point.X, scale), Length(point.Y, scale));

    private static double Length(double value, double scale)
    {
        var converted = value * scale;
        if (!double.IsFinite(converted) || (value != 0 && converted == 0))
            throw new InvalidDataException("Legacy geometry cannot be represented in normalized map units.");
        return converted;
    }

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool PathsOverlap(string first, string second)
    {
        var a = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DatabaseIdentity(int Version, ProjectId ProjectId);
    private sealed record ConvertedHistory(long CursorRevision, long LatestRevision);
    private sealed record MigrationOrigin(string SourcePath, Guid ProjectId,
        long CursorRevision, long LatestRevision);
}
