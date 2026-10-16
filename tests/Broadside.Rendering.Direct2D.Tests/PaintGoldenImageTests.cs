using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// The Direct2D backend's renders of the image, mask, shading and pattern corpus (issue #130) match the software rasterizer's golden
/// images (ISO 32000-2 §8.7, §8.9, §11.6.5.2): images and masks as Direct2D bitmaps, axial and nested radial shadings as gradient
/// brushes, other radials and meshes rasterized per device pixel, tiling patterns as bitmap tiles. Rendered on WARP.
/// </summary>
public sealed class PaintGoldenImageTests
{
    private const string Backend = "direct2d";

    private static readonly string[] Prefixes = ["image-", "inline-image", "dct-", "jpx-", "ccitt-", "jbig2-", "png-predictor", "shading-", "pattern-"];

    /// <summary>The diagnostics a page may leave: the backend's notes that it rasterized shadings itself.</summary>
    private static readonly string[] Rasterized = ["BackendMeshShadingRasterized", "BackendRadialShadingRasterized"];

    [Theory]
    [MemberData(nameof(PaintPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.All(renderer.Diagnostics, diagnostic => Assert.Contains(diagnostic.Code, Rasterized));
        GoldenCorpus.AssertMatchesReference(Backend, fileName, page, GoldenImage.FromBitmap(bitmap));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Synthetic_page_matches_the_software_rasterizer(string sample, double edges, double flat, int pixels)
    {
        // The pages the rasterizer's allocation tests paint: every image resampling path, every mask path, shading and tiling grids.
        using PdfDocument document = PdfDocument.Open(Sample(sample));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap actual = renderer.Render(document.Pages[0], options);

        Assert.All(renderer.Diagnostics, diagnostic => Assert.Contains(diagnostic.Code, Rasterized));
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), GoldenImage.FromBitmap(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string, int> PaintPages() =>
        GoldenCorpus.PagesOf([.. Corpus.AllFileNames.Where(name => Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))]);

    /// <summary>
    /// The synthetic pages and their thresholds, as for the Skia and CoreGraphics backends: mask-heavy (200 px) for edge
    /// anti-aliasing of stencils drawn through layers, shading-grid (600) for gradient rims, tiling-grid (3000) for tiles laid at
    /// exact sub-pixel offsets, mesh-patches (48) for patches cut into a fixed grid.
    /// </summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "image-heavy", 0.3, 0.05, 0 },
        { "mask-heavy", 0.3, 0.05, 200 },
        { "shading-grid", 0.3, 0.05, 600 },
        { "tiling-grid", 0.3, 0.05, 3000 },
        { "mesh-patches", 0.3, 0.05, 48 },
    };

    internal static byte[] Sample(string name) => name switch
    {
        "image-heavy" => ContentSamples.ImageHeavy(),
        "mask-heavy" => MaskSamples.MaskHeavy(),
        "shading-grid" => ShadingSamples.Grid(),
        "tiling-grid" => TilingSamples.Grid(),
        "mesh-patches" => MeshShadingSamples.Patches(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
