using System.Collections.Immutable;

namespace Mapwright.Domain;

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.NewGuid());
}

public readonly record struct MapId(Guid Value)
{
    public static MapId New() => new(Guid.NewGuid());
}

public readonly record struct LayerId(Guid Value)
{
    public static LayerId New() => new(Guid.NewGuid());
}

public readonly record struct StrokeId(Guid Value)
{
    public static StrokeId New() => new(Guid.NewGuid());
}

public readonly record struct RiverId(Guid Value)
{
    public static RiverId New() => new(Guid.NewGuid());
}

public readonly record struct CommandId(Guid Value)
{
    public static CommandId New() => new(Guid.NewGuid());
}

public enum TerrainRole
{
    Background = 0,
    Foreground = 1
}

public enum TerrainStrokeKind
{
    Texture = 0,
    Coverage = 1
}

public enum ImportRecoveryMode
{
    OriginalFlattenedAppearance = 0,
    EditableRecoveredTerrain = 1
}

public sealed record ImportedRasterTransform(
    double OffsetX,
    double OffsetY,
    double ScaleX,
    double ScaleY)
{
    public void Validate()
    {
        if (!double.IsFinite(OffsetX) || !double.IsFinite(OffsetY) ||
            !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) ||
            ScaleX <= 0 || ScaleY <= 0)
            throw new InvalidDataException("Imported raster transforms must be finite with positive scale.");
    }
}

public sealed record ImportedRasterReference(
    string SourceId,
    string SourceRole,
    int SourceOrder,
    string BlobHash,
    int PixelWidth,
    int PixelHeight,
    long? HeadTransactionId,
    ImportedRasterTransform Transform,
    string ColourSemantics,
    string CoverageSemantics,
    string BakedEffectProvenance)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceRole);
        ValidateSha256(BlobHash, nameof(BlobHash));
        if (SourceOrder < 0) throw new ArgumentOutOfRangeException(nameof(SourceOrder));
        if (PixelWidth <= 0 || PixelHeight <= 0)
            throw new InvalidDataException("Imported raster dimensions must be positive.");
        Transform.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(ColourSemantics);
        ArgumentException.ThrowIfNullOrWhiteSpace(CoverageSemantics);
        ArgumentException.ThrowIfNullOrWhiteSpace(BakedEffectProvenance);
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 value is required.", parameterName);
    }
}

public sealed record ImportedCommandReference(
    int SourceOrder,
    string CommandType,
    bool TrustedForNativeReplay)
{
    public void Validate()
    {
        if (SourceOrder < 0) throw new ArgumentOutOfRangeException(nameof(SourceOrder));
        ArgumentException.ThrowIfNullOrWhiteSpace(CommandType);
    }
}

public sealed record ImportedSourceReference(
    string SourceFileName,
    string SourceSha256,
    string SourceBlobHash,
    string PreviewBlobHash,
    int PreviewWidth,
    int PreviewHeight,
    ImportRecoveryMode RecoveryMode,
    int SourceVersion = 0,
    ImmutableArray<ImportedRasterReference>? RasterReferences = null,
    ImmutableArray<ImportedCommandReference>? SourceCommands = null,
    ImmutableArray<string>? UnsupportedMetadata = null,
    int TrustedReplayCommandCount = 0,
    string MappingVersion = "visual-preview-v1",
    string RecoveryLevel = "visual-only",
    string RecoveryReport = "",
    string? DisplaySourceBlobHash = null,
    int DisplaySourceWidth = 0,
    int DisplaySourceHeight = 0,
    double SourceSceneWidth = 0,
    double SourceSceneHeight = 0,
    double SourceToMapScale = 0)
{
    public ImmutableArray<ImportedRasterReference> ResolvedRasterReferences =>
        RasterReferences ?? ImmutableArray<ImportedRasterReference>.Empty;

    public ImmutableArray<ImportedCommandReference> ResolvedSourceCommands =>
        SourceCommands ?? ImmutableArray<ImportedCommandReference>.Empty;

    public ImmutableArray<string> ResolvedUnsupportedMetadata =>
        UnsupportedMetadata ?? ImmutableArray<string>.Empty;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceFileName);
        ValidateSha256(SourceSha256, nameof(SourceSha256));
        ValidateSha256(SourceBlobHash, nameof(SourceBlobHash));
        ValidateSha256(PreviewBlobHash, nameof(PreviewBlobHash));
        if (DisplaySourceBlobHash is null)
        {
            if (DisplaySourceWidth != 0 || DisplaySourceHeight != 0)
                throw new InvalidDataException("Display source dimensions require a blob identity.");
        }
        else
        {
            ValidateSha256(DisplaySourceBlobHash, nameof(DisplaySourceBlobHash));
            if (DisplaySourceWidth <= 0 || DisplaySourceHeight <= 0)
                throw new InvalidDataException("Display source blob requires positive dimensions.");
        }
        if (!string.Equals(SourceSha256, SourceBlobHash, StringComparison.Ordinal))
            throw new InvalidDataException("The immutable source blob must retain the imported source hash.");
        if (PreviewWidth <= 0 || PreviewHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(PreviewWidth), "Preview dimensions must be positive.");
        if (SourceSceneWidth != 0 || SourceSceneHeight != 0 || SourceToMapScale != 0)
        {
            if (!double.IsFinite(SourceSceneWidth) || !double.IsFinite(SourceSceneHeight) ||
                !double.IsFinite(SourceToMapScale) || SourceSceneWidth <= 0 ||
                SourceSceneHeight <= 0 || SourceToMapScale <= 0)
                throw new InvalidDataException("Imported scene dimensions and source-to-map scale must be positive and finite.");
        }
        if (SourceVersion < 0) throw new ArgumentOutOfRangeException(nameof(SourceVersion));
        foreach (var raster in ResolvedRasterReferences) raster.Validate();
        foreach (var command in ResolvedSourceCommands) command.Validate();
        if (TrustedReplayCommandCount < 0 || TrustedReplayCommandCount > ResolvedSourceCommands.Length)
            throw new InvalidDataException("Trusted replay cutoff is outside the preserved command sequence.");
        if (ResolvedSourceCommands.Take(TrustedReplayCommandCount).Any(command => !command.TrustedForNativeReplay))
            throw new InvalidDataException("Trusted replay cutoff crosses an untested source command.");
        ArgumentException.ThrowIfNullOrWhiteSpace(MappingVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(RecoveryLevel);
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A lowercase or uppercase SHA-256 value is required.", parameterName);
    }
}

