using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests;

/// <summary>
/// Paragraphs of styled runs and inline images, flowed, saved, reopened and read back through the content processor and the page's
/// annotations: font, size and colour change at the run boundaries on one baseline (ISO 32000-2 §9.3, §8.6.8), underlines are
/// filled bars, links are link annotations with URI actions over the run (§12.5.6.5, §12.6.4.8), and images are painted with
/// <c>Do</c> (§8.9.5).
/// </summary>
public sealed class InlineFlowTests
{
    private static readonly PageSetup Page = PageSetup.Letter.WithMargins(new PageMargins(72));

    [Fact]
    public void A_styled_paragraph_re_reads_with_the_font_size_and_colour_changes_at_the_expected_glyphs()
    {
        using PdfDocument document = NewDocument();
        var regular = new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        TextStyle bold = regular.WithFont(document.GetStandard14Font(Standard14Font.HelveticaBold));
        TextStyle big = regular.WithFont(document.GetStandard14Font(Standard14Font.TimesRoman)).WithFontSize(18).WithColor(PdfColor.FromRgb(1, 0, 0));
        var style = new ParagraphStyle(regular.Font, 12);
        var flow = new FlowDocument(Page);
        flow.Add(new Paragraph([new TextRun("Plain "), new TextRun("bold", bold), new TextRun(" then "), new TextRun("big red", big), new TextRun(" again.", regular)], style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder page = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal("Plain bold then big red again.", page.Text);
        string expected = "RRRRRRBBBBRRRRRRTTTTTTTRRRRRRR";
        for (int index = 0; index < expected.Length; index++)
        {
            RecordedGlyph glyph = page.Glyphs[index];
            (string font, double size, PdfColor color) = expected[index] switch
            {
                'B' => ("Helvetica-Bold", 12.0, PdfColor.FromGray(0)),
                'T' => ("Times-Roman", 18.0, PdfColor.FromRgb(1, 0, 0)),
                _ => ("Helvetica", 12.0, PdfColor.FromGray(0)),
            };
            Assert.Equal(font, glyph.Font);
            Assert.Equal(size, glyph.Size);
            Assert.Equal(color, glyph.Color);
        }

        // One baseline for the whole line, lowered so the 18-point run fits below the top margin.
        Assert.All(page.Glyphs, glyph => Assert.Equal(page.Glyphs[0].Y, glyph.Y, 6));
        double plainBaseline = 792 - 72 - ((12 * 1.2) - ((regular.Font.Ascent - regular.Font.Descent) * 12 / 1000)) / 2 - (regular.Font.Ascent * 12 / 1000);
        Assert.True(page.Glyphs[0].Y < plainBaseline - 1, $"baseline {page.Glyphs[0].Y} should be below {plainBaseline}");
        Assert.True(page.Glyphs[0].Y + (big.Font.Ascent * 18 / 1000) <= 792 - 72 + 1e-6);

        // The runs follow each other without gaps or overlaps.
        for (int index = 1; index < page.Glyphs.Count; index++)
        {
            Assert.Equal(page.Glyphs[index - 1].X + page.Glyphs[index - 1].Advance, page.Glyphs[index].X, 3);
        }
    }

    [Fact]
    public void Styled_runs_break_across_lines_and_justify_to_the_margin_with_every_glyph_in_order()
    {
        using PdfDocument document = NewDocument();
        var regular = new TextStyle(document.GetStandard14Font(Standard14Font.TimesRoman), 11);
        TextStyle italic = regular.WithFont(document.GetStandard14Font(Standard14Font.TimesItalic)).WithFontSize(13);
        var style = new ParagraphStyle(regular.Font, 11).WithAlignment(TextAlignment.Justify);
        List<InlineElement> runs = [];
        for (int index = 0; index < 40; index++)
        {
            runs.Add(new TextRun($"word{index} ", index % 3 == 0 ? italic : regular));
        }

        new FlowDocument(Page).Add(new Paragraph(runs, style)).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder page = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(string.Concat(runs.Cast<TextRun>().Select(run => run.Text)).Replace(" ", string.Empty, StringComparison.Ordinal), page.Text.Replace(" ", string.Empty, StringComparison.Ordinal));
        List<IGrouping<double, RecordedGlyph>> lines = [.. page.Glyphs.GroupBy(glyph => Math.Round(glyph.Y, 3))];
        Assert.True(lines.Count >= 3);
        foreach (IGrouping<double, RecordedGlyph> line in lines.Take(lines.Count - 1))
        {
            RecordedGlyph last = line.Last(glyph => glyph.Text != " ");
            Assert.Equal(612 - 72, last.X + last.Advance, 1);
            Assert.Equal(72, line.First().X, 6);
        }
    }

    [Fact]
    public void A_text_link_creates_a_link_annotation_over_the_run_on_each_line_it_is_on()
    {
        using PdfDocument document = NewDocument();
        var regular = new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        var target = new Uri("https://example.com/broadside");
        var style = new ParagraphStyle(regular.Font, 12);
        var flow = new FlowDocument(PageSetup.Letter.WithMargins(new PageMargins(72, 72, 430, 72)));
        flow.Add(new Paragraph([new TextRun("See "), new TextRun("the linked words here", regular.WithUnderline(true).WithColor(PdfColor.FromRgb(0, 0, 1))) { Link = target }, new TextRun(" for more.")], style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        PdfPage page = reopened.Pages[0];
        FlowContentRecorder content = FlowContentRecorder.Record(page);
        PdfLinkAnnotation[] links = [.. page.Annotations.OfType<PdfLinkAnnotation>()];
        Assert.Equal(2, links.Length);
        string linked = "thelinkedwordshere";
        List<RecordedGlyph> linkedGlyphs = [.. content.Glyphs.Where(glyph => glyph.Color == PdfColor.FromRgb(0, 0, 1) && glyph.Text != " ")];
        Assert.Equal(linked, string.Concat(linkedGlyphs.Select(glyph => glyph.Text)));
        foreach (PdfLinkAnnotation link in links)
        {
            Assert.Equal(target.AbsoluteUri, Assert.IsType<PdfUriAction>(link.Action).Uri);
            List<RecordedGlyph> covered = [.. linkedGlyphs.Where(glyph => glyph.Y > link.Rect.Bottom && glyph.Y < link.Rect.Top)];
            Assert.NotEmpty(covered);
            Assert.Equal(covered[0].X, link.Rect.Left, 3);
            Assert.True(covered[^1].X + covered[^1].Advance <= link.Rect.Right + 1e-3);
        }

        Assert.Equal(linkedGlyphs.Count, links.Sum(link => linkedGlyphs.Count(glyph => glyph.Y > link.Rect.Bottom && glyph.Y < link.Rect.Top)));

        // The underline is a blue bar below each line's linked glyphs; the text after the link is black again.
        Assert.Equal(2, content.Fills.Count);
        Assert.All(content.Fills, fill => Assert.Equal(PdfColor.FromRgb(0, 0, 1), fill.Color));
        Assert.All(content.Fills, fill => Assert.Contains(linkedGlyphs, glyph => fill.Bounds.Top < glyph.Y && fill.Bounds.Top > glyph.Y - 3));
        Assert.Equal(PdfColor.FromGray(0), content.Glyphs[^1].Color);
    }

    [Fact]
    public void A_strike_through_bar_crosses_the_run_at_half_its_x_height()
    {
        using PdfDocument document = NewDocument();
        var regular = new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 20);
        new FlowDocument(Page).Add(new Paragraph([new TextRun("old", regular.WithStrikeThrough(true)), new TextRun(" new", regular)], new ParagraphStyle(regular.Font, 20))).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder content = FlowContentRecorder.Record(reopened.Pages[0]);
        (PdfRectangle bar, _) = Assert.Single(content.Fills);
        RecordedGlyph o = content.Glyphs[0];
        RecordedGlyph d = content.Glyphs[2];
        Assert.Equal(o.X, bar.Left, 3);
        Assert.Equal(d.X + d.Advance, bar.Right, 3);
        Assert.Equal(o.Y + (regular.Font.XHeight * 20 / 2000), (bar.Top + bar.Bottom) / 2, 3);
    }

    [Fact]
    public void An_inline_image_stands_on_the_baseline_between_runs_and_raises_the_line()
    {
        using PdfDocument document = NewDocument();
        PdfImage image = Image(document);
        var regular = new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        var style = new ParagraphStyle(regular.Font, 12);
        new FlowDocument(Page).Add(new Paragraph([new TextRun("Logo "), new InlineImage(image, 30, 40), new TextRun(" text")], style)).Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder content = FlowContentRecorder.Record(reopened.Pages[0]);
        Matrix placed = Assert.Single(content.Images);
        RecordedGlyph space = content.Glyphs[4];
        RecordedGlyph after = content.Glyphs[5];
        Assert.Equal((30.0, 40.0), (placed.A, placed.D));
        Assert.Equal(space.X + space.Advance, placed.E, 3);
        Assert.Equal(space.Y, placed.F, 6);
        Assert.Equal(placed.E + 30, after.X, 3);
        Assert.Equal(792 - 72, placed.F + 40, 6);
    }

    [Fact]
    public void A_block_image_is_placed_below_the_text_aligned_and_scaled_down_to_the_content_width()
    {
        using PdfDocument document = NewDocument();
        PdfImage image = Image(document);
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        var flow = new FlowDocument(Page)
            .Add(new Paragraph("Above", style))
            .Add(new ImageBlock(image, 100, 50) { Alignment = TextAlignment.Center, SpaceBefore = 6 })
            .Add(new ImageBlock(image, 936, 200) { AlternateText = "Too wide" })
            .Add(new Paragraph("Below", style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder content = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(2, content.Images.Count);
        Matrix centred = content.Images[0];
        Matrix scaled = content.Images[1];
        double top = 792 - 72 - (12 * 1.2) - 6;
        Assert.Equal((100.0, 50.0, 256.0, top - 50), (centred.A, centred.D, centred.E, centred.F));
        Assert.Equal((468.0, 100.0, 72.0, top - 50 - 100), (scaled.A, scaled.D, scaled.E, scaled.F));
        Assert.True(content.Glyphs[^1].Y < scaled.F);
    }

    private static PdfImage Image(PdfDocument document) =>
        document.CreateImage([0, 64, 128, 255], new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

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
