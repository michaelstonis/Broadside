using System.Globalization;
using System.Text.Json.Nodes;
using Broadside.TestSupport;
using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>
/// The browser half of the v1 performance comparison (issue #275): the rendering gate's pinned real-world pages timed by Broadside's
/// WebAssembly build (the sample's <c>PerfProbe</c>, AOT build, main thread, software rasterizer) and by pdf.js (its Web Worker,
/// shared across documents, a canvas read back after each render, and the print intent, which does not wait for animation frames) in
/// the same Chromium, with the protocol of <c>tools/PerfCompare</c>. The page is cross-origin isolated, so the browser's clock is
/// precise to microseconds rather than a tenth of a millisecond. Runs only from <c>bench/perf-report.sh</c>, which sets
/// BROADSIDE_PERF_BROWSER_OUTPUT (the JSON lines file it writes), BROADSIDE_PDFJS_DIST (an installed pdfjs-dist) and
/// BROADSIDE_CORPUS_DIR; anywhere else it is skipped.
/// </summary>
public sealed class PerfComparisonTests(BrowserHost host)
{
    private const string BroadsideHarness = """
        async ([address, page, dpis, documentDpi, iterations]) => {
          const exports = await getDotnetRuntime(0).getAssemblyExports('Broadside.Viewer.Blazor.Sample.dll');
          return exports.Broadside.Viewer.Blazor.Sample.PerfProbe.MeasureAsync(address, page, dpis, documentDpi, iterations);
        }
        """;

