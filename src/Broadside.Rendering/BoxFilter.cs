using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Broadside.Rendering;

/// <summary>
/// The box filter of the image sampler (issue #475): the weighted average of the samples under a device pixel's footprint, each
/// sample's weight the product of its column's and its row's, as a scalar method (the reference), a <see cref="Vector128{T}"/> method
/// that matches it bit for bit, and a dispatcher that takes the vector method where <see cref="Vector128.IsHardwareAccelerated"/>
/// (outside the browser).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5.3 and §10.7.4: a reduced image averages every sample its pixel's footprint covers, so it neither aliases nor
/// drops detail. <see cref="ImagePainter"/> works out the footprint's columns, rows and weights and calls <see cref="Average"/> for
/// every device pixel of a reduced image.
/// </para>
/// <para>
/// The vector method keeps a lane per channel: blue and green in one <c>Vector128&lt;double&gt;</c>, red and alpha in another. Each
/// lane does the scalar method's operations in its order (the weight times the channel, added to the running sum, in
/// <see langword="double"/>; no fused multiply-add), so the sums, and the rounded channels, are the same bits. A sample reaches the
/// lanes as exact doubles without a conversion instruction: OR'd under the top bytes of 2^52 and broadcast, one constant byte
/// <c>Shuffle</c> puts each channel at the bottom of a 64-bit lane under those bytes (the double 2^52 + n), and a subtraction of 2^52
/// leaves n. Only operations that are fast in every build Broadside runs in (adding-a-benchmark skill, "SIMD kernels"). The sums
/// depend each on the one before, so the vector path saves operations rather than latency: about 1.15x on Arm64, and not faster in
/// either browser build, where the dispatcher keeps the scalar path. The file is BCL-only, so the Blazor sample's kernel benchmark
/// links it.
/// </para>
/// <para>
/// For the small footprints of an image drawn a little smaller than its resolution (1 × 2 to 3 × 3 samples), the fixed cost of each
/// call outweighed the samples. <see cref="AverageRun"/> averages a whole run of pixels that share their rows in one call (issue
/// #511): no test of each weight, and in the vector path the rounding done in the lanes. Both paths of it are faster than the per-pixel
/// call in every build, the browser's included, so the browser takes its scalar path.
/// </para>
/// </remarks>
internal static class BoxFilter
{
    /// <summary>The most columns or rows a footprint takes: 16 evenly spaced samples, or every sample of a span up to 16 wide (17 when it starts and ends mid-sample).</summary>
    public const int MaxTaps = 17;

    /// <summary>2^52: a 64-bit lane holding n below 2^32 OR'd into its bits reads as the double 2^52 + n.</summary>
    private const ulong Magic = 0x4330000000000000;

    /// <summary>
    /// The weighted average of <paramref name="pixels"/> at every row of <paramref name="rows"/> and column of
    /// <paramref name="columns"/>: the vector path where <see cref="Vector128"/> is accelerated outside the browser, else the scalar
    /// one. In the browser the vector path is slower than scalar in both the AOT build and the interpreter
    /// (docs/benchmarks/simd-kernels.md), so both keep the scalar path.
    /// </summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight, at least as many as <paramref name="rows"/>; a row of weight 0 or less is skipped.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight, at least as many as <paramref name="columns"/>; a sample whose weight (column times row) is 0 or less is skipped.</param>
    /// <param name="result">The average, each channel rounded to the nearest of 0 to 255; 0 when the weights sum to 0 or less.</param>
    /// <returns>Whether the weights sum to more than 0.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Average(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, out uint result) =>
        Vector128.IsHardwareAccelerated && !OperatingSystem.IsBrowser()
            ? AverageVector(pixels, rows, rowWeights, columns, columnWeights, out result)
            : AverageScalar(pixels, rows, rowWeights, columns, columnWeights, out result);

