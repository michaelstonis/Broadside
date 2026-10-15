using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// Tables flowed, saved and read back: column sizing, padding, borders and backgrounds, row and column spans, header rows repeated
/// on each page, and rows split or kept together across page breaks (ISO 32000-2 §14.8.4.8.3, §14.8.5.4.2, §14.8.5.7).
/// </summary>
public sealed class TableFlowTests
{
    private const double Margin = 72;
    private const double Padding = 4;

    [Fact]
    public void A_200_row_table_spans_pages_with_the_header_row_repeated_on_each()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var table = new Table(ColumnWidth.Fixed(60), ColumnWidth.Proportional(), ColumnWidth.Percent(20)) { HeaderRows = 1 };
        table.AddRow(new TableCell("Number", text), new TableCell("Description", text), new TableCell("Amount", text));
        for (int row = 1; row <= 200; row++)
        {
            table.AddRow(new TableCell($"R{row}", text), new TableCell($"Item number {row}", text), new TableCell($"{row * 3}", text));
        }

        FlowLayoutResult result = new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(table).Render(document);

        Assert.True(result.Pages.Count > 3);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        double rowHeight = (10 * 1.2) + (2 * Padding);
        var seen = new List<int>();
        for (int page = 0; page < reopened.Pages.Count; page++)
        {
            List<Word> words = Words(FlowContentRecorder.Record(reopened.Pages[page]));
            Assert.Equal(["Number", "Description", "Amount"], words.Take(3).Select(word => word.Text));
            double headerBaseline = words[0].Y;
            Assert.True(headerBaseline < 792 - Margin && headerBaseline > 792 - Margin - rowHeight);
            foreach (Word word in words.Where(word => word.Text.StartsWith('R')))
            {
                seen.Add(int.Parse(word.Text[1..], System.Globalization.CultureInfo.InvariantCulture));
                Assert.True(word.Y > Margin);
            }
        }

