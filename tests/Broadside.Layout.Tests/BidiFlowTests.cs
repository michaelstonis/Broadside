using Broadside.Fonts;
using Broadside.Layout.Flow;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Paragraphs of mixed direction (issue #173, UAX #9): lines broken in logical order, each reordered into visual order (right
/// to left with numbers and Latin left to right), mirrored glyphs in right-to-left runs, and extraction of the written page back
/// in logical order. The test font gives every glyph its own advance, so the advance of a shown glyph names it.
/// </summary>
public sealed class BidiFlowTests
{
    private const string Alef = "\u05D0";
    private const string Bet = "\u05D1";
    private const string Gimel = "\u05D2";
    private const string Dalet = "\u05D3";
    private const string He = "\u05D4";
    private const string Vav = "\u05D5";

    [Fact]
    public void A_mixed_hebrew_and_english_paragraph_renders_right_to_left_with_numbers_left_to_right()
    {
        string paragraph = $"{Alef}{Bet} abc {Gimel}{Dalet} 12";

        List<RecordedGlyph> glyphs = Visual(Render(paragraph, style => style, out _));

        Assert.Equal(["1", "2", " ", Dalet, Gimel, " ", "a", "b", "c", " ", Bet, Alef], glyphs.Select(static glyph => glyph.Text));
    }

    [Fact]
    public void Extracted_text_of_the_written_page_reads_in_logical_order()
    {
        string paragraph = $"{Alef}{Bet} abc {Gimel}{Dalet} 12";

        Render(paragraph, style => style, out string extracted);

        Assert.Equal(paragraph, extracted.Trim());
    }

    [Fact]
    public void Lines_break_in_logical_order_and_each_line_is_reordered_on_its_own()
    {
        // Two words fit a line: the first line holds the first two words, right to left, at the top.
        string paragraph = $"{Alef}{Bet} {Gimel}{Dalet} {He}{Vav} {Bet}{Alef}";

        FlowContentRecorder recorder = Render(paragraph, style => style, out string extracted, width: 36 + 45 + 36);

        List<double> baselines = [.. recorder.Glyphs.Select(static glyph => glyph.Y).Distinct().OrderDescending()];
        Assert.Equal(2, baselines.Count);
        IEnumerable<string> First(double y) => Visual(recorder).Where(glyph => glyph.Y == y).Select(static glyph => glyph.Text);
        Assert.Equal([Dalet, Gimel, " ", Bet, Alef], First(baselines[0]));
        Assert.Equal([Alef, Bet, " ", Vav, He], First(baselines[1]));
        Assert.Equal($"{Alef}{Bet} {Gimel}{Dalet}\n{He}{Vav} {Bet}{Alef}", extracted);
    }

    [Fact]
    public void A_right_to_left_run_shows_mirrored_brackets()
    {
        string paragraph = $"{Alef}({Bet})";

        List<RecordedGlyph> glyphs = Visual(Render(paragraph, style => style, out string extracted));

        // Visual order ) bet ( alef, each bracket drawn with its mirror's glyph: the opening glyph first, so it reads (bet)alef.
        Assert.Equal([BidiTestFont.Advance('('), BidiTestFont.Advance(Bet[0]), BidiTestFont.Advance(')'), BidiTestFont.Advance(Alef[0])],
            glyphs.Select(static glyph => Math.Round(glyph.Advance * 100)));
        Assert.Equal(paragraph, extracted.Trim());
    }

    [Fact]
    public void The_paragraph_direction_overrides_the_first_strong_character()
    {
        string paragraph = $"{Alef}{Bet} cd";

        List<RecordedGlyph> auto = Visual(Render(paragraph, style => style, out _));
        List<RecordedGlyph> leftToRight = Visual(Render(paragraph, style => style.WithDirection(TextDirection.LeftToRight), out string extracted));

        Assert.Equal(["c", "d", " ", Bet, Alef], auto.Select(static glyph => glyph.Text));
        Assert.Equal([Bet, Alef, " ", "c", "d"], leftToRight.Select(static glyph => glyph.Text));
        Assert.Equal(paragraph, extracted.Trim());
        using PdfDocument document = PdfDocument.Create();
        var style = new ParagraphStyle(document.EmbedTrueTypeFont(BidiTestFont.Build()), 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => style.WithDirection((TextDirection)7));
    }

    [Fact]
    public void Inline_runs_of_different_styles_are_reordered_as_one_line()
    {
        FlowContentRecorder recorder = RenderContent(
            font => [new TextRun($"{Alef}{Bet} "), new TextRun($"{Gimel}{Dalet}", new TextStyle(font, 14)), new TextRun(" 34")],
            out string extracted);

        List<RecordedGlyph> glyphs = Visual(recorder);
        Assert.Equal(["3", "4", " ", Dalet, Gimel, " ", Bet, Alef], glyphs.Select(static glyph => glyph.Text));
        Assert.Equal([14.0, 14.0], glyphs.Where(static glyph => glyph.Text is Dalet or Gimel).Select(static glyph => glyph.Size));
        Assert.Equal($"{Alef}{Bet} {Gimel}{Dalet} 34", extracted.Trim());
    }

    [Fact]
    public void Left_to_right_text_is_written_as_before()
    {
        // No right-to-left character: no levels, no marked content, the same content stream as without bidi.
        FlowContentRecorder recorder = Render("abc (12) def", style => style, out string extracted);

        Assert.Equal("abc (12) def", recorder.Text);
        Assert.Equal("abc (12) def", extracted.Trim());
        Assert.DoesNotContain(recorder.Order, static item => item.Event != "glyph");
    }

    private static List<RecordedGlyph> Visual(FlowContentRecorder recorder) =>
        [.. recorder.Glyphs.OrderByDescending(static glyph => glyph.Y).ThenBy(static glyph => glyph.X)];

    private static FlowContentRecorder Render(string paragraph, Func<ParagraphStyle, ParagraphStyle> styled, out string extracted, double width = 400) =>
        Render(font => new Paragraph(paragraph, styled(new ParagraphStyle(font, 10))), out extracted, width);

    private static FlowContentRecorder RenderContent(Func<PdfCanvasFont, InlineElement[]> content, out string extracted) =>
        Render(font => new Paragraph(content(font), new ParagraphStyle(font, 10)), out extracted, 400);

    private static FlowContentRecorder Render(Func<PdfCanvasFont, Paragraph> paragraph, out string extracted, double width)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        PdfCanvasFont font = document.EmbedTrueTypeFont(BidiTestFont.Build());
        var flow = new FlowDocument(new PageSetup(width, 400, new PageMargins(36, 36, 36, 36)));
        flow.Add(paragraph(font));
        flow.Render(document);
        using var stream = new MemoryStream();
        document.Save(stream);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        TextPage text = reopened.Pages[0].ExtractText();
        extracted = string.Join('\n', text.Lines.Select(line => text.GetLineText(line).ToString()));
        return FlowContentRecorder.Record(reopened.Pages[0]);
    }
}

