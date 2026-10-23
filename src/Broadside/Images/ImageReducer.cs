using System.Buffers;

namespace Broadside.Images;

/// <summary>
/// Reduces decoded samples by a power of two with an exact box average in the samples' own space: the reduced decode of an image
/// whose filters cannot reduce (Flate, LZW, RunLength, raw, CCITT, JBIG2), made from its full decode when a viewer draws it small
/// (issue #362).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.3 (the sample layout) and §8.9.4 (each reduced sample stands for the s × s source samples it covers; the last
/// row and column cover what exists). Each reduced value is the mean of the raw values it covers, rounded half up; samples of fewer
/// than 8 bits (1-bit images, 2- and 4-bit ones) become 8-bit coverage, the mean scaled to 0..255, so a 600-dpi scan shown at 100
/// dpi keeps its grey levels. Averaging raw values equals averaging their Decode-mapped values (§8.9.5.2 maps linearly), so it is
/// exact before any affine colour conversion; the caller never reduces an image whose conversion is not affine or which is Indexed.
/// </para>
/// <para>
/// Allocates the reduced image (pooled) and two pooled rows per call; nothing per row or sample.
/// </para>
/// </remarks>
internal static class ImageReducer
{
    /// <summary>Returns the bits per component of samples of <paramref name="bitsPerComponent"/> bits once reduced: 8 below 8, else the same.</summary>
    public static int ReducedBits(int bitsPerComponent) => bitsPerComponent < 8 ? 8 : bitsPerComponent;

    /// <summary>Reduces <paramref name="source"/> by 2^<paramref name="reduction"/>.</summary>
    /// <param name="source">The full decode.</param>
    /// <param name="reduction">The reduction, 1 or more.</param>
    /// <param name="cancellationToken">Checked between reduced rows.</param>
    /// <returns>The reduced samples, which the caller disposes.</returns>
    public static DecodedImage Reduce(DecodedImage source, int reduction, CancellationToken cancellationToken)
    {
        int width = source.Width;
        int height = source.Height;
        int components = source.Components;
        int scale = 1 << reduction;
        int reducedWidth = ImageGeometry.Reduce(width, reduction);
        int reducedHeight = ImageGeometry.Reduce(height, reduction);
        int bits = source.BitsPerComponent;
        int outputBits = ReducedBits(bits);
        long maximum = (1L << bits) - 1;
        bool rescale = outputBits != bits;
        int values = width * components;
        int reducedValues = reducedWidth * components;
        DecodedImageBuilder? builder = new DecodedImageBuilder(reducedWidth, reducedHeight, components, outputBits);
        ushort[] raw = ArrayPool<ushort>.Shared.Rent(Math.Max(1, values));
        long[] sums = ArrayPool<long>.Shared.Rent(Math.Max(1, reducedValues));
        try
        {
            for (int y = 0; y < reducedHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<long> sum = sums.AsSpan(0, reducedValues);
                sum.Clear();
                int firstRow = y * scale;
                int rows = Math.Min(height, firstRow + scale) - firstRow;
                for (int sy = firstRow; sy < firstRow + rows; sy++)
                {
                    ImageRows.Unpack(source.GetRow(sy), source.StorageBits, values, raw);
                    Accumulate(raw.AsSpan(0, values), sum, components, reduction);
                }

                Span<byte> row = builder.GetRow(y);
                for (int x = 0; x < reducedWidth; x++)
                {
                    int columns = Math.Min(width, (x + 1) * scale) - (x * scale);
                    long count = (long)columns * rows;
                    for (int c = 0; c < components; c++)
                    {
                        long total = sum[(x * components) + c];
                        int value = rescale
                            ? (int)(((total * 255) + ((count * maximum) / 2)) / (count * maximum))
                            : (int)((total + (count / 2)) / count);
                        Write(row, (x * components) + c, builder.StorageBits, value);
                    }
                }
            }

            builder.DecodedRows = Math.Min(reducedHeight, source.DecodedRows >> reduction);
            builder.Reduction = reduction;
            builder.SamplesInverted = source.SamplesInverted;
            builder.ColorModel = source.ColorModel;
            DecodedImage reduced = builder.Build();
            builder = null;
            return reduced;
        }
        finally
        {
            builder?.Dispose();
            ArrayPool<ushort>.Shared.Return(raw);
            ArrayPool<long>.Shared.Return(sums);
        }
    }

    /// <summary>Adds a row's raw values to the sums of the reduced samples their columns fall in.</summary>
    private static void Accumulate(ReadOnlySpan<ushort> raw, Span<long> sum, int components, int reduction)
    {
        if (components == 1)
        {
            for (int x = 0; x < raw.Length; x++)
            {
                sum[x >> reduction] += raw[x];
            }

            return;
        }

        for (int x = 0, i = 0; i < raw.Length; x++)
        {
            int target = (x >> reduction) * components;
            for (int c = 0; c < components; c++, i++)
            {
                sum[target + c] += raw[i];
            }
        }
    }

    /// <summary>Writes value <paramref name="index"/> of a row stored at 8 or 16 bits (16-bit values big-endian, §8.9.3).</summary>
    private static void Write(Span<byte> row, int index, int storageBits, int value)
    {
        if (storageBits == 16)
        {
            row[2 * index] = (byte)(value >> 8);
            row[(2 * index) + 1] = (byte)value;
        }
        else
        {
            row[index] = (byte)value;
        }
    }
}