public sealed record ResolvedBrush(
    string TextureHash,
    double Radius,
    double Hardness,
    double Opacity,
    double Flow,
    double Spacing,
    double Rotation,
    int Seed,
    int AlgorithmVersion)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TextureHash)) throw new ArgumentException("A brush texture hash is required.");
        if (Radius <= 0) throw new ArgumentOutOfRangeException(nameof(Radius));
        if (Hardness is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Hardness));
        if (Opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Opacity));
        if (Flow is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Flow));
        if (Spacing <= 0) throw new ArgumentOutOfRangeException(nameof(Spacing));
        if (AlgorithmVersion <= 0) throw new ArgumentOutOfRangeException(nameof(AlgorithmVersion));
    }
}

public enum TextureTipKind
{
    Round = 0,
    Square = 1,
    Irregular = 2,
    Edged = 3
}

public enum TextureTaperKind
{
    None = 0,
    SmoothBothEnds = 1
}

public static class TextureBrushLimits
{
    public const double MinimumDiameter = 1;
    public const double MaximumDiameter = 2048;
    public const double MinimumSpacing = 0.05;
    public const double MaximumSpacing = 1;
    public const double MinimumTextureScale = 0.0625;
    public const double MaximumTextureScale = 16;
    public const double MinimumRotationDegrees = -180;
    public const double MaximumRotationDegrees = 180;
    public const double MinimumTaperScale = 0.08;
}

public readonly record struct ResolvedTextureDab(
    MapPoint Offset,
    double RotationDegrees,
    double RadiusScale);

