using System.Text;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Clipping stays off the allocator (CLAUDE.md, "Code conventions"; ISO 32000-2 §8.5.4): rectangle clips never need a mask, and the
/// coverage masks of other clips are pooled per rasterizer, so a warm replay with nested clips allocates nothing per clip, paint or
/// scanline, whether the paint is a fill, a stroke or an image. <c>ClipBenchmarks</c> shows the cost of each kind next to the same
/// paints unclipped.
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

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Drawing_images_through_rectangle_and_path_clips_allocates_nothing_once_warm(double scale)
    {
        // Images go through the same clipped sweep as fills (§8.5.4): a rectangle clip (no mask), then a triangle clip (a mask) with
        // a nearest-sample and an interpolated image under each.
        const string image = "BI /W 2 /H 2 /CS /G /BPC 8 ID \u0000\u00ff\u00ff\u0000 EI";
        const string smooth = "BI /W 2 /H 2 /CS /G /BPC 8 /I true ID \u0000\u00ff\u00ff\u0000 EI";
        string content =
            $"q 100 100 300 300 re W n q 400 0 0 400 50 50 cm {image} Q q 0 300 -300 0 450 300 cm {smooth} Q Q "
            + $"q 50 400 m 550 400 l 50 750 l h W n q 500 0 0 380 50 380 cm {image} Q q 300 80 -80 300 400 420 cm {smooth} Q Q";
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content)));
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
