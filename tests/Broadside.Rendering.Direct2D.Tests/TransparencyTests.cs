using Broadside.Diagnostics;
using Broadside.GoldenImages;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Transparency groups and soft masks on the Direct2D backend (issue #132: ISO 32000-2 §11.3.5, §11.4, §11.5, §11.6.5, §11.6.6,
/// §11.7.4.4): the transparency corpus pages against the software rasterizer's golden images under the <c>direct2d</c> rows of
/// <see cref="GoldenThresholds"/>, the groups and pages handed to the software rasterizer reported once per kind per render, and
/// synthetic group and soft-mask pages and the Ghent transparency patches against the rasterizer.
/// </summary>
public sealed class TransparencyTests
{
    private const string Knockout = "BackendKnockoutGroupRasterized";
    private const string ColorSpace = "BackendGroupColorSpaceRasterized";
    private const string Blending = "BackendBlendingGroupRasterized";
    private const string SoftMask = "BackendSoftMaskRasterized";
    private const string Page = "BackendPageRasterized";

    [Theory]
    [MemberData(nameof(TransparencyPages))]
    public void A_transparency_page_matches_its_golden_image(string fileName, int page)
    {
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.DoesNotContain(renderer.Diagnostics, diagnostic => diagnostic.Code is "BackendTransparencyGroupApproximated" or "BackendSoftMaskIgnored" or "BackendBlendModeUnsupported");
        GoldenCorpus.AssertMatchesReference("direct2d", fileName, page, GoldenImage.FromBitmap(bitmap));
    }

    [Theory]
    [MemberData(nameof(ReportedPages))]
    public void Each_kind_of_group_or_page_the_software_rasterizer_draws_is_reported_once_per_render(string fileName, int page, string codes)
    {
        // transparency-blend-modes and extgstate-params paint in blend modes other than Normal, and transparency-groups page 1 holds
        // non-isolated groups whose discs multiply: Direct2D cannot blend with the backdrop, so the software rasterizer draws those
        // pages (the soft mask extgstate-params sets is drawn with them). Page 2's page group is isolated and DeviceCMYK, and its
        // discs multiply. Soft-mask values always come from the software rasterizer.
        using PdfDocument document = GoldenCorpus.Open(fileName);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap bitmap = renderer.Render(document.Pages[page], GoldenCorpus.CreateRenderOptions());

        Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), renderer.Diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal));
        Assert.All(renderer.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public void A_group_renders_as_the_software_rasterizer_renders_it_and_reports_how(string group, string codes)
    {
        using PdfDocument document = PdfDocument.Open(GroupSample(group));
        PdfPage page = document.Pages[0];
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(page, options);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap actual = renderer.Render(page, options);

        Assert.Equal(codes.Split(',', StringSplitOptions.RemoveEmptyEntries), renderer.Diagnostics.Select(diagnostic => diagnostic.Code));
        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), GoldenImage.FromBitmap(actual), GoldenThreshold.Default);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_synthetic_transparency_page_renders_as_the_software_rasterizer_renders_it(string sample, double edges, double flat, int pixels)
    {
        using PdfDocument document = PdfDocument.Open(Sample(sample));
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap expected = rasterizer.Render(document.Pages[0], options);
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap actual = renderer.Render(document.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), GoldenImage.FromBitmap(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Fact]
    public void A_repeated_render_of_a_masked_page_reuses_its_mask_values_and_draws_the_same_pixels()
    {
        using PdfDocument document = GoldenCorpus.Open("transparency-soft-masks.pdf");
        RenderOptions options = GoldenCorpus.CreateRenderOptions();
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap first = renderer.Render(document.Pages[0], options);
        using Bitmap second = renderer.Render(document.Pages[0], options);

        Assert.True(first.Pixels.SequenceEqual(second.Pixels));
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
        using var renderer = new Direct2DRenderer(Direct2DDriver.Warp);

        using Bitmap actual = renderer.Render(document.Pages[0], options);

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), GoldenImage.FromBitmap(actual), new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    /// <summary>The transparency corpus pages: groups, soft masks, the blend modes and the graphics state parameters.</summary>
    public static TheoryData<string, int> TransparencyPages() =>
        GoldenCorpus.PagesOf("transparency-groups.pdf", "transparency-soft-masks.pdf", "transparency-blend-modes.pdf", "extgstate-params.pdf");

    /// <summary>The pages and the diagnostics (comma-separated codes) a render of each reports.</summary>
    public static TheoryData<string, int, string> ReportedPages() => new()
    {
        { "transparency-blend-modes.pdf", 0, Page },
        { "transparency-groups.pdf", 0, Page },
        { "transparency-groups.pdf", 1, ColorSpace },
        { "transparency-soft-masks.pdf", 0, SoftMask },
        { "extgstate-params.pdf", 0, Page },
    };

    /// <summary>The single-group pages (see <see cref="GroupSample"/>) and what a render of each reports.</summary>
    public static TheoryData<string, string> Groups() => new()
    {
        { "layer", string.Empty },
        { "masked-layer", SoftMask },
        { "isolated-blending", Blending },
        { "knockout", Knockout },
        { "non-isolated-blending", Page },
        { "group-blend-mode", Page },
    };

    /// <summary>The synthetic pages and the threshold each meets against the rasterizer.</summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "group-grid", 0.3, 0.05, 0 },
        { "soft-mask-grid", 0.3, 0.05, 0 },
    };

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

    private static byte[] Sample(string name) => name switch
    {
        "group-grid" => GroupSamples.Grid(),
        "soft-mask-grid" => SoftMaskSamples.Grid(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// A page with one group XObject over a green backdrop, drawn under <c>/H</c> (<c>/ca 0.5</c>, and a luminosity soft mask for
    /// <c>masked-layer</c>, a <c>/BM /Multiply</c> for <c>group-blend-mode</c>): a red and a blue square that overlap, painted in
    /// Multiply in the blending groups.
    /// </summary>
    private static byte[] GroupSample(string name)
    {
        (string state, string group, string paint) = name switch
        {
            "layer" => ("/ca 0.5", "/S /Transparency", string.Empty),
            "masked-layer" => ("/SMask << /S /Luminosity /G 6 0 R >>", "/S /Transparency /I true", string.Empty),
            "isolated-blending" => ("/ca 0.5", "/S /Transparency /I true", "/M gs "),
            "knockout" => ("/ca 1", "/S /Transparency /K true", "/A gs "),
            "non-isolated-blending" => ("/ca 1", "/S /Transparency", "/M gs "),
            "group-blend-mode" => ("/BM /Multiply", "/S /Transparency /I true", string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        return ContentSamples.OnePage(
            "0 0.8 0.2 rg 20 20 572 300 re f q /H gs 1 0 0 1 40 40 cm /N Do Q"u8.ToArray(),
            "/ExtGState << /H << " + state + " >> >> /XObject << /N 5 0 R >>",
            ContentSamples.Stream(
                paint + "1 0 0 rg 20 20 120 120 re f 0 0 1 rg 70 70 120 120 re f",
                "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Resources << /ExtGState << /M << /BM /Multiply >> /A << /ca 0.5 >> >> >> /Group << " + group + " >>"),
            ContentSamples.Stream(
                "/Sh sh",
                "/Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Resources << /Shading << /Sh << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >> >>"));
    }
}
