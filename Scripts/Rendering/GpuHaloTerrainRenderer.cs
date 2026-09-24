using Godot;

namespace Mapwright.Rendering;

public sealed record TerrainRenderResult(byte[] Rgba, float[] SignedDistance);

public sealed class GpuHaloTerrainRenderer : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly Rid _shader;
    private readonly Rid _coverageBuffer;
    private readonly Rid _outputBuffer;
    private readonly Rid _distanceBuffer;
    private readonly Rid _uniformSet;
    private readonly Rid _pipeline;

    public GpuHaloTerrainRenderer(int maximumInputPixels, int maximumOutputPixels)
    {
        _rd = RenderingServer.CreateLocalRenderingDevice()
            ?? throw new NotSupportedException("Local RenderingDevice unavailable.");
        var shaderFile = ResourceLoader.Load<RDShaderFile>("res://Shaders/terrain_halo.glsl")
            ?? throw new InvalidOperationException("Halo terrain shader did not load.");
        _shader = _rd.ShaderCreateFromSpirV(shaderFile.GetSpirV());
        _coverageBuffer = _rd.StorageBufferCreate((uint)(maximumInputPixels * sizeof(float)));
        _outputBuffer = _rd.StorageBufferCreate((uint)(maximumOutputPixels * 4));
        _distanceBuffer = _rd.StorageBufferCreate((uint)(maximumOutputPixels * sizeof(float)));
        using var coverageUniform = StorageUniform(0, _coverageBuffer);
        using var outputUniform = StorageUniform(1, _outputBuffer);
        using var distanceUniform = StorageUniform(2, _distanceBuffer);
        _uniformSet = _rd.UniformSetCreate(
            new Godot.Collections.Array<RDUniform> { coverageUniform, outputUniform, distanceUniform }, _shader, 0);
        _pipeline = _rd.ComputePipelineCreate(_shader);
    }

    public TerrainRenderResult Render(float[] inputCoverage, int inputWidth, int inputHeight, int interiorX, int interiorY,
        int outputWidth, int outputHeight, int globalOriginX, int globalOriginY, int documentWidth, int documentHeight)
    {
        var coverageBytes = new byte[inputCoverage.Length * sizeof(float)];
        Buffer.BlockCopy(inputCoverage, 0, coverageBytes, 0, coverageBytes.Length);
        _rd.BufferUpdate(_coverageBuffer, 0, (uint)coverageBytes.Length, coverageBytes);

        Span<byte> push = stackalloc byte[40];
        WriteInt(push, 0, inputWidth);
        WriteInt(push, 4, inputHeight);
        WriteInt(push, 8, interiorX);
        WriteInt(push, 12, interiorY);
        WriteInt(push, 16, outputWidth);
        WriteInt(push, 20, outputHeight);
        WriteInt(push, 24, globalOriginX);
        WriteInt(push, 28, globalOriginY);
        WriteInt(push, 32, documentWidth);
        WriteInt(push, 36, documentHeight);

        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _rd.ComputeListDispatch(list, (uint)((outputWidth + 7) / 8), (uint)((outputHeight + 7) / 8), 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();
        var rgba = _rd.BufferGetData(_outputBuffer, 0, (uint)(outputWidth * outputHeight * 4));
        var distanceBytes = _rd.BufferGetData(_distanceBuffer, 0, (uint)(outputWidth * outputHeight * sizeof(float)));
        var distances = new float[outputWidth * outputHeight];
        Buffer.BlockCopy(distanceBytes, 0, distances, 0, distanceBytes.Length);
        return new TerrainRenderResult(rgba, distances);
    }

    private static RDUniform StorageUniform(int binding, Rid buffer)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = binding };
        uniform.AddId(buffer);
        return uniform;
    }

    private static void WriteInt(Span<byte> destination, int offset, int value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    public void Dispose()
    {
        foreach (var resource in new[] { _pipeline, _uniformSet, _distanceBuffer, _outputBuffer, _coverageBuffer, _shader })
            if (resource.IsValid) _rd.FreeRid(resource);
        _rd.Free();
    }
}
