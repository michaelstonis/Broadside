using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// One frame of the viewer's tile scheduler (issue #211, viewer capabilities 1, 3 and 13) over a 500-page document in a cooperative
/// session, with every wanted tile already on the surface: the viewport moves, the scheduler works out the wanted tiles in priority
/// order, checks the memory budget and finds nothing to draw. A shell runs this on every scroll event, so it must read <c>-</c> in
/// <c>Allocated</c>; <c>Broadside.Viewer.Tests.TileSchedulerTests.Frames_with_everything_on_the_surface_allocate_nothing</c> fails
/// the build if it regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class TileSchedulerBenchmarks
{
    private DocumentSession? _session;
    private TileScheduler? _scheduler;
    private Viewport _a;
    private Viewport _b;

    [GlobalSetup]
    public void Setup()
    {
        _session = DocumentSession.Open(ManyPagesDocument.Create(500), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = PageLayout.CreateAsync(_session).AsTask().GetAwaiter().GetResult();
        _scheduler = new TileScheduler(_session, layout, new NullSurface());
        _a = layout.ScrollTo(new Viewport(0, 0, 1200, 900, 1.25), 250);
        _b = _a with { Y = _a.Y + 200 };
        for (int i = 0; i < 3; i++)
        {
            Settle(_a);
            Settle(_b);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler?.Dispose();
        _session?.Dispose();
    }

    [Benchmark]
    public bool Frame()
    {
        _scheduler!.Update(_a);
        bool more = _scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
        _scheduler.Update(_b);
        return more | _scheduler.RunFrame(TimeSpan.FromMilliseconds(8));
    }

    private void Settle(in Viewport viewport)
    {
        _scheduler!.Update(viewport);
        while (_scheduler.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }
    }

    private sealed class NullSurface : IRenderSurface
    {
        public void Present(Tile tile, Rendering.Bitmap pixels)
        {
        }
    }
}
