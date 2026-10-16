using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Constant alpha, blend modes, annotations and tiling patterns on Direct2D (ISO 32000-2 §11.3.5, §11.6.4.4, §12.5.3, §12.5.5,
/// §8.7.3): alpha and annotation layers measured against the software rasterizer, pages that blend drawn by the software rasterizer.
/// </summary>
public sealed class CompositingTests
{
    [Fact]
    public void Constant_alpha_on_fills_and_strokes_renders_as_the_software_rasterizer_renders_it()
    {
        using PdfDocument document = PdfDocument.Open(BlendSamples.SolidGrid("Normal"));
        PdfPage page = document.Pages[0];
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap render = renderer.Render(page);

        Assert.Empty(renderer.Diagnostics);
        AssertAgrees(page, render, new RenderOptions());
    }

    [Fact]
    public void A_page_that_paints_in_a_blend_mode_other_than_normal_is_drawn_by_the_software_rasterizer_and_reported_once()
    {
        // Issue #132: Direct2D's primitive blend is source-over only, so the software rasterizer draws the page (§11.3.5).
        using PdfDocument document = PdfDocument.Open(BlendSamples.SolidGrid("Multiply", "Screen", "Hue"));
        PdfPage page = document.Pages[0];
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        using var rasterizer = new SoftwareRasterizer();

        using Bitmap render = renderer.Render(page);
        using Bitmap reference = rasterizer.Render(page);

        Assert.Equal("BackendPageRasterized", Assert.Single(renderer.Diagnostics).Code);
        Assert.True(render.Pixels.SequenceEqual(reference.Pixels));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Translucent_nozoom_norotate_annotations_and_widgets_render_as_the_software_rasterizer_renders_them(double scale)
    {
        using PdfDocument document = PdfDocument.Open(AnnotationSamples.Grid());
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap render = renderer.Render(page, options);

        AssertAgrees(page, render, options);
    }

    private static void AssertAgrees(PdfPage page, Bitmap render, RenderOptions options)
    {
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap reference = rasterizer.Render(page, options);
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(reference), GoldenImage.FromBitmap(render), GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }
}