    private const string PdfJsHarness = """
        async ([base, address, page, dpis, documentDpi, iterations]) => {
          const pdfjs = await import(base + "pdfjs/build/pdf.mjs");
          pdfjs.GlobalWorkerOptions.workerSrc = base + "pdfjs/build/pdf.worker.mjs";
          globalThis.broadsidePdfJsWorker ??= new pdfjs.PDFWorker();
          const worker = globalThis.broadsidePdfJsWorker;
          const bytes = new Uint8Array(await (await fetch(address)).arrayBuffer());
          const load = (data) => pdfjs.getDocument({
            data, worker, verbosity: 0, cMapPacked: true,
            cMapUrl: base + "pdfjs/cmaps/", standardFontDataUrl: base + "pdfjs/standard_fonts/",
            wasmUrl: base + "pdfjs/wasm/", iccUrl: base + "pdfjs/iccs/",
          });
          const render = async (document, index, dpi) => {
            const p = await document.getPage(index + 1);
            const viewport = p.getViewport({ scale: dpi / 72 });
            const canvas = globalThis.document.createElement("canvas");
            canvas.width = Math.max(1, Math.ceil(viewport.width - 1e-6));
            canvas.height = Math.max(1, Math.ceil(viewport.height - 1e-6));
            // The print intent: with the display intent pdf.js paces its rendering by animation frames, which puts a floor of
            // about one frame under every render (8 ms here even for an empty page). The two intents differ only for annotations
            // and optional content that are hidden when printing.
            const context = canvas.getContext("2d");
            await p.render({ canvas, canvasContext: context, viewport, intent: "print", background: "white", annotationMode: pdfjs.AnnotationMode.ENABLE }).promise;
            // The canvas draws on the GPU after the commands are issued; reading one pixel back waits until it has, so the time
            // covers the pixels, as Broadside's does.
            context.getImageData(0, 0, 1, 1);
            p.cleanup();
          };
          const lines = [];
          const emit = async (op, dpi, action) => {
            const line = { engine: "pdfjs", page, op, dpi };
            try {
              const warmupStart = performance.now();
              for (let runs = 0; ; ) {
                await action();
                runs++;
                if (runs >= 20 || performance.now() - warmupStart >= 500) break;
              }
              const samples = [];
              let spent = 0;
              while (samples.length < iterations && (samples.length < 3 || spent < 10000)) {
                const start = performance.now();
                await action();
                const elapsed = performance.now() - start;
                spent += elapsed;
                samples.push(elapsed);
              }
              samples.sort((a, b) => a - b);
              const middle = samples.length >> 1;
              line.median_ms = samples.length % 2 ? samples[middle] : (samples[middle - 1] + samples[middle]) / 2;
              line.min_ms = samples[0];
              line.runs = samples.length;
            } catch (error) {
              line.error = String(error?.message ?? error);
            }
            lines.push(JSON.stringify(line));
          };
          await emit("open", null, async () => {
            const task = load(bytes.slice());
            const document = await task.promise;
            void (await document.getPage(page + 1)).view;
            await task.destroy();
          });
          const task = load(bytes.slice());
          const open = await task.promise;
          await emit("text", null, async () => {
            const p = await open.getPage(page + 1);
            void (await p.getTextContent()).items.length;
            p.cleanup();
          });
          for (const dpi of dpis.split(",").map(Number)) {
            await emit("render", dpi, () => render(open, page, dpi));
            await emit("open-render", dpi, async () => {
              const own = load(bytes.slice());
              await render(await own.promise, page, dpi);
              await own.destroy();
            });
          }
          if (documentDpi > 0) {
            await emit("document", documentDpi, async () => {
              const own = load(bytes.slice());
              const whole = await own.promise;
              for (let index = 0; index < whole.numPages; index++) await render(whole, index, documentDpi);
              await own.destroy();
            });
          }
          await task.destroy();
          return lines.join("\n");
        }
        """;

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Broadside_and_pdfjs_time_the_pinned_pages_in_the_same_browser()
    {
        string? output = Environment.GetEnvironmentVariable("BROADSIDE_PERF_BROWSER_OUTPUT");
        string? corpus = Environment.GetEnvironmentVariable("BROADSIDE_CORPUS_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(output), "BROADSIDE_PERF_BROWSER_OUTPUT is not set; bench/perf-report.sh sets it.");
        Assert.SkipWhen(string.IsNullOrEmpty(corpus) || !Directory.Exists(corpus), "BROADSIDE_CORPUS_DIR does not name the real-world corpus.");
        Assert.SkipWhen(BrowserHost.PdfJsDirectory is null, "BROADSIDE_PDFJS_DIST does not name an installed pdfjs-dist.");
        BrowserHost.RequireApp();

        string dpis = Environment.GetEnvironmentVariable("BROADSIDE_PERF_DPIS") ?? "72,150";
        int documentDpi = int.Parse(Environment.GetEnvironmentVariable("BROADSIDE_PERF_DOCUMENT_DPI") ?? "150", CultureInfo.InvariantCulture);
        int iterations = int.Parse(Environment.GetEnvironmentVariable("BROADSIDE_PERF_ITERATIONS") ?? "10", CultureInfo.InvariantCulture);
        string pinned = Path.Combine(Corpus.Directory, "..", "Broadside.Rendering.Tests", "CorpusGate", "pinned-pages.txt");

        await using IBrowserContext broadsideContext = await host.Browser.NewContextAsync();
        IPage broadside = await broadsideContext.NewPageAsync();
        await broadside.GotoAsync(new Uri(host.AppAddress, "?worker=0").ToString());
        await broadside.WaitForFunctionAsync("() => globalThis.getDotnetRuntime && document.querySelector('.bs-viewer')", null, new PageWaitForFunctionOptions { Timeout = 120_000 });

        await using IBrowserContext pdfjsContext = await host.Browser.NewContextAsync();
        IPage pdfjs = await pdfjsContext.NewPageAsync();
        await pdfjs.GotoAsync(new Uri(host.BaseAddress, "blank").ToString());

        Viewer.Log($"chromium {host.Browser.Version}");
        var lines = new List<string>();
        var documented = new HashSet<string>(StringComparer.Ordinal);
        int served = 0;
        var pages = new List<(string File, int Page, string Address)>();
        foreach ((string file, int page) in PinnedPages(pinned))
        {
            string full = Path.Combine(corpus!, file);
            if (!File.Exists(full))
            {
                continue;
            }

            string name = string.Create(CultureInfo.InvariantCulture, $"perf-{served++}.pdf");
            host.Serve(name, await File.ReadAllBytesAsync(full, TestContext.Current.CancellationToken));
            pages.Add((file, page, new Uri(host.BaseAddress, "files/" + name).ToString()));
        }

        // Every page once, untimed and discarded, before anything is timed, as the native harnesses do (issue #602): pdf.js's
        // JavaScript is compiled at run time, and the first pages would otherwise be timed before it is optimized.
        foreach ((_, int page, string address) in pages)
        {
            await broadside.EvaluateAsync<string>(BroadsideHarness, new object[] { address, page, dpis, 0, 3 });
            await pdfjs.EvaluateAsync<string>(PdfJsHarness, new object[] { host.BaseAddress.ToString(), address, page, dpis, 0, 3 });
        }

        foreach ((string file, int page, string address) in pages)
        {
            int whole = documented.Add(file) ? documentDpi : 0;

            string ours = await broadside.EvaluateAsync<string>(BroadsideHarness, new object[] { address, page, dpis, whole, iterations });
            string theirs = await pdfjs.EvaluateAsync<string>(PdfJsHarness, new object[] { host.BaseAddress.ToString(), address, page, dpis, whole, iterations });

            foreach (string line in (ours + "\n" + theirs).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                JsonObject json = JsonNode.Parse(line)!.AsObject();
                json["file"] = file;
                lines.Add(json.ToJsonString());
                Viewer.Log($"perf {json["engine"]} {file} {json["op"]} {json["dpi"]}: {json["median_ms"] ?? json["error"]}");
            }
        }

        Assert.NotEmpty(lines);
        await File.WriteAllLinesAsync(output!, lines, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<(string File, int Page)> PinnedPages(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            foreach (string page in fields[1].Split(','))
            {
                yield return (fields[0], int.Parse(page, NumberStyles.None, CultureInfo.InvariantCulture));
            }
        }
    }
}
