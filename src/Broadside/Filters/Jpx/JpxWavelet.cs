using System.Buffers;

namespace Broadside.Filters.Jpx;

/// <summary>The inverse discrete wavelet transform of a tile-component, in place in its coefficient buffer.</summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex F.3: for each level from the lowest resolution up, 2D_SR interleaves the four sub-bands (F.3.3), filters every
/// row (HOR_SR, F.3.4) then every column (VER_SR, F.3.5) with 1D_SR (F.3.6), extending the signal by periodic symmetric extension
/// (PSEO, F.3.7) through mirrored indices. The 5/3 reversible lifting is equations F-5 and F-6, with floor division by arithmetic
/// shifts. The 9/7 irreversible lifting is equation F-7 in single precision, columns eight at a time; mirroring each lifting step's
/// neighbours at the ends equals the extension of F.3.7 for these symmetric filters. A length-one signal is kept at an even index and
/// halved at an odd one (T.800 (2019) F.3.6; the 2002 text has a typo).
/// </para>
/// <para>
/// Layout: resolution r occupies the top-left Width x Height of the buffer, its lower resolution r - 1 (the LL part) at the top-left,
/// HL to its right, LH below, HH diagonally, each as decoded. Allocation: pooled lines per call.
/// </para>
/// </remarks>
internal static class JpxWavelet
{
    // Table F.4; columns are filtered in groups of eight.
    private const int Columns = 8;
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;
    private const float InverseK = (float)(1 / 1.230174104914001);

    /// <summary>Reconstructs <paramref name="component"/>'s samples from its sub-bands with the 5/3 reversible filter.</summary>
    public static void Inverse53(JpxTileComponent component, int[] buffer)
    {
        int stride = component.Width;
        int longest = Math.Max(component.Width, component.Height);
        if (component.Resolutions.Length < 2 || longest == 0)
        {
            return;
        }

        int[] line = ArrayPool<int>.Shared.Rent(longest);
        int[] lows = ArrayPool<int>.Shared.Rent(longest);
        try
        {
            for (int r = 1; r < component.Resolutions.Length; r++)
            {
                JpxResolution resolution = component.Resolutions[r];
                JpxResolution lower = component.Resolutions[r - 1];
                int width = resolution.Width;
                int height = resolution.Height;
                int xParity = (int)(resolution.X0 & 1);
                int yParity = (int)(resolution.Y0 & 1);
                for (int y = 0; y < height; y++)
                {
                    Span<int> row = buffer.AsSpan(y * stride, width);
                    Interleave(row, lower.Width, xParity, line);
                    Inverse1D(line.AsSpan(0, width), xParity);
                    line.AsSpan(0, width).CopyTo(row);
                }

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        lows[y] = buffer[(y * stride) + x];
                    }

                    Interleave(lows.AsSpan(0, height), lower.Height, yParity, line);
                    Inverse1D(line.AsSpan(0, height), yParity);
                    for (int y = 0; y < height; y++)
                    {
                        buffer[(y * stride) + x] = line[y];
                    }
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(line);
            ArrayPool<int>.Shared.Return(lows);
        }
    }

    /// <summary>
    /// F.3.3 in one dimension: the low-pass samples (the first <paramref name="lowCount"/> of <paramref name="source"/>) go to the even
    /// absolute positions, the high-pass ones to the odd positions; <paramref name="parity"/> is the parity of the first position.
    /// </summary>
    private static void Interleave(ReadOnlySpan<int> source, int lowCount, int parity, Span<int> target)
    {
        int low = 0;
        int high = lowCount;
        for (int k = 0; k < source.Length; k++)
        {
            target[k] = ((k + parity) & 1) == 0 ? source[low++] : source[high++];
        }
    }

    /// <summary>1D_SR with the 5/3 filter (F-5, F-6) over a signal whose first sample has absolute parity <paramref name="parity"/>.</summary>
    private static void Inverse1D(Span<int> x, int parity)
    {
        int n = x.Length;
        if (n == 1)
        {
            if (parity == 1)
            {
                x[0] /= 2;
            }

            return;
        }

        for (int k = parity; k < n; k += 2)
        {
            int left = k > 0 ? x[k - 1] : x[k + 1];
            int right = k + 1 < n ? x[k + 1] : x[k - 1];
            x[k] -= (left + right + 2) >> 2;
        }

        for (int k = 1 - parity; k < n; k += 2)
        {
            int left = k > 0 ? x[k - 1] : x[k + 1];
            int right = k + 1 < n ? x[k + 1] : x[k - 1];
            x[k] += (left + right) >> 1;
        }
    }

