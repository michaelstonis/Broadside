using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// Bulleted and numbered lists, nested, flowed, saved and read back: each marker sits right-aligned in its level's indent on the
/// baseline of its item's first line, numbered by the list's numbering (ISO 32000-2 §14.8.4.8.2, §14.8.5.5 <c>ListNumbering</c>).
/// </summary>
public sealed class ListFlowTests
{
    private const double Margin = 72;

    [Fact]
    public void Nested_lists_of_three_levels_place_markers_at_their_indents_with_correct_numbering()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 11);
        var markers = new TextStyle(font, 11);
        var level1 = new ListStyle(markers).WithNumbering(ListNumbering.Decimal).WithIndent(20).WithMarkerGap(5);
        var level2 = new ListStyle(markers).WithNumbering(ListNumbering.LowerAlpha).WithSuffix(")").WithIndent(20).WithMarkerGap(5);
        var level3 = new ListStyle(markers).WithNumbering(ListNumbering.UpperRoman).WithIndent(20).WithMarkerGap(5);
        var inner = new ListBlock(level3).Add(new ListItem("Third level one", text)).Add(new ListItem("Third level two", text)).Add(new ListItem("Third level three", text)).Add(new ListItem("Third level four", text));
        var middle = new ListBlock(level2).Add(new ListItem(new Paragraph("Second level one", text), inner)).Add(new ListItem("Second level two", text));
        var outer = new ListBlock(level1).Add(new ListItem("First level one", text)).Add(new ListItem(new Paragraph("First level two", text), middle)).Add(new ListItem("First level three", text));

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(outer).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        List<(string Text, double Left, double Right, double Y)> words = Words(FlowContentRecorder.Record(reopened.Pages[0]));
        (string Marker, int Level, string FirstWord)[] expected =
        [
            ("1.", 1, "First"), ("2.", 1, "First"), ("a)", 2, "Second"), ("I.", 3, "Third"), ("II.", 3, "Third"), ("III.", 3, "Third"),
            ("IV.", 3, "Third"), ("b)", 2, "Second"), ("3.", 1, "First"),
        ];
        Assert.Equal(expected.Length * 4, words.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            (string marker, int level, string first) = expected[index];
            (string Text, double Left, double Right, double Y) label = words[index * 4];
            (string Text, double Left, double Right, double Y) body = words[(index * 4) + 1];
            Assert.Equal(marker, label.Text);
            Assert.Equal(first, body.Text);
            Assert.Equal(Margin + (20 * level) - 5, label.Right, 3);
            Assert.Equal(Margin + (20 * level), body.Left, 6);
            Assert.Equal(body.Y, label.Y, 6);
        }

        // Each item is one line below the last.
        for (int index = 1; index < expected.Length; index++)
        {
            Assert.Equal(11 * 1.2, words[(index - 1) * 4].Y - words[index * 4].Y, 6);
        }
    }

    [Fact]
    public void Bullets_custom_markers_and_lists_breaking_across_pages()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.TimesRoman);
        var text = new ParagraphStyle(font, 12);
        var list = new ListBlock(new ListStyle(new TextStyle(font, 12).WithColor(PdfColor.FromRgb(1, 0, 0))));
        for (int index = 0; index < 60; index++)
        {
            list.Add(index == 1 ? new ListItem($"Item {index}", text) { Marker = "–" } : new ListItem($"Item {index}", text));
        }

        int pages = new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(list).Render(document).Pages.Count;

        Assert.Equal(2, pages);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        List<RecordedGlyph> glyphs = [.. reopened.Pages.SelectMany(page => FlowContentRecorder.Record(page).Glyphs)];
        List<RecordedGlyph> bullets = [.. glyphs.Where(glyph => glyph.Color == PdfColor.FromRgb(1, 0, 0))];
        Assert.Equal(60, bullets.Count);
        Assert.Equal("–", bullets[1].Text);
        Assert.All(bullets.Where((_, index) => index != 1), bullet => Assert.Equal("•", bullet.Text));
        Assert.All(bullets, bullet => Assert.Equal(Margin + 18 - 6, bullet.X + bullet.Advance, 3));
        Assert.All(glyphs.Where(glyph => glyph.Color != PdfColor.FromRgb(1, 0, 0)), glyph => Assert.Equal(PdfColor.FromGray(0), glyph.Color));
    }

    [Fact]
    public void An_item_without_lines_still_shows_its_marker_and_numbers_continue_from_StartAt()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var list = new ListBlock(new ListStyle(new TextStyle(font, 10)).WithNumbering(ListNumbering.LowerRoman).WithStartAt(3).WithPrefix("("))
            .Add(new ListItem())
            .Add(new ListItem("four", text));

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(list).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        List<(string Text, double Left, double Right, double Y)> words = Words(FlowContentRecorder.Record(reopened.Pages[0]));
        Assert.Equal(["(iii.", "(iv.", "four"], words.Select(word => word.Text));
        Assert.Equal(10 * 1.2, words[0].Y - words[1].Y, 6);
    }

    [Fact]
    public void A_marker_the_font_cannot_encode_is_refused_before_anything_is_added()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var list = new ListBlock(new ListStyle(new TextStyle(font, 10)).WithBullet("★")).Add(new ListItem("star", new ParagraphStyle(font, 10)));
        var flow = new FlowDocument(PageSetup.Letter).Add(new Paragraph("first", new ParagraphStyle(font, 10))).Add(list);

        ArgumentException error = Assert.Throws<ArgumentException>(() => flow.Render(document));

        Assert.Contains("block 1", error.Message, StringComparison.Ordinal);
        Assert.Empty(document.Pages);
    }

    [Theory]
    [InlineData(ListNumbering.Decimal, 1, "1.")]
    [InlineData(ListNumbering.Decimal, 42, "42.")]
    [InlineData(ListNumbering.LowerAlpha, 1, "a.")]
    [InlineData(ListNumbering.LowerAlpha, 26, "z.")]
    [InlineData(ListNumbering.UpperAlpha, 27, "AA.")]
    [InlineData(ListNumbering.UpperAlpha, 703, "AAA.")]
    [InlineData(ListNumbering.LowerRoman, 4, "iv.")]
    [InlineData(ListNumbering.UpperRoman, 1994, "MCMXCIV.")]
    [InlineData(ListNumbering.UpperRoman, 4000, "4000.")]
    [InlineData(ListNumbering.Bullet, 7, "•")]
    public void Markers_are_formatted_by_the_numbering(ListNumbering numbering, int number, string expected)
    {
        using PdfDocument document = NewDocument();
        var style = new ListStyle(new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10)).WithNumbering(numbering);

        Assert.Equal(expected, style.FormatMarker(number));
    }

    /// <summary>The words of a page in painting order: runs of non-space glyphs on one baseline that touch.</summary>
    private static List<(string Text, double Left, double Right, double Y)> Words(FlowContentRecorder page)
    {
        List<(string Text, double Left, double Right, double Y)> words = [];
        RecordedGlyph? previous = null;
        foreach (RecordedGlyph glyph in page.Glyphs)
        {
            if (glyph.Text == " ")
            {
                previous = null;
                continue;
            }

            if (previous is { } last && Math.Abs(last.Y - glyph.Y) < 1e-6 && Math.Abs(last.X + last.Advance - glyph.X) < 1e-3)
            {
                (string text, double left, _, double y) = words[^1];
                words[^1] = (text + glyph.Text, left, glyph.X + glyph.Advance, y);
            }
            else
            {
                words.Add((glyph.Text, glyph.X, glyph.X + glyph.Advance, glyph.Y));
            }

            previous = glyph;
        }

        return words;
    }

    private static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