public sealed record ResolvedTextureBrush(
    string PresetId,
    TextureTipKind Tip,
    int TipVersion,
    double Radius,
    double Hardness,
    double Opacity,
    double Flow,
    double Spacing,
    string TextureSha256,
    double TextureScale,
    double RotationDegrees,
    double Jitter,
    double Roughness,
    double CornerSmoothing,
    TextureTaperKind Taper,
    int TaperVersion,
    int Seed,
    int RandomVersion,
    int AlgorithmVersion)
{
    public double Diameter => Radius * 2;

    public static ResolvedTextureBrush FromDiameter(
        string presetId,
        TextureTipKind tip,
        double diameter,
        double hardness,
        double opacity,
        double flow,
        double spacing,
        string textureSha256,
        double textureScale,
        double rotationDegrees,
        double jitter,
        double roughness,
        double cornerSmoothing,
        TextureTaperKind taper,
        int seed,
        int tipVersion = 1,
        int taperVersion = 1,
        int randomVersion = 1,
        int algorithmVersion = 1)
    {
        var recipe = new ResolvedTextureBrush(
            presetId, tip, tipVersion, diameter * 0.5, hardness, opacity, flow, spacing,
            textureSha256, textureScale, rotationDegrees, jitter, roughness, cornerSmoothing,
            taper, taperVersion, seed, randomVersion, algorithmVersion);
        recipe.Validate();
        return recipe;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PresetId);
        if (!Enum.IsDefined(Tip)) throw new ArgumentOutOfRangeException(nameof(Tip));
        if (!Enum.IsDefined(Taper)) throw new ArgumentOutOfRangeException(nameof(Taper));
        if (TipVersion != 1) throw new InvalidDataException($"Unsupported texture tip version {TipVersion}.");
        if (TaperVersion != 1) throw new InvalidDataException($"Unsupported taper version {TaperVersion}.");
        if (RandomVersion != 1) throw new InvalidDataException($"Unsupported texture random version {RandomVersion}.");
        if (AlgorithmVersion != 1)
            throw new InvalidDataException($"Unsupported texture brush algorithm version {AlgorithmVersion}.");
        RequireRange(Diameter, TextureBrushLimits.MinimumDiameter, TextureBrushLimits.MaximumDiameter,
            nameof(Diameter));
        RequireRange(Hardness, 0, 1, nameof(Hardness));
        RequireRange(Opacity, 0, 1, nameof(Opacity));
        RequireRange(Flow, 0, 1, nameof(Flow));
        RequireRange(Spacing, TextureBrushLimits.MinimumSpacing, TextureBrushLimits.MaximumSpacing,
            nameof(Spacing));
        ValidateSha256(TextureSha256, nameof(TextureSha256));
        RequireRange(TextureScale, TextureBrushLimits.MinimumTextureScale,
            TextureBrushLimits.MaximumTextureScale, nameof(TextureScale));
        RequireRange(RotationDegrees, TextureBrushLimits.MinimumRotationDegrees,
            TextureBrushLimits.MaximumRotationDegrees, nameof(RotationDegrees));
        RequireRange(Jitter, 0, 1, nameof(Jitter));
        RequireRange(Roughness, 0, 1, nameof(Roughness));
        RequireRange(CornerSmoothing, 0, 1, nameof(CornerSmoothing));
        if (Tip != TextureTipKind.Edged && CornerSmoothing != 0)
            throw new InvalidDataException("Corner smoothing applies only to edged texture tips.");
    }

    public ResolvedTextureDab ResolveDab(int sampleIndex)
    {
        if (sampleIndex < 0) throw new ArgumentOutOfRangeException(nameof(sampleIndex));
        Validate();
        var state = unchecked((ulong)(uint)Seed) ^ ((ulong)(uint)sampleIndex << 32) ^ 0x9E3779B97F4A7C15UL;
        var first = Unit(ref state);
        var second = Unit(ref state);
        var angle = first * Math.Tau;
        var magnitude = Math.Sqrt(second) * Radius * Jitter;
        return new ResolvedTextureDab(
            new MapPoint(Math.Cos(angle) * magnitude, Math.Sin(angle) * magnitude),
            RotationDegrees,
            1);
    }

    private static double Unit(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1.0 / (1UL << 53));
    }

    private static void RequireRange(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"Value must be finite and between {minimum} and {maximum}.");
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 texture identity is required.", parameterName);
    }
}

public sealed record TexturePresetDefinition(
    string Id,
    TextureTipKind Tip,
    double Hardness,
    double Spacing,
    double Roughness,
    double CornerSmoothing,
    TextureTaperKind Taper);

public static class TexturePresetCatalog
{
    public static ImmutableArray<TexturePresetDefinition> Inventory { get; } =
    [
        new("hard-round", TextureTipKind.Round, 0.92, 0.20, 0, 0, TextureTaperKind.None),
        new("soft-round", TextureTipKind.Round, 0.35, 0.18, 0, 0, TextureTaperKind.None),
        new("tapered-round", TextureTipKind.Round, 0.82, 0.16, 0, 0, TextureTaperKind.SmoothBothEnds),
        new("square-pencil", TextureTipKind.Square, 1, 0.12, 0, 0, TextureTaperKind.None),
        new("chalk-irregular", TextureTipKind.Irregular, 0.72, 0.14, 0.68, 0, TextureTaperKind.None),
        new("grain-irregular", TextureTipKind.Irregular, 0.46, 0.10, 0.42, 0, TextureTaperKind.None),
        new("edged-texture", TextureTipKind.Edged, 0.86, 0.18, 0.40, 0.50, TextureTaperKind.None)
    ];

    public static ResolvedTextureBrush Resolve(
        string presetId,
        string textureSha256,
        int seed,
        double diameter = 96)
    {
        var preset = Inventory.SingleOrDefault(candidate => candidate.Id == presetId)
            ?? throw new KeyNotFoundException($"Texture preset '{presetId}' does not exist.");
        return ResolvedTextureBrush.FromDiameter(
            preset.Id, preset.Tip, diameter, preset.Hardness, 1, 0.8, preset.Spacing,
            textureSha256, 1, 0, 0, preset.Roughness, preset.CornerSmoothing, preset.Taper, seed);
    }
}

public readonly record struct TextureStrokeSample(MapPoint Position, double RadiusScale)
{
    public void Validate()
    {
        Position.ValidateFinite();
        if (!double.IsFinite(RadiusScale) || RadiusScale is < TextureBrushLimits.MinimumTaperScale or > 1)
            throw new ArgumentOutOfRangeException(nameof(RadiusScale));
    }
}

