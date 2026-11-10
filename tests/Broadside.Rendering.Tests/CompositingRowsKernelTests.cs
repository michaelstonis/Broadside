using System.Runtime.InteropServices;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The <c>Vector128</c> compositing rows (issue #473) against the scalar ones they replace: partial-coverage fill on every (color
/// channel, coverage, backdrop channel) triple, and source-over on every (source channel, source alpha, backdrop channel) triple at
/// the coverages the rasterizer uses most plus every (channel, alpha) pair at every coverage; every short row length and
/// misalignment, the dispatchers included. In the <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware
/// intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class CompositingRowsKernelTests
{
    private delegate void FillKernel(Span<uint> target, uint color, int coverage);

    private delegate void OverKernel(ReadOnlySpan<uint> source, Span<uint> target, int coverage);

    [Fact]
    public void Partial_coverage_fill_matches_the_scalar_fill_on_every_channel_coverage_and_backdrop()
    {
        // Items are (backdrop d, color s) byte pairs; each coverage covers all 65,536, so every (s, c, d) triple is checked.
        for (int coverage = 0; coverage <= 255; coverage++)
        {
            SimdKernelAssert.ExhaustiveBytes<byte>(2, Fill(CompositingRows.FillRowScalar, coverage), Fill(CompositingRows.FillRowVector, coverage), outputsPerItem: 4);
        }

        SimdKernelAssert.ExhaustiveBytes<byte>(2, Fill(CompositingRows.FillRowScalar, 100), Fill(CompositingRows.FillRow, 100), outputsPerItem: 4);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(254)]
    [InlineData(128)]
    [InlineData(1)]
    public void Source_over_matches_the_scalar_over_on_every_channel_alpha_and_backdrop(int coverage)
    {
        // Items are (source channel s, source alpha a, backdrop d): all 2^24, premultiplied or not (s > a is clamped).
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Over(CompositingRows.OverRowScalar, coverage), Over(CompositingRows.OverRowVector, coverage), outputsPerItem: 4);
    }

    [Fact]
    public void Source_over_matches_the_scalar_over_at_every_coverage()
    {
        // Every (s, a) pair with a backdrop that varies with it, at every coverage and two outside the range.
        for (int coverage = -1; coverage <= 256; coverage++)
        {
            SimdKernelAssert.ExhaustiveBytes<byte>(2, Over(CompositingRows.OverRowScalar, coverage), Over(CompositingRows.OverRowVector, coverage), outputsPerItem: 4);
        }

        SimdKernelAssert.ExhaustiveBytes<byte>(2, Over(CompositingRows.OverRowScalar, 200), Over(CompositingRows.OverRow, 200), outputsPerItem: 4);
        SimdKernelAssert.Equal<byte, byte>(Over(CompositingRows.OverRowScalar, 77), Over(CompositingRows.OverRow, 77), SimdKernelAssert.RandomBytes(8 * 10_001, seed: 473), inputsPerItem: 8, outputsPerItem: 4);
    }

    [Fact]
    public void Source_over_reads_only_as_many_source_pixels_as_the_target_has()
    {
        uint[] source = MemoryMarshal.Cast<byte, uint>(SimdKernelAssert.RandomBytes(4 * 100, seed: 1)).ToArray();
        uint[] expected = MemoryMarshal.Cast<byte, uint>(SimdKernelAssert.RandomBytes(4 * 37, seed: 2)).ToArray();
        uint[] actual = [.. expected];

        CompositingRows.OverRowScalar(source, expected, 255);
        CompositingRows.OverRowVector(source, actual, 255);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Adapts a fill to items of (backdrop, color) bytes: each run of items with one color is one call, the color's channels and
    /// the backdrop's being different bijections of the two bytes so that a lane mix-up shows.
    /// </summary>
    private static SpanKernel<byte, byte> Fill(FillKernel kernel, int coverage) => (source, destination) =>
    {
        Span<uint> target = MemoryMarshal.Cast<byte, uint>(destination);
        int items = source.Length / 2;
        for (int i = 0; i < items; i++)
        {
            target[i] = Backdrop(source[2 * i]);
        }

        for (int start = 0; start < items;)
        {
            byte s = source[(2 * start) + 1];
            int end = start + 1;
            while (end < items && source[(2 * end) + 1] == s)
            {
                end++;
            }

            kernel(target[start..end], s | ((uint)(s ^ 0x5A) << 8) | ((uint)(255 - s) << 16) | ((uint)(s ^ 0xC3) << 24), coverage);
            start = end;
        }
    };

    /// <summary>
    /// Adapts source-over to items of (s, a, d) bytes (or of (s, a) pairs, the backdrop derived from both, or of eight bytes, two
    /// whole pixels): the source pixel holds s in its color channels (as different bijections) and a as its alpha.
    /// </summary>
    private static SpanKernel<byte, byte> Over(OverKernel kernel, int coverage) => (source, destination) =>
    {
        Span<uint> target = MemoryMarshal.Cast<byte, uint>(destination);
        int width = source.Length / Math.Max(target.Length, 1);
        uint[] pixels = new uint[target.Length];
        for (int i = 0; i < target.Length; i++)
        {
            ReadOnlySpan<byte> item = source.Slice(i * width, width);
            if (width == 8)
            {
                pixels[i] = MemoryMarshal.Read<uint>(item);
                target[i] = MemoryMarshal.Read<uint>(item[4..]);
                continue;
            }

            byte s = item[0];
            byte a = item[1];
            byte d = width == 3 ? item[2] : (byte)((s * 7) ^ (a * 13) ^ 0x5A);
            pixels[i] = s | ((uint)(s ^ 0x5A) << 8) | ((uint)(255 - s) << 16) | ((uint)a << 24);
            target[i] = Backdrop(d);
        }

        kernel(pixels, target, coverage);
    };

    private static uint Backdrop(byte d) => d | ((uint)(d ^ 0x33) << 8) | ((uint)(255 - d) << 16) | ((uint)(d ^ 0x96) << 24);
}
