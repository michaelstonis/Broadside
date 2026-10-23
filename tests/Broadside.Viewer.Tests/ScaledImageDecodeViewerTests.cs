using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// Issue #362 in the viewer: a document session's tiles are for display, so its default backend decodes images at the resolution
/// they are drawn at (two samples per device pixel unless the options say otherwise), and the options can turn it off.
/// </summary>
public sealed class ScaledImageDecodeViewerTests
{
    // A 256 x 256 image over 512 x 512 points: at a sixteenth, 2 samples per pixel keep a quarter (reduction 2).
    private const string Paint = "q 512 0 0 512 50 100 cm /Im0 Do Q";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_session_decodes_images_at_the_resolution_its_tiles_draw_them(ViewerThreading threading)
    {
        var codec = new ReducingImageFilter();
        await using DocumentSession session = DocumentSession.Open(Page(), Options(codec, threading));
        using var surface = new HeadlessSurface();

        await session.RenderPageAsync(0, 0.0625, surface, Token);

        Assert.Equal(1, codec.DecodesAt(2));
        Assert.Equal(1, codec.Decodes);
    }

    [Fact]
    public async Task The_options_set_the_samples_per_pixel_or_turn_scaled_decode_off()
    {
        var coarse = new ReducingImageFilter();
        var full = new ReducingImageFilter();
        await using DocumentSession one = DocumentSession.Open(Page(), Options(coarse, ViewerThreading.Cooperative).WithImageSamplesPerPixel(1));
        await using DocumentSession off = DocumentSession.Open(Page(), Options(full, ViewerThreading.Cooperative).WithScaledImageDecode(false));
        using var surface = new HeadlessSurface();

        await one.RenderPageAsync(0, 0.125, surface, Token);
        await off.RenderPageAsync(0, 0.0625, surface, Token);

        Assert.Equal(1, coarse.DecodesAt(2));
        Assert.Equal(1, full.DecodesAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerOptions().WithImageSamplesPerPixel(0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewerOptions().WithImageSamplesPerPixel(double.NaN));
        Assert.True(new ViewerOptions().ScaledImageDecode);
        Assert.Equal(2, new ViewerOptions().ImageSamplesPerPixel);
    }

    private static byte[] Page() => ReducingImageFilter.Page(ReducingImageFilter.Photo(256, 256, 1), 256, 256, "/DeviceGray", Paint);

    private static ViewerOptions Options(ReducingImageFilter codec, ViewerThreading threading) =>
        new ViewerOptions().WithDocumentOptions(new PdfOptions().UseFilter(codec)).WithThreading(threading);
}
