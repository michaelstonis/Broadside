using Broadside.GoldenImages;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// The CoreGraphics backend's fills, strokes and clips against the software rasterizer's golden images (issue #125), through the
/// package's public renderer and the golden-image harness only.
/// </summary>
public sealed class PathRenderingTests
{
    /// <summary>The backend name in the golden-image threshold table.</summary>
    public const string Backend = "coregraphics";

    [Theory]
    [MemberData(nameof(PathPages))]
    public void A_path_page_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        GoldenImage render = BitmapRendering.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        GoldenCorpus.AssertMatchesReference(Backend, fileName, page, render);
    }

    [Fact]
    public void A_display_list_drawn_onto_a_flipped_context_through_the_device_transform_matches_the_golden_image()
    {
        // A flipped view's context: its user space is pixels from the top left, y down, so the page mapping is the device transform.
        using PdfDocument document = GoldenCorpus.Open("clipping-paths.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using var white = new CGColor(1, 1, 1, 1);
        context.SetFillColor(white);
        context.FillRect(new CGRect(0, 0, size.Width, size.Height));
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        using var renderer = new CoreGraphicsRenderer();

        renderer.Render(page.CreateDisplayList(), context, options.GetDeviceTransform(page));

        context.Flush();
        GoldenCorpus.AssertMatchesReference(
            Backend,
            "clipping-paths.pdf",
            0,
            GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied));
    }

    [Fact]
    public void A_page_rendered_into_a_larger_bitmap_fills_its_top_left_area_and_leaves_the_rest_untouched()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width + 30, size.Height + 20, out byte[] pixels, out int stride);
        using var renderer = new CoreGraphicsRenderer();

        renderer.Render(page, context, options);

        context.Flush();
        GoldenCorpus.AssertMatchesReference(
            Backend,
            "paths-fill-rules.pdf",
            0,
            GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied));
        for (int y = 0; y < size.Height + 20; y++)
        {
            int from = y < size.Height ? size.Width : 0;
            Assert.All(pixels.AsSpan((y * stride) + (from * 4), (size.Width + 30 - from) * 4).ToArray(), value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void A_bitmap_context_smaller_than_the_page_is_refused()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-fill-rules.pdf");
        PdfPage page = document.Pages[0];
        using CGBitmapContext context = BitmapRendering.CreateContext(399, 300, out _, out _);
        using var renderer = new CoreGraphicsRenderer();

        Assert.Throws<ArgumentException>("target", () => renderer.Render(page, context, GoldenCorpus.CreateRenderOptions()));
    }

    [Fact]
    public void Rendering_leaves_the_context_graphics_state_as_it_found_it()
    {
        using PdfDocument document = GoldenCorpus.Open("paths-strokes.pdf");
        PdfPage page = document.Pages[0];
        using CGBitmapContext context = BitmapRendering.CreateContext(400, 400, out _, out _);
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, 1, 3, 4));
        CGAffineTransform before = context.GetCTM();
        CGRect clipBefore = context.GetClipBoundingBox();
        using var renderer = new CoreGraphicsRenderer();

        renderer.Render(page.CreateDisplayList(), context, Matrix.Identity);
        renderer.Render(page, context, GoldenCorpus.CreateRenderOptions().WithScale(0.5));

        Assert.Equal(before, context.GetCTM());
        Assert.Equal(clipBefore, context.GetClipBoundingBox());
    }

    public static TheoryData<string, int> PathPages() =>
        GoldenCorpus.PagesOf("paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "page-rotation.pdf");
}