public sealed record TexturePaintStroke(
    StrokeId Id,
    ImmutableArray<TextureStrokeSample> Samples,
    ResolvedTextureBrush Recipe,
    MapPoint TextureAnchor)
{
    public static TexturePaintStroke Create(
        StrokeId id,
        ImmutableArray<MapPoint> positions,
        ResolvedTextureBrush recipe,
        MapPoint textureAnchor)
    {
        if (positions.IsDefaultOrEmpty)
            throw new ArgumentException("A texture stroke requires at least one document-space sample.",
                nameof(positions));
        recipe.Validate();
        textureAnchor.ValidateFinite();
        foreach (var position in positions) position.ValidateFinite();

        var distances = new double[positions.Length];
        for (var index = 1; index < positions.Length; index++)
            distances[index] = distances[index - 1] + positions[index - 1].DistanceTo(positions[index]);
        var total = distances[^1];
        var samples = ImmutableArray.CreateBuilder<TextureStrokeSample>(positions.Length);
        for (var index = 0; index < positions.Length; index++)
        {
            var progress = total > 0 ? distances[index] / total : 0.5;
            var scale = recipe.Taper == TextureTaperKind.None ? 1 : TaperScale(progress);
            samples.Add(new TextureStrokeSample(positions[index], scale));
        }

        return new TexturePaintStroke(id, samples.MoveToImmutable(), recipe, textureAnchor);
    }

    public MapBounds Bounds
    {
        get
        {
            Validate();
            MapBounds? bounds = null;
            foreach (var sample in Samples)
            {
                var support = Recipe.Radius * ((sample.RadiusScale * Math.Sqrt(2)) + Recipe.Jitter);
                var sampleBounds = MapBounds.AroundSegment(sample.Position, sample.Position, support);
                bounds = bounds is null ? sampleBounds : bounds.Value.Union(sampleBounds);
            }
            return bounds!.Value;
        }
    }

    public void Validate()
    {
        if (Samples.IsDefaultOrEmpty)
            throw new ArgumentException("A texture stroke requires at least one document-space sample.");
        Recipe.Validate();
        TextureAnchor.ValidateFinite();
        foreach (var sample in Samples) sample.Validate();
    }

    private static double TaperScale(double progress)
    {
        var edge = Math.Clamp(Math.Min(progress, 1 - progress) / 0.20, 0, 1);
        var smooth = edge * edge * (3 - (2 * edge));
        return TextureBrushLimits.MinimumTaperScale + ((1 - TextureBrushLimits.MinimumTaperScale) * smooth);
    }
}

public enum LandShape
{
    EdgedPolygon = 0,
    RoundSoft = 1
}

public enum LandOperation
{
    Add = 0,
    Subtract = 1
}

public static class LandBrushLimits
{
    public const double MinimumDiameter = 1;
    public const double MaximumDiameter = 4096;
}

public static class CoverageMath
{
    public static double Apply(double current, double footprint, LandOperation operation)
    {
        RequireUnit(current, nameof(current));
        RequireUnit(footprint, nameof(footprint));
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));

        var composed = operation == LandOperation.Add
            ? current + (footprint * (1 - current))
            : current * (1 - footprint);
        return Math.Clamp(composed, 0, 1);
    }

    public static void RequireUnit(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName, value,
                "Coverage must be finite and between zero and one, inclusive.");
    }
}

