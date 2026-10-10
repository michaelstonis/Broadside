using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Security;

namespace Broadside.TestSupport;

/// <summary>How interpreting one real-world file ended.</summary>
public enum ContentFileOutcome
{
    /// <summary>Every page and every annotation appearance was interpreted.</summary>
    Interpreted,

    /// <summary>Encrypted with a user password that is not empty: a documented outcome.</summary>
    PasswordRequired,

    /// <summary>Encrypted for certificate recipients: a documented outcome.</summary>
    CertificateRequired,

    /// <summary>Encrypted by a handler or algorithm the engine does not implement: a documented outcome.</summary>
    EncryptionUnsupported,

    /// <summary>Lenient mode could not open the file at all (<see cref="DiagnosticException"/> from <c>Open</c>).</summary>
    Rejected,

    /// <summary>Any other exception: a bug.</summary>
    Crash,

    /// <summary>The file did not finish within the time limit: a bug unless allowlisted.</summary>
    Timeout,
}

/// <summary>What interpreting one file found.</summary>
public sealed class ContentFileResult
{
    /// <summary>Gets the path relative to the corpus root.</summary>
    public required string File { get; init; }

    /// <summary>Gets how it ended.</summary>
    public required ContentFileOutcome Outcome { get; init; }

    /// <summary>Gets whether the file is well-formed by the #47 classification (open and walk record no Warning or Error).</summary>
    public required bool WellFormed { get; init; }

    /// <summary>Gets the pages interpreted.</summary>
    public int Pages { get; init; }

    /// <summary>Gets the annotation appearance streams interpreted.</summary>
    public int Appearances { get; init; }

    /// <summary>Gets the counts of the page runs.</summary>
    public ContentCounts PageCounts { get; init; } = new();

    /// <summary>Gets the counts of the appearance runs.</summary>
    public ContentCounts AppearanceCounts { get; init; } = new();

