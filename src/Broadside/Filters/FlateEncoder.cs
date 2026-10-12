using System.Buffers;
using System.IO.Compression;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>The <c>FlateDecode</c> encoder: zlib/deflate compression through the BCL's <see cref="ZLibStream"/>, at the context's compression level.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.4.1: the data is an RFC 1950 zlib stream (two-byte header, RFC 1951 deflate data, Adler-32 checksum), which
/// <see cref="FlateDecodeFilter"/> and every other reader decode. <see cref="StreamEncoderContext.CompressionLevel"/> selects the
/// effort: <see cref="CompressionLevel.NoCompression"/> writes stored blocks, <see cref="CompressionLevel.SmallestSize"/> the
/// smallest output. The same input, level and runtime always give the same bytes.
/// </para>
/// <para>
/// The predictor functions of §7.4.4.4 named by the parameters are applied before this encoder by the pipeline, not here.
/// Allocates the BCL deflater once per call, never per byte or row.
/// </para>
/// </remarks>
public sealed class FlateEncoder : IStreamEncoder
{
    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4: <c>FlateDecode</c>.</remarks>
    public CosName Name => FilterNames.FlateDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4.1; RFC 1950; RFC 1951.</remarks>
    public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        if (data.IsEmpty)
        {
            // The BCL writes nothing for nothing; a FlateDecode stream still needs a zlib stream: the header, one empty final fixed
            // Huffman block, and the Adler-32 of no data (1), as zlib's own compress() writes.
            output.Write<byte>([0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
            return;
        }

        using var target = new BufferWriterStream(output);
        using (var zlib = new ZLibStream(target, context.CompressionLevel, leaveOpen: true))
        {
            zlib.Write(data);
        }
    }

    /// <summary>A write-only stream that hands what is written to an <see cref="IBufferWriter{T}"/>, without a buffer of its own.</summary>
    private sealed class BufferWriterStream(IBufferWriter<byte> output) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer) => output.Write(buffer);

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void WriteByte(byte value)
        {
            output.GetSpan(1)[0] = value;
            output.Advance(1);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
