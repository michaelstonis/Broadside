using System.Diagnostics;
using Broadside.Images;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// Issue #372 in the viewer: before a tile is drawn, the session awaits its backend's image prefetch, so a platform image decoder
/// decodes the tile's photograph off the frame (the cooperative scheduler keeps returning from its frames meanwhile), at the reduction
/// the software backend then asks for, and the tile shows the platform's samples.
/// </summary>
public sealed class PlatformImageViewerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_rendered_page_shows_what_the_platform_decoded_at_the_reduction_the_backend_draws(ViewerThreading threading)
    {
        var decoder = new GreenDecoder();
        await using DocumentSession session = DocumentSession.Open(CatalogueSamples.Cover(1024, 1024), Options(decoder, threading));
        using var surface = new HeadlessSurface();

        await session.RenderPageAsync(0, 0.25, surface, Token);

        Assert.InRange(decoder.Calls, 1, 3); // more only when the process-wide cache epoch moved meanwhile (parallel tests)
        Assert.Equal(1, decoder.LastReduction); // 1024 samples over 153 px with 2 samples per pixel: 1/2 (capped at 1/2 for an ICC space)
        AssertGreen(Assert.Single(surface.Tiles).Pixels);
    }

    [Fact]
    public async Task The_cooperative_scheduler_keeps_its_frames_short_while_the_platform_decodes_then_draws_the_tile()
    {
        var decoder = new GreenDecoder { Delay = TimeSpan.FromMilliseconds(150) };
        await using DocumentSession session = DocumentSession.Open(CatalogueSamples.Cover(1024, 1024), Options(decoder, ViewerThreading.Cooperative));
        using var surface = new HeadlessSurface();
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions().WithRenderAhead(0).WithNearMargin(0));
        scheduler.Update(new Viewport(0, 0, 300, 300, 0.25));

        int frames = 0;
        long started = Stopwatch.GetTimestamp();
        while (!scheduler.IsIdle && Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
        {
            scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
            frames++;
            await Task.Delay(4, Token);
        }

        Assert.True(scheduler.IsIdle);
        Assert.True(frames > 5, $"The scheduler waited for the decode inside a frame ({frames} frames).");
        // Once, unless a COS change elsewhere in the process made the document's caches stale meanwhile (the cache epoch is
        // process-wide): the resumed tile then takes a new list and decodes again.
        Assert.InRange(decoder.Calls, 1, 3);
        Assert.All(surface.Tiles, tile => AssertGreen(tile.Pixels));
    }

    private static void AssertGreen(Bitmap pixels)
    {
        // Premultiplied BGRA; the photograph covers the page, so the middle of the tile is image.
        ReadOnlySpan<byte> pixel = pixels.GetRow(pixels.Height / 2).Slice((pixels.Width / 2) * 4, 4);
        Assert.True(pixel[1] > 200 && pixel[0] < 60 && pixel[2] < 60, $"Expected the platform's green, got B {pixel[0]} G {pixel[1]} R {pixel[2]}.");
    }

    private static ViewerOptions Options(GreenDecoder decoder, ViewerThreading threading) =>
        new ViewerOptions().WithDocumentOptions(new PdfOptions().UsePlatformImageDecoder(decoder)).WithThreading(threading);

    /// <summary>A platform decoder that completes asynchronously with pure green samples.</summary>
    private sealed class GreenDecoder : IPlatformImageDecoder
    {
        private int _calls;

        public string Name => "green";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.None;

        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(1);

        public int Calls => Volatile.Read(ref _calls);

        public int LastReduction { get; private set; } = -1;

        public bool CanDecode(in PlatformImageRequest request) => true;

        public async ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastReduction = request.Reduction;
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            using DecodedImageBuilder builder = request.CreateImage(request.Reduction);
            Span<byte> samples = builder.Samples;
            for (int i = 0; i < samples.Length; i += 3)
            {
                samples[i] = 0;
                samples[i + 1] = 255;
                samples[i + 2] = 0;
            }

            return builder.Build();
        }
    }
}
