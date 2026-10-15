using Broadside.Fonts;
using Broadside.Layout.Flow;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Paragraphs shaped by the font's <c>GSUB</c> table (issue #174): ligatures measured and shown as one glyph, justified by
/// adjustments after the glyphs that show spaces, and extracted back to the paragraph's text.
/// </summary>
public sealed class ShapingFlowTests
{
    [Fact]
    public void A_justified_paragraph_shows_ligatures_and_extracts_its_text()
    {
        const string paragraph = "office fifty fluffier offices of finished officers";
        using PdfDocument reopened = LayOut(paragraph, TextShaping.Default, out TextPage text, out double right);

        Assert.Equal(paragraph, string.Join(' ', text.Lines.Select(line => text.GetLineText(line).ToString().Trim())));
        Assert.True(text.Lines.Count > 1);
        Assert.Contains(text.Glyphs, glyph => text.GetGlyphText(glyph).ToString() == "ffi");

        // Every line but the last ends at the right margin: the measure counted the ligatures as one glyph each.
        foreach (TextLine line in text.Lines.Take(text.Lines.Count - 1))
        {
            TextGlyph last = text.Glyphs[line.GlyphStart + line.GlyphCount - 1];
            Assert.Equal(right, last.Origin.X + last.Advance.X, 0.05);
        }
    }

    [Fact]
    public void Shaping_off_shows_one_glyph_per_character()
    {
        using PdfDocument reopened = LayOut("office", TextShaping.Default.WithFeature("liga", false), out TextPage text, out _);
        Assert.Equal(6, text.Glyphs.Count);
        Assert.Equal("office", text.Text.Trim());
    }

    private static PdfDocument LayOut(string paragraph, TextShaping shaping, out TextPage text, out double right)
    {
        var setup = new PageSetup(200, 400, new PageMargins(36, 36, 36, 36));
        right = 200 - 36;
        using (PdfDocument document = PdfDocument.Create())
        {
            document.Pages.RemoveAt(0);
            PdfCanvasFont font = document.EmbedTrueTypeFont(ShapingTestFont.Build());
            var flow = new FlowDocument(setup);
            flow.Add(new Paragraph(paragraph, new ParagraphStyle(font, 10).WithAlignment(TextAlignment.Justify).WithShaping(shaping)));
            flow.Render(document);
            using var stream = new MemoryStream();
            document.Save(stream);
            PdfDocument reopened = PdfDocument.Open(stream.ToArray());
            text = reopened.Pages[0].ExtractText();
            return reopened;
        }
    }
}
