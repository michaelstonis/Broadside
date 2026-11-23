using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Broadside.Rendering;

namespace Broadside.Tools.PerfCompare;

/// <summary>
/// The Broadside side of the comparison, with the protocol of <c>tools/RenderDiff/reference/pdfium/bench.py</c> and
/// <c>../pdfjs/bench.mjs</c>: each case warms up untimed (until <see cref="WarmupMilliseconds"/> or <see cref="WarmupRuns"/> runs),
/// then runs up to the requested number of timed iterations (at least three, fewer once a case has spent
/// <see cref="BudgetMilliseconds"/>); the median and minimum are reported. Before the first case, every page runs twice untimed
/// (<see cref="WarmUp"/>). Everything runs on one thread.
/// </summary>
internal static class Harness
{
    public const double WarmupMilliseconds = 500;
    public const int WarmupRuns = 20;
    public const double BudgetMilliseconds = 10_000;
    public const int WarmupPasses = 2;
    public const int WarmupPauseMilliseconds = 1000;

    /// <summary>
    /// The engine every case opens with, the rendering gate's: Standard 14 and predefined CMaps bundled, no system fonts (so the same
    /// glyphs on every OS, as the references use their own bundled fonts), and no display-list cache, so a warm render interprets the
    /// page again as the references do.
    /// </summary>
    public static PdfEngine Engine { get; } = new(new PdfOptions()
        .UseStandard14Fonts()
        .UsePredefinedCMaps()
        .UseSystemFontResolver(null)
        .WithDisplayListCacheBudget(0));

