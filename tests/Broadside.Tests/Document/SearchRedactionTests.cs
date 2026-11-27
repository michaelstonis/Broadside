using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Redaction;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Document;

/// <summary>
/// Search and redact (issue #633): queries and presets applied at once remove exactly the matched glyphs, by identity and not by
/// area, across line breaks, kerned <c>TJ</c> arrays, <c>ActualText</c> and invisible text; neighbors stay; <c>GlyphsOnly</c> decides
/// whether paths and images under the match go too; several queries share one extraction and merge. ISO 32000-2 §12.5.6.23, §9.4.3,
/// §9.10 and §14.9.4.
/// </summary>
public sealed class SearchRedactionTests
{
    private static readonly PdfRedactionOptions NoFill = new PdfRedactionOptions().WithFillColor(null);

    [Fact]
    public void A_hit_across_a_line_break_loses_its_glyphs_on_both_lines()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 10 Tf 72 700 Td (Card 4111 1111) Tj 0 -12 Td (1111 1111 ends) Tj ET");

        PdfRedactionResult result = document.Redact(TextPatterns.CreditCardNumber, NoFill);

        Assert.Equal(18, result.GlyphsRemoved);
        Assert.Equal("Card ends", Normalize(document.Pages[0].ExtractText().Text));
        RedactionSamples.AssertGone(RedactionSamples.Save(document, result), "4111");
    }

    [Fact]
    public void A_hit_inside_a_kerned_TJ_is_removed_and_the_rest_keeps_its_positions()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td [(Mail: jane.d) -15 (oe@exam) 12 (ple.com) -40 ( thanks)] TJ ET");
        PdfPage page = document.Pages[0];
        List<TextGlyph> before = [.. page.ExtractText().Glyphs];

        PdfRedactionResult result = document.Redact(TextPatterns.EmailAddress, NoFill);

        Assert.Equal("jane.doe@example.com".Length, result.GlyphsRemoved);
        List<TextGlyph> after = [.. page.ExtractText().Glyphs];
        Assert.Equal("Mail: thanks", Normalize(page.ExtractText().Text));
        AssertSamePlaces([.. before.Take(6), .. before.Skip(26)], after);
        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("exam", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hit_in_ActualText_removes_every_glyph_of_the_replaced_sequence()
    {
        using PdfDocument document = RedactionSamples.Open(
            "BT /F1 12 Tf 72 700 Td (Name ) Tj /Span << /ActualText (Smith) >> BDC (XYZ) Tj EMC ( here) Tj ET");

        PdfRedactionResult result = document.Redact(new TextQuery("mit"), NoFill);

        Assert.Equal(3, result.GlyphsRemoved);
        Assert.Equal("Name here", Normalize(document.Pages[0].ExtractText().Text));
        string content = RedactionSamples.Content(document, document.Pages[0]);
        Assert.DoesNotContain("XYZ", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Smith", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hit_in_rendering_mode_3_is_found_and_removed()
    {
        using PdfDocument document = RedactionSamples.Open(
            "BT /F1 12 Tf 72 700 Td (Visible line) Tj ET BT 3 Tr /F1 12 Tf 72 650 Td (ocr.layer@example.org) Tj ET");

        PdfRedactionResult result = document.Redact(TextPatterns.EmailAddress, NoFill);

        Assert.Equal("ocr.layer@example.org".Length, result.GlyphsRemoved);
        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithHiddenContent().WithArtifacts());
        Assert.Equal("Visible line", Normalize(text.Text));
        RedactionSamples.AssertGone(RedactionSamples.Save(document, result), "ocr.layer");
    }

    [Fact]
    public void Text_extraction_options_on_the_options_narrow_what_is_searched()
    {
        using PdfDocument document = RedactionSamples.Open(
            "/Artifact BMC BT /F1 12 Tf 72 700 Td (art@example.org) Tj ET EMC BT /F1 12 Tf 72 650 Td (real@example.org) Tj ET");

        PdfRedactionResult result = document.Redact(TextPatterns.EmailAddress, new PdfRedactionOptions().WithFillColor(null).WithTextExtractionOptions(new TextExtractionOptions()));

        Assert.Equal("real@example.org".Length, result.GlyphsRemoved);
        string content = RedactionSamples.Content(document, document.Pages[0]);
        Assert.Contains("art@example.org", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_neighbor_whose_box_overlaps_the_match_is_kept()
    {
        // The 400 adjustment pulls "j" 4.8 points left, over the colon's box: an area over the match would take the colon too.
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td [(To:) 400 (jane@example.com)] TJ ET");
        PdfPage page = document.Pages[0];
        TextMatch match = Assert.Single(page.FindText(TextPatterns.EmailAddress));
        TextGlyph colon = page.ExtractText().Glyphs[2];
        Assert.True(colon.Quad.Bounds.Right > match.Bounds.Left, "The colon's box overlaps the match.");

        PdfRedactionResult result = page.Redact([match], NoFill);

        Assert.Equal("jane@example.com".Length, result.GlyphsRemoved);
        TextPage after = page.ExtractText();
        Assert.Equal("To:", Normalize(after.Text));
        Assert.Equal(colon.Quad, after.Glyphs[2].Quad);
    }

    [Fact]
    public void Without_GlyphsOnly_the_paths_under_the_match_go_too()
    {
        using PdfDocument document = RedactionSamples.Open(Highlighted);

        PdfRedactionResult result = document.Redact(TextPatterns.EmailAddress, NoFill);

        Assert.Equal("jane@example.com".Length, result.GlyphsRemoved);
        Assert.True(result.PathsRemoved > 0);
        Assert.DoesNotContain("150 680 200 40 re", RedactionSamples.Content(document, document.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void With_GlyphsOnly_the_paths_under_the_match_stay()
    {
        using PdfDocument document = RedactionSamples.Open(Highlighted);

        PdfRedactionResult result = document.Redact(TextPatterns.EmailAddress, new PdfRedactionOptions().WithFillColor(null).WithGlyphsOnly());

        Assert.Equal("jane@example.com".Length, result.GlyphsRemoved);
        Assert.Equal(0, result.PathsRemoved);
        Assert.Contains("150 680 200 40 re", RedactionSamples.Content(document, document.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_queries_run_on_one_extraction_and_overlapping_matches_merge()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Ask jane@example.com or 192.0.2.10 now) Tj ET");

        PdfRedactionResult result = document.Redact([new TextQuery("jane"), TextPatterns.EmailAddress, TextPatterns.IPv4Address], NoFill);

        Assert.Equal("jane@example.com".Length + "192.0.2.10".Length, result.GlyphsRemoved);
        Assert.Equal("Ask or now", Normalize(document.Pages[0].ExtractText().Text));
    }

    [Fact]
    public void Merge_joins_overlapping_matches_and_keeps_touching_ones_apart()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Ask jane@example.com now) Tj ET");
        PdfPage page = document.Pages[0];
        List<TextMatch> found = [.. page.FindText(new TextQuery("jane")), .. page.FindText(TextPatterns.EmailAddress), .. page.FindText(new TextQuery("now"))];

        List<TextMatch> merged = RedactionSession.Merge(found);

        Assert.Equal(["jane@example.com", "now"], merged.Select(static match => match.Text));
        Assert.Equal(16, merged[0].GlyphCount);
    }

    [Fact]
    public void A_query_with_no_match_changes_nothing()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Nothing to see) Tj ET");
        string before = RedactionSamples.Content(document, document.Pages[0]);

        PdfRedactionResult result = document.Redact(TextPatterns.UsSocialSecurityNumber);

        Assert.Equal(0, result.GlyphsRemoved);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(before, RedactionSamples.Content(document, document.Pages[0]));
    }

    [Fact]
    public void Pages_limit_the_search()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (SSN 123-45-6789) Tj ET");
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));

        PdfRedactionResult result = document.Redact(TextPatterns.UsSocialSecurityNumber, NoFill, 1..);

        Assert.Equal(0, result.GlyphsRemoved);
    }

    [Fact]
    public void The_overlay_is_drawn_over_each_match_quad()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 10 Tf 72 700 Td (Card 4111 1111) Tj 0 -12 Td (1111 1111 ends) Tj ET");

        document.Redact(TextPatterns.CreditCardNumber);

        // One black fill per line the match covers, drawn as a Redaction artifact after the content.
        string content = RedactionSamples.Content(document, document.Pages[0]);
        string overlay = content[content.IndexOf("/Artifact", StringComparison.Ordinal)..];
        Assert.Equal(2, CountOf(overlay, " m"));
    }

    [Fact]
    public void A_match_that_cannot_be_located_is_removed_by_area_and_recorded_without_its_text()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        var stale = new TextMatch(0, "Elsewhere", 0, 0, 6, [RedactionSamples.Area(70, 690, 106, 715)], new PdfRectangle(70, 690, 106, 715));

        PdfRedactionResult result = page.Redact([stale], NoFill);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RedactionDiagnostics.RedactionMatchNotLocated, diagnostic.Kind);
        Assert.DoesNotContain("Elsewhere", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal("Public", Normalize(page.ExtractText().Text));
    }

    [Fact]
    public void Strict_mode_throws_for_a_match_that_cannot_be_located_and_leaves_the_page_unchanged()
    {
        byte[] file = ContentSamples.OnePage(Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET"), "/Font << /F1 5 0 R >>", [RedactionSamples.Helvetica]);
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfPage page = document.Pages[0];
        string before = RedactionSamples.Content(document, page);
        var stale = new TextMatch(0, "Elsewhere", 0, 0, 6, [RedactionSamples.Area(70, 690, 106, 715)], new PdfRectangle(70, 690, 106, 715));

        Assert.Throws<DiagnosticException>(() => page.Redact([stale]));

        Assert.Equal(before, RedactionSamples.Content(document, page));
    }

    [Fact]
    public void A_match_from_another_page_is_refused()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret) Tj ET");
        var other = new TextMatch(3, "Secret", 0, 0, 6, [RedactionSamples.Area(70, 690, 106, 715)], new PdfRectangle(70, 690, 106, 715));

        Assert.Throws<ArgumentException>(() => document.Pages[0].Redact([other]));
    }

    [Fact]
    public void Text_in_a_form_XObject_is_removed_by_identity()
    {
        using PdfDocument document = RedactionSamples.Open(
            "BT /F1 12 Tf 72 700 Td (Page) Tj ET q 1 0 0 1 100 0 cm /Fm1 Do Q",
            "/XObject << /Fm1 6 0 R >>",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Length 49 >>\nstream\nBT /F1 12 Tf 72 650 Td (SSN 123-45-6789 ok) Tj ET\nendstream");

        PdfRedactionResult result = document.Redact(TextPatterns.UsSocialSecurityNumber, NoFill);

        Assert.Equal(11, result.GlyphsRemoved);
        Assert.DoesNotContain(result.Diagnostics, static diagnostic => diagnostic.Kind == RedactionDiagnostics.RedactionMatchNotLocated);
        string text = document.Pages[0].ExtractText().Text;
        Assert.DoesNotContain("123", text, StringComparison.Ordinal);
        Assert.Contains("ok", text, StringComparison.Ordinal);
        Assert.StartsWith("Page", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_corpus_file_loses_every_preset_value_and_keeps_its_labels()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("redact-pii.pdf"));
        TextQuery[] presets = [TextPatterns.EmailAddress, TextPatterns.UsSocialSecurityNumber, TextPatterns.CreditCardNumber];

        PdfRedactionResult result = document.Redact(presets);

        Assert.Empty(result.Diagnostics);
        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithHiddenContent().WithArtifacts());
        foreach (TextQuery preset in presets)
        {
            Assert.Empty(text.Find(preset));
        }

        Assert.Contains("Email:", text.Text, StringComparison.Ordinal);
        Assert.Contains("SSN:", text.Text, StringComparison.Ordinal);
        byte[] saved = RedactionSamples.Save(document, result);
        RedactionSamples.AssertGone(saved, "example.com");
        RedactionSamples.AssertGone(saved, "6789");
    }

    // A blue rectangle under the end of the email address (97 to 204) and past it; a shape holding the whole match would be kept.
    private const string Highlighted = "0 0 1 rg 150 680 200 40 re f 0 g BT /F1 12 Tf 72 700 Td (Mail jane@example.com) Tj ET";

    private static string Normalize(string text) => string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static void AssertSamePlaces(List<TextGlyph> expected, List<TextGlyph> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].CharacterCode, actual[index].CharacterCode);
            Assert.Equal(expected[index].Origin.X, actual[index].Origin.X, 3);
            Assert.Equal(expected[index].Origin.Y, actual[index].Origin.Y, 3);
        }
    }
}