    /// <summary>Reconstructs <paramref name="component"/>'s samples from its dequantized sub-bands with the 9/7 irreversible filter.</summary>
    /// <remarks>ITU-T T.800 F.3.8.2, equation F-7 and Table F.4: K scales the low-pass samples, 1/K the high-pass ones.</remarks>
    public static void Inverse97(JpxTileComponent component, Span<float> buffer)
    {
        int stride = component.Width;
        int longest = Math.Max(component.Width, component.Height);
        if (component.Resolutions.Length < 2 || longest == 0)
        {
            return;
        }

        float[] line = ArrayPool<float>.Shared.Rent(longest * Columns);
        float[] lows = ArrayPool<float>.Shared.Rent(longest * Columns);
        try
        {
            for (int r = 1; r < component.Resolutions.Length; r++)
            {
                JpxResolution resolution = component.Resolutions[r];
                JpxResolution lower = component.Resolutions[r - 1];
                int width = resolution.Width;
                int height = resolution.Height;
                int xParity = (int)(resolution.X0 & 1);
                int yParity = (int)(resolution.Y0 & 1);
                for (int y = 0; y < height; y++)
                {
                    Span<float> row = buffer.Slice(y * stride, width);
                    Interleave(row, lower.Width, xParity, line);
                    Inverse1D97(line.AsSpan(0, width), xParity);
                    line.AsSpan(0, width).CopyTo(row);
                }

                // Columns in groups: gathered side by side so each lifting step walks memory in order.
                for (int x0 = 0; x0 < width; x0 += Columns)
                {
                    int count = Math.Min(Columns, width - x0);
                    int lowCount = lower.Height;
                    for (int y = 0; y < height; y++)
                    {
                        buffer.Slice((y * stride) + x0, count).CopyTo(lows.AsSpan(y * Columns, count));
                    }

                    InterleaveColumns(lows, height, lowCount, yParity, line, count);
                    Inverse1D97Columns(line, height, yParity, count);
                    for (int y = 0; y < height; y++)
                    {
                        line.AsSpan(y * Columns, count).CopyTo(buffer.Slice((y * stride) + x0, count));
                    }
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(line);
            ArrayPool<float>.Shared.Return(lows);
        }
    }

    private static void Interleave(ReadOnlySpan<float> source, int lowCount, int parity, Span<float> target)
    {
        int low = 0;
        int high = lowCount;
        for (int k = 0; k < source.Length; k++)
        {
            target[k] = ((k + parity) & 1) == 0 ? source[low++] : source[high++];
        }
    }

    /// <summary>Interleaves <paramref name="count"/> columns stored side by side (row pitch <see cref="Columns"/>).</summary>
    private static void InterleaveColumns(float[] source, int length, int lowCount, int parity, float[] target, int count)
    {
        int low = 0;
        int high = lowCount;
        for (int k = 0; k < length; k++)
        {
            int from = ((k + parity) & 1) == 0 ? low++ : high++;
            source.AsSpan(from * Columns, count).CopyTo(target.AsSpan(k * Columns, count));
        }
    }

    /// <summary>1D_SR with the 9/7 filter (F-7) over a signal whose first sample has absolute parity <paramref name="parity"/>.</summary>
    private static void Inverse1D97(Span<float> x, int parity)
    {
        int n = x.Length;
        if (n == 1)
        {
            if (parity == 1)
            {
                x[0] /= 2;
            }

            return;
        }

        int low = parity;
        int high = 1 - parity;
        for (int k = low; k < n; k += 2)
        {
            x[k] *= K;
        }

        for (int k = high; k < n; k += 2)
        {
            x[k] *= InverseK;
        }

        Lift(x, low, Delta);
        Lift(x, high, Gamma);
        Lift(x, low, Beta);
        Lift(x, high, Alpha);

        static void Lift(Span<float> x, int start, float factor)
        {
            int n = x.Length;
            for (int k = start; k < n; k += 2)
            {
                float left = k > 0 ? x[k - 1] : x[k + 1];
                float right = k + 1 < n ? x[k + 1] : x[k - 1];
                x[k] -= factor * (left + right);
            }
        }
    }

    /// <summary><see cref="Inverse1D97"/> over <paramref name="count"/> columns side by side.</summary>
    private static void Inverse1D97Columns(float[] x, int n, int parity, int count)
    {
        if (n == 1)
        {
            if (parity == 1)
            {
                for (int c = 0; c < count; c++)
                {
                    x[c] /= 2;
                }
            }

            return;
        }

        int low = parity;
        int high = 1 - parity;
        Scale(x, n, low, K, count);
        Scale(x, n, high, InverseK, count);
        Lift(x, n, low, Delta, count);
        Lift(x, n, high, Gamma, count);
        Lift(x, n, low, Beta, count);
        Lift(x, n, high, Alpha, count);

        static void Scale(float[] x, int n, int start, float factor, int count)
        {
            for (int k = start; k < n; k += 2)
            {
                Span<float> row = x.AsSpan(k * Columns, count);
                for (int c = 0; c < row.Length; c++)
                {
                    row[c] *= factor;
                }
            }
        }

        static void Lift(float[] x, int n, int start, float factor, int count)
        {
            for (int k = start; k < n; k += 2)
            {
                Span<float> row = x.AsSpan(k * Columns, count);
                ReadOnlySpan<float> left = x.AsSpan((k > 0 ? k - 1 : k + 1) * Columns, count);
                ReadOnlySpan<float> right = x.AsSpan((k + 1 < n ? k + 1 : k - 1) * Columns, count);
                for (int c = 0; c < row.Length; c++)
                {
                    row[c] -= factor * (left[c] + right[c]);
                }
            }
        }
    }
}
