using System.Collections.Concurrent;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.TestSupport;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// A test host for the MAUI shell's controller: what PdfView does on a device, minus MAUI. Sessions are cooperative (tiles render
/// inside <see cref="ViewerController.Frame"/>), the clock is manual, and continuations run on the test's own thread.
/// </summary>
internal sealed class Shell : IViewerHost, IDisposable
{
    private Shell(double width, double height, double ratio)
    {
        Controller = new ViewerController(this, Clock) { SchedulerObserver = Events.Add };
        Controller.Resize(width, height, ratio);
        Ratio = ratio;
    }

    public ManualClock Clock { get; } = new();

    public ViewerController Controller { get; }

    public double Ratio { get; }

    public List<TileSchedulerEvent> Events { get; } = [];

    public List<ViewerActionEventArgs> Actions { get; } = [];

    public List<FieldEditor?> Editors { get; } = [];

    public List<AccessibilityChange> AccessibilityChanges { get; } = [];

    public List<string> Copied { get; } = [];

    public List<Exception> Errors { get; } = [];

    public int Paints { get; private set; }

    public Viewport Viewport => Controller.Input!.Viewport;

    public PageLayout Layout => Controller.Zoom!.Layout;

    /// <summary>pdf.js's TraceMonkey paper from the real-world corpus (BROADSIDE_CORPUS_DIR): the repository does not carry it.</summary>
    public static string TraceMonkey => Path.Combine(Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") ?? "", "pdfjs", "tracemonkey.pdf");

    /// <summary>Skips the test when the real-world corpus does not have <see cref="TraceMonkey"/>.</summary>
    public static void RequireTraceMonkey() => Assert.SkipUnless(File.Exists(TraceMonkey), $"{TraceMonkey} is missing (set BROADSIDE_CORPUS_DIR)");

    public static string CorpusFile(string name) => Path.Combine(Corpus.Directory, name);

    public static ViewerOptions Options() => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
        .WithThreading(ViewerThreading.Cooperative)
        .UseBackend(() => new SkiaTileBackend());

    public static async Task<Shell> OpenAsync(string path, double width = 400, double height = 600, double ratio = 2, bool sizeFirst = true)
    {
        var shell = new Shell(sizeFirst ? width : 0, sizeFirst ? height : 0, ratio);
#pragma warning disable CA2000 // the controller owns the session (ownsSession: true) and disposes it
        DocumentSession session = DocumentSession.Open(path, Options());
#pragma warning restore CA2000
        await shell.Controller.OpenAsync(_ => Task.FromResult(session), ownsSession: true, null);
        if (!sizeFirst)
        {
            shell.Controller.Resize(width, height, ratio);
        }

        await shell.SettleAsync();
        return shell;
    }

    public static async Task<Shell> OpenAsync(byte[] bytes, double width = 400, double height = 600, double ratio = 2)
    {
        var shell = new Shell(width, height, ratio);
#pragma warning disable CA2000 // the controller owns the session (ownsSession: true) and disposes it
        DocumentSession session = DocumentSession.Open(bytes, Options());
#pragma warning restore CA2000
        await shell.Controller.OpenAsync(_ => Task.FromResult(session), ownsSession: true, null);
        await shell.SettleAsync();
        return shell;
    }

    /// <summary>
    /// Runs frames (16 ms apart on the test clock) until the controller needs no more and no page load is pending. Page loads run
    /// through the session's own tasks, so a slow runner gets real time for them: the limit is wall-clock time, not a frame count.
    /// </summary>
    public async Task SettleAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool more = true;
        while (watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (more || !Controller.IsLoadingPages)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                more = Controller.Frame();
            }

            if (!more && Controller.IsLoadingPages)
            {
                // Nothing to draw until a page's text, links and accessibility nodes arrive.
                await Task.Delay(2);
                more = true;
                continue;
            }

            await Task.Yield();
            if (!more && !Controller.IsLoadingPages)
            {
                // One more round: a page load may have completed and woken the controller.
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                more = Controller.Frame();
                if (!more && !Controller.IsLoadingPages)
                {
                    Assert.Empty(Errors);
                    return;
                }
            }
        }

