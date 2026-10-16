using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>Positioned glyphs extracted through <see cref="PdfPage.ExtractText()"/>. ISO 32000-2 §9.4.4, §9.10, §14.9.4.</summary>
public sealed class TextExtractionTests
{
    private const int Precision = 6;

    [Fact]
    public void A_line_of_Helvetica_extracts_its_text_and_each_glyph_quad_spans_the_advance_and_the_font_ascent_and_descent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("Hello, Broadside", text.Text);
        Assert.Equal(16, text.Glyphs.Count);

        // BT /F1 24 Tf 72 700 Td (Hello, Broadside) Tj: Helvetica's AFM gives H 722 wide, Ascender 718, Descender -207.
        TextGlyph h = text.Glyphs[0];
        Assert.Equal("H", text.GetGlyphText(h).ToString());
        Assert.Equal(24, h.FontSize);
        Assert.Equal("Helvetica", h.Font.BaseFont);
        AssertPoint(72, 700 - (0.207 * 24), h.Quad.BottomLeft);
        AssertPoint(72 + (0.722 * 24), 700 - (0.207 * 24), h.Quad.BottomRight);
        AssertPoint(72 + (0.722 * 24), 700 + (0.718 * 24), h.Quad.TopRight);
        AssertPoint(72, 700 + (0.718 * 24), h.Quad.TopLeft);
        AssertPoint(72, 700, h.Origin);
        AssertPoint(72 + (0.722 * 24), 700, text.Glyphs[1].Origin);
        Assert.Equal(TextGlyphFlags.None, h.Flags);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Marked_content_extracts_its_ActualText_replacement_instead_of_the_underlying_glyph()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("marked-content.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        // "/P <</MCID 0>> BDC ... (a) Tj ... EMC" then "/Span <</ActualText (x)>> BDC ... (y) Tj ... EMC"; pdftotext prints "a" and "x".
        Assert.Equal("ax", text.Text);
        Assert.Equal(2, text.Glyphs.Count);
        Assert.Equal(TextGlyphFlags.None, text.Glyphs[0].Flags);
        TextGlyph y = text.Glyphs[1];
        Assert.Equal((uint)'y', y.CharacterCode);
        Assert.Equal("x", text.GetGlyphText(y).ToString());
        Assert.Equal(TextGlyphFlags.ActualText, y.Flags);
        AssertPoint(72, 680, y.Origin);
    }

