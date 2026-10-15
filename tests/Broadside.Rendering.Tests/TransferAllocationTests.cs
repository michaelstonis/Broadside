using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Transfer functions are applied in the colour converters the rasterizer's hot paths call (CLAUDE.md, "Code conventions"): once
/// warm, rendering fills, images, strokes and shadings under transfer functions allocates nothing per paint, scanline or pixel.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class TransferAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Rendering_under_transfer_functions_allocates_nothing_once_warm(double dpi)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("transfer-functions.pdf"));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
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
