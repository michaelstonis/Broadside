using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// The CoreGraphics backend's renders of the image, mask, shading and pattern corpus files (issue #126: spec D15, D16, D20–D23)
/// compared with the software rasterizer's golden images through the harness, under the threshold <see cref="GoldenThresholds"/>
/// gives the <c>coregraphics</c> backend for each page. Nothing is skipped or approximated on these pages; the one thing reported is
/// that mesh shadings were rasterized by the backend (Information), as CoreGraphics has no Gouraud-shaded triangles.
/// </summary>
public sealed class PaintGoldenImageTests
{
    private static readonly string[] Prefixes = ["image-", "inline-image", "dct-", "jpx-", "ccitt-", "jbig2-", "png-predictor", "shading-", "pattern-"];

    [Theory]
    [MemberData(nameof(PaintPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        GoldenImage render = Render(document.Pages[page], GoldenCorpus.CreateRenderOptions(), out IReadOnlyList<Diagnostic> diagnostics);

        Assert.All(diagnostics, diagnostic => Assert.Equal("BackendMeshShadingRasterized", diagnostic.Code));
        GoldenCorpus.AssertMatchesReference("coregraphics", fileName, page, render);
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

        GoldenImage actual = Render(document.Pages[0], options, out IReadOnlyList<Diagnostic> diagnostics);

        Assert.All(diagnostics, diagnostic => Assert.Equal("BackendMeshShadingRasterized", diagnostic.Code));
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), actual, new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string, int> PaintPages() =>
        GoldenCorpus.PagesOf([.. Corpus.AllFileNames.Where(name => Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))]);

    /// <summary>
    /// The synthetic pages and the threshold each meets, measured on macOS (issue #126). image-heavy: none, every image is resampled
    /// as the rasterizer does. mask-heavy: 153 pixels where a pixel centre falls exactly on a sample boundary of a rotated stencil and
    /// the rasterizer's stepped arithmetic takes the other sample. shading-grid: 512 pixels on the rims of the radial shadings, which
    /// carry the AntiAlias hint the rasterizer honours with 2 × 2 supersampling and CoreGraphics' shadings do not (Table 77 lets a hint
    /// be ignored). tiling-grid: 2517 pixels of the rotated stripe patterns, whose tiles CoreGraphics resamples bilinearly where the
    /// rasterizer renders each tile at the device. mesh-patches: 24 pixels on the curved outer edges of patches cut into a fixed
    /// 16 × 16 grid where the rasterizer adapts its grid to the device, rasterized without anti-aliasing at the mesh's edges. Skia
    /// (#122) differs on the same pages for the same reasons.
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

    internal static GoldenImage Render(PdfPage page, RenderOptions options, out IReadOnlyList<Diagnostic> diagnostics)
    {
        PixelSize size = options.GetPixelSize(page);
        using CGBitmapContext context = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] pixels, out int stride);
        using var renderer = new CoreGraphicsRenderer();
        renderer.Render(page, context, options);
        context.Flush();
        diagnostics = [.. renderer.Diagnostics];
        return GoldenImage.FromPixels(pixels, size.Width, size.Height, stride, GoldenPixelFormat.Bgra8Premultiplied);
    }
}
