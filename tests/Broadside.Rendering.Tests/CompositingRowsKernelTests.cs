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

    private delegate void MixKernel(Span<uint> target, ReadOnlySpan<uint> to, int amount);

    private delegate void KnockOutKernel(ReadOnlySpan<uint> previous, Span<uint> composited, ReadOnlySpan<uint> backdrop, int shape);

    private delegate void RemoveBackdropKernel(ReadOnlySpan<uint> complete, ReadOnlySpan<uint> backdrop, ReadOnlySpan<byte> groupAlpha, Span<uint> destination);

    private delegate void ScaleKernel(ReadOnlySpan<byte> source, Span<byte> target, int coverage);

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
    public void Source_over_matches_the_scalar_over_on_runs_of_opaque_and_clear_source_pixels()
    {
        // Items are whole (source, backdrop) pixels. Runs of opaque and of clear source pixels, of lengths that do and do not line up
        // with the vector's four pixels, take its shortcuts at full coverage (issue #603); the rest stay random.
        const int items = 4_099;
        byte[] pixels = SimdKernelAssert.RandomBytes(8 * items, seed: 6031);
        byte[] runs = SimdKernelAssert.RandomBytes(2 * items, seed: 6032);
        for (int i = 0, r = 0; i < items; r++)
        {
            int run = 1 + (runs[2 * r] % 12);
            int kind = runs[(2 * r) + 1] % 3;
            for (int k = i; k < Math.Min(i + run, items); k++)
            {
                Span<byte> source = pixels.AsSpan(8 * k, 4);
                if (kind == 0)
                {
                    source[3] = 255;
                }
                else if (kind == 1)
                {
                    source.Clear();
                }
            }

            i += run;
        }

        foreach (int coverage in new[] { 255, 254, 128 })
        {
            SimdKernelAssert.Equal<byte, byte>(Over(CompositingRows.OverRowScalar, coverage), Over(CompositingRows.OverRowVector, coverage), pixels, inputsPerItem: 8, outputsPerItem: 4);
        }
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

    [Fact]
    public void Mix_matches_the_scalar_mix_on_every_channel_pair_and_amount()
    {
        // Items are (from, to) byte pairs; each amount covers all 65,536, and the amounts outside 1 to 254 take the scalar path.
        for (int amount = -1; amount <= 256; amount++)
        {
            SimdKernelAssert.ExhaustiveBytes<byte>(2, Mix(CompositingRows.MixRowScalar, amount), Mix(CompositingRows.MixRowVector, amount), outputsPerItem: 4);
        }

        SimdKernelAssert.ExhaustiveBytes<byte>(2, Mix(CompositingRows.MixRowScalar, 90), Mix(CompositingRows.MixRow, 90), outputsPerItem: 4);
        SimdKernelAssert.Equal<byte, byte>(Mix(CompositingRows.MixRowScalar, 201), Mix(CompositingRows.MixRow, 201), SimdKernelAssert.RandomBytes(8 * 10_001, seed: 4731), inputsPerItem: 8, outputsPerItem: 4);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(64)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(253)]
    [InlineData(254)]
    public void Knockout_matches_the_scalar_knockout_on_every_channel_triple(int shape)
    {
        // Items are (previous, composited, backdrop) bytes: all 2^24, once with the alpha the byte and the colors bijections of it
        // (the colors then meet their alpha), once with the colors the byte in every channel and the alpha a bijection.
        foreach (bool alphaIsByte in new[] { true, false })
        {
            SimdKernelAssert.ExhaustiveBytes<byte>(3, KnockOut(CompositingRows.KnockOutRowScalar, shape, alphaIsByte), KnockOut(CompositingRows.KnockOutRowVector, shape, alphaIsByte), outputsPerItem: 4);
        }
    }

    [Fact]
    public void Knockout_matches_the_scalar_knockout_on_whole_pixels_at_every_shape()
    {
        // Random whole pixels at all shapes, outside 1 to 254 (the scalar path) included; a third have the backdrop the group held.
        byte[] pixels = SimdKernelAssert.RandomBytes(12 * 4_099, seed: 4732);
        for (int i = 0; i < 4_099; i += 3)
        {
            pixels.AsSpan(12 * i, 4).CopyTo(pixels.AsSpan((12 * i) + 8, 4));
        }

        for (int shape = -1; shape <= 256; shape++)
        {
            SimdKernelAssert.Equal<byte, byte>(WholeKnockOut(CompositingRows.KnockOutRowScalar, shape), WholeKnockOut(CompositingRows.KnockOutRowVector, shape), pixels, inputsPerItem: 12, outputsPerItem: 4);
        }

        SimdKernelAssert.Equal<byte, byte>(WholeKnockOut(CompositingRows.KnockOutRowScalar, 99), WholeKnockOut(CompositingRows.KnockOutRow, 99), pixels, inputsPerItem: 12, outputsPerItem: 4);
    }

    [Fact]
    public void Backdrop_removal_matches_the_scalar_removal_on_every_channel_pair_and_group_alpha()
    {
        // Items are (complete, backdrop, group alpha) bytes: all 2^24; the alpha channels are other bijections of the same bytes.
        SimdKernelAssert.ExhaustiveBytes<byte>(3, RemoveBackdrop(CompositingRows.RemoveBackdropRowScalar), RemoveBackdrop(CompositingRows.RemoveBackdropRowVector), outputsPerItem: 4);
        SimdKernelAssert.Equal<byte, byte>(RemoveBackdropWhole(CompositingRows.RemoveBackdropRowScalar), RemoveBackdropWhole(CompositingRows.RemoveBackdropRow), SimdKernelAssert.RandomBytes(9 * 10_001, seed: 4733), inputsPerItem: 9, outputsPerItem: 4);
    }

    [Fact]
    public void Mask_scaling_matches_the_scalar_scaling_on_every_value_and_coverage()
    {
        for (int coverage = -1; coverage <= 256; coverage++)
        {
            SimdKernelAssert.ExhaustiveBytes<byte>(1, Scale(CompositingRows.ScaleRowScalar, coverage), Scale(CompositingRows.ScaleRowVector, coverage));
        }

        SimdKernelAssert.ExhaustiveBytes<byte>(1, Scale(CompositingRows.ScaleRowScalar, 77), Scale(CompositingRows.ScaleRow, 77));
        SimdKernelAssert.Equal<byte, byte>(Scale(CompositingRows.ScaleRowScalar, 130), Scale(CompositingRows.ScaleRow, 130), SimdKernelAssert.RandomBytes(100_003, seed: 4734));
    }

    [Fact]
    public void The_compositor_keeps_its_per_pixel_values()
    {
        // Known values, so that the scalar rows (the reference above) cannot drift together with the vector ones.
        Assert.Equal(0u, Compositor.RemoveBackdrop(0xFFFFFFFFu, 0xFFFFFFFFu, 0));
        Assert.Equal(0xFF003300u, Compositor.RemoveBackdrop(0xFF003300u, 0xFF000000u, 255));
        Assert.Equal(0x80404040u, Compositor.Mix(0x00000000u, 0xFF808080u, 128));
        Assert.Equal(0xFF102030u, Compositor.KnockOut(0u, 0xFF102030u, 0u, 255));
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

    /// <summary>A pixel whose color channels are different bijections of <paramref name="v"/>, so that a lane mix-up shows, and whose alpha is <paramref name="alpha"/>.</summary>
    private static uint Pixel(byte v, byte alpha) => v | ((uint)(v ^ 0x5A) << 8) | ((uint)(255 - v) << 16) | ((uint)alpha << 24);

    /// <summary>A pixel whose color channels are all <paramref name="v"/> and whose alpha is a bijection of it.</summary>
    private static uint Gray(byte v) => (uint)(v * 0x010101) | ((uint)(v ^ 0xA7) << 24);

    /// <summary>Adapts a mix to items of (from, to) bytes, or of eight bytes, two whole pixels.</summary>
    private static SpanKernel<byte, byte> Mix(MixKernel kernel, int amount) => (source, destination) =>
    {
        Span<uint> target = MemoryMarshal.Cast<byte, uint>(destination);
        uint[] to = new uint[target.Length];
        int width = source.Length / Math.Max(target.Length, 1);
        for (int i = 0; i < target.Length; i++)
        {
            if (width == 8)
            {
                target[i] = MemoryMarshal.Read<uint>(source.Slice(8 * i, 4));
                to[i] = MemoryMarshal.Read<uint>(source.Slice((8 * i) + 4, 4));
            }
            else
            {
                target[i] = Pixel(source[2 * i], (byte)(source[2 * i] ^ 0x3C));
                to[i] = Pixel(source[(2 * i) + 1], (byte)(source[(2 * i) + 1] ^ 0xC3));
            }
        }

        kernel(target, to, amount);
    };

    /// <summary>Adapts a knockout to items of (previous, composited, backdrop) bytes.</summary>
    private static SpanKernel<byte, byte> KnockOut(KnockOutKernel kernel, int shape, bool alphaIsByte) => (source, destination) =>
    {
        Span<uint> composited = MemoryMarshal.Cast<byte, uint>(destination);
        uint[] previous = new uint[composited.Length];
        uint[] backdrop = new uint[composited.Length];
        for (int i = 0; i < composited.Length; i++)
        {
            byte p = source[3 * i];
            byte c = source[(3 * i) + 1];
            byte b = source[(3 * i) + 2];
            previous[i] = alphaIsByte ? Pixel(p, p) : Gray(p);
            composited[i] = alphaIsByte ? Pixel(c, c) : Gray(c);
            backdrop[i] = alphaIsByte ? Pixel(b, b) : Gray(b);
        }

        kernel(previous, composited, backdrop, shape);
    };

    /// <summary>Adapts a knockout to items of twelve bytes, three whole pixels.</summary>
    private static SpanKernel<byte, byte> WholeKnockOut(KnockOutKernel kernel, int shape) => (source, destination) =>
    {
        Span<uint> composited = MemoryMarshal.Cast<byte, uint>(destination);
        uint[] previous = new uint[composited.Length];
        uint[] backdrop = new uint[composited.Length];
        for (int i = 0; i < composited.Length; i++)
        {
            previous[i] = MemoryMarshal.Read<uint>(source.Slice(12 * i, 4));
            composited[i] = MemoryMarshal.Read<uint>(source.Slice((12 * i) + 4, 4));
            backdrop[i] = MemoryMarshal.Read<uint>(source.Slice((12 * i) + 8, 4));
        }

        kernel(previous, composited, backdrop, shape);
    };

    /// <summary>Adapts a backdrop removal to items of (complete, backdrop, group alpha) bytes.</summary>
    private static SpanKernel<byte, byte> RemoveBackdrop(RemoveBackdropKernel kernel) => (source, destination) =>
    {
        Span<uint> result = MemoryMarshal.Cast<byte, uint>(destination);
        uint[] complete = new uint[result.Length];
        uint[] backdrop = new uint[result.Length];
        byte[] alpha = new byte[result.Length];
        for (int i = 0; i < result.Length; i++)
        {
            complete[i] = Pixel(source[3 * i], (byte)(source[3 * i] ^ 0x3C));
            backdrop[i] = Pixel(source[(3 * i) + 1], (byte)(source[(3 * i) + 1] ^ 0xC3));
            alpha[i] = source[(3 * i) + 2];
        }

        kernel(complete, backdrop, alpha, result);
    };

    /// <summary>Adapts a backdrop removal to items of nine bytes, two whole pixels and a group alpha.</summary>
    private static SpanKernel<byte, byte> RemoveBackdropWhole(RemoveBackdropKernel kernel) => (source, destination) =>
    {
        Span<uint> result = MemoryMarshal.Cast<byte, uint>(destination);
        uint[] complete = new uint[result.Length];
        uint[] backdrop = new uint[result.Length];
        byte[] alpha = new byte[result.Length];
        for (int i = 0; i < result.Length; i++)
        {
            complete[i] = MemoryMarshal.Read<uint>(source.Slice(9 * i, 4));
            backdrop[i] = MemoryMarshal.Read<uint>(source.Slice((9 * i) + 4, 4));
            alpha[i] = source[(9 * i) + 8];
        }

        kernel(complete, backdrop, alpha, result);
    };

    private static SpanKernel<byte, byte> Scale(ScaleKernel kernel, int coverage) => (source, destination) => kernel(source, destination, coverage);

    private static uint Backdrop(byte d) => d | ((uint)(d ^ 0x33) << 8) | ((uint)(255 - d) << 16) | ((uint)(d ^ 0x96) << 24);
}
