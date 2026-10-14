using System.Collections.Concurrent;
using System.Globalization;
using Broadside.GoldenImages;
using Broadside.Rendering;

namespace Broadside.Tools.RenderDiff;

/// <summary>
/// Runs a comparison: renders every input page with Broadside's software rasterizer and each available reference engine at the same
/// resolution, aligns each reference image with Broadside's, compares them with the golden-image harness's perceptual metric
/// (<see cref="GoldenComparison"/> under <see cref="RenderDiffOptions.Threshold"/>), ranks the pages and writes the report.
/// </summary>
/// <remarks>Spec #33 Testing Decisions: a development-only diff tool compares against PDFium and pdf.js renders for triage. Never a CI gate.</remarks>
public static class RenderDiffRun
{
    /// <summary>Pages with more pixels than this are not compared (a 72 DPI render of a huge page would need gigabytes).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>Runs the comparison and writes the report to <see cref="RenderDiffOptions.OutputDirectory"/>.</summary>
    /// <param name="options">What to compare.</param>
    /// <param name="log">Receives progress and the skipped engines' messages.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The report.</returns>
    public static async Task<RenderDiffReport> RunAsync(RenderDiffOptions options, TextWriter log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var engines = new List<ReferenceEngine>();
        var skipped = new List<string>();
        foreach (ReferenceEngine engine in options.Engines)
        {
            string? reason = engine.CheckAvailable();
            if (reason is null)
            {
                engines.Add(engine);
            }
            else
            {
                string message = $"Skipping {engine.Name}: {reason}.";
                skipped.Add(message);
                await log.WriteLineAsync(message).ConfigureAwait(false);
            }
        }

        var results = new ConcurrentBag<PageDiff>();
        TextWriter progress = TextWriter.Synchronized(log);
        if (engines.Count > 0)
        {
            List<InputFile> files = InputFile.Expand(options.Inputs);
            int done = 0;
            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Jobs), CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    foreach (PageDiff page in await CompareFileAsync(file, engines, options, token).ConfigureAwait(false))
                    {
                        results.Add(page);
                    }

                    int count = Interlocked.Increment(ref done);
                    if (count % 25 == 0 || count == files.Count)
                    {
                        await progress.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{count}/{files.Count} files")).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
        }

        var report = new RenderDiffReport
        {
            Pages = [.. results.Where(static p => p.Score is not null).OrderByDescending(static p => p.Score).ThenBy(static p => p.File, StringComparer.Ordinal).ThenBy(static p => p.Page)],
            Failures = [.. results.Where(static p => p.Score is null).OrderBy(static p => p.File, StringComparer.Ordinal).ThenBy(static p => p.Page)],
            Engines = [.. engines.Select(static e => e.Name)],
            SkippedEngines = skipped,
            Dpi = options.Dpi,
            Threshold = options.Threshold.ToString(),
        };
        await ReportWriter.WriteAsync(report, options, cancellationToken).ConfigureAwait(false);
        await log.WriteLineAsync($"Report: {Path.Combine(options.OutputDirectory, ReportWriter.HtmlFileName)}").ConfigureAwait(false);
        return report;
    }

