using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Operations the Skia backend does not draw yet (images, stencil masks, glyphs, shadings; issue #121) are skipped, never
/// approximated, and each kind is reported once per render (ADR 0005).
/// </summary>
public sealed class UnsupportedOperationTests
{
    [Theory]
    [InlineData("inline-image.pdf", "BackendImageUnsupported")]
    [InlineData("image-stencil-mask.pdf", "BackendImageMaskUnsupported")]
    [InlineData("text-truetype-embedded.pdf", "BackendGlyphsUnsupported")]
    [InlineData("shading-type2-axial.pdf", "BackendShadingUnsupported")]
    [InlineData("pattern-shading-axial.pdf", "BackendShadingUnsupported")]
    public void A_page_whose_only_paints_the_backend_cannot_draw_stays_white_and_reports_their_kind_once(string fileName, string code)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[0]);

        Diagnostic diagnostic = Assert.Single(backend.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.All(bitmap.Pixels, pixel => Assert.Equal(SKColors.White, pixel));
    }

    [Fact]
    public void Each_render_reports_only_what_it_skipped()
    {
        using PdfDocument text = GoldenCorpus.Open("text-truetype-embedded.pdf");
        using PdfDocument paths = GoldenCorpus.Open("paths-fill-rules.pdf");
        using var backend = new SkiaBackend();

        using (backend.Render(text.Pages[0]))
        {
            Assert.Single(backend.Diagnostics);
        }

        using (backend.Render(paths.Pages[0]))
        {
            Assert.Empty(backend.Diagnostics);
        }
    }

    [Fact]
    public void Invisible_text_paints_nothing_and_is_not_reported()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page("BT /F1 24 Tf 3 Tr 20 20 Td (Hidden) Tj ET"u8.ToArray()));
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[0]);

        Assert.Empty(backend.Diagnostics);
    }
}
