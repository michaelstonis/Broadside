using System.Collections.Concurrent;
using System.Diagnostics;
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

    /// <summary>The file did not finish within the time limit: a bug.</summary>
    Timeout,
}

/// <summary>The outcome of one file.</summary>
/// <param name="Kind">How it ended.</param>
/// <param name="Codes">The distinct codes of the Warning and Error diagnostics, ordinal order; for <see cref="CorpusOutcomeKind.Rejected"/>, the code thrown.</param>
/// <param name="Detail">The exception, for anything that threw.</param>
/// <param name="AllocatedBytes">Managed bytes allocated by the open and walk (on the thread that ran them).</param>
/// <param name="Elapsed">How long it took.</param>
public sealed record CorpusOutcome(CorpusOutcomeKind Kind, IReadOnlyList<string> Codes, string? Detail, long AllocatedBytes, TimeSpan Elapsed)
{
    /// <summary>Gets whether the outcome is a bug in the reader: an unexpected exception or a hang.</summary>
    public bool IsFailure => Kind is CorpusOutcomeKind.Crash or CorpusOutcomeKind.Timeout;
}

/// <summary>
/// The real-world corpora fetched by <c>tools/CorpusFetcher</c> (gitignored <c>corpus/&lt;id&gt;/</c>): file enumeration and the
/// open-and-walk run the corpus gate (issue #47) applies to each file, with a time limit so one file cannot stall a run.
/// </summary>
public static class RealWorldCorpus
{
    /// <summary>The environment variable that names the corpus root, overriding the search for <c>corpus/</c>.</summary>
    public const string EnvironmentVariable = CorpusLocator.RealWorldCorpusVariable;

    /// <summary>How long one file may take before it counts as a hang.</summary>
    public static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(30);

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
    /// with forward slashes, ordinal order; empty when the corpus is not fetched.
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
    /// for the whole test run (the theory and the summary share the result). The walk runs on its own task with
    /// <see cref="TimeLimit"/>; a hung walk is abandoned (the library has no cancellation), reported as a timeout, and keeps its
    /// thread.
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
        Task<CorpusOutcome> walk = Task.Factory.StartNew(
            () => Walk(bytes, strict, stopwatch),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            return await walk.WaitAsync(TimeLimit).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new CorpusOutcome(CorpusOutcomeKind.Timeout, [], $"Did not finish within {TimeLimit.TotalSeconds} s.", 0, stopwatch.Elapsed);
        }
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
