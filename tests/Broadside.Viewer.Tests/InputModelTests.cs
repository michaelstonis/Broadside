using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Synthetic gesture, wheel and keyboard sequences through the input model in every layout mode (issue #213).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class InputModelTests
{
    public static TheoryData<PageLayoutMode> Modes => new(Enum.GetValues<PageLayoutMode>());

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_drag_scrolls_with_the_finger_and_a_pinch_zooms_about_its_centre(PageLayoutMode mode)
    {
        await using Harness viewer = await Harness.OpenAsync(mode, 1.5);
        Viewport start = viewer.Input.Viewport;

        viewer.Input.PointerDown(1, PointerKind.Touch, 300, 400);
        viewer.Input.PointerMove(1, 250, 300);
        Assert.Equal(viewer.Layout.Clamp(start with { X = start.X + 50, Y = start.Y + 100 }), viewer.Input.Viewport);

        // A second finger 100 px away; the fingers spread to 200 px while their centre moves 50 px right.
        viewer.Input.PointerDown(2, PointerKind.Touch, 350, 300);
        Assert.True(viewer.Layout.TryGetPageAt(viewer.Input.Viewport, 300, 300, out PagePoint anchored));
        viewer.Input.PointerMove(2, 450, 300);

        Assert.Equal(3, viewer.Input.Viewport.Zoom, 9);
        Assert.True(viewer.Zoom.IsZooming);
        Assert.True(viewer.Layout.TryGetPageAt(viewer.Input.Viewport, 350, 300, out PagePoint now));
        Assert.Equal(anchored.PageIndex, now.PageIndex);
        Assert.Equal(anchored.X, now.X, 6);
        Assert.Equal(anchored.Y, now.Y, 6);

        viewer.Input.PointerUp(2, 450, 300);
        viewer.Input.PointerUp(1, 250, 300);
        viewer.Animate();
        Assert.False(viewer.Zoom.IsZooming);
        Assert.Equal(3, viewer.Input.Viewport.Zoom, 9);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Double_tap_toggles_between_fit_width_and_a_zoomed_level_anchored_at_the_tap(PageLayoutMode mode)
    {
        await using Harness viewer = await Harness.OpenAsync(mode, ZoomMode.FitWidth);
        double fitWidth = viewer.Input.Viewport.Zoom;
        Assert.True(viewer.Layout.TryGetPageAt(viewer.Input.Viewport, 200, 400, out PagePoint tapped));

        viewer.DoubleTap(200, 400);
        Assert.True(viewer.Input.IsAnimating);
        viewer.Animate();

        Assert.Equal(2 * fitWidth, viewer.Input.Viewport.Zoom, 9);
        Assert.False(viewer.Zoom.IsZooming);
        Assert.True(viewer.Layout.TryGetPageAt(viewer.Input.Viewport, 200, 400, out PagePoint now));
        Assert.Equal(tapped.PageIndex, now.PageIndex);
        Assert.Equal(tapped.X, now.X, 6);
        Assert.Equal(tapped.Y, now.Y, 6);

        viewer.DoubleTap(200, 400);
        viewer.Animate();
        Assert.Equal(fitWidth, viewer.Input.Viewport.Zoom);

        // Two taps too far apart in time are two single taps: nothing zooms.
        viewer.Tap(200, 400);
        viewer.Clock.Advance(TimeSpan.FromMilliseconds(600));
        viewer.Tap(200, 400);
        Assert.False(viewer.Input.IsAnimating);
        Assert.Equal(fitWidth, viewer.Input.Viewport.Zoom);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_swipe_turns_the_spread_in_paged_modes_and_flings_on_in_continuous_modes(PageLayoutMode mode)
    {
        await using Harness viewer = await Harness.OpenAsync(mode, ZoomMode.FitPage);
        bool horizontal = mode == PageLayoutMode.Horizontal;
        int spread = viewer.Layout.GetSpread(viewer.Input.CurrentPage);

        // A quick swipe towards the start of the strip: 4 moves of 40 px, 10 ms apart (4000 px/s).
        viewer.Input.PointerDown(1, PointerKind.Touch, 300, 600);
        for (int i = 1; i <= 4; i++)
        {
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(10));
            viewer.Input.PointerMove(1, horizontal ? 300 - (40 * i) : 300, horizontal ? 600 : 600 - (40 * i));
        }

        double released = Along(viewer.Input.Viewport, horizontal);
        viewer.Input.PointerUp(1, horizontal ? 140 : 300, horizontal ? 600 : 440);
        Assert.True(viewer.Input.IsAnimating);
        viewer.Animate();
        Assert.False(viewer.Input.IsAnimating);

        double settled = Along(viewer.Input.Viewport, horizontal);
        if (mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp)
        {
            Assert.Equal(spread + 1, viewer.Layout.GetSpread(viewer.Input.CurrentPage));
            // The next spread is shorter than the viewport, so it shows centred.
            LayoutRectangle next = viewer.Layout.GetSpreadBounds(spread + 1);
            Assert.Equal(((next.Y + (next.Height / 2)) * viewer.Input.Viewport.Zoom) - 400, settled, 6);
        }
        else
        {
            // Inertia carries on decelerating: 4000 px/s with a 325 ms time constant travels just under 1300 px more.
            Assert.InRange(settled - released, 1200, 1300);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Keys_and_the_wheel_page_through_the_document_and_zoom(PageLayoutMode mode)
    {
        await using Harness viewer = await Harness.OpenAsync(mode, ZoomMode.FitWidth, new InputModelOptions().WithAnimationDuration(TimeSpan.Zero), height: 400);
        bool horizontal = mode == PageLayoutMode.Horizontal;
        bool paged = mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp;
        double fitWidth = viewer.Input.Viewport.Zoom;

        Assert.True(viewer.Input.KeyDown(InputKey.End, InputModifiers.None));
        Assert.Equal(viewer.Layout.SpreadCount - 1, viewer.Layout.GetSpread(viewer.Input.CurrentPage));
        Assert.True(viewer.Input.KeyDown(InputKey.Home, InputModifiers.None));
        Assert.Equal(0, viewer.Input.CurrentPage);
        Assert.Equal(0, Along(viewer.Input.Viewport, horizontal));

        Assert.True(viewer.Input.KeyDown(InputKey.PageDown, InputModifiers.None));
        if (paged)
        {
            Assert.Equal(1, viewer.Layout.GetSpread(viewer.Input.CurrentPage));
        }
        else
        {
            Viewport v = viewer.Input.Viewport;
            Assert.Equal(0.9 * (horizontal ? v.Width : v.Height), Along(v, horizontal), 6);
        }

        Assert.True(viewer.Input.KeyDown(InputKey.PageUp, InputModifiers.None));
        Assert.Equal(0, viewer.Input.CurrentPage);

        Assert.True(viewer.Input.KeyDown(InputKey.Plus, InputModifiers.Control));
        Assert.Equal(1.25 * fitWidth, viewer.Input.Viewport.Zoom, 9);
        Assert.True(viewer.Zoom.IsZooming);
        viewer.Clock.Advance(TimeSpan.FromMilliseconds(200));
        viewer.Input.RunFrame(TimeSpan.Zero);
        Assert.False(viewer.Zoom.IsZooming);

        Assert.True(viewer.Input.KeyDown(InputKey.Zero, InputModifiers.Control));
        Assert.Equal(fitWidth, viewer.Input.Viewport.Zoom);

        // Ctrl (or Cmd) and the wheel zooms about the pointer; a bare wheel scrolls.
        viewer.Input.Wheel(0, -100, WheelDeltaMode.Pixel, InputModifiers.Meta, 300, 400);
        Assert.Equal(fitWidth * Math.Exp(0.2), viewer.Input.Viewport.Zoom, 9);
        Assert.False(viewer.Input.KeyDown(InputKey.None, InputModifiers.None));
    }

    [Fact]
    public async Task In_a_paged_mode_the_wheel_turns_the_spread_once_per_burst_at_its_edge()
    {
        await using Harness viewer = await Harness.OpenAsync(PageLayoutMode.SinglePage, ZoomMode.FitPage, new InputModelOptions().WithAnimationDuration(TimeSpan.Zero));
        int page = viewer.Input.CurrentPage;

        for (int i = 0; i < 10; i++)
        {
            viewer.Input.Wheel(0, 1, WheelDeltaMode.Line, InputModifiers.None, 300, 400);
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(page + 1, viewer.Input.CurrentPage);
        viewer.Clock.Advance(TimeSpan.FromMilliseconds(500));
        viewer.Input.Wheel(0, 1, WheelDeltaMode.Line, InputModifiers.None, 300, 400);
        Assert.Equal(page + 2, viewer.Input.CurrentPage);
    }

    [Fact]
    public async Task A_pinch_released_near_fit_width_snaps_to_it()
    {
        await using Harness viewer = await Harness.OpenAsync(PageLayoutMode.Continuous, ZoomMode.FitWidth);
        double fitWidth = viewer.Input.Viewport.Zoom;

        viewer.Input.PointerDown(1, PointerKind.Touch, 250, 400);
        viewer.Input.PointerDown(2, PointerKind.Touch, 350, 400);
        viewer.Input.PointerMove(2, 354, 400);
        Assert.Equal(1.04 * fitWidth, viewer.Input.Viewport.Zoom, 9);
        viewer.Input.PointerUp(2, 354, 400);
        viewer.Input.PointerUp(1, 250, 400);
        viewer.Animate();

        Assert.Equal(fitWidth, viewer.Input.Viewport.Zoom);
        Assert.False(viewer.Zoom.IsZooming);
    }

    [Fact]
    public async Task Events_and_frames_allocate_nothing()
    {
        await using Harness viewer = await Harness.OpenAsync(PageLayoutMode.Continuous, 1.5);
        viewer.Settle();

        long allocated = Allocations.Measure(() =>
        {
            // Every gesture returns to where it started, so the frames find the same tiles resident and render nothing new.
            viewer.Input.PointerDown(1, PointerKind.Touch, 300, 400);
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(10));
            viewer.Input.PointerMove(1, 300, 380);
            viewer.Input.PointerDown(2, PointerKind.Touch, 400, 380);
            viewer.Input.PointerMove(2, 500, 380);
            viewer.Input.RunFrame(TimeSpan.FromMilliseconds(8));
            viewer.Input.PointerMove(2, 400, 380);
            viewer.Input.PointerUp(2, 400, 380);
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(10));
            viewer.Input.PointerMove(1, 300, 400);
            viewer.Clock.Advance(TimeSpan.FromMilliseconds(200));
            viewer.Input.PointerUp(1, 300, 400);
            viewer.Input.Wheel(0, 1, WheelDeltaMode.Line, InputModifiers.None, 300, 400);
            viewer.Input.KeyDown(InputKey.ArrowUp, InputModifiers.None);
            viewer.Input.RunFrame(TimeSpan.FromMilliseconds(8));
            viewer.Input.Stop();
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task Arguments_are_checked()
    {
        await using Harness viewer = await Harness.OpenAsync(PageLayoutMode.Continuous, 1);
        Assert.Throws<ArgumentNullException>(() => new InputModel(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Input.PointerDown(1, PointerKind.Touch, double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Input.Wheel(double.PositiveInfinity, 0, WheelDeltaMode.Pixel, InputModifiers.None, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Input.PixelRatio = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Input.GoToPage(20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputModelOptions().WithDoubleTapZoom(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputModelOptions().WithAnimationDuration(TimeSpan.FromSeconds(-1)));
    }

    private static double Along(in Viewport viewport, bool horizontal) => horizontal ? viewport.X : viewport.Y;

    /// <summary>A cooperative session over 20 pages gone to page 3 in a 600-pixel-wide viewport, the progressive zoom and the input model on one manual clock.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private Harness(DocumentSession session, PageLayout layout, Viewport viewport, InputModelOptions? options)
        {
            Session = session;
            Layout = layout;
            Scheduler = new TileScheduler(session, layout, new NullSurface());
            Zoom = new ProgressiveZoom(Scheduler, viewport, new ProgressiveZoomOptions().UseTimeProvider(Clock));
            Input = new InputModel(Zoom, (options ?? new InputModelOptions()).UseTimeProvider(Clock));
        }

        public ManualClock Clock { get; } = new();

        public DocumentSession Session { get; }

        public PageLayout Layout { get; }

        public TileScheduler Scheduler { get; }

        public ProgressiveZoom Zoom { get; }

        public InputModel Input { get; }

        public static Task<Harness> OpenAsync(PageLayoutMode mode, ZoomMode zoom, InputModelOptions? options = null, double height = 800) =>
            OpenAsync(mode, layout => layout.GetZoom(zoom, 600, height, 3, 96.0 / 72), options, height);

        public static Task<Harness> OpenAsync(PageLayoutMode mode, double zoom, InputModelOptions? options = null) => OpenAsync(mode, _ => zoom, options, 800);

        public void Tap(double x, double y)
        {
            Input.PointerDown(7, PointerKind.Touch, x, y);
            Clock.Advance(TimeSpan.FromMilliseconds(50));
            Input.PointerUp(7, x, y);
        }

        public void DoubleTap(double x, double y)
        {
            Tap(x, y);
            Clock.Advance(TimeSpan.FromMilliseconds(100));
            Tap(x, y);
        }

        public void Animate()
        {
            for (int i = 0; i < 500 && Input.IsAnimating; i++)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                Input.RunFrame(TimeSpan.Zero);
            }

            Input.RunFrame(TimeSpan.Zero);
        }

        public void Settle()
        {
            while (Input.RunFrame(Timeout.InfiniteTimeSpan))
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            Scheduler.Dispose();
            await Session.DisposeAsync();
        }

        private static async Task<Harness> OpenAsync(PageLayoutMode mode, Func<PageLayout, double> zoom, InputModelOptions? options, double height)
        {
            DocumentSession session = DocumentSession.Open(ManyPagesDocument.Create(20), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
            PageLayout layout = await PageLayout.CreateAsync(session, new PageLayoutOptions().WithMode(mode), TestContext.Current.CancellationToken);
            var harness = new Harness(session, layout, new Viewport(0, 0, 600, height, zoom(layout)), options);
            harness.Input.GoToPage(3);
            harness.Animate();
            return harness;
        }
    }

    private sealed class NullSurface : IRenderSurface
    {
        public void Present(Tile tile, Rendering.Bitmap pixels)
        {
        }
    }

    internal sealed class ManualClock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }
}
