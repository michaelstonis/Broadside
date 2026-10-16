using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Tests.Writing;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Canvas-written documents saved as PDF/A-2b (issue #201) and validated by veraPDF against its PDF/A-2B validation profile: the
/// file names every rule it fails, so a failure says which veraPDF rule (ISO 19005-2 clause and test number) the writer broke. Text
/// uses Liberation Sans embedded as a subset (#150), since PDF/A needs every font program embedded (veraPDF rule 6.2.11.4.1-1).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA2bTests
{
    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    [Fact]
    public void Text_paths_and_colours_drawn_with_an_embedded_font_save_as_pdf_a_2b_that_verapdf_passes()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Broadside PDF/A-2b sample";
        document.Properties.Author = "Broadside";
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromRgb(0.9f, 0.95f, 1f));
            canvas.Rectangle(36, 36, 540, 720);
            canvas.Fill();
            canvas.SetStrokeColor(PdfColor.FromGray(0.2f));
            canvas.SetLineWidth(2);
            canvas.MoveTo(72, 650);
            canvas.CurveTo(200, 700, 400, 600, 540, 650);
            canvas.Stroke();
            canvas.BeginText();
            canvas.SetFillColor(PdfColor.FromRgb(0.1f, 0.2f, 0.6f));
            canvas.SetFont(font, 18);
            canvas.MoveText(72, 700);
            canvas.SetTextLeading(24);
            canvas.ShowText("Archived with Broadside: PDF/A-2b");
            canvas.MoveToNextLine();
            canvas.ShowText("Привет, мир! Ωμέγα. Café crème.");
            canvas.EndText();
        }

        byte[] saved = SaveAsPdfA2b(document);

        Assert.Empty(VeraPdf.FailedRules(saved, "2b"));
        AssertIdentifiedAsPdfA2b(saved);
    }

    [Fact]
    public void Images_transparency_shadings_and_forms_save_as_pdf_a_2b_that_verapdf_passes()
    {
        using PdfDocument document = PdfDocument.Create();
        byte[] samples = new byte[8 * 8 * 3];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(i * 7);
        }

        PdfImage image = document.CreateImage(samples, new PdfImageLayout(8, 8, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        PdfAxialShading shading = document.CreateAxialShading(
            new PathPoint(72, 0),
            new PathPoint(540, 0),
            [new PdfGradientStop(0, PdfColor.FromRgb(1, 0, 0)), new PdfGradientStop(1, PdfColor.FromRgb(0, 0, 1))]);
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 100, 100));
        using (PdfCanvas formCanvas = form.CreateCanvas())
        {
            formCanvas.SetFillColor(PdfColor.FromGray(0.5f));
            formCanvas.Rectangle(10, 10, 80, 80);
            formCanvas.Fill();
        }

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SaveState();
            canvas.Rectangle(72, 500, 468, 100);
            canvas.Clip();
            canvas.EndPath();
            canvas.PaintShading(shading);
            canvas.RestoreState();

            canvas.SaveState();
            canvas.SetFillAlpha(0.5);
            canvas.SetBlendMode(BlendMode.Multiply);
            canvas.Transform(new Matrix(200, 0, 0, 200, 72, 250));
            canvas.DrawImage(image);
            canvas.RestoreState();

            canvas.SaveState();
            canvas.Translate(350, 250);
            canvas.DrawForm(form);
            canvas.RestoreState();
        }

        byte[] saved = SaveAsPdfA2b(document);

        Assert.Empty(VeraPdf.FailedRules(saved, "2b"));
        AssertIdentifiedAsPdfA2b(saved);
    }

    [Fact]
    public void A_pdf_a_2b_file_updated_incrementally_in_pdf_a_mode_still_passes_verapdf()
    {
        byte[] first;
        using (PdfDocument document = PdfDocument.Create())
        {
            Draw(document, document.EmbedTrueTypeFont(LiberationSans), "First revision");
            first = SaveAsPdfA2b(document);
        }

        using var stream = new MemoryStream();
        using (PdfDocument reopened = PdfDocument.Open(first))
        {
            reopened.Properties.Subject = "Updated";
            Draw(reopened, reopened.EmbedTrueTypeFont(LiberationSans), "Second revision");
            reopened.Save(stream, new PdfSaveOptions().WithIncrementalUpdate(true).WithPdfAConformance(PdfAConformance.PdfA2b));
            Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        }

        byte[] updated = stream.ToArray();
        Assert.True(updated.Length > first.Length);
        Assert.Empty(VeraPdf.FailedRules(updated, "2b"));
        AssertIdentifiedAsPdfA2b(updated);
    }

    private static void Draw(PdfDocument document, PdfCanvasFont font, string text)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 14);
        canvas.MoveText(72, text.StartsWith('F') ? 720 : 690);
        canvas.ShowText(text);
        canvas.EndText();
    }

    private static byte[] SaveAsPdfA2b(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        return stream.ToArray();
    }

    /// <summary>What Broadside itself reads back: PDF 1.7, an sRGB PDF/A output intent and the PDF/A identification.</summary>
    private static void AssertIdentifiedAsPdfA2b(byte[] saved)
    {
        Assert.StartsWith("%PDF-1.7", System.Text.Encoding.ASCII.GetString(saved, 0, 8), StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfOutputIntent intent = Assert.Single(reopened.OutputIntents);
        Assert.Equal("GTS_PDFA1", intent.Subtype);
        Assert.Equal("sRGB IEC61966-2.1", intent.OutputConditionIdentifier);
        Assert.Equal(3, intent.DestinationProfile!.ComponentCount);
        string packet = System.Text.Encoding.UTF8.GetString(reopened.DecodeStream(reopened.Metadata!.Stream).Span);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", packet, StringComparison.Ordinal);
    }
}
