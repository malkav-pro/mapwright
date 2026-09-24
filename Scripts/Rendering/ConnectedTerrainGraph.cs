using Godot;
using Mapwright.App;
using Mapwright.Domain;
using Mapwright.Export;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using InkImportService = Mapwright.Core.InkImportService;

namespace Mapwright.Rendering;

public readonly record struct ConnectedTerrainRegion(int Left, int Top, int Width, int Height);
public sealed record PreparedViewportSource(byte[] Bytes, string Sha256, int Width, int Height);
public sealed record PlacedRaster(int Width, int Height, byte[] Rgba, ImportedRasterTransform Transform);

/// <summary>How a GPU source raster reaches the canvas sampling, mirroring the CPU inputs.</summary>
public enum GpuSourceMode
{
    /// <summary>Already at canvas sampling (prepared display source).</summary>
    Canvas = 0,
    /// <summary>Whole-raster stretch (ResampleColour / ResampleCoverage).</summary>
    Stretch = 1,
    /// <summary>Placed through its map transform (SamplePlacedColour / SamplePlacedCoverage).</summary>
    Placed = 2
}

public sealed record GpuSourceLayer(string Identity, int Width, int Height, byte[] Rgba,
    GpuSourceMode Mode, ImportedRasterTransform? Transform);

/// <summary>
/// Verified immutable inputs for one canvas sampling. The identity changes whenever any
/// source, placement, recovery mode or sampling changes; the epoch changes on eviction.
/// </summary>
public sealed record GpuTerrainSources(string Identity, long CacheEpoch, int CanvasWidth, int CanvasHeight,
    GpuSourceLayer? Background, GpuSourceLayer? Foreground, GpuSourceLayer? Coverage)
{
    public long DecodedBytes => (Background?.Rgba.LongLength ?? 0) + (Foreground?.Rgba.LongLength ?? 0) +
                                (Coverage?.Rgba.LongLength ?? 0);
}

public sealed class ConnectedTerrainGraph
{
    public const int RendererVersion = 3;
    public const int ViewportTileSize = 256;

    private readonly string _projectDirectory;
    private readonly RenderResourceLedger _resourceLedger;
    private readonly object _cacheGate = new();
    private CachedInputs? _cachedInputs;
    private readonly Dictionary<string, DecodedRaster> _nativeRasterCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SampledTileKey, object> _sampledTileCache = new();
    private long _nativeDecodedBytes;
    private long _sampledTileBytes;
    private ViewportAccumulator? _viewportAccumulator;
    private GpuTerrainSources? _gpuSources;
    private long _cacheEpoch;
    public string LastViewportAccelerationMode { get; private set; } = "none";
    public ViewportSourceStages LastViewportSourceStages { get; private set; } = new();

    public void EvictDecodedCache()
    {
        lock (_cacheGate)
        {
            _cachedInputs = null;
            _nativeRasterCache.Clear();
            _nativeDecodedBytes = 0;
            _sampledTileCache.Clear();
            _sampledTileBytes = 0;
            _viewportAccumulator = null;
            _gpuSources = null;
            _cacheEpoch++;
        }
    }

    /// <summary>Changes whenever decoded/derived inputs were evicted.</summary>
    public long CacheEpoch
    {
        get { lock (_cacheGate) return _cacheEpoch; }
    }

    public long CachedDecodedBytes
    {
        get
        {
            lock (_cacheGate)
                return checked(_nativeDecodedBytes + _sampledTileBytes + (_cachedInputs?.DecodedBytes ?? 0) +
                               (_gpuSources?.DecodedBytes ?? 0) +
                               (_viewportAccumulator is null ? 0 :
                                   (long)_viewportAccumulator.Pixels.Length * 4 * sizeof(double) +
                                   _viewportAccumulator.Rgba.LongLength +
                                   _viewportAccumulator.Touched.LongLength));
        }
    }

    private sealed record CachedInputs(
        string Identity, int Width, int Height, byte[]? Background, byte[]? Foreground,
        float[]? Coverage, long DecodedBytes, bool BackgroundOpaque);

    private readonly record struct SampledTileKey(
        ProjectId ProjectId, string BlobHash, bool Coverage,
        ImportedRasterTransform Placement, int SourceWidth, int SourceHeight,
        double MapWidth, double MapHeight, int CanvasWidth, int CanvasHeight,
        int Left, int Top, int Width, int Height);

    private sealed record ViewportAccumulator(MapProject Snapshot, string InputIdentity,
        int Width, int Height, LinearPixel[] Pixels, byte[] Rgba, byte[] Touched);

    public sealed record ViewportSourceStages(
        double ReadMilliseconds = 0, double VerifyMilliseconds = 0,
        double DecodeMilliseconds = 0, double ResampleMilliseconds = 0,
        double AccumulatorMilliseconds = 0);

    public ConnectedTerrainGraph(string projectDirectory, RenderResourceLedger? resourceLedger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        _projectDirectory = Path.GetFullPath(projectDirectory);
        _resourceLedger = resourceLedger ?? RenderResourceLedger.ForCpuInspection();
    }

    /// <summary>
    /// Resolves the immutable rasters the GPU evaluator samples for this canvas. It applies
    /// the same source choice as <see cref="EvaluateRegionAtSize"/>: normalized documents
    /// place their rasters through map transforms; the prepared flattened display source is
    /// used directly when it matches; older documents stretch whole rasters. Blobs are read
    /// and SHA-verified on every cache miss; no rendered pixels are cached here.
    /// </summary>
    public GpuTerrainSources PrepareGpuSources(MapProject snapshot, int canvasWidth, int canvasHeight)
    {
        snapshot.ValidateConnectedTerrain();
        if (canvasWidth <= 0 || canvasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(canvasWidth));
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("The connected graph requires imported source provenance.");
        var backgroundLayer = snapshot.RequireRole(TerrainRole.Background);
        var foregroundLayer = snapshot.RequireRole(TerrainRole.Foreground);
        var normalized = snapshot.StorageFormatVersion == 3 &&
                         !CanUsePreparedFlattenedViewport(snapshot, canvasWidth, canvasHeight);
        var previewTransform = new ImportedRasterTransform(0, 0,
            snapshot.Width / source.PreviewWidth, snapshot.Height / source.PreviewHeight);
        (ImportedRasterTransform Transform, int Width, int Height) Placement(
            string? hash, ImportedRasterTransform fallback, int fallbackWidth, int fallbackHeight)
        {
            var reference = source.ResolvedRasterReferences.FirstOrDefault(item =>
                string.Equals(item.BlobHash, hash, StringComparison.OrdinalIgnoreCase));
            return reference is null
                ? (fallback, fallbackWidth, fallbackHeight)
                : (reference.Transform, reference.PixelWidth, reference.PixelHeight);
        }
        var backgroundHash = source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance
            ? source.PreviewBlobHash : backgroundLayer.SourceBlobHash;
        var backgroundPlacement = Placement(backgroundHash, previewTransform,
            source.PreviewWidth, source.PreviewHeight);
        var foregroundPlacement = Placement(foregroundLayer.SourceBlobHash, previewTransform,
            source.PreviewWidth, source.PreviewHeight);
        var coveragePlacement = Placement(foregroundLayer.CoverageSourceBlobHash,
            foregroundPlacement.Transform, foregroundPlacement.Width, foregroundPlacement.Height);
        var identity = string.Join('|', normalized ? "placed" : "legacy", source.RecoveryMode,
            source.PreviewBlobHash, source.DisplaySourceBlobHash, backgroundHash, backgroundPlacement,
            foregroundLayer.SourceBlobHash, foregroundPlacement, foregroundLayer.CoverageSourceBlobHash,
            coveragePlacement, canvasWidth, canvasHeight);
        LastViewportSourceStages = new ViewportSourceStages();
        LastViewportAccelerationMode = ConnectedGpuTerrainRenderer.AccelerationMode;
        lock (_cacheGate)
            if (_gpuSources is { } cached && cached.Identity == identity) return cached;

        GpuSourceLayer? background = null, foreground = null, coverage = null;
        // The GPU samples on the device; the CPU only holds each native raster, so no
        // canvas-sized CPU working set is admitted here (16K exports stay bounded).
        const long canvasBytes = 0;
        if (normalized)
        {
            GpuSourceLayer? Placed(string? hash, (ImportedRasterTransform Transform, int Width, int Height) placement)
            {
                if (hash is null) return null;
                var raster = DecodeBoundedRaster(hash, placement.Width, placement.Height, canvasBytes);
                return new GpuSourceLayer(hash, raster.Width, raster.Height, raster.Rgba,
                    GpuSourceMode.Placed, placement.Transform);
            }
            background = Placed(backgroundHash, backgroundPlacement);
            if (source.RecoveryMode == ImportRecoveryMode.EditableRecoveredTerrain)
            {
                foreground = Placed(foregroundLayer.SourceBlobHash, foregroundPlacement);
                coverage = Placed(foregroundLayer.CoverageSourceBlobHash, coveragePlacement);
            }
        }
        else
        {
            if (source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance &&
                source.DisplaySourceBlobHash is { } displayHash &&
                source.DisplaySourceWidth == canvasWidth && source.DisplaySourceHeight == canvasHeight)
            {
                try
                {
                    background = new GpuSourceLayer(displayHash, canvasWidth, canvasHeight,
                        ReadPreparedDisplaySource(displayHash, canvasWidth, canvasHeight),
                        GpuSourceMode.Canvas, null);
                }
                catch (InvalidDataException)
                {
                    // Derived presentation source; the preserved preview reconstructs it.
                }
            }
            if (background is null)
            {
                GpuSourceLayer? Stretched(string? hash)
                {
                    if (hash is null) return null;
                    var raster = DecodeVerifiedRaster(hash);
                    return new GpuSourceLayer(hash, raster.Width, raster.Height, raster.Rgba,
                        GpuSourceMode.Stretch, null);
                }
                if (source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance)
                {
                    background = Stretched(source.PreviewBlobHash)!;
                    if (background.Width != source.PreviewWidth || background.Height != source.PreviewHeight)
                        throw new InvalidDataException("Stored preview dimensions disagree with the immutable revision.");
                }
                else
                {
                    background = Stretched(backgroundLayer.SourceBlobHash);
                    foreground = Stretched(foregroundLayer.SourceBlobHash);
                    coverage = Stretched(foregroundLayer.CoverageSourceBlobHash);
                }
            }
        }
        var decodedBytes = (background?.Rgba.LongLength ?? 0) + (foreground?.Rgba.LongLength ?? 0) +
                           (coverage?.Rgba.LongLength ?? 0);
        _resourceLedger.AdmitOrThrow(0, decodedBytes, 0, decodedBytes, 0, "GPU terrain source staging");
        lock (_cacheGate)
        {
            _gpuSources = new GpuTerrainSources(identity, _cacheEpoch, canvasWidth, canvasHeight,
                background, foreground, coverage);
            return _gpuSources;
        }
    }

