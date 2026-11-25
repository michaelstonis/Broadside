using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>
/// The predictor functions of LZWDecode and FlateDecode: TIFF Predictor 2 and the PNG predictors, undone after the filter by the
/// pipeline. Not a filter of its own.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.4.3 (Table 8: Predictor, Colors, BitsPerComponent, Columns) and §7.4.4.4 (Tables 9 and 10). Rows are
/// ceil(Colors × BitsPerComponent × Columns / 8) bytes. TIFF Predictor 2 adds each colour component to the same component of the
/// sample to its left, modulo 2<sup>BitsPerComponent</sup> (16-bit components big-endian). Predictor 10 to 15 means PNG: each row
/// starts with its own algorithm tag (0 None, 1 Sub, 2 Up, 3 Average, 4 Paeth), whatever value of 10 to 15 is declared, and bytes
/// are predicted from the byte max(1, ceil(Colors × BitsPerComponent / 8)) to the left. Samples outside the image are 0.
/// </para>
/// <para>
/// Lenient repairs, one <c>PredictorInvalid</c> diagnostic each: an unknown Predictor passes the data through unchanged; a PNG tag
/// above 4 is read as None; a final partial row is padded with zeros. Parameters out of range take their defaults
/// (<c>DecodeParmsInvalid</c>); a row longer than the decoded-length limit passes the data through. Data shorter than one row is
/// decoded as one partial row sized to the data, not padded to <c>Columns</c>, so the output is never larger than the input.
/// </para>
/// </remarks>
internal static class Predictor
{
    /// <summary>How many rows a run in slices undoes between two checks of its deadline.</summary>
    private const int RowsPerCheck = 16;

