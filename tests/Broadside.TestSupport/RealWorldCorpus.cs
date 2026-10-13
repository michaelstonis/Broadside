using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Security;

namespace Broadside.TestSupport;

/// <summary>How opening and walking one real-world file ended.</summary>
public enum CorpusOutcomeKind
{
    /// <summary>Opened and walked with no Warning or Error diagnostic.</summary>
    Clean,

    /// <summary>Opened and walked; repairs or deviations were recorded as diagnostics.</summary>
    Diagnostics,

    /// <summary>Encrypted with a user password that is not empty (<see cref="PdfPasswordException"/>): a documented outcome.</summary>
    PasswordRequired,

    /// <summary>Encrypted for certificate recipients (<see cref="PdfCertificateException"/>): a documented outcome.</summary>
    CertificateRequired,

    /// <summary>Encrypted by a handler or algorithm this engine does not implement (<see cref="PdfEncryptionNotSupportedException"/>).</summary>
    EncryptionUnsupported,

    /// <summary>
    /// The library gave up with a <see cref="DiagnosticException"/>: in lenient mode, a file whose structure cannot be read at all;
    /// in strict mode, the first deviation.
    /// </summary>
    Rejected,

    /// <summary>Any other exception: a bug.</summary>
    Crash,

    /// <summary>
    /// The walk used more than <see cref="RealWorldCorpus.ProcessorTimeLimit"/> of processor time, or had not finished when
    /// <see cref="RealWorldCorpus.HangLimit"/> passed: a bug.
    /// </summary>
    Timeout,
}

/// <summary>The outcome of one file.</summary>
/// <param name="Kind">How it ended.</param>
/// <param name="Codes">The distinct codes of the Warning and Error diagnostics, ordinal order; for <see cref="CorpusOutcomeKind.Rejected"/>, the code thrown.</param>
/// <param name="Detail">The exception, for anything that threw.</param>
/// <param name="AllocatedBytes">Managed bytes allocated by the open and walk (on the thread that ran them).</param>
/// <param name="Elapsed">How long it took on the wall clock.</param>
public sealed record CorpusOutcome(CorpusOutcomeKind Kind, IReadOnlyList<string> Codes, string? Detail, long AllocatedBytes, TimeSpan Elapsed)
{
    /// <summary>Gets whether the outcome is a bug in the reader: an unexpected exception or a hang.</summary>
    public bool IsFailure => Kind is CorpusOutcomeKind.Crash or CorpusOutcomeKind.Timeout;
}

/// <summary>
/// The real-world corpora fetched by <c>tools/CorpusFetcher</c> (gitignored <c>corpus/&lt;id&gt;/</c>): file enumeration and the
/// open-and-walk run the corpus gate (issue #47) applies to each file, with a time limit so one file cannot stall a run.
/// </summary>
/// <remarks>
/// The limit is charged in processor time, not wall-clock time (issue #280): full parallel runs on a loaded machine stretched a
/// two-second walk past a 30-second wall-clock limit three times. <see cref="TimeLimitedWalk"/> applies both limits.
/// </remarks>
public static class RealWorldCorpus
{
    /// <summary>The environment variable that names the corpus root, overriding the search for <c>corpus/</c>.</summary>
    public const string EnvironmentVariable = CorpusLocator.RealWorldCorpusVariable;

    /// <summary>
    /// How much processor time one file's open and walk may use on its thread before it counts as too slow. The slowest file of
    /// the fetched corpora uses under a second, alone or with 64 walks sharing 16 cores.
    /// </summary>
    public static readonly TimeSpan ProcessorTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long, on the wall clock, a walk that has not finished is waited for before it counts as a hang and is abandoned: ten
    /// times <see cref="ProcessorTimeLimit"/>, since a loaded machine stretched a walk's wall-clock time more than fifteen-fold
    /// (issue #280).
    /// </summary>
    public static readonly TimeSpan HangLimit = ProcessorTimeLimit * 10;

    private static readonly ConcurrentDictionary<(string Path, bool Strict), Lazy<Task<CorpusOutcome>>> Outcomes = new();

    /// <summary>At most one walk per processor at a time, however many files are awaited together.</summary>
    private static readonly SemaphoreSlim Gate = new(Environment.ProcessorCount);

    /// <summary>The directory of the corpus <paramref name="id"/>, or <see langword="null"/> when it is not fetched.</summary>
    /// <param name="id">A corpus id from <c>tools/CorpusFetcher/corpora.json</c>.</param>
    /// <returns>The directory.</returns>
    public static string? Directory(string id) =>
        CorpusLocator.RealWorldCorpusDirectory is { } root && System.IO.Directory.Exists(Path.Combine(root, id)) ? Path.Combine(root, id) : null;

