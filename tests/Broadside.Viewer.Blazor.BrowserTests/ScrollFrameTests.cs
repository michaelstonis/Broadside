using System.Globalization;
using System.Text;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The measurements behind <c>docs/benchmarks/viewer-render-worker.md</c> (#357): the demo opens TraceMonkey, pdf.js issue5567 (zoomed
/// in) and issue12841 (a heavy page), with and without the render worker, scrolls with the wheel at a steady pace and records every
/// <c>requestAnimationFrame</c> interval, on a desktop viewport and on a phone viewport with the CPU slowed 4x; plus the worker's boot
/// time, the time to the first tile, the page's memory (WebAssembly memory and JS heap) and the worker's WebAssembly memory. Explicit (wall-clock numbers): run with
/// <c>BROADSIDE_CORPUS_DIR</c> set and the published app under test; <c>BROADSIDE_FRAME_BENCH_LABEL</c> names the build in
/// <c>artifacts/scroll-frames-&lt;label&gt;.md</c>.
/// </summary>
public sealed class ScrollFrameTests(BrowserHost host)
{
    private static readonly (string Name, int Width, int Height, float Scale, bool Mobile, int Throttle)[] Devices =
    [
        ("desktop 1280x900 @2x", 1280, 900, 2, false, 1),
        ("phone 390x844 @3x, CPU/4", 390, 844, 3, true, 4),
    ];

    private static readonly (string File, int ZoomSteps)[] Documents =
        [("pdfjs/tracemonkey.pdf", 0), ("pdfjs/issue5567.pdf", 3), ("pdfjs/issue12841_reduced.pdf", 0)];

    private const string StartFrames = """
        () => {
          globalThis.__frames = [];
          globalThis.__recording = true;
          let last = performance.now();
          const tick = (now) => { globalThis.__frames.push([now, now - last]); last = now; if (globalThis.__recording) requestAnimationFrame(tick); };
          requestAnimationFrame(tick);
        }
        """;

    // The page's WebAssembly memory plus its JS heap (Chromium's performance.memory).
    private const string PageMemory = """
        async () => {
          const runtime = await globalThis.getDotnetRuntime(0);
          return runtime.Module.HEAPU8.byteLength + (performance.memory ? performance.memory.usedJSHeapSize : 0);
        }
        """;

    // The render worker's WebAssembly memory (a worker's JS heap is not exposed to script).
    private const string WorkerMemory = "() => globalThis.__broadsideRuntime ? globalThis.__broadsideRuntime.Module.HEAPU8.byteLength : -1";

    [Fact(Explicit = true)]
    public async Task Measure_main_thread_frames_while_scrolling()
    {
        BrowserHost.RequireApp();
        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus), "BROADSIDE_CORPUS_DIR is not set.");
        string label = Environment.GetEnvironmentVariable("BROADSIDE_FRAME_BENCH_LABEL") ?? "app";
        var table = new StringBuilder();
        table.AppendLine(CultureInfo.InvariantCulture, $"## {label}: rAF intervals while scrolling (ms)");
        table.AppendLine();
        table.AppendLine("| Device | Document | Worker | Frames while scrolling | Mean | p95 | Max | Over 17 ms | Settling 1.5 s after: max, over 17 ms | Worker boot (ms) | First tile (ms) | Page wasm+JS / worker wasm (MiB) |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        // BROADSIDE_FRAME_BENCH_DOCUMENTS replaces the documents: paths separated by ';', absolute or under the corpus directory,
        // shown at the fitted zoom (issue #362 measures the catalogue-class page this way).
        (string File, int ZoomSteps)[] documents = Environment.GetEnvironmentVariable("BROADSIDE_FRAME_BENCH_DOCUMENTS") is { Length: > 0 } list
            ? [.. list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static file => (file, 0))]
            : Documents;
        foreach ((string device, int width, int height, float scale, bool mobile, int throttle) in Devices)
        {
            foreach ((string file, int zoomSteps) in documents)
            {
                string path = Path.Combine(corpus!, file);
                if (!File.Exists(path))
                {
                    continue;
                }

                string served = Path.IsPathRooted(file) ? Path.GetFileName(file) : file.Replace('/', '-');
                host.Serve(served, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
                foreach (bool useWorker in new[] { false, true })
                {
                    Run run = await MeasureAsync(served, zoomSteps, useWorker, width, height, scale, mobile, throttle);
                    double[] frames = [.. run.Frames.Order()];
                    double p95 = frames.Length == 0 ? 0 : frames[Math.Max(0, (int)Math.Ceiling(frames.Length * 0.95) - 1)];
                    table.AppendLine(CultureInfo.InvariantCulture, $"| {device} | {Path.GetFileName(file)} | {run.Worker} | {frames.Length} | {frames.DefaultIfEmpty().Average():0.0} | {p95:0.0} | {(frames.Length == 0 ? 0 : frames[^1]):0} | {frames.Count(f => f > 17)} | {run.Tail.DefaultIfEmpty().Max():0}, {run.Tail.Count(f => f > 17)} | {(run.BootMs < 0 ? "-" : run.BootMs.ToString("0", CultureInfo.InvariantCulture))} | {run.FirstTileMs:0} | {Mib(run.PageBytes)} / {Mib(run.WorkerBytes)} |");
                }
            }
        }

        string artifacts = Path.Combine(FindRoot(), "artifacts");
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, $"scroll-frames-{label}.md"), table.ToString(), TestContext.Current.CancellationToken);
        Viewer.Log(table.ToString());
    }

    private static string Mib(double bytes) => bytes < 0 ? "-" : (bytes / 1048576).ToString("0", CultureInfo.InvariantCulture);

    private async Task<Run> MeasureAsync(string document, int zoomSteps, bool useWorker, int width, int height, float scale, bool mobile, int throttle)
    {
        await using IBrowserContext context = await host.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            DeviceScaleFactor = scale,
            IsMobile = mobile,
            HasTouch = mobile,
        });
        IPage page = await context.NewPageAsync();
        ICDPSession cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = throttle });
        await page.GotoAsync(new Uri(host.AppAddress, $"?file=../files/{Uri.EscapeDataString(document)}{(useWorker ? "" : "&worker=0")}").ToString());
        await page.WaitForFunctionAsync("() => Number(document.querySelector('.bs-viewer')?.dataset.tilesPresented) > 0", null, new PageWaitForFunctionOptions { Timeout = 180_000, PollingInterval = 10 });
        double firstTile = await page.EvaluateAsync<double>("() => performance.now()");
        double boot = await page.EvaluateAsync<double>("() => globalThis.broadsideRenderWorker?.bootMs ?? -1");
        await page.WaitForTimeoutAsync(4000);
        ILocator viewer = page.Locator(".bs-viewer");
        LocatorBoundingBoxResult box = (await viewer.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        for (int i = 0; i < zoomSteps; i++)
        {
            await page.Keyboard.DownAsync("Control");
            await page.Mouse.WheelAsync(0, -100);
            await page.Keyboard.UpAsync("Control");
            await page.WaitForTimeoutAsync(300);
        }

        await page.WaitForTimeoutAsync(2000);
        await page.EvaluateAsync(StartFrames);
        for (int i = 0; i < 60; i++)
        {
            await page.Mouse.WheelAsync(0, mobile ? 60 : 120);
            await page.WaitForTimeoutAsync(16);
        }

        await page.EvaluateAsync("() => { globalThis.__scrollEnd = performance.now(); }");
        await page.WaitForTimeoutAsync(1500);
        double[][] all = await page.EvaluateAsync<double[][]>("() => { globalThis.__recording = false; return globalThis.__frames.slice(1); }");
        double end = await page.EvaluateAsync<double>("() => globalThis.__scrollEnd");

        // A frame belongs to the scroll when it ended before the last wheel event had been handled; the rest is the view settling
        // (the page's text and accessibility load then, once it has been still for a moment).
        double[] frames = [.. all.Where(f => f[0] <= end).Select(f => f[1])];
        double[] tail = [.. all.Where(f => f[0] > end).Select(f => f[1])];
        string worker = await page.EvaluateAsync<string>("() => document.querySelector('.bs-viewer')?.dataset.bsWorker ?? 'no'");
        double pageBytes = await page.EvaluateAsync<double>(PageMemory);
        double workerBytes = -1;
        foreach (IWorker w in page.Workers)
        {
            workerBytes = Math.Max(workerBytes, await w.EvaluateAsync<double>(WorkerMemory));
        }

        return new Run(frames, tail, worker, boot, firstTile, pageBytes, workerBytes);
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private sealed record Run(double[] Frames, double[] Tail, string Worker, double BootMs, double FirstTileMs, double PageBytes, double WorkerBytes);
}
