using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.TestSupport;
using Broadside.Viewer.Avalonia.Tests;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Broadside.Viewer.Avalonia.Tests;

/// <summary>The headless application every test runs in: Avalonia's headless platform with real Skia drawing, so frames can be read back.</summary>
public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>Runs test bodies on the headless application's UI thread (one dispatcher for the whole assembly).</summary>
internal static class Ui
{
    private static HeadlessUnitTestSession Session => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Ui).Assembly);

    public static Task RunAsync(Func<Task> body) =>
        Session.Dispatch(async () =>
        {
            await body();
            return true;
        }, TestContext.Current.CancellationToken);
}

/// <summary>
/// A <see cref="PdfView"/> in a headless window, on a manual clock: input goes through the window as real Avalonia input events, frames
/// run through the control's animation-frame callback, and the window's rendered frame is read back as pixels.
/// </summary>
internal sealed class ViewerHarness : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<TileSchedulerEvent> _events = [];

    private ViewerHarness(double width, double height, ViewerThreading threading, bool showAnnotations)
    {
        Viewer = new PdfView(Clock) { ShowAnnotations = showAnnotations, Options = Options(threading) };
        Viewer.Controller.SchedulerObserver = e =>
        {
            lock (_gate)
            {
                _events.Add(e);
                if (e.Kind == TileEventKind.Presented)
                {
                    PresentingThreads.Add(Environment.CurrentManagedThreadId);
                }
            }
        };
        Viewer.ActionRequested += (_, e) => Actions.Add(e);
        Viewer.Error += (_, e) => Errors.Add(e.Exception);
        Window = new Window { Width = width, Height = height, Content = Viewer };
        Window.Show();
    }

    public ManualClock Clock { get; } = new();

    public Window Window { get; }

    public PdfView Viewer { get; }

    public ViewerController Controller => Viewer.Controller;

    public List<ViewerActionEventArgs> Actions { get; } = [];

    public List<Exception> Errors { get; } = [];

    public HashSet<int> PresentingThreads { get; } = [];

    public Viewport Viewport => Controller.Input!.Viewport;

    public PageLayout Layout => Controller.Zoom!.Layout;

    public double Scaling => Controller.PixelRatio;

    public static string CorpusFile(string name) => Path.Combine(Corpus.Directory, name);

    /// <summary>A representative sample document from samples/documents/pdf.</summary>
    public static string SampleDocument(string name) => Path.Combine(RepositoryRoot, "samples", "documents", "pdf", name);

    /// <summary>The repository's root (tests/Corpus is two levels below it).</summary>
    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", ".."));

    /// <summary>The real-world corpus: BROADSIDE_CORPUS_DIR, else the nearest corpus/ folder above the repository (a worktree's main checkout).</summary>
    public static string? RealWorldCorpus { get; } = FindRealWorldCorpus();

    /// <summary>A real-world corpus file, or null when the corpus or the file is absent.</summary>
    public static string? RealWorldFile(string path) =>
        RealWorldCorpus is { } root && File.Exists(Path.Combine(root, path)) ? Path.Combine(root, path) : null;

    private static string? FindRealWorldCorpus()
    {
        if (Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR") is { Length: > 0 } configured)
        {
            return Directory.Exists(configured) ? configured : null;
        }

        for (DirectoryInfo? directory = new(RepositoryRoot); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "corpus");
            if (Directory.Exists(Path.Combine(candidate, "pdfjs")))
            {
                return candidate;
            }
        }

        return null;
    }

    public static ViewerOptions Options(ViewerThreading threading) => new ViewerOptions()
        .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
        .WithThreading(threading);

    public List<TileSchedulerEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public void ClearEvents()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    public static async Task<ViewerHarness> OpenAsync(
        string path,
        double width = 600,
        double height = 800,
        double scaling = 1,
        ViewerThreading threading = ViewerThreading.Cooperative,
        bool showAnnotations = true)
    {
        var harness = new ViewerHarness(width, height, threading, showAnnotations);
        if (scaling != 1)
        {
            harness.Window.SetRenderScaling(scaling);
        }

        harness.Viewer.Source = path;
        await harness.SettleAsync();
        Assert.True(harness.Viewer.State.PageCount > 0, $"{path} did not open");
        return harness;
    }

    /// <summary>
    /// Advances the clock 16 ms per frame and lets the headless platform tick its render timer (which runs the viewer's animation-frame
    /// callback and renders the window) until the viewer wants no frame, loads no page and draws no tile. Page loads and background
    /// tiles take real time, so the limit is wall-clock time, not a frame count.
    /// </summary>
    public async Task SettleAsync()
    {
        var watch = Stopwatch.StartNew();
        int quiet = 0;
        while (watch.Elapsed < TimeSpan.FromMinutes(2))
        {
            Tick();
            bool busy = Viewer.IsFramePending || Controller.IsLoadingPages || Viewer.State.IsLoading
                || (Controller.Scheduler is { } scheduler && !scheduler.IsIdle);
            quiet = busy ? 0 : quiet + 1;
            if (quiet >= 3)
            {
                Assert.Empty(Errors);
                Assert.DoesNotContain(Events, e => e.Kind == TileEventKind.Failed);
                return;
            }

            await Task.Delay(busy ? 1 : 0);
        }

        Assert.Fail($"The viewer did not settle (frame pending: {Viewer.IsFramePending}, loading pages: {Controller.IsLoadingPages}, state: {Viewer.State}, scheduler: {Controller.Scheduler?.Statistics})");
    }

    /// <summary>One frame: 16 ms pass, the render timer ticks, the dispatcher runs what it queued.</summary>
    public void Tick()
    {
        Clock.Advance(TimeSpan.FromMilliseconds(16));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The window's next rendered frame, in device pixels.</summary>
    public FramePixels Capture()
    {
        using WriteableBitmap frame = Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
        return FramePixels.From(frame);
    }

    /// <summary>A page point's position in device-independent units from the viewer's top-left.</summary>
    public Point ToView(int page, double x, double y)
    {
        PathPoint p = Layout.PageToViewport(Viewport, page, x, y);
        return new Point(p.X / Scaling, p.Y / Scaling);
    }

    public void Drag(Point from, Point to, int steps = 8, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseDown(from, MouseButton.Left, modifiers);
        for (int i = 1; i <= steps; i++)
        {
            Clock.Advance(TimeSpan.FromMilliseconds(16));
            Window.MouseMove(from + ((to - from) * i / steps), modifiers | RawInputModifiers.LeftMouseButton);
        }

        Window.MouseUp(to, MouseButton.Left, modifiers);
    }

    public void Click(Point at)
    {
        Window.MouseDown(at, MouseButton.Left);
        Clock.Advance(TimeSpan.FromMilliseconds(40));
        Window.MouseUp(at, MouseButton.Left);
    }

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    public void Dispose()
    {
        Window.Close();
        Viewer.Dispose();
    }

    internal sealed class ManualClock : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _now);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _now, by.Ticks);
    }
}