    public static PreparedViewportSource PrepareDisplaySource(
        byte[] previewPng, int previewWidth, int previewHeight, int maxDimension = 896)
    {
        ArgumentNullException.ThrowIfNull(previewPng);
        if (previewWidth <= 0 || previewHeight <= 0 || maxDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(previewWidth));
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(previewWidth, previewHeight));
        var width = Math.Max(1, (int)Math.Round(previewWidth * scale));
        var height = Math.Max(1, (int)Math.Round(previewHeight * scale));
        using var image = new Image();
        var error = image.LoadPngFromBuffer(previewPng);
        if (error != Error.Ok) throw new InvalidDataException($"Stored preview PNG could not be decoded: {error}.");
        image.Convert(Image.Format.Rgba8);
        if (image.GetWidth() != previewWidth || image.GetHeight() != previewHeight)
            throw new InvalidDataException("Reviewed preview dimensions changed during display source preparation.");
        var rgba = ResampleColour(new DecodedRaster(previewWidth, previewHeight,
            image.GetData(), "import-preview"), width, height);
        var pixels = checked(width * height);
        var bytes = new byte[checked(16 + pixels * 4)];
        "MWDS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), height);
        rgba.CopyTo(bytes.AsSpan(16));
        return new PreparedViewportSource(bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), width, height);
    }

    public ConnectedTerrainFrame Evaluate(MapProject snapshot)
    {
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("The connected graph requires imported source provenance.");
        return EvaluateRegion(snapshot, 0, 0, source.PreviewWidth, source.PreviewHeight);
    }

    public ConnectedTerrainFrame EvaluateRegion(
        MapProject snapshot, int regionLeft, int regionTop, int regionWidth, int regionHeight)
    {
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("The connected graph requires imported source provenance.");
        return EvaluateRegionAtSize(snapshot, source.PreviewWidth, source.PreviewHeight,
            regionLeft, regionTop, regionWidth, regionHeight);
    }

    public ConnectedTerrainFrame EvaluateRegionAtSize(
        MapProject snapshot, int canvasWidth, int canvasHeight,
        int regionLeft, int regionTop, int regionWidth, int regionHeight)
    {
        snapshot.ValidateConnectedTerrain();
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("The connected graph requires imported source provenance.");
        ValidateRegion(canvasWidth, canvasHeight,
            regionLeft, regionTop, regionWidth, regionHeight);
        if (snapshot.StorageFormatVersion == 3 &&
            !CanUsePreparedFlattenedViewport(snapshot, canvasWidth, canvasHeight))
            return EvaluateNormalizedRegion(snapshot, canvasWidth, canvasHeight,
                regionLeft, regionTop, regionWidth, regionHeight);
        var outputBytes = checked((long)regionWidth * regionHeight * 4);
        var sourceBytes = checked((long)source.PreviewWidth * source.PreviewHeight * 4);
        var worstDecodedBytes = checked((long)canvasWidth * canvasHeight * 12);
        var worstPeak = checked(sourceBytes + worstDecodedBytes + outputBytes);
        _resourceLedger.AdmitOrThrow(0, checked(sourceBytes + worstDecodedBytes), outputBytes,
            worstPeak, 0, "Connected terrain region preflight");
        var inputs = GetInputs(snapshot, canvasWidth, canvasHeight);
        _resourceLedger.AdmitOrThrow(0, inputs.DecodedBytes, outputBytes,
            checked(inputs.DecodedBytes + outputBytes), 0, "Connected terrain region evaluation");
        ViewportAccumulator? accumulator;
        lock (_cacheGate)
            accumulator = _viewportAccumulator is { } cached &&
                          cached.Snapshot.ProjectId == snapshot.ProjectId &&
                          cached.Snapshot.Revision == snapshot.Revision &&
                          cached.InputIdentity == inputs.Identity &&
                          cached.Width == canvasWidth && cached.Height == canvasHeight
                ? cached : null;
        if (accumulator is not null)
            return EncodeViewportRegion(accumulator, snapshot,
                regionLeft, regionTop, regionWidth, regionHeight);
        return RenderReference(snapshot, inputs.Width, inputs.Height,
            inputs.Background, inputs.Foreground, inputs.Coverage,
            regionLeft, regionTop, regionWidth, regionHeight);
    }

    private ConnectedTerrainFrame EvaluateNormalizedRegion(MapProject snapshot,
        int canvasWidth, int canvasHeight, int left, int top, int width, int height)
    {
        lock (_cacheGate)
        {
            _cachedInputs = null;
            _viewportAccumulator = null;
        }
        var source = snapshot.ImportedSource!;
        var background = snapshot.RequireRole(TerrainRole.Background);
        var foreground = snapshot.RequireRole(TerrainRole.Foreground);
        var tilePixels = checked((long)width * height);
        var tileBytes = checked(tilePixels * 12);
        _resourceLedger.AdmitOrThrow(0, tileBytes, checked(tilePixels * 4),
            checked(tileBytes + tilePixels * 4), 0, "Normalized terrain tile preflight");

        byte[]? SampleColour(string? hash, ImportedRasterTransform transform,
            int expectedWidth, int expectedHeight)
        {
            if (hash is null) return null;
            var key = new SampledTileKey(snapshot.ProjectId, hash, false, transform,
                expectedWidth, expectedHeight, snapshot.Width, snapshot.Height,
                canvasWidth, canvasHeight, left, top, width, height);
            if (FindSampledTile<byte>(key) is { } cached) return cached;
            var raster = DecodeBoundedRaster(hash, expectedWidth, expectedHeight, tileBytes);
            var sampled = SamplePlacedColour(snapshot, canvasWidth, canvasHeight,
                new PlacedRaster(raster.Width, raster.Height, raster.Rgba, transform),
                left, top, width, height);
            StoreSampledTile(key, sampled, sampled.LongLength);
            return sampled;
        }

        float[]? SampleCoverage(string? hash, ImportedRasterTransform transform,
            int expectedWidth, int expectedHeight)
        {
            if (hash is null) return null;
            var key = new SampledTileKey(snapshot.ProjectId, hash, true, transform,
                expectedWidth, expectedHeight, snapshot.Width, snapshot.Height,
                canvasWidth, canvasHeight, left, top, width, height);
            if (FindSampledTile<float>(key) is { } cached) return cached;
            var raster = DecodeBoundedRaster(hash, expectedWidth, expectedHeight, tileBytes);
            var sampled = SamplePlacedCoverage(snapshot, canvasWidth, canvasHeight,
                new PlacedRaster(raster.Width, raster.Height, raster.Rgba, transform),
                left, top, width, height);
            StoreSampledTile(key, sampled, checked(sampled.LongLength * sizeof(float)));
            return sampled;
        }

        (ImportedRasterTransform Transform, int Width, int Height) Placement(
            string? hash, ImportedRasterTransform fallback, int fallbackWidth, int fallbackHeight)
        {
            var reference = source.ResolvedRasterReferences.FirstOrDefault(item =>
                string.Equals(item.BlobHash, hash, StringComparison.OrdinalIgnoreCase));
            return reference is null
                ? (fallback, fallbackWidth, fallbackHeight)
                : (reference.Transform, reference.PixelWidth, reference.PixelHeight);
        }

        var previewTransform = new ImportedRasterTransform(0, 0,
            snapshot.Width / source.PreviewWidth, snapshot.Height / source.PreviewHeight);
        var backgroundHash = source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance
            ? source.PreviewBlobHash : background.SourceBlobHash;
        var bg = Placement(backgroundHash, previewTransform,
            source.PreviewWidth, source.PreviewHeight);
        var backgroundTile = SampleColour(backgroundHash, bg.Transform, bg.Width, bg.Height);
        byte[]? foregroundTile = null;
        float[]? coverageTile = null;
        if (source.RecoveryMode == ImportRecoveryMode.EditableRecoveredTerrain)
        {
            var fg = Placement(foreground.SourceBlobHash, previewTransform,
                source.PreviewWidth, source.PreviewHeight);
            foregroundTile = SampleColour(foreground.SourceBlobHash, fg.Transform, fg.Width, fg.Height);
            var mask = Placement(foreground.CoverageSourceBlobHash, fg.Transform,
                fg.Width, fg.Height);
            coverageTile = SampleCoverage(foreground.CoverageSourceBlobHash,
                mask.Transform, mask.Width, mask.Height);
        }
        _resourceLedger.AdmitOrThrow(0, tileBytes, checked(tilePixels * 4),
            checked(tileBytes + tilePixels * 4), 0, "Normalized terrain tile evaluation");
        return RenderReferenceCore(snapshot, canvasWidth, canvasHeight,
            backgroundTile, foregroundTile, coverageTile,
            left, top, width, height, left, top, width, height);
    }

    private T[]? FindSampledTile<T>(SampledTileKey key)
    {
        if (Math.Max(key.CanvasWidth, key.CanvasHeight) > 2048) return null;
        lock (_cacheGate)
            return _sampledTileCache.TryGetValue(key, out var value) ? value as T[] : null;
    }

    private void StoreSampledTile<T>(SampledTileKey key, T[] sampled, long bytes)
    {
        if (Math.Max(key.CanvasWidth, key.CanvasHeight) > 2048) return;
        const long maxSampledTileBytes = 32L * 1024 * 1024;
        lock (_cacheGate)
        {
            if (_sampledTileCache.ContainsKey(key)) return;
            if (_sampledTileBytes + bytes > maxSampledTileBytes)
            {
                _sampledTileCache.Clear();
                _sampledTileBytes = 0;
            }
            if (!_resourceLedger.Fits(0, checked(_nativeDecodedBytes + _sampledTileBytes + bytes),
                    0, checked(_nativeDecodedBytes + _sampledTileBytes + bytes), 0))
                return;
            _sampledTileCache[key] = sampled;
            _sampledTileBytes += bytes;
        }
    }

    private DecodedRaster DecodeBoundedRaster(string hash, int expectedWidth,
        int expectedHeight, long tileBytes)
    {
        var path = Path.Combine(_projectDirectory, "blobs", hash);
        if (!File.Exists(path)) throw new InvalidDataException($"Missing source blob {hash}.");
        var encodedBytes = new FileInfo(path).Length;
        var decodedBytes = checked((long)expectedWidth * expectedHeight * 4);
        lock (_cacheGate)
        {
            if (_nativeRasterCache.TryGetValue(hash, out var cached))
            {
                if (cached.Width != expectedWidth || cached.Height != expectedHeight)
                    throw new InvalidDataException("Cached raster dimensions disagree with immutable provenance.");
                if (_resourceLedger.Fits(0, checked(_nativeDecodedBytes + _sampledTileBytes + tileBytes),
                        checked(tileBytes / 3),
                        checked(_nativeDecodedBytes + _sampledTileBytes + tileBytes * 2), 0))
                    return cached;
                _nativeRasterCache.Clear();
                _nativeDecodedBytes = 0;
            }
            if (!_resourceLedger.Fits(0,
                    checked(_nativeDecodedBytes + _sampledTileBytes + encodedBytes + decodedBytes + tileBytes),
                    checked(tileBytes / 3),
                    checked(_nativeDecodedBytes + _sampledTileBytes + encodedBytes + decodedBytes + tileBytes * 2), 0))
            {
                _nativeRasterCache.Clear();
                _nativeDecodedBytes = 0;
            }
            if (!_resourceLedger.Fits(0, checked(_sampledTileBytes + encodedBytes + decodedBytes + tileBytes),
                    checked(tileBytes / 3),
                    checked(_sampledTileBytes + encodedBytes + decodedBytes + tileBytes * 2), 0))
            {
                _sampledTileCache.Clear();
                _sampledTileBytes = 0;
            }
        }
        _resourceLedger.AdmitOrThrow(0, checked(encodedBytes + decodedBytes + tileBytes),
            checked(tileBytes / 3),
            checked(encodedBytes + decodedBytes + tileBytes * 2), 0,
            "Normalized source decode preflight");
        var raster = DecodeVerifiedRaster(hash);
        if (raster.Width != expectedWidth || raster.Height != expectedHeight)
            throw new InvalidDataException("Stored raster dimensions disagree with immutable provenance.");
        lock (_cacheGate)
        {
            if (_resourceLedger.Fits(0,
                    checked(_nativeDecodedBytes + _sampledTileBytes + raster.Rgba.LongLength + tileBytes),
                    checked(tileBytes / 3),
                    checked(_nativeDecodedBytes + _sampledTileBytes + raster.Rgba.LongLength + tileBytes * 2), 0))
            {
                _nativeRasterCache[hash] = raster;
                _nativeDecodedBytes = checked(_nativeDecodedBytes + raster.Rgba.LongLength);
            }
        }
        return raster;
    }

    public static ConnectedTerrainFrame RenderPlacedReference(MapProject snapshot,
        int canvasWidth, int canvasHeight, PlacedRaster? background,
        PlacedRaster? foreground, PlacedRaster? coverage,
        int left, int top, int width, int height)
    {
        snapshot.ValidateConnectedTerrain();
        ValidateRegion(canvasWidth, canvasHeight, left, top, width, height);
        var backgroundTile = background is null ? null : SamplePlacedColour(snapshot,
            canvasWidth, canvasHeight, background, left, top, width, height);
        var foregroundTile = foreground is null ? null : SamplePlacedColour(snapshot,
            canvasWidth, canvasHeight, foreground, left, top, width, height);
        var coverageTile = coverage is null ? null : SamplePlacedCoverage(snapshot,
            canvasWidth, canvasHeight, coverage, left, top, width, height);
        return RenderReferenceCore(snapshot, canvasWidth, canvasHeight,
            backgroundTile, foregroundTile, coverageTile,
            left, top, width, height, left, top, width, height);
    }

    private static byte[] SamplePlacedColour(MapProject snapshot, int canvasWidth,
        int canvasHeight, PlacedRaster source, int left, int top, int width, int height)
    {
        ValidatePlacedRaster(source);
        var output = new byte[checked(width * height * 4)];
        var transform = new DocumentRasterTransform(snapshot.Width, snapshot.Height,
            canvasWidth, canvasHeight);
        var raster = new DecodedRaster(source.Width, source.Height, source.Rgba, "placed");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = transform.OutputPixelCenterToDocument(left + x, top + y);
            var sourceX = (point.X - source.Transform.OffsetX) / source.Transform.ScaleX;
            var sourceY = (point.Y - source.Transform.OffsetY) / source.Transform.ScaleY;
            if (sourceX < 0 || sourceY < 0 || sourceX >= source.Width || sourceY >= source.Height)
                continue;
            SampleBilinear(raster, sourceX - 0.5, sourceY - 0.5)
                .WriteStraightSrgb(output, (y * width + x) * 4);
        }
        return output;
    }

    private static float[] SamplePlacedCoverage(MapProject snapshot, int canvasWidth,
        int canvasHeight, PlacedRaster source, int left, int top, int width, int height)
    {
        ValidatePlacedRaster(source);
        var output = new float[checked(width * height)];
        var transform = new DocumentRasterTransform(snapshot.Width, snapshot.Height,
            canvasWidth, canvasHeight);
        var raster = new DecodedRaster(source.Width, source.Height, source.Rgba, "placed");
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = transform.OutputPixelCenterToDocument(left + x, top + y);
            var sourceX = (point.X - source.Transform.OffsetX) / source.Transform.ScaleX;
            var sourceY = (point.Y - source.Transform.OffsetY) / source.Transform.ScaleY;
            if (sourceX < 0 || sourceY < 0 || sourceX >= source.Width || sourceY >= source.Height)
                continue;
            output[y * width + x] = (float)SampleAlphaBilinear(raster,
                sourceX - 0.5, sourceY - 0.5);
        }
        return output;
    }

    private static void ValidatePlacedRaster(PlacedRaster raster)
    {
        raster.Transform.Validate();
        if (raster.Width <= 0 || raster.Height <= 0 ||
            raster.Rgba.Length != checked(raster.Width * raster.Height * 4))
            throw new InvalidDataException("Placed raster dimensions disagree with its RGBA bytes.");
    }

    public void PrimeViewportInputs(MapProject snapshot, int canvasWidth, int canvasHeight)
    {
        snapshot.ValidateConnectedTerrain();
        if (canvasWidth <= 0 || canvasHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasWidth));
        if (snapshot.StorageFormatVersion == 3 &&
            !CanUsePreparedFlattenedViewport(snapshot, canvasWidth, canvasHeight))
        {
            // Normalized documents sample immutable sources in bounded target tiles.
            // The legacy full-canvas resample/accumulator is unused by that path.
            LastViewportAccelerationMode = "map-unit tiles";
            LastViewportSourceStages = new ViewportSourceStages();
            return;
        }
        var source = snapshot.ImportedSource
            ?? throw new InvalidDataException("The connected graph requires imported source provenance.");
        var worstPeak = checked((long)source.PreviewWidth * source.PreviewHeight * 4 +
                                (long)canvasWidth * canvasHeight * 12);
        _resourceLedger.AdmitOrThrow(0, worstPeak, 0, worstPeak, 0,
            "Connected terrain viewport source preflight");
        LastViewportSourceStages = new ViewportSourceStages();
        var inputs = GetInputs(snapshot, canvasWidth, canvasHeight);
        var accumulatorStart = Stopwatch.GetTimestamp();
        PrimeAccumulator(snapshot, inputs);
        LastViewportSourceStages = LastViewportSourceStages with
        {
            AccumulatorMilliseconds = Stopwatch.GetElapsedTime(accumulatorStart).TotalMilliseconds
        };
    }

    private static bool CanUsePreparedFlattenedViewport(MapProject snapshot,
        int canvasWidth, int canvasHeight)
    {
        if (snapshot.StorageFormatVersion != 3 ||
            snapshot.ImportedSource is not { RecoveryMode: ImportRecoveryMode.OriginalFlattenedAppearance } source ||
            source.DisplaySourceBlobHash is null ||
            source.DisplaySourceWidth != canvasWidth ||
            source.DisplaySourceHeight != canvasHeight)
            return false;
        var reference = source.ResolvedRasterReferences.FirstOrDefault(item =>
            string.Equals(item.BlobHash, source.PreviewBlobHash, StringComparison.OrdinalIgnoreCase));
        return reference is null || reference.Transform == new ImportedRasterTransform(0, 0,
            snapshot.Width / source.PreviewWidth, snapshot.Height / source.PreviewHeight);
    }

    public static IReadOnlyList<ConnectedTerrainRegion> TilesForBounds(
        MapProject snapshot, int width, int height, MapBounds? bounds)
    {
        snapshot.ValidateConnectedTerrain();
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var left = bounds is null ? 0 : Math.Clamp((int)Math.Floor(bounds.Value.Left / snapshot.Width * width) - 2, 0, width);
        var top = bounds is null ? 0 : Math.Clamp((int)Math.Floor(bounds.Value.Top / snapshot.Height * height) - 2, 0, height);
        var right = bounds is null ? width : Math.Clamp((int)Math.Ceiling(bounds.Value.Right / snapshot.Width * width) + 2, 0, width);
        var bottom = bounds is null ? height : Math.Clamp((int)Math.Ceiling(bounds.Value.Bottom / snapshot.Height * height) + 2, 0, height);
        if (right <= left || bottom <= top) return [];
        var regions = new List<ConnectedTerrainRegion>();
        for (var y = top / ViewportTileSize * ViewportTileSize; y < bottom; y += ViewportTileSize)
        for (var x = left / ViewportTileSize * ViewportTileSize; x < right; x += ViewportTileSize)
            regions.Add(new ConnectedTerrainRegion(x, y,
                Math.Min(ViewportTileSize, width - x), Math.Min(ViewportTileSize, height - y)));
        return regions;
    }

    private CachedInputs GetInputs(MapProject snapshot, int canvasWidth, int canvasHeight)
    {
        var source = snapshot.ImportedSource!;
        var backgroundLayer = snapshot.RequireRole(TerrainRole.Background);
        var foregroundLayer = snapshot.RequireRole(TerrainRole.Foreground);
        var identity = string.Join('|', source.PreviewBlobHash, source.DisplaySourceBlobHash,
            source.RecoveryMode,
            backgroundLayer.SourceBlobHash, foregroundLayer.SourceBlobHash,
            foregroundLayer.CoverageSourceBlobHash, canvasWidth, canvasHeight);
        lock (_cacheGate)
        {
            if (_cachedInputs is { } cached && cached.Identity == identity) return cached;
            _viewportAccumulator = null;
            byte[]? background = null;
            byte[]? foreground = null;
            float[]? coverage = null;
            long transientPreviewBytes = 0;
            if (source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance &&
                source.DisplaySourceBlobHash is { } displayHash &&
                source.DisplaySourceWidth == canvasWidth && source.DisplaySourceHeight == canvasHeight)
            {
                try
                {
                    background = ReadPreparedDisplaySource(displayHash,
                        canvasWidth, canvasHeight);
                }
                catch (InvalidDataException)
                {
                    // This is a derived presentation source. The preserved preview
                    // remains authoritative and can reconstruct it after damage.
                }
            }
            if (background is null)
            {
                var preview = DecodeVerifiedRaster(source.PreviewBlobHash);
                if (preview.Width != source.PreviewWidth || preview.Height != source.PreviewHeight)
                    throw new InvalidDataException("Stored preview dimensions disagree with the immutable revision.");
                transientPreviewBytes = preview.Rgba.LongLength;
                if (source.RecoveryMode == ImportRecoveryMode.OriginalFlattenedAppearance)
                {
                    var resampleStart = Stopwatch.GetTimestamp();
                    background = ResampleColour(preview, canvasWidth, canvasHeight);
                    LastViewportSourceStages = LastViewportSourceStages with
                    {
                        ResampleMilliseconds = LastViewportSourceStages.ResampleMilliseconds +
                                               Stopwatch.GetElapsedTime(resampleStart).TotalMilliseconds
                    };
                }
                else
                {
                    background = DecodeAndResample(backgroundLayer.SourceBlobHash, canvasWidth, canvasHeight);
                    foreground = DecodeAndResample(foregroundLayer.SourceBlobHash, canvasWidth, canvasHeight);
                    coverage = DecodeCoverageAndResample(
                        foregroundLayer.CoverageSourceBlobHash, canvasWidth, canvasHeight);
                }
            }

            var pixelCount = checked((long)canvasWidth * canvasHeight);
            var decodedBytes = checked(pixelCount * (background is null ? 0 : 4) +
                                       pixelCount * (foreground is null ? 0 : 4) +
                                       pixelCount * (coverage is null ? 0 : sizeof(float)));
            _resourceLedger.AdmitOrThrow(0, checked(decodedBytes + transientPreviewBytes), 0,
                checked(decodedBytes + transientPreviewBytes), 0,
                "Connected terrain decoded source cache");
            _cachedInputs = new CachedInputs(identity, canvasWidth, canvasHeight,
                background, foreground, coverage, decodedBytes,
                background is not null && IsOpaqueRegion(background, canvasWidth,
                    0, 0, canvasWidth, canvasHeight));
            return _cachedInputs;
        }
    }

    private void PrimeAccumulator(MapProject snapshot, CachedInputs inputs)
    {
        lock (_cacheGate)
        {
            if (!CanAccumulate(snapshot, inputs))
            {
                _viewportAccumulator = null;
                LastViewportAccelerationMode = "reference-fallback";
                return;
            }
            if (_viewportAccumulator is { } current && current.InputIdentity == inputs.Identity &&
                current.Width == inputs.Width && current.Height == inputs.Height &&
                current.Snapshot.ProjectId == snapshot.ProjectId)
            {
                if (current.Snapshot.Revision == snapshot.Revision)
                {
                    LastViewportAccelerationMode = "reuse";
                    return;
                }
                if (CanAppendLegacyStroke(current.Snapshot, snapshot, out var appended))
                {
                    ApplyLegacyStroke(current.Pixels, current.Rgba, current.Touched,
                        inputs.Background!, snapshot,
                        inputs.Width, inputs.Height, appended!);
                    _viewportAccumulator = current with { Snapshot = snapshot };
                    LastViewportAccelerationMode = "append-stroke";
                    return;
                }
            }

            var bytes = checked((long)inputs.Width * inputs.Height *
                                (4 * sizeof(double) + 4 + 1));
            _resourceLedger.AdmitOrThrow(0, checked(inputs.DecodedBytes + bytes), 0,
                checked(inputs.DecodedBytes + bytes), 0, "Connected viewport linear accumulator");
            var pixels = new LinearPixel[checked(inputs.Width * inputs.Height)];
            var touched = new byte[pixels.Length];
            var encoded = (byte[])inputs.Background!.Clone();
            ApplyLegacyStrokes(pixels, encoded, touched, inputs.Background,
                snapshot, inputs.Width, inputs.Height,
                snapshot.RequireRole(TerrainRole.Foreground).Strokes);
            _viewportAccumulator = new ViewportAccumulator(snapshot, inputs.Identity,
                inputs.Width, inputs.Height, pixels, encoded, touched);
            LastViewportAccelerationMode = "rebuild";
        }
    }

    private static bool CanAccumulate(MapProject snapshot, CachedInputs inputs)
    {
        var source = snapshot.ImportedSource;
        if (source?.RecoveryMode != ImportRecoveryMode.OriginalFlattenedAppearance ||
            inputs.Background is null || !inputs.BackgroundOpaque ||
            inputs.Foreground is not null || inputs.Coverage is not null) return false;
        var background = snapshot.RequireRole(TerrainRole.Background);
        var foreground = snapshot.RequireRole(TerrainRole.Foreground);
        return snapshot.IsTerrainEffectivelyVisible(TerrainRole.Background) &&
               snapshot.IsTerrainEffectivelyVisible(TerrainRole.Foreground) &&
               background.Opacity == 1 && foreground.Opacity == 1 &&
               background.Strokes.IsEmpty && background.ResolvedTextureStrokes.IsEmpty &&
               foreground.ResolvedTextureStrokes.IsEmpty && foreground.ResolvedLandStrokes.IsEmpty &&
               foreground.Strokes.All(stroke => stroke.Kind == TerrainStrokeKind.Texture) &&
               (snapshot.River is not { Enabled: true } || foreground.Strokes.IsEmpty);
    }

    private static bool CanAppendLegacyStroke(MapProject previous, MapProject next, out PaintStroke? appended)
    {
        appended = null;
        if (next.Revision != previous.Revision + 1 || next.ProjectId != previous.ProjectId ||
            next.MapId != previous.MapId || next.ImportedSource != previous.ImportedSource ||
            next.River != previous.River || next.Width != previous.Width || next.Height != previous.Height ||
            next.Title != previous.Title || next.StorageFormatVersion != previous.StorageFormatVersion)
            return false;
        var oldBackground = previous.RequireRole(TerrainRole.Background);
        var newBackground = next.RequireRole(TerrainRole.Background);
        var oldForeground = previous.RequireRole(TerrainRole.Foreground);
        var newForeground = next.RequireRole(TerrainRole.Foreground);
        if (!oldBackground.Equals(newBackground) ||
            !(oldForeground with { Strokes = ImmutableArray<PaintStroke>.Empty })
                .Equals(newForeground with { Strokes = ImmutableArray<PaintStroke>.Empty }) ||
            newForeground.Strokes.Length != oldForeground.Strokes.Length + 1 ||
            !newForeground.Strokes.Take(oldForeground.Strokes.Length)
                .SequenceEqual(oldForeground.Strokes)) return false;
        appended = newForeground.Strokes[^1];
        return appended.Kind == TerrainStrokeKind.Texture;
    }

    private sealed record LegacyViewportStroke(PaintStroke Stroke, ConnectedPixel Colour,
        int Left, int Top, int Right, int Bottom);

    private static LegacyViewportStroke? PrepareLegacyViewportStroke(
        MapProject snapshot, int width, int height, PaintStroke stroke)
    {
        var bounds = stroke.Bounds;
        var left = Math.Clamp((int)Math.Floor(bounds.Left / snapshot.Width * width) - 1, 0, width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top / snapshot.Height * height) - 1, 0, height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right / snapshot.Width * width) + 1, 0, width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom / snapshot.Height * height) + 1, 0, height);
        return right <= left || bottom <= top ? null : new LegacyViewportStroke(stroke,
            LegacyColour(stroke.Brush.TextureHash, stroke.Brush.Seed), left, top, right, bottom);
    }

    private static void ApplyLegacyStrokes(LinearPixel[] pixels, byte[] encoded,
        byte[] touched, byte[] background, MapProject snapshot,
        int width, int height, ImmutableArray<PaintStroke> strokes)
    {
        if (strokes.IsEmpty) return;
        var work = strokes.Select(stroke => PrepareLegacyViewportStroke(snapshot, width, height, stroke))
            .Where(stroke => stroke is not null).Cast<LegacyViewportStroke>().ToArray();
        var columns = (width + ViewportTileSize - 1) / ViewportTileSize;
        var rows = (height + ViewportTileSize - 1) / ViewportTileSize;
        Parallel.For(0, checked(columns * rows), tileIndex =>
        {
            var tileX = tileIndex % columns * ViewportTileSize;
            var tileY = tileIndex / columns * ViewportTileSize;
            var coverage = new double[Math.Min(ViewportTileSize, width - tileX) *
                                      Math.Min(ViewportTileSize, height - tileY)];
            foreach (var stroke in work)
                ApplyLegacyStrokeToTile(pixels, encoded, touched, background,
                    snapshot, width, height, stroke, tileX, tileY, coverage,
                    encodeImmediately: false);
            for (var y = tileY; y < Math.Min(height, tileY + ViewportTileSize); y++)
            for (var x = tileX; x < Math.Min(width, tileX + ViewportTileSize); x++)
            {
                var index = y * width + x;
                if (touched[index] != 0) pixels[index].WriteStraightSrgb(encoded, index * 4);
            }
        });
    }

    private static void ApplyLegacyStroke(LinearPixel[] pixels, byte[] encoded,
        byte[] touched, byte[] background, MapProject snapshot,
        int width, int height, PaintStroke stroke)
    {
        var work = PrepareLegacyViewportStroke(snapshot, width, height, stroke);
        if (work is null) return;
        var firstTileX = work.Left / ViewportTileSize * ViewportTileSize;
        var firstTileY = work.Top / ViewportTileSize * ViewportTileSize;
        var columns = (work.Right - firstTileX + ViewportTileSize - 1) / ViewportTileSize;
        var rows = (work.Bottom - firstTileY + ViewportTileSize - 1) / ViewportTileSize;
        Parallel.For(0, checked(columns * rows), tileIndex =>
        {
            var tileX = firstTileX + tileIndex % columns * ViewportTileSize;
            var tileY = firstTileY + tileIndex / columns * ViewportTileSize;
            var coverage = new double[Math.Min(ViewportTileSize, width - tileX) *
                                      Math.Min(ViewportTileSize, height - tileY)];
            ApplyLegacyStrokeToTile(pixels, encoded, touched, background,
                snapshot, width, height, work, tileX, tileY, coverage,
                encodeImmediately: true);
        });
    }

    private static void ApplyLegacyStrokeToTile(LinearPixel[] pixels, byte[] encoded,
        byte[] touched, byte[] background, MapProject snapshot, int width, int height,
        LegacyViewportStroke work, int tileX, int tileY, double[] coverageBuffer,
        bool encodeImmediately)
    {
        var left = Math.Max(work.Left, tileX);
        var top = Math.Max(work.Top, tileY);
        var right = Math.Min(work.Right, tileX + ViewportTileSize);
        var bottom = Math.Min(work.Bottom, tileY + ViewportTileSize);
        if (right <= left || bottom <= top) return;
        var tileBounds = new MapBounds(
            (double)tileX / width * snapshot.Width,
            (double)tileY / height * snapshot.Height,
            (double)Math.Min(width, tileX + ViewportTileSize) / width * snapshot.Width,
            (double)Math.Min(height, tileY + ViewportTileSize) / height * snapshot.Height);
        var stroke = work.Stroke;
        var samples = stroke.Samples.Where(sample =>
            IntersectsRegion(sample, stroke.Brush.Radius, tileBounds)).ToArray();
        if (samples.Length == 0) return;
        Array.Clear(coverageBuffer);
        var tileWidth = Math.Min(ViewportTileSize, width - tileX);
        var radius = stroke.Brush.Radius;
        var radiusSquared = radius * radius;
        var hardSquared = radiusSquared * stroke.Brush.Hardness * stroke.Brush.Hardness;
        foreach (var sample in samples)
        {
            var sampleLeft = Math.Max(left,
                (int)Math.Floor((sample.X - radius) / snapshot.Width * width - 0.5) - 1);
            var sampleTop = Math.Max(top,
                (int)Math.Floor((sample.Y - radius) / snapshot.Height * height - 0.5) - 1);
            var sampleRight = Math.Min(right,
                (int)Math.Ceiling((sample.X + radius) / snapshot.Width * width - 0.5) + 2);
            var sampleBottom = Math.Min(bottom,
                (int)Math.Ceiling((sample.Y + radius) / snapshot.Height * height - 0.5) + 2);
            for (var y = sampleTop; y < sampleBottom; y++)
            for (var x = sampleLeft; x < sampleRight; x++)
            {
                var offset = (y - tileY) * tileWidth + x - tileX;
                if (coverageBuffer[offset] >= 1) continue;
                var pointX = (x + 0.5) / width * snapshot.Width;
                var pointY = (y + 0.5) / height * snapshot.Height;
                var dx = pointX - sample.X;
                var dy = pointY - sample.Y;
                if (Math.Abs(dx) > radius || Math.Abs(dy) > radius) continue;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > radiusSquared) continue;
                var dab = distanceSquared <= hardSquared ? 1 :
                    Falloff(Math.Sqrt(distanceSquared) / radius, stroke.Brush.Hardness);
                coverageBuffer[offset] += dab * (1 - coverageBuffer[offset]);
            }
        }
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var coverage = coverageBuffer[(y - tileY) * tileWidth + x - tileX];
            if (coverage <= 0) continue;
            var dab = LinearPixel.FromStraightSrgb(work.Colour.R, work.Colour.G, work.Colour.B,
                stroke.Brush.Opacity * stroke.Brush.Flow * coverage);
            var index = y * width + x;
            var basePixel = touched[index] == 0 ? ReadInput(background, index) : pixels[index];
            pixels[index] = LinearPixel.Over(dab, basePixel);
            touched[index] = 1;
            if (encodeImmediately) pixels[index].WriteStraightSrgb(encoded, index * 4);
        }
    }

    private static ConnectedTerrainFrame EncodeViewportRegion(ViewportAccumulator accumulator,
        MapProject snapshot, int left, int top, int width, int height)
    {
        var output = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            Array.Copy(accumulator.Rgba, ((top + y) * accumulator.Width + left) * 4,
                output, y * width * 4, width * 4);
        return new ConnectedTerrainFrame(snapshot.ProjectId, snapshot.Revision, width, height,
            output, left, top, accumulator.Width, accumulator.Height)
        {
            TileKey = CreateTileKey(snapshot, accumulator.Width, accumulator.Height,
                left, top, width, height)
        };
    }

    public ConnectedExportResult ExportPng(MapProject snapshot, string destination)
    {
        var output = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connected export requires a .png destination.");
        var relativeToProject = Path.GetRelativePath(_projectDirectory, output);
        if (!relativeToProject.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            relativeToProject != "..")
            throw new InvalidOperationException("Export cannot overwrite the project database or immutable blobs.");

        Directory.CreateDirectory(Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("Export destination has no parent directory."));
        var temporary = output + $".tmp-{Guid.NewGuid():N}";
        try
        {
            var frame = Evaluate(snapshot);
            using (var writer = new StreamingPngWriter(temporary, frame.Width, frame.Height))
            {
                var rowBytes = checked(frame.Width * 4);
                for (var row = 0; row < frame.Height; row++)
                    writer.WriteRgbaRow(frame.Rgba.AsSpan(row * rowBytes, rowBytes));
                writer.Complete();
            }

            var validation = PngValidator.ValidateRgba8(temporary, frame.Width, frame.Height);
            if (!validation.Passed) throw new InvalidDataException(validation.Detail);
            File.Move(temporary, output, overwrite: true);
            using var published = File.OpenRead(output);
            var hash = Convert.ToHexString(SHA256.HashData(published)).ToLowerInvariant();
            return new ConnectedExportResult(output, snapshot.ProjectId, snapshot.Revision, hash, validation, frame);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Engine-independent reference path shared by contract fixtures and the decoded Godot adapter.
    /// Inputs are full-canvas straight-alpha sRGB bytes; coverage is scalar non-colour data.
    /// Returned pixels are straight-alpha sRGB and identify the exact immutable revision supplied.
    /// </summary>
    public static ConnectedTerrainFrame RenderReference(
        MapProject snapshot,
        int canvasWidth,
        int canvasHeight,
        byte[]? backgroundRgba,
        byte[]? foregroundRgba,
        float[]? baseCoverage,
        int regionLeft,
        int regionTop,
        int regionWidth,
        int regionHeight)
    {
        snapshot.ValidateConnectedTerrain();
        ValidateRegion(canvasWidth, canvasHeight, regionLeft, regionTop, regionWidth, regionHeight);
        ValidateColourInput(backgroundRgba, canvasWidth, canvasHeight, nameof(backgroundRgba));
        ValidateColourInput(foregroundRgba, canvasWidth, canvasHeight, nameof(foregroundRgba));
        if (baseCoverage is not null && baseCoverage.Length != checked(canvasWidth * canvasHeight))
            throw new ArgumentException("Coverage input must match the full output canvas.", nameof(baseCoverage));

        return RenderReferenceCore(snapshot, canvasWidth, canvasHeight,
            backgroundRgba, foregroundRgba, baseCoverage,
            regionLeft, regionTop, regionWidth, regionHeight,
            0, 0, canvasWidth, canvasHeight);
    }

    private static ConnectedTerrainFrame RenderReferenceCore(
        MapProject snapshot, int canvasWidth, int canvasHeight,
        byte[]? backgroundRgba, byte[]? foregroundRgba, float[]? baseCoverage,
        int regionLeft, int regionTop, int regionWidth, int regionHeight,
        int inputLeft, int inputTop, int inputWidth, int inputHeight)
    {
        ValidateColourInput(backgroundRgba, inputWidth, inputHeight, nameof(backgroundRgba));
        ValidateColourInput(foregroundRgba, inputWidth, inputHeight, nameof(foregroundRgba));
        if (baseCoverage is not null && baseCoverage.Length != checked(inputWidth * inputHeight))
            throw new ArgumentException("Coverage input dimensions disagree with its region.", nameof(baseCoverage));

        var output = new byte[checked(regionWidth * regionHeight * 4)];
        var backgroundLayer = snapshot.RequireRole(TerrainRole.Background);
        var foregroundLayer = snapshot.RequireRole(TerrainRole.Foreground);
        var drawBackground = snapshot.IsTerrainEffectivelyVisible(TerrainRole.Background) &&
                             backgroundLayer.Opacity > 0;
        var drawForeground = snapshot.IsTerrainEffectivelyVisible(TerrainRole.Foreground) &&
                             foregroundLayer.Opacity > 0;
        var legacyForegroundCompatibility = baseCoverage is null &&
            foregroundLayer.Strokes.Any(stroke => stroke.Kind == TerrainStrokeKind.Texture);

        foreach (var stroke in backgroundLayer.Strokes) stroke.Validate();
        foreach (var stroke in foregroundLayer.Strokes) stroke.Validate();
        foreach (var stroke in backgroundLayer.ResolvedTextureStrokes) stroke.Validate();
        foreach (var stroke in foregroundLayer.ResolvedTextureStrokes) stroke.Validate();
        foreach (var stroke in foregroundLayer.ResolvedLandStrokes) stroke.Validate();
        snapshot.River?.Validate();

        var regionBounds = new MapBounds(
            (double)regionLeft / canvasWidth * snapshot.Width,
            (double)regionTop / canvasHeight * snapshot.Height,
            (double)(regionLeft + regionWidth) / canvasWidth * snapshot.Width,
            (double)(regionTop + regionHeight) / canvasHeight * snapshot.Height);
        var backgroundLegacy = PrepareLegacyStrokes(backgroundLayer, regionBounds);
        var foregroundLegacy = PrepareLegacyStrokes(foregroundLayer, regionBounds);
        var backgroundResolved = PrepareResolvedStrokes(backgroundLayer, regionBounds);
        var foregroundResolved = PrepareResolvedStrokes(foregroundLayer, regionBounds);

        // A flattened import is already a straight-alpha sRGB raster. Preserve
        // unaffected pixels byte-for-byte and shade only texture footprints.
        // Other compositions continue through the complete linear reference path.
        var copyFlattenedBase = backgroundRgba is not null && drawBackground &&
            backgroundLayer.Opacity == 1 && backgroundLayer.Strokes.IsEmpty &&
            backgroundLayer.ResolvedTextureStrokes.IsEmpty &&
            foregroundRgba is null && baseCoverage is null &&
            foregroundLayer.ResolvedLandStrokes.IsEmpty &&
            snapshot.River is not { Enabled: true } &&
            foregroundLayer.Strokes.All(stroke => stroke.Kind == TerrainStrokeKind.Texture) &&
            IsOpaqueRegion(backgroundRgba, inputWidth,
                regionLeft - inputLeft, regionTop - inputTop, regionWidth, regionHeight);
        MapBounds? textureBounds = null;
        if (copyFlattenedBase)
        {
            for (var row = 0; row < regionHeight; row++)
                Array.Copy(backgroundRgba!, (((regionTop + row - inputTop) * inputWidth) +
                    regionLeft - inputLeft) * 4,
                    output, row * regionWidth * 4, regionWidth * 4);
            if (drawForeground)
            {
                foreach (var plan in foregroundLegacy)
                foreach (var sample in plan.Samples)
                {
                    var footprint = MapBounds.AroundSegment(sample, sample, plan.Stroke.Brush.Radius);
                    textureBounds = textureBounds is null ? footprint : textureBounds.Value.Union(footprint);
                }
                foreach (var plan in foregroundResolved)
                foreach (var sample in plan.Samples)
                {
                    var footprint = MapBounds.AroundSegment(sample.Position, sample.Position,
                        plan.Stroke.Recipe.Radius * sample.RadiusScale);
                    textureBounds = textureBounds is null ? footprint : textureBounds.Value.Union(footprint);
                }
            }
        }

        var documentTransform = new DocumentRasterTransform(snapshot.Width, snapshot.Height,
            canvasWidth, canvasHeight);
        for (var localY = 0; localY < regionHeight; localY++)
        for (var localX = 0; localX < regionWidth; localX++)
        {
            var globalX = regionLeft + localX;
            var globalY = regionTop + localY;
            if (copyFlattenedBase && (textureBounds is null ||
                (globalX + 0.5) / canvasWidth * snapshot.Width < textureBounds.Value.Left ||
                (globalX + 0.5) / canvasWidth * snapshot.Width > textureBounds.Value.Right ||
                (globalY + 0.5) / canvasHeight * snapshot.Height < textureBounds.Value.Top ||
                (globalY + 0.5) / canvasHeight * snapshot.Height > textureBounds.Value.Bottom))
                continue;
            var canvasIndex = (globalY - inputTop) * inputWidth + globalX - inputLeft;
            var documentPoint = documentTransform.OutputPixelCenterToDocument(globalX, globalY);
            var result = LinearPixel.Transparent;

            if (drawBackground)
            {
                var layer = ReadInput(backgroundRgba, canvasIndex);
                layer = ApplyTextureStrokes(layer, backgroundLegacy, backgroundResolved, documentPoint);
                result = LinearPixel.Over(layer.WithOpacity(backgroundLayer.Opacity), result);
            }

            if (drawForeground)
            {
                var layer = ReadInput(foregroundRgba, canvasIndex);
                layer = ApplyTextureStrokes(layer, foregroundLegacy, foregroundResolved, documentPoint);
                var coverage = baseCoverage is null ? 0d : baseCoverage[canvasIndex];
                CoverageMath.RequireUnit(coverage, nameof(baseCoverage));
                if (legacyForegroundCompatibility) coverage = 1;
                foreach (var stroke in foregroundLayer.ResolvedLandStrokes)
                    coverage = CoverageMath.Apply(coverage, stroke.CoverageAt(documentPoint), stroke.Operation);
                if (snapshot.River is { Enabled: true } river)
                    coverage = CoverageMath.Apply(
                        coverage, river.SubtractionAt(documentPoint), LandOperation.Subtract);
                result = LinearPixel.Over(
                    layer.WithOpacity(coverage * foregroundLayer.Opacity), result);
            }

            result.WriteStraightSrgb(output, (localY * regionWidth + localX) * 4);
        }

        return new ConnectedTerrainFrame(snapshot.ProjectId, snapshot.Revision, regionWidth, regionHeight,
            output, regionLeft, regionTop, canvasWidth, canvasHeight)
        {
            TileKey = CreateTileKey(snapshot, canvasWidth, canvasHeight,
                regionLeft, regionTop, regionWidth, regionHeight)
        };
    }

    public static string CreateTileKey(
        MapProject snapshot,
        int canvasWidth,
        int canvasHeight,
        int regionLeft,
        int regionTop,
        int regionWidth,
        int regionHeight)
    {
        snapshot.ValidateConnectedTerrain();
        ValidateRegion(canvasWidth, canvasHeight, regionLeft, regionTop, regionWidth, regionHeight);
        var inputs = string.Join('|', snapshot.TerrainLayers.Select(layer => string.Join(':',
            layer.Role, layer.Id.Value, layer.SourceBlobHash, layer.CoverageSourceBlobHash,
            layer.Visible, layer.Solo, layer.Opacity,
            string.Join(',', layer.ResolvedTextureStrokes.Select(stroke => stroke.Id.Value)),
            string.Join(',', layer.ResolvedLandStrokes.Select(stroke => stroke.Id.Value))))) +
            $"|river:{snapshot.River?.Id.Value}:{snapshot.River?.Enabled}|renderer:{RendererVersion}";
        var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs)))
            .ToLowerInvariant();
        return FormattableString.Invariant(
            $"{snapshot.ProjectId.Value:N}/{snapshot.Revision}/{RendererVersion}/{inputHash}/{canvasWidth}x{canvasHeight}/{regionLeft},{regionTop},{regionWidth},{regionHeight}");
    }

    public static MapBounds BackwardDependencyBounds(
        MapBounds outputBounds,
        IReadOnlyList<double> sequentialSupports,
        double shadowOffsetX,
        double shadowOffsetY,
        double shadowRadius)
    {
        ArgumentNullException.ThrowIfNull(sequentialSupports);
        var support = 0d;
        foreach (var value in sequentialSupports)
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(sequentialSupports));
            support = checked(support + value);
        }
        if (!double.IsFinite(shadowOffsetX) || !double.IsFinite(shadowOffsetY) ||
            !double.IsFinite(shadowRadius) || shadowRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(shadowRadius));
        var expanded = outputBounds.Expand(support);
        var shadowInput = new MapBounds(
            outputBounds.Left - shadowOffsetX - shadowRadius,
            outputBounds.Top - shadowOffsetY - shadowRadius,
            outputBounds.Right - shadowOffsetX + shadowRadius,
            outputBounds.Bottom - shadowOffsetY + shadowRadius);
        return expanded.Union(shadowInput);
    }

    private byte[]? DecodeAndResample(string? hash, int outputWidth, int outputHeight)
    {
        if (hash is null) return null;
        var raster = DecodeVerifiedRaster(hash);
        var started = Stopwatch.GetTimestamp();
        var result = ResampleColour(raster, outputWidth, outputHeight);
        LastViewportSourceStages = LastViewportSourceStages with
        {
            ResampleMilliseconds = LastViewportSourceStages.ResampleMilliseconds +
                                   Stopwatch.GetElapsedTime(started).TotalMilliseconds
        };
        return result;
    }

    private float[]? DecodeCoverageAndResample(string? hash, int outputWidth, int outputHeight)
    {
        if (hash is null) return null;
        var raster = DecodeVerifiedRaster(hash);
        var started = Stopwatch.GetTimestamp();
        var result = ResampleCoverage(raster, outputWidth, outputHeight);
        LastViewportSourceStages = LastViewportSourceStages with
        {
            ResampleMilliseconds = LastViewportSourceStages.ResampleMilliseconds +
                                   Stopwatch.GetElapsedTime(started).TotalMilliseconds
        };
        return result;
    }

    private DecodedRaster DecodeVerifiedRaster(string hash)
    {
        var bytes = ReadVerifiedBlob(hash);
        var decodeStart = Stopwatch.GetTimestamp();
        using var image = new Image();
        var error = image.LoadPngFromBuffer(bytes);
        if (error != Error.Ok) throw new InvalidDataException($"Stored PNG could not be decoded: {error}.");
        image.Convert(Image.Format.Rgba8);
        var raster = new DecodedRaster(image.GetWidth(), image.GetHeight(), image.GetData(), hash);
        LastViewportSourceStages = LastViewportSourceStages with
        {
            DecodeMilliseconds = LastViewportSourceStages.DecodeMilliseconds +
                                 Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds
        };
        return raster;
    }

    private byte[] ReadPreparedDisplaySource(
        string hash, int width, int height)
    {
        var bytes = ReadVerifiedBlob(hash);
        var pixelCount = checked(width * height);
        if (bytes.Length != checked(16 + pixelCount * 4) ||
            !bytes.AsSpan(0, 4).SequenceEqual("MWDS"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != 2 ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) != width ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)) != height)
            throw new InvalidDataException("Immutable display source format or dimensions disagree with the revision.");
        var started = Stopwatch.GetTimestamp();
        var rgba = bytes.AsSpan(16, pixelCount * 4).ToArray();
        LastViewportSourceStages = LastViewportSourceStages with
        {
            DecodeMilliseconds = LastViewportSourceStages.DecodeMilliseconds +
                                 Stopwatch.GetElapsedTime(started).TotalMilliseconds
        };
        return rgba;
    }

    private byte[] ReadVerifiedBlob(string hash)
    {
        var path = Path.Combine(_projectDirectory, "blobs", hash);
        if (!File.Exists(path)) throw new InvalidDataException($"Missing source blob {hash}.");
        var readStart = Stopwatch.GetTimestamp();
        var bytes = File.ReadAllBytes(path);
        var verifyStart = Stopwatch.GetTimestamp();
        LastViewportSourceStages = LastViewportSourceStages with
        {
            ReadMilliseconds = LastViewportSourceStages.ReadMilliseconds +
                               Stopwatch.GetElapsedTime(readStart, verifyStart).TotalMilliseconds
        };
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Source blob hash mismatch for {hash}.");
        LastViewportSourceStages = LastViewportSourceStages with
        {
            VerifyMilliseconds = LastViewportSourceStages.VerifyMilliseconds +
                                 Stopwatch.GetElapsedTime(verifyStart).TotalMilliseconds
        };
        return bytes;
    }

    private static byte[] ResampleColour(DecodedRaster source, int outputWidth, int outputHeight)
    {
        if (source.Width == outputWidth && source.Height == outputHeight)
            return (byte[])source.Rgba.Clone();
        var result = new byte[checked(outputWidth * outputHeight * 4)];
        Parallel.For(0, outputHeight, y =>
        {
            var sampleY = ((y + 0.5) * source.Height / outputHeight) - 0.5;
            for (var x = 0; x < outputWidth; x++)
            {
                var sampleX = ((x + 0.5) * source.Width / outputWidth) - 0.5;
                SampleBilinear(source, sampleX, sampleY)
                    .WriteStraightSrgb(result, (y * outputWidth + x) * 4);
            }
        });
        return result;
    }

    private static float[] ResampleCoverage(DecodedRaster source, int outputWidth, int outputHeight)
    {
        var result = new float[checked(outputWidth * outputHeight)];
        Parallel.For(0, outputHeight, y =>
        {
            var sampleY = ((y + 0.5) * source.Height / outputHeight) - 0.5;
            for (var x = 0; x < outputWidth; x++)
            {
                var sampleX = ((x + 0.5) * source.Width / outputWidth) - 0.5;
                result[y * outputWidth + x] = (float)SampleAlphaBilinear(source, sampleX, sampleY);
            }
        });
        return result;
    }

    private static LinearPixel SampleBilinear(DecodedRaster source, double x, double y)
    {
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, source.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, source.Height - 1);
        var x1 = Math.Clamp(x0 + 1, 0, source.Width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, source.Height - 1);
        var tx = Math.Clamp(x - Math.Floor(x), 0, 1);
        var ty = Math.Clamp(y - Math.Floor(y), 0, 1);
        var top = LinearPixel.Lerp(ReadInput(source.Rgba, y0 * source.Width + x0),
            ReadInput(source.Rgba, y0 * source.Width + x1), tx);
        var bottom = LinearPixel.Lerp(ReadInput(source.Rgba, y1 * source.Width + x0),
            ReadInput(source.Rgba, y1 * source.Width + x1), tx);
        return LinearPixel.Lerp(top, bottom, ty);
    }

    private static double SampleAlphaBilinear(DecodedRaster source, double x, double y)
    {
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, source.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, source.Height - 1);
        var x1 = Math.Clamp(x0 + 1, 0, source.Width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, source.Height - 1);
        var tx = Math.Clamp(x - Math.Floor(x), 0, 1);
        var ty = Math.Clamp(y - Math.Floor(y), 0, 1);
        double Alpha(int px, int py) => source.Rgba[(py * source.Width + px) * 4 + 3] / 255d;
        return Lerp(Lerp(Alpha(x0, y0), Alpha(x1, y0), tx),
            Lerp(Alpha(x0, y1), Alpha(x1, y1), tx), ty);
    }

    private sealed record LegacyStrokePlan(PaintStroke Stroke, MapPoint[] Samples, ConnectedPixel Colour);
    private sealed record ResolvedStrokePlan(TexturePaintStroke Stroke, TextureStrokeSample[] Samples,
        byte[] Hash, double CosRotation, double SinRotation);

    private static LegacyStrokePlan[] PrepareLegacyStrokes(TerrainLayer layer, MapBounds region)
    {
        var plans = new List<LegacyStrokePlan>();
        foreach (var stroke in layer.Strokes)
        {
            if (stroke.Kind != TerrainStrokeKind.Texture) continue;
            var samples = stroke.Samples.Where(sample =>
                IntersectsRegion(sample, stroke.Brush.Radius, region)).ToArray();
            if (samples.Length > 0)
                plans.Add(new LegacyStrokePlan(stroke, samples,
                    LegacyColour(stroke.Brush.TextureHash, stroke.Brush.Seed)));
        }
        return plans.ToArray();
    }

    private static ResolvedStrokePlan[] PrepareResolvedStrokes(TerrainLayer layer, MapBounds region)
    {
        var plans = new List<ResolvedStrokePlan>();
        foreach (var stroke in layer.ResolvedTextureStrokes)
        {
            var samples = stroke.Samples.Where(sample =>
                IntersectsRegion(sample.Position, stroke.Recipe.Radius * sample.RadiusScale, region)).ToArray();
            if (samples.Length == 0) continue;
            var radians = stroke.Recipe.RotationDegrees * Math.PI / 180;
            plans.Add(new ResolvedStrokePlan(stroke, samples,
                Convert.FromHexString(stroke.Recipe.TextureSha256), Math.Cos(radians), Math.Sin(radians)));
        }
        return plans.ToArray();
    }

    private static bool IntersectsRegion(MapPoint centre, double radius, MapBounds region) =>
        centre.X + radius >= region.Left && centre.X - radius <= region.Right &&
        centre.Y + radius >= region.Top && centre.Y - radius <= region.Bottom;

    private static LinearPixel ApplyTextureStrokes(
        LinearPixel layer, LegacyStrokePlan[] legacy, ResolvedStrokePlan[] resolved, MapPoint point)
    {
        foreach (var plan in legacy)
        {
            var stroke = plan.Stroke;
            var coverage = LegacyCoverage(plan, point);
            if (coverage <= 0) continue;
            var colour = plan.Colour;
            layer = LinearPixel.Over(LinearPixel.FromStraightSrgb(
                colour.R, colour.G, colour.B,
                stroke.Brush.Opacity * stroke.Brush.Flow * coverage), layer);
        }

        foreach (var plan in resolved)
        {
            var stroke = plan.Stroke;
            var coverage = ResolvedCoverage(plan, point);
            if (coverage <= 0) continue;
            var colour = ResolvedColour(plan, point);
            layer = LinearPixel.Over(LinearPixel.FromStraightSrgb(
                colour.R, colour.G, colour.B,
                stroke.Recipe.Opacity * stroke.Recipe.Flow * coverage), layer);
        }
        return layer;
    }

    private static double LegacyCoverage(LegacyStrokePlan plan, MapPoint point)
    {
        var coverage = 0d;
        var radius = plan.Stroke.Brush.Radius;
        foreach (var sample in plan.Samples)
        {
            var dx = point.X - sample.X;
            var dy = point.Y - sample.Y;
            if (Math.Abs(dx) > radius || Math.Abs(dy) > radius) continue;
            var distance = Math.Sqrt(dx * dx + dy * dy) / radius;
            if (distance > 1) continue;
            var dab = Falloff(distance, plan.Stroke.Brush.Hardness);
            coverage += dab * (1 - coverage);
        }
        return coverage;
    }

    private static double ResolvedCoverage(ResolvedStrokePlan plan, MapPoint point)
    {
        var coverage = 0d;
        foreach (var sample in plan.Samples)
        {
            var radius = plan.Stroke.Recipe.Radius * sample.RadiusScale;
            var dx = point.X - sample.Position.X;
            var dy = point.Y - sample.Position.Y;
            if (Math.Abs(dx) > radius || Math.Abs(dy) > radius) continue;
            var distance = Math.Sqrt(dx * dx + dy * dy) / radius;
            if (distance > 1) continue;
            var dab = Falloff(distance, plan.Stroke.Recipe.Hardness);
            coverage += dab * (1 - coverage);
        }
        return coverage;
    }

    private static double Falloff(double normalizedDistance, double hardness)
    {
        if (normalizedDistance > 1) return 0;
        if (normalizedDistance <= hardness || hardness >= 0.999) return 1;
        return Math.Clamp(1 - ((normalizedDistance - hardness) / (1 - hardness)), 0, 1);
    }

    internal static ConnectedPixel LegacyColour(string textureHash, int seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{textureHash}:{seed}"));
        return new ConnectedPixel(
            (byte)(96 + hash[0] % 144),
            (byte)(72 + hash[1] % 160),
            (byte)(64 + hash[2] % 168), 255);
    }

    private static ConnectedPixel ResolvedColour(ResolvedStrokePlan plan, MapPoint point)
    {
        var stroke = plan.Stroke;
        var hash = plan.Hash;
        var localX = point.X - stroke.TextureAnchor.X;
        var localY = point.Y - stroke.TextureAnchor.Y;
        var rotatedX = (localX * plan.CosRotation) + (localY * plan.SinRotation);
        var rotatedY = (-localX * plan.SinRotation) + (localY * plan.CosRotation);
        var wave = Math.Sin((rotatedX / stroke.Recipe.TextureScale) * 0.071 + hash[3]) *
                   Math.Cos((rotatedY / stroke.Recipe.TextureScale) * 0.053 + hash[4]);
        var variation = (int)Math.Round(wave * 14);
        return new ConnectedPixel(
            ClampByte(80 + hash[0] % 144 + variation),
            ClampByte(64 + hash[1] % 160 + variation),
            ClampByte(56 + hash[2] % 168 + variation), 255);
    }

    private static LinearPixel ReadInput(byte[]? rgba, int pixelIndex)
    {
        if (rgba is null) return LinearPixel.Transparent;
        var offset = pixelIndex * 4;
        return LinearPixel.FromStraightSrgb(
            rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3] / 255d);
    }

    private static bool IsOpaqueRegion(byte[] rgba, int canvasWidth,
        int regionLeft, int regionTop, int regionWidth, int regionHeight)
    {
        for (var y = regionTop; y < regionTop + regionHeight; y++)
        for (var x = regionLeft; x < regionLeft + regionWidth; x++)
            if (rgba[(y * canvasWidth + x) * 4 + 3] != 255) return false;
        return true;
    }

    private static void ValidateColourInput(byte[]? rgba, int width, int height, string name)
    {
        if (rgba is not null && rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Colour input must be full-canvas RGBA8.", name);
    }

    private static void ValidateRegion(
        int canvasWidth,
        int canvasHeight,
        int regionLeft,
        int regionTop,
        int regionWidth,
        int regionHeight)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0 || regionWidth <= 0 || regionHeight <= 0 ||
            regionLeft < 0 || regionTop < 0 ||
            checked(regionLeft + regionWidth) > canvasWidth ||
            checked(regionTop + regionHeight) > canvasHeight)
            throw new ArgumentOutOfRangeException(nameof(regionWidth), "Render region must fit the canvas.");
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private sealed record DecodedRaster(int Width, int Height, byte[] Rgba, string Identity);

    private readonly record struct LinearPixel(double R, double G, double B, double A)
    {
        private static readonly double[] DecodeTable = Enumerable.Range(0, 256)
            .Select(value => DecodeExact(value / 255d)).ToArray();
        private static readonly double[] EncodeThresholds = Enumerable.Range(0, 255)
            .Select(value => DecodeExact((value + 0.5) / 255d)).ToArray();

        public static LinearPixel Transparent => default;

        public static LinearPixel FromStraightSrgb(byte red, byte green, byte blue, double alpha)
        {
            var a = Math.Clamp(alpha, 0, 1);
            return new LinearPixel(Decode(red) * a, Decode(green) * a, Decode(blue) * a, a);
        }

        public LinearPixel WithOpacity(double opacity)
        {
            var factor = Math.Clamp(opacity, 0, 1);
            return new LinearPixel(R * factor, G * factor, B * factor, A * factor);
        }

        public static LinearPixel Over(LinearPixel source, LinearPixel destination)
        {
            var retained = 1 - source.A;
            return new LinearPixel(
                source.R + destination.R * retained,
                source.G + destination.G * retained,
                source.B + destination.B * retained,
                source.A + destination.A * retained);
        }

        public static LinearPixel Lerp(LinearPixel from, LinearPixel to, double amount) => new(
            ConnectedTerrainGraph.Lerp(from.R, to.R, amount),
            ConnectedTerrainGraph.Lerp(from.G, to.G, amount),
            ConnectedTerrainGraph.Lerp(from.B, to.B, amount),
            ConnectedTerrainGraph.Lerp(from.A, to.A, amount));

        public void WriteStraightSrgb(byte[] destination, int offset)
        {
            if (A <= 0)
            {
                destination.AsSpan(offset, 4).Clear();
                return;
            }
            destination[offset] = Encode(R / A);
            destination[offset + 1] = Encode(G / A);
            destination[offset + 2] = Encode(B / A);
            destination[offset + 3] = (byte)Math.Clamp((int)Math.Round(A * 255), 0, 255);
        }

        private static double Decode(byte value) => DecodeTable[value];

        private static double DecodeExact(double encoded)
        {
            return encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
        }

        private static byte Encode(double value)
        {
            var linear = Math.Clamp(value, 0, 1);
            // Inverse sRGB is monotonic. The 8-bit rounding boundaries are fixed,
            // so a binary search avoids three Math.Pow calls per touched pixel.
            var lower = 0;
            var upper = EncodeThresholds.Length;
            while (lower < upper)
            {
                var middle = (lower + upper) >> 1;
                if (linear < EncodeThresholds[middle] ||
                    (linear == EncodeThresholds[middle] && (middle & 1) == 0))
                    upper = middle;
                else
                    lower = middle + 1;
            }
            return (byte)lower;
        }
    }
}