public sealed record ResolvedLandBrush(
    LandShape Shape,
    double Radius,
    double Roughness,
    double CornerSmoothing,
    double Softness,
    int Seed,
    int ShapeVersion = 1,
    int RandomVersion = 1,
    int AlgorithmVersion = 1)
{
    private const int PolygonSides = 12;

    public double Diameter => Radius * 2;

    public static ResolvedLandBrush FromDiameter(
        LandShape shape,
        double diameter,
        double roughness,
        double cornerSmoothing,
        double softness,
        int seed)
    {
        var brush = new ResolvedLandBrush(
            shape, diameter * 0.5, roughness, cornerSmoothing, softness, seed);
        brush.Validate();
        return brush;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Shape)) throw new ArgumentOutOfRangeException(nameof(Shape));
        RequireRange(Diameter, LandBrushLimits.MinimumDiameter, LandBrushLimits.MaximumDiameter,
            nameof(Diameter));
        RequireRange(Roughness, 0, 1, nameof(Roughness));
        RequireRange(CornerSmoothing, 0, 1, nameof(CornerSmoothing));
        RequireRange(Softness, 0, 1, nameof(Softness));
        if (ShapeVersion != 1) throw new InvalidDataException($"Unsupported land shape version {ShapeVersion}.");
        if (RandomVersion != 1) throw new InvalidDataException($"Unsupported land random version {RandomVersion}.");
        if (AlgorithmVersion != 1)
            throw new InvalidDataException($"Unsupported land brush algorithm version {AlgorithmVersion}.");
        if (Shape == LandShape.EdgedPolygon && Softness != 0)
            throw new InvalidDataException("Edged polygon has a firm alpha edge; Softness applies only to Round soft.");
        if (Shape == LandShape.RoundSoft && (Roughness != 0 || CornerSmoothing != 0))
            throw new InvalidDataException("Roughness and corner Smooth apply only to Edged polygon.");
    }

    public double CoverageAtOffset(MapPoint offset)
    {
        Validate();
        offset.ValidateFinite();
        var distance = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y));
        if (Shape == LandShape.EdgedPolygon)
        {
            var angle = Math.Atan2(offset.Y, offset.X);
            return distance <= EdgedRadius(angle) ? 1 : 0;
        }

        if (distance > Radius) return 0;
        if (Softness == 0) return 1;
        var innerRadius = Radius * (1 - Softness);
        if (distance <= innerRadius) return 1;
        return Math.Clamp((Radius - distance) / (Radius - innerRadius), 0, 1);
    }

    private double EdgedRadius(double angle)
    {
        var sector = Math.Tau / PolygonSides;
        var local = Math.Abs((((angle + (sector * 0.5)) % sector) + sector) % sector - (sector * 0.5));
        var apothem = Radius * Math.Cos(Math.PI / PolygonSides);
        var polygonRadius = apothem / Math.Cos(local);
        var normalized = (((angle % Math.Tau) + Math.Tau) % Math.Tau) / sector;
        var vertex = (int)Math.Floor(normalized);
        var next = (vertex + 1) % PolygonSides;
        var t = normalized - Math.Floor(normalized);
        var roughRadius = Radius * (1 + (Roughness * 0.18 * Lerp(Noise(vertex), Noise(next), t)));
        var edged = Math.Min(polygonRadius, roughRadius);
        return Lerp(edged, Radius, CornerSmoothing);
    }

    private double Noise(int index)
    {
        var state = unchecked((uint)Seed) ^ unchecked((uint)(index * 0x9E3779B9));
        state ^= state >> 16;
        state *= 0x7FEB352D;
        state ^= state >> 15;
        state *= 0x846CA68B;
        state ^= state >> 16;
        return ((state & 0x00FFFFFF) / (double)0x007FFFFF) - 1;
    }

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private static void RequireRange(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"Value must be finite and between {minimum} and {maximum}.");
    }
}

public sealed record LandStroke(
    StrokeId Id,
    ImmutableArray<MapPoint> Samples,
    ResolvedLandBrush Recipe,
    LandOperation Operation)
{
    public MapBounds Bounds
    {
        get
        {
            Validate();
            var bounds = MapBounds.AroundSegment(Samples[0], Samples[0], Recipe.Radius);
            for (var index = 1; index < Samples.Length; index++)
                bounds = bounds.Union(MapBounds.AroundSegment(
                    Samples[index - 1], Samples[index], Recipe.Radius));
            return bounds;
        }
    }

    public void Validate()
    {
        if (Samples.IsDefaultOrEmpty)
            throw new ArgumentException("A Land stroke requires at least one document-space sample.");
        if (!Enum.IsDefined(Operation)) throw new ArgumentOutOfRangeException(nameof(Operation));
        Recipe.Validate();
        foreach (var sample in Samples) sample.ValidateFinite();
    }

    public double CoverageAt(MapPoint point)
    {
        Validate();
        point.ValidateFinite();
        if (Samples.Length == 1)
            return Recipe.CoverageAtOffset(point.Subtract(Samples[0]));

        var coverage = 0d;
        for (var index = 1; index < Samples.Length; index++)
        {
            var centre = MapGeometry.ClosestPointOnSegment(point, Samples[index - 1], Samples[index]);
            coverage = Math.Max(coverage, Recipe.CoverageAtOffset(point.Subtract(centre)));
        }
        return coverage;
    }
}

public sealed record PaintStroke(
    StrokeId Id,
    ImmutableArray<MapPoint> Samples,
    ResolvedBrush Brush,
    bool AddsCoverage,
    TerrainStrokeKind Kind = TerrainStrokeKind.Texture)
{
    public MapBounds Bounds
    {
        get
        {
            if (Samples.IsDefaultOrEmpty) throw new InvalidOperationException("A stroke requires samples.");
            var bounds = MapBounds.AroundSegment(Samples[0], Samples[0], Brush.Radius);
            for (var index = 1; index < Samples.Length; index++)
                bounds = bounds.Union(MapBounds.AroundSegment(Samples[index - 1], Samples[index], Brush.Radius));
            return bounds;
        }
    }

    public void Validate()
    {
        if (Samples.IsDefaultOrEmpty) throw new ArgumentException("A stroke requires at least one sample.");
        Brush.Validate();
    }
}

public sealed record CoastlineStyle(double EffectReach)
{
    public void Validate() => ArgumentOutOfRangeException.ThrowIfNegative(EffectReach);
}

