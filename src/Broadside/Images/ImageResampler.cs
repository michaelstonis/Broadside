using System.Buffers;

namespace Broadside.Images;

/// <summary>
/// Reduces decoded samples to any smaller size with an exact box (area) average in the samples' own space: the resampler of the
/// save-time image pass (issue #625), which caps an image's resolution at <see cref="PdfImageOptimization.MaxResolution"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3 (the sample layout), §8.9.4 (the image maps onto the unit square, so a smaller grid covers the same area) and
/// §8.9.5.3 (a reduced image averages what each new sample covers). Source column x of a W-wide image covers [x·W′, (x + 1)·W′) of
/// the W·W′ units the row spans, and target column i covers [i·W, (i + 1)·W); as W′ ≤ W, a source column falls in at most two
/// target columns, and its weight in each is the integer length of the overlap. Rows are weighted the same way, so every target
/// value is the integer sum of source values times overlap areas, divided by W·H and rounded half up: exact, and the same on every
/// platform. Averaging raw values equals averaging their Decode-mapped values (§8.9.5.2 maps linearly), so the image's
/// <c>Decode</c> array still applies unchanged. Indexed images are not averaged (an average of indices is not a colour); the
/// caller never passes one.
/// </para>
/// <para>
/// Output depths: 8 for sources of 2 to 8 bits (2- and 4-bit values scaled to 0..255), 16 for 16-bit sources, and 1 for bitonal
/// output (1-bit images and image masks), thresholded at half (a covered area of at least half sets the bit), without dithering.
/// </para>
/// <para>
/// Hot path (CLAUDE.md): one call rents its column map and accumulators from <see cref="ArrayPool{T}"/> once; nothing is allocated
/// per row or sample. The caller provides the destination.
/// </para>
/// </remarks>
internal static class ImageResampler
{
    /// <summary>Returns the number of bytes in one row of <paramref name="width"/> samples of <paramref name="components"/> at <paramref name="bits"/> bits.</summary>
    /// <param name="width">The samples in the row.</param>
    /// <param name="components">The components of each sample.</param>
    /// <param name="bits">The bits of each component.</param>
    /// <returns>⌈width × components × bits / 8⌉ (§8.9.3).</returns>
    public static int Stride(int width, int components, int bits) => (int)((((long)width * components * bits) + 7) / 8);

    /// <summary>Returns the depth a source of <paramref name="bitsPerComponent"/> bits is written at once resampled.</summary>
    /// <param name="bitsPerComponent">The source's logical bits per component: 1, 2, 4, 8 or 16.</param>
    /// <param name="bitonal">Whether the output stays 1-bit (a 1-bit image or an image mask).</param>
    /// <returns>1 for bitonal output, 16 for 16-bit sources, 8 otherwise.</returns>
    public static int OutputBits(int bitsPerComponent, bool bitonal) => bitonal ? 1 : bitsPerComponent == 16 ? 16 : 8;

