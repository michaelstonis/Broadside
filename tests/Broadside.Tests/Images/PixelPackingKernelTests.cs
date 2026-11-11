using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// The <c>Vector128</c> paths of the premultiplied BGRA and RGBA packing (issue #472) against the scalar ones they replace: every color
/// value with every opacity, every opaque RGB triple, every short row length and misalignment, the dispatchers included. In the
/// <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class PixelPackingKernelTests
{
    private delegate void Pack(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> alpha, Span<byte> pixels);

    [Fact]
    public void Premultiplied_BGRA_matches_the_scalar_packing_for_every_color_and_opacity()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(2, WithOpacity(PixelPacking.PackBgraScalar), WithOpacity(PixelPacking.PackBgraVector), outputsPerItem: 4);
        SimdKernelAssert.ExhaustiveBytes<byte>(2, WithOpacity(PixelPacking.PackBgraScalar), WithOpacity(PixelPacking.PackBgra), outputsPerItem: 4);
    }

    [Fact]
    public void Premultiplied_RGBA_matches_the_scalar_packing_for_every_color_and_opacity()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(2, WithOpacity(PixelPacking.PackRgbaScalar), WithOpacity(PixelPacking.PackRgbaVector), outputsPerItem: 4);
        SimdKernelAssert.ExhaustiveBytes<byte>(2, WithOpacity(PixelPacking.PackRgbaScalar), WithOpacity(PixelPacking.PackRgba), outputsPerItem: 4);
    }

    [Fact]
    public void Random_rows_with_opacity_match_the_scalar_packing()
    {
        byte[] rows = SimdKernelAssert.RandomBytes(4 * 10_001, seed: 472);
        SimdKernelAssert.Equal<byte, byte>(Interleaved(PixelPacking.PackBgraScalar), Interleaved(PixelPacking.PackBgraVector), rows, inputsPerItem: 4, outputsPerItem: 4);
        SimdKernelAssert.Equal<byte, byte>(Interleaved(PixelPacking.PackRgbaScalar), Interleaved(PixelPacking.PackRgbaVector), rows, inputsPerItem: 4, outputsPerItem: 4);
    }

    [Fact]
    public void Opaque_BGRA_matches_the_scalar_packing_for_every_color()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Opaque(PixelPacking.PackBgraScalar), Opaque(PixelPacking.PackBgraVector), outputsPerItem: 4);
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Opaque(PixelPacking.PackBgraScalar), Opaque(PixelPacking.PackBgra), outputsPerItem: 4);
    }

    [Fact]
    public void Opaque_RGBA_matches_the_scalar_packing_for_every_color()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Opaque(PixelPacking.PackRgbaScalar), Opaque(PixelPacking.PackRgbaVector), outputsPerItem: 4);
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Opaque(PixelPacking.PackRgbaScalar), Opaque(PixelPacking.PackRgba), outputsPerItem: 4);
    }

    [Fact]
    public void The_public_packing_premultiplies_and_orders_the_bytes()
    {
        byte[] rgb = [255, 128, 0, 10, 20, 30];
        byte[] opacity = [128, 255];
        byte[] bgra = new byte[8];
        byte[] rgba = new byte[8];

        ImageRows.PackBgra(rgb, opacity, 2, bgra);
        ImageRows.PackRgba(rgb, opacity, 2, rgba);

        Assert.Equal(new byte[] { 0, 64, 128, 128, 30, 20, 10, 255 }, bgra);
        Assert.Equal(new byte[] { 128, 64, 0, 128, 10, 20, 30, 255 }, rgba);
    }

    [Fact]
    public void The_public_packing_without_opacity_is_opaque()
    {
        byte[] pixels = new byte[4];
        ImageRows.PackBgra([1, 2, 3], default, 1, pixels);
        Assert.Equal(new byte[] { 3, 2, 1, 255 }, pixels);
    }

    [Fact]
    public void The_public_packing_rejects_spans_shorter_than_the_width()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.PackBgra(new byte[5], default, 2, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.PackBgra(new byte[6], new byte[1], 2, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.PackRgba(new byte[6], default, 2, new byte[7]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.PackRgba(new byte[6], default, -1, new byte[8]));
    }

    /// <summary>Items of (c, a): the pixel (c, c + 85, c + 170) at opacity a, so each channel meets every value at every opacity.</summary>
    private static SpanKernel<byte, byte> WithOpacity(Pack pack) => (source, destination) =>
    {
        int count = source.Length / 2;
        byte[] rgb = new byte[3 * count];
        byte[] alpha = new byte[count];
        for (int i = 0; i < count; i++)
        {
            byte c = source[2 * i];
            rgb[3 * i] = c;
            rgb[(3 * i) + 1] = (byte)(c + 85);
            rgb[(3 * i) + 2] = (byte)(c + 170);
            alpha[i] = source[(2 * i) + 1];
        }

        pack(rgb, alpha, destination);
    };

    /// <summary>Items of (r, g, b, a).</summary>
    private static SpanKernel<byte, byte> Interleaved(Pack pack) => (source, destination) =>
    {
        int count = source.Length / 4;
        byte[] rgb = new byte[3 * count];
        byte[] alpha = new byte[count];
        for (int i = 0; i < count; i++)
        {
            rgb[3 * i] = source[4 * i];
            rgb[(3 * i) + 1] = source[(4 * i) + 1];
            rgb[(3 * i) + 2] = source[(4 * i) + 2];
            alpha[i] = source[(4 * i) + 3];
        }

        pack(rgb, alpha, destination);
    };

    /// <summary>Items of (r, g, b) with no opacity row.</summary>
    private static SpanKernel<byte, byte> Opaque(Pack pack) => (source, destination) => pack(source, default, destination);
}
