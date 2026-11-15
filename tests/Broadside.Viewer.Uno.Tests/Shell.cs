using System.Collections.Concurrent;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.Uno.Tests;

/// <summary>
/// A test host for the Uno shell's controller: what PdfView does on a head, minus Uno. Sessions are cooperative (tiles render inside
/// <see cref="ViewerController.Frame"/>, as on the WebAssembly head), the clock is manual, and continuations run on the test's own thread.
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

    public ConcurrentQueue<TileSchedulerEvent> EventQueue { get; } = new();

    public List<TileSchedulerEvent> Events { get; } = [];

    public List<ViewerActionEventArgs> Actions { get; } = [];

    public List<FieldEditor?> Editors { get; } = [];

    public List<AccessibilityChange> AccessibilityChanges { get; } = [];

    public List<string> Copied { get; } = [];

    public List<Exception> Errors { get; } = [];

    public List<PdfViewState> States { get; } = [];

    public int Paints { get; private set; }

    public int FrameRequests { get; private set; }

    public Viewport Viewport => Controller.Input!.Viewport;

    public PageLayout Layout => Controller.Zoom!.Layout;

    public static string CorpusFile(string name) => Path.Combine(Corpus.Directory, name);

    /// <summary>A representative sample document from samples/documents/pdf (#363).</summary>
    public static string SampleDocument(string name) => Path.Combine(Corpus.Directory, "..", "..", "samples", "documents", "pdf", name);

    public static ViewerOptions Options(ViewerThreading threading = ViewerThreading.Cooperative) => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
        .WithThreading(threading);

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
    public async Task SettleAsync(Func<bool>? until = null)
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

            if ((!more && Controller.IsLoadingPages) || (until is not null && !until()))
            {
                await Task.Delay(2);
                more = true;
                continue;
            }

            await Task.Yield();
            if (!more && !Controller.IsLoadingPages)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                more = Controller.Frame();
                if (!more && !Controller.IsLoadingPages)
                {
                    Assert.Empty(Errors);
                    return;
                }
            }
        }

        Assert.Fail($"The viewer did not settle (more: {more}, loading pages: {Controller.IsLoadingPages}): stats=" + Controller.Scheduler!.Statistics);
    }

    public void Down(long id, PointerKind kind, double x, double y, InputModifiers modifiers = InputModifiers.None) =>
        Controller.PointerDown(id, kind, x, y, modifiers);

    public void Move(long id, PointerKind kind, double x, double y) => Controller.PointerMove(id, kind, x, y, inContact: true);

    public void Up(long id, double x, double y) => Controller.PointerUp(id, x, y);

    public void Drag(long id, PointerKind kind, double x0, double y0, double x1, double y1, int steps = 8)
    {
        Down(id, kind, x0, y0);
        for (int i = 1; i <= steps; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(16));
            Move(id, kind, x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps));
        }

        Up(id, x1, y1);
    }

    public void Tap(long id, double x, double y)
    {
        Down(id, PointerKind.Touch, x, y);
        Clock.Advance(TimeSpan.FromMilliseconds(40));
        Up(id, x, y);
    }

    /// <summary>A page point's position in device-independent units.</summary>
    public (double X, double Y) ToView(int page, double x, double y)
    {
        PathPoint p = Layout.PageToViewport(Viewport, page, x, y);
        return (p.X / Ratio, p.Y / Ratio);
    }

    /// <summary>Composes the latest frame's background, paper and tiles into 0xAARRGGBB pixels, as the view shows them.</summary>
    public Raster Draw()
    {
        ViewerFrame frame = Controller.LatestFrame;
        var raster = new Raster(Math.Max((int)frame.Width, 16), Math.Max((int)frame.Height, 16));
        raster.Fill(new LayoutRectangle(0, 0, raster.Width, raster.Height), frame.IsDark ? ViewerColors.DarkBackground : ViewerColors.Background);
        foreach (FrameItem item in frame.Items)
        {
            switch (item.Kind)
            {
                case FrameItemKind.Paper:
                    raster.Fill(item.Bounds, frame.IsDark ? ViewerColors.DarkPaper : ViewerColors.Paper);
                    break;
                case FrameItemKind.Tile when item.Pixels is { } pixels:
                    raster.DrawTile(item.Bounds, pixels);
                    break;
            }
        }

        return raster;
    }

    public void RequestFrame() => FrameRequests++;

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

/// <summary>0xAARRGGBB pixels of a composed frame (opaque: tiles are drawn source-over onto opaque paper).</summary>
internal sealed class Raster(int width, int height)
{
    private readonly uint[] _pixels = new uint[width * height];

    public int Width { get; } = width;

    public int Height { get; } = height;

    public uint this[int x, int y] => _pixels[(y * Width) + x];

    public void Fill(LayoutRectangle r, uint color)
    {
        for (int y = Math.Max((int)r.Y, 0); y < Math.Min((int)r.Bottom, Height); y++)
        {
            for (int x = Math.Max((int)r.X, 0); x < Math.Min((int)r.Right, Width); x++)
            {
                _pixels[(y * Width) + x] = color;
            }
        }
    }

    public void DrawTile(LayoutRectangle r, TilePixels tile)
    {
        ReadOnlySpan<byte> source = tile.Pixels;
        for (int y = Math.Max((int)r.Y, 0); y < Math.Min((int)r.Bottom, Height); y++)
        {
            int sy = Math.Clamp((int)((y - r.Y) * tile.Height / r.Height), 0, tile.Height - 1);
            for (int x = Math.Max((int)r.X, 0); x < Math.Min((int)r.Right, Width); x++)
            {
                int sx = Math.Clamp((int)((x - r.X) * tile.Width / r.Width), 0, tile.Width - 1);
                int o = (sy * tile.Stride) + (sx * 4);
                uint a = source[o + 3];
                uint dst = _pixels[(y * Width) + x];

                // Premultiplied source over an opaque destination.
                uint Blend(uint s, int shift) => s + ((((dst >> shift) & 0xFF) * (255 - a)) / 255);
                uint rr = Blend(source[o + 2], 16);
                uint gg = Blend(source[o + 1], 8);
                uint bb = Blend(source[o], 0);
                _pixels[(y * Width) + x] = 0xFF000000 | (rr << 16) | (gg << 8) | bb;
            }
        }
    }
}

/// <summary>Runs async test bodies on one thread, as the Uno UI thread runs the controller and its continuations.</summary>
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