    /// <summary>The scalar <see cref="Average"/>: four <see langword="double"/> sums, one per channel.</summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight.</param>
    /// <param name="result">The average.</param>
    /// <returns>Whether the weights sum to more than 0.</returns>
    public static bool AverageScalar(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, out uint result)
    {
        rowWeights = rowWeights[..rows.Length];
        columnWeights = columnWeights[..columns.Length];

        // The column loops read columns and columnWeights through refs at i < columns.Length, inside both (columnWeights is sliced
        // to that length above): no bounds check per sample. The sample itself is read with one.
        ref int column = ref MemoryMarshal.GetReference(columns);
        ref double columnWeight = ref MemoryMarshal.GetReference(columnWeights);
        double b = 0;
        double g = 0;
        double r = 0;
        double a = 0;
        double total = 0;
        for (int j = 0; j < rows.Length; j++)
        {
            double wy = rowWeights[j];
            if (wy <= 0)
            {
                continue;
            }

            int offset = rows[j];
            for (int i = 0; i < columns.Length; i++)
            {
                double w = Unsafe.Add(ref columnWeight, i) * wy;
                if (w <= 0)
                {
                    continue;
                }

                uint pixel = pixels[offset + Unsafe.Add(ref column, i)];
                b += w * (pixel & 0xFF);
                g += w * ((pixel >> 8) & 0xFF);
                r += w * ((pixel >> 16) & 0xFF);
                a += w * (pixel >> 24);
                total += w;
            }
        }

        if (total <= 0)
        {
            result = 0;
            return false;
        }

        double scale = 1 / total;
        result = Channel(b * scale) | (Channel(g * scale) << 8) | (Channel(r * scale) << 16) | (Channel(a * scale) << 24);
        return true;
    }

    /// <summary>The <see cref="Vector128{T}"/> <see cref="Average"/>: blue and green in one vector of two doubles, red and alpha in another.</summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight.</param>
    /// <param name="result">The average.</param>
    /// <returns>Whether the weights sum to more than 0.</returns>
    public static bool AverageVector(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, out uint result)
    {
        rowWeights = rowWeights[..rows.Length];
        columnWeights = columnWeights[..columns.Length];

        // The column loops read columns and columnWeights through refs at i < columns.Length, inside both (columnWeights is sliced
        // to that length above): no bounds check per sample. The sample itself is read with one.
        ref int column = ref MemoryMarshal.GetReference(columns);
        ref double columnWeight = ref MemoryMarshal.GetReference(columnWeights);
        Vector128<double> bias = Vector128.Create(Magic).AsDouble();
        Vector128<double> bg = Vector128<double>.Zero;
        Vector128<double> ra = Vector128<double>.Zero;
        double total = 0;
        for (int j = 0; j < rows.Length; j++)
        {
            double wy = rowWeights[j];
            if (wy <= 0)
            {
                continue;
            }

            int offset = rows[j];
            for (int i = 0; i < columns.Length; i++)
            {
                double w = Unsafe.Add(ref columnWeight, i) * wy;
                if (w <= 0)
                {
                    continue;
                }

                // The sample with 0x4330 above it, in both 64-bit lanes: bytes 0 to 3 are blue, green, red and alpha, 6 and 7 the
                // top of 2^52. Vector128.Create writes the whole register (inserting into one lane would chain each sample's
                // conversion to the one before it through that register).
                Vector128<byte> pixel = Vector128.Create(Magic | pixels[offset + Unsafe.Add(ref column, i)]).AsByte();
                Vector128<double> weight = Vector128.Create(w);

                // Blue and green, then red and alpha, each as the double 2^52 + n: the channel's byte at the bottom of a 64-bit
                // lane, 2^52's two bytes at the top (bytes 6 and 7, or 14 and 15), zeros between (index 255). Literal masks, so that
                // every compiler sees a constant shuffle.
                Vector128<byte> blueGreen = Vector128.Shuffle(pixel, Vector128.Create((byte)0, 255, 255, 255, 255, 255, 6, 7, 1, 255, 255, 255, 255, 255, 14, 15));
                Vector128<byte> redAlpha = Vector128.Shuffle(pixel, Vector128.Create((byte)2, 255, 255, 255, 255, 255, 6, 7, 3, 255, 255, 255, 255, 255, 14, 15));
                bg += weight * (blueGreen.AsDouble() - bias);
                ra += weight * (redAlpha.AsDouble() - bias);
                total += w;
            }
        }

        if (total <= 0)
        {
            result = 0;
            return false;
        }

        double scale = 1 / total;
        result = Channel(bg.ToScalar() * scale) | (Channel(bg.GetElement(1) * scale) << 8) | (Channel(ra.ToScalar() * scale) << 16) | (Channel(ra.GetElement(1) * scale) << 24);
        return true;
    }

