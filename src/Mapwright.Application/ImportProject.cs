using System.Collections.Immutable;
using System.Security.Cryptography;
using Mapwright.Domain;

namespace Mapwright.Application;

public sealed record ImportBounds(
    long MaximumCompressedBytes,
    long MaximumExpandedBytes,
    long MaximumJsonTokens,
    int MaximumJsonDepth,
    int MaximumJsonTokenBytes,
    int MaximumBase64Characters,
    int MaximumImageDimension,
    long MaximumImagePixels,
    long MaximumDecodedImageBytes,
    long MaximumRetainedRasterBytes)
{
    public static ImportBounds Default { get; } = new(
        MaximumCompressedBytes: 256L * 1024 * 1024,
        MaximumExpandedBytes: 512L * 1024 * 1024,
        MaximumJsonTokens: 10_000_000,
        MaximumJsonDepth: 64,
        MaximumJsonTokenBytes: 128 * 1024 * 1024,
        MaximumBase64Characters: 128 * 1024 * 1024,
        MaximumImageDimension: 16_384,
        MaximumImagePixels: 64L * 1024 * 1024,
        MaximumDecodedImageBytes: 256L * 1024 * 1024,
        MaximumRetainedRasterBytes: 512L * 1024 * 1024);

    public void Validate()
    {
        if (MaximumCompressedBytes <= 0 || MaximumExpandedBytes <= 0 || MaximumJsonTokens <= 0 ||
            MaximumJsonDepth <= 0 || MaximumJsonTokenBytes <= 0 || MaximumBase64Characters <= 0 ||
            MaximumImageDimension <= 0 || MaximumImagePixels <= 0 || MaximumDecodedImageBytes <= 0 ||
            MaximumRetainedRasterBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(ImportBounds), "Every import budget must be positive.");
        if (MaximumJsonTokenBytes > MaximumExpandedBytes)
            throw new InvalidDataException("A JSON token cannot exceed the expanded-stream budget.");
        if (MaximumDecodedImageBytes > MaximumRetainedRasterBytes)
            throw new InvalidDataException("One decoded image cannot exceed the aggregate retained-raster budget.");
    }
}

public sealed class ImportRejectedException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record ImportRasterPayload(
    string SourceId,
    string SourceRole,
    int SourceOrder,
    byte[] PngBytes,
    int PixelWidth,
    int PixelHeight,
    long? HeadTransactionId,
    ImportedRasterTransform Transform,
    string ColourSemantics,
    string CoverageSemantics,
    string BakedEffectProvenance)
{
    public string Sha256 => Convert.ToHexString(SHA256.HashData(PngBytes)).ToLowerInvariant();

    public ImportedRasterReference ToReference(double sourceToMapScale = 1) => new(
        SourceId,
        SourceRole,
        SourceOrder,
        Sha256,
        PixelWidth,
        PixelHeight,
        HeadTransactionId,
        new ImportedRasterTransform(
            Transform.OffsetX * sourceToMapScale,
            Transform.OffsetY * sourceToMapScale,
            Transform.ScaleX * sourceToMapScale,
            Transform.ScaleY * sourceToMapScale),
        ColourSemantics,
        CoverageSemantics,
        BakedEffectProvenance);
}

public sealed record ImportCommandPayload(
    int SourceOrder,
    string CommandType,
    bool TrustedForNativeReplay = false);

