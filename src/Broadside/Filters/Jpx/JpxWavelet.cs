using System.Buffers;
using System.Runtime.InteropServices;
using Broadside.Filters.Codecs;

namespace Broadside.Filters.Jpx;

/// <summary>The inverse discrete wavelet transform of a tile-component, in place in its coefficient buffer.</summary>
/// <remarks>
/// <para>
/// ITU-T T.800 Annex F.3: for each level from the lowest resolution up, 2D_SR interleaves the four sub-bands (F.3.3), filters every
/// row (HOR_SR, F.3.4) then every column (VER_SR, F.3.5) with 1D_SR (F.3.6), extending the signal by periodic symmetric extension
/// (PSEO, F.3.7) through mirrored indices. The 5/3 reversible lifting is equations F-5 and F-6, with floor division by arithmetic
/// shifts. The 9/7 irreversible lifting is equation F-7 in single precision; mirroring each lifting step's neighbours at the ends
/// equals the extension of F.3.7 for these symmetric filters. A length-one signal is kept at an even index and halved at an odd one
/// (T.800 (2019) F.3.6; the 2002 text has a typo).
/// </para>
/// <para>
/// The lifting runs on the sub-bands as they lie, before the interleave of F.3.3: a step updates the run of low-pass (or high-pass)
/// samples from the other run shifted by at most one place, which is 1D_SR's arithmetic on the same values sample for sample, so
/// <see cref="JpxLifting"/>'s vector kernels (issue #477) take whole runs. Rows: each row's low-pass half and high-pass half, then the
/// interleave. Columns: strips of up to <see cref="Strip"/> columns copied out row by row, so a step's targets and neighbours are
/// whole rows of the strip, then copied back in interleaved order.
/// </para>
/// <para>
/// Layout: resolution r occupies the top-left Width x Height of the buffer, its lower resolution r - 1 (the LL part) at the top-left,
/// HL to its right, LH below, HH diagonally, each as decoded. Allocation: a pooled line and strip per call.
/// </para>
/// </remarks>
internal static class JpxWavelet
{
    /// <summary>The widest column strip of the vertical pass: 256 bytes a row, so a tall strip stays in cache through its steps.</summary>
    private const int Strip = 64;

    // Table F.4.
    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;
    private const float InverseK = (float)(1 / 1.230174104914001);

