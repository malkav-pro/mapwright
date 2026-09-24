using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mapwright.Application;

namespace Mapwright.Core;

public sealed class InkImportService
{
    private const string PngDataPrefix = "data:image/png;base64,";
    private readonly ImportBounds _bounds;
    private long _retainedRasterBytes;

    public InkImportService(ImportBounds? bounds = null)
    {
        _bounds = bounds ?? ImportBounds.Default;
        _bounds.Validate();
    }

    public InkImportResult Import(string sourcePath)
    {
        try
        {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists) throw new FileNotFoundException("The import source is unavailable.", sourcePath);
        if (sourceInfo.Length > _bounds.MaximumCompressedBytes)
            throw Reject("compressed-size", $"The .ink file is {sourceInfo.Length} bytes; the configured compressed limit is {_bounds.MaximumCompressedBytes} bytes.");
        _retainedRasterBytes = 0;
        using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sourceHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        source.Position = 0;
        using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: false);
        using var expanded = new BoundedReadStream(gzip, _bounds.MaximumExpandedBytes);
        using var tokenizer = new StreamingJsonTokenizer(expanded, _bounds);
        var payload = ParseRoot(tokenizer);
        if (!double.IsFinite(payload.Width) || !double.IsFinite(payload.Height) ||
            payload.Width <= 0 || payload.Height <= 0 ||
            payload.Width > _bounds.MaximumImageDimension ||
            payload.Height > _bounds.MaximumImageDimension ||
            payload.Width * payload.Height > _bounds.MaximumImagePixels)
            throw Reject("scene-dimensions", "The source scene dimensions are invalid or exceed the import pixel budget.");
        if (payload.Preview is { } preview &&
            ((payload.PreviewWidth > 0 && payload.PreviewWidth != preview.Width) ||
             (payload.PreviewHeight > 0 && payload.PreviewHeight != preview.Height)))
            throw Reject("preview-dimensions", "Preview metadata disagrees with its intrinsic PNG dimensions.");
        payload.UnsupportedMetadata.UnionWith(tokenizer.UnsupportedMetadata);
        var peakJsonTokenBufferBytes = tokenizer.PeakBufferBytes;

        var unresolvedAssets = payload.History.AssetIds;
        unresolvedAssets.UnionWith(payload.CachedAssetIds);
        var map = new MapDocument
        {
            Title = payload.Title ?? Path.GetFileNameWithoutExtension(sourcePath),
            Width = payload.Width,
            Height = payload.Height,
            Import = new ImportProvenance
            {
                SourceFormat = "inkarnate-ink-v3",
                SourceFileName = Path.GetFileName(sourcePath),
                SourceSha256 = sourceHash,
                SourceVersion = payload.Version,
                CommandCounts = payload.History.CommandCounts,
                EntityCounts = payload.History.EntityCounts,
                UnresolvedAssetIds = unresolvedAssets.Order().ToList(),
                SourceCommands = payload.History.CommandSequence
                    .Select((commandType, sourceOrder) =>
                        new ImportedCommandDescriptor(sourceOrder, commandType, false))
                    .ToList(),
                UnsupportedMetadata = payload.UnsupportedMetadata.Order(StringComparer.Ordinal).ToList(),
                TrustedReplayCommandCount = 0,
                Warnings =
                [
                    "Inkarnate library images are referenced by numeric id and are not embedded.",
                    "The original backup is retained immutably; command counts are indexed as provenance, but command payloads are not yet converted into native history.",
                    "Raster checkpoints have a native resolution and cannot invent detail above that resolution."
                ]
            }
        };

