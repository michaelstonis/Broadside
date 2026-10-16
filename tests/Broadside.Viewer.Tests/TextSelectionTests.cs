using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

public sealed class TextSelectionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dragging_across_a_line_of_an_embedded_truetype_font_copies_it()
    {
        await using DocumentSession session = Open("text-truetype-embedded.pdf");
        var selection = new TextSelection(session);
        SelectableText text = await selection.LoadPageAsync(0, Token);
        Quad first = Glyph(text, 0), last = Glyph(text, text.Length - 1);

        Assert.True(selection.Begin(Before(0, first)));
        Assert.True(selection.IsEmpty);
        Assert.True(selection.Extend(After(0, last)));

        Assert.Equal("HI", await selection.GetTextAsync(Token));
        Assert.Equal(1, selection.GetQuads(0, []));
    }

    [Fact]
    public async Task Dragging_across_two_lines_selects_and_copies_both()
    {
        // Page 1 of text-render-modes.pdf: four lines of HI in the embedded TrueType font of text-truetype-embedded.pdf.
        await using DocumentSession session = Open("text-render-modes.pdf");
        var selection = new TextSelection(session);
        SelectableText text = await selection.LoadPageAsync(0, Token);

        selection.Begin(Before(0, Glyph(text, 0)));
        selection.Extend(After(0, Glyph(text, 3)));

        Assert.Equal("HI\nHI", await selection.GetTextAsync(Token));
        var quads = new Quad[4];
        Assert.Equal(2, selection.GetQuads(0, quads));
        Assert.True(quads[0].Bounds.Bottom > quads[1].Bounds.Top - 1, "the first line's quad is above the second's");

        // Dragging upwards selects backwards from the anchor, the same text; a point left of and above the first line is its start.
        selection.Begin(After(0, Glyph(text, 3)));
        selection.Extend(new PagePoint(0, Glyph(text, 0).BottomLeft.X - 20, Glyph(text, 0).TopLeft.Y + 2));
        Assert.True(selection.Focus < selection.Anchor);
        Assert.Equal("HI\nHI", await selection.GetTextAsync(Token));
    }

    [Fact]
    public async Task Selection_on_rotated_text_produces_rotated_quads()
    {
        await using DocumentSession session = Open("text-rotated.pdf");
        var selection = new TextSelection(session);
        await selection.LoadPageAsync(0, Token);

        selection.SelectAll();
        var quads = new Quad[4];
        int count = selection.GetQuads(0, quads);

        Assert.Equal(2, count);
        double[] angles = [.. quads[..count].Select(q => Math.Atan2(q.BottomRight.Y - q.BottomLeft.Y, q.BottomRight.X - q.BottomLeft.X) * 180 / Math.PI).Order()];
        Assert.Equal(30, angles[0], 0.01);
        Assert.Equal(90, angles[1], 0.01);
        Assert.Equal("HI\nHI", await selection.GetTextAsync(Token));
    }

    [Fact]
    public async Task A_replaceable_reading_order_turns_a_two_column_drag_into_column_order()
    {
        // The content draws each line across both columns in turn.
        await using DocumentSession session = Open("layout-two-column-untagged.pdf");
        var content = new TextSelection(session, ContentReadingOrder.Instance);
        var columns = new TextSelection(session, new ColumnOrder());
        var layout = new TextSelection(session);

        string[] copied = new string[3];
        int i = 0;
        foreach (TextSelection selection in new[] { content, columns, layout })
        {
            SelectableText text = await selection.LoadPageAsync(0, Token);
            Quad c = Glyph(text, text.GetPosition(0));
            selection.Begin(Before(0, c), SelectionGranularity.Line);
            Quad below = Glyph(text, text.GetNearestGlyph(new PathPoint(c.BottomLeft.X + 30, c.BottomLeft.Y - 10)));
            selection.Extend(After(0, below));
            copied[i++] = await selection.GetTextAsync(Token);
        }

        const string FirstLines = "Columns are found from the white\nspace that runs down the page";
        Assert.StartsWith("Columns are found from the white The second column", copied[0], StringComparison.Ordinal);
        Assert.Equal(FirstLines, copied[1]);
        Assert.Equal(FirstLines, copied[2]);
    }

    [Fact]
    public async Task Double_and_triple_click_select_a_word_and_a_line_and_keys_extend_them()
    {
        await using DocumentSession session = Open("layout-two-column-untagged.pdf");
        var selection = new TextSelection(session);
        SelectableText text = await selection.LoadPageAsync(0, Token);
        int found = FindWord(text, "found");

        selection.Begin(Centre(0, Glyph(text, found + 1)), SelectionGranularity.Word);
        Assert.Equal("found", await selection.GetTextAsync(Token));

        selection.Move(SelectionMove.CharacterForward);
        selection.Move(SelectionMove.WordForward);
        Assert.Equal("found from", await selection.GetTextAsync(Token));

        selection.Move(SelectionMove.LineEnd);
        Assert.Equal("found from the white", await selection.GetTextAsync(Token));

        selection.Move(SelectionMove.LineDown);
        Assert.StartsWith("found from the white\nspace", await selection.GetTextAsync(Token), StringComparison.Ordinal);

        selection.Move(SelectionMove.WordBackward, extend: false);
        Assert.True(selection.IsEmpty);

        selection.Begin(Centre(0, Glyph(text, found)), SelectionGranularity.Line);
        Assert.Equal("Columns are found from the white", await selection.GetTextAsync(Token));
    }

    [Fact]
    public async Task Copied_text_is_logical_and_uses_actual_text_in_structure_order()
    {
        // Painted out of logical order; a Span with ActualText (Drucker) covers "Druk-" and "ker" on two lines.
        await using DocumentSession session = Open("text-tagged-replacements.pdf");
        var selection = new TextSelection(session);
        SelectableText text = await selection.LoadPageAsync(0, Token);

        selection.SelectAll();
        string all = await selection.GetTextAsync(Token);
        Assert.StartsWith("Title\nDrucker\n", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Druk-", all, StringComparison.Ordinal);

        // Selecting only "ker" still copies the replacement of its sequence.
        int ker = FindWord(text, "Drucker") + 5;
        selection.Begin(Before(0, Glyph(text, ker)));
        selection.Extend(After(0, Glyph(text, ker + 2)));
        Assert.Equal("Drucker", await selection.GetTextAsync(Token));
    }

    [Fact]
    public async Task A_selection_runs_across_pages()
    {
        await using DocumentSession session = Open("text-render-modes.pdf");
        var selection = new TextSelection(session);
        SelectableText first = await selection.LoadPageAsync(0, Token);
        SelectableText second = await selection.LoadPageAsync(1, Token);

        selection.Begin(Before(0, Glyph(first, first.Length - 1)));
        selection.Extend(After(1, Glyph(second, 0)));

        Assert.Equal("I\nH", await selection.GetTextAsync(Token));
        Assert.Equal(1, selection.GetQuads(0, []));
        Assert.Equal(1, selection.GetQuads(1, []));
        Assert.Equal(0, selection.GetQuads(2, []));

        selection.Move(SelectionMove.CharacterBackward);
        selection.Move(SelectionMove.CharacterBackward);
        Assert.Equal("I", await selection.GetTextAsync(Token));
    }

    [Fact]
    public async Task Hovering_a_link_finds_it_and_a_page_not_loaded_ignores_gestures()
    {
        await using DocumentSession session = Open("annotations-link.pdf");
        var selection = new TextSelection(session);
        Assert.False(selection.Begin(new PagePoint(0, 10, 10)));
        Assert.Null(selection.FindLink(new PagePoint(0, 10, 10)));

        SelectableText text = await selection.LoadPageAsync(0, Token);
        HitTestLink link = Assert.Single(text.Index.Links);
        PdfRectangle box = link.Region[0].Bounds;
        Assert.Same(link, selection.FindLink(new PagePoint(0, (box.Left + box.Right) / 2, (box.Bottom + box.Top) / 2)));
        Assert.Null(selection.FindLink(new PagePoint(0, -100, -100)));
    }

    [Fact]
    public async Task A_provider_listing_a_glyph_twice_is_refused()
    {
        await using DocumentSession session = Open("text-truetype-embedded.pdf");
        DisplayList list = await session.GetDisplayListAsync(0, cancellationToken: Token);
        PdfPage page = session.Document.Pages[0];
        Assert.Throws<InvalidOperationException>(() => SelectableText.Create(page, page.CreateHitTestIndex(list), new Twice()));
    }

    internal static DocumentSession Open(string fileName) =>
        DocumentSession.Open(Corpus.Bytes(fileName), ViewerTestSupport.OptionsFor(fileName, ViewerThreading.Background));

    internal static Quad Glyph(SelectableText text, int position) => text.Index.GetGlyph(text.GetGlyphIndex(position)).Quad;

    private static PagePoint Before(int page, Quad glyph) => new(page, glyph.BottomLeft.X - 1, Mid(glyph).Y);

    private static PagePoint After(int page, Quad glyph) => new(page, glyph.BottomRight.X - 0.1, Mid(glyph).Y);

    private static PagePoint Centre(int page, Quad glyph) => new(page, Mid(glyph).X, Mid(glyph).Y);

    private static PathPoint Mid(Quad glyph) => new(
        (glyph.BottomLeft.X + glyph.TopRight.X) / 2,
        (glyph.BottomLeft.Y + glyph.TopRight.Y) / 2);

    /// <summary>The position of the first glyph of a word, matching the glyph texts run together.</summary>
    private static int FindWord(SelectableText text, string word)
    {
        var joined = new System.Text.StringBuilder();
        int[] starts = new int[text.Length];
        for (int position = 0; position < text.Length; position++)
        {
            starts[position] = joined.Length;
            joined.Append(text.GetGlyphText(position));
        }

        int at = joined.ToString().IndexOf(word, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{word} is on the page");
        return Array.IndexOf(starts, at);
    }

    /// <summary>Reads the left column, then the right, each top to bottom and left to right.</summary>
    private sealed class ColumnOrder : IReadingOrderProvider
    {
        public int[] GetReadingOrder(PdfPage page, HitTestIndex index)
        {
            double middle = (page.MediaBox.Left + page.MediaBox.Right) / 2;
            return [.. Enumerable.Range(0, index.GlyphCount)
                .Select(i => (Index: i, Quad: index.GetGlyph(i).Quad))
                .OrderBy(g => g.Quad.BottomLeft.X < middle ? 0 : 1)
                .ThenByDescending(g => Math.Round(g.Quad.BottomLeft.Y))
                .ThenBy(g => g.Quad.BottomLeft.X)
                .Select(g => g.Index)];
        }
    }

    private sealed class Twice : IReadingOrderProvider
    {
        public int[] GetReadingOrder(PdfPage page, HitTestIndex index) => [0, 0];
    }
}
