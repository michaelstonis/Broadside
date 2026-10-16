using Broadside.GoldenImages;
using Broadside.Rendering.Android.Device;
using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// Images, masks, shadings and patterns on the Android backend (issue #134): every image, mask, shading and pattern corpus page against
/// the software rasterizer's golden images, the synthetic pages against the rasterizer itself, images converted once per backend within
/// the document's image budget, and pattern cells rendered once per backend and scale.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9 (images, §8.9.5 sample data, §8.9.6 masks), §11.6.5.2 (soft mask images), §8.7.4 (shadings), §8.7.3 (tiling
/// patterns); spec #33 Testing Decisions (a backend within a perceptual threshold of the rasterizer) and D10 (the image cache budget).
/// </remarks>
public sealed class AndroidPaintTests
{
    private static readonly string[] Prefixes = ["image-", "inline-image", "dct-", "jpx-", "ccitt-", "jbig2-", "png-predictor", "shading-", "pattern-"];

    /// <summary>Every image, mask, shading and pattern corpus file.</summary>
    internal static readonly string[] PaintFiles = [.. Corpus.AllFileNames.Where(name => Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))];

    /// <summary>The files the device also renders with a zero image cache budget.</summary>
    internal static readonly string[] UncachedFiles = ["image-smask.pdf", "sample-image-heavy.pdf", "sample-mask-heavy.pdf"];

    public static TheoryData<string, int> PaintPages() => GoldenCorpus.PagesOf(PaintFiles);

    /// <summary>
    /// The synthetic pages and the threshold each meets against the rasterizer; the same causes as Skia (#122) and CoreGraphics (#126)
    /// on the same pages. image-heavy: every image is resampled as the rasterizer does. mask-heavy: pixel centres that fall exactly on
    /// a sample boundary of a rotated stencil, where the rasterizer's stepped arithmetic takes the other sample. shading-grid: the rims
    /// of the radial shadings that carry the AntiAlias hint, which the rasterizer honours with 2 × 2 supersampling and Android's
    /// gradients do not (Table 77 lets a hint be ignored). tiling-grid: the rotated stripe patterns, whose tiles the canvas resamples
    /// bilinearly where the rasterizer renders each tile at the device. mesh-patches: the curved outer edges of patches cut into a
    /// fixed 16 × 16 grid where the rasterizer adapts its grid to the device, rasterized without anti-aliasing at the mesh's edges.
    /// </summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "sample-image-heavy.pdf", 0.3, 0.05, 0 },
        { "sample-mask-heavy.pdf", 0.3, 0.05, 200 },
        { "sample-shading-grid.pdf", 0.3, 0.05, 600 },
        { "sample-tiling-grid.pdf", 0.3, 0.05, 3000 },
        { "sample-mesh-patches.pdf", 0.3, 0.05, 48 },
    };

    [Theory]
    [MemberData(nameof(PaintPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page);

        // A mesh is rasterized by the backend, and says so; nothing else is left out or approximated.
        Assert.All(device.Diagnostics(fileName, page), code => Assert.Equal("BackendMeshShadingRasterized", code));
        GoldenCorpus.AssertMatchesReference("android", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Synthetic_page_matches_the_software_rasterizer(string sample, double edges, double flat, int pixels)
    {
        AndroidDevice device = AndroidDevice.Run();
        using PdfDocument document = PdfDocument.Open(DeviceProtocol.Sample(sample));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions());

        GoldenImage actual = device.Render(sample, 0);

        Assert.All(device.Diagnostics(sample, 0), code => Assert.Equal("BackendMeshShadingRasterized", code));
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), actual, new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData("image-smask.pdf")]
    [InlineData("image-stencil-mask.pdf")]
    [InlineData("inline-image.pdf")]
    [InlineData("sample-image-heavy.pdf")]
    [InlineData("sample-mask-heavy.pdf")]
    public void Images_are_converted_once_per_backend_across_renders_and_kept_within_the_budget(string fileName)
    {
        AndroidDevice device = AndroidDevice.Run();

        int once = device.ImagesCreated(fileName, 1);
        int twice = device.ImagesCreated(fileName, 2);

        Assert.True(once > 0, $"{fileName} made no bitmap.");
        Assert.Equal(once, twice);
        Assert.InRange(device.CachedImageBytes(fileName), 1, PdfOptions.DefaultImageCacheBudget);
    }

    [Theory]
    [MemberData(nameof(UncachedPages))]
    public void A_zero_image_budget_keeps_nothing_and_draws_the_same(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage cached = device.Render(fileName, page);
        GoldenImage uncached = device.Render(fileName, page, DeviceProtocol.Uncached);

        Assert.Equal(0, device.CachedImageBytes(fileName, DeviceProtocol.Uncached));
        Assert.True(device.ImagesCreated(fileName, 1, DeviceProtocol.Uncached) >= device.ImagesCreated(fileName, 1));
        GoldenComparison comparison = GoldenComparison.Compare(cached, uncached, new GoldenThreshold(0, 0, 0));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData("pattern-tiling-colored.pdf")]
    [InlineData("pattern-tiling-uncolored.pdf")]
    [InlineData("pattern-in-form.pdf")]
    [InlineData("sample-tiling-grid.pdf")]
    public void A_tiling_pattern_cell_is_rendered_once_per_backend_and_scale(string fileName)
    {
        AndroidDevice device = AndroidDevice.Run();

        int once = device.CellsRecorded(fileName, 1);
        int twice = device.CellsRecorded(fileName, 2);

        Assert.True(once > 0, $"{fileName} rendered no pattern cell.");
        Assert.Equal(once, twice);
    }

    public static TheoryData<string, int> UncachedPages() => new()
    {
        { "image-smask.pdf", 0 },
        { "sample-image-heavy.pdf", 0 },
        { "sample-mask-heavy.pdf", 0 },
    };
}
