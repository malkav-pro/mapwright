using System.Diagnostics;
using Mapwright.Rendering;

namespace Mapwright.Export;

public sealed record TerrainPipelineExportResult(
    bool Passed,
    int Width,
    int Height,
    int TileSize,
    int Halo,
    int TilesRendered,
    long OutputBytes,
    long PeakWorkingSetBytes,
    long RecordedGpuBufferBytes,
    double Milliseconds,
    PngValidationResult? Validation,
    string? OutputPath,
    string Detail);

public static class TerrainPipelineExportProbe
{
    public static TerrainPipelineExportResult Run(string destination, int size = 2_048,
        int tileSize = 512, int halo = 22)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var peakWorkingSet = SampleWorkingSet(process);
        var recordedGpuBufferBytes = 0L;
        var tilesRendered = 0;
        var directory = Path.GetDirectoryName(destination)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            Directory.CreateDirectory(directory);
            using var renderer = new GpuJfaTerrainRenderer(
                (tileSize + halo * 2) * (tileSize + halo * 2), tileSize * tileSize);
            recordedGpuBufferBytes = renderer.AllocatedStorageBufferBytes;
            using (var png = new StreamingPngWriter(temporary, size, size))
            {
                for (var bandY = 0; bandY < size; bandY += tileSize)
                {
                    var bandHeight = Math.Min(tileSize, size - bandY);
                    var tiles = new List<(byte[] Rgba, int Width)>();
                    for (var originX = 0; originX < size; originX += tileSize)
                    {
                        var outputWidth = Math.Min(tileSize, size - originX);
                        var inputLeft = Math.Max(0, originX - halo);
                        var inputTop = Math.Max(0, bandY - halo);
                        var inputRight = Math.Min(size, originX + outputWidth + halo);
                        var inputBottom = Math.Min(size, bandY + bandHeight + halo);
                        var inputWidth = inputRight - inputLeft;
                        var inputHeight = inputBottom - inputTop;
                        var tileCoverage = GpuSeamProbe.BuildAccumulatedCoverageRegion(size, size,
                            inputLeft, inputTop, inputWidth, inputHeight);
                        var rendered = renderer.Render(tileCoverage, inputWidth, inputHeight,
                            originX - inputLeft, bandY - inputTop, outputWidth, bandHeight,
                            originX, bandY, size, size);
                        tiles.Add((rendered.Rgba, outputWidth));
                        tilesRendered++;
                        peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
                    }

                    var row = new byte[size * 4];
                    for (var localY = 0; localY < bandHeight; localY++)
                    {
                        var destinationOffset = 0;
                        foreach (var tile in tiles)
                        {
                            var rowBytes = tile.Width * 4;
                            tile.Rgba.AsSpan(localY * rowBytes, rowBytes).CopyTo(row.AsSpan(destinationOffset));
                            destinationOffset += rowBytes;
                        }
                        png.WriteRgbaRow(row);
                    }
                }
                png.Complete();
            }

            var validation = PngValidator.ValidateRgba8(temporary, size, size);
            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
            if (!validation.Passed) throw new InvalidDataException(validation.Detail);
            File.Move(temporary, destination, overwrite: true);
            stopwatch.Stop();
            var outputBytes = new FileInfo(destination).Length;
            return new TerrainPipelineExportResult(true, size, size, tileSize, halo, tilesRendered,
                outputBytes, peakWorkingSet, recordedGpuBufferBytes, stopwatch.Elapsed.TotalMilliseconds,
                validation, destination,
                "Exported the same accumulated soft-brush coverage, river, JFA distance field, coastline rings and global texture coordinates used by the seam fixture through halo-cropped tiles. Coverage is generated per halo tile; no full-document float coverage array is allocated.");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            if (File.Exists(temporary)) File.Delete(temporary);
            return new TerrainPipelineExportResult(false, size, size, tileSize, halo, tilesRendered,
                0, peakWorkingSet, recordedGpuBufferBytes, stopwatch.Elapsed.TotalMilliseconds,
                null, null, exception.ToString());
        }
    }

    private static long SampleWorkingSet(Process process)
    {
        process.Refresh();
        return process.WorkingSet64;
    }
}
