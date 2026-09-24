using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;
using Mapwright.App;
using ProjectStore = Mapwright.Core.ProjectStore;
using Microsoft.Data.Sqlite;

public sealed class LegacyMapUnitMigrationContracts : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("format-2 SQLite opens as a verified normalized sibling", ConvertsBaseline);
        registry.Add("legacy mixed history retains cursor redo IDs and geometry", ConvertsMixedHistory);
        registry.Add("unsupported legacy payloads refuse without publishing", RefusesUnsupportedHistory);
        registry.Add("recent project entries retain unsupported and missing folders", RecentEntriesRemainSelectable);
        registry.Add("legacy imported transforms and equal source order ties survive", PreservesImportedSource);
        var historicalFixture = Environment.GetEnvironmentVariable("MAPWRIGHT_LEGACY_FIXTURE");
        if (!string.IsNullOrWhiteSpace(historicalFixture))
            registry.Add("real Phase 1 project takes the recent compatibility path",
                () => ReopenHistoricalFixture(historicalFixture));
    }

    private static async Task ConvertsBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-legacy-{Guid.NewGuid():N}");
        var original = Path.Combine(root, "old.mapwright");
        try
        {
            var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
                new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
            var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
                new CoastlineStyle(20), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
            var legacy = new MapProject(ProjectId.New(), MapId.New(), "Legacy", 2000, 1000, 0,
                [background, foreground]);
            await new SqliteProjectRepository(original).CreateAsync(legacy);
            var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"));
            var interruptedStage = Path.Combine(root, "old-normalized.mapwright.stage-interrupted");
            Directory.CreateDirectory(interruptedStage);
            await File.WriteAllTextAsync(Path.Combine(interruptedStage, "sentinel"), "prior interrupted run");

            var outcome = await LegacyMapUnitMigration.OpenAsync(original, CancellationToken.None);
            if (outcome.Status != LegacyProjectOpenStatus.Migrated ||
                outcome.ProjectDirectory == original || outcome.OriginalProjectDirectory != original)
                throw new InvalidOperationException($"Legacy open did not publish a separate sibling: {outcome.Reason}");
            var normalized = await new SqliteProjectRepository(outcome.ProjectDirectory)
                .LoadAsync(legacy.ProjectId, CancellationToken.None);
            if (normalized.StorageFormatVersion != 3 || normalized.Width != 1000 ||
                normalized.Height != 500 || normalized.TerrainLayers[1].Coastline.EffectReach != 10)
                throw new InvalidOperationException("Legacy geometry was not converted to map units.");
            if (!sourceBytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"))))
                throw new InvalidOperationException("The original SQLite authority changed during migration.");
            if (await File.ReadAllTextAsync(Path.Combine(interruptedStage, "sentinel")) !=
                "prior interrupted run")
                throw new InvalidOperationException("Migration overwrote an interrupted stage it did not own.");
            var rename = new RenameTerrainLayer(CommandId.New(), normalized.Revision,
                TerrainRole.Foreground, "Edited normalized copy");
            await new SqliteProjectRepository(outcome.ProjectDirectory).CommitAsync(
                normalized, rename, rename.Apply(normalized), CancellationToken.None);
            var second = await LegacyMapUnitMigration.OpenAsync(original);
            if (second.Status != LegacyProjectOpenStatus.Migrated ||
                second.ProjectDirectory != outcome.ProjectDirectory || second.CursorRevision != 1)
                throw new InvalidOperationException("Reopening the normalized sibling was not idempotent.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ConvertsMixedHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-legacy-history-{Guid.NewGuid():N}");
        var original = Path.Combine(root, "mixed.mapwright");
        try
        {
            var baseline = Fixture();
            var repository = new SqliteProjectRepository(original);
            await repository.CreateAsync(baseline);
            var foreground = baseline.RequireRole(TerrainRole.Foreground);
            var texture = new PaintStroke(StrokeId.New(), [new MapPoint(500, 300)],
                new ResolvedBrush(new string('a', 64), 20, .8, .7, .6, .2, 0, 3, 1),
                false, TerrainStrokeKind.Texture);
            var paint = new AddTextureStroke(CommandId.New(), baseline.Revision, foreground.Id, texture);
            var afterPaint = paint.Apply(baseline).Project;
            await repository.CommitAsync(baseline, paint, paint.Apply(baseline), CancellationToken.None);
            var landStroke = new LandStroke(StrokeId.New(), [new MapPoint(750, 250)],
                ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 20, 0, 0, .25, 7),
                LandOperation.Add);
            var land = new AddLandStroke(CommandId.New(), afterPaint.Revision,
                TerrainRole.Foreground, landStroke);
            var afterLand = land.Apply(afterPaint).Project;
            await repository.CommitAsync(afterPaint, land, land.Apply(afterPaint), CancellationToken.None);
            var river = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(100, 100), new MapPoint(900, 400)], [8d, 12d], .2);
            var createRiver = new CreateRiver(CommandId.New(), afterLand.Revision, river);
            await repository.CommitAsync(afterLand, createRiver, createRiver.Apply(afterLand),
                CancellationToken.None);
            await repository.MoveHistoryCursorAsync(baseline.ProjectId, 3, 2);
            var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"));

            var result = await LegacyMapUnitMigration.OpenAsync(original);
            if (!result.CanOpen || result.Status != LegacyProjectOpenStatus.Migrated ||
                result.CursorRevision != 2 || result.LatestRevision != 3)
                throw new InvalidOperationException($"Mixed history migration refused: {result.Reason}");
            var migrated = new SqliteProjectRepository(result.ProjectDirectory);
            var cursor = await migrated.LoadAsync(baseline.ProjectId, CancellationToken.None);
            var tip = await migrated.LoadRevisionAsync(baseline.ProjectId, 3);
            if (cursor.Revision != 2 || tip.Revision != 3 || cursor.River is not null ||
                tip.River is null || tip.River.Points[1] != new MapPoint(450, 200) ||
                cursor.RequireRole(TerrainRole.Foreground).Strokes[0].Samples[0] != new MapPoint(250, 150) ||
                cursor.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes[0].Recipe.Diameter != 10)
                throw new InvalidOperationException("Migrated cursor or redo geometry changed visually.");
            var entries = await migrated.ReadHistoryPageAsync(baseline.ProjectId, 0, 10);
            if (entries.Count != 3 || entries[0].CommandId != paint.Id ||
                entries[1].CommandId != land.Id || entries[2].CommandId != createRiver.Id ||
                entries.Select(entry => entry.Sequence).Distinct().Count() != 3)
                throw new InvalidOperationException("Command IDs or sequence changed during migration.");
            await migrated.DeleteHistoryAccelerationAsync(baseline.ProjectId);
            await migrated.MoveHistoryCursorAsync(baseline.ProjectId, 2, 3);
            var afterRedo = await new SqliteProjectRepository(result.ProjectDirectory)
                .LoadAsync(baseline.ProjectId, CancellationToken.None);
            if (afterRedo.River?.Points[1] != new MapPoint(450, 200))
                throw new InvalidOperationException("Redo after cache deletion failed.");
            if (!sourceBytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"))))
                throw new InvalidOperationException("Migration changed original SQLite bytes.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RefusesUnsupportedHistory()
    {
        foreach (var mutation in new[] { "unknown", "dimension", "subunit", "blob", "disk-full", "cancelled" })
        {
            var root = Path.Combine(Path.GetTempPath(), $"mapwright-legacy-refusal-{Guid.NewGuid():N}");
            var original = Path.Combine(root, "refuse.mapwright");
            try
            {
                var baseline = Fixture();
                var repository = new SqliteProjectRepository(original);
                await repository.CreateAsync(baseline);
                if (mutation is "unknown" or "subunit")
                {
                    var recipe = TexturePresetCatalog.Resolve("hard-round", new string('a', 64),
                        7, mutation == "subunit" ? 1 : 20);
                    var stroke = TexturePaintStroke.Create(StrokeId.New(),
                        [new MapPoint(100, 100)], recipe, new MapPoint(100, 100));
                    var command = new AddResolvedTextureStroke(CommandId.New(), 0,
                        TerrainRole.Foreground, stroke);
                    await repository.CommitAsync(baseline, command, command.Apply(baseline),
                        CancellationToken.None);
                }
                if (mutation is "unknown" or "dimension")
                {
                    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(original, "scene.sqlite"),
                        Mode = SqliteOpenMode.ReadWrite,
                        Pooling = false
                    }.ToString());
                    await connection.OpenAsync();
                    await using var update = connection.CreateCommand();
                    update.CommandText = mutation == "unknown"
                        ? "UPDATE commands SET command_type='UnknownRetainedCommand';"
                        : "UPDATE revisions SET snapshot_json=replace(snapshot_json, '\"width\":2000', '\"width\":0') WHERE revision=0;";
                    await update.ExecuteNonQueryAsync();
                }
                if (mutation == "blob")
                {
                    var blobs = Path.Combine(original, "blobs");
                    Directory.CreateDirectory(blobs);
                    await File.WriteAllTextAsync(Path.Combine(blobs, "bad-name"), "tampered");
                }
                var sourceBytes = await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"));
                Action<LegacyMigrationStage>? checkpoint = mutation switch
                {
                    "disk-full" => stage =>
                    {
                        if (stage == LegacyMigrationStage.AfterBlobCopy)
                            throw new IOException("Injected disk-full copy failure.");
                    },
                    "cancelled" => stage =>
                    {
                        if (stage == LegacyMigrationStage.BeforePublication)
                            throw new OperationCanceledException("Injected interruption.");
                    },
                    _ => null
                };
                var outcome = await LegacyMapUnitMigration.OpenAsync(original,
                    CancellationToken.None, checkpoint);
                if (outcome.Status != LegacyProjectOpenStatus.Refused ||
                    Directory.Exists(Path.Combine(root, "refuse-normalized.mapwright")) ||
                    Directory.EnumerateDirectories(root, "refuse-normalized.mapwright.stage-*").Any() ||
                    !sourceBytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(original, "scene.sqlite"))))
                    throw new InvalidOperationException($"{mutation} did not refuse without source mutation: {outcome.Reason}");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    private static MapProject Fixture()
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(20), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Legacy", 2000, 1000, 0,
            [background, foreground]);
    }

    private static async Task PreservesImportedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-legacy-source-{Guid.NewGuid():N}");
        var original = Path.Combine(root, "imported.mapwright");
        var sourcePath = Path.Combine(root, "source.ink");
        try
        {
            Directory.CreateDirectory(root);
            var sourceBytes = Encoding.UTF8.GetBytes("immutable source");
            var previewBytes = Encoding.UTF8.GetBytes("preview");
            var firstBytes = Encoding.UTF8.GetBytes("first raster");
            var secondBytes = Encoding.UTF8.GetBytes("second raster");
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var rasters = ImmutableArray.Create(
                new ImportedRasterReference("first", "background", 4, Hash(firstBytes), 64, 32,
                    null, new ImportedRasterTransform(100, 50, 1.5, 2), "straight-alpha", "none", "none"),
                new ImportedRasterReference("second", "foreground", 4, Hash(secondBytes), 64, 32,
                    null, new ImportedRasterTransform(200, 75, 1, 1), "straight-alpha", "none", "none"));
            var imported = new ImportedSourceReference("source.ink", Hash(sourceBytes), Hash(sourceBytes),
                Hash(previewBytes), 64, 32, ImportRecoveryMode.EditableRecoveredTerrain,
                RasterReferences: rasters,
                SourceCommands: [new ImportedCommandReference(9, "first", false),
                    new ImportedCommandReference(9, "second", false)]);
            var baseline = Fixture();
            var layers = baseline.TerrainLayers
                .SetItem(0, baseline.TerrainLayers[0] with { SourceBlobHash = Hash(firstBytes) })
                .SetItem(1, baseline.TerrainLayers[1] with { SourceBlobHash = Hash(secondBytes) });
            baseline = baseline with { TerrainLayers = layers, ImportedSource = imported };
            await new SqliteProjectRepository(original).CreateImportedAsync(baseline, sourcePath,
                [new ImportedBlobPayload(Hash(previewBytes), previewBytes, "preview"),
                    new ImportedBlobPayload(Hash(firstBytes), firstBytes, "first"),
                    new ImportedBlobPayload(Hash(secondBytes), secondBytes, "second")],
                CancellationToken.None);
            var result = await LegacyMapUnitMigration.OpenAsync(original);
            if (!result.CanOpen)
                throw new InvalidOperationException($"Imported source refused: {result.Reason}");
            var reopened = await new SqliteProjectRepository(result.ProjectDirectory)
                .LoadAsync(baseline.ProjectId, CancellationToken.None);
            var source = reopened.ImportedSource ?? throw new InvalidOperationException("Source provenance was lost.");
            if (source.SourceSha256 != Hash(sourceBytes) || source.SourceSceneWidth != 2000 ||
                source.SourceToMapScale != .5 || source.ResolvedRasterReferences.Length != 2 ||
                source.ResolvedRasterReferences[0].SourceOrder != 4 ||
                source.ResolvedRasterReferences[1].SourceOrder != 4 ||
                source.ResolvedRasterReferences[0].Transform != new ImportedRasterTransform(50, 25, .75, 1) ||
                source.ResolvedRasterReferences[1].Transform.OffsetX != 100 ||
                source.ResolvedSourceCommands[0].SourceOrder != 9 ||
                source.ResolvedSourceCommands[1].SourceOrder != 9)
                throw new InvalidOperationException("Imported transforms, hashes or equal-order ties changed.");
            if (!sourceBytes.SequenceEqual(await File.ReadAllBytesAsync(Path.Combine(
                    result.ProjectDirectory, "blobs", Hash(sourceBytes)))))
                throw new InvalidOperationException("Immutable source blob was rewritten.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RecentEntriesRemainSelectable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-legacy-entry-{Guid.NewGuid():N}");
        var manifest = Path.Combine(root, "prototype.mapwright");
        var missing = Path.Combine(root, "missing.mapwright");
        var broken = Path.Combine(root, "broken.mapwright");
        try
        {
            Directory.CreateDirectory(manifest);
            Directory.CreateDirectory(broken);
            await File.WriteAllTextAsync(Path.Combine(manifest, "manifest.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(broken, "scene.sqlite"), "not SQLite");
            RecentProjectCatalog.Remember(root, manifest);
            RecentProjectCatalog.Remember(root, missing);
            RecentProjectCatalog.Remember(root, broken);
            var entries = RecentProjectCatalog.Read(root);
            var prototype = entries.Single(entry => entry.Path == manifest);
            var absent = entries.Single(entry => entry.Path == missing);
            var malformed = entries.Single(entry => entry.Path == broken);
            if (!ProjectStore.HasPrototypeManifest(manifest) ||
                !prototype.FolderExists || prototype.ProjectId is not null ||
                !prototype.StatusText.Contains("Prototype manifest", StringComparison.Ordinal) ||
                absent.FolderExists || malformed.ProjectId is not null)
                throw new InvalidOperationException("Recent entry compatibility labels are misleading.");
            foreach (var path in new[] { manifest, missing, broken })
            {
                var result = await LegacyMapUnitMigration.OpenAsync(path);
                if (result.Status != LegacyProjectOpenStatus.Refused ||
                    Directory.Exists(Path.Combine(root, Path.GetFileNameWithoutExtension(path) + "-normalized.mapwright")))
                    throw new InvalidOperationException("Unsupported entry opened or published a sibling.");
            }
            if (Directory.Exists(missing))
                throw new InvalidOperationException("Opening an absent folder created it.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ReopenHistoricalFixture(string fixture)
    {
        if (!File.Exists(Path.Combine(fixture, "scene.sqlite")))
            throw new InvalidOperationException("The requested Phase 1 fixture has no SQLite authority.");
        var root = Path.Combine(Path.GetTempPath(), $"mapwright-real-legacy-{Guid.NewGuid():N}");
        var copied = Path.Combine(root, "historical.mapwright");
        try
        {
            Directory.CreateDirectory(copied);
            foreach (var file in Directory.EnumerateFiles(fixture, "*", SearchOption.TopDirectoryOnly))
                File.Copy(file, Path.Combine(copied, Path.GetFileName(file)));
            var sourceBlobs = Path.Combine(fixture, "blobs");
            if (Directory.Exists(sourceBlobs))
            {
                Directory.CreateDirectory(Path.Combine(copied, "blobs"));
                foreach (var file in Directory.EnumerateFiles(sourceBlobs, "*", SearchOption.TopDirectoryOnly))
                    File.Copy(file, Path.Combine(copied, "blobs", Path.GetFileName(file)));
            }
            var recent = RecentProjectCatalog.Read(root).Single(item => item.Path == copied);
            if (recent.ProjectId is null)
                throw new InvalidOperationException($"Real fixture was not listed for opening: {recent.StatusText}");
            var outcome = await LegacyMapUnitMigration.OpenAsync(recent.Path);
            if (outcome.Status == LegacyProjectOpenStatus.Refused)
            {
                if (Directory.Exists(Path.Combine(root, "historical-normalized.mapwright")) ||
                    !outcome.Reason.Contains("original", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Real fixture refusal did not preserve recovery guidance.");
                Console.WriteLine($"REAL_LEGACY_REFUSAL {outcome.Reason}");
            }
            else
            {
                var project = await new SqliteProjectRepository(outcome.ProjectDirectory)
                    .LoadAsync(new ProjectId(recent.ProjectId.Value), CancellationToken.None);
                if (project.StorageFormatVersion != 3 || Math.Max(project.Width, project.Height) != 1000)
                    throw new InvalidOperationException("Real fixture reopened outside normalized space.");
                Console.WriteLine($"REAL_LEGACY_MIGRATED revision={project.Revision} path={outcome.ProjectDirectory}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
