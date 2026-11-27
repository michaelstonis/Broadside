using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The <c>Vector128</c> per-pixel difference of <see cref="VisualComparison"/> (issue #644) against the scalar one: every pair of
/// channel values in each channel, and random rows at every short length and misalignment, the dispatcher included. In the
/// <see cref="SimdKernelAssert.Category"/> category, which CI reruns with hardware intrinsics off.
/// </summary>
[Trait("Category", SimdKernelAssert.Category)]
public sealed class PixelDifferenceKernelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_vector_difference_matches_the_scalar_difference_on_every_pair_of_values_in_each_channel(int channel)
    {
        // Items are (first, second) byte pairs, placed in one channel of two pixels whose other channels are equal.
        SimdKernelAssert.ExhaustiveBytes<byte>(2, InChannel(PixelDifference.MaxChannelDifferenceScalar, channel), InChannel(PixelDifference.MaxChannelDifferenceVector, channel));
        SimdKernelAssert.ExhaustiveBytes<byte>(2, InChannel(PixelDifference.MaxChannelDifferenceScalar, channel), InChannel(PixelDifference.MaxChannelDifference, channel));
    }

    [Fact]
    public void The_vector_difference_matches_the_scalar_difference_on_random_pixels()
    {
        // Items are eight bytes: a pixel of the first row, then the pixel of the second.
        byte[] source = SimdKernelAssert.RandomBytes(8 * 4096, seed: 644);
        SimdKernelAssert.Equal<byte, byte>(Interleaved(PixelDifference.MaxChannelDifferenceScalar), Interleaved(PixelDifference.MaxChannelDifferenceVector), source, inputsPerItem: 8);
        SimdKernelAssert.Equal<byte, byte>(Interleaved(PixelDifference.MaxChannelDifferenceScalar), Interleaved(PixelDifference.MaxChannelDifference), source, inputsPerItem: 8);
    }

    private delegate void DifferenceKernel(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, Span<byte> differences);

    private static SpanKernel<byte, byte> InChannel(DifferenceKernel kernel, int channel) => (source, destination) =>
    {
        int count = source.Length / 2;
        var first = new byte[4 * count];
        var second = new byte[4 * count];
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                first[(4 * i) + c] = second[(4 * i) + c] = (byte)(37 * c);
            }

            first[(4 * i) + channel] = source[2 * i];
            second[(4 * i) + channel] = source[(2 * i) + 1];
        }

        kernel(first, second, destination);
    };

    private static SpanKernel<byte, byte> Interleaved(DifferenceKernel kernel) => (source, destination) =>
    {
        int count = source.Length / 8;
        var first = new byte[4 * count];
        var second = new byte[4 * count];
        for (int i = 0; i < count; i++)
        {
            source.Slice(8 * i, 4).CopyTo(first.AsSpan(4 * i));
            source.Slice((8 * i) + 4, 4).CopyTo(second.AsSpan(4 * i));
        }

        kernel(first, second, destination);
    };
}
