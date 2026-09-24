using System.Diagnostics;
using Mapwright.Rendering;

namespace Mapwright.Export;

public sealed record TiledExportResult(
    bool Passed,
    int Width,
    int Height,
    int TileSize,
    int TilesRendered,
    long OutputBytes,
    long PeakWorkingSetBytes,
    long RecordedGpuBufferBytes,
    double Milliseconds,
    PngValidationResult? Validation,
    string? OutputPath,
    string Detail);

public static class TiledExportProbe
{
    public static TiledExportResult Run(
        string destination,
        int width = 16_384,
        int height = 16_384,
        int tileSize = 2_048,
        int? injectFailureAfterTiles = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var tilesRendered = 0;
        var process = Process.GetCurrentProcess();
        var peakWorkingSet = SampleWorkingSet(process);
        var recordedGpuBufferBytes = 0L;
        var destinationDirectory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            Directory.CreateDirectory(destinationDirectory);

            using var renderer = new GpuExportTileRenderer(tileSize);
            recordedGpuBufferBytes = renderer.AllocatedStorageBufferBytes;
            using (var png = new StreamingPngWriter(temporary, width, height))
            {
                for (var bandY = 0; bandY < height; bandY += tileSize)
                {
                    var bandHeight = Math.Min(tileSize, height - bandY);
                    var tiles = new List<byte[]>();
                    var tileWidths = new List<int>();
                    for (var tileX = 0; tileX < width; tileX += tileSize)
                    {
                        var tileWidth = Math.Min(tileSize, width - tileX);
                        tiles.Add(renderer.RenderTile(tileX, bandY, tileWidth, bandHeight, width, height));
                        tileWidths.Add(tileWidth);
                        tilesRendered++;
                        if (injectFailureAfterTiles is int failureTile && tilesRendered >= failureTile)
                            throw new IOException($"Injected export failure after tile {tilesRendered}.");
                    }

                    var row = new byte[width * 4];
                    for (var localY = 0; localY < bandHeight; localY++)
                    {
                        var destinationOffset = 0;
                        for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
                        {
                            var tileWidth = tileWidths[tileIndex];
                            var source = tiles[tileIndex].AsSpan(localY * tileWidth * 4, tileWidth * 4);
                            source.CopyTo(row.AsSpan(destinationOffset));
                            destinationOffset += source.Length;
                        }
                        png.WriteRgbaRow(row);
                    }
                    peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
                }
                png.Complete();
            }

            var validation = PngValidator.ValidateRgba8(temporary, width, height);
            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
            if (!validation.Passed)
                throw new InvalidDataException($"Temporary PNG validation failed: {validation.Detail}");
            File.Move(temporary, destination, overwrite: true);
            var info = new FileInfo(destination);
            stopwatch.Stop();
            var passed = info.Exists && info.Length > 1024 && validation.Passed;
            return new TiledExportResult(passed, width, height, tileSize, tilesRendered, info.Length,
                peakWorkingSet, recordedGpuBufferBytes, stopwatch.Elapsed.TotalMilliseconds,
                validation, destination,
                $"Rendered {tilesRendered} GPU tiles with a maximum in-memory band of {width * tileSize * 4L:N0} bytes.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            if (File.Exists(temporary)) File.Delete(temporary);
            return new TiledExportResult(false, width, height, tileSize, tilesRendered, 0,
                peakWorkingSet, recordedGpuBufferBytes, stopwatch.Elapsed.TotalMilliseconds,
                null, null, exception.ToString());
        }
    }

    private static long SampleWorkingSet(Process process)
    {
        process.Refresh();
        return process.WorkingSet64;
    }
}

public sealed record ExportSafetyProbeResult(bool Passed, double Milliseconds, string Detail);

public static class ExportSafetyProbe
{
    public static ExportSafetyProbeResult Run(string destination)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var sentinel = "existing-export-must-survive"u8.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, sentinel);
            var failed = TiledExportProbe.Run(destination, 128, 128, 64, injectFailureAfterTiles: 1);
            var preserved = File.ReadAllBytes(destination).AsSpan().SequenceEqual(sentinel);
            var orphanPartials = Directory.EnumerateFiles(Path.GetDirectoryName(destination)!,
                $".{Path.GetFileName(destination)}.*.partial").Any();
            stopwatch.Stop();
            return new ExportSafetyProbeResult(!failed.Passed && preserved && !orphanPartials,
                stopwatch.Elapsed.TotalMilliseconds,
                $"Injected failure returned Passed={failed.Passed}; previous destination preserved={preserved}; orphan temporary files={orphanPartials}.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ExportSafetyProbeResult(false, stopwatch.Elapsed.TotalMilliseconds, exception.ToString());
        }
    }
}
