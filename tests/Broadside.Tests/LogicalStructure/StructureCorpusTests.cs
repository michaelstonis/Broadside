using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.LogicalStructure;

/// <summary>
/// The structure tree of every corpus file: the hand-written well-formed files read with no diagnostic, and every real-world file
/// (the gate's corpora plus veraPDF's, which holds most of the tagged files) walks leniently without an exception and in time. The
/// real-world summary (how many files are tagged, how many elements, which structure diagnostics) is a snapshot reviewed by hand.
/// ISO 32000-2 §14.7, §14.8.
/// </summary>
public class StructureCorpusTests(ITestOutputHelper output)
{
    private static readonly string[] CorpusIds = ["pdfjs", "pdfbox", "qpdf", "pdfium-tests", "pdf20examples", "verapdf-corpus"];

    /// <summary>The codes the structure views report; other codes (objects loaded on the way) belong to the document gate.</summary>
    private static readonly HashSet<string> StructureCodes = new(StringComparer.Ordinal)
    {
        "MarkInfoInvalid", "StructTreeRootInvalid", "StructElemInvalid", "StructElemTypeUnknown", "StructElemParentMismatch",
        "StructElemPageMissing", "StructTreeCycle", "StructTreeDepthExceeded", "ParentTreeMissing", "ParentTreeEntryInvalid",
        "McidDuplicate", "IdTreeDuplicate", "IdTreeEntryInvalid", "NamespaceInvalid", "NamespaceNotDeclared", "StructureTypeUnresolved",
        "AttributeObjectInvalid", "AttributeOwnerInvalid", "AttributeRevisionInvalid",
        "NameTreeNodeInvalid", "NameTreeLimitsInvalid", "NameTreeKeysUnsorted", "NameTreeDuplicateKey", "NameTreeKeyInvalid", "NameTreeCycle", "NameTreeTooDeep",
        "NumberTreeNodeInvalid", "NumberTreeLimitsInvalid", "NumberTreeKeysUnsorted", "NumberTreeDuplicateKey", "NumberTreeKeyInvalid", "NumberTreeCycle", "NumberTreeTooDeep",
    };

    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void A_well_formed_file_reads_its_structure_tree_with_no_diagnostics(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        int elements = StructureWalker.Walk(document);

        Assert.Equal(fileName switch { "tagged-structure.pdf" => 18, "associated-files.pdf" => 1, _ => -1 }, elements);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task Every_real_world_structure_tree_walks_leniently_and_the_summary_matches_the_snapshot()
    {
        string[] fetched = [.. CorpusIds.Where(id => RealWorldCorpus.Directory(id) is not null)];
        if (fetched.Length != CorpusIds.Length)
        {
            Assert.Skip($"Corpora not fetched ({string.Join(", ", CorpusIds.Except(fetched))}); run tools/CorpusFetcher.");
        }

        var results = new ConcurrentDictionary<string, (int Elements, string[] Codes, string? Failure)>(StringComparer.Ordinal);
        Parallel.ForEach(
            CorpusIds.SelectMany(RealWorldCorpus.Files),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            file => results[file] = WalkWithTimeLimit(file));

        string[] failures = [.. results.Where(static entry => entry.Value.Failure is not null).Select(static entry => $"{entry.Key}: {entry.Value.Failure}").Order(StringComparer.Ordinal)];
        Assert.Empty(failures);
        string summary = Summarize(results);
        output.WriteLine(summary);
        foreach ((string file, (int _, string[] codes, string? _)) in results.Where(static entry => entry.Value.Codes.Length > 0).OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{file}: {string.Join(", ", codes)}");
        }

        await Verify(summary);
    }

    private static (int Elements, string[] Codes, string? Failure) WalkWithTimeLimit(string file)
    {
        Task<(int, string[], string?)> walk = Task.Factory.StartNew(() => WalkOne(file), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return walk.Wait(RealWorldCorpus.TimeLimit) ? walk.Result : (0, [], "timed out");
    }

    private static (int Elements, string[] Codes, string? Failure) WalkOne(string file)
    {
        try
        {
            using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
            int before = document.Diagnostics.Count;
            int elements = StructureWalker.Walk(document);
            string[] codes = [.. document.Diagnostics.Skip(before).Where(static diagnostic => diagnostic.Severity > DiagnosticSeverity.Information && StructureCodes.Contains(diagnostic.Code)).Select(static diagnostic => diagnostic.Code).Distinct()];
            return (elements, codes, null);
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfCertificateException or PdfEncryptionNotSupportedException)
        {
            return (-1, [], null);
        }
#pragma warning disable CA1031 // Any other exception is the finding.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return (0, [], exception.ToString());
        }
    }

    private static string Summarize(ConcurrentDictionary<string, (int Elements, string[] Codes, string? Failure)> results)
    {
        var text = new StringBuilder();
        foreach (string id in CorpusIds)
        {
            var corpus = results.Where(entry => entry.Key.StartsWith(id + "/", StringComparison.Ordinal)).Select(static entry => entry.Value).ToList();
            text.Append(CultureInfo.InvariantCulture, $"{id}: {corpus.Count} files, {corpus.Count(static result => result.Elements >= 0)} with a structure tree, {corpus.Where(static result => result.Elements > 0).Sum(static result => (long)result.Elements)} elements\n");
            foreach (IGrouping<string, string> code in corpus.SelectMany(static result => result.Codes).GroupBy(static code => code, StringComparer.Ordinal).OrderByDescending(static group => group.Count()).ThenBy(static group => group.Key, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"  {code.Key}: {code.Count()}\n");
            }
        }

        return text.ToString();
    }
}