public sealed record ImportSourcePackage(
    string SourcePath,
    string SourceSha256,
    int SourceVersion,
    string Title,
    double DocumentWidth,
    double DocumentHeight,
    byte[] PreviewPng,
    int PreviewWidth,
    int PreviewHeight,
    ImmutableArray<ImportRasterPayload> Rasters,
    ImmutableArray<ImportCommandPayload> Commands,
    ImmutableArray<string> UnsupportedMetadata)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        if (!File.Exists(SourcePath)) throw new FileNotFoundException("The import source is unavailable.", SourcePath);
        ValidateSha256(SourceSha256, nameof(SourceSha256));
        if (SourceVersion < 0) throw new ArgumentOutOfRangeException(nameof(SourceVersion));
        if (!double.IsFinite(DocumentWidth) || !double.IsFinite(DocumentHeight) ||
            DocumentWidth <= 0 || DocumentHeight <= 0 || DocumentWidth > 16_384 || DocumentHeight > 16_384)
            throw new InvalidDataException("Mapwright imports documents no larger than 16,384 by 16,384.");
        var bounds = ImportBounds.Default;
        if (DocumentWidth * DocumentHeight > bounds.MaximumImagePixels)
            throw new InvalidDataException("Source scene dimensions exceed the import pixel budget.");
        if (PreviewPng.Length == 0 || PreviewWidth <= 0 || PreviewHeight <= 0)
            throw new InvalidDataException("A preserved flattened preview is required for every recovery mode.");
        if (PreviewWidth > bounds.MaximumImageDimension || PreviewHeight > bounds.MaximumImageDimension ||
            checked((long)PreviewWidth * PreviewHeight) > bounds.MaximumImagePixels ||
            PreviewPng.LongLength > bounds.MaximumRetainedRasterBytes)
            throw new InvalidDataException("Flattened preview exceeds the import budget.");
        using var source = File.OpenRead(SourcePath);
        var actualSourceHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        if (!string.Equals(actualSourceHash, SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The import source changed after recovery review.");
        var retainedBytes = PreviewPng.LongLength;
        foreach (var raster in Rasters)
        {
            raster.ToReference().Validate();
            if (raster.PngBytes.Length == 0) throw new InvalidDataException("Imported raster bytes are empty.");
            if (raster.PixelWidth > bounds.MaximumImageDimension ||
                raster.PixelHeight > bounds.MaximumImageDimension ||
                checked((long)raster.PixelWidth * raster.PixelHeight) > bounds.MaximumImagePixels)
                throw new InvalidDataException("Imported raster dimensions exceed the import budget.");
            retainedBytes = checked(retainedBytes + raster.PngBytes.LongLength);
            if (retainedBytes > bounds.MaximumRetainedRasterBytes)
                throw new InvalidDataException("Imported raster bytes exceed the retained-source budget.");
        }
        for (var index = 0; index < Commands.Length; index++)
        {
            if (Commands[index].SourceOrder < 0 || string.IsNullOrWhiteSpace(Commands[index].CommandType))
                throw new InvalidDataException("Imported command provenance is incomplete.");
        }
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 value is required.", parameterName);
    }
}

public sealed record ImportedBlobPayload(string Sha256, ReadOnlyMemory<byte> Bytes, string Purpose);

public interface IImportedProjectRepository
{
    Task CreateImportedAsync(
        MapProject initial,
        string sourcePath,
        IReadOnlyCollection<ImportedBlobPayload> blobs,
        CancellationToken cancellationToken = default);
}

public sealed record ImportProjectRequest(
    ImportSourcePackage Source,
    ImportRecoveryMode? RecoveryMode,
    string ConfiguredProjectsFolder,
    string ProjectFolderName,
    string? DestinationOverride = null);

public enum ImportRecoveryLevel
{
    Editable = 0,
    Partial = 1,
    VisualOnly = 2,
    ChoiceRequired = 3
}

public sealed record ImportProjectResult(
    bool Published,
    string? ProjectDirectory,
    MapProject? Project,
    ImportRecoveryMode? RequestedMode,
    ImportRecoveryMode? EffectiveMode,
    ImportRecoveryLevel RecoveryLevel,
    ImmutableArray<ImportedRasterReference> ExtraRasters,
    string OutcomeText);

public sealed class ImportProject(Func<string, IImportedProjectRepository> repositoryFactory)
{
    public const string EditableMappingVersion = "ink-v3-full-canvas-terrain-v1";
    public const string VisualMappingVersion = "visual-preview-v1";

