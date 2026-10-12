using System.Buffers;
using System.Globalization;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// The predictor functions of LZWDecode and FlateDecode, applied before the encoder by the encoding pipeline: the inverse of
/// <see cref="Predictor"/>. Not an encoder of its own.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.4.3 (Table 8: Predictor, Colors, BitsPerComponent, Columns) and §7.4.4.4 (Tables 9 and 10). Rows are
/// ceil(Colors × BitsPerComponent × Columns / 8) bytes. TIFF Predictor 2 replaces each colour component by its difference from the
/// same component of the sample to its left, modulo 2<sup>BitsPerComponent</sup>. Predictors 10 to 14 write every row with the PNG
/// algorithm they name (None, Sub, Up, Average, Paeth), each row preceded by its tag; Predictor 15 (PNG optimum) chooses, row by
/// row, the algorithm whose output has the smallest sum of absolute values when read as signed bytes, the heuristic of ISO/IEC
/// 15948 §12.8, which tends to give deflate the most to work with.
/// </para>
/// <para>
/// The data must be whole rows: a reader pads a partial final row with zeros, so it would not decode to the same bytes. Invalid
/// parameters or a partial row throw <see cref="ArgumentException"/>. Allocates one or two pooled rows per call (zeros above the first row; the trial row of Predictor 15), nothing per row.
/// </para>
/// </remarks>
internal static class PredictorEncoder
{
    /// <summary>Applies the predictor <paramref name="parameters"/> name; with Predictor 1 or absent, copies the data.</summary>
    /// <param name="data">The samples, whole rows.</param>
    /// <param name="output">Where to write the predicted rows, for the encoder.</param>
    /// <param name="parameters">The filter's DecodeParms, or <see langword="null"/>.</param>
    /// <param name="resolve">Resolves an indirect parameter value.</param>
    /// <exception cref="ArgumentException">A parameter is invalid, or the data is not a whole number of rows.</exception>
    public static void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, CosDictionary? parameters, Func<CosObject?, CosObject> resolve)
    {
        long predictor = Read(parameters, FilterNames.Predictor, 1, 1, 15, resolve);
        if (predictor == 1)
        {
            output.Write(data);
            return;
        }

        if (predictor is not (2 or >= 10))
        {
            throw new ArgumentException($"The Predictor parameter is {predictor}; it shall be 1, 2 or 10 to 15 (ISO 32000-2 §7.4.4.3, Table 8).", nameof(parameters));
        }

        int colors = (int)Read(parameters, FilterNames.Colors, 1, 1, 256, resolve);
        int bitsPerComponent = (int)Read(parameters, FilterNames.BitsPerComponent, 8, 1, 16, resolve);
        if (bitsPerComponent is not (1 or 2 or 4 or 8 or 16))
        {
            throw new ArgumentException($"The BitsPerComponent parameter is {bitsPerComponent}; it shall be 1, 2, 4, 8 or 16 (ISO 32000-2 §7.4.4.3, Table 8).", nameof(parameters));
        }

        long columns = Read(parameters, FilterNames.Columns, 1, 1, int.MaxValue, resolve);
        long rowLength = ((colors * bitsPerComponent * columns) + 7) / 8;
        if (rowLength >= Array.MaxLength || data.Length % rowLength != 0)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"{data.Length} bytes are not a whole number of predictor rows of {rowLength} bytes; a reader would pad the last row with zeros."),
                nameof(data));
        }

        if (data.IsEmpty)
        {
            return;
        }

        int bytesPerPixel = Math.Max(1, ((colors * bitsPerComponent) + 7) / 8);
        var writer = new FilterOutput(output);
        if (predictor == 2)
        {
            EncodeTiff(data, ref writer, (int)rowLength, colors, bitsPerComponent, (int)columns);
        }
        else
        {
            byte[] zeros = ArrayPool<byte>.Shared.Rent((int)rowLength);
            byte[]? trial = predictor == 15 ? ArrayPool<byte>.Shared.Rent((int)rowLength) : null;
            try
            {
                Span<byte> above = zeros.AsSpan(0, (int)rowLength);
                above.Clear();
                EncodePng(data, ref writer, above, trial is null ? default : trial.AsSpan(0, (int)rowLength), (int)predictor - 10, bytesPerPixel);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(zeros);
                if (trial is not null)
                {
                    ArrayPool<byte>.Shared.Return(trial);
                }
            }
        }

        writer.Flush();
    }

    /// <summary>
    /// Writes each row as its tag and its filtered bytes. Predictor 15 (<paramref name="algorithm"/> 5) filters the row with every
    /// algorithm into <paramref name="trial"/> and keeps the one whose bytes, read as signed, have the smallest sum of absolute
    /// values; the lowest tag wins a tie.
    /// </summary>
    private static void EncodePng(ReadOnlySpan<byte> data, ref FilterOutput writer, ReadOnlySpan<byte> zeroRow, Span<byte> trial, int algorithm, int bytesPerPixel)
    {
        int rowLength = zeroRow.Length;
        ReadOnlySpan<byte> above = zeroRow;
        for (int start = 0; start < data.Length; start += rowLength)
        {
            ReadOnlySpan<byte> row = data.Slice(start, rowLength);
            Span<byte> target = writer.Reserve(rowLength + 1);
            if (algorithm < 5)
            {
                target[0] = (byte)algorithm;
                Filter(algorithm, row, above, target[1..], bytesPerPixel);
            }
            else
            {
                long bestSum = long.MaxValue;
                for (int tag = 0; tag < 5; tag++)
                {
                    Filter(tag, row, above, trial, bytesPerPixel);
                    long sum = 0;
                    foreach (byte value in trial)
                    {
                        sum += Math.Abs((int)(sbyte)value);
                    }

                    if (sum < bestSum)
                    {
                        bestSum = sum;
                        target[0] = (byte)tag;
                        trial.CopyTo(target[1..]);
                    }
                }
            }

            above = row;
        }
    }

    /// <summary>Filters a row with a PNG algorithm (ISO/IEC 15948 §9.2): each byte minus its prediction from the original samples.</summary>
    private static void Filter(int tag, ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> target, int bytesPerPixel)
    {
        int length = row.Length;
        int lead = Math.Min(bytesPerPixel, length);
        switch (tag)
        {
            case 1:
                row[..lead].CopyTo(target);
                for (int index = lead; index < length; index++)
                {
                    target[index] = (byte)(row[index] - row[index - bytesPerPixel]);
                }

                break;
            case 2:
                for (int index = 0; index < length; index++)
                {
                    target[index] = (byte)(row[index] - above[index]);
                }

                break;
            case 3:
                for (int index = 0; index < lead; index++)
                {
                    target[index] = (byte)(row[index] - (above[index] >> 1));
                }

                for (int index = lead; index < length; index++)
                {
                    target[index] = (byte)(row[index] - ((row[index - bytesPerPixel] + above[index]) >> 1));
                }

                break;
            case 4:
                for (int index = 0; index < lead; index++)
                {
                    target[index] = (byte)(row[index] - above[index]);
                }

                for (int index = lead; index < length; index++)
                {
                    target[index] = (byte)(row[index] - Paeth(row[index - bytesPerPixel], above[index], above[index - bytesPerPixel]));
                }

                break;
            default:
                row.CopyTo(target);
                break;
        }
    }

    /// <summary>The Paeth predictor of ISO/IEC 15948 §9.4: whichever of left, above and upper-left is closest to left + above - upper-left.</summary>
    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return left;
        }

        return toAbove <= toUpperLeft ? above : upperLeft;
    }

    private static void EncodeTiff(ReadOnlySpan<byte> data, ref FilterOutput writer, int rowLength, int colors, int bitsPerComponent, int columns)
    {
        for (int start = 0; start < data.Length; start += rowLength)
        {
            ReadOnlySpan<byte> row = data.Slice(start, rowLength);
            Span<byte> target = writer.Reserve(rowLength);
            row.CopyTo(target);
            switch (bitsPerComponent)
            {
                case 8:
                    for (int index = colors; index < rowLength; index++)
                    {
                        target[index] = (byte)(row[index] - row[index - colors]);
                    }

                    break;
                case 16:
                    int stride = colors * 2;
                    for (int index = stride; index + 1 < rowLength; index += 2)
                    {
                        int difference = ((row[index] << 8) | row[index + 1]) - ((row[index - stride] << 8) | row[index - stride + 1]);
                        target[index] = (byte)(difference >> 8);
                        target[index + 1] = (byte)difference;
                    }

                    break;
                default:
                    SubByteDifferences(row, target, colors, bitsPerComponent, colors * columns);
                    break;
            }
        }
    }

    /// <summary>TIFF Predictor 2 for 1, 2 and 4 bits per component: components packed high-order bit first; padding bits are kept.</summary>
    private static void SubByteDifferences(ReadOnlySpan<byte> row, Span<byte> target, int colors, int bitsPerComponent, int components)
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
            int difference = (value - left) & mask;
            target[bit >> 3] = (byte)((target[bit >> 3] & ~(mask << shift)) | (difference << shift));
        }
    }

    /// <summary>Reads an integer parameter; absent or null gives the default, anything else out of range throws.</summary>
    private static long Read(CosDictionary? parameters, CosName key, long defaultValue, long minimum, long maximum, Func<CosObject?, CosObject> resolve)
    {
        if (parameters is null || !parameters.TryGetValue(key, out CosObject? entry))
        {
            return defaultValue;
        }

        return resolve(entry) switch
        {
            CosNull => defaultValue,
            CosInteger { Value: var value } when value >= minimum && value <= maximum => value,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The {key.Value} parameter shall be an integer from {minimum} to {maximum} (ISO 32000-2 §7.4.4.3, Table 8)."),
                nameof(parameters)),
        };
    }
}
