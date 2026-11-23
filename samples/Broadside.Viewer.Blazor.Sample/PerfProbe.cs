using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Broadside.Rendering;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// The Broadside side of the browser comparison with pdf.js (issue #275, <c>tests/Broadside.Viewer.Blazor.BrowserTests/PerfComparisonTests.cs</c>):
/// the operations and protocol of <c>tools/PerfCompare</c>, run in the browser on the main thread with the software rasterizer.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class PerfProbe
{
    private const double WarmupMilliseconds = 500;
    private const int WarmupRuns = 20;
    private const double BudgetMilliseconds = 10_000;

    /// <summary>
    /// Times open, text, render and first render (at each of <paramref name="dpis"/>) of one page and, when
    /// <paramref name="documentDpi"/> is above zero, the whole document; returns one JSON object per line.
    /// </summary>
    /// <param name="address">The document.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="dpis">The resolutions, comma-separated.</param>
    /// <param name="documentDpi">The resolution of the whole-document case, or 0 to skip it.</param>
    /// <param name="iterations">The timed runs per case.</param>
    /// <returns>The cases as JSON lines.</returns>
    [JSExport]
    public static async Task<string> MeasureAsync(string address, int page, string dpis, int documentDpi, int iterations)
    {
        using var http = new HttpClient();
        byte[] bytes = await http.GetByteArrayAsync(new Uri(address)).ConfigureAwait(true);
        var engine = new PdfEngine(new PdfOptions().UseStandard14Fonts().WithDisplayListCacheBudget(0));
        using var rasterizer = new SoftwareRasterizer();
        var lines = new StringBuilder();

        await EmitAsync(lines, page, "open", null, iterations, () =>
        {
            using PdfDocument document = engine.Open(bytes);
            _ = document.Pages[page].MediaBox.Width;
        }).ConfigureAwait(true);

        using (PdfDocument open = await engine.OpenAsync(bytes).ConfigureAwait(true))
        {
            await EmitAsync(lines, page, "text", null, iterations, () => _ = open.Pages[page].ExtractText().PlainText.Length).ConfigureAwait(true);
            foreach (string text in dpis.Split(','))
            {
                int dpi = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                var options = new RenderOptions { Dpi = dpi };
                await EmitAsync(lines, page, "render", dpi, iterations, () =>
                {
                    using Bitmap bitmap = rasterizer.Render(open.Pages[page], options);
                }).ConfigureAwait(true);
                await EmitAsync(lines, page, "open-render", dpi, iterations, () =>
                {
                    using PdfDocument document = engine.Open(bytes);
                    using Bitmap bitmap = rasterizer.Render(document.Pages[page], options);
                }).ConfigureAwait(true);
            }
        }

        if (documentDpi > 0)
        {
            var options = new RenderOptions { Dpi = documentDpi };
            await EmitAsync(lines, page, "document", documentDpi, iterations, () =>
            {
                using PdfDocument document = engine.Open(bytes);
                for (int index = 0; index < document.Pages.Count; index++)
                {
                    using Bitmap bitmap = rasterizer.Render(document.Pages[index], options);
                }
            }).ConfigureAwait(true);
        }

        return lines.ToString();
    }

    private static async Task EmitAsync(StringBuilder lines, int page, string op, int? dpi, int iterations, Action action)
    {
        double median = 0, minimum = 0;
        int runs = 0;
        string? error = null;
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
            while (samples.Count < iterations && (samples.Count < 3 || spent < BudgetMilliseconds))
            {
                long start = Stopwatch.GetTimestamp();
                action();
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                spent += elapsed;
                samples.Add(elapsed);
                await Task.Yield(); // keep the page responsive between runs; not timed
            }

            samples.Sort();
            int middle = samples.Count / 2;
            median = samples.Count % 2 == 1 ? samples[middle] : (samples[middle - 1] + samples[middle]) / 2;
            minimum = samples[0];
            runs = samples.Count;
        }
#pragma warning disable CA1031 // A failing case is reported, not raised.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            error = exception.Message;
        }

        // Written by hand rather than serialized: the trimmed app has no reflection-based JSON.
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            writer.WriteStartObject();
            writer.WriteString("engine", "broadside");
            writer.WriteNumber("page", page);
            writer.WriteString("op", op);
            if (dpi is { } value)
            {
                writer.WriteNumber("dpi", value);
            }
            else
            {
                writer.WriteNull("dpi");
            }

            if (error is null)
            {
                writer.WriteNumber("median_ms", median);
                writer.WriteNumber("min_ms", minimum);
                writer.WriteNumber("runs", runs);
            }
            else
            {
                writer.WriteString("error", error);
            }

            writer.WriteEndObject();
        }

        lines.AppendLine(Encoding.UTF8.GetString(json.ToArray()));
    }
}
