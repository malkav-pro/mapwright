using System.Collections.Immutable;

namespace Mapwright.Domain;

public readonly record struct NormalizedMapGeometry(
    double Width, double Height, double SourceToMapScale,
    int EditingPixelWidth, int EditingPixelHeight);

public static class MapUnitPolicy
{
    public const double LongestEdge = 1000;
    public static ImmutableArray<int> EditingLongestEdges { get; } = [1024, 2048, 3072, 4096];
    public static ImmutableArray<int> ExportLongestEdges { get; } = [1024, 2048, 3072, 4096, 8192, 16384];

    public static int ChooseImportEditingLongestEdge(double sourceWidth, double sourceHeight)
    {
        RequireSourceDimensions(sourceWidth, sourceHeight);
        var longest = Math.Max(sourceWidth, sourceHeight);
        // Midpoint ties choose the larger preset; dimensions above 4K remain capped at 4K.
        return EditingLongestEdges.MinBy(edge => (Math.Abs(edge - longest), -edge));
    }

    public static NormalizedMapGeometry FromSource(
        double sourceWidth, double sourceHeight, int editingLongestEdge)
    {
        RequireSourceDimensions(sourceWidth, sourceHeight);
        if (!EditingLongestEdges.Contains(editingLongestEdge))
            throw new ArgumentOutOfRangeException(nameof(editingLongestEdge));
        var sourceLongest = Math.Max(sourceWidth, sourceHeight);
        var scale = LongestEdge / sourceLongest;
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source-to-map scale must be finite and positive.");
        // Assign the long edge literally; never obtain it by multiplying two doubles.
        var width = sourceWidth >= sourceHeight ? LongestEdge : sourceWidth * scale;
        var height = sourceHeight >= sourceWidth ? LongestEdge : sourceHeight * scale;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Normalized map dimensions must be finite and positive.");
        var pixelWidth = sourceWidth >= sourceHeight ? editingLongestEdge : RoundShortEdge(width, editingLongestEdge);
        var pixelHeight = sourceHeight >= sourceWidth ? editingLongestEdge : RoundShortEdge(height, editingLongestEdge);
        return new NormalizedMapGeometry(width, height, scale, pixelWidth, pixelHeight);
    }

    public static NormalizedMapGeometry FromGrid(int columns, int rows, int editingLongestEdge)
    {
        if (columns <= 0 || rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(columns), "Initial grid counts must be positive.");
        return FromSource(columns, rows, editingLongestEdge);
    }

    private static int RoundShortEdge(double mapLength, int longestPixels)
    {
        var value = mapLength / LongestEdge * longestPixels;
        if (!double.IsFinite(value) || value < 0.5 || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(mapLength), "The shorter editing edge cannot be sampled safely.");
        // One midpoint-to-even rounding fixes the persisted pixel dimensions.
        return checked((int)Math.Round(value, MidpointRounding.ToEven));
    }

    private static void RequireSourceDimensions(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Source dimensions must be finite and positive.");
    }
}

public readonly record struct MapPoint(double X, double Y)
{
    public void ValidateFinite()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y))
            throw new ArgumentOutOfRangeException(nameof(MapPoint), "Document coordinates must be finite.");
    }

    public double DistanceTo(MapPoint other)
    {
        ValidateFinite();
        other.ValidateFinite();
        var x = other.X - X;
        var y = other.Y - Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    public MapPoint Subtract(MapPoint other) => new(X - other.X, Y - other.Y);
}

public static class MapGeometry
{
    public static MapPoint ClosestPointOnSegment(MapPoint point, MapPoint start, MapPoint end)
        => ProjectPointOnSegment(point, start, end).Point;

    public static SegmentProjection ProjectPointOnSegment(MapPoint point, MapPoint start, MapPoint end)
    {
        point.ValidateFinite();
        start.ValidateFinite();
        end.ValidateFinite();
        var x = end.X - start.X;
        var y = end.Y - start.Y;
        var lengthSquared = (x * x) + (y * y);
        if (lengthSquared == 0) return new SegmentProjection(start, 0);
        var amount = Math.Clamp(
            (((point.X - start.X) * x) + ((point.Y - start.Y) * y)) / lengthSquared,
            0,
            1);
        return new SegmentProjection(
            new MapPoint(start.X + (x * amount), start.Y + (y * amount)), amount);
    }
}

public readonly record struct SegmentProjection(MapPoint Point, double Amount);

public readonly record struct DocumentRasterTransform(
    double DocumentWidth,
    double DocumentHeight,
    int OutputWidth,
    int OutputHeight)
{
    public void Validate()
    {
        if (!double.IsFinite(DocumentWidth) || !double.IsFinite(DocumentHeight) ||
            DocumentWidth <= 0 || DocumentHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(DocumentWidth),
                "Document dimensions must be finite and positive.");
        if (OutputWidth <= 0 || OutputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(OutputWidth),
                "Output dimensions must be positive.");
    }

    public MapPoint OutputPixelCenterToDocument(int outputX, int outputY)
    {
        Validate();
        if (outputX < 0 || outputX >= OutputWidth) throw new ArgumentOutOfRangeException(nameof(outputX));
        if (outputY < 0 || outputY >= OutputHeight) throw new ArgumentOutOfRangeException(nameof(outputY));
        return new MapPoint(
            (outputX + 0.5) * DocumentWidth / OutputWidth,
            (outputY + 0.5) * DocumentHeight / OutputHeight);
    }

    public MapPoint DocumentToOutput(MapPoint documentPoint)
    {
        Validate();
        documentPoint.ValidateFinite();
        return new MapPoint(
            documentPoint.X * OutputWidth / DocumentWidth,
            documentPoint.Y * OutputHeight / DocumentHeight);
    }

    public int RoundDocumentLengthToOutput(double documentLength, bool horizontal)
    {
        Validate();
        if (!double.IsFinite(documentLength) || documentLength < 0)
            throw new ArgumentOutOfRangeException(nameof(documentLength));
        var scale = horizontal
            ? OutputWidth / DocumentWidth
            : OutputHeight / DocumentHeight;
        return checked((int)Math.Round(documentLength * scale, MidpointRounding.ToEven));
    }

    public double SampleCoverage(Func<MapPoint, double> evaluator, int outputX, int outputY)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        var coverage = evaluator(OutputPixelCenterToDocument(outputX, outputY));
        CoverageMath.RequireUnit(coverage, nameof(evaluator));
        return coverage;
    }

    public static bool IsLand(double coverage)
    {
        CoverageMath.RequireUnit(coverage, nameof(coverage));
        return coverage >= 0.5;
    }
}

public readonly record struct MapBounds(double Left, double Top, double Right, double Bottom)
{
    public static MapBounds AroundSegment(MapPoint start, MapPoint end, double radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        return new MapBounds(
            Math.Min(start.X, end.X) - radius,
            Math.Min(start.Y, end.Y) - radius,
            Math.Max(start.X, end.X) + radius,
            Math.Max(start.Y, end.Y) + radius);
    }

    public MapBounds Expand(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return new MapBounds(Left - amount, Top - amount, Right + amount, Bottom + amount);
    }

    public MapBounds Union(MapBounds other) => new(
        Math.Min(Left, other.Left),
        Math.Min(Top, other.Top),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));
}
