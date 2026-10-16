using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Compositing is a rasterizer hot path (CLAUDE.md, "Code conventions"): once warm, painting with constant alpha and every blend
/// mode allocates nothing per paint, scanline or pixel, whatever the resolution. <c>CompositingBenchmarks</c> shows the same in its
/// <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class CompositingAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Blending_allocates_nothing_per_scanline_or_pixel(double dpi)
    {
        // 16 modes x 2 alphas over a shaded backdrop, each through a fill, a stroke, an image, a stencil mask or a shading.
        using PdfDocument document = PdfDocument.Open(BlendSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);

        // The first cell (Normal, alpha 1) is the initial compositing and needs no record.
        Assert.Equal(31, recorder.Operations.Count(line => line.StartsWith("compositing", StringComparison.Ordinal)));
        var options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () => rasterizer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);
    }
}
