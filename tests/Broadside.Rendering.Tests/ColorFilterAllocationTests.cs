using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The color filter runs inside the rasterizer's hot path (spec #33 viewer capability 9): a warm dark-mode render, images and their
/// covered area included, allocates nothing per pixel, row or image.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ColorFilterAllocationTests
{
    [Theory]
    [InlineData("paths")]
    [InlineData("images")]
    public void A_dark_mode_render_allocates_nothing_once_warm(string content)
    {
        using PdfDocument document = PdfDocument.Open(content == "paths"
            ? ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024))
            : ContentSamples.ImageHeavy());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var options = new RenderOptions();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer { ColorFilter = ColorFilter.DarkMode };
        Action render = () =>
        {
            bitmap.Pixels.Fill(byte.MaxValue);
            rasterizer.Render(list, bitmap, device);
        };

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Equal(0, allocated);
        Assert.Contains(bitmap.Pixels.ToArray(), value => value is not 0 and not 255);
    }
}