public sealed record ConnectedTerrainFrame(
    ProjectId ProjectId,
    long Revision,
    int Width,
    int Height,
    byte[] Rgba,
    int OriginX = 0,
    int OriginY = 0,
    int? FullWidth = null,
    int? FullHeight = null)
{
    public string TileKey { get; init; } = string.Empty;
    public string RgbaSha256 => Convert.ToHexString(SHA256.HashData(Rgba)).ToLowerInvariant();

    public ConnectedPixel SampleAtDocument(double documentX, double documentY, double documentWidth, double documentHeight)
    {
        var canvasWidth = FullWidth ?? Width;
        var canvasHeight = FullHeight ?? Height;
        var globalX = Math.Clamp((int)Math.Floor(documentX / documentWidth * canvasWidth), 0, canvasWidth - 1);
        var globalY = Math.Clamp((int)Math.Floor(documentY / documentHeight * canvasHeight), 0, canvasHeight - 1);
        var x = Math.Clamp(globalX - OriginX, 0, Width - 1);
        var y = Math.Clamp(globalY - OriginY, 0, Height - 1);
        var offset = (y * Width + x) * 4;
        return new ConnectedPixel(Rgba[offset], Rgba[offset + 1], Rgba[offset + 2], Rgba[offset + 3]);
    }
}