    /// <summary>Times every case of every pinned page present under <paramref name="corpus"/> and writes one JSON line per case.</summary>
    public static void Bench(string pagesFile, string corpus, int[] dpis, int documentDpi, int iterations, TextWriter output)
    {
        using var rasterizer = new SoftwareRasterizer();
        WarmUp(pagesFile, corpus, dpis, rasterizer);
        var documented = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string file, int index) in PinnedPages.Read(pagesFile))
        {
            string full = Path.Combine(corpus, file);
            if (!File.Exists(full))
            {
                continue;
            }

            byte[] bytes = File.ReadAllBytes(full);
            Emit(output, file, index, "open", null, () =>
            {
                using PdfDocument document = Engine.Open(bytes);
                _ = document.Pages[index].MediaBox.Width;
            });

            using PdfDocument open = Engine.Open(bytes);
            Emit(output, file, index, "text", null, () => _ = open.Pages[index].ExtractText().PlainText.Length);
            foreach (int dpi in dpis)
            {
                var options = new RenderOptions { Dpi = dpi };
                Emit(output, file, index, "render", dpi, () =>
                {
                    using Bitmap bitmap = rasterizer.Render(open.Pages[index], options);
                }, measureAllocations: true);
                Emit(output, file, index, "open-render", dpi, () =>
                {
                    using PdfDocument document = Engine.Open(bytes);
                    using Bitmap bitmap = rasterizer.Render(document.Pages[index], options);
                });
            }

            if (!documented.Add(file))
            {
                continue;
            }

            var documentOptions = new RenderOptions { Dpi = documentDpi };
            Emit(output, file, index, "document", documentDpi, () =>
            {
                using PdfDocument document = Engine.Open(bytes);
                for (int page = 0; page < document.Pages.Count; page++)
                {
                    using Bitmap bitmap = rasterizer.Render(document.Pages[page], documentOptions);
                }
            });
        }
    }

    /// <summary>
    /// Runs every page's operations twice, untimed, before any case is timed, then waits for the runtime's background compilation:
    /// without it the first files are timed while their code is still being compiled and optimized (issue #602 found the first pinned
    /// page five times slower than its steady state). The reference harnesses do the same pass.
    /// </summary>
    private static void WarmUp(string pagesFile, string corpus, int[] dpis, SoftwareRasterizer rasterizer)
    {
        for (int pass = 0; pass < WarmupPasses; pass++)
        {
            foreach ((string file, int index) in PinnedPages.Read(pagesFile))
            {
                string full = Path.Combine(corpus, file);
                if (!File.Exists(full))
                {
                    continue;
                }

                try
                {
                    using PdfDocument document = Engine.Open(File.ReadAllBytes(full));
                    _ = document.Pages[index].ExtractText().PlainText.Length;
                    foreach (int dpi in dpis)
                    {
                        using Bitmap bitmap = rasterizer.Render(document.Pages[index], new RenderOptions { Dpi = dpi });
                    }
                }
#pragma warning disable CA1031 // The timed pass reports the failure.
                catch (Exception)
#pragma warning restore CA1031
                {
                }
            }
        }

        Thread.Sleep(WarmupPauseMilliseconds);
    }

    /// <summary>
    /// Opens the file, renders every page at <paramref name="dpi"/>, extracts the text of every page and closes it, once, then writes the
    /// process's peak resident set size. With no file, only loads the engine: the floor the others are compared with.
    /// </summary>
    public static void Memory(string? path, int dpi, TextWriter output)
    {
        if (path is not null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using var rasterizer = new SoftwareRasterizer();
            using PdfDocument document = Engine.Open(bytes);
            var options = new RenderOptions { Dpi = dpi };
            for (int page = 0; page < document.Pages.Count; page++)
            {
                using Bitmap bitmap = rasterizer.Render(document.Pages[page], options);
                _ = document.Pages[page].ExtractText().PlainText.Length;
            }
        }
        else
        {
            using PdfDocument document = Engine.Open(EmptyPdf);
            _ = document.Pages.Count;
        }

        output.WriteLine(JsonSerializer.Serialize(new { peak_rss_bytes = PeakResidentSetBytes() }));
    }

    private static void Emit(TextWriter output, string file, int page, string op, int? dpi, Action action, bool measureAllocations = false)
    {
        var line = new Dictionary<string, object?> { ["file"] = file, ["page"] = page, ["op"] = op, ["dpi"] = dpi };
        try
        {
            var watch = Stopwatch.StartNew();
            int warmups = 0;
            do
            {
                action();
                warmups++;
            }
            while (warmups < WarmupRuns && watch.Elapsed.TotalMilliseconds < WarmupMilliseconds);

            var samples = new List<double>();
            double spent = 0;
            while (samples.Count < Program.Iterations && (samples.Count < 3 || spent < BudgetMilliseconds))
            {
                long start = Stopwatch.GetTimestamp();
                action();
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                spent += elapsed;
                samples.Add(elapsed);
            }

            samples.Sort();
            int middle = samples.Count / 2;
            line["median_ms"] = samples.Count % 2 == 1 ? samples[middle] : (samples[middle - 1] + samples[middle]) / 2;
            line["min_ms"] = samples[0];
            line["runs"] = samples.Count;
            if (measureAllocations)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                action();
                line["alloc_bytes"] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }
#pragma warning disable CA1031 // A failing case is reported, not raised, as the references do.
        catch (Exception error)
#pragma warning restore CA1031
        {
            line["error"] = error.Message;
        }

        output.WriteLine(JsonSerializer.Serialize(line));
        output.Flush();
    }

    // The peak resident set size from getrusage(RUSAGE_SELF): ru_maxrss follows two timevals (16 bytes each on 64-bit Linux and
    // macOS) and is in kilobytes on Linux, bytes on macOS. Elsewhere, the runtime's peak working set.
    private static unsafe long PeakResidentSetBytes()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            long* usage = stackalloc long[18];
            if (GetResourceUsage(0, usage) == 0)
            {
                return OperatingSystem.IsLinux() ? usage[4] * 1024 : usage[4];
            }
        }

        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }

    [DllImport("libc", EntryPoint = "getrusage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern unsafe int GetResourceUsage(int who, long* usage);

    private static readonly byte[] EmptyPdf = CreateEmpty();

    private static byte[] CreateEmpty()
    {
        using PdfDocument document = PdfDocument.Create();
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