public sealed record TerrainLayer(
    LayerId Id,
    string Name,
    bool Visible,
    bool Locked,
    double Opacity,
    CoastlineStyle Coastline,
    ImmutableArray<PaintStroke> Strokes,
    TerrainRole Role = TerrainRole.Foreground,
    string? SourceBlobHash = null,
    bool Solo = false,
    ImmutableArray<TexturePaintStroke>? TextureStrokes = null,
    string? CoverageSourceBlobHash = null,
    ImmutableArray<LandStroke>? LandStrokes = null)
{
    public ImmutableArray<TexturePaintStroke> ResolvedTextureStrokes =>
        TextureStrokes ?? ImmutableArray<TexturePaintStroke>.Empty;

    public ImmutableArray<LandStroke> ResolvedLandStrokes =>
        LandStrokes ?? ImmutableArray<LandStroke>.Empty;

    public TerrainLayer Add(PaintStroke stroke)
    {
        if (Locked) throw new InvalidOperationException($"Layer '{Name}' is locked.");
        stroke.Validate();
        return this with { Strokes = Strokes.Add(stroke) };
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Role))
            throw new InvalidDataException($"Unknown terrain role value {(int)Role}.");
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (!double.IsFinite(Opacity) || Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        foreach (var stroke in ResolvedTextureStrokes) stroke.Validate();
        foreach (var stroke in ResolvedLandStrokes) stroke.Validate();
        if (Role != TerrainRole.Foreground && !ResolvedLandStrokes.IsEmpty)
            throw new InvalidDataException("Only Foreground may own Land coverage strokes.");
        if (SourceBlobHash is not null) ValidateOptionalSha256(SourceBlobHash, nameof(SourceBlobHash));
        if (CoverageSourceBlobHash is not null)
        {
            if (Role != TerrainRole.Foreground)
                throw new InvalidDataException("Only Foreground may reference imported coverage.");
            ValidateOptionalSha256(CoverageSourceBlobHash, nameof(CoverageSourceBlobHash));
        }
    }

    private static void ValidateOptionalSha256(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 value is required.", parameterName);
    }

    public TerrainLayer AddTexture(TexturePaintStroke stroke)
    {
        if (Locked) throw new InvalidOperationException($"Layer '{Name}' is locked.");
        stroke.Validate();
        return this with { TextureStrokes = ResolvedTextureStrokes.Add(stroke) };
    }

    public TerrainLayer AddLand(LandStroke stroke)
    {
        if (Locked) throw new InvalidOperationException($"Layer '{Name}' is locked.");
        if (Role != TerrainRole.Foreground)
            throw new InvalidDataException("Only Foreground may own Land coverage strokes.");
        stroke.Validate();
        return this with { LandStrokes = ResolvedLandStrokes.Add(stroke) };
    }
}

public static class RiverLimits
{
    public const double MinimumWidth = 1;
    public const double MaximumWidth = 4096;
    public const double MinimumBankSoftness = 0;
    public const double MaximumBankSoftness = 1;
}

public enum RiverWidthInterpolation
{
    Linear = 0
}

public sealed record RiverWidthProfile(
    ImmutableArray<double> Widths,
    RiverWidthInterpolation Interpolation = RiverWidthInterpolation.Linear,
    int Version = 1)
{
    public void Validate(int pointCount)
    {
        if (Widths.IsDefaultOrEmpty || Widths.Length != pointCount)
            throw new ArgumentException("A river width profile requires one width per centreline point.");
        if (!Enum.IsDefined(Interpolation))
            throw new ArgumentOutOfRangeException(nameof(Interpolation));
        if (Version != 1) throw new InvalidDataException($"Unsupported river width profile version {Version}.");
        foreach (var width in Widths) River.ValidateWidth(width, nameof(Widths));
    }
}

public readonly record struct RiverGeometrySegment(
    MapPoint Start,
    MapPoint End,
    double StartWidth,
    double EndWidth,
    double BankSoftness);

