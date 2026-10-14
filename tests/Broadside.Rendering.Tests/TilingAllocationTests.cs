using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Tiling is a rasterizer hot path (CLAUDE.md, "Code conventions"): once warm, painting with tiling patterns allocates nothing per
/// paint, tile, scanline or pixel, whatever the resolution. Each paint renders its cell into a kept scratch bitmap through a kept
/// child backend and lays the tiles into a kept layer. <c>TilingBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TilingAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Painting_tiling_patterns_allocates_nothing_per_tile_scanline_or_pixel(double dpi)
    {
        // Coloured, uncoloured (two colours), stroked, overlapping with alpha, rotated with no distortion, and nested patterns:
        // 55 tiling paints over 48 cells, thousands of tiles; at 300 DPI 17 times the pixels of 72 DPI.
        using PdfDocument document = PdfDocument.Open(TilingSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Equal(55, recorder.Tilings.Count);
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
}
