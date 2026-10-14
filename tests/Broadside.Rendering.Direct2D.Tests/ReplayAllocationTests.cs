using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Replay is a hot path (CLAUDE.md, "Code conventions"): once a renderer's device resources and working buffers are warm, replaying a
/// display list onto its device context allocates nothing managed per fill, stroke, dash or clip. <c>Direct2DBackendBenchmarks</c>
/// shows the same in its <c>Allocated</c> column.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ReplayAllocationTests
{
    [Theory]
    [InlineData("paths-fill-rules.pdf", 72)]
    [InlineData("paths-strokes.pdf", 72)]
    [InlineData("clipping-paths.pdf", 72)]
    [InlineData("paths-strokes.pdf", 300)]
    public void Replaying_a_corpus_page_allocates_nothing(string fileName, double dpi)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        long allocated = MeasureReplay(document, new RenderOptions().WithDpi(dpi));

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Replaying_many_fills_allocates_nothing_per_path(double scale)
    {
        // 64 KB of paths: fills of every rule, curves, transforms.
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.PathHeavy(64 * 1024)));

        long allocated = MeasureReplay(document, new RenderOptions().WithScale(scale));

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Replaying_many_strokes_allocates_nothing_per_segment_or_dash(double scale)
    {
        // 64 KB of strokes: every cap, join and miter limit, dashes with phases and zero-length dashes, curves, closed subpaths,
        // zero-width and stroke-adjusted lines and a non-uniform transform (ISO 32000-2 §8.5.3.2).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.StrokeHeavy(64 * 1024), ContentSamples.StrokeHeavyResources));

        long allocated = MeasureReplay(document, new RenderOptions().WithScale(scale));

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(ClipSampleKind.Rectangles)]
    [InlineData(ClipSampleKind.Paths)]
    public void Replaying_many_clips_allocates_nothing_per_clip(ClipSampleKind kind)
    {
        // 192 cells, each clipped twice in nested states (ISO 32000-2 §8.5.4).
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ClipSamples.Grid(kind)));

        long allocated = MeasureReplay(document, new RenderOptions());

        Assert.Equal(0, allocated);
    }

    private static long MeasureReplay(PdfDocument document, RenderOptions options)
    {
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);
        Action render = () => renderer.Render(list, bitmap, device);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Contains(bitmap.Pixels.ToArray(), value => value != 0);
        return allocated;
    }
}
