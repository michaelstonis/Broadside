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

    [Fact]
    public void Replaying_blend_modes_and_translucent_annotations_allocates_nothing_per_operation()
    {
        // ISO 32000-2 §11.3.5 and §11.6.4.4: every blend mode at two alphas; §12.5.5: annotations with transparency layers, NoZoom
        // and NoRotate placement and widgets.
        AssertReplayAllocatesNothing(BlendSamples.SolidGrid(), 1);
        AssertReplayAllocatesNothing(AnnotationSamples.Grid(), 2, DisplayLayers.All);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public void Replaying_glyph_runs_allocates_nothing_per_glyph(double scale, bool forceOutlines)
    {
        // About 2,000 glyphs of an embedded TrueType program in modes 0 to 2 with every text state operator: drawn by CoreGraphics'
        // font engine (the font loaded once per program and renderer, during the warm-up), or filled and stroked from the outlines.
        AssertReplayAllocatesNothing(TextSamples.Page(ContentSamples.TextHeavy(2000)), scale, forceOutlines: forceOutlines);
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
        // Issue #126: images, masks, shadings, rasterized meshes and pattern tiles come from the renderer's caches once warm. The
        // document is its own, not the golden corpus's shared engine's, whose display-list caches other tests' pages evict from.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        AssertReplayAllocatesNothing(document, scale);
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
        AssertReplayAllocatesNothing(document, scale);
    }

    private static void AssertReplayAllocatesNothing(byte[] file, double scale, DisplayLayers layers = DisplayLayers.Content, bool forceOutlines = false)
    {
        using PdfDocument document = PdfDocument.Open(file);
        AssertReplayAllocatesNothing(document, scale, layers, forceOutlines);
    }

    private static void AssertReplayAllocatesNothing(PdfDocument document, double scale, DisplayLayers layers = DisplayLayers.Content, bool forceOutlines = false)
    {
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList(layers);
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out _);
        context.ConcatCTM(new CGAffineTransform(1, 0, 0, -1, 0, size.Height));
        using var renderer = new CoreGraphicsRenderer();
        Action render = () => renderer.Render(list, context, device, forceOutlines);

        long allocated = Allocations.Measure(render, warmUpCalls: 5);

        Assert.Equal(0, allocated);
        context.Flush();
        Assert.Contains(pixels, value => value != 0);
    }
}
