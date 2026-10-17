using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Tests.Writing;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Text in an embedded TrueType subset (#150) saved as PDF/A-4 and PDF/A-4f (issue #203), validated by veraPDF's PDF/A-4 and
/// PDF/A-4F profiles: the PDF 2.0 file embeds every font program it uses (veraPDF rule 6.2.10.4.1-1).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA4Tests
{
    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    [Theory]
    [InlineData(PdfAConformance.PdfA4, "4")]
    [InlineData(PdfAConformance.PdfA4f, "4f")]
    public void Text_in_an_embedded_subset_saved_as_pdf_a_4_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = $"Broadside PDF/A-{flavour} sample";
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 18);
            canvas.MoveText(72, 700);
            canvas.ShowText("Archived on PDF 2.0: Café, Ωμέγα.");
            canvas.EndText();
        }

        if (conformance == PdfAConformance.PdfA4f)
        {
            PdfFileSpecification file = document.CreateEmbeddedFile("data.csv", "a,b\n1,2\n"u8, new PdfEmbeddedFileOptions().WithMediaType("text/csv").WithRelationship(PdfFileRelationship.Data));
            document.AddEmbeddedFile("data.csv", file);
            document.AddAssociatedFile(document.Catalog, file);
        }

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance));
        byte[] saved = stream.ToArray();

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }
}
