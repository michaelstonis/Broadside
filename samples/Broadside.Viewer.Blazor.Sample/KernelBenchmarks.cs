using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using System.Runtime.Versioning;
using System.Text;
using Broadside.Graphics.Colors;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The browser kernel benchmark (issue #470): times each SIMD kernel's scalar and vector methods in whichever build of the demo runs it
/// (the AOT build compiles <see cref="Vector128{T}"/> to native WebAssembly SIMD; the interpreter build maps only part of the API to
/// SIMD opcodes and can be slower than scalar), and checks that both give the same bytes there. The browser tests run it in both builds
/// and write the figures to the measurement log. A kernel PR adds its case to <see cref="Cases"/>, linking the kernel's source file
/// into this project (as <c>SimdExampleKernel</c> is linked from tests/Broadside.TestSupport).
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class KernelBenchmarks
{
    /// <summary>Items per kernel call: a long row, as an image or a page-wide span.</summary>
    private const int Items = 1 << 16;

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
    ];

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

    private static double Time(SpanKernel<byte, byte> kernel, byte[] source, byte[] destination, int rounds)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < rounds; i++)
        {
            kernel(source, destination);
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private sealed record Case(string Name, int InputsPerItem, int OutputsPerItem, SpanKernel<byte, byte> Scalar, SpanKernel<byte, byte> Vector);
}
