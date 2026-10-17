using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Progressive zoom through the headless host: scaled tiles at once, the anchor held, sharp tiles at the settled scale (issue #212).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ProgressiveZoomTests
{
    private const double Zoom = 0.5;
    private const double AnchorX = 300;
    private const double AnchorY = 400;

    [Fact]
    public async Task A_zoom_step_presents_a_frame_from_the_tiles_on_the_surface_before_any_new_render_completes()
    {
        await using Viewer viewer = await Viewer.OpenAsync();
        viewer.Settle();
        Viewport before = viewer.Zoom.Viewport;
        int events = viewer.Events.Count;

        Viewport after = viewer.Zoom.ZoomBy(1.5, AnchorX, AnchorY);
        FramePart[] frame = viewer.Frame();

        // Trace: the step drew nothing and started nothing, yet the frame is made of tiles already on the surface.
        Assert.Equal(events, viewer.Events.Count);
        Assert.Contains(frame, p => p.Kind == FramePartKind.Scaled);
        Assert.DoesNotContain(frame, p => p.Kind == FramePartKind.Sharp);
        Assert.All(frame.Where(p => p.Kind != FramePartKind.Paper), p => Assert.True(viewer.Surface.Contains(p.Tile)));

        // Every page pixel of the viewport is covered by a scaled tile: nothing blank while the sharp tiles are pending.
        AssertCovered(viewer.Layout, after, frame, FramePartKind.Scaled);

        // Each scaled tile lands where its page content shows at the new zoom.
        var pages = new VisiblePage[8];
        int count = viewer.Layout.GetVisiblePages(before, pages);
        foreach (FramePart part in frame.Where(p => p.Kind == FramePartKind.Scaled && p.Tile.Scale == Zoom))
        {
            VisiblePage page = pages[..count].Single(p => p.PageIndex == part.Tile.PageIndex);
            PagePoint corner = viewer.Layout.ViewportToPage(before, page.PageIndex, page.X + part.Tile.Region.X, page.Y + part.Tile.Region.Y);
            Graphics.PathPoint expected = viewer.Layout.PageToViewport(after, page.PageIndex, corner.X, corner.Y);
            Assert.Equal(expected.X, part.Destination.X, 6);
            Assert.Equal(expected.Y, part.Destination.Y, 6);
            Assert.Equal(part.Tile.Region.Width * 1.5, part.Destination.Width, 6);
        }
    }

    [Fact]
    public async Task The_page_point_under_the_anchor_stays_put_and_the_zoom_is_capped()
    {
        await using Viewer viewer = await Viewer.OpenAsync(new ProgressiveZoomOptions().WithZoomRange(0.25, 4));
        PagePoint anchored = Anchor(viewer.Layout, viewer.Zoom.Viewport);

        foreach (double factor in (double[])[1.1, 1.25, 0.8, 2])
        {
            Viewport viewport = viewer.Zoom.ZoomBy(factor, AnchorX, AnchorY);
            PagePoint now = Anchor(viewer.Layout, viewport);
            Assert.Equal(anchored.PageIndex, now.PageIndex);
            Assert.Equal(anchored.X, now.X, 6);
            Assert.Equal(anchored.Y, now.Y, 6);
        }

        Assert.Equal(4, viewer.Zoom.ZoomBy(100, AnchorX, AnchorY).Zoom);
        Assert.Equal(anchored.X, Anchor(viewer.Layout, viewer.Zoom.Viewport).X, 6);
        Assert.Equal(0.25, viewer.Zoom.ZoomTo(1e-6, AnchorX, AnchorY).Zoom);
    }

    [Fact]
    public async Task A_gesture_renders_only_at_the_settled_scale_and_then_discards_the_old_scale()
    {
        await using Viewer viewer = await Viewer.OpenAsync();
        viewer.Settle();
        int settledEvents = viewer.Events.Count;

        viewer.Zoom.BeginGesture();
        foreach (double factor in (double[])[1.1, 1.2, 1.3, 1.1])
        {
            viewer.Zoom.ZoomBy(factor, AnchorX, AnchorY);
            Assert.False(viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8)));
            Assert.Equal(FramePartKind.Scaled, viewer.Frame().Last().Kind);
        }

        Assert.Equal(settledEvents, viewer.Events.Count); // nothing rendered at an intermediate scale
        double settled = viewer.Zoom.EndGesture().Zoom;
        Assert.False(viewer.Zoom.IsZooming);
        viewer.Settle();

        TileSchedulerEvent[] after = [.. viewer.Events.Skip(settledEvents)];
        Assert.All(
            after.Where(e => e.Kind == TileEventKind.Started),
            e => Assert.True(e.Tile.Scale == settled || e.IsPlaceholder, $"started at {e.Tile.Scale}"));
        Assert.Contains(after, e => e.Kind == TileEventKind.Evicted && e.Tile.Scale == Zoom);

        FramePart[] frame = viewer.Frame();
        Assert.DoesNotContain(frame, p => p.Tile.Scale == Zoom);
        Assert.Equal(FramePartKind.Sharp, frame[^1].Kind);
        AssertCovered(viewer.Layout, viewer.Zoom.Viewport, frame, FramePartKind.Sharp);
        Assert.All(frame.Where(p => p.Kind != FramePartKind.Paper), p => Assert.True(viewer.Surface.Contains(p.Tile)));
    }

    [Fact]
    public async Task Wheel_steps_settle_once_they_pause_for_the_settle_delay()
    {
        var clock = new ManualClock();
        await using Viewer viewer = await Viewer.OpenAsync(new ProgressiveZoomOptions().WithSettleDelay(TimeSpan.FromMilliseconds(100)).UseTimeProvider(clock));
        viewer.Settle();
        int settledEvents = viewer.Events.Count;

        viewer.Zoom.ZoomBy(1.25, AnchorX, AnchorY);
        clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.True(viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8)));
        viewer.Zoom.ZoomBy(1.25, AnchorX, AnchorY);
        clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.True(viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8)));
        Assert.True(viewer.Zoom.IsZooming);
        Assert.Equal(settledEvents, viewer.Events.Count);

        clock.Advance(TimeSpan.FromMilliseconds(60));
        viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8));
        Assert.False(viewer.Zoom.IsZooming);
        Assert.Contains(viewer.Events.Skip(settledEvents), e => e.Kind == TileEventKind.Started && e.Tile.Scale == Zoom * 1.25 * 1.25);
    }

    [Fact]
    public async Task Pages_with_nothing_on_the_surface_show_their_paper()
    {
        await using Viewer viewer = await Viewer.OpenAsync();
        FramePart[] frame = viewer.Frame();

        var pages = new VisiblePage[8];
        int count = viewer.Layout.GetVisiblePages(viewer.Zoom.Viewport, pages);
        Assert.Equal(count, frame.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(FramePartKind.Paper, frame[i].Kind);
            Assert.Equal(pages[i].PageIndex, frame[i].Tile.PageIndex);
            Assert.Equal(pages[i].X, frame[i].Destination.X);
            Assert.Equal(viewer.Layout.GetPagePixels(pages[i].PageIndex, Zoom).Width, frame[i].Destination.Width);
        }

        AssertCovered(viewer.Layout, viewer.Zoom.Viewport, frame, FramePartKind.Paper);
        Assert.True(viewer.Scheduler.GetFrame(viewer.Zoom.Viewport, []) == count, "a span too small returns the count");
    }

    [Fact]
    public async Task Zoom_steps_and_frames_mid_gesture_allocate_nothing()
    {
        await using Viewer viewer = await Viewer.OpenAsync();
        viewer.Settle();
        var frame = new FramePart[256];
        viewer.Zoom.BeginGesture();
        for (int i = 0; i < 3; i++)
        {
            viewer.Zoom.ZoomBy(1.2, AnchorX, AnchorY);
            viewer.Zoom.ZoomBy(1 / 1.2, AnchorX, AnchorY);
            viewer.Zoom.GetFrame(frame);
        }

        int parts = 0;
        long allocated = Allocations.Measure(() =>
        {
            viewer.Zoom.ZoomBy(1.2, AnchorX, AnchorY);
            parts += viewer.Zoom.GetFrame(frame);
            viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8));
            viewer.Zoom.ZoomBy(1 / 1.2, AnchorX, AnchorY);
            parts += viewer.Zoom.GetFrame(frame);
            viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(8));
        });

        Assert.Equal(0, allocated);
        Assert.True(parts > 4);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressiveZoomOptions().WithZoomRange(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressiveZoomOptions().WithZoomRange(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressiveZoomOptions().WithZoomRange(1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressiveZoomOptions().WithSettleDelay(TimeSpan.FromMilliseconds(-1)));
        Assert.Throws<ArgumentNullException>(() => new ProgressiveZoomOptions().UseTimeProvider(null!));

        await using Viewer viewer = await Viewer.OpenAsync();
        Assert.Throws<ArgumentNullException>(() => new ProgressiveZoom(null!, viewer.Zoom.Viewport));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Zoom.ZoomBy(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Zoom.ZoomTo(double.NaN, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Zoom.ZoomTo(1, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Zoom.RunFrame(TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Zoom.SetViewport(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Scheduler.GetFrame(default, []));
    }

    private static PagePoint Anchor(PageLayout layout, in Viewport viewport)
    {
        Assert.True(layout.TryGetPageAt(viewport, AnchorX, AnchorY, out PagePoint point));
        return point;
    }

    /// <summary>Checks on a 10-pixel grid that every viewport pixel showing a page lies under a part of <paramref name="kind"/>.</summary>
    private static void AssertCovered(PageLayout layout, in Viewport viewport, FramePart[] frame, FramePartKind kind)
    {
        int checkedPoints = 0;
        for (double y = 5; y < viewport.Height; y += 10)
        {
            for (double x = 5; x < viewport.Width; x += 10)
            {
                if (!layout.TryGetPageAt(viewport, x, y, out PagePoint point))
                {
                    continue;
                }

                checkedPoints++;
                double px = x, py = y;
                Assert.True(
                    frame.Any(p => p.Kind == kind && p.Tile.PageIndex == point.PageIndex && p.Destination.Contains(px, py)),
                    $"({x}, {y}) on page {point.PageIndex} is not under a {kind} part");
            }
        }

        Assert.True(checkedPoints > 100);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }

    /// <summary>A cooperative session over 20 pages with page 3 in a 600x800 viewport, a recording surface and the zoom.</summary>
    private sealed class Viewer : IAsyncDisposable
    {
        private Viewer(DocumentSession session, PageLayout layout, ProgressiveZoomOptions? options)
        {
            Session = session;
            Layout = layout;
            Scheduler = new TileScheduler(session, layout, Surface, new TileSchedulerOptions().UseObserver(Events.Add));
            Zoom = new ProgressiveZoom(Scheduler, layout.ScrollTo(new Viewport(0, 0, 600, 800, ProgressiveZoomTests.Zoom), 3), options);
        }

        public DocumentSession Session { get; }

        public PageLayout Layout { get; }

        public RecordingSurface Surface { get; } = new();

        public List<TileSchedulerEvent> Events { get; } = [];

        public TileScheduler Scheduler { get; }

        public ProgressiveZoom Zoom { get; }

        public static async Task<Viewer> OpenAsync(ProgressiveZoomOptions? options = null)
        {
            DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(20), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
            PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: TestContext.Current.CancellationToken);
            return new Viewer(session, layout, options);
        }

        public void Settle()
        {
            while (Zoom.RunFrame(Timeout.InfiniteTimeSpan))
            {
            }
        }

        public FramePart[] Frame()
        {
            var parts = new FramePart[512];
            return parts[..Zoom.GetFrame(parts)];
        }

        public async ValueTask DisposeAsync()
        {
            Scheduler.Dispose();
            await Session.DisposeAsync();
        }
    }

    /// <summary>Keeps the set of tiles on the surface, without pixels.</summary>
    private sealed class RecordingSurface : IRenderSurface
    {
        private readonly HashSet<Tile> _tiles = [];

        public bool Contains(Tile tile) => _tiles.Contains(tile);

        public void Present(Tile tile, Rendering.Bitmap pixels) => Assert.True(_tiles.Add(tile));

        public void Evict(Tile tile) => Assert.True(_tiles.Remove(tile));
    }
}
