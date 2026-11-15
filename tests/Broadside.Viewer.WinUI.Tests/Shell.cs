using System.Collections.Concurrent;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Viewer.WinUI.Tests;

/// <summary>
/// A test host for the WinUI shell's controller: what PdfView does on Windows, minus WinUI. Sessions are cooperative (tiles render
/// inside <see cref="ViewerController.Frame"/>), the clock is manual, continuations run on the test's own thread, and tile images
/// are managed pixels a small compositor draws as the control's image elements would.
/// </summary>
internal sealed class Shell : IViewerHost, ITileImageFactory, IDisposable
{
    /// <summary>The view's background around the pages (the control's light theme).</summary>
    public const uint Background = 0xFFE8E8EC;

    private Shell(double width, double height, double ratio)
    {
        Controller = new ViewerController(this, this, Clock) { SchedulerObserver = Events.Add };
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

    public List<PdfViewState> States { get; } = [];

    public int Presents { get; private set; }

    public Viewport Viewport => Controller.Input!.Viewport;

    public PageLayout Layout => Controller.Zoom!.Layout;

    public static string CorpusFile(string name) => Path.Combine(Corpus.Directory, name);

    /// <summary>A representative sample document from samples/documents/pdf (#363).</summary>
    public static string SampleDocument(string name) => Path.Combine(Corpus.Directory, "..", "..", "samples", "documents", "pdf", name);

    public static ViewerOptions Options() => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
        .WithThreading(ViewerThreading.Cooperative);

    public static async Task<Shell> OpenAsync(string path, double width = 400, double height = 600, double ratio = 1.5, bool sizeFirst = true)
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

    /// <summary>
    /// Runs frames (16 ms apart on the test clock) until the controller needs no more and no page load or search is pending. Page loads
    /// and indexing run through the session's own tasks, so a slow runner gets real time for them: the limit is wall-clock time.
    /// </summary>
    public async Task SettleAsync()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool more = true;
        while (watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            bool busy = Controller.IsLoadingPages || Controller.Search?.IsIndexing == true;
            if (more || !busy)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                more = Controller.Frame();
            }

            busy = Controller.IsLoadingPages || Controller.Search?.IsIndexing == true;
            if (!more && busy)
            {
                await Task.Delay(2);
                more = true;
                continue;
            }

            await Task.Yield();
            if (!more && !busy)
            {
                // One more round: a page load may have completed and woken the controller.
                Clock.Advance(TimeSpan.FromMilliseconds(16));
                more = Controller.Frame();
                if (!more && !Controller.IsLoadingPages && Controller.Search?.IsIndexing != true)
                {
                    Assert.Empty(Errors);
                    return;
                }
            }
        }

        Assert.Fail($"The viewer did not settle (more: {more}, loading pages: {Controller.IsLoadingPages}).");
    }

    public void Press(long id, PointerKind kind, double x, double y, InputModifiers modifiers = InputModifiers.None) =>
        Controller.PointerPressed(id, kind, x, y, modifiers);

    public void Drag(long id, PointerKind kind, double x0, double y0, double x1, double y1, int steps = 8)
    {
        Press(id, kind, x0, y0);
        for (int i = 1; i <= steps; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(16));
            Controller.PointerMoved(id, kind, x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps), inContact: true);
        }

        Controller.PointerUp(id, x1, y1);
    }

    public void Tap(long id, double x, double y)
    {
        Press(id, PointerKind.Touch, x, y);
        Clock.Advance(TimeSpan.FromMilliseconds(40));
        Controller.PointerUp(id, x, y);
    }

    /// <summary>A page point's position in device-independent units.</summary>
    public (double X, double Y) ToView(int page, double x, double y)
    {
        PathPoint p = Layout.PageToViewport(Viewport, page, x, y);
        return (p.X / Ratio, p.Y / Ratio);
    }

    /// <summary>
    /// Composes the newest frame into 0xAARRGGBB pixels as the control's elements show it: background, paper, then tiles stretched over
    /// their destinations (nearest sample), source-over.
    /// </summary>
    public Pixels Draw()
    {
        ViewerFrame frame = Controller.LatestFrame;
        int width = Math.Max((int)frame.Width, 16);
        int height = Math.Max((int)frame.Height, 16);
        var pixels = new Pixels(width, height);
        Array.Fill(pixels.Data, frame.IsDark ? 0xFF2B2B2Bu : Background);
        foreach (ViewerFrame.Item item in frame.Items)
        {
            LayoutRectangle d = item.Destination;
            switch (item.Kind)
            {
                case ViewerFrame.ItemKind.Paper:
                    pixels.Fill(d, frame.IsDark ? 0xFF000000u : 0xFFFFFFFFu);
                    break;
                case ViewerFrame.ItemKind.Tile when item.Image is TestImage image:
                    pixels.Blit(d, image);
                    break;
            }
        }

        return pixels;
    }

    public ITileImage Create(int width, int height, ReadOnlySpan<byte> bgra, int stride)
    {
        var image = new TestImage(width, height);
        for (int y = 0; y < height; y++)
        {
            bgra.Slice(y * stride, width * 4).CopyTo(image.Bgra.AsSpan(y * width * 4));
        }

        return image;
    }

    public void RequestFrame()
    {
    }

    public void Present(ViewerFrame frame) => Presents++;

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

    internal sealed class TestImage(int width, int height) : ITileImage
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public byte[] Bgra { get; } = new byte[width * height * 4];
    }

    /// <summary>Composed pixels, 0xAARRGGBB.</summary>
    internal sealed class Pixels(int width, int height)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public uint[] Data { get; } = new uint[width * height];

        public uint this[int x, int y] => Data[(y * Width) + x];

        public void Fill(LayoutRectangle r, uint color)
        {
            for (int y = Math.Max((int)r.Y, 0); y < Math.Min((int)r.Bottom, Height); y++)
            {
                for (int x = Math.Max((int)r.X, 0); x < Math.Min((int)r.Right, Width); x++)
                {
                    Data[(y * Width) + x] = color;
                }
            }
        }

        public void Blit(LayoutRectangle r, TestImage image)
        {
            if (r.Width <= 0 || r.Height <= 0)
            {
                return;
            }

            for (int y = Math.Max((int)r.Y, 0); y < Math.Min((int)r.Bottom, Height); y++)
            {
                int sy = Math.Clamp((int)((y - r.Y + 0.5) * image.Height / r.Height), 0, image.Height - 1);
                for (int x = Math.Max((int)r.X, 0); x < Math.Min((int)r.Right, Width); x++)
                {
                    int sx = Math.Clamp((int)((x - r.X + 0.5) * image.Width / r.Width), 0, image.Width - 1);
                    int o = ((sy * image.Width) + sx) * 4;
                    uint a = image.Bgra[o + 3];
                    uint dst = Data[(y * Width) + x];
                    uint Channel(int shift, int source) => (uint)image.Bgra[o + source] + (((dst >> shift) & 0xFF) * (255 - a) / 255);
                    Data[(y * Width) + x] = 0xFF000000u | (Channel(16, 2) << 16) | (Channel(8, 1) << 8) | Channel(0, 0);
                }
            }
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

/// <summary>Runs async test bodies on one thread, as the WinUI UI thread runs the controller and its continuations.</summary>
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