    /// <summary>
    /// <see cref="Average"/> for a run of device pixels that take the same rows: pixel p's columns and their weights are
    /// <paramref name="columns"/> and <paramref name="columnWeights"/> from <c>starts[p]</c> to <c>starts[p + 1]</c>. Writes each
    /// average to <paramref name="averages"/> and stops at the first pixel whose weights sum to 0, which it leaves unwritten. The
    /// vector path where <see cref="Vector128"/> is accelerated outside the browser, else the scalar one.
    /// </summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight, at least as many as <paramref name="rows"/>; finite and not negative.</param>
    /// <param name="starts">Where each pixel's columns start, and after the last pixel's, where they end: one more than <paramref name="averages"/>.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight, as many as <paramref name="columns"/>; finite and not negative.</param>
    /// <param name="averages">Each pixel's average, as <see cref="Average"/> works it out.</param>
    /// <returns>How many pixels were averaged: all of them, or the index of the first whose weights sum to 0.</returns>
    /// <remarks>
    /// With no weight negative or infinite, a sample whose weight (column times row) is 0 adds +0 to sums that start at +0 and never go
    /// below it, which leaves them as they are; so the samples are taken without <see cref="Average"/>'s test of each weight, and the
    /// sums, and the averages, are the same bits.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AverageRun(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> starts, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, Span<uint> averages) =>
        Vector128.IsHardwareAccelerated && !OperatingSystem.IsBrowser()
            ? AverageRunVector(pixels, rows, rowWeights, starts, columns, columnWeights, averages)
            : AverageRunScalar(pixels, rows, rowWeights, starts, columns, columnWeights, averages);

    /// <summary>The scalar <see cref="AverageRun"/>: <see cref="AverageScalar"/> per pixel, without its test of each weight.</summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight; finite and not negative.</param>
    /// <param name="starts">Where each pixel's columns start, and where the last pixel's end.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight; finite and not negative.</param>
    /// <param name="averages">Each pixel's average.</param>
    /// <returns>How many pixels were averaged.</returns>
    public static int AverageRunScalar(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> starts, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, Span<uint> averages)
    {
        rowWeights = rowWeights[..rows.Length];
        starts = starts[..(averages.Length + 1)];
        columnWeights = columnWeights[..columns.Length];
        for (int p = 0; p < averages.Length; p++)
        {
            // Slicing checks the pixel's columns against both spans once; the loop reads them through refs at i < length.
            int start = starts[p];
            int length = starts[p + 1] - start;
            ref int column = ref MemoryMarshal.GetReference(columns.Slice(start, length));
            ref double columnWeight = ref MemoryMarshal.GetReference(columnWeights.Slice(start, length));
            double b = 0;
            double g = 0;
            double r = 0;
            double a = 0;
            double total = 0;
            for (int j = 0; j < rows.Length; j++)
            {
                double wy = rowWeights[j];
                int offset = rows[j];
                for (int i = 0; i < length; i++)
                {
                    double w = Unsafe.Add(ref columnWeight, i) * wy;
                    uint pixel = pixels[offset + Unsafe.Add(ref column, i)];
                    b += w * (pixel & 0xFF);
                    g += w * ((pixel >> 8) & 0xFF);
                    r += w * ((pixel >> 16) & 0xFF);
                    a += w * (pixel >> 24);
                    total += w;
                }
            }

            if (total <= 0)
            {
                return p;
            }

            double scale = 1 / total;
            averages[p] = Channel(b * scale) | (Channel(g * scale) << 8) | (Channel(r * scale) << 16) | (Channel(a * scale) << 24);
        }

        return averages.Length;
    }

