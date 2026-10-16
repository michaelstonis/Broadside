using Broadside.GoldenImages;
using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// The Skia backend's renders of the image, mask, shading and pattern corpus files (issue #122: spec D15, D16, D20–D23) compared
/// with the software rasterizer's golden images through the harness, under the threshold <see cref="GoldenThresholds"/> gives the
/// <c>skia</c> backend for each page; nothing is skipped or approximated on these pages, so the backend reports nothing.
/// </summary>
public sealed class PaintGoldenImageTests
{
    private static readonly string[] Prefixes = ["image-", "inline-image", "dct-", "jpx-", "ccitt-", "jbig2-", "png-predictor", "shading-", "pattern-"];

    [Theory]
    [MemberData(nameof(PaintPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var backend = new SkiaBackend();
        using SKBitmap bitmap = backend.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.Empty(backend.Diagnostics);
        GoldenCorpus.AssertMatchesReference("skia", fileName, page, Image(bitmap));
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
        using var backend = new SkiaBackend();
        using SKBitmap actual = backend.Render(document.Pages[0], options);

        Assert.Empty(backend.Diagnostics);
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), Image(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    public static TheoryData<string, int> PaintPages() =>
        GoldenCorpus.PagesOf([.. Corpus.AllFileNames.Where(name => Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))]);

    /// <summary>
    /// The synthetic pages and the threshold each meets, measured on macOS (issue #122). image-heavy: none, every image is resampled
    /// as the rasterizer does. mask-heavy: 153 pixels where a pixel centre falls exactly on a sample boundary of an enlarged or
    /// rotated stencil and the rasterizer's stepped arithmetic takes the other sample. shading-grid: 512 pixels on the rims of the
    /// radial shadings, which carry the AntiAlias hint the rasterizer honours with 2 × 2 supersampling and Skia's gradients do not
    /// (Table 77 lets a hint be ignored). tiling-grid: 2836 pixels of the rotated stripe patterns, whose cells Skia's picture shader
    /// resamples bilinearly where the rasterizer renders each tile at the device, and of the no-distortion lattices Skia places at their
    /// exact sub-pixel offsets where the rasterizer rounds each tile to a pixel. mesh-patches: 36 pixels on the curved outer edges of
    /// patches cut into a fixed 16 × 16 grid where the rasterizer adapts its grid to the device.
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

    internal static GoldenImage Image(SKBitmap bitmap) =>
        GoldenImage.FromPixels(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes, GoldenPixelFormat.Bgra8Premultiplied);
}
