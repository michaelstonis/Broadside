using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Intrinsics;
using System.Runtime.Versioning;
using System.Text;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The browser kernel benchmark (issue #470): times each SIMD kernel's scalar and vector methods in whichever build of the demo runs it
/// (the AOT build compiles <see cref="Vector128{T}"/> to native WebAssembly SIMD; the interpreter build maps only part of the API to
/// SIMD opcodes and can be slower than scalar), and checks that both give the same bytes there. The browser tests run it in both builds
/// and write the figures to the measurement log. A kernel PR adds its case to <see cref="Cases"/>, linking the kernel's source file
/// into this project (as <c>SimdExampleKernel</c> is linked from tests/Broadside.TestSupport and <c>CompositingRows</c> from
/// src/Broadside.Rendering).
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
        new("over-row", 8, 4, OverRowScalar, OverRowVector),
        new("fill-row", 4, 4, FillRowScalar, FillRowVector),
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

    // Fill: an opaque colour over the backdrop (the source, copied into the destination first) at a partial coverage.
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
