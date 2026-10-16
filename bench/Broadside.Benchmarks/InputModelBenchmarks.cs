using BenchmarkDotNet.Attributes;
using Broadside.TestSupport;
using Broadside.Viewer;

namespace Broadside.Benchmarks;

/// <summary>
/// A gesture through the viewer's input model (issue #213, viewer capability 14) in a cooperative session, as on the browser's one
/// thread: a drag that turns into a pinch out and back, a wheel line and an arrow key, each with the frame a shell runs. Every gesture
/// returns to where it started, so the frames find their tiles resident. A shell runs this on every input event, so it must read
/// <c>-</c> in <c>Allocated</c>; <c>Broadside.Viewer.Tests.InputModelTests.Events_and_frames_allocate_nothing</c> fails the build if it
/// regresses.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class InputModelBenchmarks
{
    private readonly FramePart[] _frame = new FramePart[1024];
    private readonly StepClock _clock = new();
    private DocumentSession? _session;
    private TileScheduler? _scheduler;
    private InputModel? _input;

    [GlobalSetup]
    public void Setup()
    {
        _session = DocumentSession.Open(ManyPagesDocument.Create(500), new ViewerOptions().WithThreading(ViewerThreading.Cooperative));
        PageLayout layout = PageLayout.CreateAsync(_session).AsTask().GetAwaiter().GetResult();
        _scheduler = new TileScheduler(_session, layout, new NullSurface());
        var zoom = new ProgressiveZoom(_scheduler, layout.ScrollTo(new Viewport(0, 0, 1200, 900, 1.25), 250), new ProgressiveZoomOptions().UseTimeProvider(_clock));
        _input = new InputModel(zoom, new InputModelOptions().UseTimeProvider(_clock));
        while (_input.RunFrame(Timeout.InfiniteTimeSpan))
        {
        }

        Gesture();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scheduler?.Dispose();
        _session?.Dispose();
    }

    [Benchmark]
    public int Gesture()
    {
        InputModel input = _input!;
        input.PointerDown(1, PointerKind.Touch, 600, 450);
        _clock.Advance(10);
        input.PointerMove(1, 600, 430);
        input.PointerDown(2, PointerKind.Touch, 700, 430);
        input.PointerMove(2, 800, 430);
        input.RunFrame(TimeSpan.FromMilliseconds(8));
        int parts = input.Zoom.GetFrame(_frame);
        input.PointerMove(2, 700, 430);
        input.PointerUp(2, 700, 430);
        _clock.Advance(10);
        input.PointerMove(1, 600, 450);
        _clock.Advance(200);
        input.PointerUp(1, 600, 450);
        input.Wheel(0, 1, WheelDeltaMode.Line, InputModifiers.None, 600, 450);
        input.KeyDown(InputKey.ArrowUp, InputModifiers.None);
        input.RunFrame(TimeSpan.FromMilliseconds(8));
        return parts + input.Zoom.GetFrame(_frame);
    }

    private sealed class StepClock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(int milliseconds) => _now += milliseconds * TimeSpan.TicksPerMillisecond;
    }

    private sealed class NullSurface : IRenderSurface
    {
        public void Present(Tile tile, Rendering.Bitmap pixels)
        {
        }
    }
}
