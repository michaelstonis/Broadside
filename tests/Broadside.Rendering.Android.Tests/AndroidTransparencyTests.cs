using Broadside.GoldenImages;
using Broadside.Rendering.Android.Device;
using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// Transparency groups and soft masks on the Android backend (issue #136): the transparency corpus pages against the software
/// rasterizer's golden images, the groups handed to the software rasterizer reported once per kind per page, the synthetic group and
/// soft-mask pages against the rasterizer, and masked and blending pages replayed without allocating once warm.
/// </summary>
/// <remarks>
/// ISO 32000-2 §11.4 (transparency groups), §11.5 and §11.6.5 (soft masks), §11.6.6 (group XObjects), §11.7.4.4 (the implicit knockout
/// group of a fill and stroke); spec #33 D34 (a group the backend cannot composite falls back to the software rasterizer with a
/// diagnostic).
/// </remarks>
public sealed class AndroidTransparencyTests
{
    private const string Knockout = "BackendKnockoutGroupRasterized";
    private const string ColorSpace = "BackendGroupColorSpaceRasterized";
    private const string NonIsolated = "BackendNonIsolatedGroupRasterized";
    private const string SoftMask = "BackendSoftMaskRasterized";

    /// <summary>The transparency corpus files: groups, soft masks, blend modes and the ExtGState parameters (with a soft mask).</summary>
    internal static readonly string[] TransparencyFiles = ["transparency-groups.pdf", "transparency-soft-masks.pdf", "transparency-blend-modes.pdf", "extgstate-params.pdf"];

    public static TheoryData<string, int> TransparencyPages() => GoldenCorpus.PagesOf(TransparencyFiles);

    /// <summary>The pages and the diagnostics (comma-separated codes) a render of each reports.</summary>
    public static TheoryData<string, int, string> ReportedPages() => new()
    {
        { "transparency-blend-modes.pdf", 0, string.Empty },
        { "transparency-groups.pdf", 0, Knockout + "," + ColorSpace },
        { "transparency-groups.pdf", 1, ColorSpace },
        { "transparency-soft-masks.pdf", 0, SoftMask },
        { "extgstate-params.pdf", 0, SoftMask },
        { "sample-non-isolated.pdf", 0, NonIsolated },
    };

    /// <summary>
    /// The synthetic pages and the threshold each meets against the rasterizer. group-grid: the same three cells as CoreGraphics (#128),
    /// the Android canvas having no layer that starts from the backdrop either: the non-isolated group of Multiply paints painted in
    /// Multiply (§11.4.8) and the non-isolated group at alpha 0.5 holding a Multiply group, both drawn by the software rasterizer against
    /// a transparent backdrop, and the tiling pattern whose cell blends, an isolated tile here (#134). Every other cell matches within the
    /// default.
    /// </summary>
    public static TheoryData<string, double, double, int> Samples() => new()
    {
        { "sample-group-grid.pdf", 0.3, 0.05, 40000 },
        { "sample-soft-mask-grid.pdf", 0.3, 0.05, 0 },
        { "sample-non-isolated.pdf", 0.3, 0.05, 0 },
    };

    [Theory]
    [MemberData(nameof(TransparencyPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page);

        Assert.DoesNotContain(device.Diagnostics(fileName, page), code => code is "BackendTransparencyGroupApproximated" or "BackendSoftMaskIgnored" or "BackendNonIsolatedGroupApproximated");
        GoldenCorpus.AssertMatchesReference("android", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(ReportedPages))]
    public void Each_kind_of_group_the_software_rasterizer_draws_is_reported_once_per_page(string fileName, int page, string codes)
    {
        // transparency-groups page 1: the explicit knockout groups of rows 1 and 2 and the DeviceCMYK group of row 3 that multiplies;
        // the implicit knockout group of row 4's b and the isolated groups are canvas layers, the non-isolated groups whose content
        // does not blend are drawn directly. Page 2: the DeviceCMYK page group, whose discs multiply. Soft-mask values always come from
        // the software rasterizer.
        AndroidDevice device = AndroidDevice.Run();

        IEnumerable<string> reported = device.Diagnostics(fileName, page);

        Assert.Equal(codes.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal), reported.Order(StringComparer.Ordinal));
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

        GoldenComparison comparison = GoldenComparison.Compare(GoldenImage.FromBitmap(expected), actual, new GoldenThreshold(edges, flat, pixels));
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData("transparency-soft-masks.pdf", 0)]
    [InlineData("transparency-blend-modes.pdf", 0)]
    [InlineData("extgstate-params.pdf", 0)]
    public void Replaying_a_masked_or_blending_page_allocates_nothing_once_warm(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();
        if (!device.IsOptimizedBuild)
        {
            Assert.Skip("The device app is a Debug build: Mono runs it without JIT optimizations, which allocate in shared generic code; build it with -c Release.");
        }

        Assert.True(device.AllocationProbeBytes() > 0, "The device's runtime reports no allocation for a new array, so a zero would prove nothing.");

        Assert.Equal(0, device.ReplayAllocatedBytes(fileName, page));
    }
}
