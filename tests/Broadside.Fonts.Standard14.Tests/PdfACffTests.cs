using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Text in an embedded CFF subset (issue #151: CIDFontType0, <c>FontFile3</c> <c>CIDFontType0C</c>, from an OpenType-CFF or a bare
/// CFF program) saved as PDF/A-2b and PDF/A-2u (issues #201, #202), validated by veraPDF: the subset holds every glyph shown
/// (6.2.11.4.1), its widths agree with <c>W</c> (6.2.11.5) and at level U its ToUnicode CMap maps every glyph (6.2.11.7.2).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class PdfACffTests
{
    private static readonly ReadOnlyMemory<byte> LiberationSans = Standard14FontResolver.GetProgram(Standard14Font.Helvetica);

    [Theory]
    [InlineData(PdfAConformance.PdfA2b, "2b", true)]
    [InlineData(PdfAConformance.PdfA2b, "2b", false)]
    [InlineData(PdfAConformance.PdfA2u, "2u", true)]
    [InlineData(PdfAConformance.PdfA2u, "2u", false)]
    public void Text_in_an_embedded_cff_subset_passes_verapdf(PdfAConformance conformance, string flavour, bool openType)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = $"Broadside PDF/A-{flavour} CFF sample";
        PdfCanvasFont font = document.EmbedFont(openType ? CffTestFonts.OpenType(LiberationSans) : CffTestFonts.Cff(LiberationSans));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 18);
            canvas.MoveText(72, 700);
            canvas.SetTextLeading(24);
            canvas.ShowText("Compact Font Format, subset.");
            canvas.MoveToNextLine();
            canvas.ShowText(openType ? "Привет, мир! Ωμέγα. Café crème." : "Archive: 0123456789");
            canvas.EndText();
        }

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance));
        byte[] saved = stream.ToArray();

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        IReadOnlyList<string> failed = VeraPdf.FailedRules(saved, flavour);
        Assert.True(failed.Count == 0, string.Join(Environment.NewLine, failed));
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2b, "2b")]
    [InlineData(PdfAConformance.PdfA2u, "2u")]
    public void Text_in_a_type1_program_converted_to_cff_passes_verapdf(PdfAConformance conformance, string flavour)
    {
        // Issue #152: Type 1 programs (rebuilt from Liberation Sans and Foxit Symbol, with hint replacement and flex) convert to CFF.
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = $"Broadside PDF/A-{flavour} Type 1 sample";
        PdfCanvasFont latin = document.EmbedFont(Type1TestFonts.FromCff(CffTestFonts.Cff(LiberationSans)));
        PdfCanvasFont symbol = document.EmbedFont(Type1TestFonts.FromCff(Standard14FontResolver.GetProgram(Standard14Font.Symbol)));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(latin, 18);
            canvas.MoveText(72, 700);
            canvas.ShowText("Type 1, converted: 0123456789");
            canvas.SetFont(symbol, 18);
            canvas.MoveText(0, -30);
            canvas.ShowText("αβγ∑∫≠∞");
            canvas.EndText();
        }

        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(conformance));
        byte[] saved = stream.ToArray();

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        IReadOnlyList<string> failed = VeraPdf.FailedRules(saved, flavour);
        Assert.True(failed.Count == 0, string.Join(Environment.NewLine, failed));
    }
}
