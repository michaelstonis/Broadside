using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Applying redaction to form XObjects (issue #624): a form painted over an area is copied and the copy rewritten with the areas
/// mapped into its space, so another page that paints the same form keeps it; a nested form is rewritten the same way (ISO 32000-2
/// §8.10.1).
/// </summary>
public sealed class RedactionFormTests
{
    private static readonly PdfRedactionOptions NoFill = new PdfRedactionOptions().WithFillColor(null);

    [Fact]
    public void A_form_shared_by_two_pages_is_copied_and_the_other_page_keeps_it()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /Resources << /Font << /F1 6 0 R >> /XObject << /Fm 5 0 R >> >> >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R >>",
            ContentSamples.Stream("BT /F1 12 Tf 0 0 Td (Secret Public) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 -10 200 20] /Resources << /Font << /F1 6 0 R >> >>"),
            RedactionSamples.Helvetica,
            ContentSamples.Stream("q 1 0 0 1 72 700 cm /Fm Do Q"),
        ]));
        PdfPage first = document.Pages[0];

        PdfRedactionResult result = first.Redact([RedactionSamples.Area(70, 690, 106, 715)], NoFill);

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal("Public", first.ExtractText().Text.Trim());
        Assert.Equal("Secret Public", document.Pages[1].ExtractText().Text.Trim());
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Kind == RedactionDiagnostics.RedactionFormCopied);
        Assert.False(ReferenceEquals(first.Resources, document.Pages[1].Resources));
    }

    [Fact]
    public void A_nested_form_is_rewritten_with_the_areas_in_its_own_space()
    {
        using PdfDocument document = RedactionSamples.Open(
            "q 2 0 0 2 0 0 cm /Outer Do Q",
            "/XObject << /Outer 6 0 R >>",
            ContentSamples.Stream("q 1 0 0 1 36 350 cm /Inner Do Q", "/Type /XObject /Subtype /Form /BBox [0 0 306 396] /Resources << /XObject << /Inner 7 0 R >> >>"),
            ContentSamples.Stream("BT /F1 6 Tf 0 0 Td (Secret Public) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 -5 100 10] /Resources << /Font << /F1 5 0 R >> >>"));
        PdfPage page = document.Pages[0];

        // The inner form's text is at (72, 700) on the page, 12 points high: the outer form scales by 2.
        PdfRedactionResult result = page.Redact([RedactionSamples.Area(70, 690, 106, 715)], NoFill);

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal("Public", page.ExtractText().Text.Trim());
        byte[] saved = RedactionSamples.Save(document, result);
        RedactionSamples.AssertGone(saved, "Secret");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Public", reopened.Pages[0].ExtractText().Text.Trim());
    }

    [Fact]
    public void A_form_wholly_inside_an_area_is_no_longer_painted()
    {
        using PdfDocument document = RedactionSamples.Open(
            "q 1 0 0 1 100 100 cm /Fm Do Q",
            "/XObject << /Fm 6 0 R >>",
            ContentSamples.Stream("0 0 50 50 re f", "/Type /XObject /Subtype /Form /BBox [0 0 50 50]"));
        PdfPage page = document.Pages[0];

        page.Redact([RedactionSamples.Area(90, 90, 200, 200)], NoFill);

        Assert.DoesNotContain("Do", RedactionSamples.Content(document, page), StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_away_from_the_areas_is_left_alone()
    {
        using PdfDocument document = RedactionSamples.Open(
            "q 1 0 0 1 100 100 cm /Fm Do Q",
            "/XObject << /Fm 6 0 R >>",
            ContentSamples.Stream("0 0 50 50 re f", "/Type /XObject /Subtype /Form /BBox [0 0 50 50]"));
        PdfPage page = document.Pages[0];
        CosObject resources = page.Dictionary[new CosName("Resources")];

        page.Redact([RedactionSamples.Area(300, 300, 400, 400)], NoFill);

        Assert.Equal("q 1 0 0 1 100 100 cm /Fm Do Q", RedactionSamples.Content(document, page));
        Assert.Same(resources, page.Dictionary[new CosName("Resources")]);
    }
}
