using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Mapwright.Domain;

namespace Mapwright.Rendering;

public readonly record struct GpuTraceSubmission(
    long Revision, long Generation, Rid DisplayTexture,
    long UploadStartedTimestamp, long DispatchRecordedTimestamp,
    double UploadMilliseconds, double DispatchMilliseconds);

/// <summary>
/// Optional long-lived shader/pipeline infrastructure. It contains no source,
/// terrain, stroke or display pixels. A strict reconstruction may reuse this
/// kernel while recreating and replaying every data resource after input.
/// </summary>
public sealed class GpuTerrainKernel : IDisposable
{
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RenderingDevice? _rd;
    private bool _disposed;
    public Task Ready => _ready.Task;
    public Rid Shader { get; private set; }
    public Rid Pipeline { get; private set; }
    public double CompileMilliseconds { get; private set; }

    public GpuTerrainKernel() => RenderingServer.CallOnRenderThread(Callable.From(() =>
    {
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainKernel));
            _rd = RenderingServer.GetRenderingDevice()
                ?? throw new NotSupportedException("The pinned renderer exposes no global RenderingDevice.");
            var started = Stopwatch.GetTimestamp();
            (Shader, Pipeline) = GpuTerrainTracer.CompileKernel(_rd);
            CompileMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _ready.TrySetResult(true);
        }
        catch (Exception exception) { _ready.TrySetException(exception); }
    }));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            if (_rd is null) return;
            if (Pipeline.IsValid) _rd.FreeRid(Pipeline);
            if (Shader.IsValid) _rd.FreeRid(Shader);
        }));
    }
}

/// <summary>
/// An opt-in legacy-stroke feasibility tracer. It retains every submitted
/// generation until disposal so an older queued canvas draw cannot sample a
/// freed texture. It is not the production GPU evaluator.
/// </summary>
public sealed class GpuTerrainTracer : IDisposable
{
    private const int MaximumSamples = 256;
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _source;
    private readonly GpuTerrainKernel? _sharedKernel;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RenderingDevice? _rd;
    private Rid _sourceTexture;
    private Rid _outputTexture;
    private Rid _displayTexture;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _sampleBuffer;
    private Rid _uniformSet;
    private readonly List<Rid> _retainedDeviceResources = [];
    private readonly List<Rid> _retainedDisplayTextures = [];
    private long _lastGeneration;
    private bool _disposed;

    public Task Ready => _ready.Task;
    public double SourceUploadMilliseconds { get; private set; }
    public double ShaderCompileMilliseconds { get; private set; }

