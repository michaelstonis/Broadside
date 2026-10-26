using System.Diagnostics;
using System.Globalization;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;
using Broadside.Viewer.Maui;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// The large-photo bench (#428): BROADSIDE_PHOTO=1 (iOS simulator: SIMCTL_CHILD_BROADSIDE_PHOTO=1), or the Android intent extra
/// "photo". It opens the generated catalogue-class page (a 2550 x 3150 DCT photograph over a letter page,
/// <see cref="CatalogueSamples.Cover"/>; never a real catalogue) at fit width and prints one BROADSIDE_PHOTO line: the tiles drawn,
/// their total and longest draw time (the backend's own work, timed around it), the time until the view was idle, and the managed
/// heap and decoded-image cache afterwards (process memory is sampled from outside: iOS does not report it to the app), then quits.
/// One measurement per launch, so each starts from a fresh process. BROADSIDE_PHOTO_WIDTH narrows the view to a share of the
/// window (a phone at 3 pixels per point reduces the cover at fit width only in a narrower view, or with BROADSIDE_PHOTO_K=1);
/// BROADSIDE_PHOTO_FULL=1 turns scaled image decode off, for comparison.
/// </summary>
public static class PhotoBench
{
    public static bool Requested { get; set; } = Environment.GetEnvironmentVariable("BROADSIDE_PHOTO") == "1";

    public static bool FullDecode { get; set; } = Environment.GetEnvironmentVariable("BROADSIDE_PHOTO_FULL") == "1";

    /// <summary>Gets or sets the share of the window's width the view takes (BROADSIDE_PHOTO_WIDTH, 1 by default).</summary>
    public static double Width { get; set; } = double.TryParse(Environment.GetEnvironmentVariable("BROADSIDE_PHOTO_WIDTH"), CultureInfo.InvariantCulture, out double width) ? width : 1;

    /// <summary>Gets or sets the samples per device pixel a reduced image keeps (BROADSIDE_PHOTO_K, 2 by default).</summary>
    public static double SamplesPerPixel { get; set; } = double.TryParse(Environment.GetEnvironmentVariable("BROADSIDE_PHOTO_K"), CultureInfo.InvariantCulture, out double k) ? k : RenderOptions.DefaultImageSamplesPerPixel;

    public static async Task RunAsync(PdfView view, Action<DocumentSession> show)
    {
        try
        {
            await RunCoreAsync(view, show);
        }
        catch (Exception exception)
        {
            Console.WriteLine("BROADSIDE_PHOTO_FAILED " + exception);
        }

        Console.WriteLine("BROADSIDE_PHOTO_DONE");
        Application.Current?.Quit();
    }

    private static async Task RunCoreAsync(PdfView view, Action<DocumentSession> show)
    {
        byte[] file = await Task.Run(() => CatalogueSamples.Cover());

        // Encoding the photograph takes memory of its own: settle it, and report where the process stands before the viewer opens.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(1000);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"BROADSIDE_PHOTO ready decode={(FullDecode ? "full" : "scaled")} managed={GC.GetTotalMemory(false) / 1048576}MB"));
        if (Width < 1)
        {
            // A narrower view (a thumbnail, a split screen): the page is drawn smaller, so the photograph is reduced at fit width.
            view.HorizontalOptions = LayoutOptions.Start;
            view.WidthRequest = view.Width * Width;
            await Task.Delay(500);
        }

        var timings = new TileTimings();
        ViewerOptions options = new ViewerOptions()
            .UseEngine(new PdfEngine(new PdfOptions().UseStandard14Fonts()))
            .WithScaledImageDecode(!FullDecode)
            .WithImageSamplesPerPixel(SamplesPerPixel)
            .UseBackend(() => new TimedTileBackend(new SkiaTileBackend(!FullDecode, SamplesPerPixel), timings));
        DocumentSession session = DocumentSession.Open(file, options);
        var clock = Stopwatch.StartNew();
        show(session);
        await IdleAsync(view);
        double idle = clock.Elapsed.TotalMilliseconds - 500;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"BROADSIDE_PHOTO fit decode={(FullDecode ? "full" : "scaled")} k={SamplesPerPixel} width={view.Width:F0}pt zoom={view.State.Zoom:F2} tiles={timings.Count} tileTotal={timings.TotalMs:F0}ms tileMax={timings.MaxMs:F0}ms idle={idle:F0}ms managed={GC.GetTotalMemory(false) / 1048576}MB imageCache={session.Document.CacheStatistics.Images.Bytes / 1048576}MB"));
    }

    /// <summary>Waits until the view has drawn everything it shows, then half a second more (it returns that much late).</summary>
    private static async Task IdleAsync(PdfView view)
    {
        var watch = Stopwatch.StartNew();
        await Task.Delay(100);
        while ((view.State.IsRendering || view.State.IsLoading || view.State.PageCount == 0) && watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(10);
        }

        await Task.Delay(500);
    }

    /// <summary>Draw times of the tiles since the last reset.</summary>
    private sealed class TileTimings
    {
        private readonly Lock _gate = new();
        private long _ticks;
        private long _max;

        public int Count { get; private set; }

        public double TotalMs => TimeSpan.FromTicks(Interlocked.Read(ref _ticks)).TotalMilliseconds;

        public double MaxMs => TimeSpan.FromTicks(Interlocked.Read(ref _max)).TotalMilliseconds;

        public void Reset()
        {
            lock (_gate)
            {
                Count = 0;
                _ticks = 0;
                _max = 0;
            }
        }

        public void Add(TimeSpan elapsed)
        {
            lock (_gate)
            {
                Count++;
                _ticks += elapsed.Ticks;
                _max = Math.Max(_max, elapsed.Ticks);
            }
        }
    }

    /// <summary>Times each tile's slices around another backend, its prefetch included.</summary>
    private sealed class TimedTileBackend(ITileBackend inner, TileTimings timings) : ITileBackend
    {
        public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
            new Timed(inner.Begin(list, target, deviceTransform, cancellationToken), timings);

        public ValueTask PrefetchImagesAsync(DisplayList list, in Matrix deviceTransform, int width, int height, CancellationToken cancellationToken) =>
            inner.PrefetchImagesAsync(list, deviceTransform, width, height, cancellationToken);

        public void Dispose() => inner.Dispose();

        private sealed class Timed(ITileRender render, TileTimings timings) : ITileRender
        {
            private TimeSpan _elapsed;

            public bool RenderSlice(TimeSpan budget)
            {
                long start = Stopwatch.GetTimestamp();
                bool done = render.RenderSlice(budget);
                _elapsed += Stopwatch.GetElapsedTime(start);
                if (done)
                {
                    timings.Add(_elapsed);
                }

                return done;
            }

            public void ReadBack() => render.ReadBack();

            public void Dispose() => render.Dispose();
        }
    }
}
