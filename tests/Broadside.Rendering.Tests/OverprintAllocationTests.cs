using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Overprint preview allocates per page, not per pixel or per paint (issue #227): its colourant planes and layers are pooled and kept
/// by the rasterizer, and what a render works out once per page (the planes of a DeviceN space, its spot colourants) does not grow
/// with the number of paints. A page render also builds the page's display list, which allocates with the paints whether the
/// preview is on or not, so the test measures what the preview adds: the same for 14 paints as for 700.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class OverprintAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Overprint_preview_adds_the_same_allocation_whatever_the_number_of_paints(double dpi)
    {
        long few = PreviewCost(OverprintSamples.Grid(14), dpi);
        long many = PreviewCost(OverprintSamples.Grid(700), dpi);

        Assert.InRange(few, 0, 4096);
        Assert.InRange(many, 0, 4096);
    }

    /// <summary>The bytes a warm page render allocates with overprint preview on beyond what it allocates with it off.</summary>
    private static long PreviewCost(byte[] file, double dpi)
    {
        using PdfDocument document = PdfDocument.Open(file);
        PdfPage page = document.Pages[0];
        PixelSize size = new RenderOptions().WithDpi(dpi).GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions off = new RenderOptions().WithDpi(dpi);
        RenderOptions on = new RenderOptions().WithDpi(dpi).WithOverprintPreview(true);

        long plain = Allocations.Measure(() => rasterizer.Render(page, bitmap, off), warmUpCalls: 3);
        long preview = Allocations.Measure(() => rasterizer.Render(page, bitmap, on), warmUpCalls: 3);
        return preview - plain;
    }
}
