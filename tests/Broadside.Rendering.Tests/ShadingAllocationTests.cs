using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Shading is a rasterizer hot path (CLAUDE.md, "Code conventions"): once warm, painting axial and radial shadings allocates nothing
/// per scanline or pixel, whatever the resolution. Each paint looks its colours up in a table built from the shading's function.
/// <c>ShadingBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ShadingAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Painting_shadings_allocates_nothing_per_scanline_or_pixel(double dpi)
    {
        // sh, pattern fills and strokes, Background, BBox, AntiAlias, a CMYK stitching function: 48 paints, the colour table
        // rebuilt at every change of shading, at 300 DPI 17 times the pixels of 72 DPI.
        using PdfDocument document = PdfDocument.Open(ShadingSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Equal(48, recorder.Shadings.Count);
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
