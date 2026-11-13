using Broadside.Fonts;
using Broadside.Graphics;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// Finishes the feature showcase that WeasyPrint lays out: opens its output, adds two optional content groups (layers) to every
/// page in an incremental update, so the file has two producers and two revisions. "Proof marks" (shown by default) outlines the
/// page margins and labels each page; "Draft stamp" (hidden by default) prints a translucent diagonal stamp. Page labels name the
/// cover and number the rest.
/// </summary>
internal static class ShowcaseLayers
{
    /// <summary>The WeasyPrint output build.sh leaves in the output folder for this step (and then deletes).</summary>
    public const string Input = ".showcase-weasyprint.pdf";

    public static void Write(GeneratorContext context)
    {
        string input = context.OutputPath(Input);
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(input));
        PdfOptionalContentGroup proof = document.CreateOptionalContentGroup("Proof marks");
        PdfOptionalContentGroup draft = document.CreateOptionalContentGroup("Draft stamp", visible: false);
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.HelveticaBold);
        PdfColor magenta = Cmyk(0, 1, 0, 0);

        for (int i = 0; i < document.Pages.Count; i++)
        {
            PdfPage page = document.Pages[i];
            PdfRectangle box = page.MediaBox;
            using PdfCanvas canvas = page.CreateCanvas();
            canvas.BeginOptionalContent(proof);
            canvas.SaveState();
            canvas.SetStrokeColor(magenta);
            canvas.SetLineWidth(0.4);
            canvas.SetDashPattern([3, 2], 0);
            if (i > 0)
            {
                canvas.Rectangle(box.Left + Mm(20), box.Bottom + Mm(24), box.Width - Mm(40), box.Height - Mm(46));
                canvas.Stroke();
            }

            canvas.SetFillColor(magenta);
            canvas.BeginText();
            canvas.SetFont(font, 6);
            canvas.MoveText(box.Left + Mm(4), box.Bottom + Mm(4));
            canvas.ShowText(i == 0 ? "PROOF · cover · full bleed" : $"PROOF · page {i} · margins 22 / 20 / 24 / 20 mm");
            canvas.EndText();
            canvas.RestoreState();
            canvas.EndMarkedContent();

            canvas.BeginOptionalContent(draft);
            canvas.SaveState();
            canvas.SetFillColor(Rgb(0.85, 0.1, 0.2));
            canvas.SetFillAlpha(0.18);
            canvas.Translate(box.Left + (box.Width / 2), box.Bottom + (box.Height / 2));
            canvas.Rotate(52);
            canvas.BeginText();
            canvas.SetFont(font, 120);
            canvas.MoveText(-font.MeasureWidth("DRAFT", 120) / 2, -40);
            canvas.ShowText("DRAFT");
            canvas.EndText();
            canvas.RestoreState();
            canvas.EndMarkedContent();
        }

        PdfPageLabels labels = document.CreatePageLabels();
        labels.SetRange(0, PdfPageLabelStyle.None, prefix: "Cover");
        labels.SetRange(1, PdfPageLabelStyle.Arabic);

        using FileStream output = File.Create(context.OutputPath("showcase.pdf"));
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithUpdateModificationDate(false));
    }
}
