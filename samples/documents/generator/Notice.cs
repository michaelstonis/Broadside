using System.Text.Json;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A utility's notice in English, Arabic and Hebrew, laid out by Broadside's flow layout with its own text shaping: Arabic joining
/// and Hebrew in right-to-left paragraphs, with Latin digits inside them (notice/notice.json). The counterpart of the WeasyPrint
/// leaflet, which HarfBuzz shapes.
/// </summary>
internal static class Notice
{
    private sealed record Section(string Lang, bool Rtl, string Title, string[] Paragraphs);

    private sealed record Content(Section[] Sections);

    public static void Write(GeneratorContext context)
    {
        Content content = JsonSerializer.Deserialize<Content>(File.ReadAllText(Path.Combine(context.Root, "notice", "notice.json")),
            JsonOptions)!;
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Notice of water main replacement", "A trilingual notice (English, Arabic, Hebrew) shaped and laid out by Broadside.");
        var fonts = new Dictionary<string, (PdfCanvasFont Regular, PdfCanvasFont Bold)>(StringComparer.Ordinal)
        {
            ["en-US"] = (context.Font(document, "notosans/NotoSans-Regular.ttf"), context.Font(document, "notosans/NotoSans-Bold.ttf")),
            ["ar"] = (context.Font(document, "notonaskharabic/NotoNaskhArabic-Regular.ttf"), context.Font(document, "notonaskharabic/NotoNaskhArabic-Bold.ttf")),
            ["he"] = (context.Font(document, "notosanshebrew/NotoSansHebrew-Regular.ttf"), context.Font(document, "notosanshebrew/NotoSansHebrew-Bold.ttf")),
        };
        PdfColor blue = Rgb(0.09, 0.31, 0.39);
        PdfColor muted = Rgb(0.42, 0.48, 0.53);

        var template = new PageTemplate(new PageSetup(Mm(210), Mm(297), new PageMargins { Left = Mm(24), Right = Mm(24), Top = Mm(52), Bottom = Mm(26) }))
        {
            Footer = new HeaderFooter(new TextStyle(fonts["en-US"].Regular, 7.5).WithColor(muted))
            {
                Left = "Riverside Water is invented for the Broadside sample documents. The phone number is not in service.",
            },
        };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Notice of water main replacement") };
        foreach (Section section in content.Sections)
        {
            (PdfCanvasFont regular, PdfCanvasFont bold) = fonts[section.Lang];
            TextDirection direction = section.Rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight;
            TextShaping shaping = TextShaping.Default.WithScript(section.Lang switch { "ar" => "arab", "he" => "hebr", _ => "latn" }).WithLanguage(section.Lang switch { "ar" => "ARA", "he" => "IWR", _ => "ENG" }).WithRightToLeft(section.Rtl);
            double size = section.Lang == "ar" ? 12 : 11;
            var heading = new ParagraphStyle(bold, size + 4).WithDirection(direction).WithShaping(shaping).WithSpaceBefore(14).WithSpaceAfter(6);
            var body = new ParagraphStyle(regular, size).WithDirection(direction).WithShaping(shaping).WithLineHeight(section.Lang == "ar" ? 1.75 : 1.5).WithSpaceAfter(6);
            if (section.Rtl)
            {
                heading = heading.WithAlignment(TextAlignment.Right);
                body = body.WithAlignment(TextAlignment.Right);
            }

            flow.Add(new Paragraph(section.Title, heading) { StructureType = "H2" });
            foreach (string paragraph in section.Paragraphs)
            {
                flow.Add(new Paragraph(paragraph, body));
            }
        }

        flow.Render(document);
        foreach (PdfPage page in document.Pages)
        {
            using PdfCanvas canvas = page.CreateCanvas();
            canvas.BeginArtifact();
            canvas.SetFillColor(blue);
            canvas.Rectangle(0, Mm(297 - 36), Mm(210), Mm(36));
            canvas.Fill();

            // A water drop: two curves meeting at the top.
            canvas.SetFillColor(Rgb(0.62, 0.86, 0.95));
            double cx = Mm(34);
            double cy = Mm(297 - 20);
            canvas.MoveTo(cx, cy + 22);
            canvas.CurveTo(cx + 4, cy + 10, cx + 14, cy + 2, cx + 14, cy - 8);
            canvas.CurveTo(cx + 14, cy - 18, cx + 6, cy - 23, cx, cy - 23);
            canvas.CurveTo(cx - 6, cy - 23, cx - 14, cy - 18, cx - 14, cy - 8);
            canvas.CurveTo(cx - 14, cy + 2, cx - 4, cy + 10, cx, cy + 22);
            canvas.Fill();
            canvas.SetFillColor(Gray(1));
            canvas.BeginText();
            canvas.SetFont(fonts["en-US"].Bold, 20);
            canvas.MoveText(Mm(46), Mm(297 - 19));
            canvas.ShowText("Riverside Water");
            canvas.SetFont(fonts["en-US"].Regular, 10);
            canvas.MoveText(0, -15);
            canvas.ShowText("Notice to residents of Elm Street");
            canvas.EndText();
            canvas.EndMarkedContent();
        }

        document.Save(context.OutputPath("notice-trilingual.pdf"));
    }
}
