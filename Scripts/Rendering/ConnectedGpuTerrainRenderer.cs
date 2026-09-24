using System.Buffers.Binary;
using System.Diagnostics;
using Godot;
using Mapwright.Domain;

namespace Mapwright.Rendering;

public readonly record struct GpuTerrainRegion(int Left, int Top, int Width, int Height);

public readonly record struct GpuTerrainGeneration(
    long Revision, long Generation, Rid DisplayTexture, int Width, int Height,
    bool InputsRebuilt, double PrepareMilliseconds, double InputMilliseconds, double RecordMilliseconds);

/// <summary>
/// Production connected-terrain renderer on Godot's main RenderingDevice. Every
/// generation re-evaluates the complete visible canvas from verified immutable inputs
/// and the acknowledged snapshot in double precision (see connected_terrain_compose.glsl);
/// no previously rendered pixels are ever an input. Sampled source buffers are derived
/// state keyed by source identity, canvas sampling and the graph's cache epoch.
/// All device work is recorded on the (single-threaded) render thread; no readback
/// occurs outside explicit validation.
/// </summary>
public sealed class ConnectedGpuTerrainRenderer : IDisposable
{
    public const string AccelerationMode = "gpu-fp64";

    private static Kernels? _kernels;
    private static string? _kernelFailure;

    private readonly RenderingDevice _rd;
    private readonly List<GenerationResources> _generations = [];
    private SourceResources? _sources;
    private InputResources? _inputs;
    private bool _disposed;

    private sealed record Kernels(Rid ComposeShader, Rid ComposePipeline, Rid SampleShader,
        Rid SamplePipeline, Rid Tables, Rid Empty);

    /// <summary>Verified immutable rasters uploaded once per source identity and cache epoch.</summary>
    private sealed class SourceResources
    {
        public required string Identity { get; init; }
        public required long Epoch { get; init; }
        public Rid Background;
        public Rid Foreground;
        public Rid Coverage;
        public readonly List<Rid> Owned = [];
    }

    /// <summary>Full-canvas sampled inputs for interactive generations.</summary>
    private sealed class InputResources
    {
        public required string Identity { get; init; }
        public required long Epoch { get; init; }
        public Rid Background;
        public Rid Foreground;
        public Rid Coverage;
        public readonly List<Rid> Owned = [];
    }

    private sealed class GenerationResources
    {
        public required long Generation { get; init; }
        public required Rid Output { get; init; }
        public required Rid Display { get; init; }
        public required Rid UniformSet { get; init; }
        public readonly List<Rid> Owned = [];
    }

    private ConnectedGpuTerrainRenderer(RenderingDevice rd) => _rd = rd;

