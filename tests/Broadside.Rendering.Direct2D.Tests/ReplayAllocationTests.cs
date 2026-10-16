using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Replay is a hot path (CLAUDE.md, "Code conventions"): once a renderer's device resources and working buffers are warm, replaying a
/// display list onto its device context allocates nothing managed per fill, stroke, dash, clip or glyph (native or outlined, #131). <c>Direct2DBackendBenchmarks</c>
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
    [InlineData("text-truetype-embedded.pdf", 72)]
    [InlineData("text-standard14.pdf", 72)]
    [InlineData("text-standard14.pdf", 300)]
    [InlineData("text-render-modes.pdf", 72)]
    [InlineData("text-type1-embedded.pdf", 72)]
    [InlineData("text-cid-identity-v.pdf", 72)]
    public void Replaying_a_corpus_page_allocates_nothing(string fileName, double dpi)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        long allocated = MeasureReplay(document, new RenderOptions().WithDpi(dpi));

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replaying_text_allocates_nothing_per_glyph(bool forceOutlines)
    {
        // 2,000 glyphs in modes 0 to 2 with every text state operator (ISO 32000-2 §9.3, §9.4).
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(ContentSamples.TextHeavy(2000)));

        long allocated = MeasureReplay(document, new RenderOptions().WithForceOutlines(forceOutlines));

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

    [Theory]
    [InlineData("pattern-shading-axial.pdf", 1)]
    [InlineData("image-smask.pdf", 1)]
    [InlineData("image-explicit-mask.pdf", 4)]
    [InlineData("image-stencil-mask.pdf", 1)]
    [InlineData("shading-type3-radial.pdf", 4)]
    [InlineData("shading-type1-function.pdf", 1)]
    [InlineData("shading-type6-coons.pdf", 1)]
    [InlineData("pattern-tiling-colored.pdf", 1)]
    [InlineData("pattern-tiling-uncolored.pdf", 4)]
    public void Replaying_a_paint_corpus_page_allocates_nothing(string fileName, double scale)
    {
        // Issue #130: images, masks, shadings, rasterized meshes and pattern tiles come from the renderer's caches once warm. The
        // document is its own, not the golden corpus's shared engine's, whose display-list caches other tests' pages evict from.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));

        long allocated = MeasureReplay(document, new RenderOptions().WithScale(scale));

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("image-heavy", 1)]
    [InlineData("image-heavy", 4)]
    [InlineData("mask-heavy", 1)]
    [InlineData("shading-grid", 4)]
    [InlineData("tiling-grid", 1)]
    [InlineData("tiling-grid", 4)]
    [InlineData("mesh-patches", 1)]
    public void Replaying_images_masks_shadings_and_patterns_allocates_nothing_per_paint(string sample, double scale)
    {
        using PdfDocument document = PdfDocument.Open(PaintGoldenImageTests.Sample(sample));

        long allocated = MeasureReplay(document, new RenderOptions().WithScale(scale));

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
        Action render = () => renderer.Render(list, bitmap, device, options.ForceOutlines);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Contains(bitmap.Pixels.ToArray(), value => value != 0);
        return allocated;
    }
}
