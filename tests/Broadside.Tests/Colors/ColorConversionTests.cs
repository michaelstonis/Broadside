using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// Conversion to device colours through <see cref="PdfColorConverter"/> and the managed colour management: the device formulas of
/// ISO 32000-2 §10.4.2 (with the clause's own example), the CIE-based spaces of §8.6.5 against sRGB reference values, and the
/// special spaces of §8.6.6.
/// </summary>
public class ColorConversionTests
{
    /// <summary>The sRGB (IEC 61966-2-1) primaries as a CalRGB Matrix with the D65 white: linear CalRGB values are linear sRGB.</summary>
    private const string SrgbCalRgb = "[/CalRGB << /WhitePoint [0.95047 1 1.08883] /Matrix [0.4124564 0.2126729 0.0193339 0.3575761 0.7151522 0.1191920 0.1804375 0.0721750 0.9503041] >>]";

    /// <summary>sRGB encoding of linear 0.5 (IEC 61966-2-1: 1.055 × 0.5^(1/2.4) − 0.055).</summary>
    private const float HalfLinear = 0.7353570f;

    [Fact]
    public void Gray_and_RGB_convert_by_the_formulas_of_10_4_2_2()
    {
        using PdfDocument document = PdfDocument.Create();
        float[] gray = new float[1];

        Near([0.4f, 0.4f, 0.4f], document.Rgb(PdfDeviceGrayColorSpace.Instance, 0.4f));
        document.GetColorConverter(PdfDeviceRgbColorSpace.Instance, new ColorConversion { Target = DeviceColorModel.Gray }).Convert([0.2f, 0.7f, 0.4f], gray);
        Near([0.517f], gray);
    }

    [Fact]
    public void RGB_to_CMYK_follows_the_example_of_10_4_2_3_before_black_generation_and_undercolour_removal()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction zero = document.GetFunction(Cos("<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [0] /N 1 >>"))!;
        float[] cmyk = new float[4];

        var none = new ColorConversion { Target = DeviceColorModel.Cmyk, BlackGeneration = zero, UndercolorRemoval = zero };
        document.GetColorConverter(PdfDeviceRgbColorSpace.Instance, none).Convert([0.2f, 0.7f, 0.4f], cmyk);
        Near([0.8f, 0.3f, 0.6f, 0f], cmyk);