    /// <summary>
    /// Creates a renderer when the main RenderingDevice exists and the fp64 kernels compile.
    /// Returns null with a reason otherwise (for example under --headless).
    /// </summary>
    public static ConnectedGpuTerrainRenderer? TryCreate(out string reason)
    {
        reason = string.Empty;
        if (System.Environment.GetEnvironmentVariable("MAPWRIGHT_TERRAIN_RENDERER") == "cpu")
        {
            reason = "CPU reference renderer selected by MAPWRIGHT_TERRAIN_RENDERER=cpu.";
            return null;
        }
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null)
        {
            reason = "No main RenderingDevice (headless or non-RD renderer).";
            return null;
        }
        ConnectedGpuTerrainRenderer? created = null;
        string failure = "The render thread did not run the GPU terrain initialization synchronously.";
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                EnsureKernels(rd);
                created = new ConnectedGpuTerrainRenderer(rd);
            }
            catch (Exception exception) { failure = exception.Message; }
        }));
        if (created is null) reason = failure;
        return created;
    }

    /// <summary>True when a main RenderingDevice exists and the GPU path is not disabled.</summary>
    public static bool CanCreate(out string reason)
    {
        reason = string.Empty;
        if (System.Environment.GetEnvironmentVariable("MAPWRIGHT_TERRAIN_RENDERER") == "cpu")
            reason = "CPU reference renderer selected by MAPWRIGHT_TERRAIN_RENDERER=cpu.";
        else if (_kernelFailure is not null)
            reason = _kernelFailure;
        else if (RenderingServer.GetRenderingDevice() is null)
            reason = "No main RenderingDevice (headless or non-RD renderer).";
        return reason.Length == 0;
    }

    /// <summary>
    /// Creates a renderer for asynchronous frozen export from any thread. Kernels are
    /// compiled on the render thread before its first recording.
    /// </summary>
    public static ConnectedGpuTerrainRenderer? CreateForExport() =>
        CanCreate(out _) && RenderingServer.GetRenderingDevice() is { } rd
            ? new ConnectedGpuTerrainRenderer(rd)
            : null;

    /// <summary>Compiles the shared kernels ahead of use; failures surface on first render.</summary>
    public static void PrepareKernels()
    {
        if (_kernels is not null || _kernelFailure is not null ||
            System.Environment.GetEnvironmentVariable("MAPWRIGHT_TERRAIN_RENDERER") == "cpu") return;
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try { EnsureKernels(rd); }
            catch (Exception exception) { GD.PrintErr($"GPU terrain kernels unavailable: {exception.Message}"); }
        }));
    }

    private static void EnsureKernels(RenderingDevice rd)
    {
        if (_kernels is not null) return;
        if (_kernelFailure is not null) throw new NotSupportedException(_kernelFailure);
        try
        {
            var (composeShader, composePipeline) = Compile(rd, "res://Shaders/connected_terrain_compose.glsl");
            var (sampleShader, samplePipeline) = Compile(rd, "res://Shaders/connected_terrain_sample.glsl");
            var tables = rd.StorageBufferCreate((uint)ColourTables.Length, ColourTables);
            var empty = rd.StorageBufferCreate(16, new byte[16]);
            if (!tables.IsValid || !empty.IsValid)
                throw new InvalidOperationException("GPU terrain table allocation failed.");
            _kernels = new Kernels(composeShader, composePipeline, sampleShader, samplePipeline, tables, empty);
        }
        catch (Exception exception)
        {
            _kernelFailure = exception.Message;
            throw;
        }
    }

    private static (Rid Shader, Rid Pipeline) Compile(RenderingDevice rd, string path)
    {
        var text = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"{path} is missing.");
        text = text.Replace("#[compute]", string.Empty, StringComparison.Ordinal);
        using var source = new RDShaderSource { SourceCompute = text };
        var spirV = rd.ShaderCompileSpirVFromSource(source);
        var error = spirV.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException($"{path} failed to compile: {error}");
        var shader = rd.ShaderCreateFromSpirV(spirV);
        var pipeline = rd.ComputePipelineCreate(shader);
        if (!shader.IsValid || !pipeline.IsValid)
            throw new InvalidOperationException($"{path} pipeline was rejected (fp64 compute required).");
        return (shader, pipeline);
    }

    /// <summary>
    /// Records a complete evaluation of <paramref name="snapshot"/> at the sources' canvas
    /// sampling. Callers publish the returned texture only for an acknowledged snapshot.
    /// </summary>
    public GpuTerrainGeneration Render(MapProject snapshot, GpuTerrainSources sources, long generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_generations.Count > 0 && generation <= _generations[^1].Generation)
            throw new InvalidOperationException("GPU terrain generations must increase.");
        var started = Stopwatch.GetTimestamp();
        snapshot.ValidateConnectedTerrain();
        var plan = TerrainProgram.Build(snapshot, sources);
        var prepared = Stopwatch.GetTimestamp();
        GpuTerrainGeneration? result = null;
        Exception? failure = null;
        var ran = false;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            ran = true;
            try { result = RecordOnRenderThread(snapshot, sources, plan, generation, started, prepared); }
            catch (Exception exception) { failure = exception; }
        }));
        if (!ran) throw new NotSupportedException("GPU terrain recording requires the single-threaded render model.");
        if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
        return result!.Value;
    }

    private GpuTerrainGeneration RecordOnRenderThread(MapProject snapshot, GpuTerrainSources sources,
        TerrainProgram plan, long generation, long started, long prepared)
    {
        var kernels = _kernels ?? throw new InvalidOperationException("GPU terrain kernels are unavailable.");
        var inputStarted = Stopwatch.GetTimestamp();
        var rebuilt = EnsureCanvasInputs(snapshot, sources, kernels);
        var inputEnded = Stopwatch.GetTimestamp();
        var inputs = _inputs!;
        var region = new GpuTerrainRegion(0, 0, sources.CanvasWidth, sources.CanvasHeight);
        var owned = new List<Rid>();
        var (output, set) = Compose(snapshot, sources, plan, region, inputs.Background,
            inputs.Foreground, inputs.Coverage, owned, kernels);
        var display = RenderingServer.TextureRdCreate(output);
        if (!display.IsValid) throw new InvalidOperationException("GPU terrain display wrapper failed.");
        var published = new GenerationResources
        {
            Generation = generation, Output = output, Display = display, UniformSet = set
        };
        published.Owned.AddRange(owned);
        _generations.Add(published);
        var recorded = Stopwatch.GetTimestamp();
        return new GpuTerrainGeneration(snapshot.Revision, generation, display,
            sources.CanvasWidth, sources.CanvasHeight, rebuilt,
            Stopwatch.GetElapsedTime(started, prepared).TotalMilliseconds,
            Stopwatch.GetElapsedTime(inputStarted, inputEnded).TotalMilliseconds,
            Stopwatch.GetElapsedTime(inputEnded, recorded).TotalMilliseconds);
    }

    /// <summary>
    /// Frozen-export path: evaluates one region of the output sampling and returns its
    /// RGBA8 bytes through the device's asynchronous readback. It may be called from any
    /// thread; recording runs on the render thread and never stalls it for the result.
    /// </summary>
    public Task<byte[]> RenderRegionAsync(MapProject snapshot, GpuTerrainSources sources,
        GpuTerrainRegion region, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (region.Width <= 0 || region.Height <= 0 || region.Left < 0 || region.Top < 0 ||
            region.Left + region.Width > sources.CanvasWidth || region.Top + region.Height > sources.CanvasHeight)
            throw new ArgumentOutOfRangeException(nameof(region));
        snapshot.ValidateConnectedTerrain();
        var plan = TerrainProgram.Build(snapshot, sources);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            var owned = new List<Rid>();
            var output = default(Rid);
            var set = default(Rid);
            void Release()
            {
                if (set.IsValid && _rd.UniformSetIsValid(set)) _rd.FreeRid(set);
                FreeAll(owned);
                if (output.IsValid) _rd.FreeRid(output);
            }
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ConnectedGpuTerrainRenderer));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureKernels(_rd);
                var kernels = _kernels ?? throw new InvalidOperationException("GPU terrain kernels are unavailable.");
                var uploaded = EnsureSources(sources);
                var background = SampleLayer(snapshot, sources.Background, uploaded.Background, false,
                    sources, region, owned, kernels);
                var foreground = SampleLayer(snapshot, sources.Foreground, uploaded.Foreground, false,
                    sources, region, owned, kernels);
                var coverage = SampleLayer(snapshot, sources.Coverage, uploaded.Coverage, true,
                    sources, region, owned, kernels);
                (output, set) = Compose(snapshot, sources, plan, region, background, foreground, coverage,
                    owned, kernels);
                var error = _rd.TextureGetDataAsync(output, 0, Callable.From((byte[] data) =>
                {
                    Release();
                    if (data.Length != checked(region.Width * region.Height * 4))
                        completion.TrySetException(new InvalidDataException("GPU export readback size is wrong."));
                    else completion.TrySetResult(data);
                }));
                if (error != Error.Ok) throw new InvalidOperationException($"GPU export readback failed: {error}.");
            }
            catch (Exception exception)
            {
                Release();
                completion.TrySetException(exception);
            }
        }));
        return completion.Task.WaitAsync(cancellationToken);
    }

    private (Rid Output, Rid Set) Compose(MapProject snapshot, GpuTerrainSources sources, TerrainProgram plan,
        GpuTerrainRegion region, Rid background, Rid foreground, Rid coverage, List<Rid> owned, Kernels kernels)
    {
        var output = _rd.TextureCreate(OutputFormat(region.Width, region.Height), new RDTextureView(),
            new Godot.Collections.Array<byte[]>());
        if (!output.IsValid) throw new InvalidOperationException("GPU terrain output allocation failed.");
        try
        {
            var parameters = Buffer(plan.Parameters(snapshot, sources, region, background.IsValid,
                foreground.IsValid, coverage.IsValid), owned);
            var strokes = plan.StrokeCount == 0 ? kernels.Empty : Buffer(plan.Strokes, owned);
            var samples = plan.SampleCount == 0 ? kernels.Empty : Buffer(plan.Samples, owned);
            var river = plan.RiverCount == 0 ? kernels.Empty : Buffer(plan.River, owned);
            var set = _rd.UniformSetCreate(new Godot.Collections.Array<RDUniform>
            {
                Storage(0, parameters), Storage(1, kernels.Tables),
                Storage(2, background.IsValid ? background : kernels.Empty),
                Storage(3, foreground.IsValid ? foreground : kernels.Empty),
                Storage(4, coverage.IsValid ? coverage : kernels.Empty),
                Storage(5, strokes), Storage(6, samples), Storage(7, river),
                Image(8, output)
            }, kernels.ComposeShader, 0);
            if (!set.IsValid) throw new InvalidOperationException("GPU terrain uniform binding failed.");
            var list = _rd.ComputeListBegin();
            _rd.ComputeListBindComputePipeline(list, kernels.ComposePipeline);
            _rd.ComputeListBindUniformSet(list, set, 0);
            _rd.ComputeListDispatch(list, (uint)((region.Width + 7) / 8), (uint)((region.Height + 7) / 8), 1);
            _rd.ComputeListEnd();
            return (output, set);
        }
        catch
        {
            _rd.FreeRid(output);
            throw;
        }
    }

    private SourceResources EnsureSources(GpuTerrainSources sources)
    {
        if (_sources is { } current && current.Identity == sources.Identity && current.Epoch == sources.CacheEpoch)
            return current;
        // Recorded work keeps replaced buffers alive until the device retires its frame.
        if (_inputs is not null) FreeAll(_inputs.Owned);
        _inputs = null;
        if (_sources is not null) FreeAll(_sources.Owned);
        var uploaded = new SourceResources { Identity = sources.Identity, Epoch = sources.CacheEpoch };
        _sources = uploaded;
        Rid Upload(GpuSourceLayer? layer) => layer is null ? default : Buffer(layer.Rgba, uploaded.Owned);
        uploaded.Background = Upload(sources.Background);
        uploaded.Foreground = Upload(sources.Foreground);
        uploaded.Coverage = Upload(sources.Coverage);
        return uploaded;
    }

    private bool EnsureCanvasInputs(MapProject snapshot, GpuTerrainSources sources, Kernels kernels)
    {
        var identity = FormattableString.Invariant(
            $"{sources.Identity}|{snapshot.Width:R}x{snapshot.Height:R}");
        var uploaded = EnsureSources(sources);
        if (_inputs is { } current && current.Identity == identity && current.Epoch == sources.CacheEpoch)
            return false;
        if (_inputs is not null) FreeAll(_inputs.Owned);
        var inputs = new InputResources { Identity = identity, Epoch = sources.CacheEpoch };
        _inputs = inputs;
        var region = new GpuTerrainRegion(0, 0, sources.CanvasWidth, sources.CanvasHeight);
        inputs.Background = SampleLayer(snapshot, sources.Background, uploaded.Background, false,
            sources, region, inputs.Owned, kernels);
        inputs.Foreground = SampleLayer(snapshot, sources.Foreground, uploaded.Foreground, false,
            sources, region, inputs.Owned, kernels);
        inputs.Coverage = SampleLayer(snapshot, sources.Coverage, uploaded.Coverage, true,
            sources, region, inputs.Owned, kernels);
        return true;
    }

    private Rid SampleLayer(MapProject snapshot, GpuSourceLayer? layer, Rid sourceBuffer, bool coverage,
        GpuTerrainSources sources, GpuTerrainRegion region, List<Rid> owned, Kernels kernels)
    {
        if (layer is null) return default;
        if (layer.Mode == GpuSourceMode.Canvas &&
            (coverage || layer.Width != sources.CanvasWidth || layer.Height != sources.CanvasHeight))
            throw new InvalidDataException("A canvas-sampled GPU source must be colour at the canvas sampling.");
        var output = _rd.StorageBufferCreate((uint)checked(region.Width * region.Height * 4));
        if (!output.IsValid) throw new InvalidOperationException("GPU terrain sampled input allocation failed.");
        owned.Add(output);
        var parameters = new byte[112];
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(0), layer.Width);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(4), layer.Height);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(8), sources.CanvasWidth);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(12), sources.CanvasHeight);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(16), (int)layer.Mode);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(20), coverage ? 1 : 0);
        var transform = layer.Transform;
        WriteDouble(parameters, 32, snapshot.Width);
        WriteDouble(parameters, 40, snapshot.Height);
        WriteDouble(parameters, 48, transform?.OffsetX ?? 0);
        WriteDouble(parameters, 56, transform?.OffsetY ?? 0);
        WriteDouble(parameters, 64, transform?.ScaleX ?? 1);
        WriteDouble(parameters, 72, transform?.ScaleY ?? 1);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(96), region.Left);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(100), region.Top);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(104), region.Width);
        BinaryPrimitives.WriteInt32LittleEndian(parameters.AsSpan(108), region.Height);
        var parameterBuffer = _rd.StorageBufferCreate((uint)parameters.Length, parameters);
        var set = _rd.UniformSetCreate(new Godot.Collections.Array<RDUniform>
        {
            Storage(0, parameterBuffer), Storage(1, kernels.Tables), Storage(2, sourceBuffer),
            Storage(3, coverage ? kernels.Empty : output), Storage(4, coverage ? output : kernels.Empty)
        }, kernels.SampleShader, 0);
        if (!set.IsValid) throw new InvalidOperationException("GPU terrain sampling binding failed.");
        var list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, kernels.SamplePipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListDispatch(list, (uint)((region.Width + 7) / 8), (uint)((region.Height + 7) / 8), 1);
        _rd.ComputeListEnd();
        // Recorded work keeps these alive until the device retires the frame.
        _rd.FreeRid(set);
        _rd.FreeRid(parameterBuffer);
        return output;
    }

    /// <summary>Frees every generation older than the one the canvas has now drawn.</summary>
    public void RetireBefore(long drawnGeneration)
    {
        if (_disposed || _generations.Count < 2) return;
        var retire = _generations.Where(item => item.Generation < drawnGeneration).ToArray();
        if (retire.Length == 0) return;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (var item in retire) FreeGeneration(item);
        }));
        foreach (var item in retire) _generations.Remove(item);
    }

    /// <summary>Validation only; never inside a timed interaction path.</summary>
    public byte[] ReadbackForValidation(long generation)
    {
        var item = _generations.LastOrDefault(value => value.Generation == generation)
            ?? throw new KeyNotFoundException($"GPU terrain generation {generation} is not resident.");
        byte[]? bytes = null;
        RenderingServer.CallOnRenderThread(Callable.From(() => bytes = _rd.TextureGetData(item.Output, 0)));
        return bytes ?? throw new NotSupportedException("GPU terrain readback did not run synchronously.");
    }

    private void FreeGeneration(GenerationResources item)
    {
        if (item.Display.IsValid) RenderingServer.FreeRid(item.Display);
        // The device already freed this set if an input buffer it bound was replaced.
        if (item.UniformSet.IsValid && _rd.UniformSetIsValid(item.UniformSet)) _rd.FreeRid(item.UniformSet);
        FreeAll(item.Owned);
        if (item.Output.IsValid) _rd.FreeRid(item.Output);
    }

    private void FreeAll(List<Rid> owned)
    {
        for (var index = owned.Count - 1; index >= 0; index--)
            if (owned[index].IsValid) _rd.FreeRid(owned[index]);
        owned.Clear();
    }

    private Rid Buffer(byte[] bytes, List<Rid> owned)
    {
        var rid = _rd.StorageBufferCreate((uint)bytes.Length, bytes);
        if (!rid.IsValid) throw new InvalidOperationException("GPU terrain buffer allocation failed.");
        owned.Add(rid);
        return rid;
    }

    private static RDTextureFormat OutputFormat(int width, int height) => new()
    {
        Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
        Width = (uint)width,
        Height = (uint)height,
        Depth = 1,
        ArrayLayers = 1,
        Mipmaps = 1,
        TextureType = RenderingDevice.TextureType.Type2D,
        Samples = RenderingDevice.TextureSamples.Samples1,
        UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit |
                    RenderingDevice.TextureUsageBits.CanCopyFromBit
    };

    private static RDUniform Storage(int binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = binding };
        uniform.AddId(rid);
        return uniform;
    }

    private static RDUniform Image(int binding, Rid rid)
    {
        var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding };
        uniform.AddId(rid);
        return uniform;
    }

    internal static void WriteDouble(byte[] destination, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(destination.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));

    // decode[256] then thresholds[255], exactly as ConnectedTerrainGraph.LinearPixel.
    private static readonly byte[] ColourTables = BuildTables();

    private static byte[] BuildTables()
    {
        static double DecodeExact(double encoded) =>
            encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
        var bytes = new byte[(256 + 255) * 8];
        for (var value = 0; value < 256; value++) WriteDouble(bytes, value * 8, DecodeExact(value / 255d));
        for (var value = 0; value < 255; value++)
            WriteDouble(bytes, (256 + value) * 8, DecodeExact((value + 0.5) / 255d));
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var generations = _generations.ToArray();
        var inputs = _inputs;
        var sources = _sources;
        _generations.Clear();
        _inputs = null;
        _sources = null;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (var item in generations) FreeGeneration(item);
            if (inputs is not null) FreeAll(inputs.Owned);
            if (sources is not null) FreeAll(sources.Owned);
        }));
    }
}

