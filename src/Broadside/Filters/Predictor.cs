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
/// (<c>DecodeParmsInvalid</c>); a row longer than the decoded-length limit passes the data through.
/// </para>
/// </remarks>
internal static class Predictor
{
    /// <summary>Undoes the predictor the context's parameters name; with Predictor 1 or absent, copies the data.</summary>
    /// <param name="data">The filter's output.</param>
    /// <param name="output">Where to write the samples.</param>
    /// <param name="context">The filter's context; its parameters are the filter's DecodeParms.</param>
    public static void Decode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context)
    {
        long predictor = context.ReadInteger(FilterNames.Predictor, 1, long.MinValue, long.MaxValue);
        if (predictor == 1)
        {
            output.Write(data);
            return;
        }

        if (predictor is not (2 or (>= 10 and <= 15)))
        {
            context.Report(DiagnosticCodes.PredictorInvalid, DiagnosticSeverity.Error, "The Predictor parameter shall be 1, 2 or 10 to 15; the data is passed through unpredicted.");
            output.Write(data);
            return;
        }

        int colors = (int)context.ReadInteger(FilterNames.Colors, 1, 1, 256);
        int bitsPerComponent = (int)context.ReadInteger(FilterNames.BitsPerComponent, 8, 1, 16);
        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            context.Report(DiagnosticCodes.DecodeParmsInvalid, DiagnosticSeverity.Warning, "The BitsPerComponent parameter shall be 1, 2, 4, 8 or 16; the default 8 is used.");
            bitsPerComponent = 8;
        }

        long columns = context.ReadInteger(FilterNames.Columns, 1, 1, int.MaxValue);
        long rowLength = ((colors * bitsPerComponent * columns) + 7) / 8;
        if (rowLength > Math.Min(context.MaxDecodedLength, Array.MaxLength - 1))
        {
            context.Report(DiagnosticCodes.PredictorInvalid, DiagnosticSeverity.Error, "The predictor's rows are longer than the decoded-length limit; the data is passed through unpredicted.");
            output.Write(data);
            return;
        }

        int bytesPerPixel = Math.Max(1, ((colors * bitsPerComponent) + 7) / 8);
        byte[] previous = ArrayPool<byte>.Shared.Rent((int)rowLength);
        byte[] current = ArrayPool<byte>.Shared.Rent((int)rowLength);
        try
        {
            if (predictor == 2)
            {
                DecodeTiff(data, output, context, current.AsSpan(0, (int)rowLength), colors, bitsPerComponent, (int)columns);
            }
            else
            {
                DecodePng(data, output, context, previous.AsSpan(0, (int)rowLength), current.AsSpan(0, (int)rowLength), bytesPerPixel);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(previous);
            ArrayPool<byte>.Shared.Return(current);
        }
    }

    private static void DecodePng(ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context, Span<byte> previous, Span<byte> current, int bytesPerPixel)
    {
        previous.Clear();
        bool unknownTag = false;
        bool partialRow = false;
        int rowLength = current.Length;
        while (!data.IsEmpty)
        {
            byte tag = data[0];
            int available = Math.Min(rowLength, data.Length - 1);
            partialRow |= available < rowLength;
            current.Clear();
            data.Slice(1, available).CopyTo(current);
            data = data[(1 + available)..];
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
        }

        if (unknownTag)
        {
            context.Report(DiagnosticCodes.PredictorInvalid, DiagnosticSeverity.Warning, "A PNG predictor row has an algorithm tag above 4; such rows are read unpredicted.");
        }

        if (partialRow)
        {
            context.Report(DiagnosticCodes.PredictorInvalid, DiagnosticSeverity.Warning, "The predicted data ends in the middle of a row; the row is padded with zeros.");
        }
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

    private static void DecodeTiff(ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context, Span<byte> row, int colors, int bitsPerComponent, int columns)
    {
        bool partialRow = false;
        int rowLength = row.Length;
        while (!data.IsEmpty)
        {
            int available = Math.Min(rowLength, data.Length);
            partialRow |= available < rowLength;
            row.Clear();
            data[..available].CopyTo(row);
            data = data[available..];
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
        }

        if (partialRow)
        {
            context.Report(DiagnosticCodes.PredictorInvalid, DiagnosticSeverity.Warning, "The predicted data ends in the middle of a row; the row is padded with zeros.");
        }
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
