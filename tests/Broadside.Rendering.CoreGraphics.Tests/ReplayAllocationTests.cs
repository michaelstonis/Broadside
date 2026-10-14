using Broadside.Graphics;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Replay is a hot path (CLAUDE.md, "Code conventions"): drawing a display list onto a CoreGraphics context allocates nothing per
/// path, segment, dash or clip once the renderer is warm. <c>CoreGraphicsBackendBenchmarks</c> shows the same in its
/// <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ReplayAllocationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Replaying_fills_strokes_and_clips_allocates_nothing_per_operation(double scale)
    {
        // 64 KB of paths with every painting operator and clip, then 64 KB of strokes with every cap, join, dash pattern, zero-width
        // and stroke-adjusted lines and a non-uniform transform.
        AssertReplayAllocatesNothing(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)), scale);
        AssertReplayAllocatesNothing(ContentSamples.OnePage(ContentSamples.StrokeHeavy(64 * 1024), ContentSamples.StrokeHeavyResources), scale);
    }

    [Theory]
    [InlineData(ClipSampleKind.Rectangles)]
    [InlineData(ClipSampleKind.Paths)]
    public void Replaying_nested_clips_allocates_nothing_per_clip(ClipSampleKind kind) =>
        AssertReplayAllocatesNothing(ContentSamples.OnePage(ClipSamples.Grid(kind)), 1);

    private static void AssertReplayAllocatesNothing(byte[] file, double scale)
    {
        using PdfDocument document = PdfDocument.Open(file);
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out _);
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        using var renderer = new CoreGraphicsRenderer();
        Action render = () => renderer.Render(list, context, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Equal(0, allocated);
        context.Flush();
        Assert.Contains(pixels, value => value != 0);
    }
}
