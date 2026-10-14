using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Constant alpha, blend modes and annotations on the Skia backend (ISO 32000-2 §11.3.5, §11.6.3, §11.6.4.4, §12.5.3, §12.5.5),
/// measured against the software rasterizer within the default golden-image threshold, and tiling patterns (§8.7.3) skipped and
/// reported until the backend draws them.
/// </summary>
public sealed class CompositingTests
{
    [Fact]
    public void Every_blend_mode_at_two_alphas_renders_as_the_software_rasterizer_renders_it()
    {
        using PdfDocument document = PdfDocument.Open(BlendSamples.SolidGrid());

        (GoldenImage reference, GoldenImage render, IReadOnlyList<Broadside.Diagnostics.Diagnostic> diagnostics) = RenderBoth(document.Pages[0], 1);

        Assert.Empty(diagnostics);
        AssertAgrees(reference, render);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Translucent_nozoom_norotate_annotations_and_widgets_render_as_the_software_rasterizer_renders_them(double scale)
    {
        using PdfDocument document = PdfDocument.Open(AnnotationSamples.Grid());

        (GoldenImage reference, GoldenImage render, _) = RenderBoth(document.Pages[0], scale);

        AssertAgrees(reference, render);
    }

    [Theory]
    [MemberData(nameof(AnnotationPages))]
    public void An_annotation_page_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        GoldenCorpus.AssertMatchesReference(
            "skia",
            fileName,
            page,
            GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied));
    }

    [Theory]
    [InlineData("pattern-tiling-colored.pdf")]
    [InlineData("pattern-tiling-uncolored.pdf")]
    public void A_tiling_pattern_is_skipped_and_reported_once(string fileName)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();

        using SKBitmap bitmap = backend.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());

        Assert.Single(backend.Diagnostics, diagnostic => diagnostic.Code == "BackendTilingPatternUnsupported");
    }

    /// <summary>NoZoom and NoRotate annotations (§12.5.3) on an upright and a rotated page, appearances of paths only.</summary>
    public static TheoryData<string, int> AnnotationPages() => GoldenCorpus.PagesOf("annotations-nozoom.pdf");

    private static (GoldenImage Reference, GoldenImage Render, IReadOnlyList<Broadside.Diagnostics.Diagnostic> Diagnostics) RenderBoth(PdfPage page, double scale)
    {
        RenderOptions options = new RenderOptions().WithScale(scale);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap reference = rasterizer.Render(page, options);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(page, options);
        GoldenImage render = GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied);
        return (GoldenImage.FromBitmap(reference), render, backend.Diagnostics.ToArray());
    }

    private static void AssertAgrees(GoldenImage reference, GoldenImage render)
    {
        GoldenComparison comparison = GoldenComparison.Compare(reference, render, GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }
}
