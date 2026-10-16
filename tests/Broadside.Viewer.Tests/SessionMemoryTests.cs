using System.Runtime.CompilerServices;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

[Collection(HeavyTestCollection.Name)]
public sealed class SessionMemoryTests
{
    private const string FileName = "paths-fill-rules.pdf";

    [Fact]
    public void A_closed_session_and_its_document_are_collected()
    {
        (WeakReference session, WeakReference document) = OpenRenderAndClose();

        for (int i = 0; i < 3 && (session.IsAlive || document.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(session.IsAlive, "The session is still reachable after closing.");
        Assert.False(document.IsAlive, "The document is still reachable after closing.");
    }

    [Fact]
    public void Opening_rendering_and_closing_repeatedly_retains_nothing()
    {
        for (int i = 0; i < 5; i++)
        {
            OpenRenderAndClose();
        }

        long before = GC.GetTotalMemory(forceFullCollection: true);
        for (int i = 0; i < 40; i++)
        {
            OpenRenderAndClose();
        }

        long growth = GC.GetTotalMemory(forceFullCollection: true) - before;

        // Each cycle builds a list and draws a 4 MB tile; anything kept per cycle would add up to megabytes.
        Assert.True(growth < 512 * 1024, $"Retained {growth} bytes over 40 sessions.");
    }

    [Fact]
    public void A_tile_rendered_from_the_cached_list_takes_its_pixels_from_the_pool()
    {
        // Cooperative with a completed yield runs synchronously on this thread, so every allocation of the render is counted here.
        ViewerOptions options = ViewerTestSupport.OptionsFor(FileName, ViewerThreading.Cooperative)
            .WithSliceBudget(Timeout.InfiniteTimeSpan)
            .UseYield(_ => ValueTask.CompletedTask);
        using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), options);
        var tile = new Tile(0, 4, new Rendering.PixelRectangle(0, 0, 1024, 1024));
        var surface = new NullSurface();

        long allocated = Allocations.Measure(() => session.RenderTileAsync(tile, surface).GetAwaiter().GetResult());

        // A 1024 x 1024 tile is 4 MiB of pixels; what remains is the bookkeeping of one asynchronous render.
        Assert.True(allocated < 8 * 1024, $"{allocated} bytes per tile.");
        Assert.Equal(0, session.Statistics.PixelBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Session, WeakReference Document) OpenRenderAndClose()
    {
        ViewerOptions options = ViewerTestSupport.OptionsFor(FileName, ViewerThreading.Cooperative).UseYield(_ => ValueTask.CompletedTask);
        DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), options);
        session.RenderTileAsync(new Tile(0, 4, new Rendering.PixelRectangle(0, 0, 1024, 1024)), new NullSurface()).GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Assert.Equal(new SessionStatistics(0, 0, 1, 0), session.Statistics);
        return (new WeakReference(session), new WeakReference(session.Document));
    }
}
