using System.Diagnostics;
using Godot;

namespace Mapwright.Rendering;

public sealed record TerrainProbeResult(
    bool Passed,
    string Device,
    double CoverageMilliseconds,
    double CoastMilliseconds,
    string? OutputPath,
    string Detail);

public static class GpuTerrainProbe
{
    private const int Width = 512;
    private const int Height = 512;

    public static TerrainProbeResult Run(string outputPath)
    {
        var rd = RenderingServer.CreateLocalRenderingDevice();
        if (rd is null)
            return new TerrainProbeResult(false, "unavailable", 0, 0, null, "Local RenderingDevice unavailable.");

        Rid coverageShader = default;
        Rid coastShader = default;
        Rid coverageBuffer = default;
        Rid colorBuffer = default;
        Rid coverageSet = default;
        Rid coastSet = default;
        Rid coveragePipeline = default;
        Rid coastPipeline = default;
        try
        {
            coverageShader = LoadShader(rd, "res://Shaders/terrain_coverage.glsl");
            coastShader = LoadShader(rd, "res://Shaders/terrain_coast.glsl");
            if (!coverageShader.IsValid || !coastShader.IsValid)
                return new TerrainProbeResult(false, rd.GetDeviceName(), 0, 0, null, "Terrain shaders failed to compile.");

            var coverageBytes = new byte[Width * Height * sizeof(float)];
            var colorBytes = new byte[Width * Height * 4 * sizeof(float)];
            coverageBuffer = rd.StorageBufferCreate((uint)coverageBytes.Length, coverageBytes);
            colorBuffer = rd.StorageBufferCreate((uint)colorBytes.Length, colorBytes);

            using var coverageUniform = StorageUniform(0, coverageBuffer);
            coverageSet = rd.UniformSetCreate(new Godot.Collections.Array<RDUniform> { coverageUniform }, coverageShader, 0);
            coveragePipeline = rd.ComputePipelineCreate(coverageShader);

            using var coastCoverageUniform = StorageUniform(0, coverageBuffer);
            using var coastColorUniform = StorageUniform(1, colorBuffer);
            coastSet = rd.UniformSetCreate(
                new Godot.Collections.Array<RDUniform> { coastCoverageUniform, coastColorUniform }, coastShader, 0);
            coastPipeline = rd.ComputePipelineCreate(coastShader);

            var stopwatch = Stopwatch.StartNew();
            Dispatch(rd, coveragePipeline, coverageSet);
            var coverageMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart();
            Dispatch(rd, coastPipeline, coastSet);
            var coastMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            var raw = rd.BufferGetData(colorBuffer);
            var rgba = new byte[Width * Height * 4];
            for (var i = 0; i < Width * Height * 4; i++)
            {
                var value = BitConverter.ToSingle(raw, i * sizeof(float));
                rgba[i] = (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
            }

            var image = Image.CreateFromData(Width, Height, false, Image.Format.Rgba8, rgba);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var saveError = image.SavePng(outputPath);
            image.Dispose();
            var passed = saveError == Error.Ok && File.Exists(outputPath) && new FileInfo(outputPath).Length > 1024;
            return new TerrainProbeResult(passed, rd.GetDeviceName(), coverageMilliseconds, coastMilliseconds,
                passed ? outputPath : null,
                $"coverage={coverageMilliseconds:0.0}ms, coast={coastMilliseconds:0.0}ms, save={saveError}");
        }
        catch (Exception exception)
        {
            return new TerrainProbeResult(false, rd.GetDeviceName(), 0, 0, null, exception.ToString());
        }
        finally
        {
            Free(rd, coastPipeline, coveragePipeline, coastSet, coverageSet, colorBuffer, coverageBuffer, coastShader, coverageShader);
            rd.Free();
        }
    }

    private static RDUniform StorageUniform(int binding, Rid buffer)
    {
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = binding
        };
        uniform.AddId(buffer);
        return uniform;
    }

    private static Rid LoadShader(RenderingDevice rd, string resourcePath)
    {
        var resource = ResourceLoader.Load<RDShaderFile>(resourcePath);
        if (resource is null) return default;
        return rd.ShaderCreateFromSpirV(resource.GetSpirV());
    }

    private static void Dispatch(RenderingDevice rd, Rid pipeline, Rid uniformSet)
    {
        var list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipeline);
        rd.ComputeListBindUniformSet(list, uniformSet, 0);
        rd.ComputeListDispatch(list, Width / 8, Height / 8, 1);
        rd.ComputeListEnd();
        rd.Submit();
        rd.Sync();
    }

    private static void Free(RenderingDevice rd, params Rid[] resources)
    {
        foreach (var resource in resources)
            if (resource.IsValid) rd.FreeRid(resource);
    }
}