    private static async Task<List<PageDiff>> CompareFileAsync(InputFile file, List<ReferenceEngine> engines, RenderDiffOptions options, CancellationToken cancellationToken)
    {
        var pages = new List<PageDiff>();
        PdfDocument? document = null;
        try
        {
            try
            {
                document = PdfDocument.Open(file.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pages.Add(new PageDiff { File = file.Relative, SourcePath = file.Path, Page = -1, BroadsideError = Describe(ex) });
                return pages;
            }

            int pageCount;
            try
            {
                pageCount = Math.Min(document.Pages.Count, Math.Max(0, options.MaxPagesPerFile));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pages.Add(new PageDiff { File = file.Relative, SourcePath = file.Path, Page = -1, BroadsideError = Describe(ex) });
                return pages;
            }

            int[] pageIndexes = [.. Enumerable.Range(0, pageCount)];
            var references = new ReferenceResult[engines.Count];
            for (int e = 0; e < engines.Count; e++)
            {
                references[e] = await RenderReferenceAsync(engines[e], file, pageIndexes, options, cancellationToken).ConfigureAwait(false);
            }

            // Broadside gets the same budget per file as each reference. The rasterizer cannot be cancelled yet (#97), so a page that
            // overruns is abandoned on its thread, with its rasterizer, and the file's remaining pages are reported as not drawn.
            // Disposed below unless a render was abandoned on it: returning its pooled buffers under a running render would corrupt them.
#pragma warning disable CA2000
            var rasterizer = new SoftwareRasterizer();
#pragma warning restore CA2000
            // The whole page as a viewer shows it (#98): content, annotations and form fields; the references are asked for the same.
            var renderOptions = new RenderOptions { Dpi = options.Dpi, Layers = DisplayLayers.All, Intent = DisplayIntent.View };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool abandoned = false;
            foreach (int index in pageIndexes)
            {
                GoldenImage? broadside = null;
                string? broadsideError;
                TimeSpan left = options.Timeout - clock.Elapsed;
                if (abandoned || left <= TimeSpan.Zero)
                {
                    broadsideError = string.Create(CultureInfo.InvariantCulture, $"not drawn: the file took more than {options.Timeout.TotalSeconds:0} s");
                }
                else
                {
                    Task<(GoldenImage? Image, string? Error)> render = Task.Run(() => RenderBroadside(document, index, rasterizer, renderOptions), cancellationToken);
                    try
                    {
                        (broadside, broadsideError) = await render.WaitAsync(left, cancellationToken).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        abandoned = true;
                        broadsideError = string.Create(CultureInfo.InvariantCulture, $"timed out: the file took more than {options.Timeout.TotalSeconds:0} s");
                    }
                }

                var diffs = new List<EngineDiff>(engines.Count);
                var drawn = new List<GoldenImage>(2);
                for (int e = 0; e < engines.Count; e++)
                {
                    (EngineDiff diff, GoldenImage? reference) = CompareReference(engines[e].Name, references[e], index, broadside, options);
                    diffs.Add(diff);
                    if (reference is not null && drawn.Count < 2)
                    {
                        drawn.Add(reference);
                    }
                }

                double? spread = null;
                if (drawn.Count == 2)
                {
                    AlignedComparison between = ImageAlignment.Align(drawn[1], drawn[0], options.MaxShift, options.Threshold);
                    spread = Fraction(between);
                }

                pages.Add(new PageDiff
                {
                    File = file.Relative,
                    SourcePath = file.Path,
                    Page = index,
                    BroadsideError = broadsideError,
                    Width = broadside?.Width ?? 0,
                    Height = broadside?.Height ?? 0,
                    Engines = diffs,
                    ReferenceSpread = spread,
                });
            }

            if (!abandoned)
            {
                rasterizer.Dispose();
            }
        }
        finally
        {
            document?.Dispose();
        }

        return pages;
    }

    private static async Task<ReferenceResult> RenderReferenceAsync(ReferenceEngine engine, InputFile file, int[] pages, RenderDiffOptions options, CancellationToken cancellationToken)
    {
        string directory = ReferencePath(options.OutputDirectory, engine.Name, file.Relative);
        int[] missing = options.ReuseReferences && engine.Draws
            ? [.. pages.Where(page => !File.Exists(Path.Combine(directory, PageImage(page))))]
            : pages;
        if (engine.Draws && missing.Length == 0)
        {
            return new ReferenceResult(directory, null);
        }

        var request = new ReferenceRequest(Path.GetFullPath(file.Path), Stem(file.Relative), missing, options.Dpi, directory, options.Timeout);
        try
        {
            return await engine.RenderAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ReferenceResult(directory, Describe(ex));
        }
    }

    private static (GoldenImage? Image, string? Error) RenderBroadside(PdfDocument document, int index, SoftwareRasterizer rasterizer, RenderOptions options)
    {
        try
        {
            PdfPage page = document.Pages[index];
            PixelSize size = options.GetPixelSize(page);
            if ((long)size.Width * size.Height > MaxPixels)
            {
                return (null, string.Create(CultureInfo.InvariantCulture, $"the page is {size.Width} x {size.Height} pixels, more than RenderDiff compares"));
            }

            using Bitmap bitmap = rasterizer.Render(page, options);
            return (GoldenImage.FromBitmap(bitmap), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, Describe(ex));
        }
    }

    private static (EngineDiff Diff, GoldenImage? Reference) CompareReference(string engine, ReferenceResult result, int page, GoldenImage? broadside, RenderDiffOptions options)
    {
        string path = Path.Combine(result.Directory, PageImage(page));
        if (!File.Exists(path))
        {
            return (new EngineDiff { Engine = engine, Error = result.Error ?? "no image" }, null);
        }

        GoldenImage reference;
        try
        {
            reference = GoldenImage.LoadPng(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (new EngineDiff { Engine = engine, Error = "unreadable image: " + Describe(ex) }, null);
        }

        if (broadside is null)
        {
            return (new EngineDiff { Engine = engine, Error = "Broadside drew no image", ImagePath = path, ReferenceWidth = reference.Width, ReferenceHeight = reference.Height }, reference);
        }

        AlignedComparison aligned = ImageAlignment.Align(broadside, reference, options.MaxShift, options.Threshold);
        GoldenComparison comparison = aligned.Comparison;
        var diff = new EngineDiff
        {
            Engine = engine,
            ImagePath = path,
            ReferenceWidth = reference.Width,
            ReferenceHeight = reference.Height,
            OffsetX = aligned.OffsetX,
            OffsetY = aligned.OffsetY,
            DifferingPixels = comparison.DifferingPixels,
            TolerantPixels = comparison.TolerantPixels,
            MaxDifference = comparison.MaxDifference,
            DifferingFraction = Fraction(aligned),
        };
        return (diff, reference);
    }

    private static double Fraction(AlignedComparison aligned) =>
        (double)aligned.Comparison.DifferingPixels / ((long)aligned.Broadside.Width * aligned.Broadside.Height);

    /// <summary>Where an engine's renders of a file go in the output directory.</summary>
    internal static string ReferencePath(string outputDirectory, string engine, string relative) =>
        Path.Combine(outputDirectory, "references", engine, Stem(relative));

    internal static string PageImage(int page) => string.Create(CultureInfo.InvariantCulture, $"page{page}.png");

    internal static string Stem(string relative) =>
        relative.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? relative[..^4] : relative;

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>A PDF to compare and its path relative to the input that named it.</summary>
    private sealed record InputFile(string Path, string Relative)
    {
        public static List<InputFile> Expand(IEnumerable<string> inputs)
        {
            var files = new List<InputFile>();
            foreach (string input in inputs)
            {
                if (Directory.Exists(input))
                {
                    files.AddRange(Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories)
                        .Where(static f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        .Order(StringComparer.Ordinal)
                        .Select(f => new InputFile(f, System.IO.Path.GetRelativePath(input, f).Replace('\\', '/'))));
                }
                else if (File.Exists(input))
                {
                    files.Add(new InputFile(input, System.IO.Path.GetFileName(input)));
                }
                else
                {
                    throw new FileNotFoundException($"The input {input} does not exist.", input);
                }
            }

            return files;
        }
    }
}
