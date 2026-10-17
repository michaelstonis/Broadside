using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Layout.Flow;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Page templates flowed, saved and read back: page size and orientation per section, first, odd and even headers and footers with
/// page number and page count fields, backgrounds and watermarks, all marked as artifacts (ISO 32000-2 §14.8.2.2.2, Table 363) so
/// text extraction leaves them out; headers and footers that reach past a margin shrink the body area.
/// </summary>
public sealed class PageTemplateFlowTests
{
    [Fact]
    public void Page_n_of_N_footers_show_the_right_totals_on_a_12_page_document()
    {
        using PdfDocument document = NewDocument();
        var style = new TextStyle(document.GetStandard14Font(Standard14Font.Helvetica), 9);
        var template = new PageTemplate(PageSetup.Letter) { Footer = new HeaderFooter(style) { Center = "Page {page} of {pages}" } };
        FlowDocument flow = new FlowDocument(template);
        AddLines(flow, document, (54 * 11) + 10, "Line");

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(12, result.Pages.Count);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        for (int page = 0; page < 12; page++)
        {
            FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[page]);
            List<RecordedGlyph> footer = [.. recorder.Glyphs.Where(glyph => glyph.Artifact is not null)];
            Assert.Equal($"Page {page + 1} of 12", string.Concat(footer.Select(glyph => glyph.Text)));
            Assert.All(footer, glyph => Assert.Equal("Pagination/Footer", glyph.Artifact));
            double width = footer[^1].X + footer[^1].Advance - footer[0].X;
            Assert.Equal(306, footer[0].X + (width / 2), 3);
            Assert.True(footer[0].Y is > 36 and < 72);
            Assert.DoesNotContain("Page", reopened.Pages[page].ExtractText().PlainText, StringComparison.Ordinal);
            Assert.Contains($"Page {page + 1} of 12", reopened.Pages[page].ExtractText(new TextExtractionOptions().WithArtifacts()).PlainText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_landscape_section_between_portrait_sections_uses_its_own_template()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var style = new TextStyle(font, 9);
        PageSetup portrait = PageSetup.Letter;
        var tall = new PageTemplate(portrait) { Header = new HeaderFooter(style) { Left = "Portrait {page}" } };
        var wide = new PageTemplate(portrait.WithOrientation(PageOrientation.Landscape)) { Header = new HeaderFooter(style) { Right = "Landscape {page}/{pages}" } };
        string longLine = string.Join(' ', Enumerable.Repeat("wide", 120));
        var paragraph = new ParagraphStyle(font, 10);

        FlowDocument flow = new FlowDocument(tall).Add(new Paragraph("First section", paragraph));
        flow.StartSection(wide).Add(new Paragraph(longLine, paragraph));
        flow.StartSection(tall).Add(new Paragraph("Third section", paragraph));
        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(PageOrientation.Landscape, wide.Setup.Orientation);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal([(612.0, 792.0), (792.0, 612.0), (612.0, 792.0)], reopened.Pages.Select(page => (page.MediaBox.Width, page.MediaBox.Height)));
        string[] headers = ["Portrait 1", "Landscape 2/3", "Portrait 3"];
        for (int page = 0; page < 3; page++)
        {
            FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[page]);
            Assert.Equal(headers[page], string.Concat(recorder.Glyphs.Where(glyph => glyph.Artifact == "Pagination/Header").Select(glyph => glyph.Text)));
        }

        FlowContentRecorder landscape = FlowContentRecorder.Record(reopened.Pages[1]);
        List<RecordedGlyph> body = [.. landscape.Glyphs.Where(glyph => glyph.Artifact is null)];
        Assert.Contains(body, glyph => glyph.X > 612);
        Assert.All(body, glyph => Assert.True(glyph.X + glyph.Advance <= 792 - 72 + 0.01));
        RecordedGlyph header = landscape.Glyphs.Last(glyph => glyph.Artifact is not null);
        Assert.Equal(792 - 72, header.X + header.Advance, 3);
    }

