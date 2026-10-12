using System.Buffers;
using System.IO.Compression;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>The <c>FlateDecode</c> filter: zlib/deflate decompression through the BCL's <see cref="DeflateStream"/>. Decode only for now.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.4.1 (RFC 1950 zlib wrapper around RFC 1951 deflate) and §7.4.4.3. The two-byte zlib header is checked here and
/// the deflate data after it is inflated; the Adler-32 checksum and anything after the final block are not read, as other readers
/// do. The predictor parameters of Table 8 are applied after this filter by the pipeline (§7.4.4.4).
/// </para>
/// <para>
/// Lenient repairs, one diagnostic each: data without a valid zlib header is inflated as raw deflate data from its first byte
/// (<c>FilterDataInvalid</c>); corrupt data keeps the bytes inflated before the error, but for at most the last one (<c>FilterDataInvalid</c>); data that ends
/// before the final deflate block keeps everything inflated (<c>FilterDataTruncated</c>).
/// </para>
/// <para>Allocates the BCL inflater once per call, never per byte.</para>
/// </remarks>
public sealed class FlateDecodeFilter : IStreamFilter
{
    private const int ChunkSize = 16384;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4: <c>FlateDecode</c>; abbreviated <c>Fl</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.FlateDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.4.1; RFC 1950; RFC 1951.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlyMemory<byte> deflate = encoded;
        if (HasZlibHeader(encoded.Span))
        {
            deflate = encoded[2..];
        }
        else
        {
            context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Warning, "FlateDecode data does not start with a zlib header (RFC 1950); it is read as raw deflate data.");
        }

        using var source = new MemorySource(deflate);
        long inflated = 0;
        try
        {
            using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            while (true)
            {
                Span<byte> chunk = output.GetSpan(ChunkSize);
                int count = inflater.Read(chunk);
                if (count == 0)
                {
                    break;
                }

                output.Advance(count);
                inflated += count;
            }
        }
        catch (InvalidDataException)
        {
            RecoverBeforeError(deflate, inflated, output);
            context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Error, "FlateDecode data is corrupt; the bytes inflated before the error are kept.");
            return;
        }

        if (source.ReadPastEnd)
        {
            context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, "FlateDecode data ends before its final deflate block; everything inflated is kept.");
        }
    }

    /// <summary>
    /// After an error, inflates again one byte at a time from where the first pass stopped committing output: the BCL discards the
    /// bytes a read produced before the error, so this recovers everything before the error except, at most, the last byte, which
    /// zlib produces in the same call that finds the error. Runs only on corrupt data.
    /// </summary>
    private static void RecoverBeforeError(ReadOnlyMemory<byte> deflate, long alreadyWritten, IBufferWriter<byte> output)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            using var source = new MemorySource(deflate);
            using var inflater = new DeflateStream(source, CompressionMode.Decompress);
            long skipped = 0;
            while (skipped < alreadyWritten)
            {
                int count = inflater.Read(scratch, 0, (int)Math.Min(scratch.Length, alreadyWritten - skipped));
                if (count == 0)
                {
                    return;
                }

                skipped += count;
            }

            while (inflater.Read(scratch, 0, 1) == 1)
            {
                output.Write(scratch.AsSpan(0, 1));
            }
        }
        catch (InvalidDataException)
        {
            // The error itself: everything before it has been written.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>Checks the RFC 1950 header: method 8 (deflate), window of at most 32 KB, check bits, no preset dictionary.</summary>
    private static bool HasZlibHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 2
        && (data[0] & 0x0F) == 8
        && (data[0] >> 4) <= 7
        && ((data[0] << 8) | data[1]) % 31 == 0
        && (data[1] & 0x20) == 0;

    /// <summary>
    /// A read-only stream over memory that records whether the inflater asked for data past the end: <see cref="DeflateStream"/>
    /// stops asking once it has inflated the final block, so asking past the end means the deflate data is truncated.
    /// </summary>
    private sealed class MemorySource(ReadOnlyMemory<byte> data) : Stream
    {
        private int _position;

        public bool ReadPastEnd { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, data.Length - _position);
            if (count == 0 && buffer.Length > 0)
            {
                ReadPastEnd = true;
                return 0;
            }

            data.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            return Read(one) == 1 ? one[0] : -1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
