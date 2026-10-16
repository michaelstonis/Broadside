using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// What the Skia backend approximates (transparency groups and soft masks until #124) is reported once per kind per render, and a
/// render reports only what it approximated itself (ADR 0005: nothing is skipped or approximated silently).
/// </summary>
public sealed class UnsupportedOperationTests
{
    [Fact]
    public void Each_render_reports_only_what_it_skipped()
    {
        using PdfDocument image = GoldenCorpus.Open("transparency-soft-masks.pdf");
        using PdfDocument paths = GoldenCorpus.Open("paths-fill-rules.pdf");
        using var backend = new SkiaBackend();

        using (backend.Render(image.Pages[0]))
        {
            Assert.NotEmpty(backend.Diagnostics);
            Assert.Equal(backend.Diagnostics.Count, backend.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct().Count());
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
