using System.Collections.Concurrent;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Wpf.Tests;

/// <summary>
/// A test host for the WPF shell's controller: what <see cref="PdfView"/> does in a window, minus WPF. Sessions are cooperative (tiles
/// render inside <see cref="ViewerController.Frame"/>), the clock is manual, continuations run on the test's own thread, and tiles
/// become <see cref="TileCopy"/> objects instead of WPF bitmaps.
/// </summary>
internal sealed class Shell : IViewerHost, IDisposable
{
    private Shell(double width, double height, double ratio)
    {
        Controller = new ViewerController(this, TileCopy.Create, Clock) { SchedulerObserver = Events.Add };
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

    public List<PdfViewState> States { get; } = [];

    public List<string> Copied { get; } = [];

    public List<Exception> Errors { get; } = [];

    public int Paints { get; private set; }

    public Viewport Viewport => Controller.Input!.Viewport;

    public PageLayout Layout => Controller.Zoom!.Layout;

    public ViewerFrame Frame => Controller.LatestFrame!;

    public static string CorpusFile(string name) => Path.Combine(Corpus.Directory, name);

    /// <summary>A representative sample document from samples/documents/pdf (#363).</summary>
    public static string SampleDocument(string name) => Path.Combine(Corpus.Directory, "..", "..", "samples", "documents", "pdf", name);

    public static ViewerOptions Options() => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
        .WithThreading(ViewerThreading.Cooperative);

    public static async Task<Shell> OpenAsync(string path, double width = 400, double height = 600, double ratio = 1.5)
    {
        var shell = new Shell(width, height, ratio);
#pragma warning disable CA2000 // the controller owns the session (ownsSession: true) and disposes it
        DocumentSession session = DocumentSession.Open(path, Options());
#pragma warning restore CA2000
        await shell.Controller.OpenAsync(_ => Task.FromResult(session), ownsSession: true, null);
        await shell.SettleAsync();
        return shell;
    }

    public static async Task<Shell> OpenAsync(byte[] bytes, double width = 400, double height = 600, double ratio = 1.5)
    {
        var shell = new Shell(width, height, ratio);
#pragma warning disable CA2000 // the controller owns the session (ownsSession: true) and disposes it
        DocumentSession session = DocumentSession.Open(bytes, Options());
#pragma warning restore CA2000
        await shell.Controller.OpenAsync(_ => Task.FromResult(session), ownsSession: true, null);
        await shell.SettleAsync();
        return shell;
    }

    /// <summary>Shows a session the test opened, owned by the test when <paramref name="ownsSession"/> is false (as a host's are).</summary>
    public static async Task<Shell> OpenAsync(DocumentSession session, bool ownsSession, double width = 400, double height = 600, double ratio = 1.5)
    {
        var shell = new Shell(width, height, ratio);
        await shell.Controller.OpenAsync(_ => Task.FromResult(session), ownsSession, null);
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

            if (!more && (Controller.IsLoadingPages || Controller.Search is { IsIndexing: true }))
            {
                // Nothing to draw until a page's text, links and accessibility nodes (or the search's pages) arrive.
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

        Assert.Fail($"The viewer did not settle (more: {more}, loading pages: {Controller.IsLoadingPages}).");
    }

    public void Drag(long id, PointerKind kind, double x0, double y0, double x1, double y1, int steps = 8)
    {
        Controller.PointerDown(id, kind, x0, y0, 1, InputModifiers.None);
        for (int i = 1; i <= steps; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(16));
            Controller.PointerMove(id, x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps));
        }

        Controller.PointerUp(id, x1, y1);
    }

    public void Tap(long id, double x, double y)
    {
        Controller.PointerDown(id, PointerKind.Touch, x, y, 1, InputModifiers.None);
        Clock.Advance(TimeSpan.FromMilliseconds(40));
        Controller.PointerUp(id, x, y);
    }

    /// <summary>A page point's position in device-independent units.</summary>
    public (double X, double Y) ToView(int page, double x, double y)
    {
        PathPoint p = Layout.PageToViewport(Viewport, page, x, y);
        return (p.X / Ratio, p.Y / Ratio);
    }

    public int Count(ViewerFrame.ItemKind kind)
    {
        int count = 0;
        foreach (ViewerFrame.Item item in Frame.Items)
        {
            count += item.Kind == kind ? 1 : 0;
        }

        return count;
    }

    public void RequestFrame()
    {
    }

    public void RequestPaint() => Paints++;

    public void StateChanged(PdfViewState state) => States.Add(state);

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

/// <summary>A presented tile's pixels, copied (the test host's stand-in for the view's frozen bitmap).</summary>
internal sealed class TileCopy(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public byte[] Pixels { get; } = pixels;

    public static object Create(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        for (int y = 0; y < bitmap.Height; y++)
        {
            bitmap.GetRow(y)[..(bitmap.Width * 4)].CopyTo(pixels.AsSpan(y * bitmap.Width * 4));
        }

        return new TileCopy(bitmap.Width, bitmap.Height, pixels);
    }

    /// <summary>The share of pixels that are opaque white (paper) and that are dark (ink), sampled.</summary>
    public (double White, double Dark) Tones()
    {
        int white = 0;
        int dark = 0;
        int samples = 0;
        for (int i = 0; i < Pixels.Length; i += 4 * 7)
        {
            byte b = Pixels[i];
            byte g = Pixels[i + 1];
            byte r = Pixels[i + 2];
            byte a = Pixels[i + 3];
            samples++;
            white += a == 255 && r == 255 && g == 255 && b == 255 ? 1 : 0;
            dark += a == 255 && r < 96 && g < 96 && b < 96 ? 1 : 0;
        }

        return samples == 0 ? (0, 0) : ((double)white / samples, (double)dark / samples);
    }
}

/// <summary>Runs async test bodies on one thread, as the WPF dispatcher runs the controller and its continuations.</summary>
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
