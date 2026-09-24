using System.Diagnostics;
using Godot;

namespace Mapwright.Rendering;

public sealed record GpuProbeResult(bool Passed, string Device, double Milliseconds, string Detail);

public static class GpuComputeProbe
{
    public static GpuProbeResult Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var renderingDevice = RenderingServer.CreateLocalRenderingDevice();
        if (renderingDevice is null)
            return new GpuProbeResult(false, "unavailable", 0, "Godot could not create a local RenderingDevice.");

        Rid shader = default;
        Rid buffer = default;
        Rid uniformSet = default;
        Rid pipeline = default;
        try
        {
            var shaderFile = ResourceLoader.Load<RDShaderFile>("res://Shaders/probe_fill.glsl");
            if (shaderFile is null)
                return new GpuProbeResult(false, "unknown", 0, "Compute shader resource did not load.");
            var spirV = shaderFile.GetSpirV();
            shader = renderingDevice.ShaderCreateFromSpirV(spirV);
            if (!shader.IsValid)
                return new GpuProbeResult(false, "unknown", 0, "Compute shader failed to compile.");

            const int valueCount = 4096;
            var input = new byte[valueCount * sizeof(float)];
            buffer = renderingDevice.StorageBufferCreate((uint)input.Length, input);

            using var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.StorageBuffer,
                Binding = 0
            };
            uniform.AddId(buffer);
            var uniforms = new Godot.Collections.Array<RDUniform> { uniform };
            uniformSet = renderingDevice.UniformSetCreate(uniforms, shader, 0);
            pipeline = renderingDevice.ComputePipelineCreate(shader);

            var list = renderingDevice.ComputeListBegin();
            renderingDevice.ComputeListBindComputePipeline(list, pipeline);
            renderingDevice.ComputeListBindUniformSet(list, uniformSet, 0);
            renderingDevice.ComputeListDispatch(list, valueCount / 64, 1, 1);
            renderingDevice.ComputeListEnd();
            renderingDevice.Submit();
            renderingDevice.Sync();

            var output = renderingDevice.BufferGetData(buffer);
            var first = BitConverter.ToSingle(output, 0);
            var last = BitConverter.ToSingle(output, (valueCount - 1) * sizeof(float));
            var expected = (valueCount - 1) * 1.5f + 7f;
            var passed = Math.Abs(first - 7f) < 0.001f && Math.Abs(last - expected) < 0.01f;
            stopwatch.Stop();
            var name = renderingDevice.GetDeviceName();
            return new GpuProbeResult(passed, name, stopwatch.Elapsed.TotalMilliseconds,
                $"first={first:0.###}, last={last:0.###}, expected={expected:0.###}");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new GpuProbeResult(false, "error", stopwatch.Elapsed.TotalMilliseconds, exception.Message);
        }
        finally
        {
            if (pipeline.IsValid) renderingDevice.FreeRid(pipeline);
            if (uniformSet.IsValid) renderingDevice.FreeRid(uniformSet);
            if (buffer.IsValid) renderingDevice.FreeRid(buffer);
            if (shader.IsValid) renderingDevice.FreeRid(shader);
            renderingDevice.Free();
        }
    }
}