    public async Task<ImportProjectResult> ExecuteAsync(
        ImportProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Source.Validate();
        if (request.RecoveryMode is null)
            return new ImportProjectResult(false, null, null, null, null,
                ImportRecoveryLevel.ChoiceRequired, [],
                "Choose Editable recovered terrain or Original flattened appearance before creating a project.");

        var destination = ResolveDestination(request);
        var editingLongestEdge = MapUnitPolicy.ChooseImportEditingLongestEdge(
            request.Source.DocumentWidth, request.Source.DocumentHeight);
        var geometry = MapUnitPolicy.FromSource(
            request.Source.DocumentWidth, request.Source.DocumentHeight, editingLongestEdge);
        var orderedRasters = request.Source.Rasters
            .OrderBy(raster => raster.SourceOrder)
            .ThenBy(raster => raster.SourceId, StringComparer.Ordinal)
            .ThenBy(raster => raster.SourceRole, StringComparer.Ordinal)
            .ToImmutableArray();
        var references = orderedRasters.Select(raster => raster.ToReference(geometry.SourceToMapScale))
            .ToImmutableArray();
        foreach (var reference in references) reference.Validate();
        var previewHash = Convert.ToHexString(SHA256.HashData(request.Source.PreviewPng)).ToLowerInvariant();

        var requested = request.RecoveryMode.Value;
        var mapping = requested == ImportRecoveryMode.EditableRecoveredTerrain
            ? TryMapEditable(request.Source, orderedRasters, geometry.SourceToMapScale)
            : null;
        var editable = mapping is not null;
        var effective = editable ? ImportRecoveryMode.EditableRecoveredTerrain :
            ImportRecoveryMode.OriginalFlattenedAppearance;
        var level = editable
            ? (mapping!.ExtraRasters.Length == 0 ? ImportRecoveryLevel.Editable : ImportRecoveryLevel.Partial)
            : requested == ImportRecoveryMode.EditableRecoveredTerrain
                ? ImportRecoveryLevel.VisualOnly
                : ImportRecoveryLevel.VisualOnly;

        var sourceCommands = request.Source.Commands
            .OrderBy(command => command.SourceOrder)
            .ThenBy(command => command.CommandType, StringComparer.Ordinal)
            .Select(command => new ImportedCommandReference(
                command.SourceOrder, command.CommandType, command.TrustedForNativeReplay))
            .ToImmutableArray();
        var extraRasters = editable ? mapping!.ExtraRasters : references;
        var outcome = BuildOutcome(requested, level, editable, extraRasters.Length,
            sourceCommands.Count(command => !command.TrustedForNativeReplay));
        var provenance = new ImportedSourceReference(
            Path.GetFileName(request.Source.SourcePath),
            request.Source.SourceSha256.ToLowerInvariant(),
            request.Source.SourceSha256.ToLowerInvariant(),
            previewHash,
            request.Source.PreviewWidth,
            request.Source.PreviewHeight,
            effective,
            request.Source.SourceVersion,
            references,
            sourceCommands,
            request.Source.UnsupportedMetadata.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            TrustedReplayCommandCount: 0,
            MappingVersion: editable ? EditableMappingVersion : VisualMappingVersion,
            RecoveryLevel: level.ToString().ToLowerInvariant(),
            RecoveryReport: outcome);

        var background = new TerrainLayer(
            LayerId.New(),
            "Background",
            true,
            !editable,
            1,
            new CoastlineStyle(0),
            ImmutableArray<PaintStroke>.Empty,
            TerrainRole.Background,
            editable ? mapping!.Background.BlobHash : previewHash);
        var foreground = new TerrainLayer(
            LayerId.New(),
            "Foreground",
            true,
            false,
            1,
            new CoastlineStyle(0),
            ImmutableArray<PaintStroke>.Empty,
            TerrainRole.Foreground,
            editable ? mapping!.ForegroundColour.BlobHash : null,
            CoverageSourceBlobHash: editable ? mapping!.ForegroundCoverage.BlobHash : null);
        var project = MapProject.CreateNormalizedFromSource(
            ProjectId.New(), MapId.New(), request.Source.Title,
            request.Source.DocumentWidth, request.Source.DocumentHeight, editingLongestEdge,
            [background, foreground], provenance);
        project.ValidateConnectedTerrain();

        var blobs = orderedRasters
            .Select(raster => new ImportedBlobPayload(raster.Sha256, raster.PngBytes,
                $"source raster {raster.SourceId}/{raster.SourceRole}"))
            .Append(new ImportedBlobPayload(previewHash, request.Source.PreviewPng, "flattened preview"))
            .GroupBy(blob => blob.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        await repositoryFactory(destination).CreateImportedAsync(
            project, request.Source.SourcePath, blobs, cancellationToken).ConfigureAwait(false);
        return new ImportProjectResult(true, destination, project, requested, effective, level,
            extraRasters, outcome);
    }

    private static string ResolveDestination(ImportProjectRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConfiguredProjectsFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectFolderName);
        if (Path.IsPathRooted(request.ProjectFolderName) ||
            request.ProjectFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            request.ProjectFolderName is "." or "..")
            throw new InvalidDataException("Project folder name must be one safe local folder name.");

        var configuredRoot = Path.GetFullPath(request.ConfiguredProjectsFolder);
        var destination = request.DestinationOverride is null
            ? Path.GetFullPath(Path.Combine(configuredRoot, request.ProjectFolderName))
            : Path.GetFullPath(request.DestinationOverride);
        if (request.DestinationOverride is null &&
            !destination.StartsWith(configuredRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project destination escaped the configured projects folder.");

        var source = Path.GetFullPath(request.Source.SourcePath);
        var destinationPrefix = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The project destination must remain separate from the immutable source.");
        return destination;
    }

    private static EditableMapping? TryMapEditable(
        ImportSourcePackage source,
        ImmutableArray<ImportRasterPayload> ordered,
        double sourceToMapScale)
    {
        if (source.SourceVersion != 3) return null;
        var background = Single("layer-bg", "brush");
        var foreground = Single("layer-fg", "brush");
        var coverage = Single("layer-fg", "mask");
        if (background is null || foreground is null || coverage is null) return null;
        if (!IsTrustedFullCanvas(background) || !IsTrustedFullCanvas(foreground) || !IsTrustedFullCanvas(coverage))
            return null;
        if (!coverage.CoverageSemantics.Contains("alpha", StringComparison.OrdinalIgnoreCase)) return null;

        var used = new HashSet<(string, string)>(
            [(background.SourceId, background.SourceRole),
             (foreground.SourceId, foreground.SourceRole),
             (coverage.SourceId, coverage.SourceRole)]);
        var extras = ordered.Where(raster => !used.Contains((raster.SourceId, raster.SourceRole)))
            .Select(raster => raster.ToReference(sourceToMapScale)).ToImmutableArray();
        return new EditableMapping(background.ToReference(sourceToMapScale),
            foreground.ToReference(sourceToMapScale),
            coverage.ToReference(sourceToMapScale), extras);

        ImportRasterPayload? Single(string id, string role)
        {
            var matches = ordered.Where(raster => raster.SourceId == id && raster.SourceRole == role).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        bool IsTrustedFullCanvas(ImportRasterPayload raster) =>
            raster.PixelWidth == source.PreviewWidth && raster.PixelHeight == source.PreviewHeight &&
            raster.Transform.OffsetX == 0 && raster.Transform.OffsetY == 0 &&
            NearlyEqual(raster.Transform.ScaleX, source.DocumentWidth / source.PreviewWidth) &&
            NearlyEqual(raster.Transform.ScaleY, source.DocumentHeight / source.PreviewHeight);
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(1e-9, Math.Abs(right) * 1e-9);

    private static string BuildOutcome(
        ImportRecoveryMode requested,
        ImportRecoveryLevel level,
        bool editable,
        int extraRasterCount,
        int untrustedCommandCount)
    {
        if (!editable && requested == ImportRecoveryMode.EditableRecoveredTerrain)
            return "Imported with degradation. No verified terrain base matched the versioned mapping; the original flattened preview is preserved as locked Background and Foreground starts empty. Native source commands were not replayed.";
        if (!editable)
            return "Original flattened appearance selected. Background is the locked preserved preview, Foreground starts empty, and baked coasts remain pixels rather than editable geometry.";
        var partial = level == ImportRecoveryLevel.Partial
            ? $" {extraRasterCount} extra raster(s) remain preserved and listed as partial recovery."
            : "";
        return $"Editable recovered terrain uses verified version-3 full-canvas Background colour, Foreground colour, and Foreground alpha coverage.{partial} {untrustedCommandCount} source command(s) remain preserved but are not replayed.";
    }

    private sealed record EditableMapping(
        ImportedRasterReference Background,
        ImportedRasterReference ForegroundColour,
        ImportedRasterReference ForegroundCoverage,
        ImmutableArray<ImportedRasterReference> ExtraRasters);
}
