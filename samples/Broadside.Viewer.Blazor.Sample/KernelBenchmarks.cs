using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using System.Runtime.Versioning;
using System.Text;
using Broadside.Filters.Dct;
using Broadside.Filters.Jpx;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The browser kernel benchmark (issue #470): times each SIMD kernel's scalar and vector methods in whichever build of the demo runs it
/// (the AOT build compiles <see cref="Vector128{T}"/> to native WebAssembly SIMD; the interpreter build maps only part of the API to
/// SIMD opcodes and can be slower than scalar), and checks that both give the same bytes there. The browser tests run it in both builds
/// and write the figures to the measurement log. A kernel PR adds its case to <see cref="Cases"/>, linking the kernel's source file
/// into this project (as <c>SimdExampleKernel</c> is linked from tests/Broadside.TestSupport, <c>JpegColor</c>, <c>InverseDct</c>, <c>JpxLifting</c>, <c>JpxLevelShift</c> and
/// <c>CmykCharacterization</c> from src/Broadside and <c>CompositingRows</c> and <c>BoxFilter</c> from src/Broadside.Rendering).
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class KernelBenchmarks
{
    /// <summary>Items per kernel call: a long row, as an image or a page-wide span.</summary>
    private const int Items = 1 << 16;

    /// <summary>The run of a JPEG 2000 lifting step: a row of the vertical pass's 64-column strip.</summary>
    private const int LiftRun = 64;

    /// <summary>The shortest timed sample: well above the browser's coarsened timer.</summary>
    private const double SampleMilliseconds = 20;

    private const int Samples = 7;

    private static readonly Case[] Cases =
    [
        new("example-multiply", 2, 1, SimdExampleKernel.MultiplyScalar, SimdExampleKernel.MultiplyVector),
        new("pack-bgra-opaque", 3, 4, PackBgraOpaqueScalar, PackBgraOpaqueVector),
        new("pack-bgra-premultiplied", 4, 4, PackBgraScalar, PackBgraVector),
        new("pack-rgba-premultiplied", 4, 4, PackRgbaScalar, PackRgbaVector),
        new("gray-to-rgb", 1, 3, GrayExpansion.ToRgbScalar, GrayExpansion.ToRgbVector),
        new("ycc-to-rgb", 3, 3, YccToRgbScalar, YccToRgbVector),
        new("ycck-to-cmyk", 4, 4, YcckToCmykScalar, YcckToCmykVector),
        new("over-row", 8, 4, OverRowScalar, OverRowVector),
        new("fill-row", 4, 4, FillRowScalar, FillRowVector),
        new("box-filter-4x4", 64, 4, BoxFilterScalar, BoxFilterVector),
        new("box-filter-run-2x2", 16, 4, BoxFilterRunScalar, BoxFilterRunVector),
        new("idct-8x8", 128, 64, IdctScalar, IdctVector),
        new("cmyk-to-rgb", 4, 12, CmykToRgbScalar, CmykToRgbVector),
        new("jpx-lift-53", 12, 4, Lift53Scalar, Lift53Vector),
        new("jpx-lift-97", 12, 4, Lift97Scalar, Lift97Vector),
        new("jpx-interleave", 8, 8, InterleaveScalar, InterleaveVector),
        new("jpx-rct", 12, 12, RctScalar, RctVector),
        new("jpx-ict", 12, 12, IctScalar, IctVector),
        new("jpx-shift", 4, 2, ShiftScalar, ShiftVector),
        new("jpx-round-shift", 4, 2, RoundShiftScalar, RoundShiftVector),
    ];

    /// <summary>The CMYK components of <c>cmyk-to-rgb</c>'s items, filled from the bytes by both methods alike.</summary>
    private static readonly float[] CmykComponents = new float[4 * Items];

    // The box filter run case's columns and weights, built for the longest run it has been given.
    private static int[] RunStarts = [];
    private static int[] RunColumns = [];
    private static double[] RunWeights = [];

    /// <summary>
    /// Runs every case and returns one line each:
    /// <c>name: scalar S ns/item, vector V ns/item, speed-up X, equal yes|no, accelerated true|false</c>.
    /// </summary>
    /// <returns>The report.</returns>
    [JSExport]
    public static string Run()
    {
        var report = new StringBuilder();
        foreach (Case kernel in Cases)
        {
            byte[] source = new byte[Items * kernel.InputsPerItem];
#pragma warning disable CA5394 // benchmark input, not security
            new Random(470).NextBytes(source);
#pragma warning restore CA5394
            byte[] expected = new byte[Items * kernel.OutputsPerItem];
            byte[] actual = new byte[Items * kernel.OutputsPerItem];
            kernel.Scalar(source, expected);
            kernel.Vector(source, actual);
            bool equal = expected.AsSpan().SequenceEqual(actual);

            // Calibrate on the scalar method (warming both), then take the best of several samples of each.
            int rounds = 1;
            while (Time(kernel.Scalar, source, expected, rounds) < SampleMilliseconds && rounds < 1 << 12)
            {
                rounds *= 2;
            }

            Time(kernel.Vector, source, actual, rounds);
            double scalar = double.MaxValue;
            double vector = double.MaxValue;
            for (int i = 0; i < Samples; i++)
            {
                scalar = Math.Min(scalar, Time(kernel.Scalar, source, expected, rounds));
                vector = Math.Min(vector, Time(kernel.Vector, source, actual, rounds));
            }

            double perItem = 1e6 / ((double)rounds * Items);
            report.Append(CultureInfo.InvariantCulture, $"{kernel.Name}: scalar {scalar * perItem:0.000} ns/item, vector {vector * perItem:0.000} ns/item, ");
            report.Append(CultureInfo.InvariantCulture, $"speed-up {scalar / vector:0.00}x, equal {(equal ? "yes" : "no")}, accelerated {(Vector128.IsHardwareAccelerated ? "true" : "false")}\n");
        }

        return report.ToString();
    }

    // The image row packing (#472): RGB bytes alone (opaque), or the RGB row followed by the opacity row.
    private static void PackBgraOpaqueScalar(ReadOnlySpan<byte> rgb, Span<byte> pixels) => PixelPacking.PackBgraScalar(rgb, default, pixels);

    private static void PackBgraOpaqueVector(ReadOnlySpan<byte> rgb, Span<byte> pixels) => PixelPacking.PackBgraVector(rgb, default, pixels);

    private static void PackBgraScalar(ReadOnlySpan<byte> rows, Span<byte> pixels) => PixelPacking.PackBgraScalar(rows[..(3 * (rows.Length / 4))], rows[(3 * (rows.Length / 4))..], pixels);

    private static void PackBgraVector(ReadOnlySpan<byte> rows, Span<byte> pixels) => PixelPacking.PackBgraVector(rows[..(3 * (rows.Length / 4))], rows[(3 * (rows.Length / 4))..], pixels);

    private static void PackRgbaScalar(ReadOnlySpan<byte> rows, Span<byte> pixels) => PixelPacking.PackRgbaScalar(rows[..(3 * (rows.Length / 4))], rows[(3 * (rows.Length / 4))..], pixels);

    private static void PackRgbaVector(ReadOnlySpan<byte> rows, Span<byte> pixels) => PixelPacking.PackRgbaVector(rows[..(3 * (rows.Length / 4))], rows[(3 * (rows.Length / 4))..], pixels);

    // The compositing rows (#473), one pixel per item. Over: the source holds the source pixels, then the backdrop, which is copied
    // into the destination first (both methods pay the copy); full coverage, as a transparency group composites onto its parent.
    private static void OverRowScalar(ReadOnlySpan<byte> pixels, Span<byte> target)
    {
        int n = pixels.Length / 2;
        pixels[n..].CopyTo(target);
        CompositingRows.OverRowScalar(MemoryMarshal.Cast<byte, uint>(pixels[..n]), MemoryMarshal.Cast<byte, uint>(target), 255);
    }

    private static void OverRowVector(ReadOnlySpan<byte> pixels, Span<byte> target)
    {
        int n = pixels.Length / 2;
        pixels[n..].CopyTo(target);
        CompositingRows.OverRowVector(MemoryMarshal.Cast<byte, uint>(pixels[..n]), MemoryMarshal.Cast<byte, uint>(target), 255);
    }

    // Fill: an opaque color over the backdrop (the source, copied into the destination first) at a partial coverage.
    private static void FillRowScalar(ReadOnlySpan<byte> backdrop, Span<byte> target)
    {
        backdrop.CopyTo(target);
        CompositingRows.FillRowScalar(MemoryMarshal.Cast<byte, uint>(target), 0xFF3366CCu, 100);
    }

    private static void FillRowVector(ReadOnlySpan<byte> backdrop, Span<byte> target)
    {
        backdrop.CopyTo(target);
        CompositingRows.FillRowVector(MemoryMarshal.Cast<byte, uint>(target), 0xFF3366CCu, 100);
    }

    // The box filter (#475): each item is one device pixel averaging a 4 × 4 footprint (the item's 16 pixels, four rows of four) with
    // fractional weights at its edges, as an image drawn at a quarter of its size; the output is the averaged pixel.
    private static void BoxFilterScalar(ReadOnlySpan<byte> footprints, Span<byte> pixels) => BoxFilterRow(footprints, pixels, vector: false);

    private static void BoxFilterVector(ReadOnlySpan<byte> footprints, Span<byte> pixels) => BoxFilterRow(footprints, pixels, vector: true);

    private static void BoxFilterRow(ReadOnlySpan<byte> footprints, Span<byte> pixels, bool vector)
    {
        ReadOnlySpan<uint> samples = MemoryMarshal.Cast<byte, uint>(footprints);
        Span<uint> averages = MemoryMarshal.Cast<byte, uint>(pixels);
        ReadOnlySpan<int> rows = [0, 4, 8, 12];
        ReadOnlySpan<int> columns = [0, 1, 2, 3];
        ReadOnlySpan<double> rowWeights = [0.4, 1, 1, 0.6];
        ReadOnlySpan<double> columnWeights = [0.7, 1, 1, 0.3];
        for (int i = 0; i < averages.Length; i++)
        {
            ReadOnlySpan<uint> footprint = samples.Slice(16 * i, 16);
            if (vector)
            {
                _ = BoxFilter.AverageVector(footprint, rows, rowWeights, columns, columnWeights, out averages[i]);
            }
            else
            {
                _ = BoxFilter.AverageScalar(footprint, rows, rowWeights, columns, columnWeights, out averages[i]);
            }
        }
    }

    // The box filter's run of pixels that share their rows: each item is one device pixel averaging a 2 × 2 footprint (the item's 4
    // pixels, two rows of two) with fractional weights, as an image drawn at about two thirds of its size; the output is the averaged
    // pixel.
    private static void BoxFilterRunScalar(ReadOnlySpan<byte> footprints, Span<byte> pixels) => BoxFilterRun(footprints, pixels, vector: false);

    private static void BoxFilterRunVector(ReadOnlySpan<byte> footprints, Span<byte> pixels) => BoxFilterRun(footprints, pixels, vector: true);

    private static void BoxFilterRun(ReadOnlySpan<byte> footprints, Span<byte> pixels, bool vector)
    {
        ReadOnlySpan<uint> samples = MemoryMarshal.Cast<byte, uint>(footprints);
        Span<uint> averages = MemoryMarshal.Cast<byte, uint>(pixels);
        int count = averages.Length;
        if (RunStarts.Length < count + 1)
        {
            RunStarts = new int[count + 1];
            RunColumns = new int[2 * count];
            RunWeights = new double[2 * count];
            for (int i = 0; i < count; i++)
            {
                RunStarts[i] = 2 * i;
                RunColumns[2 * i] = 4 * i;
                RunColumns[(2 * i) + 1] = (4 * i) + 1;
                RunWeights[2 * i] = 0.25 + ((i % 7) / 10.0);
                RunWeights[(2 * i) + 1] = 0.9 - ((i % 5) / 10.0);
            }
        }

        RunStarts[count] = 2 * count;
        ReadOnlySpan<int> rows = [0, 2];
        ReadOnlySpan<double> rowWeights = [0.65, 0.35];
        ReadOnlySpan<int> starts = RunStarts.AsSpan(0, count + 1);
        ReadOnlySpan<int> columns = RunColumns.AsSpan(0, 2 * count);
        ReadOnlySpan<double> weights = RunWeights.AsSpan(0, 2 * count);
        _ = vector
            ? BoxFilter.AverageRunVector(samples, rows, rowWeights, starts, columns, weights, averages)
            : BoxFilter.AverageRunScalar(samples, rows, rowWeights, starts, columns, weights, averages);
    }

    // The CMYK characterization (#476): each item is one 8-bit CMYK color (b / 255 per component, as an image's samples reach it), the
    // output its three RGB floats.
    private static void CmykToRgbScalar(ReadOnlySpan<byte> samples, Span<byte> rgb) => CmykToRgb(samples, rgb, vector: false);

    private static void CmykToRgbVector(ReadOnlySpan<byte> samples, Span<byte> rgb) => CmykToRgb(samples, rgb, vector: true);

    private static void CmykToRgb(ReadOnlySpan<byte> samples, Span<byte> rgb, bool vector)
    {
        Span<float> cmyk = CmykComponents.AsSpan(0, samples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            cmyk[i] = (float)(samples[i] / 255.0);
        }

        Span<float> colors = MemoryMarshal.Cast<byte, float>(rgb);
        if (vector)
        {
            CmykCharacterization.ToRgbRowVector(cmyk, colors);
        }
        else
        {
            CmykCharacterization.ToRgbRowScalar(cmyk, colors);
        }
    }

    // The DCT colour transforms (#471) over a source holding the Y, Cb, Cr (and K) rows one after another.
    private static void YccToRgbScalar(ReadOnlySpan<byte> rows, Span<byte> rgb)
    {
        int n = rows.Length / 3;
        JpegColor.YccToRgbScalar(rows[..n], rows[n..(2 * n)], rows[(2 * n)..], rgb);
    }

    private static void YccToRgbVector(ReadOnlySpan<byte> rows, Span<byte> rgb)
    {
        int n = rows.Length / 3;
        JpegColor.YccToRgbVector(rows[..n], rows[n..(2 * n)], rows[(2 * n)..], rgb);
    }

    private static void YcckToCmykScalar(ReadOnlySpan<byte> rows, Span<byte> cmyk)
    {
        int n = rows.Length / 4;
        JpegColor.YcckToCmykScalar(rows[..n], rows[n..(2 * n)], rows[(2 * n)..(3 * n)], rows[(3 * n)..], cmyk);
    }

    private static void YcckToCmykVector(ReadOnlySpan<byte> rows, Span<byte> cmyk)
    {
        int n = rows.Length / 4;
        JpegColor.YcckToCmykVector(rows[..n], rows[n..(2 * n)], rows[(2 * n)..(3 * n)], rows[(3 * n)..], cmyk);
    }

    // The inverse DCT (#474): each item is one block, two source bytes per coefficient (the first deciding by frequency whether it is
    // zero, as in a quantized photograph, the second its value), the T.81 Table K.1 luminance table; the output is the 8 x 8 samples.
    private static ReadOnlySpan<int> Luminance =>
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static void IdctScalar(ReadOnlySpan<byte> source, Span<byte> samples) => Idct(source, samples, vector: false);

    private static void IdctVector(ReadOnlySpan<byte> source, Span<byte> samples) => Idct(source, samples, vector: true);

    private static void Idct(ReadOnlySpan<byte> source, Span<byte> samples, bool vector)
    {
        // The blocks are shaped once per source (the benchmark passes the same array every round) so only the transform is timed.
        short[] blocks = IdctBlocks.For(source);
        for (int i = 0; i < blocks.Length / 64; i++)
        {
            if (vector)
            {
                InverseDct.TransformVector(blocks.AsSpan(64 * i, 64), Luminance, samples.Slice(64 * i, 64), 8);
            }
            else
            {
                InverseDct.TransformScalar(blocks.AsSpan(64 * i, 64), Luminance, samples.Slice(64 * i, 64), 8);
            }
        }
    }

    // The JPEG 2000 lifting steps and component transforms (#477). The source holds three planes one after another (a step's targets,
    // left and right neighbours; or the three components), copied into the destination first (both methods pay the copy). The steps
    // run in 64-sample runs, as the vertical pass gives them a strip row at a time. The 5/3 and RCT planes are the source's 32-bit
    // integers; the 9/7 and ICT planes floats made from the source's 16-bit integers over 16, once per source (FloatPlanes).

    private static void Lift53Scalar(ReadOnlySpan<byte> planes, Span<byte> target) => Lift53(planes, target, vector: false);

    private static void Lift53Vector(ReadOnlySpan<byte> planes, Span<byte> target) => Lift53(planes, target, vector: true);

    private static void Lift53(ReadOnlySpan<byte> planes, Span<byte> target, bool vector)
    {
        ReadOnlySpan<int> values = MemoryMarshal.Cast<byte, int>(planes);
        int n = values.Length / 3;
        Span<int> samples = MemoryMarshal.Cast<byte, int>(target);
        values[..n].CopyTo(samples);
        for (int i = 0; i < n; i += LiftRun)
        {
            int length = Math.Min(LiftRun, n - i);
            if (vector)
            {
                JpxLifting.Lift53LowVector(samples.Slice(i, length), values.Slice(n + i, length), values.Slice((2 * n) + i, length));
            }
            else
            {
                JpxLifting.Lift53LowScalar(samples.Slice(i, length), values.Slice(n + i, length), values.Slice((2 * n) + i, length));
            }
        }
    }

    private static void Lift97Scalar(ReadOnlySpan<byte> planes, Span<byte> target) => Lift97(planes, target, vector: false);

    private static void Lift97Vector(ReadOnlySpan<byte> planes, Span<byte> target) => Lift97(planes, target, vector: true);

    private static void Lift97(ReadOnlySpan<byte> planes, Span<byte> target, bool vector)
    {
        float[] values = FloatPlanes.For(planes);
        int n = values.Length / 3;
        Span<float> samples = MemoryMarshal.Cast<byte, float>(target);
        values.AsSpan(0, n).CopyTo(samples);
        for (int i = 0; i < n; i += LiftRun)
        {
            int length = Math.Min(LiftRun, n - i);
            if (vector)
            {
                JpxLifting.Lift97Vector(samples.Slice(i, length), values.AsSpan(n + i, length), values.AsSpan((2 * n) + i, length), 0.882911075530934f);
            }
            else
            {
                JpxLifting.Lift97Scalar(samples.Slice(i, length), values.AsSpan(n + i, length), values.AsSpan((2 * n) + i, length), 0.882911075530934f);
            }
        }
    }

    // The interleave of a row's low-pass and high-pass halves (the source's two planes of 32-bit samples) into the signal.
    private static void InterleaveScalar(ReadOnlySpan<byte> planes, Span<byte> target)
    {
        ReadOnlySpan<int> values = MemoryMarshal.Cast<byte, int>(planes);
        JpxLifting.InterleaveScalar(values[..(values.Length / 2)], values[(values.Length / 2)..], MemoryMarshal.Cast<byte, int>(target));
    }

    private static void InterleaveVector(ReadOnlySpan<byte> planes, Span<byte> target)
    {
        ReadOnlySpan<int> values = MemoryMarshal.Cast<byte, int>(planes);
        JpxLifting.InterleaveVector(values[..(values.Length / 2)], values[(values.Length / 2)..], MemoryMarshal.Cast<byte, int>(target));
    }

    private static void RctScalar(ReadOnlySpan<byte> planes, Span<byte> target) => Rct(planes, target, vector: false);

    private static void RctVector(ReadOnlySpan<byte> planes, Span<byte> target) => Rct(planes, target, vector: true);

    private static void Rct(ReadOnlySpan<byte> planes, Span<byte> target, bool vector)
    {
        planes.CopyTo(target);
        Span<int> samples = MemoryMarshal.Cast<byte, int>(target);
        int n = samples.Length / 3;
        if (vector)
        {
            JpxLifting.InverseRctVector(samples[..n], samples[n..(2 * n)], samples[(2 * n)..]);
        }
        else
        {
            JpxLifting.InverseRctScalar(samples[..n], samples[n..(2 * n)], samples[(2 * n)..]);
        }
    }

    private static void IctScalar(ReadOnlySpan<byte> planes, Span<byte> target) => Ict(planes, target, vector: false);

    private static void IctVector(ReadOnlySpan<byte> planes, Span<byte> target) => Ict(planes, target, vector: true);

    private static void Ict(ReadOnlySpan<byte> planes, Span<byte> target, bool vector)
    {
        Span<float> samples = MemoryMarshal.Cast<byte, float>(target);
        FloatPlanes.For(planes).CopyTo(samples);
        int n = samples.Length / 3;
        if (vector)
        {
            JpxLifting.InverseIctVector(samples[..n], samples[n..(2 * n)], samples[(2 * n)..]);
        }
        else
        {
            JpxLifting.InverseIctScalar(samples[..n], samples[n..(2 * n)], samples[(2 * n)..]);
        }
    }

    // The JPEG 2000 DC level shift (#503) of a 12-bit component: the samples (5/3) or values (9/7) as for the ICT, in range.
    private static void ShiftScalar(ReadOnlySpan<byte> values, Span<byte> target) =>
        JpxLevelShift.ShiftScalar(FloatPlanes.IntsFor(values), MemoryMarshal.Cast<byte, ushort>(target), 2048, 4095);

    private static void ShiftVector(ReadOnlySpan<byte> values, Span<byte> target) =>
        JpxLevelShift.ShiftVector(FloatPlanes.IntsFor(values), MemoryMarshal.Cast<byte, ushort>(target), 2048, 4095);

    private static void RoundShiftScalar(ReadOnlySpan<byte> values, Span<byte> target) =>
        JpxLevelShift.RoundShiftScalar(FloatPlanes.For(values), MemoryMarshal.Cast<byte, ushort>(target), 2048, 4095);

    private static void RoundShiftVector(ReadOnlySpan<byte> values, Span<byte> target) =>
        JpxLevelShift.RoundShiftVector(FloatPlanes.For(values), MemoryMarshal.Cast<byte, ushort>(target), 2048, 4095);

    private static double Time(SpanKernel<byte, byte> kernel, byte[] source, byte[] destination, int rounds)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < rounds; i++)
        {
            kernel(source, destination);
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static class IdctBlocks
    {
        private static byte[] s_source = [];
        private static short[] s_blocks = [];

        public static short[] For(ReadOnlySpan<byte> source)
        {
            if (s_blocks.Length != source.Length / 2 || !source[..Math.Min(64, source.Length)].SequenceEqual(s_source))
            {
                s_source = source[..Math.Min(64, source.Length)].ToArray();
                s_blocks = new short[source.Length / 2];
                for (int i = 0; i < s_blocks.Length; i++)
                {
                    int position = i % 64;
                    int frequency = (position >> 3) + (position & 7);
                    s_blocks[i] = source[2 * i] < 256 >> Math.Min(frequency, 8) ? (short)((sbyte)source[(2 * i) + 1] >> 2) : (short)0;
                }
            }

            return s_blocks;
        }
    }

    private static class FloatPlanes
    {
        private static byte[] s_source = [];
        private static float[] s_values = [];
        private static float[] s_intsOf = [];
        private static int[] s_ints = [];

        public static float[] For(ReadOnlySpan<byte> source)
        {
            if (s_values.Length != source.Length / 4 || !source[..Math.Min(64, source.Length)].SequenceEqual(s_source))
            {
                s_source = source[..Math.Min(64, source.Length)].ToArray();
                s_values = new float[source.Length / 4];
                for (int i = 0; i < s_values.Length; i++)
                {
                    s_values[i] = BitConverter.ToInt16(source.Slice(4 * i, 2)) / 16f;
                }
            }

            return s_values;
        }

        /// <summary>The same values truncated to integers.</summary>
        public static int[] IntsFor(ReadOnlySpan<byte> source)
        {
            float[] values = For(source);
            if (s_ints.Length != values.Length || s_intsOf != values)
            {
                s_intsOf = values;
                s_ints = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    s_ints[i] = (int)values[i];
                }
            }

            return s_ints;
        }
    }

    private sealed record Case(string Name, int InputsPerItem, int OutputsPerItem, SpanKernel<byte, byte> Scalar, SpanKernel<byte, byte> Vector);
}