    /// <summary>
    /// Reconstructs <paramref name="component"/>'s samples from its sub-bands with the 5/3 reversible filter, up to resolution
    /// <paramref name="top"/> (its last for the full image; a lower one for a reduced decode, B.5, whose samples are then the
    /// top-left of the buffer at the full stride).
    /// </summary>
    public static void Inverse53(JpxTileComponent component, int[] buffer, CodecReporter reporter, int top)
    {
        int stride = component.Width;
        int longest = Math.Max(component.Width, component.Height);
        if (top < 1 || longest == 0)
        {
            return;
        }

        int[] line = ArrayPool<int>.Shared.Rent(longest);
        int[] strip = ArrayPool<int>.Shared.Rent(Math.Max(1, component.Height * Math.Min(Strip, component.Width)));
        try
        {
            for (int r = 1; r <= top; r++)
            {
                JpxResolution resolution = component.Resolutions[r];
                JpxResolution lower = component.Resolutions[r - 1];
                int width = resolution.Width;
                int height = resolution.Height;
                int xParity = (int)(resolution.X0 & 1);
                int yParity = (int)(resolution.Y0 & 1);
                for (int y = 0; y < height; y++)
                {
                    reporter.ThrowIfCancellationRequested();
                    Rows53(buffer.AsSpan(y * stride, width), lower.Width, xParity, line);
                }

                for (int x0 = 0; x0 < width; x0 += Strip)
                {
                    reporter.ThrowIfCancellationRequested();
                    int count = Math.Min(Strip, width - x0);
                    Span<int> columns = strip.AsSpan(0, height * count);
                    for (int y = 0; y < height; y++)
                    {
                        buffer.AsSpan((y * stride) + x0, count).CopyTo(columns.Slice(y * count, count));
                    }

                    Columns53(columns, height, count, lower.Height, yParity);
                    Scatter<int>(columns, height, count, lower.Height, yParity, buffer.AsSpan(x0), stride);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(line);
            ArrayPool<int>.Shared.Return(strip);
        }
    }

    /// <summary>1D_SR with the 5/3 filter (F-5, F-6) over a row whose low-pass samples are its first <paramref name="lowCount"/>.</summary>
    /// <param name="row">The row, low-pass then high-pass samples; the reconstructed signal on return.</param>
    /// <param name="lowCount">The number of low-pass samples.</param>
    /// <param name="parity">The absolute parity of the row's first sample.</param>
    /// <param name="line">Scratch at least as long as the row.</param>
    private static void Rows53(Span<int> row, int lowCount, int parity, int[] line)
    {
        if (row.Length == 1)
        {
            if (parity == 1)
            {
                row[0] /= 2;
            }

            return;
        }

        Span<int> lows = row[..lowCount];
        Span<int> highs = row[lowCount..];
        Step53(lows, highs, LowOffset(parity), low: true);
        Step53(highs, lows, HighOffset(parity), low: false);
        Span<int> target = line.AsSpan(0, row.Length);
        Interleave(lows, highs, parity, target);
        target.CopyTo(row);
    }

    /// <summary>
    /// One 5/3 lifting step over a run of one parity (F-5 on the low-pass run, F-6 on the high-pass one): target i's neighbours are
    /// <paramref name="source"/>[i + <paramref name="offset"/>] and the sample after it, mirrored at the ends (F.3.7).
    /// </summary>
    private static void Step53(Span<int> target, ReadOnlySpan<int> source, int offset, bool low)
    {
        (int first, int last) = Interior(target.Length, source.Length, offset);
        for (int i = 0; i < first; i++)
        {
            Edge(target, source, offset, low, i);
        }

        if (last > first)
        {
            Span<int> run = target[first..last];
            ReadOnlySpan<int> left = source.Slice(first + offset, run.Length);
            ReadOnlySpan<int> right = source.Slice(first + offset + 1, run.Length);
            if (low)
            {
                JpxLifting.Lift53Low(run, left, right);
            }
            else
            {
                JpxLifting.Lift53High(run, left, right);
            }
        }

        for (int i = Math.Max(first, last); i < target.Length; i++)
        {
            Edge(target, source, offset, low, i);
        }

        static void Edge(Span<int> target, ReadOnlySpan<int> source, int offset, bool low, int i)
        {
            (int a, int b) = Neighbours(i, offset, source.Length);
            if (low)
            {
                target[i] -= (source[a] + source[b] + 2) >> 2;
            }
            else
            {
                target[i] += (source[a] + source[b]) >> 1;
            }
        }
    }

    /// <summary>
    /// 1D_SR with the 5/3 filter down <paramref name="count"/> columns side by side (row pitch <paramref name="count"/>), whose first
    /// <paramref name="lowCount"/> rows are low-pass: each step a row at a time.
    /// </summary>
    private static void Columns53(Span<int> columns, int height, int count, int lowCount, int parity)
    {
        if (height == 1)
        {
            if (parity == 1)
            {
                for (int c = 0; c < count; c++)
                {
                    columns[c] /= 2;
                }
            }

            return;
        }

        int highCount = height - lowCount;
        for (int i = 0; i < lowCount; i++)
        {
            (int left, int right) = Neighbours(i, LowOffset(parity), highCount);
            JpxLifting.Lift53Low(Row(columns, i, count), Row(columns, lowCount + left, count), Row(columns, lowCount + right, count));
        }

        for (int i = 0; i < highCount; i++)
        {
            (int left, int right) = Neighbours(i, HighOffset(parity), lowCount);
            JpxLifting.Lift53High(Row(columns, lowCount + i, count), Row(columns, left, count), Row(columns, right, count));
        }
    }

    /// <summary>Reconstructs <paramref name="component"/>'s samples from its dequantized sub-bands with the 9/7 irreversible filter.</summary>
    /// <remarks>ITU-T T.800 F.3.8.2, equation F-7 and Table F.4: K scales the low-pass samples, 1/K the high-pass ones.</remarks>
    /// <param name="component">The tile-component.</param>
    /// <param name="buffer">Its sub-bands, reconstructed in place.</param>
    /// <param name="reporter">The cancellation.</param>
    /// <param name="top">The resolution reconstructed, as for <see cref="Inverse53"/>.</param>
    public static void Inverse97(JpxTileComponent component, Span<float> buffer, CodecReporter reporter, int top)
    {
        int stride = component.Width;
        int longest = Math.Max(component.Width, component.Height);
        if (top < 1 || longest == 0)
        {
            return;
        }

        float[] line = ArrayPool<float>.Shared.Rent(longest);
        float[] strip = ArrayPool<float>.Shared.Rent(Math.Max(1, component.Height * Math.Min(Strip, component.Width)));
        try
        {
            for (int r = 1; r <= top; r++)
            {
                JpxResolution resolution = component.Resolutions[r];
                JpxResolution lower = component.Resolutions[r - 1];
                int width = resolution.Width;
                int height = resolution.Height;
                int xParity = (int)(resolution.X0 & 1);
                int yParity = (int)(resolution.Y0 & 1);
                for (int y = 0; y < height; y++)
                {
                    reporter.ThrowIfCancellationRequested();
                    Rows97(buffer.Slice(y * stride, width), lower.Width, xParity, line);
                }

                for (int x0 = 0; x0 < width; x0 += Strip)
                {
                    reporter.ThrowIfCancellationRequested();
                    int count = Math.Min(Strip, width - x0);
                    Span<float> columns = strip.AsSpan(0, height * count);
                    for (int y = 0; y < height; y++)
                    {
                        buffer.Slice((y * stride) + x0, count).CopyTo(columns.Slice(y * count, count));
                    }

                    Columns97(columns, height, count, lower.Height, yParity);
                    Scatter<float>(columns, height, count, lower.Height, yParity, buffer[x0..], stride);
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(line);
            ArrayPool<float>.Shared.Return(strip);
        }
    }

    /// <summary>1D_SR with the 9/7 filter (F-7) over a row whose low-pass samples are its first <paramref name="lowCount"/>.</summary>
    /// <param name="row">The row, low-pass then high-pass samples; the reconstructed signal on return.</param>
    /// <param name="lowCount">The number of low-pass samples.</param>
    /// <param name="parity">The absolute parity of the row's first sample.</param>
    /// <param name="line">Scratch at least as long as the row.</param>
    private static void Rows97(Span<float> row, int lowCount, int parity, float[] line)
    {
        if (row.Length == 1)
        {
            if (parity == 1)
            {
                row[0] /= 2;
            }

            return;
        }

        Span<float> lows = row[..lowCount];
        Span<float> highs = row[lowCount..];
        JpxLifting.Scale97(lows, K);
        JpxLifting.Scale97(highs, InverseK);
        Step97(lows, highs, LowOffset(parity), Delta);
        Step97(highs, lows, HighOffset(parity), Gamma);
        Step97(lows, highs, LowOffset(parity), Beta);
        Step97(highs, lows, HighOffset(parity), Alpha);
        Span<float> target = line.AsSpan(0, row.Length);
        Interleave(MemoryMarshal.Cast<float, int>(lows), MemoryMarshal.Cast<float, int>(highs), parity, MemoryMarshal.Cast<float, int>(target));
        target.CopyTo(row);
    }

    /// <summary>One 9/7 lifting step of F-7 over a run of one parity, its neighbours found as in <see cref="Step53"/>.</summary>
    private static void Step97(Span<float> target, ReadOnlySpan<float> source, int offset, float factor)
    {
        (int first, int last) = Interior(target.Length, source.Length, offset);
        for (int i = 0; i < first; i++)
        {
            Edge(target, source, offset, factor, i);
        }

        if (last > first)
        {
            int length = last - first;
            JpxLifting.Lift97(target[first..last], source.Slice(first + offset, length), source.Slice(first + offset + 1, length), factor);
        }

        for (int i = Math.Max(first, last); i < target.Length; i++)
        {
            Edge(target, source, offset, factor, i);
        }

        static void Edge(Span<float> target, ReadOnlySpan<float> source, int offset, float factor, int i)
        {
            (int a, int b) = Neighbours(i, offset, source.Length);
            target[i] -= factor * (source[a] + source[b]);
        }
    }

    /// <summary><see cref="Rows97"/> down <paramref name="count"/> columns side by side, as <see cref="Columns53"/>.</summary>
    private static void Columns97(Span<float> columns, int height, int count, int lowCount, int parity)
    {
        if (height == 1)
        {
            if (parity == 1)
            {
                for (int c = 0; c < count; c++)
                {
                    columns[c] /= 2;
                }
            }

            return;
        }

        int highCount = height - lowCount;
        JpxLifting.Scale97(columns[..(lowCount * count)], K);
        JpxLifting.Scale97(columns[(lowCount * count)..], InverseK);
        LiftRows(columns, count, 0, lowCount, lowCount, highCount, LowOffset(parity), Delta);
        LiftRows(columns, count, lowCount, highCount, 0, lowCount, HighOffset(parity), Gamma);
        LiftRows(columns, count, 0, lowCount, lowCount, highCount, LowOffset(parity), Beta);
        LiftRows(columns, count, lowCount, highCount, 0, lowCount, HighOffset(parity), Alpha);

        static void LiftRows(Span<float> columns, int count, int targetRow, int targets, int sourceRow, int sources, int offset, float factor)
        {
            for (int i = 0; i < targets; i++)
            {
                (int left, int right) = Neighbours(i, offset, sources);
                JpxLifting.Lift97(Row(columns, targetRow + i, count), Row(columns, sourceRow + left, count), Row(columns, sourceRow + right, count), factor);
            }
        }
    }

    /// <summary>
    /// Where a low-pass sample's left neighbour is in the high-pass run, relative to its own index: at even positions (parity 0) the
    /// sample at 2i has the high-pass sample at 2i - 1, index i - 1; at odd positions the one at 2i + 1 has 2i, index i.
    /// </summary>
    private static int LowOffset(int parity) => parity == 0 ? -1 : 0;

    /// <summary>Where a high-pass sample's left neighbour is in the low-pass run, relative to its own index (as <see cref="LowOffset"/>).</summary>
    private static int HighOffset(int parity) => parity == 0 ? 0 : -1;

    /// <summary>
    /// The targets whose neighbours are both on the signal: from the first whose left neighbour index is not negative to the last
    /// whose right neighbour index is inside the run of <paramref name="sources"/>.
    /// </summary>
    private static (int First, int Last) Interior(int targets, int sources, int offset) =>
        (Math.Min(-offset, targets), Math.Min(targets, sources - offset - 1));

    /// <summary>
    /// The indices in a run of <paramref name="sources"/> of target <paramref name="i"/>'s left and right neighbours, the sample at
    /// <paramref name="i"/> + <paramref name="offset"/> and the next, each mirrored to the other where it is off the signal (F.3.7).
    /// </summary>
    private static (int Left, int Right) Neighbours(int i, int offset, int sources)
    {
        int left = i + offset;
        int right = left + 1;
        return (left >= 0 ? left : right, right < sources ? right : left);
    }

    private static Span<T> Row<T>(Span<T> columns, int row, int count) => columns.Slice(row * count, count);

    /// <summary>
    /// F.3.3 in one dimension: the low-pass samples go to the even absolute positions, the high-pass ones to the odd positions;
    /// <paramref name="parity"/> is the parity of the first position. 32-bit samples of either kind (the 9/7 floats as their bits).
    /// </summary>
    private static void Interleave(ReadOnlySpan<int> lows, ReadOnlySpan<int> highs, int parity, Span<int> target)
    {
        if (parity == 0)
        {
            JpxLifting.Interleave(lows, highs, target);
        }
        else
        {
            JpxLifting.Interleave(highs, lows, target);
        }
    }

    /// <summary>F.3.3 down the columns: copies a strip's rows, low-pass then high-pass, to the buffer's rows in interleaved order.</summary>
    private static void Scatter<T>(ReadOnlySpan<T> columns, int height, int count, int lowCount, int parity, Span<T> buffer, int stride)
    {
        int low = 0;
        int high = lowCount;
        for (int k = 0; k < height; k++)
        {
            int from = ((k + parity) & 1) == 0 ? low++ : high++;
            columns.Slice(from * count, count).CopyTo(buffer.Slice(k * stride, count));
        }
    }
}
