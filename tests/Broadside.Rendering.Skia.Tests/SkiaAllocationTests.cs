using Broadside.GoldenImages;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// A warm Skia backend replays a display list without allocating (issue #121; CLAUDE.md, hot paths): geometry is recorded in kept
/// managed buffers and the Skia path for it comes from the backend's path cache, so nothing is allocated per fill, stroke, dash, clip
/// or skipped operation.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class SkiaAllocationTests
{
    [Theory]
    [InlineData("paths-fill-rules.pdf", 72)]
    [InlineData("paths-strokes.pdf", 72)]
    [InlineData("paths-strokes.pdf", 300)]
    [InlineData("clipping-paths.pdf", 72)]
    [InlineData("clipping-paths.pdf", 300)]
    [InlineData("pattern-shading-axial.pdf", 72)]
    [InlineData("text-truetype-embedded.pdf", 72)]
    public void Replaying_a_corpus_page_allocates_nothing(string fileName, int dpi)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        Assert.Equal(0, MeasureReplay(document, dpi));
    }

    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void Replaying_thousands_of_dashed_and_adjusted_strokes_allocates_nothing_per_stroke(int dpi)
    {
        // The page SkiaBackendBenchmarks replays; its strokes fit the path cache, so a warm replay builds no path.
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(ContentSamples.StrokeHeavy(256 * 1024), ContentSamples.StrokeHeavyResources));
        Assert.Equal(0, MeasureReplay(document, dpi));
    }

    [Fact]
    public void Replaying_blend_modes_and_translucent_annotations_allocates_nothing()
    {
        // ISO 32000-2 §11.3.5 and §11.6.4.4: every blend mode at two alphas; §12.5.5: annotation layers, NoZoom and NoRotate.
        using PdfDocument blends = PdfDocument.Open(BlendSamples.SolidGrid());
        using PdfDocument annotations = PdfDocument.Open(AnnotationSamples.Grid());
        Assert.Equal(0, MeasureReplay(blends, 72));
        Assert.Equal(0, MeasureReplay(annotations, 144, DisplayLayers.All));
    }

    private static long MeasureReplay(PdfDocument document, int dpi, DisplayLayers layers = DisplayLayers.Content)
    {
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList(layers);
        RenderOptions options = new RenderOptions().WithDpi(dpi);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var backend = new SkiaBackend();
        Action replay = () => backend.Render(list, canvas, device);
        return Allocations.Measure(replay);
    }
}
