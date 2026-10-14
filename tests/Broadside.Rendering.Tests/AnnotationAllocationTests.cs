using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The annotation layers are part of the rasterizer's hot path (CLAUDE.md, "Code conventions"): replaying 80 annotations, half of
/// them composited through a layer with their opacity and a third pivoted for NoZoom, allocates nothing once the rasterizer's layer
/// is warm. <c>AnnotationRenderingBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class AnnotationAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Replaying_annotation_layers_allocates_nothing_per_annotation_or_scanline(double dpi)
    {
        using PdfDocument document = PdfDocument.Open(AnnotationSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList(DisplayLayers.All);
        var options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        Action render = () =>
        {
            bitmap.Pixels.Fill(0xFF);
            rasterizer.Render(list, bitmap, device);
        };

        long allocated = Allocations.Measure(render, warmUpCalls: 3);

        Assert.Equal(0, allocated);

        // The third annotation (CA 0.5, no flags, at [163 10 203 50]) is orange at half opacity over the dark blue backdrop.
        Rgba pixel = bitmap.At((int)(168 * dpi / 72), size.Height - (int)(15 * dpi / 72));
        Assert.InRange(pixel.R, 125, 131);
        Assert.InRange(pixel.G, 61, 67);
        Assert.InRange(pixel.B, 61, 67);
    }
}