        return new InkImportResult
        {
            SourcePath = Path.GetFullPath(sourcePath),
            Document = map,
            Rasters = payload.Rasters.Select(raster => raster with
            {
                Transform = raster.Transform ?? new ImportedRasterTransform(
                    0,
                    0,
                    payload.Width / raster.Width,
                    payload.Height / raster.Height)
            }).ToArray(),
            PreviewPng = payload.Preview?.Bytes,
            PreviewWidth = payload.PreviewWidth > 0 ? payload.PreviewWidth : payload.Preview?.Width ?? 0,
            PreviewHeight = payload.PreviewHeight > 0 ? payload.PreviewHeight : payload.Preview?.Height ?? 0,
            PeakJsonTokenBufferBytes = peakJsonTokenBufferBytes,
            RetainedRasterBytes = payload.Rasters.Sum(raster => (long)raster.PngBytes.Length) +
                                  (payload.Preview?.Bytes.LongLength ?? 0),
            Report = BuildReport(map, payload.Rasters,
                payload.PreviewWidth > 0 ? payload.PreviewWidth : payload.Preview?.Width ?? 0,
                payload.PreviewHeight > 0 ? payload.PreviewHeight : payload.Preview?.Height ?? 0)
        };
        }
        catch (ImportRejectedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or
                                           OverflowException or FormatException or ArgumentOutOfRangeException)
        {
            throw Reject("malformed-source",
                $"The .ink could not be read safely ({exception.Message}). Nothing was written and the source is untouched.",
                exception);
        }
    }

    private ParsedInk ParseRoot(StreamingJsonTokenizer json)
    {
        Expect(json.Next(), JsonTokenType.StartObject);
        var result = new ParsedInk();
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) return result;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            switch (token.Text)
            {
                case "version": result.Version = value.Int32; break;
                case "title": result.Title = value.Text; break;
                case "scene" when value.Type == JsonTokenType.StartObject: ParseScene(json, result); break;
                case "history" when value.Type == JsonTokenType.StartArray: ParseHistory(json, result.History); break;
                case "layers" when value.Type == JsonTokenType.StartArray: ParseLayers(json, result.Rasters); break;
                case "preview": result.Preview = value.Type == JsonTokenType.String ? DecodePng(value.Text) : null; break;
                case "previewDimensions" when value.Type == JsonTokenType.StartObject:
                    ParseSize(json, out int previewWidth, out int previewHeight);
                    result.PreviewWidth = previewWidth;
                    result.PreviewHeight = previewHeight;
                    break;
                default:
                    json.RecordUnsupported($"root.{token.Text}");
                    json.SkipValue(value);
                    break;
            }
        }
    }

    private static void ParseScene(StreamingJsonTokenizer json, ParsedInk result)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) return;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            if (token.Text == "normSceneSize" && value.Type == JsonTokenType.StartObject)
            {
                ParseSize(json, out double width, out double height);
                result.Width = width;
                result.Height = height;
            }
            else if (token.Text == "cachedAssetIds" && value.Type == JsonTokenType.StartArray)
                ParseLongArray(json, result.CachedAssetIds);
            else
            {
                json.RecordUnsupported($"scene.{token.Text}");
                json.SkipValue(value);
            }
        }
    }

    private void ParseLayers(StreamingJsonTokenizer json, ICollection<ImportedRaster> rasters)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndArray) return;
            if (token.Type != JsonTokenType.StartObject) { json.SkipValue(token); continue; }
            ParseLayer(json, rasters);
        }
    }

    private void ParseLayer(StreamingJsonTokenizer json, ICollection<ImportedRaster> rasters)
    {
        var layerId = "unknown-layer";
        long? brushHead = null;
        long? maskHead = null;
        var images = new List<ParsedLayerImage>();
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) break;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            switch (token.Text)
            {
                case "layerId" when value.Type == JsonTokenType.String: layerId = value.Text ?? layerId; break;
                case "brushHeadTransactionId" when value.Type == JsonTokenType.Number: brushHead = value.Int64; break;
                case "maskHeadTransactionId" when value.Type == JsonTokenType.Number: maskHead = value.Int64; break;
                case "layerImages" when value.Type == JsonTokenType.StartArray: ParseImages(json, images); break;
                default:
                    json.RecordUnsupported($"layer.{token.Text}");
                    json.SkipValue(value);
                    break;
            }
        }
        foreach (var image in images)
            rasters.Add(new ImportedRaster(layerId, image.Role, image.Png.Bytes, image.Png.Width,
                image.Png.Height, image.Role == "mask" ? maskHead : brushHead, rasters.Count,
                Transform: image.Transform,
                CoverageSemantics: image.Role == "mask"
                    ? "coverage is stored in alpha; RGB is ignored"
                    : "no coverage; colour only"));
    }

    private void ParseImages(StreamingJsonTokenizer json, ICollection<ParsedLayerImage> images)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndArray) return;
            if (token.Type != JsonTokenType.StartObject) { json.SkipValue(token); continue; }
            var role = "image";
            DecodedPng? png = null;
            ImportedRasterTransform? transform = null;
            while (true)
            {
                token = json.Next();
                if (token.Type == JsonTokenType.EndObject) break;
                Expect(token, JsonTokenType.PropertyName);
                var value = json.Next();
                if (token.Text == "canvasName" && value.Type == JsonTokenType.String) role = value.Text ?? role;
                else if (token.Text == "image" && value.Type == JsonTokenType.String) png = DecodePng(value.Text);
                else if (token.Text == "sceneTransform" && value.Type == JsonTokenType.StartObject)
                    transform = ParseSceneTransform(json);
                else
                {
                    json.RecordUnsupported($"layerImages.{token.Text}");
                    json.SkipValue(value);
                }
            }
            if (png is not null) images.Add(new ParsedLayerImage(role, png, transform));
        }
    }

    private static ImportedRasterTransform ParseSceneTransform(StreamingJsonTokenizer json)
    {
        var offsetX = double.NaN;
        var offsetY = double.NaN;
        var scaleX = double.NaN;
        var scaleY = double.NaN;
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) break;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            if (value.Type != JsonTokenType.Number)
                throw new InvalidDataException("Scene raster transform values must be numeric.");
            switch (token.Text)
            {
                case "offsetX": offsetX = value.Double; break;
                case "offsetY": offsetY = value.Double; break;
                case "scaleX": scaleX = value.Double; break;
                case "scaleY": scaleY = value.Double; break;
                default: throw new InvalidDataException("Unsupported scene raster transform field.");
            }
        }
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY) ||
            !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            throw new InvalidDataException("Scene raster transform must be finite with positive scale.");
        return new ImportedRasterTransform(offsetX, offsetY, scaleX, scaleY);
    }

    private static void ParseHistory(StreamingJsonTokenizer json, ImportHistorySummary summary)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndArray) return;
            if (token.Type == JsonTokenType.StartObject) ParseCommand(json, summary);
            else json.SkipValue(token);
        }
    }

    private static void ParseCommand(StreamingJsonTokenizer json, ImportHistorySummary summary)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) return;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            switch (token.Text)
            {
                case "cmdType" when value.Type == JsonTokenType.String:
                    Increment(summary.CommandCounts, value.Text ?? "unknown");
                    summary.CommandSequence.Add(value.Text ?? "unknown");
                    break;
                case "textureId" when value.Type == JsonTokenType.Number:
                    summary.AssetIds.Add(value.Int64);
                    break;
                case "cmds" when value.Type == JsonTokenType.StartArray:
                    ParseHistory(json, summary);
                    break;
                case "items" when value.Type == JsonTokenType.StartArray:
                    ParseItems(json, summary);
                    break;
                default:
                    json.RecordUnsupported($"history.{token.Text}");
                    json.SkipValue(value);
                    break;
            }
        }
    }

    private static void ParseItems(StreamingJsonTokenizer json, ImportHistorySummary summary)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndArray) return;
            if (token.Type != JsonTokenType.StartObject) { json.SkipValue(token); continue; }
            while (true)
            {
                token = json.Next();
                if (token.Type == JsonTokenType.EndObject) break;
                Expect(token, JsonTokenType.PropertyName);
                var value = json.Next();
                if (token.Text == "entity" && value.Type == JsonTokenType.StartObject) ParseEntity(json, summary);
                else
                {
                    json.RecordUnsupported($"history.items.{token.Text}");
                    json.SkipValue(value);
                }
            }
        }
    }

    private static void ParseEntity(StreamingJsonTokenizer json, ImportHistorySummary summary)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) return;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            if (token.Text == "entityType" && value.Type == JsonTokenType.String)
                Increment(summary.EntityCounts, value.Text ?? "unknown");
            else if (token.Text is "stampId" or "wallAssetId" or "wallCapAssetId" or "floorAssetId" &&
                     value.Type == JsonTokenType.Number && value.Int64 > 0)
                summary.AssetIds.Add(value.Int64);
            else
            {
                json.RecordUnsupported($"history.entity.{token.Text}");
                json.SkipValue(value);
            }
        }
    }

    private static void ParseSize(StreamingJsonTokenizer json, out double width, out double height)
    {
        width = 0;
        height = 0;
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndObject) return;
            Expect(token, JsonTokenType.PropertyName);
            var value = json.Next();
            if (token.Text == "w" && value.Type == JsonTokenType.Number) width = value.Double;
            else if (token.Text == "h" && value.Type == JsonTokenType.Number) height = value.Double;
            else json.SkipValue(value);
        }
    }

    private static void ParseSize(StreamingJsonTokenizer json, out int width, out int height)
    {
        ParseSize(json, out double doubleWidth, out double doubleHeight);
        if (!double.IsFinite(doubleWidth) || !double.IsFinite(doubleHeight) ||
            doubleWidth <= 0 || doubleHeight <= 0 ||
            doubleWidth > int.MaxValue || doubleHeight > int.MaxValue ||
            Math.Truncate(doubleWidth) != doubleWidth || Math.Truncate(doubleHeight) != doubleHeight)
            throw new InvalidDataException("Preview dimensions must be positive finite pixel counts.");
        width = (int)doubleWidth;
        height = (int)doubleHeight;
    }

    private static void ParseLongArray(StreamingJsonTokenizer json, ISet<long> values)
    {
        while (true)
        {
            var token = json.Next();
            if (token.Type == JsonTokenType.EndArray) return;
            if (token.Type == JsonTokenType.Number) values.Add(token.Int64);
            else json.SkipValue(token);
        }
    }

    private static void Expect(JsonToken token, JsonTokenType expected)
    {
        if (token.Type != expected) throw new JsonException($"Expected {expected}, got {token.Type}.");
    }

    private static void Increment(IDictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }

    private DecodedPng? DecodePng(string? dataUri)
    {
        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith(PngDataPrefix, StringComparison.Ordinal))
            return null;
        var encoded = dataUri.AsSpan(PngDataPrefix.Length);
        if (encoded.Length > _bounds.MaximumBase64Characters)
            throw Reject("base64-size", $"An embedded image exceeds the {_bounds.MaximumBase64Characters}-character base64 limit.");
        var capacity = checked(((encoded.Length + 3) / 4 * 3) + 3);
        if (capacity > _bounds.MaximumDecodedImageBytes)
            throw Reject("decoded-image-size", "An embedded image would exceed the configured decoded-image allocation limit.");
        var rented = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            if (!Convert.TryFromBase64Chars(encoded, rented, out var bytesWritten) || bytesWritten < 24)
                throw Reject("invalid-base64", "An embedded PNG has invalid or truncated base64 data.");
            var png = rented.AsSpan(0, bytesWritten);
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (!png[..8].SequenceEqual(signature))
                throw Reject("invalid-png", "An embedded image is not a PNG.");
            var width = ReadBigEndianInt32(png.Slice(16, 4));
            var height = ReadBigEndianInt32(png.Slice(20, 4));
            if (width <= 0 || height <= 0 || width > _bounds.MaximumImageDimension ||
                height > _bounds.MaximumImageDimension)
                throw Reject("image-dimensions",
                    $"An embedded PNG declares {width} by {height}; the per-axis limit is {_bounds.MaximumImageDimension}.");
            var pixels = checked((long)width * height);
            if (pixels > _bounds.MaximumImagePixels)
                throw Reject("image-pixels", $"An embedded PNG declares {pixels} pixels; the limit is {_bounds.MaximumImagePixels}.");
            var decodedBytes = checked(pixels * 4);
            if (decodedBytes > _bounds.MaximumDecodedImageBytes)
                throw Reject("decoded-image-size", "An embedded PNG would exceed the decoded RGBA allocation limit.");
            var aggregate = checked(_retainedRasterBytes + bytesWritten);
            if (aggregate > _bounds.MaximumRetainedRasterBytes)
                throw Reject("aggregate-raster-size", "Embedded rasters exceed the aggregate retained-byte limit.");
            _retainedRasterBytes = aggregate;
            return new DecodedPng(png.ToArray(), width, height);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> value) =>
        (value[0] << 24) | (value[1] << 16) | (value[2] << 8) | value[3];

    private static string BuildReport(MapDocument map, IReadOnlyCollection<ImportedRaster> rasters,
        int previewWidth, int previewHeight)
    {
        var provenance = map.Import!;
        var report = new StringBuilder();
        report.AppendLine($"Imported: {map.Title}");
        report.AppendLine($"Document: {map.Width:0.##} × {map.Height:0.##}");
        report.AppendLine($"Preview: {previewWidth} × {previewHeight}");
        report.AppendLine($"Raster checkpoints: {rasters.Count}");
        report.AppendLine($"Commands: {provenance.CommandCounts.Values.Sum()}");
        report.AppendLine($"Entities observed: {provenance.EntityCounts.Values.Sum()}");
        report.AppendLine($"Unresolved asset ids: {provenance.UnresolvedAssetIds.Count}");
        report.AppendLine();
        foreach (var pair in provenance.CommandCounts.OrderByDescending(pair => pair.Value).Take(12))
            report.AppendLine($"  {pair.Key}: {pair.Value}");
        return report.ToString();
    }

    private sealed class StreamingJsonTokenizer : IDisposable
    {
        private readonly Stream _source;
        private readonly ImportBounds _bounds;
        private byte[] _buffer;
        private int _offset;
        private int _count;
        private bool _final;
        private JsonReaderState _state;
        private long _tokens;

        public StreamingJsonTokenizer(Stream source, ImportBounds bounds)
        {
            _source = source;
            _bounds = bounds;
            _buffer = new byte[Math.Min(64 * 1024, bounds.MaximumJsonTokenBytes)];
            _state = new JsonReaderState(new JsonReaderOptions { MaxDepth = bounds.MaximumJsonDepth });
        }

        public long PeakBufferBytes { get; private set; } = 64 * 1024;
        public HashSet<string> UnsupportedMetadata { get; } = new(StringComparer.Ordinal);

        public void RecordUnsupported(string path) => UnsupportedMetadata.Add(path);

        public JsonToken Next()
        {
            while (true)
            {
                var reader = new Utf8JsonReader(_buffer.AsSpan(_offset, _count), _final, _state);
                if (reader.Read())
                {
                    var token = Capture(ref reader);
                    Consume(ref reader);
                    _tokens = checked(_tokens + 1);
                    if (_tokens > _bounds.MaximumJsonTokens)
                        throw Reject("json-token-count", $"The .ink exceeds the {_bounds.MaximumJsonTokens}-token JSON limit.");
                    return token;
                }
                Consume(ref reader);
                if (_final) throw new JsonException("Unexpected end of JSON stream.");
                Fill();
            }
        }

        public void SkipValue(JsonToken first)
        {
            if (first.Type is not (JsonTokenType.StartObject or JsonTokenType.StartArray)) return;
            var depth = 1;
            while (depth > 0)
            {
                var token = Next();
                if (token.Type is JsonTokenType.StartObject or JsonTokenType.StartArray) depth++;
                else if (token.Type is JsonTokenType.EndObject or JsonTokenType.EndArray) depth--;
            }
        }

        private static JsonToken Capture(ref Utf8JsonReader reader)
        {
            var type = reader.TokenType;
            var text = type is JsonTokenType.PropertyName or JsonTokenType.String ? reader.GetString() : null;
            var integer = type == JsonTokenType.Number && reader.TryGetInt64(out var longValue) ? longValue : 0;
            var number = type == JsonTokenType.Number && reader.TryGetDouble(out var doubleValue) ? doubleValue : integer;
            return new JsonToken(type, text, integer, number);
        }

        private void Consume(ref Utf8JsonReader reader)
        {
            var consumed = checked((int)reader.BytesConsumed);
            _offset += consumed;
            _count -= consumed;
            _state = reader.CurrentState;
        }

        private void Fill()
        {
            if (_offset > 0 && _count > 0) Buffer.BlockCopy(_buffer, _offset, _buffer, 0, _count);
            _offset = 0;
            if (_count == _buffer.Length)
            {
                if (_buffer.Length >= _bounds.MaximumJsonTokenBytes)
                    throw Reject("json-token-size", "One JSON token exceeds the configured allocation limit.");
                var requested = Math.Min(checked(_buffer.Length * 2), _bounds.MaximumJsonTokenBytes);
                var larger = new byte[requested];
                Buffer.BlockCopy(_buffer, 0, larger, 0, _count);
                _buffer = larger;
                PeakBufferBytes = Math.Max(PeakBufferBytes, _buffer.LongLength);
            }
            var read = _source.Read(_buffer, _count, _buffer.Length - _count);
            _count += read;
            _final = read == 0;
        }

        public void Dispose()
        {
            _buffer = [];
        }
    }

    private readonly record struct JsonToken(JsonTokenType Type, string? Text, long Int64, double Double)
    {
        public int Int32 => checked((int)Int64);
    }

    private sealed record DecodedPng(byte[] Bytes, int Width, int Height);
    private sealed record ParsedLayerImage(string Role, DecodedPng Png, ImportedRasterTransform? Transform);
    private sealed class ImportHistorySummary
    {
        public Dictionary<string, int> CommandCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> EntityCounts { get; } = new(StringComparer.Ordinal);
        public HashSet<long> AssetIds { get; } = [];
        public List<string> CommandSequence { get; } = [];
    }

    private sealed class ParsedInk
    {
        public int Version { get; set; }
        public string? Title { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int PreviewWidth { get; set; }
        public int PreviewHeight { get; set; }
        public DecodedPng? Preview { get; set; }
        public List<ImportedRaster> Rasters { get; } = [];
        public HashSet<long> CachedAssetIds { get; } = [];
        public ImportHistorySummary History { get; } = new();
        public HashSet<string> UnsupportedMetadata { get; } = new(StringComparer.Ordinal);
    }

    private static ImportRejectedException Reject(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed class BoundedReadStream(Stream source, long maximumBytes) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var result = source.Read(buffer, offset, count);
            Count(result);
            return result;
        }

        public override int Read(Span<byte> buffer)
        {
            var result = source.Read(buffer);
            Count(result);
            return result;
        }

        private void Count(int count)
        {
            _read = checked(_read + count);
            if (_read > maximumBytes)
                throw Reject("expanded-size", $"The expanded .ink exceeds the {maximumBytes}-byte limit.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
