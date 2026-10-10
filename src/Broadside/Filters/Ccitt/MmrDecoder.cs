using System.Buffers;

namespace Broadside.Filters.Ccitt;

/// <summary>How an MMR decode ended.</summary>
internal enum MmrStatus
{
    /// <summary>Every row decoded (an EOFB after them, if any, was consumed).</summary>
    Ok,

    /// <summary>An EOFB came before the last row; the rows after it were not written.</summary>
    EndOfBlock,

    /// <summary>The data ended inside or before a row; the partial row is written (white after), later rows are not.</summary>
    Truncated,

    /// <summary>A row had an invalid code, a misplaced vertical mode, an EOL or an extension code (forbidden by T.88); that row is written as far as it decoded, later rows are not.</summary>
    Invalid,
}

/// <summary>The outcome of <see cref="MmrDecoder.Decode"/>.</summary>
/// <param name="Status">How the decode ended.</param>
/// <param name="Rows">The rows written (the damaged or partial last row included).</param>
/// <param name="BytesConsumed">The bytes the decode used, rounded up to a whole byte (T.88 §6.2.6: an integral number of bytes).</param>
internal readonly record struct MmrResult(MmrStatus Status, int Rows, int BytesConsumed);

/// <summary>
/// Decodes MMR (Modified Modified READ, ITU-T T.6) bitmaps without PDF parameters: the seam JBIG2 (T.88 §6.2.6) uses for MMR generic
/// regions, symbol bitmaps, pattern dictionaries and gray-scale bitplanes. Black pixels are 1 bits.
/// </summary>
/// <remarks>
/// ITU-T T.88 §6.2.6 and ITU-T T.6 §2.2-2.4: pure two-dimensional coding, first reference line white, no EOLs or alignment between
/// rows, EOFB optional when the byte count is known (running out of data while looking for it is normal), extension codes
/// (uncompressed mode included) not allowed, consumption rounded up to a byte so several bitmaps can be read back to back.
/// </remarks>
internal static class MmrDecoder
{
    /// <summary>Decodes <paramref name="height"/> rows of <paramref name="width"/> pixels into <paramref name="bitmap"/>.</summary>
    /// <param name="data">The MMR data, starting on a byte boundary; it may extend past the bitmap's data.</param>
    /// <param name="width">The number of pixels in a row, at least 1.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="bitmap">The destination: <paramref name="height"/> rows of <paramref name="stride"/> bytes, MSB first, 1 = black; rows not reached are left as they are.</param>
    /// <param name="stride">The bytes per destination row, at least <c>ceil(width / 8)</c>.</param>
    /// <returns>How it ended, the rows written and the bytes consumed.</returns>
    public static MmrResult Decode(ReadOnlySpan<byte> data, int width, int height, Span<byte> bitmap, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, (width + 7) >> 3);
        ArgumentOutOfRangeException.ThrowIfLessThan((long)bitmap.Length, (long)stride * height);

        int work = CcittLineDecoder.WorkLength(width);
        int[] first = ArrayPool<int>.Shared.Rent(work);
        int[] second = ArrayPool<int>.Shared.Rent(work);
        try
        {
            var line = new CcittLineDecoder(data, width, first, second);
            for (int row = 0; row < height; row++)
            {
                long start = line.BitPosition;
                if (line.TrySkipEol() && line.IsEolAhead(afterTagBit: false))
                {
                    line.TrySkipEol();
                    return new MmrResult(MmrStatus.EndOfBlock, row, line.BytesConsumed);
                }

                line.BitPosition = start;
                CcittLineStatus status = line.Decode2DLine();
                line.PackCurrent(bitmap.Slice(row * stride, stride), blackIsOne: true);
                switch (status)
                {
                    case CcittLineStatus.Ok:
                    case CcittLineStatus.RunTooLong:
                        line.UseCurrentAsReference();
                        break;
                    case CcittLineStatus.EndOfData:
                        return new MmrResult(MmrStatus.Truncated, row + 1, line.BytesConsumed);
                    default:
                        return new MmrResult(MmrStatus.Invalid, row + 1, line.BytesConsumed);
                }
            }

            // An optional EOFB after the last row: consume it when it is whole.
            long end = line.BitPosition;
            if (!(line.TrySkipEol() && line.TrySkipEol()))
            {
                line.BitPosition = end;
            }

            return new MmrResult(MmrStatus.Ok, height, line.BytesConsumed);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(first);
            ArrayPool<int>.Shared.Return(second);
        }
    }
}
