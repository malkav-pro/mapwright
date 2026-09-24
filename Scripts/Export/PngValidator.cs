using System.Buffers.Binary;
using System.IO.Compression;

namespace Mapwright.Export;

public sealed record PngValidationResult(
    bool Passed,
    int Width,
    int Height,
    long CompressedBytes,
    long InflatedBytes,
    string Detail)
{
    public bool HasSrgbMetadata { get; init; }
    public bool HasStraightAlpha { get; init; }
}

public static class PngValidator
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static PngValidationResult ValidateRgba8(
        string path,
        int expectedWidth,
        int expectedHeight,
        CancellationToken cancellationToken = default,
        Action<long, long>? progress = null)
    {
        try
        {
            using var input = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[8];
            input.ReadExactly(signature);
            if (!signature.SequenceEqual(Signature))
                return Fail("Invalid PNG signature.");

            var width = 0;
            var height = 0;
            var sawIhdr = false;
            var sawIend = false;
            var sawSrgb = false;
            var sawGamma = false;
            var header = new byte[8];
            var storedCrc = new byte[4];
            var transferBuffer = new byte[1024 * 1024];
            var compressedPath = path + $".idat-{Guid.NewGuid():N}.tmp";
            using var compressed = new FileStream(compressedPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, transferBuffer.Length,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan);
            long compressedBytes = 0;

            while (!sawIend)
            {
                cancellationToken.ThrowIfCancellationRequested();
                input.ReadExactly(header);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
                if (length > int.MaxValue)
                    return Fail("PNG chunk is too large.");

                var type = header.AsSpan(4, 4).ToArray();
                var chunkType = System.Text.Encoding.ASCII.GetString(type);
                byte[]? ihdr = chunkType == "IHDR" ? new byte[(int)length] : null;
                var crc = BeginCrc(type);
                var remaining = (long)length;
                var ihdrOffset = 0;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requested = (int)Math.Min(transferBuffer.Length, remaining);
                    input.ReadExactly(transferBuffer.AsSpan(0, requested));
                    crc = Update(crc, transferBuffer.AsSpan(0, requested));
                    if (chunkType == "IDAT")
                    {
                        compressed.Write(transferBuffer, 0, requested);
                        compressedBytes += requested;
                    }
                    if (ihdr is not null)
                    {
                        transferBuffer.AsSpan(0, requested).CopyTo(ihdr.AsSpan(ihdrOffset));
                        ihdrOffset += requested;
                    }
                    remaining -= requested;
                }
                input.ReadExactly(storedCrc);

                if (FinishCrc(crc) != BinaryPrimitives.ReadUInt32BigEndian(storedCrc))
                    return Fail($"CRC mismatch in {chunkType} chunk.");

                switch (chunkType)
                {
                    case "IHDR":
                        if (ihdr is null || ihdr.Length != 13)
                            return Fail("Invalid IHDR length.");
                        width = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(0, 4));
                        height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4, 4));
                        if (ihdr[8] != 8 || ihdr[9] != 6 || ihdr[10] != 0 || ihdr[11] != 0 || ihdr[12] != 0)
                            return Fail("Expected non-interlaced 8-bit RGBA PNG.");
                        sawIhdr = true;
                        break;
                    case "gAMA":
                        if (ihdr is not null || length != 4)
                            return Fail("Invalid gAMA length.");
                        var gammaBytes = new byte[4];
                        input.Position -= 8;
                        input.ReadExactly(gammaBytes);
                        input.Position += 4;
                        if (BinaryPrimitives.ReadUInt32BigEndian(gammaBytes) != 45_455)
                            return Fail("Expected PNG sRGB gamma 45455.");
                        sawGamma = true;
                        break;
                    case "sRGB":
                        if (length != 1)
                            return Fail("Invalid sRGB length.");
                        input.Position -= 5;
                        var intent = input.ReadByte();
                        input.Position += 4;
                        if (intent is < 0 or > 3)
                            return Fail("Invalid sRGB rendering intent.");
                        sawSrgb = true;
                        break;
                    case "IEND":
                        sawIend = true;
                        break;
                }
            }

            if (!sawIhdr || width != expectedWidth || height != expectedHeight)
                return Fail($"Expected {expectedWidth}x{expectedHeight}; got {width}x{height}.", width, height);
            if (!sawSrgb || !sawGamma)
                return Fail("PNG is missing required sRGB/gAMA colour metadata.", width, height);
            if (compressedBytes == 0) return Fail("PNG contains no IDAT payload.", width, height);

            compressed.Flush();
            compressed.Position = 0;
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var buffer = new byte[1024 * 1024];
            var rowBytes = checked(width * 4 + 1);
            var expectedInflatedBytes = checked((long)rowBytes * height);
            long inflatedBytes = 0;
            int read;
            while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = 0; index < read; index++)
                {
                    if ((inflatedBytes + index) % rowBytes == 0 && buffer[index] > 4)
                        return Fail($"Invalid PNG filter byte {buffer[index]} at row {(inflatedBytes + index) / rowBytes}.", width, height);
                }
                inflatedBytes += read;
                if (inflatedBytes > expectedInflatedBytes)
                    return Fail("PNG inflated payload exceeds the declared dimensions.", width, height);
                progress?.Invoke(inflatedBytes, expectedInflatedBytes);
            }

            if (inflatedBytes != expectedInflatedBytes)
                return Fail($"Expected {expectedInflatedBytes:N0} inflated bytes; got {inflatedBytes:N0}.", width, height);

            if (input.Position != input.Length)
                return Fail("PNG contains trailing bytes after IEND.", width, height);
            return new PngValidationResult(true, width, height, compressedBytes, inflatedBytes,
                "PNG signature, chunk CRCs, dimensions, straight-alpha RGBA, sRGB/gAMA metadata, scanline filters, and complete zlib payload are valid using bounded-memory validation.")
            {
                HasSrgbMetadata = true,
                HasStraightAlpha = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }

        PngValidationResult Fail(string detail, int width = 0, int height = 0) =>
            new(false, width, height, 0, 0, detail);
    }

    private static uint BeginCrc(ReadOnlySpan<byte> type)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type) crc = Update(crc, value);
        return crc;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data) crc = Update(crc, value);
        return crc;
    }

    private static uint FinishCrc(uint crc) => crc ^ 0xFFFFFFFFu;

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }
}