public sealed record River(
    RiverId Id,
    LayerId TargetLayerId,
    ImmutableArray<MapPoint> Points,
    double Width,
    RiverWidthProfile? WidthProfile = null,
    double BankSoftness = 0,
    bool Enabled = true,
    int GeometryVersion = 1)
{
    public RiverWidthProfile ResolvedWidthProfile => WidthProfile ?? new RiverWidthProfile(
        Enumerable.Repeat(Width, Points.Length).ToImmutableArray());

    public static River Create(
        RiverId id,
        LayerId targetLayerId,
        ImmutableArray<MapPoint> points,
        ImmutableArray<double> widths,
        double bankSoftness,
        bool enabled = true)
    {
        var river = new River(id, targetLayerId, points, widths.IsDefaultOrEmpty ? 0 : widths[0],
            new RiverWidthProfile(widths), bankSoftness, enabled);
        river.Validate();
        return river;
    }

    public void Validate()
    {
        if (Points.Length < 2) throw new ArgumentException("A river requires at least two points.");
        foreach (var point in Points) point.ValidateFinite();
        ValidateWidth(Width, nameof(Width));
        ResolvedWidthProfile.Validate(Points.Length);
        RequireRange(BankSoftness, RiverLimits.MinimumBankSoftness,
            RiverLimits.MaximumBankSoftness, nameof(BankSoftness));
        if (GeometryVersion != 1)
            throw new InvalidDataException($"Unsupported river geometry version {GeometryVersion}.");
    }

    public ImmutableArray<RiverGeometrySegment> GeometrySegments()
    {
        Validate();
        var widths = ResolvedWidthProfile.Widths;
        var segments = ImmutableArray.CreateBuilder<RiverGeometrySegment>(Points.Length - 1);
        for (var index = 1; index < Points.Length; index++)
            segments.Add(new RiverGeometrySegment(
                Points[index - 1], Points[index], widths[index - 1], widths[index], BankSoftness));
        return segments.MoveToImmutable();
    }

    public double SubtractionAt(MapPoint point)
    {
        Validate();
        point.ValidateFinite();
        if (!Enabled) return 0;
        var subtraction = 0d;
        foreach (var segment in GeometrySegments())
        {
            var projection = MapGeometry.ProjectPointOnSegment(point, segment.Start, segment.End);
            var width = segment.StartWidth + ((segment.EndWidth - segment.StartWidth) * projection.Amount);
            var radius = width * 0.5;
            var distance = point.DistanceTo(projection.Point);
            double segmentCoverage;
            if (distance <= radius)
                segmentCoverage = 1;
            else if (segment.BankSoftness == 0)
                segmentCoverage = 0;
            else
            {
                var outerRadius = radius * (1 + segment.BankSoftness);
                segmentCoverage = Math.Clamp((outerRadius - distance) / (outerRadius - radius), 0, 1);
            }
            subtraction = Math.Max(subtraction, segmentCoverage);
        }
        return subtraction;
    }

    public MapBounds Bounds(double effectReach)
    {
        Validate();
        if (!double.IsFinite(effectReach) || effectReach < 0)
            throw new ArgumentOutOfRangeException(nameof(effectReach));
        MapBounds? bounds = null;
        foreach (var segment in GeometrySegments())
        {
            var radius = Math.Max(segment.StartWidth, segment.EndWidth) * 0.5 *
                         (1 + segment.BankSoftness) + effectReach;
            var segmentBounds = MapBounds.AroundSegment(segment.Start, segment.End, radius);
            bounds = bounds is null ? segmentBounds : bounds.Value.Union(segmentBounds);
        }
        return bounds!.Value;
    }

    internal static void ValidateWidth(double width, string parameterName) => RequireRange(
        width, RiverLimits.MinimumWidth, RiverLimits.MaximumWidth, parameterName);

    private static void RequireRange(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"Value must be finite and between {minimum} and {maximum}.");
    }
}

public sealed record InitialGridGeometry(int Columns, int Rows, double CellWidth, double CellHeight)
{
    public void Validate(double mapWidth, double mapHeight)
    {
        if (Columns <= 0 || Rows <= 0 || !double.IsFinite(CellWidth) || !double.IsFinite(CellHeight) ||
            CellWidth <= 0 || CellHeight <= 0 ||
            Math.Abs(CellWidth * Columns - mapWidth) > 1e-9 ||
            Math.Abs(CellHeight * Rows - mapHeight) > 1e-9)
            throw new InvalidDataException("Initial grid cells must partition the map geometry.");
    }
}

