using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Mapwright.Export;

public sealed class StreamingPngWriter : IDisposable
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly FileStream _file;
    private readonly IdatChunkStream _idat;
    private readonly ZLibStream _zlib;
    private readonly int _width;
    private readonly int _height;
    private int _rowsWritten;
    private bool _completed;

    public StreamingPngWriter(string path, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        _width = width;
        _height = height;
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);
        _file.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(_file, "IHDR"u8, header);
        Span<byte> gamma = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(gamma, 45_455);
        WriteChunk(_file, "gAMA"u8, gamma);
        WriteChunk(_file, "sRGB"u8, [0]);
        _idat = new IdatChunkStream(_file, 1 << 20);
        _zlib = new ZLibStream(_idat, CompressionLevel.Fastest, leaveOpen: true);
    }

    public void WriteRgbaRow(ReadOnlySpan<byte> rgba)
    {
        if (_completed) throw new InvalidOperationException("PNG is already complete.");
        if (_rowsWritten >= _height) throw new InvalidOperationException("Too many rows.");
        if (rgba.Length != _width * 4) throw new ArgumentException("Unexpected row length.", nameof(rgba));
        _zlib.WriteByte(0);
        _zlib.Write(rgba);
        _rowsWritten++;
    }

    public void Complete()
    {
        if (_completed) return;
        if (_rowsWritten != _height) throw new InvalidOperationException($"Expected {_height} rows, received {_rowsWritten}.");
        _zlib.Dispose();
        _idat.Complete();
        WriteChunk(_file, "IEND"u8, ReadOnlySpan<byte>.Empty);
        _file.Flush(flushToDisk: true);
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _zlib.Dispose();
            _idat.Dispose();
        }
        _file.Dispose();
    }

    private static void WriteChunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);
        destination.Write(type);
        destination.Write(data);
        var crc = Crc32.Compute(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        destination.Write(crcBytes);
    }

    private sealed class IdatChunkStream(Stream destination, int capacity) : Stream
    {
        private readonly byte[] _buffer = new byte[capacity];
        private int _count;
        private bool _completed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => FlushChunk();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> source)
        {
            while (!source.IsEmpty)
            {
                var take = Math.Min(source.Length, _buffer.Length - _count);
                source[..take].CopyTo(_buffer.AsSpan(_count));
                _count += take;
                source = source[take..];
                if (_count == _buffer.Length) FlushChunk();
            }
        }

        public void Complete()
        {
            if (_completed) return;
            FlushChunk();
            _completed = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_completed) Complete();
            base.Dispose(disposing);
        }

        private void FlushChunk()
        {
            if (_count == 0) return;
            WriteChunk(destination, "IDAT"u8, _buffer.AsSpan(0, _count));
            _count = 0;
        }
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        {
            var crc = uint.MaxValue;
            foreach (var value in first) crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
            foreach (var value in second) crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }
}