    [Fact]
    public void The_first_page_and_even_pages_use_their_own_header_and_odd_pages_the_default()
    {
        using PdfDocument document = NewDocument();
        var style = new TextStyle(document.GetStandard14Font(Standard14Font.TimesRoman), 10);
        var template = new PageTemplate(PageSetup.Letter)
        {
            Header = new HeaderFooter(style) { Right = "Odd {page}" },
            EvenPageHeader = new HeaderFooter(style) { Left = "Even {page}" },
            FirstPageHeader = new HeaderFooter(style) { Center = "Title page" },
            FirstPageFooter = HeaderFooter.None,
            Footer = new HeaderFooter(style) { Center = "{page}" },
        };
        FlowDocument flow = new FlowDocument(template);
        AddLines(flow, document, (54 * 3) + 5, "Text");

        Assert.Equal(4, flow.Render(document).Pages.Count);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        string[] expected = ["Title page", "Even 2|2", "Odd 3|3", "Even 4|4"];
        for (int page = 0; page < 4; page++)
        {
            FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[page]);
            string header = string.Concat(recorder.Glyphs.Where(glyph => glyph.Artifact == "Pagination/Header").Select(glyph => glyph.Text));
            string footer = string.Concat(recorder.Glyphs.Where(glyph => glyph.Artifact == "Pagination/Footer").Select(glyph => glyph.Text));
            Assert.Equal(expected[page], footer.Length == 0 ? header : $"{header}|{footer}");
        }
    }

    [Fact]
    public void A_header_or_footer_reaching_past_its_margin_pushes_the_body_area_in()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var big = new TextStyle(font, 24);
        PageSetup setup = PageSetup.Letter.WithMargins(new PageMargins(20));
        var template = new PageTemplate(setup)
        {
            Header = new HeaderFooter(big) { Left = "Header", Offset = 10 },
            Footer = new HeaderFooter(big) { Left = "Footer", Offset = 10 },
        };
        FlowDocument flow = new FlowDocument(template);
        AddLines(flow, document, 200, "Body");

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        double headerBottom = 792 - 10 - ((font.Ascent - font.Descent) * 24 / 1000);
        double footerTop = 10 + ((font.Ascent - font.Descent) * 24 / 1000);
        List<RecordedGlyph> body = [.. recorder.Glyphs.Where(glyph => glyph.Artifact is null)];
        Assert.All(body, glyph => Assert.True(glyph.Y + (font.Ascent * 10 / 1000) <= headerBottom + 0.01 && glyph.Y + (font.Descent * 10 / 1000) >= footerTop - 0.01));
        Assert.True(body.Max(glyph => glyph.Y) > 792 - 20 - 40);
        Assert.Equal(792 - 10 - (font.Ascent * 24 / 1000), recorder.Glyphs.First(glyph => glyph.Artifact == "Pagination/Header").Y, 3);
        Assert.Equal(10 - (font.Descent * 24 / 1000), recorder.Glyphs.First(glyph => glyph.Artifact == "Pagination/Footer").Y, 3);
    }

    [Fact]
    public void A_background_colour_image_and_watermark_paint_under_the_body_as_artifacts()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.HelveticaBold);
        PdfImage image = document.CreateImage([255, 0, 0, 0, 0, 255], new PdfImageLayout(2, 1, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        var template = new PageTemplate(PageSetup.Letter)
        {
            BackgroundColor = PdfColor.FromRgb(1, 1, 0.9f),
            BackgroundImage = image,
            Watermark = new Watermark("DRAFT", new TextStyle(font, 72)) { Opacity = 0.2 },
        };
        FlowDocument flow = new FlowDocument(template).Add(new Paragraph("Body", new ParagraphStyle(font, 10)));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(["Background/", "Pagination/Watermark"], recorder.Artifacts);
        Assert.Equal(
            [("fill", "Background/"), ("image", "Background/"), .. Enumerable.Repeat<(string, string?)>(("glyph", "Pagination/Watermark"), 5), .. Enumerable.Repeat<(string, string?)>(("glyph", null), 4)],
            recorder.Order);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), recorder.Fills[0].Bounds);
        Assert.Equal(new Matrix(612, 0, 0, 792, 0, 0), recorder.Images[0]);
        RecordedGlyph first = recorder.Glyphs[0];
        RecordedGlyph last = recorder.Glyphs[4];
        Assert.True(last.X > first.X && last.Y > first.Y);
        Assert.Equal("Body", reopened.Pages[0].ExtractText().PlainText.Trim());
    }

    [Fact]
    public void A_header_character_the_font_cannot_encode_is_refused_before_any_page_is_added()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        var template = new PageTemplate(PageSetup.Letter) { Header = new HeaderFooter(new TextStyle(font, 9)) { Left = "Chapter 一" } };
        FlowDocument flow = new FlowDocument(template).Add(new Paragraph("Body", new ParagraphStyle(font, 10)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => flow.Render(document));

        Assert.Contains("U+4E00", error.Message, StringComparison.Ordinal);
        Assert.Empty(document.Pages);
    }

    private static void AddLines(FlowDocument flow, PdfDocument document, int count, string word)
    {
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);
        for (int line = 1; line <= count; line++)
        {
            flow.Add(new Paragraph($"{word} {line}", style));
        }
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
