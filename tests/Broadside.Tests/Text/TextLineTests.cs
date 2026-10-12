using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Glyphs assembled into words and lines, and the page's plain text, through <see cref="PdfPage.ExtractText()"/>. ISO 32000-2 §9.4.4
/// (glyph positions), §9.7.4.3 (vertical writing), §14.8.2.6.2 (word breaks) and §14.9.4 (replacement text).
/// </summary>
public sealed class TextLineTests
{
    private const int Precision = 3;

    [Fact]
    public void Upright_ninety_degree_and_thirty_degree_lines_each_run_along_their_own_baseline()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-lines-rotated.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        // pdftotext -raw prints the same six lines.
        Assert.Equal("Zero degrees\nsecond line\nNinety degrees\nsecond line\nThirty degrees\nsecond line", text.PlainText);
        Assert.Equal(6, text.Lines.Count);
        Assert.All(text.Lines, line => Assert.Equal(2, line.WordCount));
        Assert.Equal(
            ["Zero degrees", "second line", "Ninety degrees", "second line", "Thirty degrees", "second line"],
            text.Lines.Select(line => text.GetLineText(line).ToString()));
        Assert.Equal(
            ["Zero", "degrees", "second", "line", "Ninety", "degrees", "second", "line", "Thirty", "degrees", "second", "line"],
            text.Words.Select(word => text.GetWordText(word).ToString()));

        // The baseline runs along the text matrix's x axis: (1, 0), (0, 1) and (cos 30, sin 30).
        AssertDirection(1, 0, text.Lines[0].Quad);
        AssertDirection(0, 1, text.Lines[2].Quad);
        AssertDirection(0.866025, 0.5, text.Lines[4].Quad);

        // "Zero degrees" in Helvetica 12 (AFM widths Z 611, e 556, r 333, o 556, space 278, d 556, g 556, s 500): 5947 / 1000 x 12
        // wide, from the descender -207 to the ascender 718; "Zero" alone is 2056 / 1000 x 12 wide.
        Quad zero = text.Lines[0].Quad;
        AssertPoint(72, 700 - 2.484, zero.BottomLeft);
        AssertPoint(72 + 71.364, 700 - 2.484, zero.BottomRight);
        AssertPoint(72 + 71.364, 700 + 8.616, zero.TopRight);
        AssertPoint(72 + 24.672, 700 - 2.484, text.Words[0].Quad.BottomRight);

