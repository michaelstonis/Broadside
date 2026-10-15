using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.CorpusGate;

/// <summary>How rendering one real-world file ended.</summary>
internal enum RenderFileOutcome
{
    /// <summary>Every page rendered, every layer.</summary>
    Rendered,

    /// <summary>Encrypted with a user password that is not empty: a documented outcome.</summary>
    PasswordRequired,

    /// <summary>Encrypted for certificate recipients: a documented outcome.</summary>
    CertificateRequired,

    /// <summary>Encrypted by a handler or algorithm the engine does not implement: a documented outcome.</summary>
    EncryptionUnsupported,

    /// <summary>Lenient mode could not open the file (<see cref="DiagnosticException"/> from <c>Open</c>): the structure gate's concern.</summary>
    Rejected,

    /// <summary>Any other exception: a bug.</summary>
    Crash,

    /// <summary>A page used more processor time than <see cref="RenderCorpusGate.PageProcessorTimeLimit"/>, or stopped making progress: a bug.</summary>
    Timeout,
}

/// <summary>What rendering one file found.</summary>
internal sealed class RenderFileResult
{
    public required string File { get; init; }

    public required RenderFileOutcome Outcome { get; init; }

    /// <summary>Gets whether the file is well-formed by the #47 classification (open and walk record no Warning or Error).</summary>
    public required bool WellFormed { get; init; }

    public int Pages { get; init; }

