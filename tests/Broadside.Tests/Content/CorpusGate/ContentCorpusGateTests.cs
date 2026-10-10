using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content.CorpusGate;

/// <summary>
/// The content interpreter corpus gate (issue #80), one test class per fetched corpus so xUnit runs the corpora in parallel: every
/// page of every file is interpreted with <see cref="CountingContentProcessor"/> (forms, each tiling cell, each Type 3 glyph description and
/// each soft-mask group entered; every mesh decoded), then every annotation appearance stream (N, R, D, every state), with a
/// per-file time limit (<see cref="ContentCorpusGate.TimeLimit"/>). No exception and no timeout may occur on any file. Every Warning
/// or Error diagnostic interpretation records on a well-formed file (the #47 classification) must be listed, with its exact count,
/// in <c>content-diagnostics.allowlist.txt</c>, and every listed entry must still occur. Well-formed multi-page files are also
/// interpreted with all pages in parallel on one document, and the counts must equal the sequential pass. The per-corpus totals
/// are a Verify snapshot. The corpora are fetched by <c>tools/CorpusFetcher</c>; the test skips when its corpus is absent.
/// ISO 32000-2 §7.8, §8, §9, §12.5.5.
/// </summary>
[Trait("Category", "Corpus")]
public abstract class ContentCorpusGateTests(ITestOutputHelper output)
{
    /// <summary>The allowlist, next to this file: <c>corpus/path TAB code TAB count TAB justification</c>, sorted.</summary>
    public static readonly string AllowlistPath = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Content", "CorpusGate", "content-diagnostics.allowlist.txt"));

    /// <summary>Gets the fetcher id of the corpus this class runs.</summary>
    protected abstract string CorpusId { get; }