public sealed record MapProject(
    ProjectId ProjectId,
    MapId MapId,
    string Title,
    double Width,
    double Height,
    long Revision,
    ImmutableArray<TerrainLayer> TerrainLayers,
    River? River = null,
    int StorageFormatVersion = 2,
    ImportedSourceReference? ImportedSource = null,
    int EditingPixelWidth = 0,
    int EditingPixelHeight = 0,
    InitialGridGeometry? InitialGrid = null)
{
    public static MapProject CreateNormalizedFromGrid(
        ProjectId projectId, MapId mapId, string title, int columns, int rows,
        int editingLongestEdge, ImmutableArray<TerrainLayer> terrainLayers)
    {
        var geometry = MapUnitPolicy.FromGrid(columns, rows, editingLongestEdge);
        var grid = new InitialGridGeometry(columns, rows,
            geometry.Width / columns, geometry.Height / rows);
        var project = new MapProject(projectId, mapId, title, geometry.Width, geometry.Height,
            0, terrainLayers, null, 3, null,
            geometry.EditingPixelWidth, geometry.EditingPixelHeight, grid);
        project.ValidateConnectedTerrain();
        return project;
    }

    public static MapProject CreateNormalizedFromSource(
        ProjectId projectId, MapId mapId, string title, double sourceWidth, double sourceHeight,
        int editingLongestEdge, ImmutableArray<TerrainLayer> terrainLayers,
        ImportedSourceReference importedSource)
    {
        ArgumentNullException.ThrowIfNull(importedSource);
        var geometry = MapUnitPolicy.FromSource(sourceWidth, sourceHeight, editingLongestEdge);
        var source = importedSource with
        {
            SourceSceneWidth = sourceWidth,
            SourceSceneHeight = sourceHeight,
            SourceToMapScale = geometry.SourceToMapScale
        };
        var project = new MapProject(projectId, mapId, title, geometry.Width, geometry.Height,
            0, terrainLayers, null, 3, source,
            geometry.EditingPixelWidth, geometry.EditingPixelHeight);
        project.ValidateConnectedTerrain();
        return project;
    }

    public TerrainLayer RequireLayer(LayerId id) =>
        TerrainLayers.FirstOrDefault(layer => layer.Id == id)
        ?? throw new KeyNotFoundException($"Terrain layer {id.Value} does not exist.");

    public int RequireLayerIndex(LayerId id)
    {
        for (var index = 0; index < TerrainLayers.Length; index++)
            if (TerrainLayers[index].Id == id) return index;
        throw new KeyNotFoundException($"Terrain layer {id.Value} does not exist.");
    }

    public TerrainLayer RequireRole(TerrainRole role)
    {
        if (!Enum.IsDefined(role))
            throw new InvalidDataException($"Unknown terrain role value {(int)role}.");
        return TerrainLayers.SingleOrDefault(layer => layer.Role == role)
            ?? throw new InvalidDataException($"The project does not contain exactly one {role} terrain role.");
    }

    public bool IsTerrainEffectivelyVisible(TerrainRole role)
    {
        var layer = RequireRole(role);
        if (!layer.Visible) return false;
        var anySolo = TerrainLayers.Any(candidate => candidate.Solo);
        return !anySolo || layer.Solo;
    }

    public double EvaluateForegroundCoverage(MapPoint point)
    {
        point.ValidateFinite();
        var coverage = 0d;
        foreach (var stroke in RequireRole(TerrainRole.Foreground).ResolvedLandStrokes)
            coverage = CoverageMath.Apply(coverage, stroke.CoverageAt(point), stroke.Operation);
        if (River is { Enabled: true } river)
            coverage = CoverageMath.Apply(coverage, river.SubtractionAt(point), LandOperation.Subtract);
        return coverage;
    }

    public void ValidateConnectedTerrain()
    {
        if (StorageFormatVersion is not (2 or 3))
            throw new InvalidDataException($"Unsupported connected project format {StorageFormatVersion}.");
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width <= 0 || Height <= 0)
            throw new InvalidDataException("Map dimensions must be finite and positive.");
        if (StorageFormatVersion == 3)
        {
            if (Math.Max(Width, Height) != MapUnitPolicy.LongestEdge ||
                !MapUnitPolicy.EditingLongestEdges.Contains(Math.Max(EditingPixelWidth, EditingPixelHeight)))
                throw new InvalidDataException("Normalized projects require a 1,000-unit long edge and a supported editing preset.");
            var expected = MapUnitPolicy.FromSource(Width, Height,
                Math.Max(EditingPixelWidth, EditingPixelHeight));
            if (EditingPixelWidth != expected.EditingPixelWidth ||
                EditingPixelHeight != expected.EditingPixelHeight)
                throw new InvalidDataException("Editing pixels must follow the map aspect and midpoint-to-even rounding.");
            InitialGrid?.Validate(Width, Height);
            if (ImportedSource is { } source &&
                (source.SourceSceneWidth <= 0 || source.SourceSceneHeight <= 0 ||
                 source.SourceToMapScale <= 0 ||
                 Math.Abs(source.SourceSceneWidth * source.SourceToMapScale - Width) > 1e-9 ||
                 Math.Abs(source.SourceSceneHeight * source.SourceToMapScale - Height) > 1e-9))
                throw new InvalidDataException("Imported source geometry disagrees with normalized map bounds.");
        }
        if (TerrainLayers.Length != 2 ||
            TerrainLayers[0].Role != TerrainRole.Background ||
            TerrainLayers[1].Role != TerrainRole.Foreground)
            throw new InvalidDataException("Connected projects require Background below Foreground.");
        if (TerrainLayers.Select(layer => layer.Role).Distinct().Count() != 2)
            throw new InvalidDataException("Connected projects require one identity for each terrain role.");
        if (TerrainLayers.Select(layer => layer.Id).Distinct().Count() != 2)
            throw new InvalidDataException("Connected terrain roles require distinct stable layer identities.");
        foreach (var layer in TerrainLayers) layer.Validate();
        if (TerrainLayers[0].Strokes.Any(stroke => stroke.AddsCoverage || stroke.Kind == TerrainStrokeKind.Coverage))
            throw new InvalidDataException("Only Foreground may own terrain coverage.");
        if (River is not null)
        {
            River.Validate();
            if (River.TargetLayerId != RequireRole(TerrainRole.Foreground).Id)
                throw new InvalidDataException("The river modifier must target Foreground coverage.");
        }
        ImportedSource?.Validate();
    }
}
