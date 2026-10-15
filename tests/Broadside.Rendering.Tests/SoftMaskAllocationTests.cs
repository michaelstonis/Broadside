using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Soft masks go through rasterizer hot paths (CLAUDE.md, "Code conventions"): once warm, a mask's values, the bitmap its group renders
/// into and the child backend that renders it come from buffers kept between renders, so painting under masks (a group at
/// <c>Do</c>, a knockout group and an image included) allocates nothing per mask, paint, scanline or pixel.
/// <c>SoftMaskBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class SoftMaskAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Painting_under_soft_masks_allocates_nothing_once_warm(double dpi)
    {
        using PdfDocument document = PdfDocument.Open(SoftMaskSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        Assert.Contains(recorder.Operations, line => line.StartsWith("softmask Luminosity", StringComparison.Ordinal));
        Assert.Contains(recorder.Operations, line => line.StartsWith("softmask Alpha", StringComparison.Ordinal));
        Assert.Contains(recorder.Operations, line => line.StartsWith("group", StringComparison.Ordinal) && line.Contains(" softmask ", StringComparison.Ordinal));
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