/// <summary>
/// CPU-side, engine-free packing of one acknowledged snapshot into the typed stroke,
/// sample and river records the compose kernel evaluates. Array order preserves the
/// oracle's stored order within each kind and layer.
/// </summary>
internal sealed class TerrainProgram
{
    public byte[] Strokes { get; private init; } = [];
    public byte[] Samples { get; private init; } = [];
    public byte[] River { get; private init; } = [];
    public int StrokeCount { get; private init; }
    public int SampleCount { get; private init; }
    public int RiverCount { get; private init; }

    public static TerrainProgram Build(MapProject snapshot, GpuTerrainSources sources)
    {
        var background = snapshot.RequireRole(TerrainRole.Background);
        var foreground = snapshot.RequireRole(TerrainRole.Foreground);
        var strokes = new List<byte[]>();
        var samples = new List<(double X, double Y, double Scale)>();

        void Legacy(TerrainLayer layer, int role)
        {
            foreach (var stroke in layer.Strokes)
            {
                stroke.Validate();
                if (stroke.Kind != TerrainStrokeKind.Texture) continue;
                var record = Record(0, role, samples.Count, stroke.Samples.Length, stroke.Bounds);
                foreach (var sample in stroke.Samples) samples.Add((sample.X, sample.Y, 1));
                var brush = stroke.Brush;
                PutDoubles(record, 64, brush.Radius, brush.Hardness, brush.Opacity, brush.Flow);
                var colour = ConnectedTerrainGraph.LegacyColour(brush.TextureHash, brush.Seed);
                PutDoubles(record, 96, colour.R, colour.G, colour.B, 0);
                strokes.Add(record);
            }
        }

        void Resolved(TerrainLayer layer, int role)
        {
            foreach (var stroke in layer.ResolvedTextureStrokes)
            {
                stroke.Validate();
                var record = Record(1, role, samples.Count, stroke.Samples.Length, stroke.Bounds);
                foreach (var sample in stroke.Samples)
                    samples.Add((sample.Position.X, sample.Position.Y, sample.RadiusScale));
                var recipe = stroke.Recipe;
                var hash = Convert.FromHexString(recipe.TextureSha256);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), hash[3]);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), hash[4]);
                PutDoubles(record, 64, recipe.Radius, recipe.Hardness, recipe.Opacity, recipe.Flow);
                PutDoubles(record, 96, 80 + hash[0] % 144, 64 + hash[1] % 160, 56 + hash[2] % 168,
                    recipe.TextureScale);
                var radians = recipe.RotationDegrees * Math.PI / 180;
                PutDoubles(record, 128, stroke.TextureAnchor.X, stroke.TextureAnchor.Y,
                    Math.Cos(radians), Math.Sin(radians));
                strokes.Add(record);
            }
        }

        Legacy(background, 0);
        Resolved(background, 0);
        Legacy(foreground, 1);
        Resolved(foreground, 1);
        foreach (var stroke in foreground.ResolvedLandStrokes)
        {
            stroke.Validate();
            var record = Record(2, 1, samples.Count, stroke.Samples.Length, stroke.Bounds);
            foreach (var sample in stroke.Samples) samples.Add((sample.X, sample.Y, 1));
            var recipe = stroke.Recipe;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), recipe.Shape == LandShape.EdgedPolygon ? 0 : 1);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), stroke.Operation == LandOperation.Add ? 0 : 1);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(24), recipe.Seed);
            PutDoubles(record, 64, recipe.Radius, recipe.Roughness, recipe.CornerSmoothing, recipe.Softness);
            PutDoubles(record, 96, recipe.Radius * Math.Cos(Math.PI / 12), 0, 0, 0);
            strokes.Add(record);
        }

        var river = new List<byte[]>();
        snapshot.River?.Validate();
        if (snapshot.River is { Enabled: true } enabled)
        {
            foreach (var segment in enabled.GeometrySegments())
            {
                var record = new byte[64];
                PutDoubles(record, 0, segment.Start.X, segment.Start.Y, segment.End.X, segment.End.Y);
                PutDoubles(record, 32, segment.StartWidth, segment.EndWidth, segment.BankSoftness, 0);
                river.Add(record);
            }
        }

        var sampleBytes = new byte[samples.Count * 32];
        for (var index = 0; index < samples.Count; index++)
            PutDoubles(sampleBytes, index * 32, samples[index].X, samples[index].Y, samples[index].Scale, 0);
        return new TerrainProgram
        {
            Strokes = strokes.SelectMany(item => item).ToArray(),
            Samples = sampleBytes,
            River = river.SelectMany(item => item).ToArray(),
            StrokeCount = strokes.Count,
            SampleCount = samples.Count,
            RiverCount = river.Count
        };
    }

    public byte[] Parameters(MapProject snapshot, GpuTerrainSources sources, GpuTerrainRegion region,
        bool hasBackground, bool hasForeground, bool hasCoverage)
    {
        var background = snapshot.RequireRole(TerrainRole.Background);
        var foreground = snapshot.RequireRole(TerrainRole.Foreground);
        var bytes = new byte[112];
        PutDoubles(bytes, 0, snapshot.Width, snapshot.Height, sources.CanvasWidth, sources.CanvasHeight);
        PutDoubles(bytes, 32, background.Opacity, foreground.Opacity, Math.Tau, Math.Tau / 12);
        var drawBackground = snapshot.IsTerrainEffectivelyVisible(TerrainRole.Background) && background.Opacity > 0;
        var drawForeground = snapshot.IsTerrainEffectivelyVisible(TerrainRole.Foreground) && foreground.Opacity > 0;
        var legacyCompatibility = !hasCoverage && foreground.Strokes.Any(stroke => stroke.Kind == TerrainStrokeKind.Texture);
        PutInts(bytes, 64, drawBackground ? 1 : 0, drawForeground ? 1 : 0, hasBackground ? 1 : 0, hasForeground ? 1 : 0);
        PutInts(bytes, 80, hasCoverage ? 1 : 0, legacyCompatibility ? 1 : 0, StrokeCount, RiverCount);
        PutInts(bytes, 96, region.Left, region.Top, region.Width, region.Height);
        return bytes;
    }

    private static byte[] Record(int kind, int role, int sampleStart, int sampleCount, MapBounds bounds)
    {
        var record = new byte[160];
        PutInts(record, 0, kind, role, sampleStart, sampleCount);
        PutDoubles(record, 32, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        return record;
    }

    private static void PutDoubles(byte[] destination, int offset, double a, double b, double c, double d)
    {
        ConnectedGpuTerrainRenderer.WriteDouble(destination, offset, a);
        ConnectedGpuTerrainRenderer.WriteDouble(destination, offset + 8, b);
        ConnectedGpuTerrainRenderer.WriteDouble(destination, offset + 16, c);
        ConnectedGpuTerrainRenderer.WriteDouble(destination, offset + 24, d);
    }

    private static void PutInts(byte[] destination, int offset, int a, int b, int c, int d)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), a);
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset + 4), b);
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset + 8), c);
        BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset + 12), d);
    }
}
