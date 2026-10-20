using System.Collections.Concurrent;
using System.Diagnostics;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.TestSupport;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// UI-thread frame timing while scrolling, as the MAUI shell runs (#357): a real UI thread (one thread with a synchronization context,
/// every callback timed), a 15 ms frame timer, background sessions with the Skia tile backend and the real clock. Explicit: it
/// measures wall-clock time, so it prints numbers instead of failing on a slow runner (<c>dotnet test --filter FrameTiming -- --explicit on</c>).
/// </summary>
public sealed class FrameTimingTests
{
    public static TheoryData<string, double, double> Documents => new()
    {
        { Shell.TraceMonkey, 40, 1 },
        { Path.Combine(Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? "", "pdfjs", "issue5567.pdf"), 25, 3 },
    };

    [Theory(Explicit = true)]
    [MemberData(nameof(Documents))]
    public void Scrolling_keeps_every_ui_thread_callback_under_16_ms(string path, double step, double zoom)
    {
        Assert.SkipUnless(File.Exists(path), $"{path} is missing (set BROADSIDE_CORPUS_DIR)");
        FrameTiming timing = FrameTiming.Scroll(path, step, zoom, 390, 844, 3);
        TestContext.Current.TestOutputHelper?.WriteLine($"{Path.GetFileName(path)}: {timing}");
        Console.WriteLine($"{Path.GetFileName(path)}: {timing}");
    }
}

/// <summary>Drives a <see cref="ViewerController"/> on a timed UI thread and scrolls it to the end of the document.</summary>
internal sealed class FrameTiming : IViewerHost
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(15);
    private readonly List<double> _callbacks = [];
    private readonly List<double> _frames = [];

    private FrameTiming()
    {
    }

    public int Callbacks => _callbacks.Count;

    public double Max => _callbacks.Count == 0 ? 0 : _callbacks.Max();

    public int Over16 => _callbacks.Count(ms => ms > 16);

    public static FrameTiming Scroll(string path, double step, double zoom, double width, double height, double ratio)
    {
        var timing = new FrameTiming();
        var context = new TimedContext(timing._callbacks);
        var thread = new Thread(() => timing.Run(context, path, step, zoom, width, height, ratio)) { Name = "ui" };
        thread.Start();
        thread.Join();
        return timing;
    }

    public override string ToString()
    {
        double[] sorted = [.. _callbacks.Order()];
        double p99 = sorted.Length == 0 ? 0 : sorted[(int)(sorted.Length * 0.99)];
        return $"{_frames.Count} frames, {sorted.Length} UI callbacks, mean {sorted.DefaultIfEmpty().Average():F2} ms, p99 {p99:F1} ms, "
            + $"max {Max:F1} ms, {Over16} over 16 ms; longest frame {(_frames.Count == 0 ? 0 : _frames.Max()):F1} ms";
    }

    private void Run(TimedContext context, string path, double step, double zoom, double width, double height, double ratio)
    {
        SynchronizationContext.SetSynchronizationContext(context);
        var controller = new ViewerController(this);
        controller.Resize(width, height, ratio);
        ViewerOptions options = new ViewerOptions()
            .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
            .WithThreading(ViewerThreading.Background)
            .UseBackend(() => new SkiaTileBackend());
        Task open = controller.OpenAsync(token => Task.Run(() => DocumentSession.Open(path, options), token), ownsSession: true, null);
        var clock = Stopwatch.StartNew();
        TimeSpan next = TimeSpan.Zero;
        bool scrolling = false;
        double lastY = double.NaN;
        int still = 0;
        TimeSpan limit = TimeSpan.FromSeconds(90);
        while (clock.Elapsed < limit)
        {
            context.RunPending();
            if (clock.Elapsed < next)
            {
                Thread.Sleep(1);
                continue;
            }

            next = clock.Elapsed + FrameInterval;
            if (!open.IsCompleted || controller.Input is null)
            {
                continue;
            }

            long start = Stopwatch.GetTimestamp();
            if (!scrolling)
            {
                // The first page settles before the scroll starts (opening is not scrolling); a zoomed run zooms in once first.
                controller.Frame();
                if (controller.Scheduler!.IsIdle && !controller.Input.IsAnimating)
                {
                    if (zoom > 1)
                    {
                        controller.Magnify(zoom, width / 2, height / 4);
                        zoom = 1;
                    }
                    else
                    {
                        scrolling = true;
                    }
                }

                continue;
            }

            controller.Wheel(0, step, WheelDeltaMode.Pixel, InputModifiers.None, width / 2, height / 2);
            controller.Frame();
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _frames.Add(ms);
            if (ms > 16)
            {
                Console.WriteLine($"  slow frame {ms:F1} ms");
            }

            context.Record(ms);
            double y = controller.Input.Viewport.Y;
            still = y == lastY ? still + 1 : 0;
            lastY = y;
            if (still > 30)
            {
                break; // the end of the document
            }
        }

        controller.Dispose();
        context.RunPending();
    }

    void IViewerHost.RequestFrame()
    {
    }

    void IViewerHost.RequestPaint()
    {
    }

    void IViewerHost.StateChanged(PdfViewState state)
    {
    }

    void IViewerHost.ActionRequested(ViewerActionEventArgs e)
    {
    }

    void IViewerHost.Error(Exception exception) => Console.WriteLine(exception);

    void IViewerHost.EditorChanged(FieldEditor? editor)
    {
    }

    void IViewerHost.EditorMoved(double x, double y, double width, double height)
    {
    }

    void IViewerHost.AccessibilityChanged(AccessibilityChange change)
    {
    }

    void IViewerHost.CopyText(string text)
    {
    }

    /// <summary>A UI thread's context: continuations posted to it run in <see cref="RunPending"/>, each one timed.</summary>
    private sealed class TimedContext(List<double> durations) : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void Record(double ms) => durations.Add(ms);

        public void RunPending()
        {
            while (_queue.TryDequeue(out (SendOrPostCallback Callback, object? State) item))
            {
                long start = Stopwatch.GetTimestamp();
                item.Callback(item.State);
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                durations.Add(ms);
                if (ms > 16)
                {
                    Console.WriteLine($"  slow callback {ms:F1} ms: {(item.State is Delegate action ? action.Target?.GetType().ToString() : item.State?.GetType().ToString())}");
                }
            }
        }
    }
}