    /// <summary>Resamples the samples of a <paramref name="width"/> × <paramref name="height"/> image to <paramref name="targetWidth"/> × <paramref name="targetHeight"/>.</summary>
    /// <param name="source">The source rows, <see cref="Stride"/>(width, components, storageBits) bytes each, top row first.</param>
    /// <param name="width">The source's samples per row.</param>
    /// <param name="height">The source's rows.</param>
    /// <param name="components">The components of each sample.</param>
    /// <param name="storageBits">The bits each component occupies in <paramref name="source"/>: 1, 2, 4, 8 or 16.</param>
    /// <param name="bitsPerComponent">The logical depth, which sets the largest raw value (2^n − 1); at most <paramref name="storageBits"/>.</param>
    /// <param name="targetWidth">The target's samples per row, 1 to <paramref name="width"/>.</param>
    /// <param name="targetHeight">The target's rows, 1 to <paramref name="height"/>.</param>
    /// <param name="outputBits">The target's depth: 1 (bitonal; one component), 8 or 16 (at least <paramref name="bitsPerComponent"/>).</param>
    /// <param name="destination">The target rows, at least <see cref="Stride"/>(targetWidth, components, outputBits) × targetHeight bytes.</param>
    /// <exception cref="ArgumentException">A size, depth or buffer does not fit the others.</exception>
    public static void Resample(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int components,
        int storageBits,
        int bitsPerComponent,
        int targetWidth,
        int targetHeight,
        int outputBits,
        Span<byte> destination)
    {
        if (width < 1 || height < 1 || components < 1 || targetWidth < 1 || targetHeight < 1 || targetWidth > width || targetHeight > height
            || storageBits is not (1 or 2 or 4 or 8 or 16) || bitsPerComponent < 1 || bitsPerComponent > storageBits
            || outputBits is not (1 or 8 or 16) || (outputBits == 1 && components != 1) || (outputBits != 1 && outputBits < bitsPerComponent))
        {
            throw new ArgumentException("The sizes and depths do not describe a reduction the box filter makes.");
        }

        int sourceStride = Stride(width, components, storageBits);
        int targetStride = Stride(targetWidth, components, outputBits);
        if (source.Length < (long)sourceStride * height || destination.Length < (long)targetStride * targetHeight)
        {
            throw new ArgumentException("A buffer is shorter than its rows.");
        }

        int values = width * components;
        int targetValues = targetWidth * components;
        ushort[] raw = ArrayPool<ushort>.Shared.Rent(values);
        int[] columnTarget = ArrayPool<int>.Shared.Rent(width);
        int[] columnWeight = ArrayPool<int>.Shared.Rent(width);
        long[] rowSums = ArrayPool<long>.Shared.Rent(targetValues);
        long[] sums = ArrayPool<long>.Shared.Rent(targetValues);
        try
        {
            MapColumns(width, targetWidth, columnTarget, columnWeight);
            long area = (long)width * height;
            long maximum = (1L << bitsPerComponent) - 1;
            for (int y = 0; y < targetHeight; y++)
            {
                Span<long> sum = sums.AsSpan(0, targetValues);
                sum.Clear();

                // Target row y covers [y·H, (y + 1)·H) of the H·H′ units; source row r covers [r·H′, (r + 1)·H′).
                long top = (long)y * height;
                long bottom = top + height;
                int first = (int)(top / targetHeight);
                int last = (int)((bottom - 1) / targetHeight);
                for (int r = first; r <= last; r++)
                {
                    long start = Math.Max(top, (long)r * targetHeight);
                    long end = Math.Min(bottom, (long)(r + 1) * targetHeight);
                    long rowWeight = end - start;
                    ImageRows.Unpack(source.Slice(r * sourceStride, sourceStride), storageBits, values, raw);
                    Span<long> rowSum = rowSums.AsSpan(0, targetValues);
                    rowSum.Clear();
                    AccumulateRow(raw.AsSpan(0, values), components, targetWidth, columnTarget.AsSpan(0, width), columnWeight.AsSpan(0, width), rowSum);
                    for (int i = 0; i < rowSum.Length; i++)
                    {
                        sum[i] += rowSum[i] * rowWeight;
                    }
                }

                WriteRow(sum, destination.Slice(y * targetStride, targetStride), area, maximum, bitsPerComponent, outputBits);
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(raw);
            ArrayPool<int>.Shared.Return(columnTarget);
            ArrayPool<int>.Shared.Return(columnWeight);
            ArrayPool<long>.Shared.Return(rowSums);
            ArrayPool<long>.Shared.Return(sums);
        }
    }

    /// <summary>
    /// For each source column, the first target column it falls in and its weight there (the overlap, in units where a target column
    /// is <paramref name="width"/> wide); the rest of its <paramref name="targetWidth"/> units fall in the next target column.
    /// </summary>
    private static void MapColumns(int width, int targetWidth, Span<int> target, Span<int> weight)
    {
        for (int x = 0; x < width; x++)
        {
            long start = (long)x * targetWidth;
            long end = start + targetWidth;
            int column = (int)(start / width);
            long boundary = (long)(column + 1) * width;
            target[x] = column;
            weight[x] = (int)(Math.Min(end, boundary) - start);
        }
    }

    /// <summary>Adds a row's raw values, times their columns' weights, to the sums of the target columns they fall in.</summary>
    private static void AccumulateRow(ReadOnlySpan<ushort> raw, int components, int targetWidth, ReadOnlySpan<int> columnTarget, ReadOnlySpan<int> columnWeight, Span<long> rowSum)
    {
        int lastColumn = targetWidth - 1;
        if (components == 1)
        {
            for (int x = 0; x < columnTarget.Length; x++)
            {
                int column = columnTarget[x];
                int weight = columnWeight[x];
                long value = raw[x];
                rowSum[column] += value * weight;
                int rest = targetWidth - weight;
                if (rest > 0 && column < lastColumn)
                {
                    rowSum[column + 1] += value * rest;
                }
            }

            return;
        }

        for (int x = 0; x < columnTarget.Length; x++)
        {
            int column = columnTarget[x];
            int weight = columnWeight[x];
            int rest = targetWidth - weight;
            int sourceIndex = x * components;
            int targetIndex = column * components;
            for (int c = 0; c < components; c++)
            {
                rowSum[targetIndex + c] += (long)raw[sourceIndex + c] * weight;
            }

            if (rest > 0 && column < lastColumn)
            {
                targetIndex += components;
                for (int c = 0; c < components; c++)
                {
                    rowSum[targetIndex + c] += (long)raw[sourceIndex + c] * rest;
                }
            }
        }
    }

    /// <summary>Divides a target row's sums by the area each target sample covers and writes them at <paramref name="outputBits"/>.</summary>
    private static void WriteRow(ReadOnlySpan<long> sum, Span<byte> row, long area, long maximum, int bitsPerComponent, int outputBits)
    {
        if (outputBits == 1)
        {
            // Bitonal: the bit is set when at least half the area is set (2·sum ≥ area·max), packed most significant bit first.
            row.Clear();
            long half = area * maximum;
            for (int i = 0; i < sum.Length; i++)
            {
                if (2 * sum[i] >= half)
                {
                    row[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
            }

            return;
        }

        if (outputBits == 8)
        {
            if (bitsPerComponent == 8)
            {
                long rounding = area / 2;
                for (int i = 0; i < sum.Length; i++)
                {
                    row[i] = (byte)((sum[i] + rounding) / area);
                }
            }
            else
            {
                // 2- and 4-bit values (and 1-bit ones averaged to grey) scale to 0..255.
                long divisor = area * maximum;
                long rounding = divisor / 2;
                for (int i = 0; i < sum.Length; i++)
                {
                    row[i] = (byte)(((sum[i] * 255) + rounding) / divisor);
                }
            }

            return;
        }

        long half16 = area / 2;
        for (int i = 0; i < sum.Length; i++)
        {
            int value = (int)((sum[i] + half16) / area);
            row[2 * i] = (byte)(value >> 8);
            row[(2 * i) + 1] = (byte)value;
        }
    }
}
