using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// Transparency groups and soft masks on the CoreGraphics backend (issue #128: ISO 32000-2 §11.4, §11.5, §11.6.5, §11.6.6,
/// §11.7.4.4): the transparency corpus pages against the software rasterizer's golden images under the <c>coregraphics</c> rows of
/// <see cref="GoldenThresholds"/>, the groups handed to the software rasterizer reported once per kind per page, synthetic group and
/// soft-mask pages and the Ghent transparency patches against the rasterizer.
/// </summary>
public sealed class TransparencyTests
{
    private const string Knockout = "BackendKnockoutGroupRasterized";
    private const string ColorSpace = "BackendGroupColorSpaceRasterized";
    private const string NonIsolated = "BackendNonIsolatedGroupRasterized";
    private const string NonIsolatedApproximated = "BackendNonIsolatedGroupApproximated";
    private const string SoftMask = "BackendSoftMaskRasterized";
    private const string BlendApproximated = "BackendBlendModeApproximated";

    [Theory]
    [MemberData(nameof(TransparencyPages))]
    public void A_transparency_page_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);

        GoldenImage render = PaintGoldenImageTests.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions(), out IReadOnlyList<Diagnostic> diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code is "BackendTransparencyGroupApproximated" or "BackendSoftMaskIgnored" or NonIsolatedApproximated);
        GoldenCorpus.AssertMatchesReference("coregraphics", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(ReportedPages))]
    public void Each_kind_of_group_the_software_rasterizer_draws_is_reported_once_per_page(string fileName, int page, string codes)
    {
        // transparency-groups page 1: the explicit knockout groups of rows 1 and 2 and the DeviceCMYK group of row 3 that multiplies;
        // the implicit knockout group of row 4's b and the isolated groups are CoreGraphics layers, the non-isolated groups whose
        // content does not blend are drawn directly. Page 2: the DeviceCMYK page group, whose discs multiply. Soft-mask values always
        // come from the software rasterizer.
        using PdfDocument document = GoldenCorpus.Open(fileName);

        _ = PaintGoldenImageTests.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions(), out IReadOnlyList<Diagnostic> diagnostics);

        Assert.Equal(codes.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal));
        Assert.All(diagnostics, diagnostic => Assert.True(diagnostic.Severity == DiagnosticSeverity.Information || diagnostic.Code == BlendApproximated, diagnostic.Code));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_synthetic_transparency_page_renders_as_the_software_rasterizer_renders_it(string sample, double edges, double flat, int pixels)
    {
        using PdfDocument document = PdfDocument.Open(Sample(sample));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);

        GoldenImage actual = PaintGoldenImageTests.Render(document.Pages[0], options, out _);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), actual, new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Fact]
    public void A_non_isolated_group_whose_paints_blend_in_one_mode_is_rasterized_and_reported_once()
    {
        using PdfDocument document = PdfDocument.Open(NonIsolatedSample());

        _ = PaintGoldenImageTests.Render(document.Pages[0], GoldenCorpus.CreateRenderOptions(), out IReadOnlyList<Diagnostic> diagnostics);

        Assert.Equal(NonIsolated, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void A_repeated_render_of_a_masked_page_reuses_its_mask_values_and_draws_the_same_pixels()
    {
        using PdfDocument document = GoldenCorpus.Open("transparency-soft-masks.pdf");
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        PdfPage page = document.Pages[0];
        PixelSize size = options.GetPixelSize(page);
        using var renderer = new CoreGraphicsRenderer();
        using var first = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] firstPixels, out _);
        using var second = BitmapRendering.CreateContext(size.Width, size.Height, out byte[] secondPixels, out _);

        renderer.Render(page, first, options);
        renderer.Render(page, second, options);
        first.Flush();
        second.Flush();

        Assert.True(firstPixels.AsSpan().SequenceEqual(secondPixels));
    }

    [Theory]
    [MemberData(nameof(GhentPatches))]
    [Trait("Category", "Corpus")]
    public void A_ghent_transparency_patch_renders_as_the_software_rasterizer_renders_it(string name, double edges, double flat, int pixels)
    {
        string? path = RealWorldCorpus.Files("gwg-output-suite").FirstOrDefault(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal));
        Assert.SkipWhen(path is null, "The gwg-output-suite corpus is not fetched (BROADSIDE_CORPUS_DIR).");
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(path!)));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);

        GoldenImage actual = PaintGoldenImageTests.Render(document.Pages[0], options, out _);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), actual, new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    /// <summary>
    /// The group and soft-mask pages. transparency-blend-modes.pdf is left out: its ColorDodge, ColorBurn and SoftLight are
    /// CoreGraphics' own functions (reported, <see cref="CompositingTests"/>), not groups or masks.
    /// </summary>
    public static TheoryData<string, int> TransparencyPages() =>
        GoldenCorpus.PagesOf("transparency-groups.pdf", "transparency-soft-masks.pdf", "extgstate-params.pdf");

    /// <summary>The pages and the diagnostics (comma-separated codes) a render of each reports.</summary>
    public static TheoryData<string, int, string> ReportedPages() => new()
    {
        { "transparency-blend-modes.pdf", 0, BlendApproximated },
        { "transparency-groups.pdf", 0, Knockout + "," + ColorSpace },
        { "transparency-groups.pdf", 1, ColorSpace },
        { "transparency-soft-masks.pdf", 0, SoftMask },
        { "extgstate-params.pdf", 0, SoftMask },
    };

    /// <summary>
    /// The synthetic pages and the threshold each meets against the rasterizer. group-grid: three cells differ, 36 974 pixels at most
    /// 0.17 apart. Two are Skia's (#124): the non-isolated group of Multiply paints itself painted in Multiply (§11.4.8), drawn by the
    /// software rasterizer against a transparent backdrop, and the tiling pattern whose cell blends, an isolated tile here (#126) and
    /// the non-isolated group of §11.6.7 NOTE 2 in the rasterizer. The third is the non-isolated group at alpha 0.5 holding a Multiply
    /// group: CoreGraphics has no layer that starts from the backdrop (Skia's InitializeWithPrevious), so it is rasterized against a
    /// transparent backdrop as well (reported as <c>BackendNonIsolatedGroupApproximated</c>). Every other cell, the knockout, gray,
    /// CMYK, nested and clipped groups and the fill and stroke's knockout layer, matches within the default.
    /// </summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "group-grid", 0.3, 0.05, 40000 },
        { "soft-mask-grid", 0.3, 0.05, 0 },
        { "non-isolated", 0.3, 0.05, 0 },
    };

    private static byte[] Sample(string name) => name switch
    {
        "group-grid" => GroupSamples.Grid(),
        "soft-mask-grid" => SoftMaskSamples.Grid(),
        "non-isolated" => NonIsolatedSample(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// A non-isolated group of Multiply paints at <c>/ca 0.5</c> (§11.4.8) over an opaque backdrop, and the same group inside an
    /// isolated group over a translucent backdrop of its own (a backdrop whose alpha is below 1).
    /// </summary>
    private static byte[] NonIsolatedSample() => ContentSamples.OnePage(
        "0 0.8 0.2 rg 20 20 572 300 re f q /H gs 1 0 0 1 40 40 cm /N Do Q q 1 0 0 1 320 40 cm /I Do Q"u8.ToArray(),
        "/ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R /I 6 0 R >>",
        ContentSamples.Stream(
            "/M gs 1 0 0 rg 20 20 120 120 re f 0 0 1 rg 70 70 120 120 re f",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /M << /BM /Multiply >> >> >> /Group << /S /Transparency >>"),
        ContentSamples.Stream(
            "/H gs 1 1 0 rg 0 0 200 200 re f /N Do",
            "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /H << /ca 0.5 >> >> /XObject << /N 5 0 R >> >> /Group << /S /Transparency /I true >>"));

    /// <summary>The Ghent Output Suite 5.0 transparency and soft-mask patches and the threshold each meets against the rasterizer.</summary>
    public static TheoryData<string, double, double, int> GhentPatches() => new()
    {
        { "GWG160_", 0.3, 0.05, 0 },
        { "GWG161_Transp_Basic_BM_DeviceCMYK", 0.3, 0.05, 0 },
        { "GWG161_Transp_Basic_BM_ICCBasedRGB", 0.3, 0.05, 0 },
        { "GWG162_", 0.3, 0.05, 0 },
        { "GWG164_", 0.3, 0.05, 0 },
        { "GWG1610_", 0.3, 0.05, 0 },
        { "GWG1611_", 0.3, 0.05, 0 },
    };
}
