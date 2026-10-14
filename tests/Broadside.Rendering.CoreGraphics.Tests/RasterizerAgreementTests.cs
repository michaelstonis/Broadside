using System.Text;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Beyond the corpus: the CoreGraphics backend and the software rasterizer (the reference) render the same synthetic pages within
/// the default golden-image threshold, through public API only: clips, path edge cases, the 13 blend modes CoreGraphics defines as
/// ISO 32000-2 §11.3.5 does at alpha 1 and 0.5 over solid backdrops (§11.6.4.4), and translucent, NoZoom and NoRotate annotations
/// and widgets (§12.5.3, §12.5.5).
/// </summary>
/// <remarks>
/// The dense stroke and path samples of the allocation tests are not compared: where pieces of one stroke overlap (dashes whose caps
/// meet, a polyline crossing itself), the rasterizer adds the coverage of their anti-aliased edges (exact-area cells, non-zero
/// clamped per pixel) and CoreGraphics paints their union, so those edge pixels differ by up to half a level of coverage.
/// </remarks>
public sealed class RasterizerAgreementTests
{
    [Theory]
    [MemberData(nameof(Pages))]
    public void A_synthetic_page_renders_as_the_software_rasterizer_renders_it(string name)
    {
        using PdfDocument document = PdfDocument.Open(Create(name));
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(name.EndsWith("2x", StringComparison.Ordinal) ? 2 : 1);
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap reference = rasterizer.Render(page, options);

        GoldenImage render = BitmapRendering.Render(page, options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(reference), render, GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string> Pages() =>
        ["clip-paths", "clip-rectangles", "clip-paths 2x", "edge-cases", "edge-cases 2x", "blend-modes", "annotations", "annotations 2x"];

    private static byte[] Create(string name) => name switch
    {
        "clip-paths" or "clip-paths 2x" => ContentSamples.OnePage(ClipSamples.Grid(ClipSampleKind.Paths)),
        "clip-rectangles" => ContentSamples.OnePage(ClipSamples.Grid(ClipSampleKind.Rectangles)),
        "blend-modes" => BlendSamples.SolidGrid(CompositingTests.ExactModes),
        "annotations" or "annotations 2x" => AnnotationSamples.Grid(),
        _ => ContentSamples.OnePage(Encoding.ASCII.GetBytes(EdgeCases)),
    };

    /// <summary>
    /// Degenerate fill subpaths (the pixel under the point, §8.5.3.3.1), square-capped degenerate strokes (nothing, §8.5.3.2),
    /// geometry far beyond the page, a clip to a lone moveto, and an elliptical pen under a non-uniform transform.
    /// </summary>
    private const string EdgeCases =
        "0 0 1 rg 100.5 700.5 m 100.5 700.5 l f 120.25 700.75 m h f\n" +
        "2 J 10 w 1 0 0 RG 150 700 m 150 700 l S 1 J 200 700 m 200 700 l S\n" +
        "0 0.5 0 rg -1e12 -1e12 m 1e12 -1e12 l 300 650 l h f\n" +
        "q 4 0 0 1 100 500 cm 0 0 1 RG 3 w 0 0 m 30 40 l 60 0 l S Q\n" +
        "q 0 0 m W n 1 0 0 rg 0 0 612 792 re f Q\n" +
        "q 0.2 g 0 0 m 1e300 1e300 l 1e300 0 l h f Q\n";
}