    /// <summary>Read only an immutable MWDS source blob; never render changed pixels on CPU.</summary>
    public static byte[] ReadVerifiedPreparedSource(string projectRoot, string hash,
        int width, int height)
    {
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Prepared source hash is invalid.");
        var path = Path.Combine(Path.GetFullPath(projectRoot), "blobs", hash);
        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Immutable prepared source hash changed.");
        if (bytes.Length != checked(16 + width * height * 4) ||
            !bytes.AsSpan(0, 4).SequenceEqual("MWDS"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != 2 ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4)) != width ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4)) != height)
            throw new InvalidDataException("Immutable prepared source dimensions or version changed.");
        return bytes.AsSpan(16).ToArray();
    }

    public GpuTerrainTracer(int width, int height, byte[] sourceRgba,
        GpuTerrainKernel? sharedKernel = null)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (sourceRgba.Length != checked(width * height * 4))
            throw new ArgumentException("A complete RGBA8 source is required.", nameof(sourceRgba));
        _width = width;
        _height = height;
        _source = (byte[])sourceRgba.Clone();
        if (sharedKernel is not null && !sharedKernel.Ready.IsCompletedSuccessfully)
            throw new InvalidOperationException("Shared GPU kernel must be initialized before source upload.");
        _sharedKernel = sharedKernel;
        RenderingServer.CallOnRenderThread(Callable.From(InitializeOnRenderThread));
    }

    public async Task<GpuTraceSubmission> SubmitLegacyAsync(
        PaintStroke stroke, double mapWidth, double mapHeight,
        long revision, long generation, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        stroke.Validate();
        if (stroke.Kind != TerrainStrokeKind.Texture || stroke.Samples.Length is < 1 or > MaximumSamples)
            throw new NotSupportedException("Tracer accepts one legacy texture stroke with 1–256 samples.");
        if (!double.IsFinite(mapWidth) || !double.IsFinite(mapHeight) || mapWidth <= 0 || mapHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapWidth));
        if (generation <= _lastGeneration)
            throw new InvalidOperationException("Tracer generations must be strictly increasing.");
        _lastGeneration = generation;
        await Ready.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource<GpuTraceSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                var result = RecordStrokeOnRenderThread(stroke, mapWidth, mapHeight, revision, generation);
                completion.TrySetResult(result);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }));
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Strict reconstruction experiment: replay a projected complete legacy
    /// history from the immutable source into two GPU scratch textures. The
    /// caller must require a matching durable acknowledgement before display.
    /// A barrier separates strokes; no previous visible texture is a source.
    /// </summary>
    public async Task<GpuTraceSubmission> SubmitLegacyReplayAsync(
        IReadOnlyList<PaintStroke> strokes, double mapWidth, double mapHeight,
        long revision, long generation, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        ArgumentNullException.ThrowIfNull(strokes);
        if (strokes.Count is < 1 or > 64 || strokes.Any(stroke =>
                stroke.Kind != TerrainStrokeKind.Texture ||
                stroke.Samples.Length is < 1 or > MaximumSamples))
            throw new NotSupportedException("Strict tracer accepts 1–64 legacy texture strokes of 1–256 samples.");
        foreach (var stroke in strokes) stroke.Validate();
        if (!double.IsFinite(mapWidth) || !double.IsFinite(mapHeight) || mapWidth <= 0 || mapHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapWidth));
        if (generation <= _lastGeneration)
            throw new InvalidOperationException("Tracer generations must be strictly increasing.");
        _lastGeneration = generation;
        await Ready.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource<GpuTraceSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                completion.TrySetResult(RecordReplayOnRenderThread(strokes, mapWidth, mapHeight,
                    revision, generation));
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }));
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Establishes the already-visible imported baseline before timed edits.
    /// Its texture is a display-only lease and is never passed to a successor.
    /// </summary>
    public async Task<GpuTraceSubmission> SubmitSourceDisplayAsync(
        long revision, long generation, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        if (generation <= _lastGeneration)
            throw new InvalidOperationException("Tracer generations must be strictly increasing.");
        _lastGeneration = generation;
        await Ready.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource<GpuTraceSubmission>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                if (_rd is null || !_sourceTexture.IsValid)
                    throw new InvalidOperationException("Baseline source upload is unavailable.");
                var started = Stopwatch.GetTimestamp();
                _displayTexture = RenderingServer.TextureRdCreate(_sourceTexture);
                if (!_displayTexture.IsValid)
                    throw new InvalidOperationException("Baseline display wrapper allocation failed.");
                _retainedDisplayTextures.Add(_displayTexture);
                var recorded = Stopwatch.GetTimestamp();
                completion.TrySetResult(new GpuTraceSubmission(revision, generation, _displayTexture,
                    started, recorded, 0, Stopwatch.GetElapsedTime(started, recorded).TotalMilliseconds));
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }));
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Validation-only readback; never call within a timed viewport path.</summary>
    public async Task<byte[]> ReadbackForValidationAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        if (_lastGeneration == 0) throw new InvalidOperationException("Submit a stroke before readback.");
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                var bytes = _rd?.TextureGetData(_outputTexture, 0)
                    ?? throw new NotSupportedException("Global RenderingDevice readback is unavailable.");
                if (bytes.Length != checked(_width * _height * 4))
                    throw new InvalidDataException("GPU readback dimensions do not match the source.");
                completion.TrySetResult(bytes);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
        }));
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private void InitializeOnRenderThread()
    {
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GpuTerrainTracer));
            _rd = RenderingServer.GetRenderingDevice()
                ?? throw new NotSupportedException("The pinned renderer exposes no global RenderingDevice.");
            var uploadStarted = Stopwatch.GetTimestamp();
            using var sourceFormat = TextureFormat(RenderingDevice.TextureUsageBits.StorageBit |
                RenderingDevice.TextureUsageBits.SamplingBit);
            using var view = new RDTextureView();
            _sourceTexture = _rd.TextureCreate(sourceFormat, view,
                new Godot.Collections.Array<byte[]> { _source });
            if (!_sourceTexture.IsValid)
                throw new InvalidOperationException("The global device rejected an RGBA8 tracer texture.");
            var uploadEnded = Stopwatch.GetTimestamp();
            SourceUploadMilliseconds = Stopwatch.GetElapsedTime(uploadStarted, uploadEnded).TotalMilliseconds;
            if (_sharedKernel is null)
            {
                var compileStarted = Stopwatch.GetTimestamp();
                (_shader, _pipeline) = CompileKernel(_rd);
                ShaderCompileMilliseconds = Stopwatch.GetElapsedTime(compileStarted).TotalMilliseconds;
            }
            else
            {
                _shader = _sharedKernel.Shader;
                _pipeline = _sharedKernel.Pipeline;
            }
            _ready.TrySetResult(true);
        }
        catch (Exception exception) { _ready.TrySetException(exception); }
    }

    internal static (Rid Shader, Rid Pipeline) CompileKernel(RenderingDevice device)
    {
        // Compile checked-in GLSL directly; no editor import cache is required.
        var sourceText = Godot.FileAccess.GetFileAsString("res://Shaders/gpu_terrain_tracer.glsl");
        if (string.IsNullOrWhiteSpace(sourceText))
            throw new InvalidOperationException("GPU terrain tracer GLSL source is missing.");
        sourceText = sourceText.Replace("#[compute]", string.Empty, StringComparison.Ordinal);
        using var shaderSource = new RDShaderSource { SourceCompute = sourceText };
        var spirV = device.ShaderCompileSpirVFromSource(shaderSource);
        var compileError = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrWhiteSpace(compileError))
            throw new InvalidOperationException($"GPU terrain tracer shader compile failed: {compileError}");
        var shader = device.ShaderCreateFromSpirV(spirV);
        var pipeline = device.ComputePipelineCreate(shader);
        if (!shader.IsValid || !pipeline.IsValid)
            throw new InvalidOperationException("The global device rejected the tracer pipeline.");
        return (shader, pipeline);
    }

    private GpuTraceSubmission RecordStrokeOnRenderThread(PaintStroke stroke,
        double mapWidth, double mapHeight, long revision, long generation)
    {
        if (_disposed || _rd is null || !_pipeline.IsValid)
            throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        var started = Stopwatch.GetTimestamp();
        var inputTexture = _outputTexture.IsValid ? _outputTexture : _sourceTexture;
        using var outputFormat = TextureFormat(RenderingDevice.TextureUsageBits.StorageBit |
            RenderingDevice.TextureUsageBits.SamplingBit |
            RenderingDevice.TextureUsageBits.CanCopyFromBit);
        using var view = new RDTextureView();
        _outputTexture = _rd.TextureCreate(outputFormat, view,
            new Godot.Collections.Array<byte[]> { new byte[_source.Length] });
        _displayTexture = RenderingServer.TextureRdCreate(_outputTexture);
        if (!_outputTexture.IsValid || !_displayTexture.IsValid)
            throw new InvalidOperationException("GPU tracer generation allocation failed.");
        _retainedDeviceResources.Add(_outputTexture);
        _retainedDisplayTextures.Add(_displayTexture);
        var samples = new byte[checked(stroke.Samples.Length * 16)];
        for (var index = 0; index < stroke.Samples.Length; index++)
        {
            PutFloat(samples, index * 16, (float)stroke.Samples[index].X);
            PutFloat(samples, index * 16 + 4, (float)stroke.Samples[index].Y);
        }
        _sampleBuffer = _rd.StorageBufferCreate((uint)samples.Length, samples);
        if (!_sampleBuffer.IsValid) throw new InvalidOperationException("Sample upload failed.");
        _retainedDeviceResources.Add(_sampleBuffer);
        using var sourceUniform = Uniform(RenderingDevice.UniformType.Image, 0, inputTexture);
        using var outputUniform = Uniform(RenderingDevice.UniformType.Image, 1, _outputTexture);
        using var sampleUniform = Uniform(RenderingDevice.UniformType.StorageBuffer, 2, _sampleBuffer);
        _uniformSet = _rd.UniformSetCreate(
            new Godot.Collections.Array<RDUniform> { sourceUniform, outputUniform, sampleUniform },
            _shader, 0);
        if (!_uniformSet.IsValid) throw new InvalidOperationException("GPU tracer uniform binding failed.");
        _retainedDeviceResources.Add(_uniformSet);
        var uploadEnd = Stopwatch.GetTimestamp();
        var colourHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{stroke.Brush.TextureHash}:{stroke.Brush.Seed}"));
        Span<byte> push = stackalloc byte[48];
        PutFloat(push, 0, _width);
        PutFloat(push, 4, _height);
        PutFloat(push, 8, (float)mapWidth);
        PutFloat(push, 12, (float)mapHeight);
        PutFloat(push, 16, (float)stroke.Brush.Radius);
        PutFloat(push, 20, (float)stroke.Brush.Hardness);
        PutFloat(push, 24, (float)(stroke.Brush.Opacity * stroke.Brush.Flow));
        PutFloat(push, 28, stroke.Samples.Length);
        PutFloat(push, 32, (96 + colourHash[0] % 144) / 255f);
        PutFloat(push, 36, (72 + colourHash[1] % 160) / 255f);
        PutFloat(push, 40, (64 + colourHash[2] % 168) / 255f);
        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _pipeline);
        _rd.ComputeListBindUniformSet(list, _uniformSet, 0);
        _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
        _rd.ComputeListDispatch(list, (uint)((_width + 7) / 8), (uint)((_height + 7) / 8), 1);
        _rd.ComputeListEnd();
        var recorded = Stopwatch.GetTimestamp();
        return new GpuTraceSubmission(revision, generation, _displayTexture,
            started, recorded,
            Stopwatch.GetElapsedTime(started, uploadEnd).TotalMilliseconds,
            Stopwatch.GetElapsedTime(uploadEnd, recorded).TotalMilliseconds);
    }

    private GpuTraceSubmission RecordReplayOnRenderThread(IReadOnlyList<PaintStroke> strokes,
        double mapWidth, double mapHeight, long revision, long generation)
    {
        if (_disposed || _rd is null || !_pipeline.IsValid)
            throw new ObjectDisposedException(nameof(GpuTerrainTracer));
        var started = Stopwatch.GetTimestamp();
        using var outputFormat = TextureFormat(RenderingDevice.TextureUsageBits.StorageBit |
            RenderingDevice.TextureUsageBits.SamplingBit |
            RenderingDevice.TextureUsageBits.CanCopyFromBit);
        using var view = new RDTextureView();
        // Every dispatch writes every output pixel, so scratch images do not
        // need a zero-filled CPU upload before the first write.
        var first = _rd.TextureCreate(outputFormat, view,
            new Godot.Collections.Array<byte[]>());
        if (!first.IsValid) throw new InvalidOperationException("Strict first scratch texture allocation failed.");
        _retainedDeviceResources.Add(first);
        var second = strokes.Count > 1
            ? _rd.TextureCreate(outputFormat, view,
                new Godot.Collections.Array<byte[]>())
            : default;
        if (strokes.Count > 1)
        {
            if (!second.IsValid) throw new InvalidOperationException("Strict second scratch texture allocation failed.");
            _retainedDeviceResources.Add(second);
        }

        var uploadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var input = _sourceTexture;
        var list = _rd.ComputeListBegin();
        Span<byte> push = stackalloc byte[48];
        for (var strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
        {
            var operationStarted = Stopwatch.GetTimestamp();
            var stroke = strokes[strokeIndex];
            var output = strokeIndex % 2 == 0 ? first : second;
            var samples = new byte[checked(stroke.Samples.Length * 16)];
            for (var index = 0; index < stroke.Samples.Length; index++)
            {
                PutFloat(samples, index * 16, (float)stroke.Samples[index].X);
                PutFloat(samples, index * 16 + 4, (float)stroke.Samples[index].Y);
            }
            var sampleBuffer = _rd.StorageBufferCreate((uint)samples.Length, samples);
            if (!sampleBuffer.IsValid) throw new InvalidOperationException("Strict replay sample upload failed.");
            _retainedDeviceResources.Add(sampleBuffer);
            using var sourceUniform = Uniform(RenderingDevice.UniformType.Image, 0, input);
            using var outputUniform = Uniform(RenderingDevice.UniformType.Image, 1, output);
            using var sampleUniform = Uniform(RenderingDevice.UniformType.StorageBuffer, 2, sampleBuffer);
            var uniformSet = _rd.UniformSetCreate(
                new Godot.Collections.Array<RDUniform> { sourceUniform, outputUniform, sampleUniform },
                _shader, 0);
            if (!uniformSet.IsValid) throw new InvalidOperationException("Strict replay uniform binding failed.");
            _retainedDeviceResources.Add(uniformSet);
            var colourHash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{stroke.Brush.TextureHash}:{stroke.Brush.Seed}"));
            push.Clear();
            PutFloat(push, 0, _width);
            PutFloat(push, 4, _height);
            PutFloat(push, 8, (float)mapWidth);
            PutFloat(push, 12, (float)mapHeight);
            PutFloat(push, 16, (float)stroke.Brush.Radius);
            PutFloat(push, 20, (float)stroke.Brush.Hardness);
            PutFloat(push, 24, (float)(stroke.Brush.Opacity * stroke.Brush.Flow));
            PutFloat(push, 28, stroke.Samples.Length);
            PutFloat(push, 32, (96 + colourHash[0] % 144) / 255f);
            PutFloat(push, 36, (72 + colourHash[1] % 160) / 255f);
            PutFloat(push, 40, (64 + colourHash[2] % 168) / 255f);
            var prepared = Stopwatch.GetTimestamp();
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, uniformSet, 0);
            _rd.ComputeListSetPushConstant(list, push, (uint)push.Length);
            _rd.ComputeListDispatch(list, (uint)((_width + 7) / 8), (uint)((_height + 7) / 8), 1);
            if (strokeIndex + 1 < strokes.Count) _rd.ComputeListAddBarrier(list);
            uploadMilliseconds += Stopwatch.GetElapsedTime(operationStarted, prepared).TotalMilliseconds;
            input = output;
        }
        _rd.ComputeListEnd();
        _outputTexture = input;
        _displayTexture = RenderingServer.TextureRdCreate(input);
        if (!_displayTexture.IsValid)
            throw new InvalidOperationException("Strict final display wrapper allocation failed.");
        _retainedDisplayTextures.Add(_displayTexture);
        var recorded = Stopwatch.GetTimestamp();
        var dispatchMilliseconds = Math.Max(0,
            Stopwatch.GetElapsedTime(started, recorded).TotalMilliseconds - uploadMilliseconds);
        return new GpuTraceSubmission(revision, generation, _displayTexture,
            started, recorded, uploadMilliseconds, dispatchMilliseconds);
    }

    private RDTextureFormat TextureFormat(RenderingDevice.TextureUsageBits usage) => new()
    {
        Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
        Width = (uint)_width,
        Height = (uint)_height,
        Depth = 1,
        ArrayLayers = 1,
        Mipmaps = 1,
        TextureType = RenderingDevice.TextureType.Type2D,
        Samples = RenderingDevice.TextureSamples.Samples1,
        UsageBits = usage
    };

    private static RDUniform Uniform(RenderingDevice.UniformType type, int binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = type, Binding = binding };
        uniform.AddId(rid);
        return uniform;
    }

    private static void PutFloat(Span<byte> destination, int offset, float value) =>
        BitConverter.TryWriteBytes(destination.Slice(offset, 4), value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (var wrapper in _retainedDisplayTextures)
                if (wrapper.IsValid) RenderingServer.FreeRid(wrapper);
            if (_rd is null) return;
            // Uniform sets refer to the input/output texture RIDs. Retire in
            // reverse dependency order; freeing an input first invalidates a
            // later generation's uniform set in pinned Godot 4.7.2.
            for (var index = _retainedDeviceResources.Count - 1; index >= 0; index--)
            {
                var rid = _retainedDeviceResources[index];
                if (rid.IsValid) _rd.FreeRid(rid);
            }
            if (_sourceTexture.IsValid) _rd.FreeRid(_sourceTexture);
            if (_sharedKernel is null)
            {
                if (_pipeline.IsValid) _rd.FreeRid(_pipeline);
                if (_shader.IsValid) _rd.FreeRid(_shader);
            }
        }));
    }
}
