using System.Diagnostics;
using Godot;

namespace Mapwright.Rendering;

public sealed class GlobalGpuBrushSurface : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly Action<Rid> _ready;
    private readonly Action<long> _submitted;
    private RenderingDevice? _rd;
    private Rid _coverageTexture;
    private Rid _colorTexture;
    private Rid _displayTexture;
    private Rid _shader;
    private Rid _uniformSet;
    private Rid _pipeline;
    private bool _disposed;

    public GlobalGpuBrushSurface(int width, int height, Action<Rid> ready, Action<long> submitted)
    {
        _width = width;
        _height = height;
        _ready = ready;
        _submitted = submitted;
        RenderingServer.CallOnRenderThread(Callable.From(InitializeOnRenderThread));
    }

    public void PaintDab(Vector2 center, float radius, float opacity, bool add, long inputTimestamp)
    {
        if (_disposed) return;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
            PaintOnRenderThread(center, radius, opacity, add, inputTimestamp)));
    }

    private void InitializeOnRenderThread()
    {
        if (_disposed) return;
        _rd = RenderingServer.GetRenderingDevice()
            ?? throw new NotSupportedException("Global RenderingDevice unavailable.");
        var coverageFormat = CreateFormat(RenderingDevice.DataFormat.R32Sfloat,
            RenderingDevice.TextureUsageBits.StorageBit);
        var colorFormat = CreateFormat(RenderingDevice.DataFormat.R8G8B8A8Unorm,
            RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit);
        using var view = new RDTextureView();
        _coverageTexture = _rd.TextureCreate(coverageFormat, view,
            new Godot.Collections.Array<byte[]> { new byte[_width * _height * sizeof(float)] });
        _colorTexture = _rd.TextureCreate(colorFormat, view,
            new Godot.Collections.Array<byte[]> { new byte[_width * _height * 4] });
        coverageFormat.Dispose();
        colorFormat.Dispose();

        var shaderFile = ResourceLoader.Load<RDShaderFile>("res://Shaders/interactive_brush.glsl")
            ?? throw new InvalidOperationException("Interactive brush shader did not load.");
        var spirV = shaderFile.GetSpirV();
        var compileError = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrWhiteSpace(compileError)) throw new InvalidOperationException(compileError);
        _shader = _rd.ShaderCreateFromSpirV(spirV);
        using var coverageUniform = ImageUniform(0, _coverageTexture);
        using var colorUniform = ImageUniform(1, _colorTexture);
        _uniformSet = _rd.UniformSetCreate(
            new Godot.Collections.Array<RDUniform> { coverageUniform, colorUniform }, _shader, 0);
        _pipeline = _rd.ComputePipelineCreate(_shader);
        _displayTexture = RenderingServer.TextureRdCreate(_colorTexture);
        Callable.From(() => _ready(_displayTexture)).CallDeferred();
    }

    private void PaintOnRenderThread(Vector2 center, float radius, float opacity, bool add, long inputTimestamp)
    {
        if (_disposed || _rd is null || !_pipeline.IsValid) return;
        Span<byte> push = stackalloc byte[32];
        WriteFloat(push, 0, center.X);
        WriteFloat(push, 4, center.Y);
        WriteFloat(push, 8, radius);
        WriteFloat(push, 12, opacity);
        WriteInt(push, 16, _width);
        WriteInt(push, 20, _height);
        WriteInt(push, 24, add ? 1 : 0);
        WriteInt(push, 28, 0);
        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _rd.ComputeListDispatch(list, (uint)((_width + 7) / 8), (uint)((_height + 7) / 8), 1);
        _rd.ComputeListEnd();
        Callable.From(() => _submitted(inputTimestamp)).CallDeferred();
    }

    private RDTextureFormat CreateFormat(RenderingDevice.DataFormat format,
        RenderingDevice.TextureUsageBits usage)
    {
        return new RDTextureFormat
        {
            Format = format,
            Width = (uint)_width,
            Height = (uint)_height,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = 1,
            TextureType = RenderingDevice.TextureType.Type2D,
            Samples = RenderingDevice.TextureSamples.Samples1,
            UsageBits = usage
        };
    }

    private static RDUniform ImageUniform(int binding, Rid texture)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding };
        uniform.AddId(texture);
        return uniform;
    }

    private static void WriteFloat(Span<byte> destination, int offset, float value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    private static void WriteInt(Span<byte> destination, int offset, int value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RenderingServer.CallOnRenderThread(Callable.From(FreeOnRenderThread));
    }

    private void FreeOnRenderThread()
    {
        if (_rd is not null)
        {
            foreach (var resource in new[] { _pipeline, _uniformSet, _colorTexture, _coverageTexture, _shader })
                if (resource.IsValid) _rd.FreeRid(resource);
        }
        if (_displayTexture.IsValid) RenderingServer.FreeRid(_displayTexture);
    }
}
