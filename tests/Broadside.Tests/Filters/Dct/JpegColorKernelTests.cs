using Broadside.Filters.Dct;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The <c>Vector128</c> paths of the DCT colour transforms (issue #471) against the scalar ones they replace: every (Y, Cb, Cr) triple,
/// every short row length and misalignment, the dispatcher included. In the <see cref="SimdKernelAssert.Category"/> category, which
/// CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class JpegColorKernelTests
{
    private delegate void Ycc(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, Span<byte> rgb);

    private delegate void Ycck(ReadOnlySpan<byte> y, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr, ReadOnlySpan<byte> k, Span<byte> cmyk);

    [Fact]
    public void YCbCr_to_RGB_matches_the_scalar_transform_on_every_triple()
    {
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Planar(JpegColor.YccToRgbScalar), Planar(JpegColor.YccToRgbVector), outputsPerItem: 3);
        SimdKernelAssert.ExhaustiveBytes<byte>(3, Planar(JpegColor.YccToRgbScalar), Planar(JpegColor.YccToRgb), outputsPerItem: 3);
    }

    [Fact]
    public void YCCK_to_CMYK_matches_the_scalar_transform_on_every_triple()
    {
        // K passes through: every (Y, Cb, Cr) triple with a K that varies with it, then random rows of all four.
        SimdKernelAssert.ExhaustiveBytes<byte>(3, PlanarWithBlack(JpegColor.YcckToCmykScalar), PlanarWithBlack(JpegColor.YcckToCmykVector), outputsPerItem: 4);
        SimdKernelAssert.ExhaustiveBytes<byte>(3, PlanarWithBlack(JpegColor.YcckToCmykScalar), PlanarWithBlack(JpegColor.YcckToCmyk), outputsPerItem: 4);
        SimdKernelAssert.Equal<byte, byte>(Planar(JpegColor.YcckToCmykScalar), Planar(JpegColor.YcckToCmykVector), SimdKernelAssert.RandomBytes(4 * 10_001, seed: 471), inputsPerItem: 4, outputsPerItem: 4);
    }

    [Fact]
    public void A_row_wider_than_its_output_converts_only_the_output_width()
    {
        // The decoder hands over component lines longer than the row (padded to whole blocks).
        byte[] y = SimdKernelAssert.RandomBytes(100, seed: 1);
        byte[] cb = SimdKernelAssert.RandomBytes(100, seed: 2);
        byte[] cr = SimdKernelAssert.RandomBytes(100, seed: 3);
        byte[] expected = new byte[3 * 37];
        byte[] actual = new byte[3 * 37];

        JpegColor.YccToRgbScalar(y, cb, cr, expected);
        JpegColor.YccToRgbVector(y, cb, cr, actual);

        Assert.Equal(expected, actual);
    }

    /// <summary>Adapts a planar transform to interleaved items (Y, Cb, Cr[, K]), as the shared checks enumerate them.</summary>
    private static SpanKernel<byte, byte> Planar(Ycc kernel) => (source, destination) =>
    {
        byte[][] planes = Split(source, 3);
        kernel(planes[0], planes[1], planes[2], destination);
    };

    private static SpanKernel<byte, byte> Planar(Ycck kernel) => (source, destination) =>
    {
        byte[][] planes = Split(source, 4);
        kernel(planes[0], planes[1], planes[2], planes[3], destination);
    };

    /// <summary>Items of (Y, Cb, Cr) with K = Y + 3 Cb + 7 Cr (mod 256), so K takes every value across the triples.</summary>
    private static SpanKernel<byte, byte> PlanarWithBlack(Ycck kernel) => (source, destination) =>
    {
        byte[][] planes = Split(source, 3);
        byte[] k = new byte[planes[0].Length];
        for (int i = 0; i < k.Length; i++)
        {
            k[i] = (byte)(planes[0][i] + (3 * planes[1][i]) + (7 * planes[2][i]));
        }

        kernel(planes[0], planes[1], planes[2], k, destination);
    };

    private static byte[][] Split(ReadOnlySpan<byte> source, int components)
    {
        int count = source.Length / components;
        byte[][] planes = new byte[components][];
        for (int c = 0; c < components; c++)
        {
            planes[c] = new byte[count];
            for (int i = 0; i < count; i++)
            {
                planes[c][i] = source[(i * components) + c];
            }
        }

        return planes;
    }
}
