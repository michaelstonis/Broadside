using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.CorpusGate;

/// <summary>
/// The real-world rendering gate (issue #138), one class per fetched corpus so xUnit runs the corpora in parallel: every page of
/// every file renders through the software rasterizer with every layer (see <see cref="RenderCorpusGate"/>), with no exception and
/// no page over its processor-time budget. Every Warning or Error diagnostic the renders record on a well-formed file (the #47
/// classification) must be triaged: either the content gate already lists that file and code
/// (<c>tests/Broadside.Tests/Content/CorpusGate/content-diagnostics.allowlist.txt</c>, issue #80), or
/// <c>render-diagnostics.allowlist.txt</c> lists it with its exact count and a justification. The per-corpus totals are a Verify
/// snapshot. The corpora are fetched by <c>tools/CorpusFetcher</c>; each test skips when its corpus is absent.
/// ISO 32000-2 §8, §10, §11, §12.5.
/// </summary>
[Trait("Category", "Corpus")]
public abstract class RenderCorpusGateTests(ITestOutputHelper output)
{
    /// <summary>The render allowlist, next to this file: <c>corpus/path TAB code TAB count TAB justification</c>, sorted.</summary>
    public static readonly string AllowlistPath = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Broadside.Rendering.Tests", "CorpusGate", "render-diagnostics.allowlist.txt"));

    /// <summary>The content gate's allowlist (#80): diagnostics interpretation records, already triaged there.</summary>
    public static readonly string ContentAllowlistPath = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Content", "CorpusGate", "content-diagnostics.allowlist.txt"));

    /// <summary>Gets the fetcher id of the corpus this class runs.</summary>
    protected abstract string CorpusId { get; }

