using System.Globalization;
using System.Text;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// The real-world corpus gate (issue #47): every PDF of the pdf.js, PDFBox, qpdf and PDFium corpora (and the PDF Association's PDF
/// 2.0 examples) opens in lenient mode and walks completely (<see cref="DocumentWalker"/>: pages, every object, every stream
/// decoded) without an exception and within <see cref="RealWorldCorpus.TimeLimit"/>. The documented outcomes that are not crashes
/// are counted per corpus: a password or a certificate is required, the encryption is not supported, or the file is listed in
/// <see cref="Unreadable"/>. The corpora are fetched by <c>tools/CorpusFetcher</c> and are not in CI; every test skips when they
/// are absent. ISO 32000-2 §7.
/// </summary>
[Trait("Category", "Corpus")]
public class RealWorldCorpusTests(ITestOutputHelper output)
{
    /// <summary>The corpora of the gate, by fetcher id.</summary>
    public static readonly string[] CorpusIds = ["pdfjs", "pdfbox", "qpdf", "pdfium-tests", "pdf20examples"];

    /// <summary>
    /// Files no reader can open in lenient mode: each threw <c>CatalogNotFound</c>, and each was checked by hand against qpdf 12.4.2
    /// (<c>qpdf --show-npages</c> fails on every one) and poppler (<c>pdfinfo</c>). A file another reader opens does not belong here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Unreadable = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pdfbox/org/apache/pdfbox/pdfparser/PDFBOX-6041-example.pdf"] = "No %PDF- header and no trailer (pdfinfo: Couldn't find trailer dictionary).",
        ["pdfium-tests/fx/FRC_3.5_part1/FRC_3.5_Encrypt_is_damage.pdf"] = "Encrypt dictionary and cross-reference deliberately damaged (qpdf: xref not found; pdfinfo: Unknown compression method).",
        ["pdfjs/Brotli-Prototype-FileA.pdf"] = "Cross-reference stream and the object stream holding the page tree use /BrotliDecode, which ISO 32000-2:2020 does not define (qpdf: unfilterable stream; poppler 25+ reads Brotli).",
        ["pdfjs/REDHAT-1531897-0.pdf"] = "Fuzzed: cross-reference stream with an out-of-range number and no recoverable catalog (qpdf and pdfinfo fail too).",
        ["qpdf/bad1.pdf"] = "Not a PDF: no header, no objects (qpdf: can't find PDF header).",
        ["qpdf/issue-100.pdf"] = "Fuzzed (qpdf issue 100): no readable cross-reference and no catalog.",
        ["qpdf/issue-101.pdf"] = "Fuzzed (qpdf issue 101): no readable cross-reference and no catalog.",
        ["qpdf/issue-141a.pdf"] = "Fuzzed (qpdf issue 141): cross-reference stream without Length, no catalog.",
        ["qpdf/issue-141b.pdf"] = "Fuzzed (qpdf issue 141): no header, no catalog.",
        ["qpdf/issue-146.pdf"] = "Fuzzed (qpdf issue 146): no startxref, no catalog.",
        ["qpdf/issue-147.pdf"] = "Fuzzed (qpdf issue 147): no header, no catalog.",
        ["qpdf/issue-148.pdf"] = "Fuzzed (qpdf issue 148): cross-reference stream without Length, no catalog.",
        ["qpdf/issue-150.pdf"] = "Fuzzed (qpdf issue 150): integer overflow in the cross-reference stream, no catalog.",
        ["qpdf/issue-263.pdf"] = "Fuzzed (qpdf issue 263): no header, no catalog.",
        ["qpdf/issue-335a.pdf"] = "Fuzzed (qpdf issue 335): no header, no catalog.",
        ["qpdf/issue-335b.pdf"] = "Fuzzed (qpdf issue 335): no header, no trailer, no catalog.",
        ["qpdf/issue-99b.pdf"] = "Fuzzed (qpdf issue 99): object 0 referenced, no catalog.",
    };

    public static TheoryData<string> Files
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string id in CorpusIds)
            {
                IReadOnlyList<string> files = RealWorldCorpus.Files(id);
                if (files.Count == 0)
                {
                    data.Add($"{id}/(not fetched)");
                }

                foreach (string file in files)
                {
                    data.Add(file);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_real_world_file_opens_and_walks_leniently_without_an_exception(string file)
    {
        SkipWhenNotFetched(file);

        CorpusOutcome outcome = await RealWorldCorpus.OutcomeAsync(file);

        Assert.False(outcome.IsFailure, $"{outcome.Kind}: {outcome.Detail}");
        if (outcome.Kind == CorpusOutcomeKind.Rejected)
        {
            Assert.True(Unreadable.ContainsKey(file), $"Lenient mode gave up on a file that is not listed as unreadable: {outcome.Detail}");
        }
    }

    [Fact]
    public async Task The_per_corpus_counts_and_diagnostic_histograms_match_the_snapshot()
    {
        string[] fetched = [.. CorpusIds.Where(id => RealWorldCorpus.Directory(id) is not null)];
        if (fetched.Length != CorpusIds.Length)
        {
            Assert.Skip($"Corpora not fetched ({string.Join(", ", CorpusIds.Except(fetched))}); run tools/CorpusFetcher.");
        }

        var outcomes = new Dictionary<string, CorpusOutcome>(StringComparer.Ordinal);
        foreach (string id in CorpusIds)
        {
            string[] files = [.. RealWorldCorpus.Files(id)];
            CorpusOutcome[] results = await Task.WhenAll(files.Select(file => RealWorldCorpus.OutcomeAsync(file)));
            for (int index = 0; index < files.Length; index++)
            {
                outcomes[files[index]] = results[index];
            }
        }

        string summary = Summarize(outcomes);
        output.WriteLine(summary);
        foreach ((string file, CorpusOutcome outcome) in outcomes.OrderByDescending(entry => entry.Value.AllocatedBytes).Take(5))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"allocated {outcome.AllocatedBytes / (1024 * 1024)} MB, {outcome.Elapsed.TotalMilliseconds:F0} ms: {file}"));
        }

        string[] failures = [.. outcomes.Where(entry => entry.Value.IsFailure).Select(entry => $"{entry.Key}: {entry.Value.Kind} {entry.Value.Detail}")];
        Assert.Empty(failures);
        string[] stale = [.. Unreadable.Keys.Where(file => !outcomes.TryGetValue(file, out CorpusOutcome? outcome) || outcome.Kind != CorpusOutcomeKind.Rejected)];
        Assert.Empty(stale);
        await Verify(summary);
    }

    internal static void SkipWhenNotFetched(string file)
    {
        if (file.EndsWith("/(not fetched)", StringComparison.Ordinal))
        {
            Assert.Skip($"Corpus '{file[..file.IndexOf('/', StringComparison.Ordinal)]}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }
    }

    /// <summary>Per corpus: the count of each outcome, then how many files carry each diagnostic code.</summary>
    private static string Summarize(Dictionary<string, CorpusOutcome> outcomes)
    {
        var text = new StringBuilder();
        foreach (string id in CorpusIds)
        {
            CorpusOutcome[] corpus = [.. outcomes.Where(entry => entry.Key.StartsWith(id + "/", StringComparison.Ordinal)).Select(entry => entry.Value)];
            text.Append(CultureInfo.InvariantCulture, $"{id}: {corpus.Length} files\n");
            foreach (CorpusOutcomeKind kind in Enum.GetValues<CorpusOutcomeKind>())
            {
                text.Append(CultureInfo.InvariantCulture, $"  {kind}: {corpus.Count(outcome => outcome.Kind == kind)}\n");
            }

            text.Append("  files with each diagnostic code:\n");
            foreach (IGrouping<string, string> code in corpus
                .Where(outcome => outcome.Kind == CorpusOutcomeKind.Diagnostics)
                .SelectMany(outcome => outcome.Codes)
                .GroupBy(code => code, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"    {code.Key}: {code.Count()}\n");
            }
        }

        return text.ToString();
    }
}