public readonly record struct ConnectedPixel(byte R, byte G, byte B, byte A);

public sealed record ConnectedExportResult(
    string Destination,
    ProjectId ProjectId,
    long Revision,
    string PngSha256,
    PngValidationResult Validation,
    ConnectedTerrainFrame Frame);

public sealed class RenderReferenceConnectedCaseProvider : IConnectedCaseProvider
{
    public void Register(ConnectedCaseRegistry registry) =>
        registry.Register("RenderReference", RenderReferenceConnectedCase.RunAsync);
}

public static class RenderReferenceConnectedCase
{
    private const string FixtureSha256 = "67b69858634b1a2eee8082efd80043e915dcebb36e29c8dc66e42c6406e54fab";

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            assertions++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fixture = System.Environment.GetEnvironmentVariable("MAPWRIGHT_INK_FIXTURE")
            ?? throw new InvalidOperationException("MAPWRIGHT_INK_FIXTURE is required.");
        await using (var source = File.OpenRead(fixture))
            Check(Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken))
                    .Equals(FixtureSha256, StringComparison.OrdinalIgnoreCase),
                "Real import fixture identity changed.");

        var imported = new InkImportService().Import(fixture);
        Check(imported.PreviewWidth == 3780 && imported.PreviewHeight == 4097,
            "Real import preview dimensions changed.");
        var mask = imported.Rasters.Single(raster =>
            raster.LayerId == "layer-fg" && raster.Role == "mask");
        using (var maskImage = new Image())
        {
            Check(maskImage.LoadPngFromBuffer(mask.PngBytes) == Error.Ok,
                "Real imported coverage could not be decoded.");
            maskImage.Convert(Image.Format.Rgba8);
            var bytes = maskImage.GetData();
            var hasSoftCoverage = false;
            for (var offset = 3; offset < bytes.Length; offset += 4)
            {
                if (bytes[offset] is > 0 and < 255)
                {
                    hasSoftCoverage = true;
                    break;
                }
            }
            Check(hasSoftCoverage, "Real imported Foreground mask lost soft scalar coverage.");
        }

        Check(CoastStylePolicy.SelectedBranch == CoastStyleBranch.UnstyledGeneratedEdge,
            "Q5 did not select the authorized D-20 fallback.");
        Check(!CoastStylePolicy.DistanceFieldActive,
            "Inactive coast distance checks must remain N/A rather than passed.");

        const int width = 64;
        const int height = 64;
        var solid = Solid(width, height, 220, 180, 100, 255);
        var coverage = new float[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            coverage[y * width + x] = x < width / 2
                ? Math.Clamp((width / 2f + 0.5f - x) / 3f, 0f, 1f)
                : 0f;
        for (var y = 24; y < 40; y++)
        for (var x = 10; x < 20; x++)
            coverage[y * width + x] = 0f;

        var project = Fixture(width, height);
        var foreground = project.RequireRole(TerrainRole.Foreground);
        project = project with
        {
            River = River.Create(RiverId.New(), foreground.Id,
                [new MapPoint(8, 32), new MapPoint(32, 32)], [4d, 4d], bankSoftness: 0.5)
        };
        var whole = ConnectedTerrainGraph.RenderReference(project, width, height, null, solid, coverage,
            0, 0, width, height).Rgba;
        Check(OnlyBaseColour(whole, 220, 180, 100),
            "Open coast, lake-like cut, inland river bank or river mouth received decorative colour.");

        var stitched = new byte[whole.Length];
        for (var tileX = 0; tileX < width; tileX += 16)
        {
            var tile = ConnectedTerrainGraph.RenderReference(project, width, height, null, solid, coverage,
                tileX, 0, 16, height).Rgba;
            for (var row = 0; row < height; row++)
                tile.AsSpan(row * 16 * 4, 16 * 4)
                    .CopyTo(stitched.AsSpan((row * width + tileX) * 4));
        }
        Check(whole.SequenceEqual(stitched),
            "Whole and tiled synthetic coast/river references differ at a tile crossing.");
        Check(HasSoftAlpha(whole),
            "Synthetic fixture did not retain soft coverage or bank softness.");

        var accumulatorRoot = Path.Combine(Path.GetTempPath(), $"mapwright-viewport-{Guid.NewGuid():N}");
        try
        {
            var blobRoot = Path.Combine(accumulatorRoot, "blobs");
            Directory.CreateDirectory(blobRoot);
            var previewPath = Path.Combine(accumulatorRoot, "preview.png");
            var opaque = Solid(width, height, 35, 52, 71, 255);
            using (var writer = new StreamingPngWriter(previewPath, width, height))
            {
                for (var row = 0; row < height; row++)
                    writer.WriteRgbaRow(opaque.AsSpan(row * width * 4, width * 4));
                writer.Complete();
            }
            var previewBytes = await File.ReadAllBytesAsync(previewPath, cancellationToken);
            var previewHash = Convert.ToHexString(SHA256.HashData(previewBytes)).ToLowerInvariant();
            File.Move(previewPath, Path.Combine(blobRoot, previewHash));
            var importedSource = new ImportedSourceReference("synthetic.ink",
                new string('a', 64), new string('a', 64), previewHash,
                width, height, ImportRecoveryMode.OriginalFlattenedAppearance);
            var accumulated = Fixture(width, height) with { Revision = 0, ImportedSource = importedSource };
            var graph = new ConnectedTerrainGraph(accumulatorRoot);
            graph.PrimeViewportInputs(accumulated, width, height);
            var emptyRiver = accumulated with
            {
                Revision = accumulated.Revision + 1,
                River = River.Create(RiverId.New(),
                    accumulated.RequireRole(TerrainRole.Foreground).Id,
                    [new MapPoint(8, 32), new MapPoint(32, 32)],
                    [4d, 4d], bankSoftness: 0.5)
            };
            graph.PrimeViewportInputs(emptyRiver, width, height);
            Check(graph.LastViewportAccelerationMode == "rebuild",
                "A river over empty flattened Foreground did not use base-preserving reconstruction.");
            var emptyRiverPixels = graph.EvaluateRegionAtSize(emptyRiver, width, height,
                0, 0, width, height).Rgba;
            var emptyRiverReference = ConnectedTerrainGraph.RenderReference(emptyRiver,
                width, height, opaque, null, null, 0, 0, width, height).Rgba;
            Check(emptyRiverPixels.Zip(emptyRiverReference)
                    .All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "River over empty Foreground changed flattened base pixels.");
            graph.PrimeViewportInputs(accumulated, width, height);
            for (var index = 0; index < 16; index++)
            {
                var target = accumulated.RequireRole(TerrainRole.Foreground);
                var stroke = new PaintStroke(StrokeId.New(),
                    [new MapPoint(28 + index % 3 * 3, 32),
                     new MapPoint(32 + index % 3 * 2, 32)],
                    new ResolvedBrush(new string('c', 64), 7, 0.7, 0.8, 0.75,
                        0.2, 0, index + 11, 1), false, TerrainStrokeKind.Texture);
                accumulated = accumulated with
                {
                    Revision = accumulated.Revision + 1,
                    TerrainLayers = accumulated.TerrainLayers.SetItem(1,
                        target with { Strokes = target.Strokes.Add(stroke) })
                };
                graph.PrimeViewportInputs(accumulated, width, height);
                Check(graph.LastViewportAccelerationMode == "append-stroke",
                    "Adjacent texture edit did not use the revision-stamped append path.");
                var actual = graph.EvaluateRegionAtSize(accumulated, width, height,
                    0, 0, width, height).Rgba;
                var expected = ConnectedTerrainGraph.RenderReference(accumulated, width, height,
                    opaque, null, null, 0, 0, width, height).Rgba;
                Check(actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                    "Accumulated texture pixels differ from a fresh full reference by over one channel.");
            }
            graph.EvictDecodedCache();
            graph.PrimeViewportInputs(accumulated, width, height);
            Check(graph.LastViewportAccelerationMode == "rebuild",
                "Evicted viewport acceleration was silently retained.");
            var rebuilt = graph.EvaluateRegionAtSize(accumulated, width, height,
                0, 0, width, height).Rgba;
            var fresh = ConnectedTerrainGraph.RenderReference(accumulated, width, height,
                opaque, null, null, 0, 0, width, height).Rgba;
            Check(rebuilt.Zip(fresh).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "Evicted reconstruction diverged from the authoritative full reference.");

            var gap = accumulated with { Revision = accumulated.Revision + 2 };
            graph.PrimeViewportInputs(gap, width, height);
            Check(graph.LastViewportAccelerationMode == "rebuild",
                "A revision gap incorrectly reused the append-only viewport state.");
            var gapPixels = graph.EvaluateRegionAtSize(gap, width, height,
                0, 0, width, height).Rgba;
            Check(gapPixels.Zip(fresh).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "Revision-gap rebuild diverged from the authoritative full reference.");

            var translucent = (byte[])opaque.Clone();
            translucent[3] = 128;
            var translucentPath = Path.Combine(accumulatorRoot, "translucent.png");
            using (var writer = new StreamingPngWriter(translucentPath, width, height))
            {
                for (var row = 0; row < height; row++)
                    writer.WriteRgbaRow(translucent.AsSpan(row * width * 4, width * 4));
                writer.Complete();
            }
            var translucentBytes = await File.ReadAllBytesAsync(translucentPath, cancellationToken);
            var translucentHash = Convert.ToHexString(SHA256.HashData(translucentBytes)).ToLowerInvariant();
            File.Move(translucentPath, Path.Combine(blobRoot, translucentHash));
            var translucentSnapshot = gap with
            {
                ImportedSource = importedSource with { PreviewBlobHash = translucentHash }
            };
            graph.PrimeViewportInputs(translucentSnapshot, width, height);
            Check(graph.LastViewportAccelerationMode == "reference-fallback",
                "A translucent flattened base incorrectly entered opaque-only accumulation.");
            var fallbackPixels = graph.EvaluateRegionAtSize(translucentSnapshot, width, height,
                0, 0, width, height).Rgba;
            var fallbackReference = ConnectedTerrainGraph.RenderReference(translucentSnapshot,
                width, height, translucent, null, null, 0, 0, width, height).Rgba;
            Check(fallbackPixels.Zip(fallbackReference)
                    .All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "Translucent fallback diverged from the authoritative full reference.");

            var preparedSource = ConnectedTerrainGraph.PrepareDisplaySource(previewBytes,
                width, height);
            var preparedPath = Path.Combine(blobRoot, preparedSource.Sha256);
            await File.WriteAllBytesAsync(preparedPath, preparedSource.Bytes, cancellationToken);
            var preparedSnapshot = gap with
            {
                ImportedSource = importedSource with
                {
                    DisplaySourceBlobHash = preparedSource.Sha256,
                    DisplaySourceWidth = preparedSource.Width,
                    DisplaySourceHeight = preparedSource.Height
                }
            };
            graph.EvictDecodedCache();
            graph.PrimeViewportInputs(preparedSnapshot, width, height);
            Check(graph.LastViewportAccelerationMode == "rebuild" &&
                  graph.LastViewportSourceStages.DecodeMilliseconds < 100,
                "A prepared immutable display source did not replace preview PNG decoding.");
            var preparedPixels = graph.EvaluateRegionAtSize(preparedSnapshot, width, height,
                0, 0, width, height).Rgba;
            Check(preparedPixels.Zip(fresh).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "Prepared display source diverged from the full reference.");
            var normalized = MapProject.CreateNormalizedFromSource(ProjectId.New(), MapId.New(),
                "normalized viewport", width, height, 1024, Fixture(width, height).TerrainLayers,
                preparedSnapshot.ImportedSource!);
            var normalizedForeground = normalized.RequireRole(TerrainRole.Foreground);
            var normalizedStroke = new PaintStroke(StrokeId.New(),
                [new MapPoint(500, 500)],
                new ResolvedBrush(new string('c', 64), 96, 0.7, 0.8, 0.75,
                    0.2, 0, 97, 1), false, TerrainStrokeKind.Texture);
            normalized = normalized with
            {
                Revision = 1,
                TerrainLayers = normalized.TerrainLayers.SetItem(1,
                    normalizedForeground with { Strokes = [normalizedStroke] })
            };
            graph.EvictDecodedCache();
            graph.PrimeViewportInputs(normalized, width, height);
            Check(graph.LastViewportAccelerationMode == "rebuild",
                "Normalized flattened viewport did not use the prepared full-map source.");
            var normalizedPixels = graph.EvaluateRegionAtSize(normalized, width, height,
                0, 0, width, height).Rgba;
            var normalizedReference = ConnectedTerrainGraph.RenderReference(normalized,
                width, height, opaque, null, null, 0, 0, width, height).Rgba;
            Check(normalizedPixels.Zip(normalizedReference)
                    .All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "Normalized prepared viewport diverged from the map-unit reference.");
            graph.EvictDecodedCache();
            var tampered = (byte[])preparedSource.Bytes.Clone();
            tampered[^1] ^= 1;
            await File.WriteAllBytesAsync(preparedPath, tampered, cancellationToken);
            graph.PrimeViewportInputs(preparedSnapshot, width, height);
            var recoveredPixels = graph.EvaluateRegionAtSize(preparedSnapshot, width, height,
                0, 0, width, height).Rgba;
            Check(recoveredPixels.Zip(fresh).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                "A tampered display source did not reconstruct from the preserved preview.");
        }
        finally
        {
            if (Directory.Exists(accumulatorRoot)) Directory.Delete(accumulatorRoot, recursive: true);
        }

        // Vulkan's fixed VkPhysicalDeviceMemoryProperties layout: 32 memory types,
        // then heap count at byte 260 and 16-byte-aligned heap records at byte 264.
        var nativeMemoryFacts = new byte[520];
        BitConverter.TryWriteBytes(nativeMemoryFacts.AsSpan(260, 4), 2);
        BitConverter.TryWriteBytes(nativeMemoryFacts.AsSpan(264, 8), 16UL * (ulong)RenderResourceLedger.Gibibyte);
        BitConverter.TryWriteBytes(nativeMemoryFacts.AsSpan(272, 4), 1);
        BitConverter.TryWriteBytes(nativeMemoryFacts.AsSpan(280, 8), 32UL * (ulong)RenderResourceLedger.Gibibyte);
        Check(RenderResourceLedger.ParseVulkanLocalMemoryBytes(nativeMemoryFacts) ==
              16L * RenderResourceLedger.Gibibyte,
            "Vulkan capacity parser counted shared memory as local VRAM or missed the selected heap.");
        BitConverter.TryWriteBytes(nativeMemoryFacts.AsSpan(260, 4), 17);
        Check(RenderResourceLedger.ParseVulkanLocalMemoryBytes(nativeMemoryFacts) == 0,
            "Vulkan capacity parser accepted an invalid heap count.");

        var hardware = RenderResourceLedger.CaptureStartup();
        Check(hardware.ReportedSystemRamBytes > 0, "Startup did not report physical system RAM.");
        Check(hardware.Status == RenderHardwareStatus.Ready ||
              hardware.Status == RenderHardwareStatus.InspectionOnly,
            "Startup hardware status is invalid.");
        var measuredGpuBudget = 0L;
        var measuredProcessBudget = hardware.ReportedSystemRamBytes / 4;
        if (hardware.Status == RenderHardwareStatus.Ready)
        {
            var measured = RenderResourceLedger.FromSnapshot(hardware);
            measuredGpuBudget = measured.GpuBudgetBytes;
            Check(measured.Fits(0, 0, 0, 0, 0),
                "Measured hardware ledger rejected an empty working set.");
        }
        else
        {
            Check(hardware.Detail.Contains("inspection/recovery", StringComparison.OrdinalIgnoreCase),
                "Device absence did not produce the required inspection/recovery outcome.");
        }

        var synthetic = new RenderResourceLedger(
            16L * RenderResourceLedger.Gibibyte,
            32L * RenderResourceLedger.Gibibyte,
            512L * RenderResourceLedger.Mebibyte);
        Check(synthetic.Fits(synthetic.GpuBudgetBytes, synthetic.DecodedCpuBudgetBytes,
            synthetic.ExportBufferBudgetBytes, synthetic.ProcessBudgetBytes,
            synthetic.HistoryAccelerationBudgetBytes), "Exact resource caps were rejected.");
        Check(!synthetic.Fits(synthetic.GpuBudgetBytes + 1, 0, 0, 0, 0),
            "GPU cap + 1 was admitted.");
        Check(!synthetic.Fits(0, synthetic.DecodedCpuBudgetBytes + 1, 0, 0, 0),
            "Decoded CPU cap + 1 was admitted.");
        Check(!synthetic.Fits(0, 0, synthetic.ExportBufferBudgetBytes + 1, 0, 0),
            "Export/band cap + 1 was admitted.");
        Check(!synthetic.Fits(0, 0, 0, synthetic.ProcessBudgetBytes + 1, 0),
            "Process cap + 1 was admitted.");
        Check(!synthetic.Fits(0, 0, 0, 0, synthetic.HistoryAccelerationBudgetBytes + 1),
            "History cap + 1 was admitted.");
        Check(synthetic.FitBandHeight(16384, 8191, 1, 4, 1) == 8190,
            "Band/halo working set was not reduced before allocation.");

        GD.Print($"Q5 fixture_sha256={FixtureSha256} branch={CoastStylePolicy.SelectedBranch} " +
                 $"distance_check=N/A reported_vram_bytes={hardware.ReportedVramBytes} " +
                 $"system_ram_bytes={hardware.ReportedSystemRamBytes} " +
                 $"headroom_bytes={hardware.EngineCompositorHeadroomBytes} " +
                 $"gpu_budget_bytes={measuredGpuBudget} process_budget_bytes={measuredProcessBudget} " +
                 $"hardware_status={hardware.Status} max_channel_difference=0");
        return assertions;
    }

    private static MapProject Fixture(int width, int height)
    {
        var background = new TerrainLayer(LayerId.New(), "Background", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Background);
        var foreground = new TerrainLayer(LayerId.New(), "Foreground", true, false, 1,
            new CoastlineStyle(0), ImmutableArray<PaintStroke>.Empty, TerrainRole.Foreground);
        return new MapProject(ProjectId.New(), MapId.New(), "Q5 render reference", width, height, 1,
            [background, foreground]);
    }

    private static byte[] Solid(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var result = new byte[checked(width * height * 4)];
        for (var offset = 0; offset < result.Length; offset += 4)
        {
            result[offset] = red;
            result[offset + 1] = green;
            result[offset + 2] = blue;
            result[offset + 3] = alpha;
        }
        return result;
    }

    private static bool OnlyBaseColour(byte[] rgba, byte red, byte green, byte blue)
    {
        for (var offset = 0; offset < rgba.Length; offset += 4)
        {
            if (rgba[offset + 3] == 0) continue;
            if (rgba[offset] != red || rgba[offset + 1] != green || rgba[offset + 2] != blue)
                return false;
        }
        return true;
    }

    private static bool HasSoftAlpha(byte[] rgba)
    {
        for (var offset = 3; offset < rgba.Length; offset += 4)
            if (rgba[offset] is > 0 and < 255)
                return true;
        return false;
    }
}
