using System.Diagnostics;
using System.Globalization;
using Broadside;
using Broadside.Benchmarks;
using Broadside.Rendering;

// Renders every page of every sample document (samples/documents/pdf) with the software rasterizer and prints the time of each
// page's render as a Markdown table row per document: pages, total, mean, median, 95th percentile and the slowest page.
//
//   Broadside.SampleTimings [--dpi 96] [--label jit] [document.pdf ...]
//
// Each document is opened once and every page rendered twice; the second pass is timed. The engine has no display-list cache, so
// the timed pass interprets each page again (fonts, images and colour spaces stay cached, as in a viewer that has shown the page).
double dpi = 96;
string label = System.Runtime.JitInfo.GetCompiledMethodCount() > 0 ? "JIT" : "Native AOT"; // natively compiled code compiles nothing at run time
var only = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dpi":
            dpi = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--label":
            label = args[++i];
            break;
        default:
            only.Add(args[i]);
            break;
    }
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"## {label}, {dpi:0} DPI, {Environment.ProcessorCount} logical processors, {RuntimeInformationText()}"));
Console.WriteLine();
Console.WriteLine("| Document | Pages | Total (ms) | Mean (ms) | Median (ms) | p95 (ms) | Slowest page (ms) |");
Console.WriteLine("|---|---:|---:|---:|---:|---:|---|");
using var rasterizer = new SoftwareRasterizer();
var options = new RenderOptions().WithDpi(dpi);
foreach (string file in SampleDocuments.Files().Where(f => only.Count == 0 || only.Contains(f)))
{
    using PdfDocument document = SampleDocuments.Engine.Open(File.ReadAllBytes(Path.Combine(SampleDocuments.Directory, file)));
    int count = document.Pages.Count;
    for (int page = 0; page < count; page++)
    {
        using Bitmap warm = rasterizer.Render(document.Pages[page], options);
    }

    double[] times = new double[count];
    for (int page = 0; page < count; page++)
    {
        long start = Stopwatch.GetTimestamp();
        using Bitmap bitmap = rasterizer.Render(document.Pages[page], options);
        times[page] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    int slowest = Array.IndexOf(times, times.Max());
    double[] sorted = [.. times.Order()];
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"| `{file}` | {count} | {times.Sum():0} | {times.Average():0.0} | {Percentile(sorted, 0.5):0.0} | {Percentile(sorted, 0.95):0.0} | {times[slowest]:0.0} (page {slowest + 1}) |"));
}

return 0;

static double Percentile(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];

static string RuntimeInformationText() =>
    $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