    /// <summary>Undoes the predictor the context's parameters name; with Predictor 1 or absent, copies the data.</summary>
    /// <param name="data">The filter's output.</param>
    /// <param name="output">Where to write the samples.</param>
    /// <param name="context">The filter's context; its parameters are the filter's DecodeParms.</param>
    public static void Decode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context)
    {
        var run = default(Run);
        try
        {
            if (run.Begin(data, output, context))
            {
                run.Continue(data, output, long.MaxValue);
                run.End(context);
            }
        }
        finally
        {
            run.Release();
        }
    }

    /// <summary>
    /// The state of one predictor run, which <see cref="Continue"/> can undo a few rows at a time (issue #566): a viewer on one thread
    /// decodes a large Flate image between frames. The rows and diagnostics equal <see cref="Decode"/>'s however the run is sliced.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4.4. Not thread-safe; <see cref="Release"/> returns its row buffers.</remarks>
    internal struct Run
    {
        private byte[]? _previous;
        private byte[]? _current;
        private int _rowLength;
        private int _bytesPerPixel;
        private int _colors;
        private int _bitsPerComponent;
        private int _columns;
        private int _offset;
        private bool _tiff;
        private bool _unknownTag;
        private bool _partialRow;

        /// <summary>
        /// Reads the parameters; when the data passes through (no predictor, or one it cannot apply) writes it and returns
        /// <see langword="false"/>, otherwise rents the row buffers and returns <see langword="true"/>: rows remain to undo.
        /// </summary>
        public bool Begin(ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context)
        {
            long predictor = context.ReadInteger(FilterNames.Predictor, 1, long.MinValue, long.MaxValue);
            if (predictor == 1)
            {
                output.Write(data);
                return false;
            }

            if (predictor is not (2 or (>= 10 and <= 15)))
            {
                context.Report(FilterDiagnostics.PredictorInvalid, DiagnosticSeverity.Error, "The Predictor parameter shall be 1, 2 or 10 to 15; the data is passed through unpredicted.");
                output.Write(data);
                return false;
            }

            int colors = (int)context.ReadInteger(FilterNames.Colors, 1, 1, 256);
            int bitsPerComponent = (int)context.ReadInteger(FilterNames.BitsPerComponent, 8, 1, 16);
            if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
            {
                context.Report(FilterDiagnostics.DecodeParmsInvalid, DiagnosticSeverity.Warning, "The BitsPerComponent parameter shall be 1, 2, 4, 8 or 16; the default 8 is used.");
                bitsPerComponent = 8;
            }

            long columns = context.ReadInteger(FilterNames.Columns, 1, 1, int.MaxValue);
            long rowLength = ((colors * bitsPerComponent * columns) + 7) / 8;
            if (rowLength > Math.Min(context.MaxDecodedLength, Array.MaxLength - 1))
            {
                context.Report(FilterDiagnostics.PredictorInvalid, DiagnosticSeverity.Error, "The predictor's rows are longer than the decoded-length limit; the data is passed through unpredicted.");
                output.Write(data);
                return false;
            }

            if (data.IsEmpty)
            {
                // No rows to undo: Columns alone must not size the row buffers (issue #274).
                return false;
            }

            long dataRow = predictor == 2 ? data.Length : data.Length - 1;
            if (rowLength > dataRow && !data.IsEmpty)
            {
                // Not one whole row: Columns, not the data, would size the row buffers and the zero padding of the last row, so a few
                // bytes with a huge Columns would decode to up to the decoded-length limit (fuzzing finding, issue #48). The data is one
                // partial row: decode it sized to the data, without padding.
                context.Report(FilterDiagnostics.PredictorInvalid, DiagnosticSeverity.Warning, "The predicted data is shorter than one row; it is decoded as one partial row, not padded.");
                rowLength = Math.Max(dataRow, 0);
                columns = rowLength * 8 / (colors * bitsPerComponent);
            }

            _bytesPerPixel = Math.Max(1, ((colors * bitsPerComponent) + 7) / 8);
            _rowLength = (int)rowLength;
            _colors = colors;
            _bitsPerComponent = bitsPerComponent;
            _columns = (int)columns;
            _tiff = predictor == 2;
            _offset = 0;
            _previous = ArrayPool<byte>.Shared.Rent(_rowLength);
            _current = ArrayPool<byte>.Shared.Rent(_rowLength);
            if (!_tiff)
            {
                _previous.AsSpan(0, _rowLength).Clear();
            }

            return true;
        }

        /// <summary>
        /// Undoes rows of <paramref name="data"/> (the same data <see cref="Begin"/> saw) until its end or, after at least a few rows,
        /// until <see cref="System.Diagnostics.Stopwatch"/> reaches <paramref name="deadline"/>.
        /// </summary>
        /// <returns><see langword="true"/> when every row is done.</returns>
        public bool Continue(ReadOnlySpan<byte> data, IBufferWriter<byte> output, long deadline)
        {
            Span<byte> previous = _previous.AsSpan(0, _rowLength);
            Span<byte> current = _current.AsSpan(0, _rowLength);
            int rows = 0;
            while (_offset < data.Length)
            {
                if (deadline != long.MaxValue && ++rows > RowsPerCheck)
                {
                    rows = 1;
                    if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline)
                    {
                        return false;
                    }
                }

                _offset = _tiff
                    ? TiffRow(data, _offset, output, current, _colors, _bitsPerComponent, _columns, ref _partialRow)
                    : PngRow(data, _offset, output, previous, current, _bytesPerPixel, ref _unknownTag, ref _partialRow);
            }

            return true;
        }

        /// <summary>Reports what the rows met, once every row is done.</summary>
        public readonly void End(FilterContext context)
        {
            if (_unknownTag)
            {
                context.Report(FilterDiagnostics.PredictorInvalid, DiagnosticSeverity.Warning, "A PNG predictor row has an algorithm tag above 4; such rows are read unpredicted.");
            }

            if (_partialRow)
            {
                context.Report(FilterDiagnostics.PredictorInvalid, DiagnosticSeverity.Warning, "The predicted data ends in the middle of a row; the row is padded with zeros.");
            }
        }

        /// <summary>Returns the row buffers.</summary>
        public void Release()
        {
            if (_previous is { } previous)
            {
                ArrayPool<byte>.Shared.Return(previous);
                _previous = null;
            }

            if (_current is { } current)
            {
                ArrayPool<byte>.Shared.Return(current);
                _current = null;
            }
        }
    }

    /// <summary>Undoes the PNG row at <paramref name="offset"/> and writes it; returns the offset of the next row.</summary>
    private static int PngRow(ReadOnlySpan<byte> data, int offset, IBufferWriter<byte> output, Span<byte> previous, Span<byte> current, int bytesPerPixel, ref bool unknownTag, ref bool partialRow)
    {
        int rowLength = current.Length;
        byte tag = data[offset];
        int available = Math.Min(rowLength, data.Length - offset - 1);
        partialRow |= available < rowLength;
        current.Clear();
        data.Slice(offset + 1, available).CopyTo(current);
        switch (tag)
        {
            case 0:
                break;
            case 1:
                for (int index = bytesPerPixel; index < rowLength; index++)
                {
                    current[index] += current[index - bytesPerPixel];
                }

                break;
            case 2:
                for (int index = 0; index < rowLength; index++)
                {
                    current[index] += previous[index];
                }

                break;
            case 3:
                for (int index = 0; index < rowLength; index++)
                {
                    int left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                    current[index] += (byte)((left + previous[index]) >> 1);
                }

                break;
            case 4:
                for (int index = 0; index < rowLength; index++)
                {
                    int left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                    int upperLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;
                    current[index] += Paeth(left, previous[index], upperLeft);
                }

                break;
            default:
                unknownTag = true;
                break;
        }

        output.Write(current);
        current.CopyTo(previous);
        return offset + 1 + available;
    }

    /// <summary>The Paeth predictor of ISO/IEC 15948 §9.4: whichever of left, above and upper-left is closest to left + above - upper-left.</summary>
    private static byte Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return (byte)left;
        }

        return toAbove <= toUpperLeft ? (byte)above : (byte)upperLeft;
    }

    /// <summary>Undoes the TIFF row at <paramref name="offset"/> and writes it; returns the offset of the next row.</summary>
    private static int TiffRow(ReadOnlySpan<byte> data, int offset, IBufferWriter<byte> output, Span<byte> row, int colors, int bitsPerComponent, int columns, ref bool partialRow)
    {
        int rowLength = row.Length;
        int available = Math.Min(rowLength, data.Length - offset);
        partialRow |= available < rowLength;
        row.Clear();
        data.Slice(offset, available).CopyTo(row);
        switch (bitsPerComponent)
        {
            case 8:
                for (int index = colors; index < rowLength; index++)
                {
                    row[index] += row[index - colors];
                }

                break;
            case 16:
                int stride = colors * 2;
                for (int index = stride; index + 1 < rowLength; index += 2)
                {
                    int sum = ((row[index] << 8) | row[index + 1]) + ((row[index - stride] << 8) | row[index - stride + 1]);
                    row[index] = (byte)(sum >> 8);
                    row[index + 1] = (byte)sum;
                }

                break;
            default:
                UndoSubByteDifferences(row, colors, bitsPerComponent, colors * columns);
                break;
        }

        output.Write(row);
        return offset + available;
    }

    /// <summary>TIFF Predictor 2 for 1, 2 and 4 bits per component: components packed high-order bit first.</summary>
    private static void UndoSubByteDifferences(Span<byte> row, int colors, int bitsPerComponent, int components)
    {
        int mask = (1 << bitsPerComponent) - 1;
        for (int component = colors; component < components; component++)
        {
            int bit = component * bitsPerComponent;
            int leftBit = bit - (colors * bitsPerComponent);
            int shift = 8 - bitsPerComponent - (bit & 7);
            int leftShift = 8 - bitsPerComponent - (leftBit & 7);
            int value = (row[bit >> 3] >> shift) & mask;
            int left = (row[leftBit >> 3] >> leftShift) & mask;
            int sum = (value + left) & mask;
            row[bit >> 3] = (byte)((row[bit >> 3] & ~(mask << shift)) | (sum << shift));
        }
    }
}
