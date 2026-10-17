using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Tests.Writing;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Text in an embedded TrueType subset (#150) saved as PDF/A-2u and PDF/A-3u (issue #202), validated by veraPDF: the subset's
/// ToUnicode CMap gives every glyph shown its Unicode value (veraPDF rules 6.2.11.7.2-1 and -2).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfA2uTests
{
    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    [Theory]
    [InlineData(PdfAConformance.PdfA2u, "2u")]
    [InlineData(PdfAConformance.PdfA3u, "3u")]
    public void Text_in_an_embedded_subset_saved_at_level_u_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = $"Broadside PDF/A-{flavour} sample";
        PdfCanvasFont font = document.EmbedTrueTypeFont(LiberationSans);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 18);
            canvas.MoveText(72, 700);
            canvas.SetTextLeading(24);
            canvas.ShowText("Searchable archive: fi ffl");
            canvas.MoveToNextLine();
            canvas.ShowText("Привет, мир! Ωμέγα. Café crème.");
            canvas.EndText();
        }

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance));
        byte[] saved = stream.ToArray();

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }
}
