using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Clipping stays off the allocator (CLAUDE.md, "Code conventions"; ISO 32000-2 §8.5.4): rectangle clips never need a mask, and the
/// coverage masks of other clips are pooled per rasterizer, so a warm replay with nested clips allocates nothing per clip, paint or
/// scanline. <c>ClipBenchmarks</c> shows the cost of each kind next to the same paints unclipped.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ClipAllocationTests
{
    [Theory]
    [InlineData(ClipSampleKind.Rectangles, 1)]
    [InlineData(ClipSampleKind.Rectangles, 4)]
    [InlineData(ClipSampleKind.Paths, 1)]
    [InlineData(ClipSampleKind.Paths, 4)]
    public void Replaying_nested_clips_allocates_nothing_once_warm(ClipSampleKind kind, double scale)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ClipSamples.Grid(kind)));
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
}