/// <summary>
/// A TrueType program for bidi tests: space, parentheses, digits, Latin small letters and the Hebrew letters U+05D0 to U+05EA,
/// each glyph a rectangle with its own advance (500 + 10 per glyph id, in thousandths of an em).
/// </summary>
internal static class BidiTestFont
{
    private static readonly (int Start, int End, int Glyph)[] Ranges =
    [
        (0x20, 0x20, 1),
        (0x28, 0x29, 2),
        (0x30, 0x39, 4),
        (0x61, 0x7A, 14),
        (0x05D0, 0x05EA, 40),
    ];

    private const int GlyphCount = 67;

    /// <summary>The advance of <paramref name="character"/>'s glyph at size 10, in hundredths of a point.</summary>
    public static double Advance(char character)
    {
        foreach ((int start, int end, int glyph) in Ranges)
        {
            if (character >= start && character <= end)
            {
                return 500 + ((glyph + character - start) * 10);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(character));
    }

    public static byte[] Build()
    {
        var builder = new TrueTypeBuilder
        {
            Name = "BroadsideBidi",
            Cmap = TrueTypeBuilder.CmapTable((3, 10, TrueTypeBuilder.Format12(12, Ranges))),
            Metrics = [.. Enumerable.Range(0, GlyphCount).Select(static glyph => ((ushort)(500 + (glyph * 10)), (short)50))],
        };
        for (int glyph = 0; glyph < GlyphCount; glyph++)
        {
            builder.Glyphs.Add(TrueTypeBuilder.Rectangle(50, 0, 450, 600));
        }

        return builder.Build();
    }
}
