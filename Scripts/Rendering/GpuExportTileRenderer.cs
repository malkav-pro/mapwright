using Godot;

namespace Mapwright.Rendering;

public sealed class GpuExportTileRenderer : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly int _tileSize;
    private readonly Rid _shader;
    private readonly Rid _buffer;
    private readonly Rid _uniformSet;
    private readonly Rid _pipeline;

    public GpuExportTileRenderer(int tileSize)
    {
        _tileSize = tileSize;
        _rd = RenderingServer.CreateLocalRenderingDevice()
            ?? throw new NotSupportedException("Local RenderingDevice unavailable.");
        var shaderFile = ResourceLoader.Load<RDShaderFile>("res://Shaders/export_tile.glsl")
            ?? throw new InvalidOperationException("Export shader did not load.");
        _shader = _rd.ShaderCreateFromSpirV(shaderFile.GetSpirV());
        var bytes = new byte[tileSize * tileSize * 4];
        _buffer = _rd.StorageBufferCreate((uint)bytes.Length, bytes);
        using var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 0
        };
        uniform.AddId(_buffer);
        _uniformSet = _rd.UniformSetCreate(new Godot.Collections.Array<RDUniform> { uniform }, _shader, 0);
        _pipeline = _rd.ComputePipelineCreate(_shader);
    }

    public string DeviceName => _rd.GetDeviceName();
    public long AllocatedStorageBufferBytes => _tileSize * (long)_tileSize * 4;

    public byte[] RenderTile(int originX, int originY, int width, int height, int outputWidth, int outputHeight)
    {
        Span<byte> push = stackalloc byte[24];
        WriteInt(push, 0, originX);
        WriteInt(push, 4, originY);
        WriteInt(push, 8, width);
        WriteInt(push, 12, height);
        WriteInt(push, 16, outputWidth);
        WriteInt(push, 20, outputHeight);

        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _rd.ComputeListDispatch(list, (uint)((width + 7) / 8), (uint)((height + 7) / 8), 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();
        return _rd.BufferGetData(_buffer, 0, (uint)(width * height * 4));
    }

    private static void WriteInt(Span<byte> destination, int offset, int value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    public void Dispose()
    {
        foreach (var resource in new[] { _pipeline, _uniformSet, _buffer, _shader })
            if (resource.IsValid) _rd.FreeRid(resource);
        _rd.Free();
    }
}
