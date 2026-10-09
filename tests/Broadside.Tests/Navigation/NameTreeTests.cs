using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.Tests.Hosting;
using Broadside.TestSupport;
using Microsoft.Extensions.Logging;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Name trees (ISO 32000-2 §7.9.6, Table 36; Annex J.3.3) read through <see cref="PdfDocument.Names"/> and
/// <see cref="PdfDocument.GetNameTree"/>: Limits-guided lookups, byte-wise key order, lenient reading of damaged trees.
/// </summary>
public class NameTreeTests
{
    private static readonly CosName Dests = new("Dests");

    [Fact]
    public void Name_tree_dests_pdf_maps_its_three_keys_to_their_destination_arrays_with_no_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-dests.pdf"));

        PdfNameTree tree = Assert.IsType<PdfNameTree>(document.Names?.Dests);

        Assert.Equal(["alpha", "beta", "gamma"], tree.Select(entry => Encoding.ASCII.GetString(entry.Key.Bytes)));
        Assert.True(tree.TryGetValue("alpha", out CosObject? alpha));
        Assert.Equal("[3 0 R /Fit]", alpha.ToString());
        Assert.True(tree.TryGetValue(new CosString("beta"u8), out CosObject? beta));
        Assert.Equal("[3 0 R /XYZ 0 792 0]", beta.ToString());
        Assert.True(tree.TryGetValue("gamma"u8, out CosObject? gamma));
        Assert.Equal("[3 0 R /FitH 792]", gamma.ToString());
        Assert.False(tree.TryGetValue("delta", out _));
        Assert.Same(tree.Root, document.GetNameTree(document.Names!.Dictionary[Dests])!.Root);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Every_key_of_a_four_level_tree_resolves_and_enumeration_returns_them_once_in_byte_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"));
        PdfNameTree tree = document.Names!.Dests!;

        byte[][] expected =
        [
            [0x18], "a"u8.ToArray(), "ab"u8.ToArray(),
            .. Enumerable.Range(1, 193).Select(number => Encoding.ASCII.GetBytes(number.ToString("'n'000", CultureInfo.InvariantCulture))),
            [0x80], [0xE9], [0xFE, 0xFF, 0x00, 0x61], [0xFE, 0xFF, 0x00, 0xE9],
        ];