    /// <summary>Gets how many Warning and Error diagnostics of each code interpretation recorded (after the page tree was read).</summary>
    public IReadOnlyDictionary<string, int> Diagnostics { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the first message of each diagnostic code in <see cref="Diagnostics"/>, with its object reference, for triage.</summary>
    public IReadOnlyDictionary<string, string> DiagnosticSamples { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets whether interpreting every page concurrently on one document gave the same counts as the sequential pass (null when not checked).</summary>
    public bool? ConcurrentCountsMatch { get; init; }

    /// <summary>Gets the exception or the reason, for anything that did not interpret.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the managed bytes the sequential pass allocated on its thread.</summary>
    public long AllocatedBytes { get; init; }

    /// <summary>Gets how long the file took.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Gets whether the outcome is a bug: an exception, a hang, or concurrent counts that differ.</summary>
    public bool IsFailure => Outcome is ContentFileOutcome.Crash or ContentFileOutcome.Timeout || ConcurrentCountsMatch == false;
}

/// <summary>
/// The content interpreter corpus gate (issue #80): opens each real-world file leniently and interprets every page with a
/// <see cref="CountingContentProcessor"/>, then every annotation appearance stream (N, R and D, each state), with a per-file time limit;
/// well-formed multi-page files are interpreted a second time with all pages in parallel on one document, and the counts must match.
/// ISO 32000-2 §7.8, §8, §9, §12.5.5.
/// </summary>
public static class ContentCorpusGate
{
    /// <summary>The environment variable that sets the per-file time limit, in seconds.</summary>
    public const string TimeoutVariable = "BROADSIDE_CORPUS_TIMEOUT";

    /// <summary>The environment variable that caps how many files run at once.</summary>
    public const string ParallelismVariable = "BROADSIDE_CORPUS_PARALLELISM";

    /// <summary>Files larger than this run one at a time, so their images and meshes cannot add up.</summary>
    public const long LargeFileBytes = 50L * 1024 * 1024;

    /// <summary>Multi-page well-formed files up to this many pages get the concurrent pass.</summary>
    public const int ConcurrentPassMaxPages = 200;

    private static readonly SemaphoreSlim LargeFiles = new(1);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Gets the per-file time limit: <see cref="TimeoutVariable"/>, else 60 s.</summary>
    public static TimeSpan TimeLimit { get; } =
        int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(60);

    /// <summary>The fetched ref of corpus <paramref name="id"/> from <c>corpus/manifest.lock.json</c>, or "unknown".</summary>
    /// <param name="id">A corpus id.</param>
    /// <returns>The ref.</returns>
    public static string CorpusRef(string id)
    {
        string? root = CorpusLocator.RealWorldCorpusDirectory;
        string lockFile = root is null ? string.Empty : Path.Combine(root, "manifest.lock.json");
        if (!System.IO.File.Exists(lockFile))
        {
            return "unknown";
        }

        using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllBytes(lockFile));
        return json.RootElement.TryGetProperty("corpora", out JsonElement corpora)
            && corpora.TryGetProperty(id, out JsonElement corpus)
            && corpus.TryGetProperty("ref", out JsonElement reference)
            ? reference.GetString() ?? "unknown"
            : "unknown";
    }

    /// <summary>
    /// Interprets every file of <paramref name="files"/> (paths relative to the corpus root), at most one per processor at a time,
    /// files over <see cref="LargeFileBytes"/> one at a time. After a hard timeout (a file that ignored cancellation) no new file
    /// starts; the remaining ones are reported as timeouts naming the stuck file.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>One result per file, in the order given.</returns>
    public static async Task<IReadOnlyList<ContentFileResult>> RunAsync(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var results = new ContentFileResult[files.Count];
        string? stuck = null;
        int parallelism = int.TryParse(Environment.GetEnvironmentVariable(ParallelismVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out int configured) && configured > 0
            ? configured
            : Environment.ProcessorCount;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (index, _) =>
            {
                string file = files[index];
                if (Volatile.Read(ref stuck) is { } hung)
                {
                    results[index] = new ContentFileResult
                    {
                        File = file,
                        Outcome = ContentFileOutcome.Timeout,
                        WellFormed = false,
                        Detail = $"Not run: {hung} ignored cancellation and is still running.",
                    };
                    return;
                }

                results[index] = await RunFileAsync(file).ConfigureAwait(false);
                if (results[index].Outcome == ContentFileOutcome.Timeout && results[index].Detail?.StartsWith("Hard", StringComparison.Ordinal) == true)
                {
                    Volatile.Write(ref stuck, file);
                }
            }).ConfigureAwait(false);
        return results;
    }

    /// <summary>Interprets one file; see <see cref="ContentCorpusGate"/>.</summary>
    /// <param name="relativePath">A path relative to the corpus root.</param>
    /// <returns>The result.</returns>
    public static async Task<ContentFileResult> RunFileAsync(string relativePath)
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
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
            using var budget = new CancellationTokenSource(TimeLimit);
            var stopwatch = Stopwatch.StartNew();
            Task<ContentFileResult> run = Task.Factory.StartNew(
                () => Interpret(relativePath, bytes, wellFormed, stopwatch, budget.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            try
            {
                return await run.WaitAsync(TimeLimit + TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return new ContentFileResult
                {
                    File = relativePath,
                    Outcome = ContentFileOutcome.Timeout,
                    WellFormed = wellFormed,
                    Detail = $"Hard timeout: still running {TimeLimit.TotalSeconds + 10} s after it started, ignoring cancellation.",
                    Elapsed = stopwatch.Elapsed,
                };
            }
        }
        finally
        {
            if (large)
            {
                LargeFiles.Release();
            }
        }
    }

    /// <summary>Writes per-file detail (counts, diagnostics, time, allocation) to <c>artifacts/corpus-gate/&lt;name&gt;.json</c>.</summary>
    /// <param name="name">The file name without extension, such as the corpus id.</param>
    /// <param name="results">The results.</param>
    /// <returns>The path written.</returns>
    public static string WriteDetail(string name, IEnumerable<ContentFileResult> results)
    {
        string directory = Path.Combine(CorpusLocator.RepositoryRoot, "artifacts", "corpus-gate");
        System.IO.Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".json");
        var rows = results.Select(result => new
        {
            result.File,
            Outcome = result.Outcome.ToString(),
            result.WellFormed,
            result.Pages,
            result.Appearances,
            PageCounts = result.PageCounts.Values().Where(pair => pair.Value != 0).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            AppearanceCounts = result.AppearanceCounts.Values().Where(pair => pair.Value != 0).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            result.Diagnostics,
            result.DiagnosticSamples,
            result.ConcurrentCountsMatch,
            ElapsedMilliseconds = Math.Round(result.Elapsed.TotalMilliseconds, 1),
            result.AllocatedBytes,
            result.Detail,
        });
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions));
        return path;
    }

    private static ContentFileResult Interpret(string relativePath, byte[] bytes, bool wellFormed, Stopwatch stopwatch, CancellationToken cancellation)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var pageProcessor = new CountingContentProcessor();
        var appearanceProcessor = new CountingContentProcessor();
        int pages = 0;
        int appearances = 0;
        ContentFileResult Result(ContentFileOutcome outcome, string? detail, IReadOnlyList<Diagnostic>? found = null, bool? concurrent = null) => new()
        {
            File = relativePath,
            Outcome = outcome,
            WellFormed = wellFormed,
            Pages = pages,
            Appearances = appearances,
            PageCounts = pageProcessor.Counts,
            AppearanceCounts = appearanceProcessor.Counts,
            Diagnostics = (found ?? []).GroupBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            DiagnosticSamples = (found ?? []).GroupBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => $"{group.First().ObjectReference}: {group.First().Message}", StringComparer.Ordinal),
            ConcurrentCountsMatch = concurrent,
            Detail = detail,
            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before,
            Elapsed = stopwatch.Elapsed,
        };

        try
        {
            var options = new ContentOptions { CancellationToken = cancellation };
            using PdfDocument? document = TryOpen(bytes, out string? rejection);
            if (document is null)
            {
                return Result(ContentFileOutcome.Rejected, rejection);
            }

            int pageCount = document.Pages.Count;
            int baseline = document.Diagnostics.Count;
            foreach (PdfPage page in document.Pages)
            {
                pageProcessor.ResetPage();
                page.ProcessContent(pageProcessor, options);
                pages++;
                appearances += InterpretAppearances(page, appearanceProcessor, options);
            }

            Diagnostic[] diagnostics = [.. document.Diagnostics.Skip(baseline).Where(static diagnostic => diagnostic.Severity > DiagnosticSeverity.Information)];
            bool? concurrent = wellFormed && pageCount > 1 && pageCount <= ConcurrentPassMaxPages
                ? InterpretConcurrently(bytes, options).SameAs(pageProcessor.Counts)
                : null;
            return Result(ContentFileOutcome.Interpreted, concurrent == false ? "Concurrent page counts differ from the sequential pass." : null, diagnostics, concurrent);
        }
        catch (PdfPasswordException exception)
        {
            return Result(ContentFileOutcome.PasswordRequired, exception.Message);
        }
        catch (PdfCertificateException exception)
        {
            return Result(ContentFileOutcome.CertificateRequired, exception.Message);
        }
        catch (PdfEncryptionNotSupportedException exception)
        {
            return Result(ContentFileOutcome.EncryptionUnsupported, exception.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Result(ContentFileOutcome.Timeout, $"Canceled after {TimeLimit.TotalSeconds} s on page {pages + 1}.");
        }
#pragma warning disable CA1031 // Any other exception is the finding this gate exists to report.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Result(ContentFileOutcome.Crash, $"page {pages + 1}: {exception}");
        }
    }

    /// <summary>Opens leniently; <see langword="null"/> with the reason when lenient mode gives up on the file.</summary>
    private static PdfDocument? TryOpen(byte[] bytes, out string? rejection)
    {
        try
        {
            rejection = null;
            return PdfDocument.Open(bytes);
        }
        catch (DiagnosticException exception)
        {
            rejection = exception.Message;
            return null;
        }
    }

    /// <summary>Interprets the appearance streams of every annotation of the page: N, R and D, every state of each.</summary>
    private static int InterpretAppearances(PdfPage page, CountingContentProcessor processor, ContentOptions options)
    {
        int count = 0;
        var seen = new HashSet<CosStream>(ReferenceEqualityComparer.Instance);
        foreach (PdfAnnotation annotation in page.Annotations)
        {
            if (annotation.AppearanceDictionary is not { } appearances)
            {
                continue;
            }

            foreach (PdfAppearanceMode mode in (ReadOnlySpan<PdfAppearanceMode>)[PdfAppearanceMode.Normal, PdfAppearanceMode.Rollover, PdfAppearanceMode.Down])
            {
                if (appearances.GetEntry(mode) is not { } entry)
                {
                    continue;
                }

                if (!entry.HasStates)
                {
                    count += Run(annotation, entry.Form);
                    continue;
                }

                foreach (CosName state in entry.StateNames)
                {
                    count += Run(annotation, entry.GetAppearance(state));
                }
            }
        }

        return count;

        int Run(PdfAnnotation annotation, PdfFormXObject? form)
        {
            if (form is null || !seen.Add(form.Stream))
            {
                return 0;
            }

            processor.ResetPage();
            annotation.ProcessAppearance(form, processor, options);
            return 1;
        }
    }

    /// <summary>Interprets every page of a fresh document at once, one processor per page, and sums the counts.</summary>
    private static ContentCounts InterpretConcurrently(byte[] bytes, ContentOptions options)
    {
        using PdfDocument document = PdfDocument.Open(bytes);
        PdfPage[] pages = [.. document.Pages];
        var perPage = new ContentCounts[pages.Length];
        Parallel.For(0, pages.Length, index =>
        {
            var processor = new CountingContentProcessor();
            pages[index].ProcessContent(processor, options);
            perPage[index] = processor.Counts;
        });
        var total = new ContentCounts();
        foreach (ContentCounts counts in perPage)
        {
            total.Add(counts);
        }

        return total;
    }
}