    [Fact]
    public void ActualText_from_a_named_property_list_replaces_every_glyph_of_its_sequence_and_the_first_glyph_carries_it()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/Font << /F1 5 0 R >> /Properties << /P1 6 0 R >>",
            "BT /F1 10 Tf (A) Tj /Span /P1 BDC (ffi) Tj EMC (B) Tj ET",
            ContentPdf.Helvetica,
            "<< /ActualText <FEFF FB03> >>"));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("AﬃB", text.Text);
        Assert.Equal(5, text.Glyphs.Count);
        Assert.Equal(["A", "ﬃ", string.Empty, string.Empty, "B"], text.Glyphs.Select(glyph => text.GetGlyphText(glyph).ToString()));
        Assert.Equal(
            [TextGlyphFlags.None, TextGlyphFlags.ActualText, TextGlyphFlags.ActualText, TextGlyphFlags.ActualText, TextGlyphFlags.None],
            text.Glyphs.Select(glyph => glyph.Flags));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Nested_ActualText_is_covered_by_the_outermost_replacement()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            ContentPdf.HelveticaResources,
            "BT /F1 10 Tf /Span << /ActualText (outer) >> BDC (a) Tj /Span << /ActualText (inner) >> BDC (b) Tj EMC (c) Tj EMC ET",
            ContentPdf.Helvetica));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("outer", text.Text);
        Assert.Equal(3, text.Glyphs.Count);
        Assert.All(text.Glyphs, glyph => Assert.Equal(TextGlyphFlags.ActualText, glyph.Flags));
    }

    [Fact]
    public void Invisible_text_in_rendering_modes_3_and_7_is_extracted_and_flagged()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            ContentPdf.HelveticaResources,
            "BT /F1 10 Tf (a) Tj 3 Tr (b) Tj 7 Tr (c) Tj 0 Tr (d) Tj ET",
            ContentPdf.Helvetica));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("abcd", text.Text);
        Assert.Equal(
            [TextGlyphFlags.None, TextGlyphFlags.Invisible, TextGlyphFlags.Invisible, TextGlyphFlags.None],
            text.Glyphs.Select(glyph => glyph.Flags));
        Assert.Equal(
            [TextRenderingMode.Fill, TextRenderingMode.Invisible, TextRenderingMode.Clip, TextRenderingMode.Fill],
            text.Glyphs.Select(glyph => glyph.RenderingMode));
    }

    [Fact]
    public void Glyphs_of_a_form_xobject_shown_inside_an_artifact_are_artifacts_and_lie_where_the_form_matrix_puts_them()
    {
        string form = ContentPdf.Stream("BT /F1 10 Tf (f) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Matrix [2 0 0 2 10 20] /Resources << /Font << /F1 5 0 R >> >>");
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(
            "/Font << /F1 5 0 R >> /XObject << /Fm 6 0 R >>",
            "1 0 0 1 100 200 cm /Fm Do /Artifact BMC /Fm Do EMC",
            ContentPdf.Helvetica,
            form));

        TextPage text = document.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts());

        Assert.Equal("ff", text.Text);
        Assert.Equal("f", document.Pages[0].ExtractText().Text);
        Assert.Equal([TextGlyphFlags.None, TextGlyphFlags.Artifact], text.Glyphs.Select(glyph => glyph.Flags));

        // Form space (0, 0) -> Matrix [2 0 0 2 10 20] -> cm [1 0 0 1 100 200]: (110, 220); f is 278 wide at 10 pt, doubled.
        AssertPoint(110, 220, text.Glyphs[0].Origin);
        AssertPoint(110 + (2 * 2.78), 220, text.Glyphs[0].Quad.BottomRight with { Y = 220 });
        Assert.Equal(10, text.Glyphs[0].FontSize);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Text_hidden_by_optional_content_is_left_out_unless_requested_and_then_flagged()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWithCatalog(
            "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            "/Font << /F1 5 0 R >> /Properties << /oc1 6 0 R >>",
            "BT /F1 10 Tf (a) Tj /OC /oc1 BDC (b) Tj EMC (c) Tj ET",
            ContentPdf.Helvetica,
            "<< /Type /OCG /Name (Hidden layer) >>"));
        PdfPage page = document.Pages[0];

        TextPage visible = page.ExtractText();
        TextPage all = page.ExtractText(new TextExtractionOptions().WithHiddenContent());

        Assert.Equal("ac", visible.Text);
        Assert.Equal("abc", all.Text);
        Assert.Equal([TextGlyphFlags.None, TextGlyphFlags.Hidden, TextGlyphFlags.None], all.Glyphs.Select(glyph => glyph.Flags));
        AssertPoint(visible.Glyphs[1].Origin.X, visible.Glyphs[1].Origin.Y, all.Glyphs[2].Origin);
    }

    [Fact]
    public void Extractions_one_after_another_each_follow_their_own_options_and_keep_their_own_glyphs()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWithCatalog(
            "/OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>",
            "/Font << /F1 5 0 R >> /Properties << /oc1 6 0 R >>",
            "BT /F1 10 Tf (a) Tj /OC /oc1 BDC (b) Tj EMC (c) Tj ET",
            ContentPdf.Helvetica,
            "<< /Type /OCG /Name (Hidden layer) >>"));
        PdfPage page = document.Pages[0];

        TextPage all = page.ExtractText(new TextExtractionOptions().WithHiddenContent());
        TextPage visible = page.ExtractText();
        TextPage allAgain = page.ExtractText(new TextExtractionOptions().WithHiddenContent());

        Assert.Equal("abc", all.Text);
        Assert.Equal("ac", visible.Text);
        Assert.Equal("abc", allAgain.Text);
        Assert.Equal([TextGlyphFlags.None, TextGlyphFlags.Hidden, TextGlyphFlags.None], all.Glyphs.Select(glyph => glyph.Flags));
        Assert.Equal([TextGlyphFlags.None, TextGlyphFlags.None], visible.Glyphs.Select(glyph => glyph.Flags));
        Assert.Equal(["a", "b", "c"], all.Glyphs.Select(glyph => all.GetGlyphText(glyph).ToString()));
        Assert.Equal("abc", all.PlainText);
    }

    [Fact]
    public void Type3_glyphs_are_extracted_with_their_box_from_the_font_bbox_through_the_font_matrix()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        Assert.Equal("■▲●■▲●", text.Text);
        Assert.All(text.Glyphs, glyph => Assert.IsType<Broadside.Fonts.PdfType3Font>(glyph.Font));

        // T3a: FontMatrix 0.001, FontBBox [0 0 750 750], Widths 1000: the box is 1 x 0.75 text space units, scaled by the font size.
        TextGlyph square = text.Glyphs[0];
        PdfRectangle box = square.Quad.Bounds;
        Assert.Equal(square.FontSize, box.Width, Precision);
        Assert.Equal(0.75 * square.FontSize, box.Height, Precision);
        Assert.Equal(square.Origin.Y, box.Bottom, Precision);
    }

    private static void AssertPoint(double x, double y, PathPoint actual)
    {
        Assert.Equal(x, actual.X, Precision);
        Assert.Equal(y, actual.Y, Precision);
    }
}