    [Fact]
    public async Task Every_stream_interprets_and_every_diagnostic_is_triaged()
    {
        if (RealWorldCorpus.Directory(CorpusId) is null)
        {
            Assert.Skip($"Corpus '{CorpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        IReadOnlyList<string> files = RealWorldCorpus.Files(CorpusId);
        IReadOnlyList<ContentFileResult> results = await ContentCorpusGate.RunAsync(files);
        string detail = ContentCorpusGate.WriteDetail(CorpusId, results);
        output.WriteLine($"Per-file detail: {detail}");
        foreach (ContentFileResult slow in results.OrderByDescending(result => result.Elapsed).Take(5))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{slow.Elapsed.TotalMilliseconds:F0} ms, {slow.AllocatedBytes / (1024 * 1024)} MB: {slow.File}"));
        }

        string[] failures = [.. results.Where(result => result.IsFailure).Select(result => $"{result.File}: {result.Outcome} {result.Detail}")];
        Assert.True(failures.Length == 0, $"{failures.Length} file(s) failed:\n{string.Join("\n", failures)}");

        // Lenient opening gives up on exactly the files #47 lists as unreadable.
        string[] rejected = [.. results.Where(result => result.Outcome == ContentFileOutcome.Rejected && !RealWorldCorpusTests.Unreadable.ContainsKey(result.File)).Select(result => result.File)];
        Assert.Empty(rejected);

        CompareWithAllowlist(results);
        await Verify(Summarize(CorpusId, results));
    }

    /// <summary>Reads the allowlist entries of <paramref name="corpusId"/>: (file, code) to count.</summary>
    /// <param name="corpusId">A corpus id.</param>
    /// <returns>The entries.</returns>
    internal static Dictionary<(string File, string Code), int> ReadAllowlist(string corpusId)
    {
        var entries = new Dictionary<(string, string), int>();
        foreach (string line in File.ReadLines(AllowlistPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            Assert.True(fields.Length == 4 && fields[3].Trim().Length > 0, $"Allowlist line needs file, code, count and a justification: {line}");
            if (fields[0].StartsWith(corpusId + "/", StringComparison.Ordinal))
            {
                Assert.True(entries.TryAdd((fields[0], fields[1]), int.Parse(fields[2], CultureInfo.InvariantCulture)), $"Duplicate allowlist entry: {line}");
            }
        }

        return entries;
    }

    private void CompareWithAllowlist(IReadOnlyList<ContentFileResult> results)
    {
        Dictionary<(string File, string Code), int> allowed = ReadAllowlist(CorpusId);
        var actual = new Dictionary<(string File, string Code), int>();
        foreach (ContentFileResult result in results.Where(result => result.WellFormed))
        {
            foreach ((string code, int count) in result.Diagnostics)
            {
                actual[(result.File, code)] = count;
            }
        }

        var problems = new List<string>();
        foreach (((string file, string code), int count) in actual.OrderBy(entry => entry.Key.File, StringComparer.Ordinal).ThenBy(entry => entry.Key.Code, StringComparer.Ordinal))
        {
            if (!allowed.TryGetValue((file, code), out int listed))
            {
                problems.Add($"triage me: {file}\t{code}\t{count}");
            }
            else if (listed != count)
            {
                problems.Add($"count changed: {file}\t{code}\t{count} (listed {listed})");
            }
        }

        foreach ((string file, string code) in allowed.Keys.Where(key => !actual.ContainsKey(key)))
        {
            problems.Add($"stale entry, delete it: {file}\t{code}");
        }

        Assert.True(problems.Count == 0, $"Diagnostics on well-formed files differ from {AllowlistPath}:\n{string.Join("\n", problems)}");
    }

    /// <summary>The snapshot: corpus ref, outcome counts, pages, streams and event totals by bucket, diagnostic histograms. Sums only.</summary>
    private static string Summarize(string corpusId, IReadOnlyList<ContentFileResult> results)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"corpus: {corpusId} @ {ContentCorpusGate.CorpusRef(corpusId)}\n");
        text.Append(CultureInfo.InvariantCulture, $"files: {results.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"well-formed files: {results.Count(result => result.WellFormed)}\n");
        text.Append("outcomes:\n");
        foreach (ContentFileOutcome outcome in Enum.GetValues<ContentFileOutcome>())
        {
            text.Append(CultureInfo.InvariantCulture, $"  {outcome}: {results.Count(result => result.Outcome == outcome)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"pages: {results.Sum(result => result.Pages)}\n");
        text.Append(CultureInfo.InvariantCulture, $"appearance streams: {results.Sum(result => result.Appearances)}\n");
        text.Append(CultureInfo.InvariantCulture, $"concurrent passes: {results.Count(result => result.ConcurrentCountsMatch is not null)}\n");
        AppendCounts(text, "page events", results.Select(result => result.PageCounts));
        AppendCounts(text, "appearance events", results.Select(result => result.AppearanceCounts));
        AppendHistogram(text, "diagnostics on well-formed files (files, total)", results.Where(result => result.WellFormed));
        AppendHistogram(text, "diagnostics on other files (files, total)", results.Where(result => !result.WellFormed));
        return text.ToString();
    }

    private static void AppendCounts(StringBuilder text, string title, IEnumerable<ContentCounts> counts)
    {
        var total = new ContentCounts();
        foreach (ContentCounts item in counts)
        {
            total.Add(item);
        }

        text.Append(CultureInfo.InvariantCulture, $"{title}:\n");
        foreach ((ContentCounter counter, long value) in total.Values())
        {
            text.Append(CultureInfo.InvariantCulture, $"  {counter}: {value}\n");
        }
    }

    private static void AppendHistogram(StringBuilder text, string title, IEnumerable<ContentFileResult> results)
    {
        text.Append(CultureInfo.InvariantCulture, $"{title}:\n");
        foreach (IGrouping<string, KeyValuePair<string, int>> code in results
            .SelectMany(result => result.Diagnostics)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"  {code.Key}: {code.Count()}, {code.Sum(entry => entry.Value)}\n");
        }
    }
}

/// <summary>Reproduces the gate on the files whose corpus-relative path matches <c>BROADSIDE_CORPUS_FILTER</c> (a glob), with full output.</summary>
[Trait("Category", "Corpus")]
public sealed class ContentCorpusGateReproduction(ITestOutputHelper output)
{
    /// <summary>The environment variable holding the glob, such as <c>pdfjs/issue1*.pdf</c>.</summary>
    public const string FilterVariable = "BROADSIDE_CORPUS_FILTER";

    [Fact(Explicit = true)]
    public async Task The_files_matching_the_filter_interpret_without_an_exception()
    {
        string? filter = Environment.GetEnvironmentVariable(FilterVariable);
        string id = filter is null ? string.Empty : filter[..Math.Max(0, filter.IndexOf('/', StringComparison.Ordinal))];
        if (string.IsNullOrWhiteSpace(filter) || id.Length == 0 || RealWorldCorpus.Directory(id) is null)
        {
            Assert.Skip($"Set {FilterVariable} to a glob over corpus-relative paths, such as pdfjs/issue1*.pdf, of a fetched corpus.");
        }

        string[] files = [.. RealWorldCorpus.Files(id).Where(file => FileSystemName.MatchesSimpleExpression(filter, file))];
        IReadOnlyList<ContentFileResult> results = await ContentCorpusGate.RunAsync(files);
        foreach (ContentFileResult result in results)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.File}: {result.Outcome}, well-formed {result.WellFormed}, {result.Pages} pages, {result.Appearances} appearances, {result.Elapsed.TotalMilliseconds:F0} ms\n  pages: {result.PageCounts}\n  appearances: {result.AppearanceCounts}\n  diagnostics: {string.Join(", ", result.Diagnostics.Select(entry => $"{entry.Key}={entry.Value}"))}\n  {result.Detail}"));
        }

        Assert.DoesNotContain(results, result => result.IsFailure);
    }
}

public sealed class PdfjsContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "pdfjs";
}

public sealed class PdfboxContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "pdfbox";
}

public sealed class QpdfContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "qpdf";
}

public sealed class PdfiumContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "pdfium-tests";
}

public sealed class Pdf20ExamplesContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "pdf20examples";
}

public sealed class VeraPdfContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "verapdf-corpus";
}

public sealed class BfoPdfaContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "bfo-pdfa-testsuite";
}

public sealed class GwgContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "gwg-output-suite";
}

public sealed class SafeDocsContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "safedocs";
}

public sealed class IsartorContentCorpusGate(ITestOutputHelper output) : ContentCorpusGateTests(output)
{
    protected override string CorpusId => "isartor";
}