        Assert.Fail($"The viewer did not settle (more: {more}, loading pages: {Controller.IsLoadingPages}): " + string.Join(",", Events.GroupBy(e => e.Kind + "/" + e.Priority + "/" + e.Tile.Layers).Select(g => g.Key + "=" + g.Count())) + " stats=" + Controller.Scheduler!.Statistics);
    }

    /// <summary>Sends a SkiaSharp touch event at a point in device-independent units (as SkiaSharp reports it: canvas pixels).</summary>
    public SKTouchEventArgs Touch(long id, SKTouchAction action, SKTouchDeviceType device, double x, double y, int wheelDelta = 0)
    {
        var location = new SKPoint((float)(x * Ratio), (float)(y * Ratio));
        SKTouchEventArgs e = action == SKTouchAction.WheelChanged
            ? new SKTouchEventArgs(id, action, SKMouseButton.Middle, device, location, false, wheelDelta)
            : new SKTouchEventArgs(id, action, SKMouseButton.Left, device, location, action is SKTouchAction.Pressed or SKTouchAction.Moved);
        Controller.Touch(e);
        return e;
    }

    public void Drag(long id, SKTouchDeviceType device, double x0, double y0, double x1, double y1, int steps = 8)
    {
        Touch(id, SKTouchAction.Pressed, device, x0, y0);
        for (int i = 1; i <= steps; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(16));
            Touch(id, SKTouchAction.Moved, device, x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps));
        }

        Touch(id, SKTouchAction.Released, device, x1, y1);
    }

    public void Tap(long id, double x, double y)
    {
        Touch(id, SKTouchAction.Pressed, SKTouchDeviceType.Touch, x, y);
        Clock.Advance(TimeSpan.FromMilliseconds(40));
        Touch(id, SKTouchAction.Released, SKTouchDeviceType.Touch, x, y);
    }

    /// <summary>A page point's position in device-independent units.</summary>
    public (double X, double Y) ToView(int page, double x, double y)
    {
        PathPoint p = Layout.PageToViewport(Viewport, page, x, y);
        return (p.X / Ratio, p.Y / Ratio);
    }

    /// <summary>Draws the newest published frame into a raster bitmap, as the canvas's paint would.</summary>
    public SKBitmap Draw()
    {
        ViewerFrame frame = Controller.Presenter.Latest!;
        var bitmap = new SKBitmap(Math.Max((int)frame.Width, 16), Math.Max((int)frame.Height, 16));
        using var canvas = new SKCanvas(bitmap);
        Controller.Presenter.Draw(canvas);
        return bitmap;
    }

    public void RequestFrame()
    {
    }

    public void RequestPaint() => Paints++;

    public void StateChanged(PdfViewState state)
    {
    }

    public void ActionRequested(ViewerActionEventArgs e) => Actions.Add(e);

    public void Error(Exception exception) => Errors.Add(exception);

    public void EditorChanged(FieldEditor? editor) => Editors.Add(editor);

    public void EditorMoved(double x, double y, double width, double height)
    {
    }

    public void AccessibilityChanged(AccessibilityChange change) => AccessibilityChanges.Add(change);

    public void CopyText(string text) => Copied.Add(text);

    public void Dispose() => Controller.Dispose();

    internal sealed class ManualClock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now;

        public void Advance(TimeSpan by) => _now += by.Ticks;
    }
}

/// <summary>Runs async test bodies on one thread, as the MAUI UI thread runs the controller and its continuations.</summary>
internal static class UiThread
{
    public static void Run(Func<Task> body)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        var context = new SingleThreadContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            Task task = body();
            task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
            context.RunUntilComplete();
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        // Continuations posted after the test finished (a closed session's background sweep) are dropped.
        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.TryAdd((d, state));
            }
            catch (InvalidOperationException)
            {
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void Complete() => _queue.CompleteAdding();

        public void RunUntilComplete()
        {
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
