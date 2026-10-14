using Broadside.GoldenImages;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// The Android backend (issue #133) against the software rasterizer's golden images: every page of the path corpus files rendered
/// through the Android Canvas on a device or emulator at the golden images' 72 DPI, and its replay allocating nothing once warm.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.3 (filling and stroking), §8.5.4 (clipping), §8.4.3 (line parameters), §10.7.5 (stroke adjustment);
/// spec #33 Testing Decisions: a backend is measured against the rasterizer within a perceptual threshold, never by its own snapshots.
/// </remarks>
public sealed class AndroidBackendTests
{
    /// <summary>The corpus files the device renders: the three path files of the ticket, and the page mapping (rotation, UserUnit, CropBox).</summary>
    internal static readonly string[] Files = ["paths-fill-rules.pdf", "paths-strokes.pdf", "clipping-paths.pdf", "page-rotation.pdf"];

    public static TheoryData<string, int> Pages() => GoldenCorpus.PagesOf(Files);

    [Theory]
    [MemberData(nameof(Pages))]
    public void Page_matches_the_golden_image(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();

        GoldenImage render = device.Render(fileName, page);

        GoldenCorpus.AssertMatchesReference("android", fileName, page, render);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Replaying_the_page_allocates_nothing_once_warm(string fileName, int page)
    {
        AndroidDevice device = AndroidDevice.Run();
        Assert.True(device.AllocationProbeBytes() > 0, "The device's runtime reports no allocation for a new array, so a zero would prove nothing.");

        long allocated = device.ReplayAllocatedBytes(fileName, page);

        TestContext.Current.TestOutputHelper?.WriteLine($"{fileName} page {page}: {device.ReplayMicroseconds(fileName, page):F1} us per replay, {allocated} B");
        Assert.Equal(0, allocated);
    }
}
