namespace Broadside.Tests.Content;

/// <summary>
/// Applying redaction to paths and shadings (issue #624): a subpath under an area is removed, a filled or stroked rectangle cut
/// exactly, one whose region holds the whole area kept (ISO 32000-2 §8.5.2, §8.5.3); a shading is clipped to exclude the area
/// (§8.7.4.2, §8.5.4).
/// </summary>
public sealed class RedactionPathTests
{
    private static readonly PdfRedactionOptions NoFill = new PdfRedactionOptions().WithFillColor(null);

    [Fact]
    public void A_page_background_that_holds_the_area_is_kept()
    {
        using PdfDocument document = RedactionSamples.Open("0.9 g 0 0 612 792 re f");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(100, 100, 200, 200)], NoFill);

        Assert.Equal(0, result.PathsRemoved);
        Assert.Equal("0.9 g 0 0 612 792 re f", RedactionSamples.Content(document, page));
    }

    [Fact]
    public void A_filled_rectangle_under_an_area_is_cut_exactly()
    {
        using PdfDocument document = RedactionSamples.Open("100 100 100 100 re f");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(150, 50, 250, 250)], NoFill);

        Assert.Equal(1, result.PathsRemoved);
        Assert.Equal("100 100 50 100 re\nf", RedactionSamples.Content(document, page).Trim());
    }

    [Fact]
    public void A_shape_under_an_area_is_removed_whole_and_other_subpaths_stay()
    {
        using PdfDocument document = RedactionSamples.Open("q 150 150 m 160 180 170 180 180 150 c h 400 400 m 450 400 l 450 450 l h f Q");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(140, 140, 200, 200)], NoFill);

        Assert.Equal(1, result.PathsRemoved);
        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("160 180", content, StringComparison.Ordinal);
        Assert.Contains("400 400 m\n450 400 l\n450 450 l\nh\nf", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stroked_line_keeps_its_parts_outside_the_area_and_half_its_width()
    {
        using PdfDocument document = RedactionSamples.Open("2 w 100 300 m 300 300 l S");
        PdfPage page = document.Pages[0];

        page.Redact([RedactionSamples.Area(150, 250, 200, 350)], NoFill);

        Assert.Equal("2 w   100 300 m\n149 300 l\n201 300 m\n300 300 l\nS", RedactionSamples.Content(document, page).Trim());
    }

    [Fact]
    public void A_clipping_path_keeps_its_clip_and_paints_only_what_is_left()
    {
        using PdfDocument document = RedactionSamples.Open("100 100 100 100 re W f");
        PdfPage page = document.Pages[0];

        page.Redact([RedactionSamples.Area(150, 50, 250, 250)], NoFill);

        Assert.Equal("100 100 100 100 re W n\n100 100 50 100 re\nf", RedactionSamples.Content(document, page).Trim());
    }

    [Fact]
    public void A_shading_is_clipped_to_leave_out_the_area()
    {
        using PdfDocument document = RedactionSamples.Open(
            "/Sh0 sh",
            "/Shading << /Sh0 << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 612 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >>");
        PdfPage page = document.Pages[0];

        page.Redact([RedactionSamples.Area(100, 100, 200, 200)], NoFill);

        string content = RedactionSamples.Content(document, page);
        Assert.StartsWith("q\n", content, StringComparison.Ordinal);
        Assert.Contains("100 100 m\n200 100 l\n200 200 l\n100 200 l\nh\nW*\nn", content, StringComparison.Ordinal);
        Assert.EndsWith("/Sh0 sh\nQ", content.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_image_under_an_area_is_removed()
    {
        using PdfDocument document = RedactionSamples.Open(
            "q 100 0 0 100 100 100 cm /Im0 Do Q q 100 0 0 100 400 400 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI Q",
            "/XObject << /Im0 6 0 R >>",
            TestSupport.ContentSamples.Stream("ÿ", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8"));
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(50, 50, 250, 250), RedactionSamples.Area(450, 450, 600, 600)], NoFill);

        Assert.Equal(2, result.ImagesChanged);
        Assert.Empty(page.ExtractImages());
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Kind == Diagnostics.RedactionDiagnostics.RedactionImageRemovedWhole);
    }
}
