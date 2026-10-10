using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// The colour operators of ISO 32000-2 §8.6.8, Table 73, observed through the public processor seam: the fill and stroke colours of
/// the graphics state at each paint.
/// </summary>
public class ColorOperatorTests
{
    private const string Rect = " 0 0 10 10 re f\n";

    [Fact]
    public void The_initial_colours_are_device_gray_black()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< >>", "0 0 10 10 re B"));

        Assert.Equal("DeviceGray 0", Assert.Single(paints).FillText);
        Assert.Equal("DeviceGray 0", paints[0].StrokeText);
        Assert.Empty(codes);
    }

    [Fact]
    public void G_g_RG_rg_K_k_select_the_device_space_and_set_the_colour()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< >>",
            "0.25 g 0.75 G" + Rect + "0.1 0.2 0.3 rg 0.4 0.5 0.6 RG" + Rect + "0.1 0.2 0.3 0.4 k 0.5 0.6 0.7 0.8 K" + Rect));

        Assert.Equal(["DeviceGray 0.25", "DeviceRgb 0.1 0.2 0.3", "DeviceCmyk 0.1 0.2 0.3 0.4"], paints.Select(p => p.FillText));
        Assert.Equal(["DeviceGray 0.75", "DeviceRgb 0.4 0.5 0.6", "DeviceCmyk 0.5 0.6 0.7 0.8"], paints.Select(p => p.StrokeText));
        Assert.Empty(codes);
    }

    [Fact]
    public void Cs_selects_a_named_resource_and_its_initial_colour_and_sc_sets_the_components()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /CS0 [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] /Sep 5 0 R >> >>",
            "/CS0 cs" + Rect + "0.5 0.25 1 sc" + Rect + "/Sep cs" + Rect + "0.3 scn" + Rect + "/DeviceCMYK CS 0 0 10 10 re S",
            "[/Separation /Spot /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]"));

        Assert.Equal(["CalRgb 0 0 0", "CalRgb 0.5 0.25 1", "Separation 1", "Separation 0.3", "Separation 0.3"], paints.Select(p => p.FillText));
        Assert.Equal("DeviceCmyk 0 0 0 1", paints[^1].StrokeText);
        Assert.Empty(codes);
    }

    [Fact]
    public void CS_and_cs_reset_the_colour_to_the_initial_colour_of_each_family()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /Lab [/Lab << /WhitePoint [0.9642 1 0.8249] /Range [10 50 -20 -5] >>] /Icc [/ICCBased 5 0 R] "
            + "/DN [/DeviceN [/A /B] /DeviceGray << /FunctionType 2 /Domain [0 1 0 1] /C0 [1] /C1 [0] /N 1 >>] "
            + "/Ix [/Indexed /DeviceRGB 1 <FF0000 00FF00>] >> >>",
            "0.5 0.5 0.5 rg /Lab cs" + Rect + "/Icc cs" + Rect + "/DN cs" + Rect + "/Ix cs" + Rect + "/DeviceRGB cs" + Rect,
            "<< /N 3 /Range [0.2 1 -1 -0.5 0 1] /Length 0 >>\nstream\n\nendstream"));

        Assert.Equal(["Lab 0 10 -5", "IccBased 0.2 -0.5 0", "DeviceN 1 1", "Indexed 0", "DeviceRgb 0 0 0"], paints.Select(p => p.FillText));
        Assert.Contains("IccProfileInvalid", codes);
    }

    [Fact]
    public void Scn_in_a_pattern_space_names_the_pattern_and_captures_the_components_of_an_uncoloured_pattern()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /P [/Pattern /DeviceRGB] >> /Pattern << /Tile 5 0 R /Shade 6 0 R >> >>",
            "/P cs" + Rect + "1 0 0 /Tile scn" + Rect + "/Pattern cs /Shade scn" + Rect,
            "<< /PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 1 1] /XStep 1 /YStep 1 /Resources << >> /Length 0 >>\nstream\n\nendstream",
            "<< /PatternType 2 /Shading << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >> >> >>"));

        Assert.Equal(["Pattern 0 0 0", "Pattern 1 0 0 /Tile", "Pattern /Shade"], paints.Select(p => p.FillText));
        Assert.Null(paints[0].Fill.Pattern);
        Assert.IsType<CosStream>(paints[1].Fill.Pattern);
        Assert.IsType<CosDictionary>(paints[2].Fill.Pattern);
        Assert.True(paints[2].Fill.PatternMatrix.IsIdentity);
        Assert.Equal([1f, 0f, 0f], paints[1].FillRgb);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_name_missing_from_the_resources_reads_as_device_gray_with_a_diagnostic()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< >>", "1 0 0 rg /Nowhere cs 0.5 sc" + Rect));

        Assert.Equal("DeviceGray 0.5", Assert.Single(paints).FillText);
        Assert.Equal(["ContentColorSpaceMissing"], codes);
    }

    [Fact]
    public void An_inline_image_abbreviation_outside_an_inline_image_is_read_after_the_resources()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< /ColorSpace << /G [/CalGray << /WhitePoint [0.9505 1 1.089] >>] >> >>", "/RGB cs 1 0 0 sc" + Rect + "/G cs" + Rect));

        Assert.Equal(["DeviceRgb 1 0 0", "CalGray 0"], paints.Select(p => p.FillText));
        Assert.Equal(["ContentColorSpaceAbbreviated"], codes);
    }

    [Fact]
    public void Too_few_components_skip_the_operator_and_too_many_use_the_last_ones()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< >>", "/DeviceRGB cs 0.5 0.5 sc" + Rect + "0.9 0.1 0.2 0.3 sc" + Rect));

        Assert.Equal(["DeviceRgb 0 0 0", "DeviceRgb 0.1 0.2 0.3"], paints.Select(p => p.FillText));
        Assert.Equal(["ContentColorOperandCount"], codes);
    }

    [Fact]
    public void Sc_in_a_pattern_space_and_scn_with_a_name_elsewhere_are_skipped()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< /Pattern << /P0 5 0 R >> >>", "0.5 g /P0 scn" + Rect + "/Pattern cs 0.5 sc" + Rect + "0.2 scn" + Rect, "<< /PatternType 2 /Shading << >> >>"));

        Assert.Equal(["DeviceGray 0.5", "Pattern", "Pattern"], paints.Select(p => p.FillText));
        Assert.Equal(["ContentColorOperatorInvalid"], codes);
    }

    [Fact]
    public void Sc_in_a_separation_space_is_accepted_with_a_diagnostic()
    {
        (List<Paint> paints, string[] codes) = Run(Page(
            "<< /ColorSpace << /S [/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0 0 1] /N 1 >>] >> >>",
            "/S cs 0.4 sc" + Rect));

        Assert.Equal("Separation 0.4", Assert.Single(paints).FillText);
        Assert.Equal(["ContentColorOperatorMismatch"], codes);
    }

    [Fact]
    public void A_pattern_missing_from_the_resources_gives_a_colour_without_a_pattern()
    {
        (List<Paint> paints, string[] codes) = Run(Page("<< >>", "/Pattern cs /Nowhere scn" + Rect));

        Assert.Null(Assert.Single(paints).Fill.Pattern);
        Assert.Equal(["ContentPatternMissing"], codes);
    }

    [Fact]
    public void Strict_mode_throws_for_a_missing_colour_space()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", "/Nowhere cs" + Rect), new PdfOptions().UseStrict());

        var exception = Assert.Throws<Diagnostics.DiagnosticException>(() => document.Pages[0].ProcessContent(new ColorRecorder()));
        Assert.Equal("ContentColorSpaceMissing", exception.Diagnostic.Code);
    }
}
