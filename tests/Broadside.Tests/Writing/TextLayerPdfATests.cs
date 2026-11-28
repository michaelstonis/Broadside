using Broadside.Graphics;
using Broadside.Images;
using Broadside.Text;

namespace Broadside.Tests.Writing;

/// <summary>
/// A scanned page with an OCR text layer (issue #649) saved as PDF/A-2b and PDF/A-2u: the glyphless font embeds a valid TrueType
/// program whose blank glyph is not .notdef, its widths agree with the program's, and its <c>ToUnicode</c> map covers every code
/// shown, so veraPDF finds nothing (ISO 19005-2 §6.2.11.4, §6.2.11.5, §6.2.11.7 and §6.2.11.8).
/// </summary>
public sealed class TextLayerPdfATests
{
    [Theory]
    [Trait("Category", "VeraPdf")]
    [InlineData(PdfAConformance.PdfA2b, "2b")]
    [InlineData(PdfAConformance.PdfA2u, "2u")]
    public void A_scanned_page_with_a_text_layer_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        Assert.Empty(VeraPdf.FailedRules(Save(conformance), flavour));
    }

    [Fact]
    public void A_scanned_page_with_a_text_layer_saves_as_pdf_a_2b_without_a_conformance_diagnostic()
    {
        byte[] file = Save(PdfAConformance.PdfA2b);

        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal("Rechnung Nr. 17 Ωμέγα", reopened.Pages[0].ExtractText().PlainText.Trim());
    }

    private static byte[] Save(PdfAConformance conformance)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Scan";
        byte[] samples = Images.ImageOptimizationTests.Noise(200, 260, 1);
        PdfImage scan = document.CreateImage(samples, new PdfImageLayout(200, 260, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(scan, 0, 0, 612, 792);
        }

        document.Pages[0].AddTextLayer(new OcrPage(2550, 3300,
        [
            new OcrLine([
                new OcrWord("Rechnung", new PdfRectangle(200, 200, 440, 250)),
                new OcrWord("Nr.", new PdfRectangle(460, 200, 580, 250)),
                new OcrWord("17", new PdfRectangle(600, 200, 700, 250)),
                new OcrWord("Ωμέγα", new PdfRectangle(720, 200, 900, 250)),
            ], Language: "de"),
        ]));

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfANonConformant" or "PdfANotIdentified");
        return stream.ToArray();
    }
}
