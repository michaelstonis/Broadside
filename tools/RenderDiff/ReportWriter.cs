using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broadside.GoldenImages;
using Broadside.Rendering;

namespace Broadside.Tools.RenderDiff;

/// <summary>
/// Writes a run's report into the output directory: <c>index.html</c> (every compared page worst first, the worst ones with Broadside's
/// image, each aligned reference and each diff image side by side), <c>report.json</c> (everything), and <c>triage.md</c> (the worst
/// pages as a Markdown table with an empty verdict column, to paste into a pull request).
/// </summary>
internal static class ReportWriter
{
    public const string HtmlFileName = "index.html";
    public const string JsonFileName = "report.json";
    public const string TriageFileName = "triage.md";

    public static async Task WriteAsync(RenderDiffReport report, RenderDiffOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var images = WriteImages(report, options);

        await using (FileStream json = File.Create(Path.Combine(options.OutputDirectory, JsonFileName)))
        {
            await JsonSerializer.SerializeAsync(json, report, ReportJsonContext.Default.RenderDiffReport, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, TriageFileName), Triage(report, options.TriagePages), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, HtmlFileName), Html(report, images), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-renders the worst pages and writes their side-by-side and diff images; returns each page's images by engine.</summary>
    private static Dictionary<PageDiff, PageImages> WriteImages(RenderDiffReport report, RenderDiffOptions options)
    {
        var written = new Dictionary<PageDiff, PageImages>();
        string pagesDirectory = Path.Combine(options.OutputDirectory, "pages");
        if (Directory.Exists(pagesDirectory))
        {
            Directory.Delete(pagesDirectory, recursive: true);
        }

        using var rasterizer = new SoftwareRasterizer();
        var renderOptions = new RenderOptions { Dpi = options.Dpi };
        foreach (IGrouping<string, PageDiff> file in report.Pages.Take(Math.Max(0, options.ReportedPages)).GroupBy(static p => p.SourcePath))
        {
            using PdfDocument document = PdfDocument.Open(file.Key);
            foreach (PageDiff page in file)
            {
                GoldenImage broadside;
                using (Bitmap bitmap = rasterizer.Render(document.Pages[page.Page], renderOptions))
                {
                    broadside = GoldenImage.FromBitmap(bitmap);
                }

                string stem = RenderDiffRun.Stem(page.File);
                string prefix = $"pages/{stem}/page{page.Page.ToString(CultureInfo.InvariantCulture)}";
                var images = new PageImages(prefix + ".broadside.png");
                Save(broadside, options.OutputDirectory, images.Broadside);
                foreach (EngineDiff diff in page.Engines.Where(static e => e.Compared && e.ImagePath is not null))
                {
                    AlignedComparison aligned = ImageAlignment.Align(broadside, GoldenImage.LoadPng(diff.ImagePath!), options.MaxShift, options.Threshold);
                    string reference = $"{prefix}.{diff.Engine}.png";
                    string diffImage = $"{prefix}.{diff.Engine}.diff.png";
                    Save(aligned.Reference, options.OutputDirectory, reference);
                    Save(aligned.Comparison.CreateDiffImage(), options.OutputDirectory, diffImage);
                    images.Engines[diff.Engine] = (reference, diffImage);
                }

                written[page] = images;
            }
        }

        return written;
    }

    private static void Save(GoldenImage image, string outputDirectory, string relative) =>
        image.SavePng(Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Triage(RenderDiffReport report, int count)
    {
        var text = new StringBuilder();
        text.AppendLine("# RenderDiff triage");
        text.AppendLine();
        text.AppendLine(Summary(report));
        text.AppendLine();
        text.AppendLine("Score: the share of pixels beyond the golden-image tolerance against the closest reference. Verdict: bug, accepted difference, or reference-engine bug.");
        text.AppendLine();
        text.Append("| Rank | File | Page | Score |");
        foreach (string engine in report.Engines)
        {
            text.Append(CultureInfo.InvariantCulture, $" {engine} |");
        }

        text.AppendLine(" References differ | Verdict | Notes |");
        text.Append("|---:|---|---:|---:|");
        text.Append(string.Concat(Enumerable.Repeat("---:|", report.Engines.Count)));
        text.AppendLine("---:|---|---|");
        int rank = 0;
        foreach (PageDiff page in report.Pages.Take(Math.Max(0, count)))
        {
            rank++;
            text.Append(CultureInfo.InvariantCulture, $"| {rank} | {Cell(page.File)} | {page.Page} | {Percent(page.Score)} |");
            foreach (string engine in report.Engines)
            {
                EngineDiff? diff = page.Engines.FirstOrDefault(e => e.Engine == engine);
                text.Append(CultureInfo.InvariantCulture, $" {(diff is null ? string.Empty : EngineCell(diff))} |");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $" {Percent(page.ReferenceSpread)} | | |");
        }

        if (report.Failures.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## Not compared ({report.Failures.Count})");
            text.AppendLine();
            foreach (PageDiff page in report.Failures)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {Cell(page.File)} {PageLabel(page)}: {Cell(FailureText(page))}");
            }
        }

        return text.ToString();
    }

    private static string Html(RenderDiffReport report, Dictionary<PageDiff, PageImages> images)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("<title>RenderDiff report</title>");
        html.AppendLine("""
            <style>
            :root { color-scheme: light dark; --line: #8884; --bad: #c62828; }
            body { font: 14px/1.4 system-ui, sans-serif; margin: 16px; }
            table { border-collapse: collapse; }
            th, td { border-bottom: 1px solid var(--line); padding: 4px 8px; text-align: right; vertical-align: top; }
            th:nth-child(2), td:nth-child(2) { text-align: left; }
            .images { display: flex; flex-wrap: wrap; gap: 8px; text-align: left; }
            .images figure { margin: 0; }
            .images img { max-width: 420px; border: 1px solid var(--line); background: #fff; image-rendering: pixelated; }
            .error { color: var(--bad); text-align: left; }
            </style>
            """);
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>RenderDiff report</h1>");
        html.AppendLine(CultureInfo.InvariantCulture, $"<p>{Encode(Summary(report))}</p>");
        html.AppendLine("<p>Score: the share of pixels beyond the golden-image tolerance against the closest reference; each engine's cell gives its share, differing pixels and alignment offset. Diff images: red beyond tolerance, amber within it, the reference faded elsewhere.</p>");
        foreach (string skipped in report.SkippedEngines)
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<p class=\"error\">{Encode(skipped)}</p>");
        }

        html.Append("<table><thead><tr><th>Rank</th><th>File</th><th>Page</th><th>Score</th>");
        foreach (string engine in report.Engines)
        {
            html.Append(CultureInfo.InvariantCulture, $"<th>{Encode(engine)}</th>");
        }

        html.AppendLine("<th>References differ</th></tr></thead><tbody>");
        int columns = 5 + report.Engines.Count;
        int rank = 0;
        foreach (PageDiff page in report.Pages)
        {
            rank++;
            html.Append(CultureInfo.InvariantCulture, $"<tr id=\"r{rank}\"><td>{rank}</td><td>{Encode(page.File)}</td><td>{page.Page}</td><td>{Percent(page.Score)}</td>");
            foreach (string engine in report.Engines)
            {
                EngineDiff? diff = page.Engines.FirstOrDefault(e => e.Engine == engine);
                html.Append(diff is null ? "<td></td>" : $"<td{(diff.Compared ? string.Empty : " class=\"error\"")}>{Encode(EngineCell(diff))}</td>");
            }

            html.AppendLine(CultureInfo.InvariantCulture, $"<td>{Percent(page.ReferenceSpread)}</td></tr>");
            if (images.TryGetValue(page, out PageImages? pageImages))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td colspan=\"{columns}\"><div class=\"images\">");
                html.Append(Figure(pageImages.Broadside, "Broadside"));
                foreach ((string engine, (string reference, string diff)) in pageImages.Engines)
                {
                    html.Append(Figure(reference, engine));
                    html.Append(Figure(diff, engine + " diff"));
                }

                html.AppendLine("</div></td></tr>");
            }
        }

        html.AppendLine("</tbody></table>");
        if (report.Failures.Count > 0)
        {
            html.AppendLine(CultureInfo.InvariantCulture, $"<h2>Not compared ({report.Failures.Count})</h2><ul>");
            foreach (PageDiff page in report.Failures)
            {
                html.AppendLine(CultureInfo.InvariantCulture, $"<li>{Encode(page.File)} {Encode(PageLabel(page))}: <span class=\"error\">{Encode(FailureText(page))}</span></li>");
            }

            html.AppendLine("</ul>");
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string Figure(string relative, string caption) =>
        $"<figure><a href=\"{Url(relative)}\"><img src=\"{Url(relative)}\" alt=\"{Encode(caption)}\" loading=\"lazy\"></a><figcaption>{Encode(caption)}</figcaption></figure>";

    private static string Summary(RenderDiffReport report)
    {
        string engines = report.Engines.Count == 0 ? "none" : string.Join(", ", report.Engines);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{report.Dpi:0.##} DPI; threshold: {report.Threshold}; engines: {engines}; {report.Pages.Count} pages compared, {report.Failures.Count} not compared.");
    }

    private static string EngineCell(EngineDiff diff) => diff.Compared
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{Percent(diff.DifferingFraction)} ({diff.DifferingPixels} px{(diff.OffsetX != 0 || diff.OffsetY != 0 ? $", moved {diff.OffsetX},{diff.OffsetY}" : string.Empty)})")
        : "failed: " + FirstLine(diff.Error!);

    private static string FailureText(PageDiff page) =>
        page.BroadsideError is not null
            ? "Broadside: " + FirstLine(page.BroadsideError)
            : string.Join("; ", page.Engines.Select(static e => $"{e.Engine}: {FirstLine(e.Error ?? "compared")}"));

    private static string PageLabel(PageDiff page) => page.Page < 0 ? "(file)" : string.Create(CultureInfo.InvariantCulture, $"page {page.Page}");

    private static string Percent(double? fraction) =>
        fraction is double value ? string.Create(CultureInfo.InvariantCulture, $"{value * 100:0.###} %") : string.Empty;

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        string line = end < 0 ? text : text[..end];
        return line.Length > 200 ? line[..200] + "..." : line;
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string Url(string relative) => string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));

    private sealed record PageImages(string Broadside)
    {
        public Dictionary<string, (string Reference, string Diff)> Engines { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>The report's JSON shape: camel-case names, indented.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RenderDiffReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;
