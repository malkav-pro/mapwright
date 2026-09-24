using System.Diagnostics;
using Godot;

namespace Mapwright.Rendering;

public sealed record GpuSeamResult(
    bool Passed,
    int FixtureSize,
    int TileSize,
    int Halo,
    int TilesRendered,
    int MaximumChannelDifference,
    long DifferentChannels,
    long BoundaryDifferentChannels,
    float MaximumDiscreteOracleError,
    float MaximumTiledDistanceDifference,
    double Milliseconds,
    string? WholeOutputPath,
    string? TiledOutputPath,
    string Detail);

public static class GpuSeamProbe
{
    private const int Halo = 22;

    public static GpuSeamResult Run(string? outputDirectory = null, int fixtureSize = 512, int tileSize = 128)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var coverage = BuildAccumulatedCoverage(fixtureSize, fixtureSize);
            TerrainRenderResult exactReference;
            TerrainRenderResult whole;
            var maximumInputPixels = fixtureSize * fixtureSize;
            using (var renderer = new GpuHaloTerrainRenderer(maximumInputPixels, maximumInputPixels))
                exactReference = renderer.Render(coverage, fixtureSize, fixtureSize, 0, 0,
                    fixtureSize, fixtureSize, 0, 0, fixtureSize, fixtureSize);
            using (var renderer = new GpuJfaTerrainRenderer(maximumInputPixels, maximumInputPixels))
                whole = renderer.Render(coverage, fixtureSize, fixtureSize, 0, 0,
                    fixtureSize, fixtureSize, 0, 0, fixtureSize, fixtureSize);

            var tiled = new byte[whole.Rgba.Length];
            var tiledDistance = new float[whole.SignedDistance.Length];
            var tilesRendered = 0;
            using (var renderer = new GpuJfaTerrainRenderer(
                       (tileSize + Halo * 2) * (tileSize + Halo * 2), tileSize * tileSize))
            {
                for (var originY = 0; originY < fixtureSize; originY += tileSize)
                for (var originX = 0; originX < fixtureSize; originX += tileSize)
                {
                    var outputWidth = Math.Min(tileSize, fixtureSize - originX);
                    var outputHeight = Math.Min(tileSize, fixtureSize - originY);
                    var inputLeft = Math.Max(0, originX - Halo);
                    var inputTop = Math.Max(0, originY - Halo);
                    var inputRight = Math.Min(fixtureSize, originX + outputWidth + Halo);
                    var inputBottom = Math.Min(fixtureSize, originY + outputHeight + Halo);
                    var inputWidth = inputRight - inputLeft;
                    var inputHeight = inputBottom - inputTop;
                    var tileCoverage = Extract(coverage, fixtureSize, inputLeft, inputTop, inputWidth, inputHeight);
                    var tile = renderer.Render(tileCoverage, inputWidth, inputHeight,
                        originX - inputLeft, originY - inputTop, outputWidth, outputHeight,
                        originX, originY, fixtureSize, fixtureSize);
                    for (var row = 0; row < outputHeight; row++)
                        tile.Rgba.AsSpan(row * outputWidth * 4, outputWidth * 4)
                            .CopyTo(tiled.AsSpan(((originY + row) * fixtureSize + originX) * 4));
                    for (var row = 0; row < outputHeight; row++)
                        tile.SignedDistance.AsSpan(row * outputWidth, outputWidth)
                            .CopyTo(tiledDistance.AsSpan((originY + row) * fixtureSize + originX, outputWidth));
                    tilesRendered++;
                }
            }

            var maximumDifference = 0;
            long differentChannels = 0;
            long boundaryDifferentChannels = 0;
            for (var index = 0; index < whole.Rgba.Length; index++)
            {
                var difference = Math.Abs(whole.Rgba[index] - tiled[index]);
                maximumDifference = Math.Max(maximumDifference, difference);
                if (difference == 0) continue;
                differentChannels++;
                var pixel = index / 4;
                var x = pixel % fixtureSize;
                var y = pixel / fixtureSize;
                if (x % tileSize <= 1 || x % tileSize >= tileSize - 2 ||
                    y % tileSize <= 1 || y % tileSize >= tileSize - 2)
                    boundaryDifferentChannels++;
            }
            var maximumDistanceError = 0f;
            var maximumTiledDistanceDifference = 0f;
            for (var index = 0; index < whole.SignedDistance.Length; index++)
            {
                maximumDistanceError = Math.Max(maximumDistanceError,
                    Math.Abs(whole.SignedDistance[index] - exactReference.SignedDistance[index]));
                maximumTiledDistanceDifference = Math.Max(maximumTiledDistanceDifference,
                    Math.Abs(whole.SignedDistance[index] - tiledDistance[index]));
            }

