using System.Runtime.InteropServices;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The <c>Vector128</c> box filter (issue #475) against the scalar one it replaces: every value of every channel, in every lane, under
/// a set of weight patterns (fractions, ones, zeros, negatives, products that underflow to 0); and random footprints of 0 to
/// <see cref="BoxFilter.MaxTaps"/> columns and rows over a random image, the dispatcher included. In the
/// <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class BoxFilterKernelTests
{
    private const int ImageSize = 64;

    private delegate bool AverageKernel(ReadOnlySpan<uint> pixels, ReadOnlySpan<int> rows, ReadOnlySpan<double> rowWeights, ReadOnlySpan<int> columns, ReadOnlySpan<double> columnWeights, out uint result);

    private static readonly uint[] Image = MemoryMarshal.Cast<byte, uint>(SimdKernelAssert.RandomBytes(4 * ImageSize * ImageSize, seed: 475)).ToArray();

    public static TheoryData<int> WeightPatterns => [0, 1, 2, 3, 4, 5, 6, 7];

    [Theory]
    [MemberData(nameof(WeightPatterns))]
    public void The_vector_average_matches_the_scalar_average_on_every_channel_value(int pattern)
    {
        // Items are byte pairs (x, y), all 65,536: a 2 × 2 footprint whose four samples hold x and y in every channel, each channel
        // a different bijection so that a lane mix-up shows.
        SimdKernelAssert.ExhaustiveBytes<byte>(2, Pairs(BoxFilter.AverageScalar, pattern), Pairs(BoxFilter.AverageVector, pattern), outputsPerItem: 5);
        SimdKernelAssert.ExhaustiveBytes<byte>(2, Pairs(BoxFilter.AverageScalar, pattern), Pairs(BoxFilter.Average, pattern), outputsPerItem: 5);
    }

    [Fact]
    public void The_vector_average_matches_the_scalar_average_on_random_footprints()
    {
        byte[] items = SimdKernelAssert.RandomBytes(8 * 200_000, seed: 4750);
        SimdKernelAssert.Equal<byte, byte>(Footprints(BoxFilter.AverageScalar), Footprints(BoxFilter.AverageVector), items, inputsPerItem: 8, outputsPerItem: 5);
        SimdKernelAssert.Equal<byte, byte>(Footprints(BoxFilter.AverageScalar), Footprints(BoxFilter.Average), items, inputsPerItem: 8, outputsPerItem: 5);
    }

    [Fact]
    public void A_footprint_whose_weights_sum_to_zero_has_no_average()
    {
        uint[] pixels = [0xFF102030u, 0x80405060u];
        int[] rows = [0];
        double[] rowWeights = [1];
        int[] columns = [0, 1];
        double[] columnWeights = [0, -0.5];

        Assert.False(BoxFilter.AverageScalar(pixels, rows, rowWeights, columns, columnWeights, out uint scalar));
        Assert.False(BoxFilter.AverageVector(pixels, rows, rowWeights, columns, columnWeights, out uint vector));
        Assert.Equal(0u, scalar);
        Assert.Equal(0u, vector);
    }

    [Fact]
    public void An_even_average_of_two_samples_rounds_each_channel_half_up()
    {
        uint[] pixels = [0x01030507u, 0x02040608u];
        int[] rows = [0];
        double[] rowWeights = [0.5];
        int[] columns = [0, 1];
        double[] columnWeights = [1, 1];

        Assert.True(BoxFilter.AverageVector(pixels, rows, rowWeights, columns, columnWeights, out uint vector));
        Assert.True(BoxFilter.AverageScalar(pixels, rows, rowWeights, columns, columnWeights, out uint scalar));
        Assert.Equal(0x02040608u, vector);
        Assert.Equal(scalar, vector);
    }

    /// <summary>Column and row weights for the channel test: two each, by pattern.</summary>
    private static (double, double, double, double) Weights(int pattern) => pattern switch
    {
        0 => (0.3, 0.7, 0.45, 0.55),
        1 => (1, 1, 1, 1),
        2 => (0.25, 1, 1, 0.125),
        3 => (0, 0.6, 0.9, 0),
        4 => (-0.2, 0.8, 0.5, -1),
        5 => (1e-200, 0.1, 1e-200, 1),
        6 => (1.0 / 3, 2.0 / 3, 0.1, 0.9),
        _ => (0.999999, 0.000001, 0.333333, 0.666667),
    };

    /// <summary>
    /// Adapts the average to items of (x, y) bytes: a 2 × 2 image per item, the samples' channels different bijections of x and
    /// y; the output is the average's four bytes and whether there was one.
    /// </summary>
    private static SpanKernel<byte, byte> Pairs(AverageKernel kernel, int pattern) => (source, destination) =>
    {
        (double c0, double c1, double r0, double r1) = Weights(pattern);
        Span<uint> pixels = stackalloc uint[4];
        ReadOnlySpan<int> rows = [0, 2];
        ReadOnlySpan<int> columns = [0, 1];
        ReadOnlySpan<double> rowWeights = [r0, r1];
        ReadOnlySpan<double> columnWeights = [c0, c1];
        for (int i = 0; i < source.Length / 2; i++)
        {
            uint x = source[2 * i];
            uint y = source[(2 * i) + 1];
            pixels[0] = x | (y << 8) | ((255 - x) << 16) | ((x ^ y) << 24);
            pixels[1] = y | ((255 - x) << 8) | ((x ^ 0x5A) << 16) | (x << 24);
            pixels[2] = (255 - y) | ((x ^ y) << 8) | (y << 16) | ((y ^ 0xC3) << 24);
            pixels[3] = (x ^ y) | (x << 8) | ((255 - y) << 16) | (y << 24);
            Write(destination.Slice(5 * i, 5), kernel(pixels, rows, rowWeights, columns, columnWeights, out uint result), result);
        }
    };

    /// <summary>
    /// Adapts the average to items of eight random bytes, each a footprint on a random 64 × 64 image: column and row counts of 0 to
    /// <see cref="BoxFilter.MaxTaps"/>, their first column and row, and their weights by one of several patterns.
    /// </summary>
    private static SpanKernel<byte, byte> Footprints(AverageKernel kernel) => (source, destination) =>
    {
        Span<int> columns = stackalloc int[BoxFilter.MaxTaps];
        Span<int> rows = stackalloc int[BoxFilter.MaxTaps];
        Span<double> columnWeights = stackalloc double[BoxFilter.MaxTaps];
        Span<double> rowWeights = stackalloc double[BoxFilter.MaxTaps];
        for (int i = 0; i < source.Length / 8; i++)
        {
            ReadOnlySpan<byte> item = source.Slice(8 * i, 8);
            int columnCount = item[0] % (BoxFilter.MaxTaps + 1);
            int rowCount = item[1] % (BoxFilter.MaxTaps + 1);
            int left = item[2] % (ImageSize - BoxFilter.MaxTaps);
            int top = item[3] % (ImageSize - BoxFilter.MaxTaps);
            for (int k = 0; k < columnCount; k++)
            {
                columns[k] = left + k;
                columnWeights[k] = Weight(item[6], item[4], k);
            }

            for (int k = 0; k < rowCount; k++)
            {
                rows[k] = (top + k) * ImageSize;
                rowWeights[k] = Weight(item[7], item[5], k);
            }

            bool any = kernel(Image, rows[..rowCount], rowWeights, columns[..columnCount], columnWeights, out uint result);
            Write(destination.Slice(5 * i, 5), any, result);
        }
    };

    /// <summary>The weight of tap <paramref name="k"/>: ones, fractions, fractions with zeros and negatives, or tiny values whose products underflow.</summary>
    private static double Weight(byte mode, byte seed, int k)
    {
        int value = (seed + (k * 37)) & 0xFF;
        return (mode & 3) switch
        {
            0 => 1,
            1 => (value + 1) / 256.0,
            2 => (value - 40) / 97.0,
            _ => value < 64 ? 1e-170 * (value + 1) : value / 255.0,
        };
    }

    private static void Write(Span<byte> destination, bool any, uint result)
    {
        MemoryMarshal.Write(destination, result);
        destination[4] = any ? (byte)1 : (byte)0;
    }
}
