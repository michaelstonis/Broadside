using Broadside.Images;

namespace Broadside.Tests.Images;

/// <summary>
/// Issue #377: the managed decoder's YCbCr transform, public for platform decoders that hand back planes (ISO 32000-2 §7.4.8 Table
/// 13): libjpeg's 16-bit fixed point, clamped per channel, and short rows refused.
/// </summary>
public sealed class YCbCrConversionTests
{
    [Fact]
    public void The_transform_is_libjpegs_fixed_point_clamped_per_channel()
    {
        byte[] rgb = new byte[9];
        YCbCrConversion.ToRgb([77, 200, 0], [128, 85, 255], [128, 255, 0], rgb);

        Assert.Equal<byte>([77, 77, 77], rgb[..3]);
        Assert.Equal(255, rgb[3]);
        Assert.Equal(200 + (((-22554 * (85 - 128)) + 32768 + (-46802 * (255 - 128))) >> 16), rgb[4]);
        Assert.Equal(200 + (((116130 * (85 - 128)) + 32768) >> 16), rgb[5]);
        Assert.Equal(0, rgb[6]);
        Assert.Equal(Math.Clamp(((-22554 * 127) + 32768 + (-46802 * -128)) >> 16, 0, 255), rgb[7]);
        Assert.Equal(Math.Clamp(((116130 * 127) + 32768) >> 16, 0, 255), rgb[8]);
    }

    [Fact]
    public void A_component_row_shorter_than_the_output_is_refused() =>
        Assert.Throws<ArgumentException>(() => YCbCrConversion.ToRgb([1, 2], [1], [1, 2], new byte[6]));
}
