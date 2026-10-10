using System.Buffers;

namespace Broadside.Filters.Jpx;

/// <summary>The inverse discrete wavelet transform of a tile-component, in place in its coefficient buffer.</summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex F.3: for each level from the lowest resolution up, 2D_SR interleaves the four sub-bands (F.3.3), filters every
/// row (HOR_SR, F.3.4) then every column (VER_SR, F.3.5) with 1D_SR (F.3.6), extending the signal by periodic symmetric extension
/// (PSEO, F.3.7) through mirrored indices. The 5/3 reversible lifting is equations F-5 and F-6, with floor division by arithmetic
/// shifts. A length-one signal is kept at an even index and halved at an odd one (T.800 (2019) F.3.6; the 2002 text has a typo).
/// </para>
/// <para>
/// Layout: resolution r occupies the top-left Width x Height of the buffer, its lower resolution r - 1 (the LL part) at the top-left,
/// HL to its right, LH below, HH diagonally, each as decoded. Allocation: one pooled line per call.
/// </para>
/// </remarks>
internal static class JpxWavelet
{
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
}
