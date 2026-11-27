using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Tests.Content;

/// <summary>
/// Applying redaction to text (issue #624): a glyph under an area leaves its text-showing operator, which becomes a <c>TJ</c> whose
/// adjustments keep every other glyph where it was (ISO 32000-2 §9.4.3, Table 107; §9.4.4), for simple, Type 0 and Type 3 fonts,
/// the quote operators and every rendering mode.
/// </summary>
public sealed class RedactionEditorTests
{
    // Helvetica 12 from x 72: "Secret" spans 72 to 106.68, the space to 110.02, "Public" from 110.02.
    private static readonly Quad SecretArea = RedactionSamples.Area(70, 690, 106, 715);

    [Fact]
    public void A_simple_font_loses_the_covered_glyphs_and_the_others_keep_their_positions()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];
        List<(string Text, PdfRectangle Box)> before = Glyphs(page);

        PdfRedactionResult result = page.Redact([SecretArea], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal(" Public", page.ExtractText().Text.TrimEnd());
        AssertSamePositions(before.Skip(6), Glyphs(page));
        Assert.DoesNotContain("Secret", RedactionSamples.Content(document, page), StringComparison.Ordinal);
    }

    [Fact]
    public void A_glyph_in_the_middle_becomes_an_adjustment_of_its_advance()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 10 Tf 100 700 Td (AXB) Tj ET");
        PdfPage page = document.Pages[0];
        List<(string Text, PdfRectangle Box)> before = Glyphs(page);

        // Helvetica A is 667 wide: X spans 106.67 to 113.34.
        page.Redact([RedactionSamples.Area(108, 695, 112, 712)], new PdfRedactionOptions().WithFillColor(null));

        Assert.Contains("[<41>-667 <42>] TJ", RedactionSamples.Content(document, page), StringComparison.Ordinal);
        AssertSamePositions([before[0], before[2]], Glyphs(page));
    }

    [Fact]
    public void Kerning_adjustments_of_a_TJ_array_are_kept_and_merged_with_the_removed_advances()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td [(A) -120 (V) 80 (Secret) -250 (Tail)] TJ ET");
        PdfPage page = document.Pages[0];
        List<(string Text, PdfRectangle Box)> before = Glyphs(page);
        PdfRectangle secret = Quad.FromRectangle(Matrix.Identity, before[1].Box.Right + 0.1, 690, before[7].Box.Right - 0.5, 715).Bounds;

        PdfRedactionResult result = page.Redact([Quad.FromRectangle(Matrix.Identity, secret.Left, secret.Bottom, secret.Right, secret.Top)], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.Equal("AVTail", page.ExtractText().Text.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd());
        AssertSamePositions([before[0], before[1], .. before.Skip(8)], Glyphs(page));
        Assert.Contains("-120", RedactionSamples.Content(document, page), StringComparison.Ordinal);
    }

    [Fact]
    public void The_quote_operators_keep_their_line_moves_and_spacing()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 14 TL 72 700 Td (Keep) Tj (Secret) ' 2 1 (Hidden word) \" (After) ' ET");
        PdfPage page = document.Pages[0];
        List<(string Text, PdfRectangle Box)> before = Glyphs(page);

        // "Secret" on the second line (baseline 686) and "Hidden" on the third (672).
        PdfRedactionResult result = page.Redact([RedactionSamples.Area(70, 683, 100, 695), RedactionSamples.Area(70, 669, 118, 681)], new PdfRedactionOptions().WithFillColor(null));

        Assert.True(result.GlyphsRemoved >= 12);
        string text = page.ExtractText().Text;
        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", text, StringComparison.Ordinal);
        Assert.Contains("Keep", text, StringComparison.Ordinal);
        Assert.Contains("word", text, StringComparison.Ordinal);
        Assert.Contains("After", text, StringComparison.Ordinal);
        List<(string Text, PdfRectangle Box)> after = Glyphs(page);
        PdfRectangle[] areas = [new(70, 683, 100, 695), new(70, 669, 118, 681)];
        AssertSamePositions(before.Where(glyph => !areas.Any(area => glyph.Box.Left < area.Right && glyph.Box.Right > area.Left && glyph.Box.Bottom < area.Top && glyph.Box.Top > area.Bottom)), after);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void Invisible_and_clipping_text_is_removed_too(int mode)
    {
        using PdfDocument document = RedactionSamples.Open($"BT /F1 12 Tf {mode} Tr 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([SecretArea], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(6, result.GlyphsRemoved);
        Assert.DoesNotContain("Secret", page.ExtractText().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type0_font_with_two_byte_codes_loses_whole_codes()
    {
        const string font = "<< /Type /Font /Subtype /Type0 /BaseFont /Sample /Encoding /Identity-H /DescendantFonts [7 0 R] /ToUnicode 8 0 R >>";
        const string descendant = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Sample /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 500 /FontDescriptor << /Type /FontDescriptor /FontName /Sample /Flags 32 /FontBBox [0 -200 1000 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >> >>";
        const string cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Sample def 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0001> <0005> <0041> endbfrange endcmap CMapName currentdict /CMap defineresource pop end end";
        using PdfDocument document = RedactionSamples.Open(
            "BT /F2 10 Tf 100 700 Td <00010002000300040005> Tj ET",
            "/Font << /F1 5 0 R /F2 6 0 R >>",
            font,
            descendant,
            TestSupport.ContentSamples.Stream(cmap));
        PdfPage page = document.Pages[0];
        List<(string Text, PdfRectangle Box)> before = Glyphs(page);

        // Each glyph is 5 wide: B is 105 to 110, C 110 to 115.
        PdfRedactionResult result = page.Redact([RedactionSamples.Area(106, 690, 114, 712)], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(2, result.GlyphsRemoved);
        Assert.Equal("ADE", page.ExtractText().Text.TrimEnd());
        Assert.Contains("[<0001>-1000 <00040005>] TJ", RedactionSamples.Content(document, page), StringComparison.Ordinal);
        AssertSamePositions([before[0], before[3], before[4]], Glyphs(page));
    }

    [Fact]
    public void A_type3_font_loses_the_covered_glyph_codes()
    {
        const string font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 600 700] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 7 0 R >>"
            + " /Encoding << /Type /Encoding /Differences [97 /a 98 /a 99 /a] >> /FirstChar 97 /LastChar 99 /Widths [600 600 600] >>";
        using PdfDocument document = RedactionSamples.Open(
            "BT /T3 10 Tf 100 700 Td (abc) Tj ET",
            "/Font << /F1 5 0 R /T3 6 0 R >>",
            font,
            TestSupport.ContentSamples.Stream("600 0 0 0 600 700 d1 0 0 600 700 re f"));
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(107, 695, 111, 705)], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(1, result.GlyphsRemoved);
        Assert.Contains("[<61>-600 <63>] TJ", RedactionSamples.Content(document, page), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_without_content_under_the_areas_is_not_rewritten()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET 10 10 50 50 re f");
        PdfPage page = document.Pages[0];
        string before = RedactionSamples.Content(document, page);

        PdfRedactionResult result = page.Redact([RedactionSamples.Area(300, 300, 400, 400)], new PdfRedactionOptions().WithFillColor(null));

        Assert.Equal(0, result.GlyphsRemoved + result.PathsRemoved + result.ImagesChanged);
        Assert.Equal(before, RedactionSamples.Content(document, page));
    }

    [Fact]
    public void An_empty_area_list_changes_nothing()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret) Tj ET");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([]);

        Assert.Equal(0, result.GlyphsRemoved);
        Assert.False(page.Dictionary.IsDirty);
    }

    [Fact]
    public void Replacement_text_of_marked_content_whose_glyphs_were_removed_is_dropped()
    {
        using PdfDocument document = RedactionSamples.Open("/Span <</ActualText (Secret) /MCID 0>> BDC BT /F1 12 Tf 72 700 Td (Secret) Tj ET EMC /Span <</ActualText (Other)>> BDC BT /F1 12 Tf 72 600 Td (Other) Tj ET EMC");
        PdfPage page = document.Pages[0];

        page.Redact([SecretArea], new PdfRedactionOptions().WithFillColor(null));

        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("(Secret)", content, StringComparison.Ordinal);
        Assert.Contains("/Span << /MCID 0 >> BDC", content, StringComparison.Ordinal);
        Assert.Contains("/ActualText (Other)", content, StringComparison.Ordinal);
    }

    private static List<(string Text, PdfRectangle Box)> Glyphs(PdfPage page)
    {
        TextPage text = page.ExtractText();
        return [.. text.Glyphs.Select(glyph => (text.Text.Substring(glyph.TextStart, glyph.TextLength), glyph.Quad.Bounds))];
    }

    private static void AssertSamePositions(IEnumerable<(string Text, PdfRectangle Box)> expected, List<(string Text, PdfRectangle Box)> actual)
    {
        List<(string Text, PdfRectangle Box)> wanted = [.. expected.Where(glyph => glyph.Text != " ")];
        List<(string Text, PdfRectangle Box)> found = [.. actual.Where(glyph => glyph.Text != " ")];
        Assert.Equal(wanted.Count, found.Count);
        for (int index = 0; index < wanted.Count; index++)
        {
            Assert.Equal(wanted[index].Text, found[index].Text);
            Assert.Equal(wanted[index].Box.Left, found[index].Box.Left, 3);
            Assert.Equal(wanted[index].Box.Bottom, found[index].Box.Bottom, 3);
        }
    }
}
