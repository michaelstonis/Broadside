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