    [Fact]
    public async Task Every_page_renders_and_every_diagnostic_is_triaged()
    {
        if (RealWorldCorpus.Directory(CorpusId) is null)
        {
            Assert.Skip($"Corpus '{CorpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        IReadOnlyList<RenderFileResult> results = await RenderCorpusGate.RunAsync(RealWorldCorpus.Files(CorpusId));
        output.WriteLine($"Per-file detail: {RenderCorpusGate.WriteDetail(CorpusId, results)}");
        foreach (RenderFileResult slow in results.OrderByDescending(result => result.SlowestPageTime).Take(5))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{slow.SlowestPageTime.TotalMilliseconds:F0} ms processor time: {slow.File} page {slow.SlowestPage + 1}"));
        }

        string[] failures = [.. results.Where(result => result.IsFailure).Select(result => $"{result.File}: {result.Outcome} {result.Detail}")];
        Assert.True(failures.Length == 0, $"{failures.Length} file(s) failed:\n{string.Join("\n", failures)}");

        CompareWithAllowlist(results);
        await Verify(Summarize(CorpusId, results));
    }

    /// <summary>Reads an allowlist's entries of <paramref name="corpusId"/>: (file, code) to count.</summary>
    private static Dictionary<(string File, string Code), int> ReadAllowlist(string path, string corpusId)
    {
        var entries = new Dictionary<(string, string), int>();
        foreach (string line in File.ReadLines(path))
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

    private void CompareWithAllowlist(IReadOnlyList<RenderFileResult> results)
    {
        Dictionary<(string File, string Code), int> content = ReadAllowlist(ContentAllowlistPath, CorpusId);
        Dictionary<(string File, string Code), int> allowed = ReadAllowlist(AllowlistPath, CorpusId);
        var actual = new Dictionary<(string File, string Code), int>();
        foreach (RenderFileResult result in results.Where(result => result.WellFormed))
        {
            foreach ((string code, int count) in result.Diagnostics.Where(entry => !content.ContainsKey((result.File, entry.Key))))
            {
                actual[(result.File, code)] = count;
            }
        }

        var problems = new List<string>();
        foreach (((string file, string code), int count) in actual.OrderBy(entry => entry.Key.File, StringComparer.Ordinal).ThenBy(entry => entry.Key.Code, StringComparer.Ordinal))
        {
            if (!allowed.TryGetValue((file, code), out int listed))
            {
                string sample = results.First(result => result.File == file).DiagnosticSamples[code];
                problems.Add($"triage me: {file}\t{code}\t{count}\t({sample})");
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

    /// <summary>The snapshot: corpus ref, outcome counts, pages, and the diagnostic histogram of well-formed files.</summary>
    private static string Summarize(string corpusId, IReadOnlyList<RenderFileResult> results)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"corpus: {corpusId} @ {ContentCorpusGate.CorpusRef(corpusId)}\n");
        text.Append(CultureInfo.InvariantCulture, $"files: {results.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"well-formed files: {results.Count(result => result.WellFormed)}\n");
        text.Append("outcomes:\n");
        foreach (RenderFileOutcome outcome in Enum.GetValues<RenderFileOutcome>())
        {
            text.Append(CultureInfo.InvariantCulture, $"  {outcome}: {results.Count(result => result.Outcome == outcome)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"pages rendered: {results.Sum(result => result.Pages)}\n");
        text.Append("render diagnostics on well-formed files (files, total):\n");
        foreach (IGrouping<string, KeyValuePair<string, int>> code in results
            .Where(result => result.WellFormed)
            .SelectMany(result => result.Diagnostics)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"  {code.Key}: {code.Count()}, {code.Sum(entry => entry.Value)}\n");
        }

        return text.ToString();
    }
}

/// <summary>Reproduces the render gate on the files whose corpus-relative path matches <c>BROADSIDE_CORPUS_FILTER</c> (a glob).</summary>
[Trait("Category", "Corpus")]
public sealed class RenderCorpusGateReproduction(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task The_files_matching_the_filter_render_without_an_exception()
    {
        string? filter = Environment.GetEnvironmentVariable(ContentCorpusGateReproductionVariable);
        string id = filter is null ? string.Empty : filter[..Math.Max(0, filter.IndexOf('/', StringComparison.Ordinal))];
        if (string.IsNullOrWhiteSpace(filter) || id.Length == 0 || RealWorldCorpus.Directory(id) is null)
        {
            Assert.Skip($"Set {ContentCorpusGateReproductionVariable} to a glob over corpus-relative paths, such as pdfjs/issue1*.pdf, of a fetched corpus.");
        }

        string[] files = [.. RealWorldCorpus.Files(id).Where(file => FileSystemName.MatchesSimpleExpression(filter, file))];
        IReadOnlyList<RenderFileResult> results = await RenderCorpusGate.RunAsync(files);
        foreach (RenderFileResult result in results)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.File}: {result.Outcome}, well-formed {result.WellFormed}, {result.Pages} pages, slowest page {result.SlowestPage + 1} {result.SlowestPageTime.TotalMilliseconds:F0} ms\n  diagnostics: {string.Join(", ", result.Diagnostics.Select(entry => $"{entry.Key}={entry.Value}"))}\n  {result.Detail}"));
        }

        Assert.DoesNotContain(results, result => result.IsFailure);
    }

    private const string ContentCorpusGateReproductionVariable = "BROADSIDE_CORPUS_FILTER";
}

public sealed class PdfjsRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "pdfjs";
}

public sealed class PdfboxRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "pdfbox";
}

public sealed class QpdfRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "qpdf";
}

public sealed class PdfiumRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "pdfium-tests";
}

public sealed class Pdf20ExamplesRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "pdf20examples";
}

public sealed class VeraPdfRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "verapdf-corpus";
}

public sealed class BfoPdfaRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "bfo-pdfa-testsuite";
}

public sealed class GwgRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "gwg-output-suite";
}

public sealed class SafeDocsRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "safedocs";
}

public sealed class IsartorRenderCorpusGate(ITestOutputHelper output) : RenderCorpusGateTests(output)
{
    protected override string CorpusId => "isartor";
}