        // T* moves down 14 in text space: to the right on the page for the line turned 90 degrees.
        AssertPoint(400 + 14 + 2.484, 500, text.Lines[3].Quad.BottomLeft);
        Assert.All(text.Lines, line => Assert.False(line.IsVertical));
    }

    [Fact]
    public void Vertical_text_extracts_as_one_column_read_from_top_to_bottom()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-identity-v.pdf"));

        TextPage text = document.Pages[0].ExtractText();

        // Identity-V shows H, I and a space down one column (pdftotext prints "HI"); the space ends the word and starts no other.
        Assert.Equal("HI", text.PlainText);
        TextLine line = Assert.Single(text.Lines);
        Assert.True(line.IsVertical);
        TextWord word = Assert.Single(text.Words);
        Assert.Equal((0, 2), (word.GlyphStart, word.GlyphCount));
        Assert.Equal((0, 2, 0, 1), (line.GlyphStart, line.GlyphCount, line.WordStart, line.WordCount));

        // The column holds both glyph boxes: H at x 290.4-314.4, y 674.08-698.08 and I at x 295.2-319.2, y 645.28-669.28.
        AssertPoint(290.4, 645.28, line.Quad.BottomLeft);
        AssertPoint(319.2, 645.28, line.Quad.BottomRight);
        AssertPoint(319.2, 698.08, line.Quad.TopRight);
        AssertPoint(290.4, 698.08, line.Quad.TopLeft);
    }

    [Fact]
    public void Kerning_in_a_TJ_array_keeps_a_word_together_and_a_gap_wider_than_a_tenth_of_an_em_breaks_it()
    {
        // 120 and 80 pull the next glyph back (kerning); -50 pushes it 0.05 em on (tracking); -250 opens a quarter-em gap.
        TextPage text = Extract("BT /F1 10 Tf 72 700 Td [(A) 120 (V) -50 (A) -250 (B) 80 (C)] TJ ET");

        Assert.Equal("AVA BC", text.PlainText);
        Assert.Equal(["AVA", "BC"], text.Words.Select(word => text.GetWordText(word).ToString()));
        Assert.Single(text.Lines);
    }

    [Fact]
    public void A_gap_made_by_a_text_positioning_operator_breaks_the_word_but_character_and_word_spacing_do_not()
    {
        // Tc 3 and Tw 5 widen every advance and are part of it: "one two" is 35.02 + 7 x 3 + 5 = 61.02 wide, so the Td of 80 from its
        // start leaves a gap of 18.98 before "three".
        TextPage text = Extract("BT /F1 10 Tf 3 Tc 5 Tw 72 700 Td (one two) Tj 80 0 Td (three) Tj ET");

        Assert.Equal("one two three", text.PlainText);
        Assert.Equal(["one", "two", "three"], text.Words.Select(word => text.GetWordText(word).ToString()));
    }

    [Fact]
    public void A_space_glyph_separates_words_once_and_white_space_at_a_line_end_adds_nothing()
    {
        TextPage text = Extract("BT /F1 10 Tf 12 TL 72 700 Td (Hello  World ) Tj T* ( again) Tj ET");

        Assert.Equal("Hello World\nagain", text.PlainText);
        Assert.Equal(["Hello", "World", "again"], text.Words.Select(word => text.GetWordText(word).ToString()));
        Assert.Equal(["Hello World", "again"], text.Lines.Select(line => text.GetLineText(line).ToString()));

        // The line runs from H to d, the spaces between the words included, the trailing ones not.
        Assert.Equal((0, 12), (text.Lines[0].GlyphStart, text.Lines[0].GlyphCount));
        Assert.Equal((14, 5), (text.Lines[1].GlyphStart, text.Lines[1].GlyphCount));
    }

    [Fact]
    public void Superscripts_and_subscripts_stay_on_their_line()
    {
        // A rise of 4 at size 7 after size 10 text, then a rise of -2: each less than half an em off the line's baseline, though 6 apart.
        TextPage text = Extract("BT /F1 10 Tf 72 700 Td (E=mc) Tj /F1 7 Tf 4 Ts (2) Tj -2 Ts (x) Tj 0 Ts /F1 10 Tf ( end) Tj ET");

        Assert.Equal("E=mc2x end", text.PlainText);
        Assert.Single(text.Lines);
    }

    [Fact]
    public void Consecutive_ActualText_sequences_have_no_word_break_between_them_even_across_a_gap()
    {
        const string sequences = "/Span << /ActualText (a) >> BDC (x) Tj EMC 20 0 Td /Span << /ActualText (b) >> BDC (y) Tj EMC";

        TextPage replaced = Extract($"BT /F1 10 Tf 72 700 Td {sequences} ET");
        TextPage plain = Extract("BT /F1 10 Tf 72 700 Td (x) Tj 20 0 Td (y) Tj ET");

        // ISO 32000-2 §14.9.4: "they shall be treated as if no word break is present between them". Without ActualText, the 1.5 em gap
        // breaks the word.
        Assert.Equal("ab", replaced.PlainText);
        Assert.Equal((0, 2), (replaced.Words[0].GlyphStart, replaced.Words[0].GlyphCount));
        Assert.Equal("x y", plain.PlainText);
    }

    [Fact]
    public void An_ActualText_replacement_covering_several_glyphs_is_one_word_whose_box_spans_them_all()
    {
        TextPage text = Extract("BT /F1 10 Tf 72 700 Td (a ) Tj /Span << /ActualText <FEFF FB03> >> BDC [(f) -400 (f) -400 (i)] TJ EMC ( b) Tj ET");

        Assert.Equal("a ﬃ b", text.PlainText);
        TextWord ligature = text.Words[1];
        Assert.Equal((2, 3), (ligature.GlyphStart, ligature.GlyphCount));
        Assert.Equal("ﬃ", text.GetWordText(ligature).ToString());

        // f 278, f 278, i 222 at size 10 with two gaps of 4: from 72 + 8.34 (a 556 and the space 278) to 2.78 + 4 + 2.78 + 4 + 2.22 further.
        Assert.Equal(72 + 8.34, ligature.Quad.BottomLeft.X, Precision);
        Assert.Equal(72 + 8.34 + 15.78, ligature.Quad.BottomRight.X, Precision);
    }

    [Fact]
    public void A_reused_extractor_assembles_the_same_words_lines_and_plain_text_as_the_page_and_reassembles_after_another_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-lines-rotated.pdf"));
        using PdfDocument other = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        TextPage page = document.Pages[0].ExtractText();
        var extractor = new TextExtractor();

        document.Pages[0].ProcessContent(extractor);

        Assert.Equal(page.PlainText, extractor.PlainText.ToString());
        Assert.Equal(page.Words, extractor.Words.ToArray());
        Assert.Equal(page.Lines, extractor.Lines.ToArray());
        Assert.Equal("second line", extractor.GetLineText(extractor.Lines[5]).ToString());
        Assert.Equal("Thirty", extractor.GetWordText(extractor.Words[8]).ToString());

        extractor.Clear();
        other.Pages[0].ProcessContent(extractor);

        Assert.Equal("Hello, Broadside", extractor.PlainText.ToString());
        Assert.Equal(2, extractor.Words.Length);
    }

    private static TextPage Extract(string content)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, content, ContentPdf.Helvetica));
        TextPage text = document.Pages[0].ExtractText();
        Assert.Empty(document.Diagnostics);
        return text;
    }

    private static void AssertDirection(double x, double y, Quad quad)
    {
        double dx = quad.BottomRight.X - quad.BottomLeft.X;
        double dy = quad.BottomRight.Y - quad.BottomLeft.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        Assert.Equal(x, dx / length, Precision);
        Assert.Equal(y, dy / length, Precision);
    }

    private static void AssertPoint(double x, double y, PathPoint point)
    {
        Assert.Equal(x, point.X, Precision);
        Assert.Equal(y, point.Y, Precision);
    }
}
