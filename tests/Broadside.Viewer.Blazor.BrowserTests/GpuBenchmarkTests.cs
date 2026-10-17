using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The measurements behind <c>docs/benchmarks/viewer-gpu-vs-software.md</c> (#353): the demo opens real-world documents with the
/// software rasterizer and with WebGL, and records the time to the first sharp page, frame times while scrolling and zooming, tile
/// render times, fallbacks and memory, on a desktop viewport and on a phone viewport with the CPU slowed 4x. Explicit: run with
/// <c>BROADSIDE_CORPUS_DIR</c> set (the pdf.js corpus) and the published app under test, e.g.
/// <c>dotnet test --project tests/Broadside.Viewer.Blazor.BrowserTests -- --filter-class "*GpuBenchmarkTests" --explicit only</c>;
/// <c>BROADSIDE_GPU_BENCH_LABEL</c> names the build (aot, no-aot) in the table written to <c>artifacts/gpu-bench-&lt;label&gt;.md</c>.
/// </summary>
public sealed class GpuBenchmarkTests(BrowserHost host)
{
    private static readonly string[] Documents = ["pdfjs/tracemonkey.pdf", "pdfjs/issue5567.pdf", "pdfjs/personwithdog.pdf"];

    private static readonly (string Name, int Width, int Height, float Scale, bool Mobile, int Throttle)[] Devices =
    [
        ("desktop 1280x900 @2x", 1280, 900, 2, false, 1),
        ("phone 390x844 @3x, CPU/4", 390, 844, 3, true, 4),
    ];

    private const string Snapshot = """
        async () => {
          const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
          const s = JSON.parse(exports.Broadside.Viewer.Blazor.Sample.RenderStats.Snapshot());
          s.wasmHeap = getDotnetRuntime(0).Module.HEAPU8.byteLength;
          s.jsHeap = performance.memory ? performance.memory.usedJSHeapSize : 0;
          return JSON.stringify(s);
        }
        """;

    private const string StartFrames = """
        async () => {
          const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
          exports.Broadside.Viewer.Blazor.Sample.RenderStats.ResetStatistics();
          globalThis.__frames = [];
          globalThis.__recording = true;
          let last = performance.now();
          const tick = (now) => { globalThis.__frames.push(now - last); last = now; if (globalThis.__recording) requestAnimationFrame(tick); };
          requestAnimationFrame(tick);
        }
        """;

    [Fact(Explicit = true)]
    public async Task Measure_the_viewer_with_the_software_rasterizer_and_with_WebGL()
    {
        BrowserHost.RequireApp();
        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus), "BROADSIDE_CORPUS_DIR is not set.");
        string label = Environment.GetEnvironmentVariable("BROADSIDE_GPU_BENCH_LABEL") ?? "app";
        var table = new StringBuilder();
        table.AppendLine(CultureInfo.InvariantCulture, $"## {label}");
        table.AppendLine();
        table.AppendLine("| Device | Document | Renderer | First sharp page (ms) | Scroll frames mean / p95 / max (ms), >50 ms | Zoom frames mean / p95 / max (ms), >50 ms | Tiles: mean / p95 / max (ms) | Fallback tiles | WASM heap / JS heap / textures (MiB) |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|");
        var lines = new List<string>();
        foreach ((string device, int width, int height, float scale, bool mobile, int throttle) in Devices)
        {
            foreach (string document in Documents)
            {
                string path = Path.Combine(corpus!, document);
                if (!File.Exists(path))
                {
                    continue;
                }

                string served = document.Replace('/', '-');
                host.Serve(served, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
                foreach (string renderer in new[] { "software", "webgl" })
                {
                    Run run = await MeasureAsync(served, renderer, width, height, scale, mobile, throttle);
                    lines.Add(run.Json);
                    table.AppendLine(CultureInfo.InvariantCulture, $"| {device} | {Path.GetFileName(document)} | {renderer} | {run.FirstSharp:0} | {run.Scroll} | {run.Zoom} | {run.Tiles} | {run.Fallbacks} | {run.Memory} |");
                }
            }
        }

        string output = Path.Combine(FindArtifacts(), $"gpu-bench-{label}.md");
        await File.WriteAllTextAsync(output, table.ToString(), TestContext.Current.CancellationToken);
        await File.WriteAllLinesAsync(Path.ChangeExtension(output, ".jsonl"), lines, TestContext.Current.CancellationToken);
        Viewer.Log(table.ToString());
    }

    /// <summary>
    /// Whole pages of the rendering gate's pinned set (tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt) drawn outside
    /// the viewer at 144 DPI by each renderer: the median of three renders after a warm-up, and what fell back.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Measure_the_pinned_pages_with_the_software_rasterizer_and_with_WebGL()
    {
        BrowserHost.RequireApp();
        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus), "BROADSIDE_CORPUS_DIR is not set.");
        string label = Environment.GetEnvironmentVariable("BROADSIDE_GPU_BENCH_LABEL") ?? "app";
        string pinned = Path.Combine(FindArtifacts(), "..", "tests", "Broadside.Rendering.Tests", "CorpusGate", "pinned-pages.txt");
        await using IBrowserContext context = await host.Browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        await page.GotoAsync(host.AppAddress.ToString());
        await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, new PageWaitForFunctionOptions { Timeout = 180_000 });
        var table = new StringBuilder();
        table.AppendLine(CultureInfo.InvariantCulture, $"## {label}: pinned pages at 144 DPI");
        table.AppendLine();
        table.AppendLine("| Page | Software (ms) | WebGL (ms) | WebGL diagnostics |");
        table.AppendLine("|---|---|---|---|");
        foreach (string line in await File.ReadAllLinesAsync(pinned, TestContext.Current.CancellationToken))
        {
            if (line.StartsWith('#') || line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split('\t');
            string path = Path.Combine(corpus!, fields[0]);
            if (!File.Exists(path))
            {
                continue;
            }

            string served = "pinned-" + Path.GetFileName(fields[0]).Replace(' ', '-');
            host.Serve(served, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            string address = new Uri(host.BaseAddress, "files/" + Uri.EscapeDataString(served)).ToString();
            foreach (string index in fields[1].Split(','))
            {
                int pageIndex = int.Parse(index, CultureInfo.InvariantCulture);
                (double software, _) = await RenderPageAsync(page, address, pageIndex, webgl: false);
                (double webgl, string codes) = await RenderPageAsync(page, address, pageIndex, webgl: true);
                table.AppendLine(CultureInfo.InvariantCulture, $"| {Path.GetFileName(fields[0])}#{pageIndex} | {software:0} | {webgl:0} | {(codes.Length == 0 ? "-" : codes)} |");
            }
        }

        string output = Path.Combine(FindArtifacts(), $"gpu-bench-pinned-{label}.md");
        await File.WriteAllTextAsync(output, table.ToString(), TestContext.Current.CancellationToken);
        Viewer.Log(table.ToString());
    }

    private static async Task<(double Milliseconds, string Codes)> RenderPageAsync(IPage page, string address, int pageIndex, bool webgl)
    {
        var times = new List<double>();
        string codes = "";
        for (int i = 0; i < 4; i++)
        {
            string result = await page.EvaluateAsync<string>(
                """
                async ([address, page, dpi, webgl]) => {
                  const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
                  const r = await exports.Broadside.Viewer.Blazor.Sample.RenderStats.RenderPageAsync(address, page, dpi, webgl);
                  const parts = r.split(';');
                  return parts[0] + ';' + parts[1];
                }
                """,
                new object[] { address, pageIndex, 144, webgl });
            string[] parts = result.Split(';');
            if (i > 0)
            {
                times.Add(double.Parse(parts[0], CultureInfo.InvariantCulture));
            }

            codes = parts[1];
        }

        times.Sort();
        return (times[1], codes);
    }

    private async Task<Run> MeasureAsync(string document, string renderer, int width, int height, float scale, bool mobile, int throttle)
    {
        Viewer.Log($"measuring {document} with {renderer} at {width} px");
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
        string query = $"?file=../files/{Uri.EscapeDataString(document)}" + (renderer == "webgl" ? "&renderer=webgl" : "");
        await page.GotoAsync(new Uri(host.AppAddress, query).ToString());
        await page.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer canvas')", null, new PageWaitForFunctionOptions { Timeout = 180_000 });
        JsonElement first = default;
        for (int i = 0; i < 1200; i++)
        {
            first = JsonDocument.Parse(await page.EvaluateAsync<string>(Snapshot)).RootElement;
            if (first.GetProperty("firstSharp").GetDouble() >= 0)
            {
                break;
            }

            await page.WaitForTimeoutAsync(100);
        }

        await page.WaitForTimeoutAsync(1500);
        ILocator viewer = page.Locator(".bs-viewer");
        LocatorBoundingBoxResult box = (await viewer.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        (string scroll, JsonElement afterScroll) = await FramesAsync(page, async () =>
        {
            for (int i = 0; i < 40; i++)
            {
                await page.Mouse.WheelAsync(0, 160);
                await page.WaitForTimeoutAsync(16);
            }
        });
        (string zoom, JsonElement afterZoom) = await FramesAsync(page, async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                await page.Keyboard.DownAsync("Control");
                await page.Mouse.WheelAsync(0, -100);
                await page.Keyboard.UpAsync("Control");
                await page.WaitForTimeoutAsync(150);
            }
        });

        static double Mib(JsonElement e, string name) => e.GetProperty(name).GetDouble() / 1048576;
        string tiles = string.Create(
            CultureInfo.InvariantCulture,
            $"open {first.GetProperty("meanTile").GetDouble():0.0} / {first.GetProperty("p95Tile").GetDouble():0.0} / {first.GetProperty("maxTile").GetDouble():0}; scroll {afterScroll.GetProperty("meanTile").GetDouble():0.0} / {afterScroll.GetProperty("p95Tile").GetDouble():0.0} / {afterScroll.GetProperty("maxTile").GetDouble():0}");
        int fallbacks = first.GetProperty("fallbackTiles").GetInt32() + afterScroll.GetProperty("fallbackTiles").GetInt32() + afterZoom.GetProperty("fallbackTiles").GetInt32();
        string memory = string.Create(CultureInfo.InvariantCulture, $"{Mib(afterZoom, "wasmHeap"):0} / {Mib(afterZoom, "jsHeap"):0} / {Mib(afterZoom, "textureBytes"):0.0}");
        string json = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"document\":\"{document}\",\"renderer\":\"{renderer}\",\"width\":{width},\"scale\":{scale},\"throttle\":{throttle},\"first\":{first.GetRawText()},\"scroll\":\"{scroll}\",\"afterScroll\":{afterScroll.GetRawText()},\"zoom\":\"{zoom}\",\"afterZoom\":{afterZoom.GetRawText()}}}");
        return new Run(first.GetProperty("firstSharp").GetDouble(), scroll, zoom, tiles, fallbacks, memory, json);
    }

    private static async Task<(string Summary, JsonElement Stats)> FramesAsync(IPage page, Func<Task> gesture)
    {
        await page.EvaluateAsync(StartFrames);
        await gesture();
        await page.WaitForTimeoutAsync(1500);
        double[] frames = await page.EvaluateAsync<double[]>("() => { globalThis.__recording = false; return globalThis.__frames.slice(1); }");
        JsonElement stats = JsonDocument.Parse(await page.EvaluateAsync<string>(Snapshot)).RootElement;
        Array.Sort(frames);
        if (frames.Length == 0)
        {
            return ("-", stats);
        }

        double p95 = frames[Math.Max(0, (int)Math.Ceiling(frames.Length * 0.95) - 1)];
        return (string.Create(CultureInfo.InvariantCulture, $"{frames.Average():0.0} / {p95:0.0} / {frames[^1]:0}, {frames.Count(f => f > 50)}"), stats);
    }

    private static string FindArtifacts()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
        {
            directory = directory.Parent;
        }

        string artifacts = Path.Combine(directory?.FullName ?? AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }

    private sealed record Run(double FirstSharp, string Scroll, string Zoom, string Tiles, int Fallbacks, string Memory, string Json);
}
