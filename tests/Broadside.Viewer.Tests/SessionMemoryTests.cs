using System.Runtime.CompilerServices;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// What a closed session keeps. Measured on the session's own objects and counters, never on process-wide memory: the rasterizer's
/// working arrays and the tile bitmaps live in <c>ArrayPool&lt;T&gt;.Shared</c>, which belongs to the process and which the runtime
/// trims on its own schedule, and other tests leave their own pooled and cached state behind, so <c>GC.GetTotalMemory</c> swings by
/// megabytes with the order the tests run in.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class SessionMemoryTests
{
    private const string FileName = "paths-fill-rules.pdf";

    [Fact]
    public void A_closed_session_and_its_document_are_collected()
    {
        var probe = new RetentionProbe();
        OpenRenderAndClose(probe);

        Assert.Equal(Survivors.None, probe.CollectSurvivors());
    }

    [Fact]
    public void Opening_rendering_and_closing_repeatedly_retains_nothing()
    {
        var probe = new RetentionProbe();
        for (int i = 0; i < 120; i++)
        {
            OpenRenderAndClose(probe);
        }

        Assert.Equal(Survivors.None, probe.CollectSurvivors());
    }

    [Fact]
    public void A_document_kept_alive_is_reported()
    {
        // The canary: the probe that proves nothing is retained must see a document something still holds.
        var probe = new RetentionProbe();
        for (int i = 0; i < 10; i++)
        {
            OpenRenderAndClose(probe);
        }

        PdfDocument kept = OpenRenderCloseAndKeepDocument(probe);

        Survivors survivors = probe.CollectSurvivors();

        Assert.Equal(new Survivors(0, 1, 0), survivors);
        GC.KeepAlive(kept);
    }

    [Fact]
    public void A_display_list_kept_alive_is_reported()
    {
        var probe = new RetentionProbe();
        DisplayList kept = OpenAndKeepList(probe);

        Survivors survivors = probe.CollectSurvivors();

        // A display list refers to its document's resources, so keeping one keeps the document; never the session.
        Assert.Equal(0, survivors.Sessions);
        Assert.Equal(1, survivors.DisplayLists);
        GC.KeepAlive(kept);
    }

    [Fact]
    public void A_tile_rendered_from_the_cached_list_takes_its_pixels_from_the_pool()
    {
        // Cooperative with a completed yield runs synchronously on this thread, so every allocation of the render is counted here.
        ViewerOptions options = ViewerTestSupport.OptionsFor(FileName, ViewerThreading.Cooperative)
            .WithSliceBudget(Timeout.InfiniteTimeSpan)
            .UseYield(_ => ValueTask.CompletedTask);
        using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), options);
        var tile = new Tile(0, 4, new PixelRectangle(0, 0, 1024, 1024));
        var surface = new NullSurface();

        long allocated = Allocations.Measure(() => session.RenderTileAsync(tile, surface).GetAwaiter().GetResult());

        // A 1024 x 1024 tile is 4 MiB of pixels; what remains is the bookkeeping of one asynchronous render.
        Assert.True(allocated < 8 * 1024, $"{allocated} bytes per tile.");
        Assert.Equal(0, session.Statistics.PixelBytes);
    }

    /// <summary>
    /// Opens a session, renders a tile, closes it and checks it gave back everything it took from the pools: no tile pixels held,
    /// every backend (and the rasterizer scratch it rents) disposed, the document's caches empty. Then records the session, its
    /// document and its display list so the probe can prove they are collected.
    /// </summary>
    /// <remarks>
    /// Returns nothing on purpose: an unoptimized (Debug) caller keeps a discarded return value alive in a hidden local until it
    /// returns, which would hold the last document of a loop.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OpenRenderAndClose(RetentionProbe probe) => OpenRenderCloseAndKeepDocument(probe);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PdfDocument OpenRenderCloseAndKeepDocument(RetentionProbe probe)
    {
        DocumentSession session = Open();
        DisplayList list = session.GetDisplayListAsync(0).AsTask().GetAwaiter().GetResult();
        session.RenderTileAsync(new Tile(0, 4, new PixelRectangle(0, 0, 1024, 1024)), new NullSurface()).GetAwaiter().GetResult();
        Assert.Equal(1, session.LiveBackends);
        Assert.True(session.Document.CachedBytes > 0, "The display list is not in the document's cache.");

        session.DisposeAsync().AsTask().GetAwaiter().GetResult();

        Assert.Equal(new SessionStatistics(0, 0, 1, 0), session.Statistics);
        Assert.Equal(0, session.LiveBackends);
        Assert.Equal(0, session.Document.CachedBytes);
        probe.Track(session, list);
        return session.Document;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DisplayList OpenAndKeepList(RetentionProbe probe)
    {
        DocumentSession session = Open();
        DisplayList list = session.GetDisplayListAsync(0).AsTask().GetAwaiter().GetResult();
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        probe.Track(session, list);
        return list;
    }

    private static DocumentSession Open()
    {
        ViewerOptions options = ViewerTestSupport.OptionsFor(FileName, ViewerThreading.Cooperative).UseYield(_ => ValueTask.CompletedTask);
        return DocumentSession.Open(Corpus.Bytes(FileName), options);
    }

    private readonly record struct Survivors(int Sessions, int Documents, int DisplayLists)
    {
        public static Survivors None => default;
    }

    /// <summary>Weak references to what closed sessions must not keep alive.</summary>
    private sealed class RetentionProbe
    {
        private readonly List<WeakReference> _sessions = [];
        private readonly List<WeakReference> _documents = [];
        private readonly List<WeakReference> _lists = [];

        public void Track(DocumentSession session, DisplayList list)
        {
            _sessions.Add(new WeakReference(session));
            _documents.Add(new WeakReference(session.Document));
            _lists.Add(new WeakReference(list));
        }

        /// <summary>Collects until nothing tracked is alive (finalizers may release more on each pass) and counts what survives.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Survivors CollectSurvivors()
        {
            Survivors survivors = Count();
            for (int i = 0; i < 5 && survivors != Survivors.None; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                survivors = Count();
            }

            return survivors;
        }

        private Survivors Count() => new(Alive(_sessions), Alive(_documents), Alive(_lists));

        private static int Alive(List<WeakReference> references) => references.Count(static reference => reference.IsAlive);
    }
}
