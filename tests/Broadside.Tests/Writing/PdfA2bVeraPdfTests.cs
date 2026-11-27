using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Files Broadside saves as PDF/A-2b (issue #201) validated by veraPDF's PDF/A-2B profile: canvas-drawn documents without text
/// (text with embedded fonts is in the Standard 14 tests' <c>PdfA2bTests</c>) and hand-written corpus files whose content can conform.
/// A failure lists the veraPDF rules (ISO 19005-2 clause and test number) the file breaks.
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA2bVeraPdfTests
{
    [Fact]
    public void A_canvas_drawn_document_with_rgb_gray_alpha_and_a_dash_saved_as_pdf_a_2b_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Shapes";
        document.Properties.Author = "Broadside";
        document.Properties.Keywords = "PDF/A";
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillColor(PdfColor.FromRgb(0.2f, 0.4f, 0.8f));
            canvas.Rectangle(72, 600, 200, 100);
            canvas.Fill();
            canvas.SaveState();
            canvas.SetFillAlpha(0.4);
            canvas.SetFillColor(PdfColor.FromGray(0.3f));
            canvas.Rectangle(150, 550, 200, 100);
            canvas.Fill();
            canvas.RestoreState();
            canvas.SetDashPattern([6, 3], 0);
            canvas.SetStrokeColor(PdfColor.FromRgb(1, 0, 0));
            canvas.MoveTo(72, 500);
            canvas.LineTo(540, 500);
            canvas.Stroke();
        }

        Assert.Empty(VeraPdf.FailedRules(Save(document), "2b"));
    }

    [Theory]
    [InlineData("empty-page.pdf")]
    [InlineData("info-dictionary.pdf")]
    [InlineData("metadata-xmp.pdf")]
    public void A_corpus_file_that_can_conform_saved_as_pdf_a_2b_passes_verapdf(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));

        Assert.Empty(VeraPdf.FailedRules(Save(document), "2b"));
    }

    [Fact]
    public void A_document_whose_images_are_resampled_and_recompressed_on_save_passes_verapdf()
    {
        // Issue #625: the image pass keeps colour spaces, so the PDF/A check sees no new violation.
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Images";
        PdfImage photo = document.CreateImage(Tests.Images.ImageOptimizationTests.Noise(600, 600, 3), new PdfImageLayout(600, 600, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
        PdfImage scan = document.CreateImage(Tests.Images.ImageOptimizationTests.Gradient(900, 900, 1), new PdfImageLayout(900, 900, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawImage(photo, 72, 400, 144, 144);
            canvas.DrawImage(scan, 300, 400, 72, 72);
        }

        var optimization = new PdfImageOptimization().WithMaxResolution(150).WithJpegQuality(75).WithRecompressLossless();

        Assert.Empty(VeraPdf.FailedRules(Save(document, optimization), "2b"));
    }

    private static byte[] Save(PdfDocument document, PdfImageOptimization? optimization = null)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b).WithImageOptimization(optimization));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "PdfANonConformant" or "PdfANotIdentified");
        return stream.ToArray();
    }
}
