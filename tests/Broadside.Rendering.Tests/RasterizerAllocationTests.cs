using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The rasterizer is a hot path (CLAUDE.md, "Code conventions"): replaying a display list into a bitmap allocates nothing per path,
/// cell or scanline once its pooled buffers are warm. <c>RasterizerBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class RasterizerAllocationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Replaying_a_display_list_into_a_bitmap_allocates_nothing_at_any_size(double scale)
    {
        // 64 KB of paths: fills of every rule, curves, transforms; at 1x and 4x the scale (16 times the scanlines and cells).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value != 0);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Drawing_images_allocates_nothing_per_image_row_or_pixel(double dpi)
    {
        // Every resampling path (nearest, bilinear, box, rotated) and every conversion path (bytes, component values, indices),
        // each image converted afresh per render: at 300 DPI, 17 times the pixels of 72 DPI.
        using PdfDocument document = PdfDocument.Open(ContentSamples.ImageHeavy());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Equal(10, recorder.Images.Count);
        var options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value is not (0 or 0xFF));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Drawing_glyphs_allocates_nothing_per_glyph_once_warm(double scale)
    {
        // More than 2,000 glyphs filled, stroked and both, from outlines with lines and quadratic curves.
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value != 0);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Drawing_masked_images_and_stencil_masks_allocates_nothing_per_mask_row_or_pixel(double dpi)
    {
        // Every mask path (ISO 32000-2 §8.9.6, §11.6.5): stencil masks enlarged, reduced, interpolated, rotated and inline; explicit
        // masks of another size and of the image's; a 16-bit soft mask with Decode at another size; Matte on DeviceRGB and Indexed
        // images; colour key masking. Each image and mask is converted afresh per render.
        using PdfDocument document = PdfDocument.Open(MaskSamples.MaskHeavy());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Equal(6, recorder.Images.Count);
        Assert.Equal(5, recorder.ImageMasks.Count);
        Assert.Empty(document.Diagnostics);
        var options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value is not (0 or 0xFF));
    }

    [Fact]
    public void Replaying_the_corpus_fill_rules_page_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var options = new RenderOptions().WithDpi(300);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render);

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Stroking_allocates_nothing_per_segment_once_warm(double scale)
    {
        // 64 KB of strokes: every cap, join and miter limit, dashes with phases and zero-length dashes, curves, closed subpaths,
        // zero-width and stroke-adjusted lines and a non-uniform transform (ISO 32000-2 §8.5.3.2).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.StrokeHeavy(64 * 1024), ContentSamples.StrokeHeavyResources));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value != 0);
    }

    [Fact]
    public void Replaying_the_corpus_strokes_page_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-strokes.pdf"));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var options = new RenderOptions().WithDpi(300);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render);

        Assert.Equal(0, allocated);
    }
}