/// <summary>A rendered frame's pixels as straight BGRA, for comparisons.</summary>
internal sealed class FramePixels
{
    private readonly byte[] _pixels;

    private FramePixels(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public static unsafe FramePixels From(WriteableBitmap bitmap)
    {
        using ILockedFramebuffer locked = bitmap.Lock();
        int width = locked.Size.Width;
        int height = locked.Size.Height;
        byte[] pixels = new byte[width * height * 4];
        bool rgba = locked.Format == PixelFormat.Rgba8888;
        for (int y = 0; y < height; y++)
        {
            var row = new ReadOnlySpan<byte>((byte*)locked.Address + ((long)y * locked.RowBytes), width * 4);
            for (int x = 0; x < width; x++)
            {
                int o = ((y * width) + x) * 4;
                pixels[o] = rgba ? row[(x * 4) + 2] : row[x * 4];
                pixels[o + 1] = row[(x * 4) + 1];
                pixels[o + 2] = rgba ? row[x * 4] : row[(x * 4) + 2];
                pixels[o + 3] = row[(x * 4) + 3];
            }
        }

        return new FramePixels(pixels, width, height);
    }

    /// <summary>Copies a tile bitmap's premultiplied BGRA pixels (opaque content tiles: equal to straight).</summary>
    public static unsafe FramePixels From(AvaloniaBitmap bitmap)
    {
        PixelSize size = bitmap.PixelSize;
        byte[] pixels = new byte[size.Width * size.Height * 4];
        fixed (byte* p = pixels)
        {
            bitmap.CopyPixels(new PixelRect(size), (IntPtr)p, pixels.Length, size.Width * 4);
        }

        return new FramePixels(pixels, size.Width, size.Height);
    }

    public (byte B, byte G, byte R, byte A) this[int x, int y]
    {
        get
        {
            int o = ((y * Width) + x) * 4;
            return (_pixels[o], _pixels[o + 1], _pixels[o + 2], _pixels[o + 3]);
        }
    }

    public static int Difference((byte B, byte G, byte R, byte A) a, (byte B, byte G, byte R, byte A) b) =>
        Math.Max(Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.G - b.G)), Math.Abs(a.R - b.R));
}
