using Broadside.Graphics;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// DefaultGray, DefaultRGB and DefaultCMYK (ISO 32000-2 §8.6.5.6) as a processor sees them at paint time: the colour keeps the space
/// the operator selected, the converter with the current defaults paints it in the default space, including nested device spaces.
/// </summary>
public class DefaultColorSpaceTests
{
    /// <summary>CalGray and CalRGB whose gamma-1 values are linear sRGB, so a remapped 0.5 paints as 0.7354, not 0.5.</summary>
    private const string Defaults =
        "/DefaultGray [/CalGray << /WhitePoint [0.95047 1 1.08883] >>] "
        + "/DefaultRGB [/CalRGB << /WhitePoint [0.95047 1 1.08883] /Matrix [0.4124564 0.2126729 0.0193339 0.3575761 0.7151522 0.1191920 0.1804375 0.0721750 0.9503041] >>]";

    private const float Remapped = 0.7353570f;

    private const string Rect = " 0 0 10 10 re f\n";

    [Fact]
    public void Device_colours_paint_in_the_default_spaces_of_the_current_resources()
    {
        (List<Paint> paints, string[] codes) = Run(Page($"<< /ColorSpace << {Defaults} >> >>", "0.5 g" + Rect + "0.5 0.5 0.5 rg" + Rect + "0 0 0 0.5 k" + Rect));

        Assert.Equal(["DeviceGray 0.5", "DeviceRgb 0.5 0.5 0.5", "DeviceCmyk 0 0 0 0.5"], paints.Select(p => p.FillText));
        Near([Remapped, Remapped, Remapped], paints[0].FillRgb, 2e-3);
        Near([Remapped, Remapped, Remapped], paints[1].FillRgb, 2e-3);
        Assert.IsType<PdfCalRgbColorSpace>(paints[1].Defaults.Rgb);
        Assert.Null(paints[2].Defaults.Cmyk);
        Assert.Empty(codes);
    }

    [Fact]
    public void The_initial_device_gray_is_remapped_too()
    {
        (List<Paint> paints, _) = Run(Page("<< /ColorSpace << /DefaultGray [/CalGray << /WhitePoint [0.95047 1 1.08883] >>] >> >>", "0 0 10 10 re f"));

        Assert.IsType<PdfCalGrayColorSpace>(Assert.Single(paints).Defaults.Remap(paints[0].Fill.ColorSpace));
        Near([0f, 0f, 0f], paints[0].FillRgb);
    }

    [Fact]
    public void Nested_device_spaces_of_Indexed_Separation_and_Pattern_are_remapped()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            $"<< /ColorSpace << {Defaults} /Ix [/Indexed /DeviceGray 1 <0080>] "
            + "/Sep [/Separation /Spot /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>] /Pat [/Pattern /DeviceRGB] >> "
            + "/Pattern << /T 5 0 R >> >>",
            "/Ix cs 1 sc" + Rect + "/Sep cs 0.5 scn" + Rect + "/Pat cs 0.5 0.5 0.5 /T scn" + Rect,
            "<< /PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 1 1] /XStep 1 /YStep 1 /Resources << >> /Length 0 >>\nstream\n\nendstream"));

        Near([0.7366f, 0.7366f, 0.7366f], paints[0].FillRgb, 2e-3);
        Near([Remapped, Remapped, Remapped], paints[1].FillRgb, 2e-3);
        Near([Remapped, Remapped, Remapped], paints[2].FillRgb, 2e-3);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_default_is_not_remapped_again()
    {
        // A Separation may be DefaultGray; its own DeviceGray alternate stays DeviceGray.
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /DefaultGray [/Separation /S /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [0.2] /C1 [0.2] /N 1 >>] >> >>",
            "0.9 g" + Rect));

        Near([0.2f, 0.2f, 0.2f], Assert.Single(paints).FillRgb);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_default_of_the_wrong_size_or_family_is_ignored_with_a_diagnostic()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /DefaultRGB /DeviceGray /DefaultCMYK [/Lab << /WhitePoint [0.9642 1 0.8249] >>] >> >>",
            "0.5 0.5 0.5 rg" + Rect));

        Assert.True(Assert.Single(paints).Defaults.IsEmpty);
        Near([0.5f, 0.5f, 0.5f], paints[0].FillRgb);
        Assert.Equal(["DefaultColorSpaceInvalid"], codes);
    }

    [Fact]
    public void The_defaults_of_any_resource_dictionary_are_available_from_the_document()
    {
        using PdfDocument document = PdfDocument.Open(Page($"<< /ColorSpace << {Defaults} >> >>", string.Empty));
        PdfDefaultColorSpaces defaults = document.GetDefaultColorSpaces(document.Pages[0].Resources);

        Assert.IsType<PdfCalGrayColorSpace>(defaults.Gray);
        Assert.Same(defaults, document.GetDefaultColorSpaces(document.Pages[0].Resources));
        Assert.Same(PdfDefaultColorSpaces.None, document.GetDefaultColorSpaces(null));
        Assert.Same(PdfDeviceCmykColorSpace.Instance, defaults.Remap(PdfDeviceCmykColorSpace.Instance));
    }
}