            string? wholePath = null;
            string? tiledPath = null;
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                wholePath = Path.Combine(outputDirectory, "halo-terrain-whole.png");
                tiledPath = Path.Combine(outputDirectory, "halo-terrain-tiled.png");
                Save(whole.Rgba, fixtureSize, fixtureSize, wholePath);
                Save(tiled, fixtureSize, fixtureSize, tiledPath);
            }

            stopwatch.Stop();
            return new GpuSeamResult(maximumDifference <= 1 && boundaryDifferentChannels == 0 &&
                maximumDistanceError <= 0.5f && maximumTiledDistanceDifference <= 0.5f,
                fixtureSize, tileSize, Halo, tilesRendered, maximumDifference, differentChannels,
                boundaryDifferentChannels, maximumDistanceError, maximumTiledDistanceDifference,
                stopwatch.Elapsed.TotalMilliseconds, wholePath, tiledPath,
                "Compared whole and halo-tiled JFA coastline renders over persistent CPU coverage accumulated from soft brush dabs and a cross-tile river. Interiors are cropped from 22-pixel halos. Distance agreement is checked against a 45x45 discrete opposite-class pixel-center oracle, not an interpolated 0.5 coverage contour.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new GpuSeamResult(false, fixtureSize, tileSize, Halo, 0, int.MaxValue, 0, 0,
                float.MaxValue, float.MaxValue,
                stopwatch.Elapsed.TotalMilliseconds, null, null, exception.ToString());
        }
    }

    public static float[] BuildAccumulatedCoverage(int width, int height)
        => BuildAccumulatedCoverageRegion(width, height, 0, 0, width, height);

    public static float[] BuildAccumulatedCoverageRegion(int documentWidth, int documentHeight,
        int regionLeft, int regionTop, int regionWidth, int regionHeight)
    {
        var coverage = new float[regionWidth * regionHeight];
        var scale = new Vector2(documentWidth / 512f, documentHeight / 512f);
        var radiusScale = Math.Min(scale.X, scale.Y);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(76, 260) * scale, new Vector2(446, 248) * scale, 112 * radiusScale, 0.74f, true);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(116, 166) * scale, new Vector2(390, 354) * scale, 104 * radiusScale, 0.82f, true);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(210, 72) * scale, new Vector2(290, 452) * scale, 66 * radiusScale, 0.45f, true);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(220, 42) * scale, new Vector2(256, 188) * scale, 10 * radiusScale, 1f, false);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(256, 188) * scale, new Vector2(238, 320) * scale, 13 * radiusScale, 1f, false);
        ApplyStroke(coverage, regionWidth, regionHeight, regionLeft, regionTop,
            new Vector2(238, 320) * scale, new Vector2(184, 488) * scale, 17 * radiusScale, 1f, false);
        return coverage;
    }

    private static void ApplyStroke(float[] coverage, int regionWidth, int regionHeight,
        int regionLeft, int regionTop, Vector2 start, Vector2 end, float radius, float opacity, bool add)
    {
        var distance = start.DistanceTo(end);
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / Math.Max(2f, radius * 0.22f)));
        for (var step = 0; step <= steps; step++)
        {
            var center = start.Lerp(end, step / (float)steps);
            var left = Math.Max(regionLeft, (int)MathF.Floor(center.X - radius));
            var right = Math.Min(regionLeft + regionWidth - 1, (int)MathF.Ceiling(center.X + radius));
            var top = Math.Max(regionTop, (int)MathF.Floor(center.Y - radius));
            var bottom = Math.Min(regionTop + regionHeight - 1, (int)MathF.Ceiling(center.Y + radius));
            for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                var normalized = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(center) / radius;
                if (normalized >= 1f) continue;
                var falloff = 1f - normalized * normalized;
                var dab = opacity * falloff * falloff;
                var index = (y - regionTop) * regionWidth + x - regionLeft;
                coverage[index] = add
                    ? Math.Clamp(coverage[index] + dab * (1f - coverage[index]), 0f, 1f)
                    : Math.Clamp(coverage[index] * (1f - dab), 0f, 1f);
            }
        }
    }

    public static float[] Extract(float[] source, int sourceWidth, int left, int top, int width, int height)
    {
        var result = new float[width * height];
        for (var row = 0; row < height; row++)
            source.AsSpan((top + row) * sourceWidth + left, width).CopyTo(result.AsSpan(row * width, width));
        return result;
    }

    private static void Save(byte[] rgba, int width, int height, string path)
    {
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        var result = image.SavePng(path);
        if (result != Error.Ok) throw new IOException($"Could not save seam fixture: {result}");
    }
}