        Assert.Equal(Enumerable.Range(1, 200), seen);
    }

    [Fact]
    public void A_cell_spanning_two_rows_and_two_columns_draws_one_border_around_the_merged_area()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var table = new Table(ColumnWidth.Fixed(100), ColumnWidth.Fixed(100), ColumnWidth.Fixed(100));
        table.AddRow(new TableCell("Merged", text) { RowSpan = 2, ColumnSpan = 2 }, new TableCell("B", text));
        table.AddRow(new TableCell("C", text));
        table.AddRow(new TableCell("D", text), new TableCell("E", text), new TableCell("F", text));

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(table).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder page = FlowContentRecorder.Record(reopened.Pages[0]);
        double row = (10 * 1.2) + (2 * Padding);
        double top = 792 - Margin;
        Assert.Equal(6, page.Strokes.Count);
        PdfRectangle merged = Assert.Single(page.Strokes, stroke => Near(stroke.Bounds.Width, 200)).Bounds;
        AssertRectangle(merged, Margin, top - (2 * row), 200, 2 * row);

        // Nothing is stroked inside the merged area: every other border lies outside it or on its edge.
        foreach ((PdfRectangle bounds, double width) in page.Strokes)
        {
            Assert.Equal(0.5, width, 6);
            if (bounds != merged)
            {
                Assert.True(bounds.Left >= merged.Right - 1e-6 || bounds.Top <= merged.Bottom + 1e-6);
            }
        }

        Word mergedText = Assert.Single(Words(page), word => word.Text == "Merged");
        Assert.Equal(Margin + Padding, mergedText.Left, 6);
    }

    [Fact]
    public void Content_sized_columns_fit_their_widest_unbreakable_content()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 12);
        const string longText = "Incomprehensibilities";
        string sentence = longText + string.Concat(Enumerable.Repeat(" and more words to wrap", 8));
        string filler = string.Join(' ', Enumerable.Repeat("filler words that wrap", 10));
        var squeezed = new Table(ColumnWidth.Auto, ColumnWidth.Auto) { SpaceAfter = 20 };
        squeezed.AddRow(new TableCell(sentence, text), new TableCell("tiny cell", text));
        var roomy = new Table(ColumnWidth.Auto, ColumnWidth.Auto, ColumnWidth.Proportional());
        roomy.AddRow(new TableCell("tiny cell", text), new TableCell("x", text), new TableCell(filler, text));

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(squeezed).Add(roomy).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder page = FlowContentRecorder.Record(reopened.Pages[0]);
        double available = 612 - (2 * Margin);
        PdfRectangle[] first = [.. page.Strokes.Select(stroke => stroke.Bounds).Where(bounds => Near(bounds.Top, 792 - Margin)).OrderBy(bounds => bounds.Left)];
        PdfRectangle[] second = [.. page.Strokes.Select(stroke => stroke.Bounds).Where(bounds => bounds.Top < first[0].Bottom).OrderBy(bounds => bounds.Left)];

        // Too wide to fit unwrapped: the content-sized columns share the width, each at least its widest word.
        Assert.Equal(2, first.Length);
        Assert.Equal(available, first[0].Width + first[1].Width, 6);
        Assert.True(first[0].Width >= font.MeasureWidth(longText, 12) + (2 * Padding) - 1e-6);
        Assert.True(first[1].Width >= font.MeasureWidth("tiny", 12) + (2 * Padding) - 1e-6);
        List<Word> words = Words(page);
        Word longWord = Assert.Single(words, word => word.Text == longText);
        Assert.Equal(Margin + Padding, longWord.Left, 6);

        // Room to spare: content-sized columns take their unwrapped width and the proportional column the rest.
        Assert.Equal(3, second.Length);
        Assert.Equal(font.MeasureWidth("tiny cell", 12) + (2 * Padding), second[0].Width, 6);
        Assert.Equal(font.MeasureWidth("x", 12) + (2 * Padding), second[1].Width, 6);
        Assert.Equal(available, second[0].Width + second[1].Width + second[2].Width, 6);
        Word[] tiny = [.. words.Where(word => word.Text is "tiny" or "cell")];
        Assert.Equal(4, tiny.Length);
        Assert.Equal(tiny[2].Y, tiny[3].Y, 6);
    }

    [Fact]
    public void Columns_take_fixed_percentage_and_proportional_widths_with_padding_and_backgrounds()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        PdfColor header = PdfColor.FromRgb(0.8f, 0.8f, 1);
        PdfColor highlight = PdfColor.FromRgb(1, 1, 0);
        var style = TableStyle.Default.WithPadding(new CellPadding(2, 3, 4, 5)).WithBorder(new TableBorder(1)).WithHeaderBackground(header);
        var table = new Table(ColumnWidth.Fixed(100), ColumnWidth.Percent(25), ColumnWidth.Proportional(1), ColumnWidth.Proportional(3)) { HeaderRows = 1, Style = style };
        table.AddRow(new TableCell("A", text), new TableCell("B", text), new TableCell("C", text), new TableCell("D", text));
        table.AddRow(new TableCell("1", text) { Background = highlight }, new TableCell("2", text), new TableCell("3", text), new TableCell("4", text));

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin))).Add(table).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder page = FlowContentRecorder.Record(reopened.Pages[0]);
        double available = 612 - (2 * Margin);
        double rest = available - 100 - (available / 4);
        double[] widths = [100, available / 4, rest / 4, rest * 3 / 4];
        double row = (10 * 1.2) + 6;
        PdfRectangle[] headerCells = [.. page.Strokes.Select(stroke => stroke.Bounds).Where(bounds => Near(bounds.Top, 792 - Margin)).OrderBy(bounds => bounds.Left)];
        Assert.Equal(4, headerCells.Length);
        double x = Margin;
        for (int column = 0; column < 4; column++)
        {
            AssertRectangle(headerCells[column], x, 792 - Margin - row, widths[column], row);
            x += widths[column];
        }

        Assert.Equal(4, page.Fills.Count(fill => fill.Color.Equals(header)));
        (PdfRectangle bounds, _) = Assert.Single(page.Fills, fill => fill.Color.Equals(highlight));
        AssertRectangle(bounds, Margin, 792 - Margin - (2 * row), 100, row);
        Word a = Assert.Single(Words(page), word => word.Text == "A");
        Assert.Equal(Margin + 5, a.Left, 6);
    }

    [Fact]
    public void A_row_taller_than_the_room_left_splits_across_pages_unless_kept_together()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        string tall = string.Join('\n', Enumerable.Range(1, 30).Select(line => $"L{line}"));
        var flow = new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin)));
        flow.Add(new Paragraph(string.Join('\n', Enumerable.Range(1, 40).Select(line => $"Intro {line}")), text));
        var split = new Table(ColumnWidth.Proportional(), ColumnWidth.Proportional()) { HeaderRows = 1 };
        split.AddRow(new TableCell("Head", text), new TableCell("Other", text));
        split.AddRow(new TableCell(tall, text), new TableCell("short", text));
        var kept = new Table(ColumnWidth.Proportional()) { Style = TableStyle.Default.WithKeepRowsTogether(true) };
        kept.AddRow(new TableCell("brief", text));
        kept.AddRow(new TableCell(string.Join('\n', Enumerable.Range(1, 32).Select(line => $"K{line}")), text));
        flow.Add(split).Add(kept);

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal(3, reopened.Pages.Count);
        List<Word> first = Words(FlowContentRecorder.Record(reopened.Pages[0]));
        List<Word> second = Words(FlowContentRecorder.Record(reopened.Pages[1]));
        List<Word> third = Words(FlowContentRecorder.Record(reopened.Pages[2]));

        // The split row starts under the intro and continues under the repeated header on the next page.
        string[] splitFirst = [.. first.Where(word => word.Text.StartsWith('L')).Select(word => word.Text)];
        string[] splitSecond = [.. second.Where(word => word.Text.StartsWith('L')).Select(word => word.Text)];
        Assert.NotEmpty(splitFirst);
        Assert.Equal(Enumerable.Range(1, 30).Select(line => $"L{line}"), splitFirst.Concat(splitSecond));
        Assert.Equal("Head", second[0].Text);
        Assert.Contains(first, word => word.Text == "short");
        Assert.DoesNotContain(second, word => word.Text == "short");

        // The kept row does not fit below the split row's remainder, so it moves to the next page whole.
        Assert.Contains(second, word => word.Text == "brief");
        Assert.Equal(Enumerable.Range(1, 32).Select(line => $"K{line}"), third.Select(word => word.Text));
        Assert.True(first.Min(word => word.Y) > Margin);
    }

    [Fact]
    public void Rows_joined_by_a_row_span_move_to_the_next_page_together_and_cells_hold_lists()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var table = new Table(ColumnWidth.Auto, ColumnWidth.Proportional(), ColumnWidth.Proportional()) { HeaderRows = 1 };
        table.AddRow(new TableCell("Group", text), new TableCell("Detail", text), new TableCell("Notes", text));
        for (int group = 1; group <= 12; group++)
        {
            var list = new ListBlock(new ListStyle(new TextStyle(font, 10))).Add(new ListItem("first item", text)).Add(new ListItem("second", text));
            table.AddRow(new TableCell($"G{group}", text) { RowSpan = 3 }, new TableCell(list), new TableCell($"N{group}a", text));
            table.AddRow(new TableCell($"D{group}b", text) { ColumnSpan = 2 });
            table.AddRow(new TableCell($"D{group}c", text), new TableCell($"N{group}c", text));
        }

        new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(Margin)).WithSize(612, 400)).Add(table).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.True(reopened.Pages.Count > 2);
        var groups = new List<int>();
        foreach (PdfPage page in reopened.Pages)
        {
            List<Word> words = Words(FlowContentRecorder.Record(page));
            Assert.Equal("Group", words[0].Text);
            foreach (Word word in words.Where(word => word.Text.Length > 1 && word.Text[0] == 'G' && char.IsDigit(word.Text[1])))
            {
                int group = int.Parse(word.Text[1..], System.Globalization.CultureInfo.InvariantCulture);
                groups.Add(group);

                // The whole group is on the page with its label.
                Assert.Contains(words, other => other.Text == $"D{group}c");
                Assert.Contains(words, other => other.Text == $"N{group}c");
            }

            Assert.Equal(words.Count(word => word.Text == "first"), words.Count(word => word.Text == "•") / 2);
        }

        Assert.Equal(Enumerable.Range(1, 12), groups);
    }

    [Fact]
    public void Cells_that_do_not_fit_the_grid_are_refused_before_anything_is_written()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var text = new ParagraphStyle(font, 10);
        var tooWide = new Table(ColumnWidth.Auto, ColumnWidth.Auto).AddRow(new TableCell("x", text) { ColumnSpan = 3 });
        var overlap = new Table(ColumnWidth.Auto, ColumnWidth.Auto)
            .AddRow(new TableCell("a", text), new TableCell("b", text) { RowSpan = 2 })
            .AddRow(new TableCell("c", text) { ColumnSpan = 2 });
        var headerSpan = new Table(ColumnWidth.Auto) { HeaderRows = 1 }.AddRow(new TableCell("h", text) { RowSpan = 2 }).AddRow();

        foreach (Table table in new[] { tooWide, overlap, headerSpan })
        {
            Assert.Throws<ArgumentException>(() => new FlowDocument(PageSetup.Letter).Add(table).Render(document));
        }

        Assert.Empty(document.Pages);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableCell("x", text) { RowSpan = 0 });
        Assert.Throws<ArgumentException>(() => new Table());
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static void AssertRectangle(PdfRectangle rectangle, double left, double bottom, double width, double height)
    {
        Assert.Equal(left, rectangle.Left, 6);
        Assert.Equal(bottom, rectangle.Bottom, 6);
        Assert.Equal(width, rectangle.Width, 6);
        Assert.Equal(height, rectangle.Height, 6);
    }

    private static List<Word> Words(FlowContentRecorder page)
    {
        List<Word> words = [];
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
                words[^1] = words[^1] with { Text = words[^1].Text + glyph.Text };
            }
            else
            {
                words.Add(new Word(glyph.Text, glyph.X, glyph.Y));
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

    private readonly record struct Word(string Text, double Left, double Y);
}
