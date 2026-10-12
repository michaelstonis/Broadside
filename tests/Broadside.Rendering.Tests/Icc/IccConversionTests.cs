using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.Rendering.Tests.Icc.IccProfiles;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// The ICC engine through the colour-management extension point (ISO 32000-2 §8.6.5.5, §10.3; ICC.1:2022 Annex F): what it
/// converts itself, what it leaves to the wrapped management, and how it reports a profile it cannot use (ADR 0005).
/// </summary>
public sealed class IccConversionTests
{
    private static readonly PdfOptions Icc = new PdfOptions().UseIccColorManagement();

    public static TheoryData<string, byte[]> BrokenProfiles => new()
    {
        { "rTRC", Rgb(red: Curve(1000)) },
        { "gTRC", Rgb(green: Parametric(9, 1)) },
        { "bTRC", Rgb(blue: Parametric(3, 2.4, 1)) },
        { "rXYZ", Profile("mntr", "RGB ", [("rXYZ", Gamma(1)), ("gXYZ", Xyz(0.4, 0.7, 0.1)), ("bXYZ", Xyz(0.1, 0.1, 0.7)), ("rTRC", Gamma(1)), ("gTRC", Gamma(1)), ("bTRC", Gamma(1))]) },
        { "gTRC", Profile("mntr", "RGB ", [("rXYZ", Xyz(0.4, 0.2, 0)), ("gXYZ", Xyz(0.4, 0.7, 0.1)), ("bXYZ", Xyz(0.1, 0.1, 0.7)), ("rTRC", Gamma(1)), ("bTRC", Gamma(1))]) },
    };

    [Fact]
    public void A_matrix_TRC_profile_converts_through_its_curves_and_colorants()
    {
        using PdfDocument document = PdfDocument.Create(Icc);
        PdfColorSpace space = document.GetColorSpace(Space(Rgb(), 3, "DeviceRGB"));

        // Gamma 1.8: 0.5 is linear 0.287, which the sRGB encoding shows as 0.572 (the managed default's alternate would give 0.5).
        float[] rgb = document.Rgb(space, 0.5f, 0.5f, 0.5f);

        Assert.All(rgb, value => Assert.InRange(value, 0.566f, 0.578f));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(BrokenProfiles))]
    public void A_profile_with_unusable_tags_converts_through_the_alternate_with_IccProfileInvalid(string tag, byte[] profile)
    {
        using PdfDocument document = PdfDocument.Create(Icc);
        PdfColorSpace space = document.GetColorSpace(Space(profile, 3, "DeviceRGB"));

        float[] rgb = document.Rgb(space, 0.5f, 0.25f, 1f);

        Assert.Equal([0.5f, 0.25f, 1f], rgb);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("IccProfileInvalid", diagnostic.Code);
        Assert.Contains(tag, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unusable_profile_is_an_error_in_strict_mode()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict().UseIccColorManagement());
        PdfColorSpace space = document.GetColorSpace(Space(Rgb(red: Curve(1000)), 3, "DeviceRGB"));

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.GetColorConverter(space));

        Assert.Equal("IccProfileInvalid", error.Diagnostic.Code);
    }

    [Fact]
    public void A_lookup_table_profile_and_device_spaces_go_to_the_wrapped_management_without_a_diagnostic()
    {
        byte[] lut = Profile("mntr", "RGB ", [("wtpt", Xyz(0.9642, 1, 0.8249)), ("A2B0", [.. "mAB "u8, 0, 0, 0, 0])]);
        using PdfDocument document = PdfDocument.Create(Icc);

        Assert.Equal([0.5f, 0.25f, 1f], document.Rgb(document.GetColorSpace(Space(lut, 3, "DeviceRGB")), 0.5f, 0.25f, 1f));
        Assert.Equal([0.5f, 0.25f, 1f], document.Rgb(PdfDeviceRgbColorSpace.Instance, 0.5f, 0.25f, 1f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Gray_and_CMYK_targets_convert_the_profile_s_sRGB_by_the_device_formulas()
    {
        byte[] gray = Profile("mntr", "GRAY", [("wtpt", Xyz(0.9642, 1, 0.8249)), ("kTRC", Parametric(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045))]);
        using PdfDocument document = PdfDocument.Create(Icc);
        PdfColorSpace space = document.GetColorSpace(Space(gray, 1));
        float[] device = new float[4];

        // The sRGB curve takes 0.5 to sRGB (0.5, 0.5, 0.5): gray 0.5 (§10.4.2.2), and CMYK (0, 0, 0, 0.5) (§10.4.2.3, BG(k) = UCR(k) = k).
        document.GetColorConverter(space, new ColorConversion { Target = DeviceColorModel.Gray }).Convert([0.5f], device);
        Assert.InRange(device[0], 0.497f, 0.503f);
        document.GetColorConverter(space, new ColorConversion { Target = DeviceColorModel.Cmyk }).Convert([0.5f], device);
        Assert.InRange(device[0], 0f, 0.003f);
        Assert.InRange(device[2], 0f, 0.003f);
        Assert.InRange(device[3], 0.497f, 0.503f);
    }

    [Fact]
    public void The_absolute_colorimetric_intent_keeps_the_media_white()
    {
        byte[] d50 = Profile("mntr", "GRAY", [("wtpt", Xyz(0.9642, 1, 0.8249)), ("kTRC", Gamma(1))]);
        byte[] d65 = Profile("mntr", "GRAY", [("wtpt", Xyz(0.9505, 1, 1.089)), ("kTRC", Gamma(1))], major: 2);
        using PdfDocument document = PdfDocument.Create(Icc);
        var absolute = new ColorConversion { Intent = RenderingIntent.AbsoluteColorimetric };
        float[] rgb = new float[3];

        // Media-relative white is the display's white; ICC-absolute (§6.3.2.2) shows a D50 media white as D50, yellowish on sRGB,
        // and a D65 media white as the sRGB white.
        Assert.All(document.Rgb(document.GetColorSpace(Space(d50, 1)), 1f), value => Assert.InRange(value, 0.997f, 1f));
        document.GetColorConverter(document.GetColorSpace(Space(d50, 1)), absolute).Convert([1f], rgb);
        Assert.True(rgb[2] < 0.9f, $"blue {rgb[2]}");
        document.GetColorConverter(document.GetColorSpace(Space(d65, 1)), absolute).Convert([1f], rgb);
        Assert.All(rgb, value => Assert.InRange(value, 0.99f, 1f));
    }
}
