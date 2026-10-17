using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// A pinch step of the viewer's progressive zoom (issue #212, viewer capability 2) in a cooperative session, as on the browser's one
/// thread: two steps over a 500-page document with the tiles of the old zoom on the surface, each followed by the frame a shell draws
/// (scaled tiles over the pages' paper) and the frame's work (none mid-gesture). A shell runs this on every pointer event, so it must
/// read <c>-</c> in <c>Allocated</c>; <c>Broadside.Viewer.Tests.ProgressiveZoomTests.Zoom_steps_and_frames_mid_gesture_allocate_nothing</c>
/// fails the build if it regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ProgressiveZoomBenchmarks
{
    private readonly FramePart[] _frame = new FramePart[1024];
    private DocumentSession? _session;
    private TileScheduler? _scheduler;
    private ProgressiveZoom? _zoom;

    [GlobalSetup]
    public void Setup()
    {
        _session = DocumentSession.Open(ManyPagesDocument.Create(500), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = PageLayout.CreateAsync(_session).AsTask().GetAwaiter().GetResult();
        _scheduler = new TileScheduler(_session, layout, new NullSurface());
        _zoom = new ProgressiveZoom(_scheduler, layout.ScrollTo(new Viewport(0, 0, 1200, 900, 1.25), 250));
        while (_zoom.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        _zoom.BeginGesture();
        for (int i = 0; i < 3; i++)
        {
            ZoomStep();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler?.Dispose();
        _session?.Dispose();
    }

    [Benchmark]
    public int ZoomStep()
    {
        _zoom!.ZoomBy(1.1, 600, 450);
        int parts = _zoom.GetFrame(_frame);
        _zoom.RunFrame(TimeSpan.FromMilliseconds(8));
        _zoom.ZoomBy(1 / 1.1, 600, 450);
        return parts + _zoom.GetFrame(_frame);
    }

    private sealed class NullSurface : IRenderSurface
    {
        public void Present(Tile tile, Rendering.Bitmap pixels)
        {
        }
    }
}
