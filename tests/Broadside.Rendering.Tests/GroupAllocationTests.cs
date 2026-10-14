using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Transparency groups go through rasterizer hot paths (CLAUDE.md, "Code conventions"): once warm, group layers, their initial
/// backdrops, alpha and shape planes and the conversion of a CMYK or gray group's result come from pooled buffers kept between
/// renders, so painting groups (nested eight deep included) allocates nothing per group, scanline or pixel.
/// <c>GroupRenderingBenchmarks</c> shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class GroupAllocationTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Painting_groups_allocates_nothing_once_warm(double dpi)
    {
        using PdfDocument document = PdfDocument.Open(GroupSamples.Grid());
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        var recorder = new RecordingBackend();
        list.Replay(recorder);

        // Seven single groups, eight nested, two fill-and-stroke groups, a knockout group holding two, one under a clip, and one
        // holding a knockout group.
        Assert.Equal(23, recorder.Operations.Count(line => line.StartsWith("group", StringComparison.Ordinal)));
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
