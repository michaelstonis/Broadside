using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Paragraphs broken by UAX #14, Unicode Line Breaking Algorithm (Unicode 16.0.0), read back through text extraction: breaks after
/// hyphens, none at no-break spaces, a hyphen shown at a soft hyphen only where the line breaks, and emergency breaks inside a word
/// longer than the line. Courier advances 600/1000 em: at 10 points, 20 characters fill the 120-point measure exactly.
/// </summary>
public sealed class LineBreakingFlowTests
{
    private static readonly PageSetup Narrow = new(264, 400, new PageMargins(72, 72, 72, 72));

    [Fact]
    public void A_line_may_break_after_a_hyphen()
    {
        Assert.Equal(["aaaaaaaaaaaaaa well-", "known"], Lines("aaaaaaaaaaaaaa well-known"));
    }

    [Fact]
    public void A_no_break_space_keeps_its_neighbours_on_one_line()
    {
        string[] lines = Lines("aaaaaaaaaaaaaaa 10\u00A0kg");

        Assert.Equal(["aaaaaaaaaaaaaaa", "10 kg"], lines.Select(static line => line.Replace('\u00A0', ' ')).ToArray());
    }

    [Fact]
    public void A_soft_hyphen_shows_a_hyphen_only_where_the_line_breaks_at_it()
    {
        Assert.Equal(["aaaaaaaaaaaaa extra-", "ordinary"], Lines("aaaaaaaaaaaaa extra\u00ADordinary"));
        Assert.Equal(["extraordinary"], Lines("extra\u00ADordinary"));
        Assert.Equal(["aaaaaaaaaaaa extrao-", "rdinary"], Lines("aaaaaaaaaaaa extrao\u00ADrdinary"));

        // The hyphen counts: with it, the soft hyphen's break would pass the right margin.
        Assert.Equal(["aaaaaaaaaaaaa", "extraordinary"], Lines("aaaaaaaaaaaaa extrao\u00ADrdinary"));
    }

    [Fact]
    public void A_word_longer_than_the_line_breaks_inside_it_without_overflowing()
    {
        using PdfDocument document = LayOut("ab abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz cd", out TextPage text);

        Assert.Equal(
            ["ab", "abcdefghijklmnopqrst", "uvwxyzabcdefghijklmn", "opqrstuvwxyz cd"],
            text.Lines.Select(line => text.GetLineText(line).ToString().Trim()).ToArray());
        Assert.All(text.Glyphs, glyph => Assert.InRange(glyph.Quad.Bounds.Right, 72, 192 + 0.001));
    }

    private static string[] Lines(string paragraph)
    {
        using PdfDocument document = LayOut(paragraph, out TextPage text);
        return [.. text.Lines.Select(line => text.GetLineText(line).ToString().Trim())];
    }

    private static PdfDocument LayOut(string paragraph, out TextPage text)
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        PdfCanvasFont courier = document.GetStandard14Font(Standard14Font.Courier);
        var flow = new FlowDocument(Narrow);
        flow.Add(new Paragraph(paragraph, new ParagraphStyle(courier, 10)));
        flow.Render(document);
        using var stream = new MemoryStream();
        document.Save(stream);
        document.Dispose();
        PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        text = reopened.Pages[0].ExtractText();
        return reopened;
    }
}