    /// <summary>
    /// The <see cref="Vector128{T}"/> <see cref="AverageRun"/>: <see cref="AverageVector"/> per pixel, without its test of each weight,
    /// and with the channels rounded in the vectors.
    /// </summary>
    /// <param name="pixels">The image's samples, premultiplied BGRA, row after row.</param>
    /// <param name="rows">The index in <paramref name="pixels"/> of each row's first sample.</param>
    /// <param name="rowWeights">Each row's weight; finite and not negative.</param>
    /// <param name="starts">Where each pixel's columns start, and where the last pixel's end.</param>
    /// <param name="columns">The column of each sample in its row.</param>
    /// <param name="columnWeights">Each column's weight; finite and not negative.</param>
    /// <param name="averages">Each pixel's average.</param>
    /// <returns>How many pixels were averaged.</returns>
    /// <remarks>
    /// The scalar rounding is <c>Math.Clamp((int)(x + 0.5), 0, 255)</c>, x the channel's sum times the scale. Here x + 0.5 is computed
    /// in each lane by the same two operations, then kept where it is greater than 0 and set to 0 elsewhere (so a NaN, which the scalar
    /// conversion takes to 0, gives 0 too), limited to 255, and floored: for a value of 0 or more, the floor is the truncation the scalar
    /// conversion does, and truncating then clamping to 0 to 255 is clamping then truncating, as both bounds are integers. The floored
    /// value plus 2^52 is that integer in the low bytes of its lane, from which one byte <c>Shuffle</c> each gathers the four channels.
    /// </remarks>
    public static int AverageRunVector(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> starts, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, Span<uint> averages)
    {
        rowWeights = rowWeights[..rows.Length];
        starts = starts[..(averages.Length + 1)];
        columnWeights = columnWeights[..columns.Length];
        Vector128<double> bias = Vector128.Create(Magic).AsDouble();
        for (int p = 0; p < averages.Length; p++)
        {
            // Slicing checks the pixel's columns against both spans once; the loop reads them through refs at i < length.
            int start = starts[p];
            int length = starts[p + 1] - start;
            ref int column = ref MemoryMarshal.GetReference(columns.Slice(start, length));
            ref double columnWeight = ref MemoryMarshal.GetReference(columnWeights.Slice(start, length));
            Vector128<double> bg = Vector128<double>.Zero;
            Vector128<double> ra = Vector128<double>.Zero;
            double total = 0;
            for (int j = 0; j < rows.Length; j++)
            {
                double wy = rowWeights[j];
                int offset = rows[j];
                for (int i = 0; i < length; i++)
                {
                    // As in AverageVector.
                    double w = Unsafe.Add(ref columnWeight, i) * wy;
                    Vector128<byte> pixel = Vector128.Create(Magic | pixels[offset + Unsafe.Add(ref column, i)]).AsByte();
                    Vector128<double> weight = Vector128.Create(w);
                    Vector128<byte> blueGreen = Vector128.Shuffle(pixel, Vector128.Create((byte)0, 255, 255, 255, 255, 255, 6, 7, 1, 255, 255, 255, 255, 255, 14, 15));
                    Vector128<byte> redAlpha = Vector128.Shuffle(pixel, Vector128.Create((byte)2, 255, 255, 255, 255, 255, 6, 7, 3, 255, 255, 255, 255, 255, 14, 15));
                    bg += weight * (blueGreen.AsDouble() - bias);
                    ra += weight * (redAlpha.AsDouble() - bias);
                    total += w;
                }
            }

            if (total <= 0)
            {
                return p;
            }

            Vector128<double> scale = Vector128.Create(1 / total);
            Vector128<double> half = Vector128.Create(0.5);
            Vector128<double> top = Vector128.Create(255.0);
            Vector128<double> blueGreenOut = (bg * scale) + half;
            Vector128<double> redAlphaOut = (ra * scale) + half;
            blueGreenOut = Vector128.Floor(Vector128.Min(blueGreenOut & Vector128.GreaterThan(blueGreenOut, Vector128<double>.Zero), top)) + bias;
            redAlphaOut = Vector128.Floor(Vector128.Min(redAlphaOut & Vector128.GreaterThan(redAlphaOut, Vector128<double>.Zero), top)) + bias;

            // Blue and green are bytes 0 and 8 of the first, red and alpha bytes 0 and 8 of the second.
            Vector128<byte> low = Vector128.Shuffle(blueGreenOut.AsByte(), Vector128.Create((byte)0, 8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255));
            Vector128<byte> high = Vector128.Shuffle(redAlphaOut.AsByte(), Vector128.Create((byte)255, 255, 0, 8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255));
            averages[p] = (low | high).AsUInt32().ToScalar();
        }

        return averages.Length;
    }

    private static uint Channel(double value) => (uint)Math.Clamp((int)(value + 0.5), 0, 255);
}

