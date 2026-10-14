using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>What <see cref="Direct2DRenderer"/> promises besides matching the golden images: where it draws, and what it reports.</summary>
public sealed class RendererTests
{
    private const uint Blue = 0xFF0000FF;

    [Fact]
    public void A_warp_renderer_reports_its_driver()
    {
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        Assert.Equal(Direct2DDriver.Warp, renderer.Driver);
    }

    [Fact]
    public void A_page_rendered_into_a_larger_bitmap_leaves_the_pixels_outside_the_page_untouched()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width + 7, size.Height + 5);
        Fill(bitmap, Blue);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        renderer.Render(page, bitmap, options);

        // Top left of the page is white paper; the margin right of and below it keeps its blue.
        Assert.Equal(0xFFFFFFFFu, Pixel(bitmap, 0, 0));
        Assert.Equal(Blue, Pixel(bitmap, size.Width + 3, 10));
        Assert.Equal(Blue, Pixel(bitmap, 10, size.Height + 2));

        // The centre of the non-zero pentagram (65, 225), 75 pixels from the top of the 300-unit page, is filled red.
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 65, 75));
    }

    [Fact]
    public void Replaying_a_display_list_draws_over_the_bitmap_without_a_background()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        Fill(bitmap, Blue);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        renderer.Render(page.CreateDisplayList(), bitmap, device);

        Assert.Equal(Blue, Pixel(bitmap, 0, 0));
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 65, 75));
    }

    [Fact]
    public void A_page_of_paths_records_no_diagnostics()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-strokes.pdf");
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[0]);

        Assert.Empty(renderer.Diagnostics);
    }

    [Fact]
    public void An_operation_the_backend_cannot_draw_yet_is_skipped_with_one_diagnostic_per_kind()
    {
        // One inline image: images come to Direct2D with issue #130; until then they are skipped, never approximated.
        using PdfDocument document = GoldenCorpus.Open("inline-image.pdf");
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[0]);

        Diagnostic diagnostic = Assert.Single(renderer.Diagnostics);
        Assert.Equal("RenderingOperationSkipped", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("image", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_renderer_renders_no_more()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        renderer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.Render(document.Pages[0]));
    }

    private static void Fill(Bitmap bitmap, uint argb)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                row[(x * 4) + 0] = (byte)argb;
                row[(x * 4) + 1] = (byte)(argb >> 8);
                row[(x * 4) + 2] = (byte)(argb >> 16);
                row[(x * 4) + 3] = (byte)(argb >> 24);
            }
        }
    }

    /// <summary>Returns a pixel of the premultiplied BGRA bitmap as 0xAARRGGBB.</summary>
    private static uint Pixel(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<byte> row = bitmap.GetRow(y);
        int i = x * 4;
        return ((uint)row[i + 3] << 24) | ((uint)row[i + 2] << 16) | ((uint)row[i + 1] << 8) | row[i];
    }
}
