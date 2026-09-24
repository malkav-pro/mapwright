using Godot;

namespace Mapwright.Rendering;

public sealed class GpuJfaTerrainRenderer : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly Rid _seedShader;
    private readonly Rid _jumpShader;
    private readonly Rid _colorShader;
    private readonly Rid _coverageBuffer;
    private readonly Rid _seedA;
    private readonly Rid _seedB;
    private readonly Rid _outputBuffer;
    private readonly Rid _distanceBuffer;
    private readonly Rid _seedSet;
    private readonly Rid _jumpAbSet;
    private readonly Rid _jumpBaSet;
    private readonly Rid _colorASet;
    private readonly Rid _colorBSet;
    private readonly Rid _seedPipeline;
    private readonly Rid _jumpPipeline;
    private readonly Rid _colorPipeline;

    public GpuJfaTerrainRenderer(int maximumInputPixels, int maximumOutputPixels)
    {
        _rd = RenderingServer.CreateLocalRenderingDevice()
            ?? throw new NotSupportedException("Local RenderingDevice unavailable.");
        _seedShader = LoadShader("res://Shaders/terrain_sdf_seed.glsl");
        _jumpShader = LoadShader("res://Shaders/terrain_sdf_jump.glsl");
        _colorShader = LoadShader("res://Shaders/terrain_sdf_color.glsl");
        _coverageBuffer = _rd.StorageBufferCreate((uint)(maximumInputPixels * sizeof(float)));
        _seedA = _rd.StorageBufferCreate((uint)(maximumInputPixels * 4 * sizeof(int)));
        _seedB = _rd.StorageBufferCreate((uint)(maximumInputPixels * 4 * sizeof(int)));
        _outputBuffer = _rd.StorageBufferCreate((uint)(maximumOutputPixels * 4));
        _distanceBuffer = _rd.StorageBufferCreate((uint)(maximumOutputPixels * sizeof(float)));
        _seedSet = CreateSet(_seedShader, (0, _coverageBuffer), (1, _seedA));
        _jumpAbSet = CreateSet(_jumpShader, (0, _seedA), (1, _seedB));
        _jumpBaSet = CreateSet(_jumpShader, (0, _seedB), (1, _seedA));
        _colorASet = CreateSet(_colorShader, (0, _coverageBuffer), (1, _seedA),
            (2, _outputBuffer), (3, _distanceBuffer));
        _colorBSet = CreateSet(_colorShader, (0, _coverageBuffer), (1, _seedB),
            (2, _outputBuffer), (3, _distanceBuffer));
        _seedPipeline = _rd.ComputePipelineCreate(_seedShader);
        _jumpPipeline = _rd.ComputePipelineCreate(_jumpShader);
        _colorPipeline = _rd.ComputePipelineCreate(_colorShader);
        AllocatedStorageBufferBytes = maximumInputPixels * 36L + maximumOutputPixels * 8L;
    }

    public long AllocatedStorageBufferBytes { get; }

    public TerrainRenderResult Render(float[] inputCoverage, int inputWidth, int inputHeight, int interiorX,
        int interiorY, int outputWidth, int outputHeight, int globalOriginX, int globalOriginY,
        int documentWidth, int documentHeight)
    {
        var coverageBytes = new byte[inputCoverage.Length * sizeof(float)];
        Buffer.BlockCopy(inputCoverage, 0, coverageBytes, 0, coverageBytes.Length);
        _rd.BufferUpdate(_coverageBuffer, 0, (uint)coverageBytes.Length, coverageBytes);

        Span<byte> seedPush = stackalloc byte[8];
        WriteInt(seedPush, 0, inputWidth);
        WriteInt(seedPush, 4, inputHeight);
        var groupsX = (uint)((inputWidth + 7) / 8);
        var groupsY = (uint)((inputHeight + 7) / 8);
        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _seedPipeline);
        _rd.ComputeListBindUniformSet(list, _seedSet, 0);
        _rd.ComputeListSetPushConstant(list, seedPush, (uint)seedPush.Length);
        _rd.ComputeListDispatch(list, groupsX, groupsY, 1);
        _rd.ComputeListAddBarrier(list);

        var firstStep = 1;
        while (firstStep < Math.Max(inputWidth, inputHeight)) firstStep <<= 1;
        firstStep >>= 1;
        var currentIsA = true;
        for (var step = firstStep; step >= 1; step >>= 1)
            Jump(step);
        Jump(1);
        Jump(1);

        Span<byte> colorPush = stackalloc byte[40];
        WriteInt(colorPush, 0, inputWidth);
        WriteInt(colorPush, 4, inputHeight);
        WriteInt(colorPush, 8, interiorX);
        WriteInt(colorPush, 12, interiorY);
        WriteInt(colorPush, 16, outputWidth);
        WriteInt(colorPush, 20, outputHeight);
        WriteInt(colorPush, 24, globalOriginX);
        WriteInt(colorPush, 28, globalOriginY);
        WriteInt(colorPush, 32, documentWidth);
        WriteInt(colorPush, 36, documentHeight);
        _rd.ComputeListBindComputePipeline(list, _colorPipeline);
        _rd.ComputeListBindUniformSet(list, currentIsA ? _colorASet : _colorBSet, 0);
        _rd.ComputeListSetPushConstant(list, colorPush, (uint)colorPush.Length);
        _rd.ComputeListDispatch(list, (uint)((outputWidth + 7) / 8), (uint)((outputHeight + 7) / 8), 1);
        _rd.ComputeListEnd();
        _rd.Submit();
        _rd.Sync();

        var rgba = _rd.BufferGetData(_outputBuffer, 0, (uint)(outputWidth * outputHeight * 4));
        var distanceBytes = _rd.BufferGetData(_distanceBuffer, 0, (uint)(outputWidth * outputHeight * sizeof(float)));
        var distances = new float[outputWidth * outputHeight];
        Buffer.BlockCopy(distanceBytes, 0, distances, 0, distanceBytes.Length);
        return new TerrainRenderResult(rgba, distances);

        void Jump(int step)
        {
            Span<byte> jumpPush = stackalloc byte[16];
            WriteInt(jumpPush, 0, inputWidth);
            WriteInt(jumpPush, 4, inputHeight);
            WriteInt(jumpPush, 8, step);
            WriteInt(jumpPush, 12, 0);
            _rd.ComputeListBindComputePipeline(list, _jumpPipeline);
            _rd.ComputeListBindUniformSet(list, currentIsA ? _jumpAbSet : _jumpBaSet, 0);
            _rd.ComputeListSetPushConstant(list, jumpPush, (uint)jumpPush.Length);
            _rd.ComputeListDispatch(list, groupsX, groupsY, 1);
            _rd.ComputeListAddBarrier(list);
            currentIsA = !currentIsA;
        }
    }

    private Rid LoadShader(string path)
    {
        var file = ResourceLoader.Load<RDShaderFile>(path)
            ?? throw new InvalidOperationException($"Shader did not load: {path}");
        var spirV = file.GetSpirV();
        var compileError = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrWhiteSpace(compileError))
            throw new InvalidOperationException($"Shader did not compile: {path}\n{compileError}");
        var shader = _rd.ShaderCreateFromSpirV(spirV);
        if (!shader.IsValid) throw new InvalidOperationException($"Shader produced invalid bytecode: {path}");
        return shader;
    }

    private Rid CreateSet(Rid shader, params (int Binding, Rid Buffer)[] bindings)
    {
        var uniforms = new Godot.Collections.Array<RDUniform>();
        try
        {
            foreach (var (binding, buffer) in bindings)
            {
                var uniform = new RDUniform
                {
                    UniformType = RenderingDevice.UniformType.StorageBuffer,
                    Binding = binding
                };
                uniform.AddId(buffer);
                uniforms.Add(uniform);
            }
            return _rd.UniformSetCreate(uniforms, shader, 0);
        }
        finally
        {
            foreach (var uniform in uniforms) uniform.Dispose();
        }
    }

    private static void WriteInt(Span<byte> destination, int offset, int value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    public void Dispose()
    {
        foreach (var resource in new[]
                 {
                     _colorPipeline, _jumpPipeline, _seedPipeline, _colorBSet, _colorASet, _jumpBaSet,
                     _jumpAbSet, _seedSet, _distanceBuffer, _outputBuffer, _seedB, _seedA, _coverageBuffer,
                     _colorShader, _jumpShader, _seedShader
                 })
            if (resource.IsValid) _rd.FreeRid(resource);
        _rd.Free();
    }
}
