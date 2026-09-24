using System.Text.Json;
using Godot;

namespace Mapwright.Rendering;

public sealed record DeviceLossProbeResult(
    bool SyncReturned,
    bool InProcessRecoveryPassed,
    bool RestartRequired,
    string Stage,
    string Detail);

public static class DeviceLossProbe
{
    public static DeviceLossProbeResult Run(string resultPath)
    {
        RenderingDevice? rd = null;
        var shader = new Rid();
        var buffer = new Rid();
        var uniformSet = new Rid();
        var pipeline = new Rid();
        WriteStage(resultPath, "initializing", "Creating isolated local RenderingDevice.");
        try
        {
            rd = RenderingServer.CreateLocalRenderingDevice()
                ?? throw new NotSupportedException("Local RenderingDevice unavailable.");
            var shaderFile = ResourceLoader.Load<RDShaderFile>("res://Shaders/tdr_hang.glsl")
                ?? throw new InvalidOperationException("TDR shader did not load.");
            var spirV = shaderFile.GetSpirV();
            var compileError = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
            if (!string.IsNullOrWhiteSpace(compileError)) throw new InvalidOperationException(compileError);
            shader = rd.ShaderCreateFromSpirV(spirV);
            buffer = rd.StorageBufferCreate(sizeof(uint), BitConverter.GetBytes(0u));
            using var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.StorageBuffer,
                Binding = 0
            };
            uniform.AddId(buffer);
            uniformSet = rd.UniformSetCreate(new Godot.Collections.Array<RDUniform> { uniform }, shader, 0);
            pipeline = rd.ComputePipelineCreate(shader);

            WriteStage(resultPath, "submitting_hung_shader",
                "The next dispatch is intentionally non-terminating and should trigger Windows TDR.");
            var list = rd.ComputeListBegin();
            rd.ComputeListBindComputePipeline(list, pipeline);
            rd.ComputeListBindUniformSet(list, uniformSet, 0);
            rd.ComputeListDispatch(list, 1, 1, 1);
            rd.ComputeListEnd();
            rd.Submit();
            WriteStage(resultPath, "waiting_for_tdr", "Dispatch submitted; waiting for device reset or failure.");
            rd.Sync();

            WriteStage(resultPath, "sync_returned", "RenderingDevice.Sync returned after the hung dispatch.");
            var recovery = GpuComputeProbe.Run();
            var result = new DeviceLossProbeResult(true, recovery.Passed, !recovery.Passed, "completed",
                $"Managed control returned after TDR. In-process fresh-device compute passed={recovery.Passed}. " +
                $"The child may still terminate during device cleanup. {recovery.Detail}");
            WriteResult(resultPath, result);
            return result;
        }
        catch (Exception exception)
        {
            var recovery = GpuComputeProbe.Run();
            var result = new DeviceLossProbeResult(false, recovery.Passed, !recovery.Passed, "caught_exception",
                $"{exception.GetType().Name}: {exception.Message}; fresh-device compute passed={recovery.Passed}. {recovery.Detail}");
            WriteResult(resultPath, result);
            return result;
        }
        finally
        {
            if (rd is not null)
            {
                foreach (var resource in new[] { pipeline, uniformSet, buffer, shader })
                    if (resource.IsValid) rd.FreeRid(resource);
                rd.Free();
            }
        }
    }

    private static void WriteStage(string path, string stage, string detail) =>
        WriteJson(path, new { syncReturned = false, inProcessRecoveryPassed = false, restartRequired = false, stage, detail });

    private static void WriteResult(string path, DeviceLossProbeResult result) => WriteJson(path, result);

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
