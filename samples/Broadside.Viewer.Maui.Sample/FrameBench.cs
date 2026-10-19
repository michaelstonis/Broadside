using System.Diagnostics;
using System.Globalization;
using Broadside.Viewer.Maui;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// The UI-thread frame bench (#357): BROADSIDE_FRAMES=1 (iOS simulator: SIMCTL_CHILD_BROADSIDE_FRAMES=1), or the Android intent
/// extra "frames". It scrolls each bench document at a steady speed while a probe thread measures how long the UI thread takes to
/// pick up a work item every 4 ms: the longest wait is the longest UI-thread frame. Prints one BROADSIDE_FRAMES line per document.
/// </summary>
public static class FrameBench
{
    public static bool Requested { get; set; } = Environment.GetEnvironmentVariable("BROADSIDE_FRAMES") == "1";

    /// <summary>The bench documents: TraceMonkey at fit width, and pdf.js issue5567 (a large JPX image) zoomed in when it was bundled.</summary>
    public static readonly (string File, double Zoom, double Step)[] Documents =
    [
        ("documents/tracemonkey.pdf", 1, 24),
        ("documents/issue5567.pdf", 3, 12),
    ];

    public static async Task RunAsync(PdfView view, Func<string, Task<bool>> open)
    {
        foreach ((string file, double zoom, double step) in Documents)
        {
            if (!await open(file))
            {
                Console.WriteLine($"BROADSIDE_FRAMES {file} missing");
                continue;
            }

            await IdleAsync(view);
            if (zoom != 1)
            {
                view.ZoomBy(zoom);
                await IdleAsync(view);
            }

            string result = await ScrollAsync(view, step);
            Console.WriteLine($"BROADSIDE_FRAMES {Path.GetFileName(file)} {result}");
        }

        Console.WriteLine("BROADSIDE_FRAMES_DONE");
        Application.Current?.Quit();
    }

    private static async Task IdleAsync(PdfView view)
    {
        var watch = Stopwatch.StartNew();
        await Task.Delay(500);
        while ((view.State.IsRendering || view.State.IsLoading || view.State.PageCount == 0) && watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50);
        }

        await Task.Delay(500);
    }

    private static async Task<string> ScrollAsync(PdfView view, double step)
    {
        IDispatcher dispatcher = view.Dispatcher;
        var waits = new List<double>();
        using var stop = new CancellationTokenSource();
        var probe = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                long posted = Stopwatch.GetTimestamp();
                using var picked = new ManualResetEventSlim();
                dispatcher.Dispatch(() =>
                {
                    double ms = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
                    lock (waits)
                    {
                        waits.Add(ms);
                    }

                    picked.Set();
                });
                picked.Wait(TimeSpan.FromSeconds(5));
                Thread.Sleep(4);
            }
        }) { IsBackground = true, Name = "frame probe" };
        probe.Start();

        // Four seconds of steady scrolling (60 steps a second), then the tail of the work it started.
        var clock = Stopwatch.StartNew();
        int ticks = 0;
        while (clock.Elapsed < TimeSpan.FromSeconds(4))
        {
            view.ScrollBy(0, step);
            ticks++;
            await Task.Delay(16);
        }

        await Task.Delay(1000);
        await stop.CancelAsync();
        probe.Join();
        double[] sorted;
        lock (waits)
        {
            sorted = [.. waits.Order()];
        }

        double p99 = sorted.Length == 0 ? 0 : sorted[(int)(sorted.Length * 0.99)];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"samples={sorted.Length} scrolls={ticks} mean={sorted.DefaultIfEmpty().Average():F2}ms p99={p99:F1}ms max={(sorted.Length == 0 ? 0 : sorted[^1]):F1}ms over16={sorted.Count(ms => ms > 16)} page={view.State.PageIndex + 1}/{view.State.PageCount} gpu={view.UseHardwareAcceleration}");
    }
}
