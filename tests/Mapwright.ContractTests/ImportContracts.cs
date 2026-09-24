using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using Mapwright.Application;
using Mapwright.Domain;
using Mapwright.Infrastructure;

public sealed class ImportContractCases : IContractCaseProvider
{
    public void Register(ContractRegistry registry)
    {
        registry.Add("import project maps verified or flattened recovery", RecoveryModesMapOneProject);
        registry.Add("import project persists source descriptors and replay cutoff", ProvenanceAndReplayCutoffPersist);
        registry.Add("import project degrades untrusted editable bases honestly", UntrustedEditableMappingFallsBack);
        registry.Add("import project publishes all immutable blobs through SQLite", ImportedBlobsReopenFromSqlite);
        registry.Add("import bounds reject hostile sources before allocation", ImportBoundsContractExists);
        registry.Add("import project rejects destination escape and source overlap", UnsafeDestinationsAreRejected);
        registry.Add("import normalizes scene and positioned raster provenance once", NormalizedImportGeometry);
        registry.Add("import rejects invalid and over-budget dimensions before publication", InvalidDimensionsPublishNothing);
        registry.Add("ink scene raster placement survives source-to-map composition", ParsedSceneTransformSurvives);
    }

    private static Task ImportBoundsContractExists()
    {
        var bounds = ImportBounds.Default;
        bounds.Validate();
        True(bounds.MaximumCompressedBytes < bounds.MaximumExpandedBytes,
            "Compressed and expanded input do not have distinct checked budgets.");
        True(bounds.MaximumJsonDepth <= 64 && bounds.MaximumJsonTokens > 0 &&
             bounds.MaximumJsonTokenBytes > 0,
            "JSON depth, token count, and token allocation limits are incomplete.");
        True(bounds.MaximumBase64Characters > 0 && bounds.MaximumImageDimension == 16_384 &&
             bounds.MaximumImagePixels > 0 && bounds.MaximumDecodedImageBytes > 0 &&
             bounds.MaximumRetainedRasterBytes >= bounds.MaximumDecodedImageBytes,
            "Embedded image allocations are not bounded before decode.");
        Throws<ArgumentOutOfRangeException>(() => (bounds with { MaximumJsonTokens = 0 }).Validate(),
            "A zero token budget was accepted.");
        Throws<InvalidDataException>(() => (bounds with
        {
            MaximumJsonTokenBytes = checked((int)bounds.MaximumExpandedBytes + 1)
        }).Validate(), "A token allocation larger than the expanded stream was accepted.");
        return Task.CompletedTask;
    }