        KeyValuePair<CosString, CosObject>[] entries = [.. tree];
        Assert.Equal(expected.Select(Convert.ToHexString), entries.Select(entry => Convert.ToHexString(entry.Key.Bytes)));
        Assert.Equal(Enumerable.Range(0, 200), entries.Select(entry => Top(entry.Value)));
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.True(tree.TryGetValue(expected[index], out CosObject? value));
            Assert.Equal(index, Top(value));
        }

        Assert.False(tree.TryGetValue("n000"u8, out _));
        Assert.False(tree.TryGetValue("n194"u8, out _));
        Assert.False(tree.TryGetValue(ReadOnlySpan<byte>.Empty, out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Keys_are_compared_byte_by_byte_and_a_text_lookup_tries_pdf_doc_encoding_first()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"));
        PdfNameTree tree = document.Names!.Dests!;

        // (a) and <FEFF0061> both read as "a" but are different keys; (a) is shorter-prefix-first before (ab).
        Assert.True(tree.TryGetValue([0xFE, 0xFF, 0x00, 0x61], out CosObject? utf16));
        Assert.Equal(198, Top(utf16));
        Assert.True(tree.TryGetValue("a", out CosObject? text));
        Assert.Equal(1, Top(text));

        // PDFDocEncoding 0x18 is the breve U+02D8: first in byte order although its text sorts after every ASCII letter.
        Assert.True(tree.TryGetValue("˘", out CosObject? breve));
        Assert.Equal(0, Top(breve));
        Assert.True(tree.TryGetValue("•", out CosObject? bullet));
        Assert.Equal(196, Top(bullet));
        Assert.True(tree.TryGetValue("é", out CosObject? eAcute));
        Assert.Equal(197, Top(eAcute));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_text_lookup_falls_back_to_utf_16_and_then_to_every_key_decoded()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Names [<EFBBBFE69687> (utf-8) <FEFF4E2D> (utf-16)] >>");
        using PdfDocument document = PdfDocument.Open(file);
        PdfNameTree tree = document.Names!.Dests!;

        Assert.True(tree.TryGetValue("中", out CosObject? utf16));
        Assert.Equal("(utf-16)", utf16.ToString());
        Assert.True(tree.TryGetValue("文", out CosObject? utf8));
        Assert.Equal("(utf-8)", utf8.ToString());
        Assert.False(tree.TryGetValue("斈", out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_lookup_reads_one_path_from_the_root_and_not_the_whole_tree()
    {
        using var logs = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = LazyLoadingTests.TraceLogging(logs);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"), new PdfOptions().WithLoggerFactory(loggerFactory));
        PdfNameTree tree = document.Names!.Dests!;
        int afterRoot = LazyLoadingTests.ParsedObjects(logs).Length;

        Assert.True(tree.TryGetValue("n150"u8, out CosObject? value));

        // 28 nodes below the root (20 leaves, 5 + 2 intermediate nodes); a binary search over each node's kids touches a few per level.
        Assert.Equal(152, Top(value)); // (n001) is at 3
        Assert.InRange(LazyLoadingTests.ParsedObjects(logs).Length - afterRoot, 3, 8);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_damaged_tree_reports_each_defect_once_per_node_and_every_key_still_resolves()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-broken.pdf"));
        PdfNameTree tree = document.Names!.Dests!;
        Assert.Empty(document.Diagnostics);

        Assert.Equal(["a", "b", "d", "f", "e", "g", "h"], tree.Select(entry => Encoding.ASCII.GetString(entry.Key.Bytes)));
        string[] expected =
        [
            "NameTreeLimitsInvalid 7", // (d) outside leaf 7's Limits [(a) (c)]
            "NameTreeKeysUnsorted 8", // (f) before (e)
            "NameTreeLimitsInvalid 6", // intermediate node without Limits
            "NameTreeCycle 4", // node 6 lists the root
            "NameTreeNodeInvalid 9", // odd-length Names: the dangling (i) is ignored
            "NameTreeDuplicateKey 9", // (g) again, after leaf 8's
        ];
        Assert.Equal(expected, document.Diagnostics.Select(Describe));
        Assert.All(document.Diagnostics, diagnostic => Assert.True(diagnostic.Severity >= DiagnosticSeverity.Warning));

        int[] tops = [.. "abdefgh".Select(key => Top(Lookup(tree, key)))];
        Assert.Equal([1, 2, 4, 5, 6, 7, 8], tops);
        Assert.False(tree.TryGetValue("i", out _));
        Assert.False(tree.TryGetValue("c", out _));
        Assert.Equal(expected, document.Diagnostics.Select(Describe));
    }

    [Fact]
    public void Lookups_of_a_damaged_tree_fall_back_to_the_whole_tree_and_agree_with_enumeration_on_duplicates()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-broken.pdf"));
        PdfNameTree tree = document.Names!.Dests!;

        // (d) lies outside its leaf's Limits, so the Limits-guided descent misses it; the fallback finds it and reports the damage.
        Assert.Equal(4, Top(Lookup(tree, 'd')));
        Assert.Contains("NameTreeLimitsInvalid 7", document.Diagnostics.Select(Describe));

        // (g) is in leaves 8 and 9: lookups and enumeration both give the first in tree order.
        Assert.Equal(7, Top(Lookup(tree, 'g')));
        Assert.Equal(7, Top(tree.Single(entry => entry.Key.Bytes.SequenceEqual("g"u8)).Value));
    }

    [Fact]
    public void Strict_mode_throws_from_the_lookup_that_meets_the_damage_not_from_open()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-broken.pdf"), new PdfOptions().UseStrict());
        PdfNameTree tree = document.Names!.Dests!;

        // (a) is reached through intact nodes and Limits.
        Assert.Equal(1, Top(Lookup(tree, 'a')));
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => tree.TryGetValue("d", out _));
        Assert.Equal("NameTreeLimitsInvalid 7", Describe(error.Diagnostic));
        Assert.Throws<DiagnosticException>(() => tree.ToList());
    }

    [Fact]
    public void Reading_a_tree_writes_nothing_and_a_change_to_a_node_shows_in_the_next_lookup()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-broken.pdf"));
        PdfNameTree tree = document.Names!.Dests!;
        Assert.False(tree.TryGetValue("i", out _));
        _ = tree.ToList();

        CosDictionary[] nodes = [.. Enumerable.Range(4, 6).Select(number => (CosDictionary)document.Resolve(new CosReference(number, 0)))];
        Assert.All(nodes, node => Assert.False(node.IsDirty));
        Assert.False(document.Catalog.IsDirty);

        // Leaf 9's dangling key (i) gets a value: the index built for the earlier lookups is stale and rebuilt.
        var leaf = (CosArray)nodes[5][new CosName("Names")];
        leaf.Add(new CosArray([new CosReference(3, 0), new CosName("XYZ"), new CosInteger(0), new CosInteger(9), CosNull.Instance]));

        Assert.Equal(9, Top(Lookup(tree, 'i')));
        Assert.Equal(["a", "b", "d", "f", "e", "g", "h", "i"], tree.Select(entry => Encoding.ASCII.GetString(entry.Key.Bytes)));
    }

    [Fact]
    public void A_tree_root_that_is_not_a_dictionary_reads_as_absent_with_a_diagnostic()
    {
        byte[] file = TestPdf.OnePage(pageEntries: string.Empty, catalogEntries: "/Names << /Dests 42 /AP null >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Null(document.Names!.Dests);
        Assert.Null(document.Names.GetTree(new CosName("AP")));
        Assert.Null(document.Names.GetTree(new CosName("EmbeddedFiles")));
        Assert.Equal(["NameTreeNodeInvalid -"], document.Diagnostics.Select(Describe));
    }

    [Fact]
    public void Malformed_nodes_and_keys_are_repaired_or_skipped_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Names << /Dests 4 0 R >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Limits [(a) (z)] /Names [(a) 1] /Kids [5 0 R << /Limits [(x) (x)] /Names [(x) 24] >> 6 0 R 7 0 R] >>",
            "<< /Limits [(b) (c)] /Names [/b 2 3 (three) (c) 3] >>",
            "<< >>",
            "(not a node)");
        using PdfDocument document = PdfDocument.Open(file);
        PdfNameTree tree = document.Names!.Dests!;

        Assert.Equal(["a", "b", "c", "x"], tree.Select(entry => Encoding.ASCII.GetString(entry.Key.Bytes)));
        Assert.Equal(
            [
                "NameTreeLimitsInvalid 4", // Limits on the root
                "NameTreeNodeInvalid 4", // root with both Names and Kids (and a direct kid: once per node)
                "NameTreeNodeInvalid 7", // a kid that is not a node
                "NameTreeKeyInvalid 5", // /b used by its bytes; 3 skipped
                "NameTreeLimitsInvalid 6", // a node below the root without Limits
                "NameTreeNodeInvalid 6", // ... and with neither Kids nor Names
            ],
            document.Diagnostics.Select(Describe));
        Assert.Equal("24", Lookup(tree, 'x').ToString());
        Assert.Equal("2", Lookup(tree, 'b').ToString());
    }

    private static CosObject Lookup(PdfNameTree tree, char key)
    {
        Assert.True(tree.TryGetValue(key.ToString(), out CosObject? value), $"({key}) not found");
        return value;
    }

    private static int Top(CosObject destination) => (int)Assert.IsType<CosInteger>(Assert.IsType<CosArray>(destination)[3]).Value;

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
