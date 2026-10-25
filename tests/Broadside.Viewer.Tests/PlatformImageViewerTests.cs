using Broadside.Images;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// Issue #372 in the viewer: before a tile is drawn, the session awaits its backend's image prefetch, so a platform image decoder
/// decodes the tile's photograph off the frame (the cooperative scheduler keeps returning from its frames meanwhile), at the reduction
/// the software backend then asks for, and the tile shows the platform's samples.
/// </summary>
/// <remarks>
/// In the non-parallel collection: the document caches' epoch is process-wide, so a COS change in a parallel test would make the
/// cached image stale and a resumed tile decode again.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class PlatformImageViewerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_rendered_page_shows_what_the_platform_decoded_at_the_reduction_the_backend_draws(ViewerThreading threading)
    {
        var decoder = new GreenDecoder();
        decoder.Release();
        await using DocumentSession session = DocumentSession.Open(CatalogueSamples.Cover(1024, 1024), Options(decoder, threading));
        using var surface = new HeadlessSurface();

        await session.RenderPageAsync(0, 0.25, surface, Token);

        Assert.Equal(1, decoder.Calls);
        Assert.Equal(1, decoder.LastReduction); // 1024 samples over 153 px with 2 samples per pixel: 1/2 (capped at 1/2 for an ICC space)
        AssertGreen(Assert.Single(surface.Tiles).Pixels);
    }

    [Fact]
    public async Task The_cooperative_scheduler_keeps_its_frames_short_while_the_platform_decodes_then_draws_the_tile()
    {
        var decoder = new GreenDecoder();
        await using DocumentSession session = DocumentSession.Open(CatalogueSamples.Cover(1024, 1024), Options(decoder, ViewerThreading.Cooperative));
        using var surface = new HeadlessSurface();
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var scheduler = new TileScheduler(session, layout, surface, new TileSchedulerOptions { PlaceholderScale = 0 }.WithRenderAhead(0).WithNearMargin(0));
        scheduler.Update(new Viewport(0, 0, 700, 900, 1)); // 12 tiles wait for the one decode together

        // While the decode is held, frames return (they never wait on it) and nothing is drawn.
        for (int frame = 0; frame < 50 && decoder.Calls == 0; frame++)
        {
            Assert.True(scheduler.RunFrame(Timeout.InfiniteTimeSpan));
        }

        Assert.Equal(1, decoder.Calls);
        for (int frame = 0; frame < 10; frame++)
        {
            Assert.True(scheduler.RunFrame(Timeout.InfiniteTimeSpan), "the scheduler has the waiting tiles to draw");
        }

        Assert.Empty(surface.Tiles);
        Assert.True(session.LiveBackends <= 1, $"{session.LiveBackends} backends while 12 tiles wait"); // a waiting tile holds none

        decoder.Release();
        await decoder.Finished.Task.WaitAsync(Token);
        // The tiles resume once the decode's continuation has run on the thread pool; the deadline only bounds a broken run.
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            while (!scheduler.IsIdle && !deadline.IsCancellationRequested)
            {
                scheduler.RunFrame(Timeout.InfiniteTimeSpan);
                await Task.Delay(1, Token);
            }
        }

        Assert.True(scheduler.IsIdle);
        Assert.Equal(1, decoder.Calls);
        Assert.NotEmpty(surface.Tiles);
        Assert.All(surface.Tiles, tile => AssertGreen(tile.Pixels));

        // The tiles drew one after another with one backend, so the image was converted once.
        Assert.True(session.LiveBackends <= 1, $"{session.LiveBackends} backends");
    }

    private static void AssertGreen(Bitmap pixels)
    {
        // Premultiplied BGRA; the photograph covers the page, so the middle of the tile is image.
        ReadOnlySpan<byte> pixel = pixels.GetRow(pixels.Height / 2).Slice((pixels.Width / 2) * 4, 4);
        Assert.True(pixel[1] > 200 && pixel[0] < 60 && pixel[2] < 60, $"Expected the platform's green, got B {pixel[0]} G {pixel[1]} R {pixel[2]}.");
    }

    private static ViewerOptions Options(GreenDecoder decoder, ViewerThreading threading) =>
        new ViewerOptions().WithDocumentOptions(new PdfOptions().UsePlatformImageDecoder(decoder)).WithThreading(threading);

    /// <summary>A platform decoder that completes, with pure green samples, when the test releases it.</summary>
    private sealed class GreenDecoder : IPlatformImageDecoder
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public string Name => "green";

        public PlatformImageFidelity Fidelity => PlatformImageFidelity.Tolerance;

        public PlatformImageFeatures Features => PlatformImageFeatures.None;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public int LastReduction { get; private set; } = -1;

        public void Release() => _gate.TrySetResult();

        public bool CanDecode(in PlatformImageRequest request) => true;

        public async ValueTask<DecodedImage?> DecodeAsync(ReadOnlyMemory<byte> encoded, PlatformImageRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastReduction = request.Reduction;
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            using DecodedImageBuilder builder = request.CreateImage(request.Reduction);
            Span<byte> samples = builder.Samples;
            for (int i = 0; i < samples.Length; i += 3)
            {
                samples[i] = 0;
                samples[i + 1] = 255;
                samples[i + 2] = 0;
            }

            Finished.TrySetResult();
            return builder.Build();
        }
    }
}