        // Without BG and UCR in the graphics state, Broadside removes all of the undercolour and replaces it by black.
        document.GetColorConverter(PdfDeviceRgbColorSpace.Instance, new ColorConversion { Target = DeviceColorModel.Cmyk }).Convert([0.2f, 0.7f, 0.4f], cmyk);
        Near([0.5f, 0f, 0.3f, 0.3f], cmyk);
    }

    [Fact]
    public void Gray_and_CMYK_convert_by_the_formulas_of_10_4_2_4()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty), new PdfOptions().UseColorManagement(ManagedColorManagement.Classic));
        float[] cmyk = new float[4];
        float[] gray = new float[1];

        document.GetColorConverter(PdfDeviceGrayColorSpace.Instance, new ColorConversion { Target = DeviceColorModel.Cmyk }).Convert([0.25f], cmyk);
        Near([0f, 0f, 0f, 0.75f], cmyk);
        document.GetColorConverter(PdfDeviceCmykColorSpace.Instance, new ColorConversion { Target = DeviceColorModel.Gray }).Convert([0.2f, 0.3f, 0.4f, 0.5f], gray);
        Near([0.219f], gray);
    }

    [Fact]
    public void Classic_CMYK_to_RGB_is_the_formula_of_10_4_2_5()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty), new PdfOptions().UseColorManagement(ManagedColorManagement.Classic));

        Near([0f, 1f, 1f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 1, 0, 0, 0));
        Near([0.3f, 0.2f, 0.1f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 0.2f, 0.3f, 0.4f, 0.5f));
        Near([0f, 0f, 0f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 0, 0, 0, 1));
    }

    [Fact]
    public void The_default_CMYK_conversion_approximates_SWOP_printing()
    {
        using PdfDocument document = PdfDocument.Create();

        // Paper stays exactly white; process cyan is the (0, 0.68, 0.94) viewers show, not the formula's (0, 1, 1); solid black is
        // the SWOP black measured in CGATS TR 001 (L* 24, about 54/255), as no black point compensation is applied.
        Near([1f, 1f, 1f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 0, 0, 0, 0), 0);
        Near([0f, 0.68f, 0.94f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 1, 0, 0, 0), 0.03);
        Near([0.21f, 0.21f, 0.21f], document.Rgb(PdfDeviceCmykColorSpace.Instance, 0, 0, 0, 1), 0.03);
    }

    [Fact]
    public void CalRGB_with_the_sRGB_primaries_and_gamma_1_reproduces_sRGB()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace space = document.GetColorSpace(Cos(SrgbCalRgb));

        Near([1f, 1f, 1f], document.Rgb(space, 1, 1, 1), 2e-3);
        Near([HalfLinear, 0f, 0f], document.Rgb(space, 0.5f, 0, 0), 2e-3);
        Near([HalfLinear, HalfLinear, HalfLinear], document.Rgb(space, 0.5f, 0.5f, 0.5f), 2e-3);
    }

    [Fact]
    public void CalGray_maps_Y_to_sRGB_gray()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace space = document.GetColorSpace(Cos("[/CalGray << /WhitePoint [0.95047 1 1.08883] /Gamma 2 >>]"));

        Near([HalfLinear, HalfLinear, HalfLinear], document.Rgb(space, MathF.Sqrt(0.5f)), 2e-3);
        Near([0f, 0f, 0f], document.Rgb(space, 0));
    }

    [Fact]
    public void Lab_with_a_D50_white_is_adapted_to_the_sRGB_white()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace space = document.GetColorSpace(Cos("[/Lab << /WhitePoint [0.9642 1 0.8249] >>]"));

        // L* 50 is Y 0.1842, sRGB 119/255 (CIE 15 and IEC 61966-2-1).
        Near([1f, 1f, 1f], document.Rgb(space, 100, 0, 0), 2e-3);
        Near([0.4663f, 0.4663f, 0.4663f], document.Rgb(space, 50, 0, 0), 2e-3);
        Near([0f, 0f, 0f], document.Rgb(space, 0, 0, 0), 2e-3);

        // Absolute colorimetric keeps the D50 white's colour: warmer than the sRGB white.
        float[] absolute = new float[3];
        document.GetColorConverter(space, new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric }).Convert([100f, 0, 0], absolute);
        Assert.True(absolute[2] < absolute[0] - 0.05, $"Expected a warm white, got [{string.Join(", ", absolute)}].");
    }

    [Fact]
    public void Black_point_compensation_maps_the_source_black_point_to_black_unless_it_is_off()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace space = document.GetColorSpace(Cos("[/CalGray << /WhitePoint [0.95047 1 1.08883] /BlackPoint [0.047523 0.05 0.054441] >>]"));
        float[] rgb = new float[3];

        document.GetColorConverter(space, new ColorConversion { BlackPointCompensation = BlackPointCompensation.On }).Convert([0.05f], rgb);
        Near([0f, 0f, 0f], rgb, 1e-3);
        document.GetColorConverter(space, new ColorConversion { BlackPointCompensation = BlackPointCompensation.Off }).Convert([0.05f], rgb);
        Near([0.2474f, 0.2474f, 0.2474f], rgb, 2e-3);
        document.GetColorConverter(space, new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric }).Convert([0.05f], rgb);
        Assert.True(rgb[1] > 0.2f);
    }

    [Fact]
    public void An_ICC_Lab_profile_converts_as_Lab_and_another_profile_through_its_alternate()
    {
        using PdfDocument document = PdfDocument.Open(Page(
            "<< >>",
            string.Empty,
            IccProfiles.Stream(IccProfiles.Header("spac", "Lab "), "/N 3 /Alternate /DeviceRGB"),
            IccProfiles.Stream(IccProfiles.Header("mntr", "RGB "), "/N 3 /Alternate " + SrgbCalRgb + " /Range [0 1 0 1 0 0.5]")));
        PdfColorSpace lab = document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));
        PdfColorSpace rgb = document.GetColorSpace(Cos("[/ICCBased 6 0 R]"));

        Near([0.4663f, 0.4663f, 0.4663f], document.Rgb(lab, 50, 0, 0), 2e-3);
        Near([HalfLinear, 0f, HalfLinear], document.Rgb(rgb, 0.5f, 0f, 1f), 2e-3);
    }

    [Fact]
    public void Indexed_colours_come_from_the_table_scaled_to_the_base_ranges()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace rgb = document.GetColorSpace(Cos("[/Indexed /DeviceRGB 1 <FF0000 0000FF>]"));
        PdfColorSpace lab = document.GetColorSpace(Cos("[/Indexed [/Lab << /WhitePoint [0.9642 1 0.8249] >>] 0 <FF8080>]"));
        byte[] bytes = new byte[6];

        Near([0f, 0f, 1f], document.Rgb(rgb, 1));
        Near([0f, 0f, 1f], document.Rgb(rgb, 7.6f));
        Near([1f, 0f, 0f], document.Rgb(rgb, 0.4f));
        document.GetColorConverter(rgb).Convert(new byte[] { 1, 0 }, bytes, 2);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0 }, bytes);
        Near([1f, 1f, 1f], document.Rgb(lab, 0), 5e-3);
    }

    [Fact]
    public void Separation_tints_go_through_the_tint_transform_to_the_alternate()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty), new PdfOptions().UseColorManagement(ManagedColorManagement.Classic));
        PdfColorSpace gold = document.GetColorSpace(Cos("[/Separation /Gold /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.2 1 0] /N 1 >>]"));
        byte[] bytes = new byte[6];

        Near([1f, 0.8f, 0f], document.Rgb(gold, 1));
        Near([1f, 0.9f, 0.5f], document.Rgb(gold, 0.5f));
        document.GetColorConverter(gold).Convert(new byte[] { 255, 0 }, bytes, 2);
        Assert.Equal(new byte[] { 255, 204, 0, 255, 255, 255 }, bytes);
    }

    [Fact]
    public void A_missing_tint_transform_is_replaced_by_subtractive_ink_on_the_alternate()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty), new PdfOptions().UseColorManagement(ManagedColorManagement.Classic));

        Near([0.75f, 0.75f, 0.75f], document.Rgb(document.GetColorSpace(Cos("[/Separation /S /DeviceRGB 0]")), 0.25f));
        Near([0.75f, 0.75f, 0.75f], document.Rgb(document.GetColorSpace(Cos("[/Separation /S /DeviceCMYK /Identity]")), 0.25f));
        Near([0.4f, 0.4f, 0.4f], document.Rgb(document.GetColorSpace(Cos("[/DeviceN [/A /B] /DeviceGray 0]")), 0.6f, 0.1f));
    }

    [Fact]
    public void All_marks_every_colourant_and_None_paints_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        const string tint = "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [0] /N 1 >>";
        PdfColorSpace all = document.GetColorSpace(Cos($"[/Separation /All /DeviceGray {tint}]"));
        PdfColorSpace none = document.GetColorSpace(Cos($"[/Separation /None /DeviceGray {tint}]"));
        PdfColorSpace allNone = document.GetColorSpace(Cos("[/DeviceN [/None /None] /DeviceGray << /FunctionType 4 /Domain [0 1 0 1] /Range [0 1] /Length 5 >>]"));
        float[] cmyk = new float[4];

        Near([0.7f, 0.7f, 0.7f], document.Rgb(all, 0.3f));
        document.GetColorConverter(all, new ColorConversion { Target = DeviceColorModel.Cmyk }).Convert([0.3f], cmyk);
        Near([0.3f, 0.3f, 0.3f, 0.3f], cmyk);
        Assert.False(document.GetColorConverter(all).PaintsNothing);
        Assert.True(document.GetColorConverter(none).PaintsNothing);
        Assert.True(document.GetColorConverter(allNone).PaintsNothing);
        Near([1f, 1f, 1f], document.Rgb(none, 1));
    }

    [Fact]
    public void An_uncoloured_pattern_converts_through_its_underlying_space()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfColorSpace space = document.GetColorSpace(Cos("[/Pattern /DeviceRGB]"));

        Near([0f, 1f, 0f], document.Rgb(space, 0, 1, 0));
        Assert.Equal(3, document.GetColorConverter(space).InputCount);
    }

    [Fact]
    public void A_converter_is_shared_and_rebuilt_when_its_space_changes()
    {
        using PdfDocument document = PdfDocument.Create();
        var array = (CosArray)Cos("[/CalGray << /WhitePoint [0.95047 1 1.08883] /Gamma 1 >>]");
        PdfColorSpace space = document.GetColorSpace(array);
        PdfColorConverter converter = document.GetColorConverter(space);

        Assert.Same(converter, document.GetColorConverter(space));
        Near([HalfLinear, HalfLinear, HalfLinear], document.Rgb(space, 0.5f), 2e-3);
        ((CosDictionary)array[1])[new CosName("Gamma")] = new CosReal(2);
        Assert.NotSame(converter, document.GetColorConverter(space));
        Near([0.5371f, 0.5371f, 0.5371f], document.Rgb(space, 0.5f), 2e-3);
    }

    [Fact]
    public void Components_are_clipped_into_their_ranges_before_conversion()
    {
        using PdfDocument document = PdfDocument.Create();

        Near([1f, 0f, 1f], document.Rgb(PdfDeviceRgbColorSpace.Instance, 7, -3, float.MaxValue));
    }
}