    /// <summary>
    /// Every PDF of corpus <paramref name="id"/> (any case of the <c>.pdf</c> extension) as a path relative to the corpus root,
    /// with forward slashes, ordinal order; empty when the corpus is not fetched. Files under a <c>__MACOSX</c> directory are
    /// skipped: they are the AppleDouble metadata a macOS zip carries for each file (<c>._name.pdf</c>), not PDFs (the
    /// gwg-output-suite zips carry one per PDF).
    /// </summary>
    /// <param name="id">A corpus id.</param>
    /// <returns>The relative paths, starting with <paramref name="id"/>.</returns>
    public static IReadOnlyList<string> Files(string id)
    {
        if (Directory(id) is not { } directory)
        {
            return [];
        }

        string root = CorpusLocator.RealWorldCorpusDirectory!;
        return
        [
            .. System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(static path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(static path => !path.Contains("/__MACOSX/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>The absolute path of a file named relative to the corpus root.</summary>
    /// <param name="relativePath">A path from <see cref="Files"/>.</param>
    /// <returns>The path.</returns>
    public static string PathOf(string relativePath) =>
        Path.Combine(CorpusLocator.RealWorldCorpusDirectory ?? throw new InvalidOperationException("No real-world corpus is fetched."), relativePath);

    /// <summary>
    /// Opens the file from bytes with the empty password and walks it with <see cref="DocumentWalker"/>, once per file and mode
    /// for the whole test run (the theory and the summary share the result). The walk runs through <see cref="TimeLimitedWalk"/>
    /// with <see cref="ProcessorTimeLimit"/> and <see cref="HangLimit"/>; a hung walk is abandoned (the library has no
    /// cancellation), reported as a timeout, and keeps its thread.
    /// </summary>
    /// <param name="relativePath">A path from <see cref="Files"/>.</param>
    /// <param name="strict">Strict mode instead of lenient mode.</param>
    /// <returns>The outcome.</returns>
    public static Task<CorpusOutcome> OutcomeAsync(string relativePath, bool strict = false) =>
        Outcomes.GetOrAdd((relativePath, strict), key => new Lazy<Task<CorpusOutcome>>(() => RunAsync(key.Path, key.Strict))).Value;

    private static async Task<CorpusOutcome> RunAsync(string relativePath, bool strict)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RunGatedAsync(relativePath, strict).ConfigureAwait(false);
        }
        finally
        {
            // A walk that timed out keeps running on its own thread; its slot is released anyway so the run goes on.
            Gate.Release();
        }
    }

    private static async Task<CorpusOutcome> RunGatedAsync(string relativePath, bool strict)
    {
        byte[] bytes = await File.ReadAllBytesAsync(PathOf(relativePath)).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        TimeLimitedResult<CorpusOutcome> run = await TimeLimitedWalk.RunAsync(() => Walk(bytes, strict, stopwatch), ProcessorTimeLimit, HangLimit).ConfigureAwait(false);
        return run.Verdict switch
        {
            TimeLimitedVerdict.Finished => run.Value!,
            TimeLimitedVerdict.OverBudget => run.Value! with
            {
                Kind = CorpusOutcomeKind.Timeout,
                Detail = string.Create(CultureInfo.InvariantCulture, $"Used {run.ProcessorTime.TotalSeconds:F1} s of processor time; the limit is {ProcessorTimeLimit.TotalSeconds} s."),
            },
            _ => new CorpusOutcome(CorpusOutcomeKind.Timeout, [], $"Did not finish within {HangLimit.TotalSeconds} s on the wall clock.", 0, run.Elapsed),
        };
    }

    private static CorpusOutcome Walk(byte[] bytes, bool strict, Stopwatch stopwatch)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        CorpusOutcome Outcome(CorpusOutcomeKind kind, IReadOnlyList<string> codes, string? detail = null) =>
            new(kind, codes, detail, GC.GetAllocatedBytesForCurrentThread() - before, stopwatch.Elapsed);

        try
        {
            var options = new PdfOptions();
            if (strict)
            {
                options.UseStrict();
            }

            using PdfDocument document = PdfDocument.Open(bytes, options);
            _ = DocumentWalker.Walk(document);
            string[] codes =
            [
                .. document.Diagnostics
                    .Where(static diagnostic => diagnostic.Severity > DiagnosticSeverity.Information)
                    .Select(static diagnostic => diagnostic.Code)
                    .Distinct()
                    .Order(StringComparer.Ordinal),
            ];
            return Outcome(codes.Length == 0 ? CorpusOutcomeKind.Clean : CorpusOutcomeKind.Diagnostics, codes);
        }
        catch (DiagnosticException exception)
        {
            return Outcome(CorpusOutcomeKind.Rejected, [exception.Diagnostic.Code], exception.Message);
        }
        catch (PdfPasswordException exception)
        {
            return Outcome(CorpusOutcomeKind.PasswordRequired, [], exception.Message);
        }
        catch (PdfCertificateException exception)
        {
            return Outcome(CorpusOutcomeKind.CertificateRequired, [], exception.Message);
        }
        catch (PdfEncryptionNotSupportedException exception)
        {
            return Outcome(CorpusOutcomeKind.EncryptionUnsupported, [], exception.Message);
        }
#pragma warning disable CA1031 // Any other exception is the finding this run exists to report.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Outcome(CorpusOutcomeKind.Crash, [], exception.ToString());
        }
    }
}
