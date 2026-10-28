using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// Issue #428: the MAUI viewer's Skia tiles are for display, so by default they decode a large photograph at the resolution it is
/// drawn at (two samples per device pixel, configurable), within the page tolerance of a full decode.
/// </summary>
public sealed class SkiaTileBackendTests
{
    [Fact]
    public void A_tile_decodes_the_cover_at_the_resolution_it_is_drawn_unless_told_otherwise()
    {
        (byte[] reduced, long reducedBytes) = Tile(new SkiaTileBackend());
        (byte[] full, long fullBytes) = Tile(new SkiaTileBackend(scaledImageDecode: false, RenderOptions.DefaultImageSamplesPerPixel));
        (_, long coarserBytes) = Tile(new SkiaTileBackend(scaledImageDecode: true, imageSamplesPerPixel: 1));

        // 2550 x 3150 RGB samples drawn 390 pixels wide: a half keeps two samples per pixel, a quarter one.
        // The cache charges pooled buffers and more than the samples, so compare sizes: each step keeps fewer than half the bytes.
        Assert.True(fullBytes >= 2550L * 3150 * 3, $"{fullBytes} bytes in full");
        Assert.True(reducedBytes * 3 < fullBytes, $"{reducedBytes} bytes at a half, {fullBytes} in full");
        Assert.True(coarserBytes * 2 < reducedBytes, $"{coarserBytes} bytes at a quarter, {reducedBytes} at a half");
        ScaledImagePages.AssertWithinTolerance(full, reduced, "cover tile");
    }

    [Fact]
    public void The_samples_per_pixel_must_be_at_least_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SkiaTileBackend(true, 0));

    /// <summary>Draws the top-left 256-pixel tile of the cover at 390 pixels wide; returns its pixels and the decoded bytes the document keeps.</summary>
    private static (byte[] Pixels, long Decoded) Tile(SkiaTileBackend backend)
    {
        using (backend)
        {
            using PdfDocument document = PdfDocument.Open(CatalogueSamples.Cover(), new PdfOptions().WithDisplayListCacheBudget(0));
            PdfPage page = document.Pages[0];
            Matrix device = new RenderOptions().WithScale(390 / 612.0).GetDeviceTransform(page);
            DisplayList list = page.CreateDisplayList();
            using var target = new Bitmap(256, 256);
            target.Pixels.Fill(byte.MaxValue);
            backend.PrefetchImagesAsync(list, device, 256, 256, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            using (ITileRender render = backend.Begin(list, target, device, CancellationToken.None))
            {
                Assert.True(render.RenderSlice(Timeout.InfiniteTimeSpan));
            }

            return (target.Pixels.ToArray(), document.CacheStatistics.Images.Bytes);
        }
    }
}
