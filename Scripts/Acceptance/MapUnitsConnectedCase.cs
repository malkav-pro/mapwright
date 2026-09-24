using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using Mapwright.App;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Export;
using Mapwright.Infrastructure;
using Mapwright.Rendering;
using InkImportService = Mapwright.Core.InkImportService;
using InkImportPackageFactory = Mapwright.Core.InkImportPackageFactory;

namespace Mapwright.Acceptance;

public sealed class MapUnitsConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("MapUnits", MapUnitsConnectedCase.RunAsync);
}

public static class MapUnitsConnectedCase
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
        var artifactRoot = ConnectedEvidenceRoot.Resolve(repositoryRoot, "mapunits-connected", "mapunits");
        if (Directory.Exists(artifactRoot) &&
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_RUN_ID")))
            Directory.Delete(artifactRoot, recursive: true);
        Directory.CreateDirectory(artifactRoot);
        var sourcePath = Main.ConfiguredInkPath;
        Check(File.Exists(sourcePath), "Pinned real .ink source is missing.");
        var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(
            File.OpenRead(sourcePath), cancellationToken)).ToLowerInvariant();
        var imported = await Task.Run(() => new InkImportService().Import(sourcePath), cancellationToken);
        Check(Math.Abs(imported.Document.Width - 7559.1416309012875) < 1e-6 &&
              imported.Document.Height == 8192 &&
              imported.Document.Import?.SourceSha256 == sourceHash,
            $"Real imported dimensions or source identity changed: {imported.Document.Width}x{imported.Document.Height}, " +
            $"hash {imported.Document.Import?.SourceSha256}, source {sourceHash}.");
        var package = InkImportPackageFactory.Create(imported);
        var projectsRoot = Path.Combine(artifactRoot, "projects");
        var published = await new ImportProject(path => new SqliteProjectRepository(path)).ExecuteAsync(
            new ImportProjectRequest(package, ImportRecoveryMode.EditableRecoveredTerrain,
                projectsRoot, "editable.mapwright"), cancellationToken);
        Check(published.Published && published.Project is not null,
            "Editable recovery did not publish the real source.");
        var root = Path.Combine(projectsRoot, "editable.mapwright");
        var repository = new SqliteProjectRepository(root);
        var project = await repository.LoadAsync(published.Project!.ProjectId, cancellationToken);
        Check(project.StorageFormatVersion == 3 &&
              Math.Abs(project.Width - imported.Document.Width * 1000 / imported.Document.Height) < 1e-9 &&
              project.Height == 1000 && project.EditingPixelHeight == 4096 &&
              project.ImportedSource?.SourceSha256 == sourceHash,
            "Editable recovery lost normalized geometry, editing sampling or source provenance.");
        Check(project.ImportedSource!.ResolvedRasterReferences.Length == 3 &&
              project.ImportedSource.ResolvedRasterReferences.All(r => r.PixelWidth > 0 && r.PixelHeight > 0),
            "Placed source rasters lost native dimensions.");
        var exactRasterGeometry = MapUnitPolicy.FromSource(7559, 8192, 4096);
        Check(Math.Abs(exactRasterGeometry.Width - 922.7294921875) < 1e-9 &&
              exactRasterGeometry.Height == 1000,
            "A 7559x8192 raster did not normalize to the D-09 map geometry.");
        Check(project.TerrainLayers.Select(layer => layer.Role)
                .SequenceEqual([TerrainRole.Background, TerrainRole.Foreground]),
            "Background/Foreground order changed during normalized import.");

        var flattened = Main.CreateFlattenedSnapshot(imported);
        Check(flattened.StorageFormatVersion == 3 &&
              Math.Abs(flattened.Width - project.Width) < 1e-9 &&
              flattened.Height == project.Height &&
              flattened.ImportedSource!.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance,
            "Flattened recovery has different map geometry or lost its explicit mode.");
        foreach (var sampling in new[] { 1024, 4096 })
        {
            foreach (var (columns, rows) in new[] { (40, 30), (80, 60) })
            {
                var grid = MapUnitPolicy.FromGrid(columns, rows, sampling);
                Check(grid.Width == 1000 && grid.Height == 750 &&
                      grid.EditingPixelWidth == sampling && grid.EditingPixelHeight == sampling * 3 / 4,
                    "Grid counts or editing pixels changed map geometry.");
            }
        }

        var center = new MapPoint(project.Width / 2, project.Height / 2);
        var controls = new EditorControlsState();
        controls.SelectTool("Land");
        var canvas = new MapCanvas();
        canvas.BindEditorControls(controls);
        var textureSize = new Vector2(project.EditingPixelWidth, project.EditingPixelHeight);
        var pan = new Vector2(17, 23);
        const float zoom = 2;
        var screen = pan + new Vector2((float)(center.X * textureSize.X / project.Width),
            (float)(center.Y * textureSize.Y / project.Height)) * zoom;
        var mapped = MapCanvas.CanvasToDocument(screen, textureSize, pan, zoom, project);
        Check(mapped is not null && mapped.Value.DistanceTo(center) < 0.001,
            "Zoomed viewport pointer no longer maps to the same map anchor.");
        var gesture = new GestureController();
        Check(gesture.BeginEdit("Land", "Foreground mask", project.Revision,
                mapped!.Value.X, mapped.Value.Y, true, false).Accepted,
            "Zoomed map-unit gesture was refused.");
        var plan = gesture.PrepareCommit(project.Revision, true, false);
        var command = canvas.BuildGestureCommand(plan, project);
        canvas.Free();
        await using (var session = new EditSession(project, repository, new NullRenderInvalidationQueue()))
        {
            var acknowledged = await session.ExecuteAsync(command, cancellationToken);
            Check(acknowledged.Revision == project.Revision + 1 &&
                  session.Current.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single()
                      .Recipe.Diameter == 96,
                "A 96-map-unit Land gesture was not durably acknowledged.");
            await session.SaveAsync(cancellationToken);
        }
        var committed = await repository.LoadAsync(project.ProjectId, cancellationToken);
        Check(committed.Revision == project.Revision + 1 &&
              committed.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single()
                  .Samples[0].DistanceTo(center) < 0.001,
            "Fresh repository load shifted the durable map-unit gesture.");
        await using (var history = new HistorySession(committed, repository, new NullRenderInvalidationQueue()))
        {
            await history.UndoAsync(cancellationToken);
        }
        var cursor = await repository.ReadHistoryStateAsync(project.ProjectId, cancellationToken);
        Check(cursor.CursorRevision == project.Revision &&
              cursor.LatestRevision == committed.Revision && cursor.CanRedo,
            "Undo did not persist its cursor and redo tail.");
        var reopenedAtCursor = await repository.LoadAsync(project.ProjectId, cancellationToken);
        Check(reopenedAtCursor.Revision == project.Revision &&
              reopenedAtCursor.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.IsEmpty,
            "Fresh reopen did not honor the durable cursor.");
        await using (var history = new HistorySession(reopenedAtCursor, repository,
                         new NullRenderInvalidationQueue()))
        {
            await history.RedoAsync(cancellationToken);
        }
        var reopened = await repository.LoadAsync(project.ProjectId, cancellationToken);
        Check(reopened.Revision == committed.Revision &&
              reopened.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single()
                  .Recipe.Diameter == 96,
            "Fresh redo lost the 96-map-unit command.");

        var graph = new ConnectedTerrainGraph(root);
        var outputLedger = new RenderResourceLedger(16L * RenderResourceLedger.Gibibyte,
            32L * RenderResourceLedger.Gibibyte, 512L * RenderResourceLedger.Mebibyte);
        var exporter = new DocumentPngExport(graph, outputLedger, root);
        var supported = DocumentPngExport.DimensionsForPreset(reopened, 1024);
        Check(ExistingExportRequest.TryResolve(reopened, supported.Width, supported.Height,
                out var supportedEdge, out _) && supportedEdge == 1024,
            "Existing export entry rejected a supported aspect-preserving output.");
        var previousDestination = Path.Combine(artifactRoot, "previous-destination.png");
        var previousBytes = "unchanged-destination"u8.ToArray();
        await File.WriteAllBytesAsync(previousDestination, previousBytes, cancellationToken);
        Check(!ExistingExportRequest.TryResolve(reopened, 1333, supported.Height,
                  out _, out var unsupportedMessage) && unsupportedMessage.Contains("1K") &&
              !ExistingExportRequest.TryResolve(reopened, supported.Width - 1, supported.Height,
                  out _, out var aspectMessage) && aspectMessage.Contains("enter") &&
              (await File.ReadAllBytesAsync(previousDestination, cancellationToken))
                  .SequenceEqual(previousBytes),
            "Unsupported or stretched dimensions were accepted or changed the existing destination.");
        var full = System.Environment.GetEnvironmentVariable("MAPWRIGHT_PHASE11_FULL") == "1";
        foreach (var longest in full ? new[] { 1024, 4096, 16384 } : new[] { 1024, 4096 })
        {
            var (width, height) = DocumentPngExport.DimensionsForPreset(reopened, longest);
            var destination = Path.Combine(artifactRoot, $"mapunits-{longest}.png");
            var result = await exporter.ExportPresetAsync(reopened, destination, longest,
                new DocumentPngExportOptions(1024, 16), cancellationToken: cancellationToken);
            Check(result.Validation.Passed && result.Width == width && result.Height == height &&
                  result.FrozenRevision == reopened.Revision && result.PinnedBlobHashes.Count >= 4,
                "Frozen target export lost revision, source pins or aspect.");
            Check(result.PeakExportBufferBytes <= outputLedger.ExportBufferBudgetBytes &&
                  result.PeakProcessWorkingSetBytes <= outputLedger.ProcessBudgetBytes,
                "Target export exceeded its measured resource envelope.");
            using var image = new Image();
            Check(image.Load(destination) == Error.Ok && image.GetWidth() == width &&
                  image.GetHeight() == height, "Target PNG could not be decoded at its expected dimensions.");
            foreach (var (x, y) in new[] { (width / 4, height / 4), (width / 2, height / 2),
                         (width * 3 / 4, height * 3 / 4) })
            {
                var expected = graph.EvaluateRegionAtSize(reopened, width, height, x, y, 1, 1).Rgba;
                var actual = image.GetPixel(x, y);
                Check(Math.Abs(actual.R8 - expected[0]) <= 1 &&
                      Math.Abs(actual.G8 - expected[1]) <= 1 &&
                      Math.Abs(actual.B8 - expected[2]) <= 1 &&
                      Math.Abs(actual.A8 - expected[3]) <= 1,
                    "Viewport/export sampling drifted at a normalized map anchor.");
            }
            GD.Print($"MAPUNITS_EXPORT source_sha256={sourceHash} revision={reopened.Revision} " +
                     $"dimensions={width}x{height} peak_export_buffer_bytes={result.PeakExportBufferBytes} " +
                     $"peak_process_bytes={result.PeakProcessWorkingSetBytes} png_sha256={result.PngSha256}");
        }
        Check(await LegacyMapUnitMigration.OpenAsync(root, cancellationToken) is
              { CanOpen: true, ProjectDirectory: var opened } && opened == root,
            "Connected open path refused a normalized durable project.");
        var legacyRoot = Path.Combine(artifactRoot, "legacy.mapwright");
        var legacyBackground = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var legacyForeground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        var legacy = new MapProject(ProjectId.New(), MapId.New(), "Legacy pixel map", 2000, 1000,
            0, [legacyBackground, legacyForeground]);
        var legacyRepository = new SqliteProjectRepository(legacyRoot);
        await legacyRepository.CreateAsync(legacy, cancellationToken);
        var legacyStroke = new LandStroke(StrokeId.New(), [new MapPoint(500, 250)],
            ResolvedLandBrush.FromDiameter(LandShape.RoundSoft, 96, 0, 0, .25, 7),
            LandOperation.Add);
        var legacyCommand = new AddLandStroke(CommandId.New(), legacy.Revision,
            TerrainRole.Foreground, legacyStroke);
        await legacyRepository.CommitAsync(legacy, legacyCommand, legacyCommand.Apply(legacy),
            cancellationToken);
        await legacyRepository.MoveHistoryCursorAsync(legacy.ProjectId, 1, 0, cancellationToken);
        var legacyAuthority = Path.Combine(legacyRoot, "scene.sqlite");
        var legacyHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            legacyAuthority, cancellationToken))).ToLowerInvariant();
        var legacyOpen = await LegacyMapUnitMigration.OpenAsync(legacyRoot, cancellationToken);
        Check(legacyOpen.Status == LegacyProjectOpenStatus.Migrated && legacyOpen.CanOpen &&
              legacyOpen.ProjectDirectory != legacyRoot && legacyOpen.CursorRevision == 0 &&
              legacyOpen.LatestRevision == 1,
            "Legacy open did not retain a separate normalized sibling and redo cursor.");
        var migratedRepository = new SqliteProjectRepository(legacyOpen.ProjectDirectory);
        var migratedCursor = await migratedRepository.LoadAsync(legacy.ProjectId, cancellationToken);
        var migratedTip = await migratedRepository.LoadRevisionAsync(legacy.ProjectId, 1,
            cancellationToken);
        Check(migratedCursor.Revision == 0 && migratedCursor.Width == 1000 &&
              migratedCursor.Height == 500 &&
              migratedTip.RequireRole(TerrainRole.Foreground).ResolvedLandStrokes.Single()
                  .Samples[0] == new MapPoint(250, 125),
            "Legacy cursor or redo command was silently reinterpreted as map units.");
        Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
                  legacyAuthority, cancellationToken))).ToLowerInvariant() == legacyHash,
            "Legacy open changed the original pixel-space SQLite authority.");
        GD.Print($"MAPUNITS_SOURCE source_sha256={sourceHash} revision={reopened.Revision} " +
                 $"map={reopened.Width:R}x{reopened.Height:R} editing={reopened.EditingPixelWidth}x{reopened.EditingPixelHeight}");
        return assertions;
    }
}
