using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The <c>DCTDecode</c> filter through the filter contract (ISO 32000-2 §7.4.8; ITU-T T.81 baseline and extended sequential):
/// samples within ±1 of libjpeg-turbo (<c>djpeg -dct int -nosmooth</c>) on the vectors next to this file.
/// </summary>
public class DctDecodeFilterTests
{
    [Fact]
    public void A_baseline_YCbCr_JPEG_decodes_to_interleaved_RGB_within_one_of_the_reference()
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(Jpeg("testorig"));

        AssertWithinOne(Golden("testorig"), samples);
        Assert.Empty(context.Diagnostics);
    }

    [Theory]
    [InlineData("testimgint", "second baseline encoding of the same picture")]
    [InlineData("sampling-444", "4:4:4, odd size")]
    [InlineData("sampling-422", "4:2:2, restart interval of one MCU row")]
    [InlineData("sampling-440", "4:4:0")]
    [InlineData("sampling-420", "4:2:0, restart interval of one MCU row")]
    [InlineData("sampling-411", "4:1:1, MCUs 32 samples wide")]
    [InlineData("gray-2x2", "one component with sampling factors 2x2: a non-interleaved scan over its own block extent")]
    [InlineData("rgb", "RGB components (APP14 transform 0, identifiers R G B): no colour transform")]
    public void Every_common_layout_decodes_within_one_of_the_reference(string vector, string layout)
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(Jpeg(vector));

        AssertWithinOne(Golden(vector), samples);
        Assert.True(context.Diagnostics.Count == 0, layout);
    }

    [Theory]
    [InlineData("multiscan", "sampling-420", "one sequential scan per component (buffered coefficients)")]
    [InlineData("sof1", "sampling-420", "extended sequential (SOF1) frame")]
    [InlineData("restart-blocks", "testorig", "restart interval of two blocks' worth of MCUs")]
    public void Scan_structure_does_not_change_the_samples(string vector, string reference, string structure)
    {
        (byte[] samples, Broadside.Filters.FilterContext context) = DecodeBytes(Jpeg(vector));

        AssertWithinOne(Golden(reference), samples);
        Assert.True(context.Diagnostics.Count == 0, structure);
    }
}
