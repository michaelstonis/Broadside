using Broadside.GoldenImages;
using Broadside.Rendering.Android.Device;
using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// The Android backend (issues #133, #135) against the software rasterizer's golden images: every page of the path corpus files and of
/// the text corpus files rendered through the Android Canvas on a device or emulator at the golden images' 72 DPI, glyphs natively and
/// with forced outlines, and its replay allocating nothing once warm.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.3 (filling and stroking), §8.5.4 (clipping), §8.4.3 (line parameters), §10.7.5 (stroke adjustment), §9.3.6 and
/// §9.4.3 (text), §9.9 (embedded font programs); spec #33 Testing Decisions: a backend is measured against the rasterizer within a
/// perceptual threshold, never by its own snapshots. Thresholds: paths and forced outlines under the golden table's (the default,
/// edges 0.3, flat 0.05, no pixel beyond); native glyphs under <see cref="GoldenThreshold.Text"/> (edges 0.8, flat 0.05, 8 pixels);
/// forced outlines against the device's own path rendering of the same outlines under <see cref="GoldenThreshold.Tight"/> (0.01
/// everywhere, no pixel beyond).
/// </remarks>
public sealed class AndroidBackendTests
{
    /// <summary>The path corpus files of #133: the three path files and the page mapping (rotation, UserUnit, CropBox).</summary>
    internal static readonly string[] PathFiles = ["paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "page-rotation.pdf"];

    /// <summary>Every text corpus file (<c>text-*.pdf</c>), issue #135.</summary>
    internal static readonly string[] TextFiles = [.. Corpus.AllFileNames.Where(name => name.StartsWith("text-", StringComparison.Ordinal))];

    /// <summary>The corpus files the device renders.</summary>
    internal static readonly string[] Files = [.. PathFiles, .. TextFiles];

    /// <summary>
    /// The pages of the text corpus files that also paint an image or a stencil mask, which this backend does not draw yet (#134): they
    /// cannot match their golden images until it does.
    /// </summary>
    private static readonly (string File, int Page)[] PagesWithImages =
    [
        ("text-render-modes.pdf", 1),
        ("text-render-modes.pdf", 2),
        ("text-render-modes.pdf", 3),
        ("text-render-modes.pdf", 4),
        ("text-tagged-spanning-image.pdf", 0),
        ("text-type3.pdf", 0),
    ];

    public static TheoryData<string, int> PathPages() => GoldenCorpus.PagesOf(PathFiles);

    public static TheoryData<string, int> TextPages() => Without(GoldenCorpus.PagesOf(TextFiles), PagesWithImages);

    public static TheoryData<string, int> AllPages() => GoldenCorpus.PagesOf(Files);

    [Theory]
    [MemberData(nameof(PathPages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page);

        GoldenCorpus.AssertMatchesReference("android", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(TextPages))]
    public void Text_page_drawn_with_native_glyphs_matches_the_golden_image_within_the_text_threshold(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page);

        // A program Android's font engine cannot draw (or reads differently) is drawn from its outlines and reported; nothing else.
        Assert.All(device.Diagnostics(fileName, page), code => Assert.Equal("BackendNativeGlyphsUnavailable", code));
        GoldenCorpus.AssertMatchesReference("android", fileName, page, render, GoldenThreshold.Text);
    }

    [Theory]
    [MemberData(nameof(TextPages))]
    public void Text_page_drawn_from_outlines_matches_the_golden_image(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page, DeviceProtocol.Outlines);

        Assert.Empty(device.Diagnostics(fileName, page, DeviceProtocol.Outlines));
        GoldenCorpus.AssertMatchesReference("android-outlines", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(TextPages))]
    public void Forced_outlines_match_the_android_path_rendering_of_the_same_outlines_within_the_tight_threshold(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();
        if (device.PathReference(fileName, page) is { } refused)
        {
            Assert.Skip($"The path reference does not draw this page: {refused}");
        }

        GoldenImage outlines = device.Render(fileName, page, DeviceProtocol.Outlines);
        GoldenImage paths = device.Render(fileName, page, DeviceProtocol.Paths);

        GoldenComparison comparison = GoldenComparison.Compare(paths, outlines, GoldenThreshold.Tight);
        Assert.True(comparison.Matches, comparison.Describe());
    }

    [Theory]
    [InlineData("text-truetype-embedded.pdf")]
    [InlineData("text-truetype-composite.pdf")]
    [InlineData("text-opentype-cff-embedded.pdf")]
    [InlineData("text-cid-identity-h.pdf")]
    public void Glyphs_of_an_embedded_TrueType_or_OpenType_program_are_drawn_by_the_font_engine_unless_outlines_are_forced(string fileName)
    {
        AndroidDevice device = AndroidDevice.Run();

        int native = device.NativeGlyphs(fileName, 0);
        int forced = device.NativeGlyphs(fileName, 0, DeviceProtocol.Outlines);

        Assert.Empty(device.Diagnostics(fileName, 0));
        Assert.True(native > 0, $"{fileName} drew no glyph through Android's font engine.");
        Assert.Equal(0, forced);
    }

    [Theory]
    [InlineData("text-truetype-embedded.pdf", 1)]
    [InlineData("text-truetype-composite.pdf", 1)]
    [InlineData("text-opentype-cff-embedded.pdf", 1)]
    [InlineData("text-cid-identity-h.pdf", 1)]
    [InlineData("text-type1-embedded.pdf", 0)]
    [InlineData("text-cff-embedded.pdf", 0)]
    [InlineData("text-type3.pdf", 0)]
    public void An_android_font_is_built_once_per_TrueType_or_OpenType_program_per_backend(string fileName, int fonts)
    {
        AndroidDevice device = AndroidDevice.Run();

        Assert.Equal(fonts, device.FontsCreated(fileName));
        Assert.Equal(fonts > 0, device.NativeGlyphs(fileName, 0) > 0);
    }

    [Theory]
    [MemberData(nameof(AllPages))]
    public void Replaying_the_page_allocates_nothing_once_warm(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();
        Assert.True(device.AllocationProbeBytes() > 0, "The device's runtime reports no allocation for a new array, so a zero would prove nothing.");

        long native = device.ReplayAllocatedBytes(fileName, page);
        long outlines = device.ReplayAllocatedBytes(fileName, page, DeviceProtocol.Outlines);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{fileName} page {page}: native {device.ReplayMicroseconds(fileName, page):F1} us, {native} B; " +
            $"outlines {device.ReplayMicroseconds(fileName, page, DeviceProtocol.Outlines):F1} us, {outlines} B");
        Assert.Equal(0, native);
        Assert.Equal(0, outlines);
    }

    private static TheoryData<string, int> Without(TheoryData<string, int> pages, (string File, int Page)[] excluded)
    {
        var kept = new TheoryData<string, int>();
        foreach (TheoryDataRow<string, int> row in pages)
        {
            if (!excluded.Contains(row.Data))
            {
                kept.Add(row.Data.Item1, row.Data.Item2);
            }
        }

        return kept;
    }
}