    private static async Task RecoveryModesMapOneProject()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root);
            var repositories = new List<RecordingImportedRepository>();
            var useCase = new ImportProject(path =>
            {
                var repository = new RecordingImportedRepository(path);
                repositories.Add(repository);
                return repository;
            });
            var unselected = await useCase.ExecuteAsync(new ImportProjectRequest(
                source, null, root, "unselected.mapwright"));
            True(!unselected.Published && unselected.RecoveryLevel == ImportRecoveryLevel.ChoiceRequired,
                "Recovery review published before either mode was selected.");
            Equal(0, repositories.Count);

            var flattened = await useCase.ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.OriginalFlattenedAppearance, root, "flat.mapwright"));
            True(flattened.Published, "Flattened recovery did not publish one project.");
            AssertFixedRoles(flattened.Project!);
            True(flattened.Project!.RequireRole(TerrainRole.Background).Locked,
                "Flattened Background must remain locked.");
            True(flattened.Project.RequireRole(TerrainRole.Foreground).SourceBlobHash is null,
                "Flattened recovery invented editable Foreground source colour.");

            var overridePath = Path.Combine(root, "chosen", "editable.mapwright");
            var editable = await useCase.ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.EditableRecoveredTerrain, root, "ignored.mapwright", overridePath));
            True(editable.Published && editable.ProjectDirectory == Path.GetFullPath(overridePath),
                "Chosen destination override was not used exactly.");
            AssertFixedRoles(editable.Project!);
            True(!editable.Project!.RequireRole(TerrainRole.Background).Locked,
                "Verified editable Background remained locked.");
            True(editable.Project.RequireRole(TerrainRole.Foreground).CoverageSourceBlobHash is not null,
                "Verified Foreground alpha coverage was not mapped.");
            Equal(2, repositories.Count);
            True(repositories.All(repository => repository.CreateCalls == 1),
                "A recovery mode created more than one authoritative initial revision.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task UnsafeDestinationsAreRejected()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root);
            var factoryCalls = 0;
            var useCase = new ImportProject(path =>
            {
                factoryCalls++;
                return new RecordingImportedRepository(path);
            });
            await ThrowsAsync<InvalidDataException>(() => useCase.ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.OriginalFlattenedAppearance, root, "../escape")),
                "A project folder escaped the configured projects root.");
            await ThrowsAsync<InvalidDataException>(() => useCase.ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.OriginalFlattenedAppearance, root, "ignored", root)),
                "A project destination was allowed to contain and overwrite its immutable source.");
            Equal(0, factoryCalls);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ProvenanceAndReplayCutoffPersist()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root, includeExtra: true, equalSourceOrder: true);
            var repository = new RecordingImportedRepository(Path.Combine(root, "project.mapwright"));
            var result = await new ImportProject(_ => repository).ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.EditableRecoveredTerrain, root, "project.mapwright"));
            var provenance = result.Project!.ImportedSource!;
            Equal(3, provenance.SourceVersion);
            Equal(0, provenance.TrustedReplayCommandCount);
            Equal(2, provenance.ResolvedSourceCommands.Length);
            True(provenance.ResolvedSourceCommands.All(command => !command.TrustedForNativeReplay),
                "Untested source state changes crossed the replay cutoff.");
            Equal(4, provenance.ResolvedRasterReferences.Length);
            Equal("extra", result.ExtraRasters.Single().SourceId);
            Equal(ImportRecoveryLevel.Partial, result.RecoveryLevel);
            var tied = provenance.ResolvedRasterReferences.Where(raster => raster.SourceOrder == 0).ToArray();
            Equal("extra", tied[0].SourceId);
            Equal("layer-bg", tied[1].SourceId);
            True(provenance.ResolvedUnsupportedMetadata.SequenceEqual(
                    provenance.ResolvedUnsupportedMetadata.Order(StringComparer.Ordinal)),
                "Unsupported metadata did not retain deterministic source ordering.");
            True(repository.Blobs.Count == 5,
                "Preview, trusted bases, and extra raster were not all sent to immutable blob storage.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task UntrustedEditableMappingFallsBack()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root);
            source = source with
            {
                Rasters = source.Rasters.SetItem(2, source.Rasters[2] with { SourceId = "unknown-mask" })
            };
            var repository = new RecordingImportedRepository(Path.Combine(root, "fallback.mapwright"));
            var result = await new ImportProject(_ => repository).ExecuteAsync(new ImportProjectRequest(
                source, ImportRecoveryMode.EditableRecoveredTerrain, root, "fallback.mapwright"));
            Equal(ImportRecoveryMode.EditableRecoveredTerrain, result.RequestedMode!.Value);
            Equal(ImportRecoveryMode.OriginalFlattenedAppearance, result.EffectiveMode!.Value);
            Equal(ImportRecoveryLevel.VisualOnly, result.RecoveryLevel);
            True(result.Project!.RequireRole(TerrainRole.Background).Locked,
                "Untrusted mapping did not retain the locked visual fallback.");
            True(result.OutcomeText.Contains("degradation", StringComparison.OrdinalIgnoreCase),
                "Fallback outcome was not actionable and honest.");
            Equal(3, result.ExtraRasters.Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ImportedBlobsReopenFromSqlite()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root, includeExtra: true);
            var destination = Path.Combine(root, "sqlite-project.mapwright");
            var result = await new ImportProject(path => new SqliteProjectRepository(path)).ExecuteAsync(
                new ImportProjectRequest(source, ImportRecoveryMode.EditableRecoveredTerrain,
                    root, "sqlite-project.mapwright"));
            True(!File.Exists(Path.Combine(destination, "manifest.json")),
                "Legacy manifest became a second authority.");
            var reopened = await new SqliteProjectRepository(destination).LoadAsync(
                result.Project!.ProjectId, CancellationToken.None);
            Equal(0L, reopened.Revision);
            Equal(result.Project.ImportedSource!.ResolvedRasterReferences.Length,
                reopened.ImportedSource!.ResolvedRasterReferences.Length);
            foreach (var raster in reopened.ImportedSource.ResolvedRasterReferences)
                True(File.Exists(Path.Combine(destination, "blobs", raster.BlobHash)),
                    $"Preserved raster {raster.SourceId}/{raster.SourceRole} is missing.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task NormalizedImportGeometry()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root, includeExtra: true);
            var fullCanvas = new ImportedRasterTransform(0, 0, 7559d / 3780, 8192d / 4097);
            var rasters = source.Rasters.Select((raster, index) => index < 3
                ? raster with { PixelWidth = 3780, PixelHeight = 4097, Transform = fullCanvas }
                : raster).ToImmutableArray();
            rasters = rasters.SetItem(3, rasters[3] with
            {
                Transform = new ImportedRasterTransform(100, 200, 3, 4)
            });
            source = source with
            {
                DocumentWidth = 7559, DocumentHeight = 8192,
                PreviewWidth = 3780, PreviewHeight = 4097, Rasters = rasters
            };
            var useCase = new ImportProject(path => new SqliteProjectRepository(path));
            var flattened = await useCase.ExecuteAsync(new ImportProjectRequest(source,
                ImportRecoveryMode.OriginalFlattenedAppearance, root, "flat.mapwright"));
            var editable = await useCase.ExecuteAsync(new ImportProjectRequest(source,
                ImportRecoveryMode.EditableRecoveredTerrain, root, "edit.mapwright"));
            foreach (var result in new[] { flattened, editable })
            {
                True(result.Published && result.Project is not null, "Recovery did not publish.");
                var project = result.Project!;
                Equal(3, project.StorageFormatVersion);
                Equal(922.7294921875, project.Width);
                Equal(1000d, project.Height);
                Equal(4096, project.EditingPixelHeight);
                Equal(3780, project.EditingPixelWidth);
                Equal(4097, project.ImportedSource!.PreviewHeight);
                Equal(7559d, project.ImportedSource!.SourceSceneWidth);
                Equal(8192d, project.ImportedSource.SourceSceneHeight);
                var scale = project.ImportedSource.SourceToMapScale;
                var partial = project.ImportedSource.ResolvedRasterReferences.Single(raster => raster.SourceId == "extra");
                True(Math.Abs(partial.Transform.OffsetX - 100 * scale) < 1e-9 &&
                     Math.Abs(partial.Transform.OffsetY - 200 * scale) < 1e-9 &&
                     Math.Abs(partial.Transform.ScaleX - 3 * scale) < 1e-9 &&
                     Math.Abs(partial.Transform.ScaleY - 4 * scale) < 1e-9,
                    "Partial raster placement was not composed into map units once.");
                var reopened = await new SqliteProjectRepository(result.ProjectDirectory!).LoadAsync(
                    project.ProjectId, CancellationToken.None);
                Equal(project.Revision, reopened.Revision);
                Equal(project.ImportedSource.SourceSha256, reopened.ImportedSource!.SourceSha256);
                Equal(partial.Transform, reopened.ImportedSource.ResolvedRasterReferences.Single(
                    raster => raster.SourceId == "extra").Transform);
            }
            Equal(ImportRecoveryMode.EditableRecoveredTerrain, editable.EffectiveMode!.Value);
            Equal(ImportRecoveryMode.OriginalFlattenedAppearance, flattened.EffectiveMode!.Value);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task InvalidDimensionsPublishNothing()
    {
        var root = TempRoot();
        try
        {
            var source = Package(root);
            var publications = 0;
            var useCase = new ImportProject(path =>
            {
                publications++;
                return new RecordingImportedRepository(path);
            });
            foreach (var dimensions in new[] { (0d, 100d), (double.NaN, 100d), (16_384d, 16_384d) })
                await ThrowsAsync<InvalidDataException>(() => useCase.ExecuteAsync(new ImportProjectRequest(
                    source with { DocumentWidth = dimensions.Item1, DocumentHeight = dimensions.Item2 },
                    ImportRecoveryMode.OriginalFlattenedAppearance, root, "invalid.mapwright")),
                    "Invalid or over-budget source scene dimensions were published.");
            Equal(0, publications);
            Equal(2048, MapUnitPolicy.ChooseImportEditingLongestEdge(1536, 1000));
            Equal(3072, MapUnitPolicy.ChooseImportEditingLongestEdge(2560, 1000));
            Equal(4096, MapUnitPolicy.ChooseImportEditingLongestEdge(8192, 7559));
            Throws<ArgumentOutOfRangeException>(() =>
                MapUnitPolicy.FromSource(double.Epsilon, double.Epsilon, 1024),
                "An overflowing source-to-map scale was accepted.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task ParsedSceneTransformSurvives()
    {
        var root = TempRoot();
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "placed.ink");
            var preview = Convert.ToBase64String(PngHeader(4, 2));
            var raster = Convert.ToBase64String(PngHeader(2, 2));
            var json = "{\"version\":3,\"title\":\"Placed\",\"scene\":{\"normSceneSize\":{\"w\":200,\"h\":100}}," +
                "\"previewDimensions\":{\"w\":4,\"h\":2}," +
                $"\"preview\":\"data:image/png;base64,{preview}\"," +
                "\"layers\":[{\"layerId\":\"extra\",\"layerImages\":[{" +
                "\"canvasName\":\"brush\",\"sceneTransform\":{" +
                "\"offsetX\":20,\"offsetY\":10,\"scaleX\":3,\"scaleY\":4}," +
                $"\"image\":\"data:image/png;base64,{raster}\"" + "}]}]}";
            await using (var file = File.Create(sourcePath))
            await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await using (var writer = new StreamWriter(gzip))
                await writer.WriteAsync(json);
            var imported = new Mapwright.Core.InkImportService().Import(sourcePath);
            var parsed = imported.Rasters.Single();
            Equal(20d, parsed.Transform!.OffsetX);
            Equal(3d, parsed.Transform.ScaleX);
            var package = Mapwright.Core.InkImportPackageFactory.Create(imported);
            var result = await new ImportProject(path => new RecordingImportedRepository(path))
                .ExecuteAsync(new ImportProjectRequest(package,
                    ImportRecoveryMode.OriginalFlattenedAppearance, root, "placed.mapwright"));
            var mapped = result.Project!.ImportedSource!.ResolvedRasterReferences.Single().Transform;
            Equal(100d, mapped.OffsetX);
            Equal(50d, mapped.OffsetY);
            Equal(15d, mapped.ScaleX);
            Equal(20d, mapped.ScaleY);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        bytes[16] = (byte)(width >> 24);
        bytes[17] = (byte)(width >> 16);
        bytes[18] = (byte)(width >> 8);
        bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24);
        bytes[21] = (byte)(height >> 16);
        bytes[22] = (byte)(height >> 8);
        bytes[23] = (byte)height;
        return bytes;
    }

    private static ImportSourcePackage Package(
        string root,
        bool includeExtra = false,
        bool equalSourceOrder = false)
    {
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "fixture.ink");
        File.WriteAllBytes(sourcePath, [31, 139, 8, 0, 1, 2, 3, 4]);
        var sourceHash = Hash(File.ReadAllBytes(sourcePath));
        var transform = new ImportedRasterTransform(0, 0, 2, 2);
        var rasters = ImmutableArray.CreateBuilder<ImportRasterPayload>();
        rasters.Add(Raster("layer-bg", "brush", 0, [1, 2, 3], transform,
            "straight-alpha sRGB colour checkpoint", "no coverage; colour only"));
        rasters.Add(Raster("layer-fg", "brush", 1, [4, 5, 6], transform,
            "straight-alpha sRGB colour checkpoint", "no coverage; colour only"));
        rasters.Add(Raster("layer-fg", "mask", 2, [7, 8, 9], transform,
            "non-colour mask checkpoint", "coverage is stored in alpha; RGB is ignored"));
        if (includeExtra)
            rasters.Add(Raster("extra", "brush", equalSourceOrder ? 0 : 3, [10, 11], transform,
                "unsupported source raster", "unknown coverage semantics"));
        return new ImportSourcePackage(
            sourcePath, sourceHash, 3, "Fixture", 200, 200,
            [12, 13, 14], 100, 100, rasters.ToImmutable(),
            [new ImportCommandPayload(0, "cmd-brush"), new ImportCommandPayload(1, "cmd-unknown-state")],
            ["root.futureMetadata", "history.unknownState"]);
    }

    private static ImportRasterPayload Raster(
        string id,
        string role,
        int order,
        byte[] bytes,
        ImportedRasterTransform transform,
        string colour,
        string coverage) => new(
            id, role, order, bytes, 100, 100, 1000 + order, transform,
            colour, coverage, "source checkpoint; baked source effects may be present");

    private static void AssertFixedRoles(MapProject project)
    {
        project.ValidateConnectedTerrain();
        Equal(2, project.TerrainLayers.Length);
        Equal(TerrainRole.Background, project.TerrainLayers[0].Role);
        Equal(TerrainRole.Foreground, project.TerrainLayers[1].Role);
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"mapwright-import-{Guid.NewGuid():N}");

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private sealed class RecordingImportedRepository(string projectDirectory) : IImportedProjectRepository
    {
        public string ProjectDirectory { get; } = projectDirectory;
        public int CreateCalls { get; private set; }
        public IReadOnlyCollection<ImportedBlobPayload> Blobs { get; private set; } = [];

        public Task CreateImportedAsync(
            MapProject initial,
            string sourcePath,
            IReadOnlyCollection<ImportedBlobPayload> blobs,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            Blobs = blobs;
            return Task.CompletedTask;
        }
    }
}
