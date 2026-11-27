using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>The box resampler of the save-time image pass (issue #625): exact area averages at any ratio. ISO 32000-2 §8.9.3, §8.9.5.3.</summary>
public sealed class ImageResamplerTests
{
    [Fact]
    public void An_integer_ratio_averages_each_block()
    {
        byte[] source = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150];
        byte[] target = new byte[4];

        ImageResampler.Resample(source, 4, 4, 1, 8, 8, 2, 2, 8, target);

        // Block (0,0) holds 0, 10, 40, 50: mean 25.
        Assert.Equal([25, 45, 105, 125], target);
    }

    [Fact]
    public void A_fractional_ratio_weights_samples_by_the_area_they_cover()
    {
        // Three samples into two: the middle sample is split half and half.
        byte[] source = [0, 90, 180];
        byte[] target = new byte[2];

        ImageResampler.Resample(source, 3, 1, 1, 8, 8, 2, 1, 8, target);

        // Target 0 covers 1 of sample 0 and 1/2 of sample 1: (0 + 45) / 1.5 = 30; target 1: (45 + 180) / 1.5 = 150.
        Assert.Equal([30, 150], target);
    }

    [Fact]
    public void Components_are_averaged_separately()
    {
        byte[] source = [255, 0, 0, 0, 0, 255];
        byte[] target = new byte[3];

        ImageResampler.Resample(source, 2, 1, 3, 8, 8, 1, 1, 8, target);

        Assert.Equal([128, 0, 128], target);
    }

    [Fact]
    public void Sixteen_bit_samples_stay_sixteen_bit()
    {
        byte[] source = [0x00, 0x00, 0xFF, 0xFF];
        byte[] target = new byte[2];

        ImageResampler.Resample(source, 2, 1, 1, 16, 16, 1, 1, 16, target);

        Assert.Equal([0x80, 0x00], target);
    }

    [Fact]
    public void Four_bit_samples_become_eight_bit_coverage()
    {
        byte[] source = [0xF0];
        byte[] target = new byte[1];

        ImageResampler.Resample(source, 2, 1, 1, 4, 4, 1, 1, 8, target);

        Assert.Equal([128], target);
    }

    [Fact]
    public void Bitonal_output_is_thresholded_at_half_without_dithering()
    {
        // 8 x 2 bits: the left half set in both rows, then a column pattern 1 0 1 1 per 2 x 2 block.
        byte[] source = [0b1111_1011, 0b1111_0001];
        byte[] target = new byte[1];

        ImageResampler.Resample(source, 8, 2, 1, 1, 1, 4, 1, 1, target);

        // Blocks: 4/4, 4/4, 1/4 (below half), 3/4.
        Assert.Equal([0b1101_0000], target);
    }

    [Fact]
    public void A_one_sample_target_is_the_mean_of_the_whole_image()
    {
        byte[] source = [.. Enumerable.Range(0, 7 * 5).Select(i => (byte)(i * 7))];
        byte[] target = new byte[1];

        ImageResampler.Resample(source, 7, 5, 1, 8, 8, 1, 1, 8, target);

        Assert.Equal((byte)Math.Round(source.Average(b => b), MidpointRounding.AwayFromZero), target[0]);
    }

    [Fact]
    public void Enlarging_or_a_mismatched_buffer_is_refused()
    {
        byte[] source = new byte[4];
        Assert.Throws<ArgumentException>(() => ImageResampler.Resample(source, 2, 2, 1, 8, 8, 3, 2, 8, new byte[6]));
        Assert.Throws<ArgumentException>(() => ImageResampler.Resample(source, 2, 2, 1, 8, 8, 1, 1, 8, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => ImageResampler.Resample(new byte[12], 2, 2, 3, 8, 8, 1, 1, 1, new byte[1]));
    }
}

/// <summary>The resampler is a hot path and allocates nothing per row (CLAUDE.md; ISO 32000-2 §8.9.5.3); <c>ImageResampleBenchmarks</c> measures it.</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageResamplerAllocationTests
{
    [Fact]
    public void Resampling_allocates_nothing()
    {
        byte[] source = [.. Enumerable.Range(0, 1200 * 300 * 3).Select(i => (byte)i)];
        byte[] target = new byte[150 * 37 * 3];
        byte[] bits = new byte[150 * 300];
        byte[] bitonal = new byte[19 * 75];

        void Pass()
        {
            ImageResampler.Resample(source, 1200, 300, 3, 8, 8, 150, 37, 8, target);
            ImageResampler.Resample(bits, 1200, 300, 1, 1, 1, 150, 75, 1, bitonal);
        }

        Action pass = Pass;
        Assert.Equal(0, Allocations.Measure(pass));
    }
}
