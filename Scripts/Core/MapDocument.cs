using System.Text.Json.Serialization;
using System.Collections.Immutable;
using Mapwright.Application;

namespace Mapwright.Core;

public sealed class MapDocument
{
    public int FormatVersion { get; init; } = 1;
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    public Guid MapId { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled map";
    public double Width { get; set; } = 8192;
    public double Height { get; set; } = 8192;
    public long Revision { get; set; }
    public List<MapLayer> Layers { get; init; } = [];
    public ImportProvenance? Import { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(RasterLayer), "raster")]
[JsonDerivedType(typeof(UnresolvedObjectLayer), "objects")]
public abstract class MapLayer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public float Opacity { get; set; } = 1f;
}

public sealed class RasterLayer : MapLayer
{
    public required string BlobHash { get; set; }
    public required string Role { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public bool Authoritative { get; set; } = true;
}

public sealed class UnresolvedObjectLayer : MapLayer
{
    public int EntityCount { get; set; }
    public List<long> AssetIds { get; init; } = [];
}

public sealed class ImportProvenance
{
    public required string SourceFormat { get; init; }
    public required string SourceFileName { get; init; }
    public string? SourceSha256 { get; init; }
    public string? SourceBlobHash { get; set; }
    public int SourceVersion { get; init; }
    public Dictionary<string, int> CommandCounts { get; init; } = [];
    public Dictionary<string, int> EntityCounts { get; init; } = [];
    public List<long> UnresolvedAssetIds { get; init; } = [];
    public List<ImportedCommandDescriptor> SourceCommands { get; init; } = [];
    public List<string> UnsupportedMetadata { get; init; } = [];
    public int TrustedReplayCommandCount { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed record ImportedCommandDescriptor(
    int SourceOrder,
    string CommandType,
    bool TrustedForNativeReplay);

public sealed record ImportedRasterTransform(
    double OffsetX,
    double OffsetY,
    double ScaleX,
    double ScaleY);

public sealed record ImportedRaster(
    string LayerId,
    string Role,
    byte[] PngBytes,
    int Width,
    int Height,
    long? HeadTransactionId,
    int SourceOrder = 0,
    ImportedRasterTransform? Transform = null,
    string ColourSemantics = "straight-alpha sRGB colour checkpoint",
    string CoverageSemantics = "no coverage; colour only",
    string BakedEffectProvenance = "source checkpoint; baked source effects may be present");

public sealed class InkImportResult
{
    public required string SourcePath { get; init; }
    public required MapDocument Document { get; init; }
    public required IReadOnlyList<ImportedRaster> Rasters { get; init; }
    public byte[]? PreviewPng { get; init; }
    public int PreviewWidth { get; init; }
    public int PreviewHeight { get; init; }
    public long PeakJsonTokenBufferBytes { get; init; }
    public long RetainedRasterBytes { get; init; }
    public required string Report { get; init; }
}

public static class InkImportPackageFactory
{
    public static ImportSourcePackage Create(InkImportResult import)
    {
        var provenance = import.Document.Import
            ?? throw new InvalidDataException("Import provenance is missing.");
        var rasters = import.Rasters.Select(raster => new ImportRasterPayload(
            raster.LayerId,
            raster.Role,
            raster.SourceOrder,
            raster.PngBytes,
            raster.Width,
            raster.Height,
            raster.HeadTransactionId,
            new Mapwright.Domain.ImportedRasterTransform(
                raster.Transform?.OffsetX ?? 0,
                raster.Transform?.OffsetY ?? 0,
                raster.Transform?.ScaleX ?? import.Document.Width / raster.Width,
                raster.Transform?.ScaleY ?? import.Document.Height / raster.Height),
            raster.ColourSemantics,
            raster.CoverageSemantics,
            raster.BakedEffectProvenance)).ToImmutableArray();
        var commands = provenance.SourceCommands.Select(command => new ImportCommandPayload(
            command.SourceOrder, command.CommandType, command.TrustedForNativeReplay)).ToImmutableArray();
        return new ImportSourcePackage(
            import.SourcePath,
            provenance.SourceSha256 ?? throw new InvalidDataException("Source hash is missing."),
            provenance.SourceVersion,
            import.Document.Title,
            import.Document.Width,
            import.Document.Height,
            import.PreviewPng ?? throw new InvalidDataException("Flattened preview is missing."),
            import.PreviewWidth,
            import.PreviewHeight,
            rasters,
            commands,
            provenance.UnsupportedMetadata.ToImmutableArray());
    }
}
