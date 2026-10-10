using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using static Broadside.Tests.Filters.Dct.DctVectors;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// Four-component JPEGs and the colour transform choice of ISO 32000-2 §7.4.8 Table 13: the Adobe APP14 segment wins, DecodeParms
/// <c>ColorTransform</c> applies only without it; YCCK becomes CMYK (Adobe TN 5116 §13.2); the codec never inverts, an inverted
/// image says so with its <c>Decode</c> array (§8.9.5.2). References are TurboJPEG's raw CMYK decodes (README.md next to this file).
/// </summary>
public class DctColorTransformTests
{
    [Theory]
    [InlineData("cmyk", "cmyk", 0, false)]
    [InlineData("ycck", "ycck", 2, true)]
    [InlineData("ycck-progressive", "ycck", 2, true)]
    [InlineData("ycck-arithmetic", "ycck", 2, true)]
    public void CMYK_and_YCCK_decode_to_CMYK_samples_within_one_of_the_reference(string vector, string reference, int transform, bool applied)
    {
        var context = new ImageFilterContext(new FilterContext());

        using DecodedImage decoded = new DctDecodeFilter().DecodeImage(Jpeg(vector), context)!;

        AssertWithinOne(Golden(reference), decoded.Samples);
        Assert.Equal((4, ImageColorModel.Cmyk, (int?)transform, applied, false), (decoded.Components, decoded.ColorModel, decoded.AdobeTransform, decoded.ColorTransformApplied, decoded.SamplesInverted));
        Assert.Empty(context.Filter.Diagnostics);
    }

    [Fact]
    public void Twelve_bit_YCCK_decodes_to_eight_bit_CMYK()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg("ycck-precision12"));

        AssertWithinOne(Golden("ycck-precision12"), samples);
        Assert.Equal(["DctPrecisionReduced"], Codes(context));
    }

    [Theory]
    [InlineData(null, "inverted CMYK expressed by the Decode array")]
    [InlineData("[0 1 0 1 0 1 0 1]", "the default Decode array")]
    public void The_samples_are_the_same_with_or_without_an_inverting_Decode_array_which_the_image_layer_applies(string? decode, string convention)
    {
        string entries = "/Width 75 /Height 43 /ColorSpace /DeviceCMYK /BitsPerComponent 8 /Filter /DCTDecode";
        using PdfDocument inverted = PdfDocument.Open(OneImage(entries + " /Decode [1 0 1 0 1 0 1 0]", Bytes(Jpeg("ycck"))));
        using PdfDocument plain = PdfDocument.Open(OneImage(entries + (decode is null ? string.Empty : " /Decode " + decode), Bytes(Jpeg("ycck"))));
        PdfImage invertedImage = inverted.Pages[0].GetImage("Im0")!;
        PdfImage plainImage = plain.Pages[0].GetImage("Im0")!;

        using DecodedImage a = invertedImage.Decode()!;
        using DecodedImage b = plainImage.Decode()!;

        AssertWithinOne(Golden("ycck"), a.Samples);
        Assert.True(a.Samples.SequenceEqual(b.Samples), convention);
        ImageDecodeMap invertedMap = invertedImage.CreateDecodeMap(a);
        ImageDecodeMap plainMap = plainImage.CreateDecodeMap(b);
        Assert.Equal((true, false), (invertedMap.IsInverted, plainMap.IsInverted));
        Assert.Equal((1f, 0f), (invertedMap.Map(0, 0), plainMap.Map(0, 0)));
        Assert.Empty(inverted.Diagnostics);
    }

    [Fact]
    public void APP14_transform_0_on_four_components_ignores_ColorTransform_1()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg("cmyk"), "<< /ColorTransform 1 >>");

        AssertWithinOne(Golden("cmyk"), samples);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void APP14_transform_2_on_four_components_ignores_ColorTransform_0()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(Jpeg("ycck"), "<< /ColorTransform 0 >>");

        AssertWithinOne(Golden("ycck"), samples);
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void Without_APP14_ColorTransform_1_converts_four_components_from_YCCK()
    {
        (byte[] samples, FilterContext context) = DecodeBytes(WithoutApp14(Jpeg("ycck")), "<< /ColorTransform 1 >>");

        AssertWithinOne(Golden("ycck"), samples);
        Assert.Empty(context.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<< /ColorTransform 0 >>")]
    public void Without_APP14_four_components_are_not_transformed_by_default_or_with_ColorTransform_0(string? parameters)
    {
        (byte[] raw, FilterContext context) = DecodeBytes(WithoutApp14(Jpeg("ycck")), parameters);

        // The untransformed samples are YCCK: Adobe TN 5116 §13.2 (C = 255 - R and so on, K unchanged) gives the reference.
        AssertWithinOne(Golden("ycck"), YcckToCmyk(raw));
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public void APP14_transform_1_on_four_components_is_read_as_YCCK_and_reported()
    {
        byte[] jpeg = Jpeg("ycck");
        jpeg[DctRepairTests.FindMarker(jpeg, 0xEE) + 15] = 1;

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        AssertWithinOne(Golden("ycck"), samples);
        Assert.Equal(["DctAdobeTransformInvalid"], Codes(context));
    }

    [Fact]
    public void APP14_transform_2_on_three_components_is_read_as_YCbCr_and_reported()
    {
        byte[] original = Jpeg("testorig");
        byte[] app14 = [0xFF, 0xEE, 0x00, 0x0E, .. "Adobe"u8, 0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02];
        byte[] jpeg = [.. original[..2], .. app14, .. original[2..]];

        (byte[] samples, FilterContext context) = DecodeBytes(jpeg);

        AssertWithinOne(Golden("testorig"), samples);
        Assert.Equal(["DctAdobeTransformInvalid"], Codes(context));
    }

    private static byte[] YcckToCmyk(byte[] ycck)
    {
        byte[] cmyk = new byte[ycck.Length];
        for (int i = 0; i < ycck.Length; i += 4)
        {
            double y = ycck[i];
            double cb = ycck[i + 1] - 128.0;
            double cr = ycck[i + 2] - 128.0;
            cmyk[i] = Clamp(255 - (y + (1.402 * cr)));
            cmyk[i + 1] = Clamp(255 - (y - (0.3441363 * cb) - (0.71413636 * cr)));
            cmyk[i + 2] = Clamp(255 - (y + (1.772 * cb)));
            cmyk[i + 3] = ycck[i + 3];
        }

        return cmyk;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static byte[] WithoutApp14(byte[] jpeg)
    {
        int app14 = DctRepairTests.FindMarker(jpeg, 0xEE);
        int length = (jpeg[app14 + 2] << 8) | jpeg[app14 + 3];
        return [.. jpeg[..app14], .. jpeg[(app14 + 2 + length)..]];
    }
}