    /// <summary>Gets how many Warning and Error diagnostics of each code the renders recorded (after the page tree was read).</summary>
    public IReadOnlyDictionary<string, int> Diagnostics { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the first message of each code in <see cref="Diagnostics"/>, for triage.</summary>
    public IReadOnlyDictionary<string, string> DiagnosticSamples { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the SHA-256 of each page's pixels (only when asked for).</summary>
    public IReadOnlyList<string>? PageHashes { get; init; }

    /// <summary>Gets the zero-based page that used the most processor time.</summary>
    public int SlowestPage { get; init; }

    /// <summary>Gets the processor time of <see cref="SlowestPage"/>.</summary>
    public TimeSpan SlowestPageTime { get; init; }

    public TimeSpan ProcessorTime { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string? Detail { get; init; }

    public bool IsFailure => Outcome is RenderFileOutcome.Crash or RenderFileOutcome.Timeout;
}

/// <summary>
/// The real-world rendering gate (issue #138): opens each fetched corpus file leniently, with the bundled Standard 14 fonts and
/// predefined CMaps and no operating-system fonts (so every machine draws the same pages), and renders every page with every layer
/// (content, annotations, form fields) through the software rasterizer at 72 DPI, scaled down to <see cref="MaxPixels"/> when larger.
/// Each page is charged the processor time of its thread (issue #280) against <see cref="PageProcessorTimeLimit"/>; a page that makes
/// no progress for <see cref="PageHangLimit"/> on the wall clock is cancelled through <see cref="RenderOptions.CancellationToken"/>.
/// ISO 32000-2 §8, §10, §11, §12.5.
/// </summary>
internal static class RenderCorpusGate
{
    /// <summary>The environment variable that sets the per-page processor-time limit, in seconds.</summary>
    public const string PageBudgetVariable = "BROADSIDE_RENDER_PAGE_BUDGET";

    /// <summary>The largest bitmap a page renders into; larger pages render at a smaller scale.</summary>
    public const long MaxPixels = 16L * 1024 * 1024;

    /// <summary>Files larger than this render one at a time.</summary>
    public const long LargeFileBytes = 50L * 1024 * 1024;

    private static readonly SemaphoreSlim LargeFiles = new(1);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Gets the engine every gate render opens with: lenient, Standard 14 fonts and predefined CMaps, no system fonts.</summary>
    public static PdfEngine Engine { get; } = new(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null));

    /// <summary>Gets the processor time one page may use: <see cref="PageBudgetVariable"/>, else 20 s.</summary>
    public static TimeSpan PageProcessorTimeLimit { get; } =
        int.TryParse(Environment.GetEnvironmentVariable(PageBudgetVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(20);

    /// <summary>Gets the wall-clock time after which a page that has not finished is cancelled: ten times the processor budget.</summary>
    public static TimeSpan PageHangLimit => PageProcessorTimeLimit * 10;

    /// <summary>Returns the options a page renders with: 72 DPI, or smaller so the bitmap holds at most <see cref="MaxPixels"/>.</summary>
    /// <param name="page">The page.</param>
    /// <param name="cancellation">The page's cancellation.</param>
    /// <returns>The options.</returns>
    public static RenderOptions OptionsFor(PdfPage page, CancellationToken cancellation)
    {
        var options = new RenderOptions { CancellationToken = cancellation };
        PixelSize size = options.GetPixelSize(page);
        double pixels = (double)size.Width * size.Height;
        if (pixels > MaxPixels)
        {
            options.Scale = Math.Sqrt(MaxPixels / pixels) * 0.999;
        }

        return options;
    }

    /// <summary>Returns the SHA-256 of a bitmap's visible pixels, row by row.</summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <returns>Lowercase hex.</returns>
    public static string Hash(Bitmap bitmap)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int y = 0; y < bitmap.Height; y++)
        {
            hash.AppendData(bitmap.GetRow(y)[..(bitmap.Width * 4)]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Renders every file, at most one per processor at a time, large files one at a time.</summary>
    /// <param name="files">Paths relative to the corpus root.</param>
    /// <param name="hashes">Whether to hash every page.</param>
    /// <returns>One result per file, in order.</returns>
    public static async Task<IReadOnlyList<RenderFileResult>> RunAsync(IReadOnlyList<string> files, bool hashes = false)
    {
        var results = new RenderFileResult[files.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = ContentCorpusGateParallelism() },
            async (index, _) => results[index] = await RunFileAsync(files[index], hashes).ConfigureAwait(false)).ConfigureAwait(false);
        return results;
    }

    /// <summary>Renders one file; see <see cref="RenderCorpusGate"/>.</summary>
    /// <param name="relativePath">A path relative to the corpus root.</param>
    /// <param name="hashes">Whether to hash every page.</param>
    /// <returns>The result.</returns>
    public static async Task<RenderFileResult> RunFileAsync(string relativePath, bool hashes = false)
    {
        CorpusOutcome classification = await RealWorldCorpus.OutcomeAsync(relativePath).ConfigureAwait(false);
        bool wellFormed = classification.Kind == CorpusOutcomeKind.Clean;
        string path = RealWorldCorpus.PathOf(relativePath);
        bool large = new FileInfo(path).Length > LargeFileBytes;
        if (large)
        {
            await LargeFiles.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var progress = new Progress();
            var stopwatch = Stopwatch.StartNew();
            Task<RenderFileResult> run = Task.Factory.StartNew(
                () => Render(relativePath, bytes, wellFormed, hashes, progress, stopwatch),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            // A page is cancelled after PageHangLimit; one that ignores cancellation for another minute is a hard hang.
            while (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != run)
            {
                if (progress.SinceLastPage > PageHangLimit + TimeSpan.FromMinutes(1))
                {
                    return new RenderFileResult
                    {
                        File = relativePath,
                        Outcome = RenderFileOutcome.Timeout,
                        WellFormed = wellFormed,
                        Pages = progress.Pages,
                        Detail = $"Hard timeout: page {progress.Pages + 1} ignored cancellation.",
                        Elapsed = stopwatch.Elapsed,
                    };
                }
            }

            return await run.ConfigureAwait(false);
        }
        finally
        {
            if (large)
            {
                LargeFiles.Release();
            }
        }
    }

    /// <summary>Writes per-file detail to <c>artifacts/render-gate/&lt;name&gt;.json</c>.</summary>
    /// <param name="name">The corpus id.</param>
    /// <param name="results">The results.</param>
    /// <returns>The path written.</returns>
    public static string WriteDetail(string name, IEnumerable<RenderFileResult> results)
    {
        string directory = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "artifacts", "render-gate"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        var rows = results.Select(result => new
        {
            result.File,
            Outcome = result.Outcome.ToString(),
            result.WellFormed,
            result.Pages,
            result.Diagnostics,
            result.DiagnosticSamples,
            result.SlowestPage,
            SlowestPageMilliseconds = Math.Round(result.SlowestPageTime.TotalMilliseconds, 1),
            ProcessorMilliseconds = Math.Round(result.ProcessorTime.TotalMilliseconds, 1),
            ElapsedMilliseconds = Math.Round(result.Elapsed.TotalMilliseconds, 1),
            result.Detail,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions));
        return path;
    }

    private static int ContentCorpusGateParallelism() =>
        int.TryParse(Environment.GetEnvironmentVariable(ContentCorpusGate.ParallelismVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int configured) && configured > 0
            ? configured
            : Environment.ProcessorCount;

    private static RenderFileResult Render(string relativePath, byte[] bytes, bool wellFormed, bool hashes, Progress progress, Stopwatch stopwatch)
    {
        int pages = 0;
        int slowest = 0;
        TimeSpan slowestTime = TimeSpan.Zero;
        TimeSpan total = TimeSpan.Zero;
        var pageHashes = hashes ? new List<string>() : null;
        RenderFileResult Result(RenderFileOutcome outcome, string? detail, IReadOnlyList<Diagnostic>? found = null) => new()
        {
            File = relativePath,
            Outcome = outcome,
            WellFormed = wellFormed,
            Pages = pages,
            Diagnostics = (found ?? []).GroupBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            DiagnosticSamples = (found ?? []).GroupBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => $"{group.First().ObjectReference}: {group.First().Message}", StringComparer.Ordinal),
            PageHashes = pageHashes,
            SlowestPage = slowest,
            SlowestPageTime = slowestTime,
            ProcessorTime = total,
            Elapsed = stopwatch.Elapsed,
            Detail = detail,
        };

        PdfDocument document;
        try
        {
            document = Engine.Open(bytes);
        }
        catch (DiagnosticException exception)
        {
            return Result(RenderFileOutcome.Rejected, exception.Message);
        }
        catch (PdfPasswordException exception)
        {
            return Result(RenderFileOutcome.PasswordRequired, exception.Message);
        }
        catch (PdfCertificateException exception)
        {
            return Result(RenderFileOutcome.CertificateRequired, exception.Message);
        }
        catch (PdfEncryptionNotSupportedException exception)
        {
            return Result(RenderFileOutcome.EncryptionUnsupported, exception.Message);
        }
#pragma warning disable CA1031 // Any other exception is the finding this gate exists to report.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Result(RenderFileOutcome.Crash, $"open: {exception}");
        }

        using (document)
        using (var rasterizer = new SoftwareRasterizer())
        using (var budget = new CancellationTokenSource())
        {
            try
            {
                int pageCount = document.Pages.Count;
                int baseline = document.Diagnostics.Count;
                for (int index = 0; index < pageCount; index++)
                {
                    budget.CancelAfter(PageHangLimit);
                    TimeSpan start = ThreadProcessorTime.Current ?? stopwatch.Elapsed;
                    PdfPage page = document.Pages[index];
                    using (Bitmap bitmap = rasterizer.Render(page, OptionsFor(page, budget.Token)))
                    {
                        pageHashes?.Add(Hash(bitmap));
                    }

                    TimeSpan used = (ThreadProcessorTime.Current ?? stopwatch.Elapsed) - start;
                    total += used;
                    if (used > slowestTime)
                    {
                        (slowest, slowestTime) = (index, used);
                    }

                    pages++;
                    progress.PageDone();
                    if (used > PageProcessorTimeLimit)
                    {
                        return Result(RenderFileOutcome.Timeout, $"page {index + 1} used {used.TotalSeconds:F1} s of processor time (limit {PageProcessorTimeLimit.TotalSeconds} s).");
                    }
                }

                Diagnostic[] diagnostics = [.. document.Diagnostics.Skip(baseline).Where(static diagnostic => diagnostic.Severity > DiagnosticSeverity.Information)];
                return Result(RenderFileOutcome.Rendered, null, diagnostics);
            }
            catch (PdfPasswordException exception)
            {
                return Result(RenderFileOutcome.PasswordRequired, exception.Message);
            }
            catch (OperationCanceledException)
            {
                return Result(RenderFileOutcome.Timeout, $"page {pages + 1} cancelled after {PageHangLimit.TotalSeconds} s on the wall clock.");
            }
#pragma warning disable CA1031 // Any other exception is the finding this gate exists to report.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return Result(RenderFileOutcome.Crash, $"page {pages + 1}: {exception}");
            }
        }
    }

    /// <summary>When the render thread last finished a page.</summary>
    private sealed class Progress
    {
        private readonly Stopwatch _sinceLastPage = Stopwatch.StartNew();
        private int _pages;

        public int Pages => Volatile.Read(ref _pages);

        public TimeSpan SinceLastPage
        {
            get
            {
                lock (_sinceLastPage)
                {
                    return _sinceLastPage.Elapsed;
                }
            }
        }

        public void PageDone()
        {
            Interlocked.Increment(ref _pages);
            lock (_sinceLastPage)
            {
                _sinceLastPage.Restart();
            }
        }
    }
}
