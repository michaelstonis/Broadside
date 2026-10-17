using System.Globalization;
using System.Text;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>
/// Writing name trees (ISO 32000-2 §7.9.6, Table 36) and number trees (§7.9.7, Table 37): balanced trees built from keys, edited in
/// place with every <c>Limits</c> entry kept correct, read back through <see cref="PdfNameTree"/> and <see cref="PdfNumberTree"/>
/// after a save, and checked by qpdf.
/// </summary>
public sealed class TreeWritingTests
{
    private static readonly CosName Names = new("Names");
    private static readonly CosName Dests = new("Dests");
    private static readonly CosName Nums = new("Nums");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName PageLabels = new("PageLabels");
    private static readonly CosName St = new("St");

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 1)]
    [InlineData(100_000, 2)]
    public void A_name_tree_built_from_unsorted_keys_reads_back_in_byte_order_after_a_save(int count, int depth)
    {
        using PdfDocument document = PdfDocument.Create();
        CosReference page = PageReference(document);
        byte[][] keys = AsciiKeys(count, seed: count);
        PdfNameTree built = document.CreateNameTree(
            keys.Select((key, index) => KeyValuePair.Create(new CosString(key), (CosObject)Destination(page, index))));
        document.Catalog[Names] = new CosDictionary { [Dests] = built.RootReference! };

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfNameTree tree = reopened.Names!.Dests!;

        byte[][] expected = [.. keys.Order(ByteOrder.Instance)];
        KeyValuePair<CosString, CosObject>[] entries = [.. tree];
        Assert.Equal(expected.Select(Convert.ToHexString), entries.Select(entry => Convert.ToHexString(entry.Key.Bytes)));
        for (int index = 0; index < keys.Length; index++)
        {
            Assert.True(tree.TryGetValue(keys[index], out CosObject? value));
            Assert.Equal(index, Top(value));
        }

        Assert.Equal(depth, TreeShape.AssertWellFormed(reopened, tree.Root, Names, NameKey).Depth);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 1)]
    [InlineData(100_000, 2)]
    public void A_number_tree_built_from_unsorted_keys_reads_back_in_ascending_order_after_a_save(int count, int depth)
    {
        using PdfDocument document = PdfDocument.Create();

        // The multiples of 7 below 7 x count, scrambled: a step coprime with the count visits every index once.
        int step = count == 1 ? 0 : 3;
        int[] keys = [.. Enumerable.Range(0, count).Select(index => (int)((long)index * step % count) * 7)];
        PdfNumberTree built = document.CreateNumberTree(keys.Select(key => KeyValuePair.Create(key, (CosObject)Label(key))));
        document.Catalog[PageLabels] = built.RootReference!;

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfNumberTree tree = reopened.GetNumberTree(reopened.Catalog[PageLabels])!;

        KeyValuePair<int, CosObject>[] entries = [.. tree];
        Assert.Equal(Enumerable.Range(0, count).Select(index => index * 7), entries.Select(entry => entry.Key));
        Assert.All(entries, entry => Assert.Equal(entry.Key, Start(entry.Value)));
        Assert.True(tree.TryGetFloor((7 * count) - 1, out int floor, out _));
        Assert.Equal(7 * (count - 1), floor);
        Assert.Equal(depth, TreeShape.AssertWellFormed(reopened, tree.Root, Nums, NumberKey).Depth);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void A_tree_is_built_from_each_key_once()
    {
        using PdfDocument document = PdfDocument.Create();
        KeyValuePair<int, CosObject> number = KeyValuePair.Create(1, (CosObject)CosNull.Instance);
        KeyValuePair<CosString, CosObject> name = KeyValuePair.Create(new CosString("a"u8), (CosObject)CosNull.Instance);

        Assert.Throws<ArgumentException>(() => document.CreateNumberTree([number, number]));
        Assert.Throws<ArgumentException>(() => document.CreateNameTree([name, name]));
    }

    [Fact]
    public void An_empty_tree_is_a_root_with_an_empty_names_array()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfNameTree tree = document.CreateNameTree([]);

        Assert.Equal("<< /Names [] >>", tree.Root.ToString());
        Assert.Empty(tree);
    }

    [Fact]
    public void Editing_name_tree_deep_pdf_keeps_every_limits_entry_correct()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"));
        CosReference page = PageReference(document);
        PdfNameTree tree = document.Names!.Dests!;
        var expected = new SortedDictionary<byte[], int>(tree.ToDictionary(entry => entry.Key.Bytes.ToArray(), entry => Top(entry.Value)), ByteOrder.Instance);

        // Before every key, between keys of one leaf and across leaves, past the ASCII keys, and after every key.
        byte[][] inserted =
        [
            [0x01], [0x17],
            .. Enumerable.Range(0, 300).Select(number => Encoding.ASCII.GetBytes(number.ToString("'m'000", CultureInfo.InvariantCulture))),
            .. Enumerable.Range(1, 193).Where(number => number % 4 == 0).Select(number => Encoding.ASCII.GetBytes(number.ToString("'n'000'x'", CultureInfo.InvariantCulture))),
            "zz"u8.ToArray(), [0xFF],
        ];
        int next = 1000;
        foreach (byte[] key in inserted)
        {
            tree.Set(new CosString(key), Destination(page, next));
            expected[key] = next++;
        }

        // Replace some values; remove the even-numbered n keys and both ends.
        foreach (byte[] key in new[] { "a"u8.ToArray(), "m150"u8.ToArray(), "n100"u8.ToArray() })
        {
            tree.Set(new CosString(key), Destination(page, next));
            expected[key] = next++;
        }

        byte[][] removed = [.. expected.Keys.Where(key => key.Length == 4 && key[0] == (byte)'n' && key[3] % 2 == 0), [0x01], [0xFF]];
        foreach (byte[] key in removed)
        {
            Assert.True(tree.Remove(new CosString(key)));
            expected.Remove(key);
        }

        Assert.False(tree.Remove(new CosString("absent"u8)));
        AssertTreeHolds(tree, expected);
        TreeShape.AssertWellFormed(document, tree.Root, Names, NameKey);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfNameTree saved = reopened.Names!.Dests!;
        AssertTreeHolds(saved, expected);
        TreeShape.AssertWellFormed(reopened, saved.Root, Names, NameKey);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPassesOrComparesKeysAsText(file);
    }

    [Fact]
    public void Editing_number_tree_deep_pdf_keeps_every_limits_entry_correct()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        PdfNumberTree tree = document.GetNumberTree(document.Catalog[PageLabels])!;
        Dictionary<int, string> expected = tree.ToDictionary(entry => entry.Key, entry => entry.Value.ToString());

        // Fill the gaps, then grow past the end far enough to split leaves, intermediate nodes and the root.
        foreach (int key in new[] { 3, 6, 9, -5 }.Concat(Enumerable.Range(12, 5000)))
        {
            tree.Set(key, Label(key));
            expected[key] = Label(key).ToString();
        }

        tree.Set(7, Label(70));
        expected[7] = Label(70).ToString();
        foreach (int key in new[] { -5, 0, 4, 11 }.Concat(Enumerable.Range(100, 3000)))
        {
            Assert.True(tree.Remove(key));
            expected.Remove(key);
        }

        Assert.False(tree.Remove(3000));
        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfNumberTree saved = reopened.GetNumberTree(reopened.Catalog[PageLabels])!;
        Assert.Equal(expected.OrderBy(entry => entry.Key), saved.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.ToString())));
        Assert.All(expected.Keys, key => Assert.True(saved.ContainsKey(key)));
        TreeShape.AssertWellFormed(reopened, saved.Root, Nums, NumberKey);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Inserting_a_key_inside_a_leaf_changes_only_that_leaf()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"));
        PdfNameTree tree = document.Names!.Dests!;
        List<CosDictionary> nodes = Nodes(document, tree.Root);
        CosDictionary leaf = nodes.First(node => node.TryGetValue(Names, out CosObject? pairs) && ((CosArray)document.Resolve(pairs)).Count >= 4);
        var pairs = (CosArray)document.Resolve(leaf[Names]);
        int count = pairs.Count;

        // The first key followed by a zero byte sorts between the leaf's first and second keys.
        tree.Set(new CosString([.. ((CosString)pairs[0]).Bytes, 0]), CosNull.Instance);

        Assert.Equal([leaf], nodes.Where(node => node.IsDirty));
        Assert.Equal(count + 2, pairs.Count);
    }

    [Fact]
    public void Removing_the_last_key_of_a_leaf_removes_the_leaf()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-deep.pdf"));
        PdfNameTree tree = document.Names!.Dests!;
        int before = tree.Count();
        (CosDictionary parent, CosReference leafReference) = Nodes(document, tree.Root)
            .Where(node => node.ContainsKey(Kids))
            .SelectMany(node => ((CosArray)document.Resolve(node[Kids])).Select(kid => (Parent: node, Kid: (CosReference)kid)))
            .First(pair => ((CosDictionary)document.Resolve(pair.Kid)).ContainsKey(Names));
        var parentKids = (CosArray)document.Resolve(parent[Kids]);
        int kidCount = parentKids.Count;
        var leafPairs = (CosArray)document.Resolve(((CosDictionary)document.Resolve(leafReference))[Names]);
        CosString[] leafKeys = [.. leafPairs.Where((_, index) => index % 2 == 0).Cast<CosString>()];

        foreach (CosString key in leafKeys)
        {
            Assert.True(tree.Remove(key));
        }

        Assert.DoesNotContain(leafReference, parentKids);
        Assert.Equal(kidCount - 1, parentKids.Count);
        Assert.Equal(before - leafKeys.Length, tree.Count());
        Assert.All(leafKeys, key => Assert.False(tree.ContainsKey(key)));
        TreeShape.AssertWellFormed(document, tree.Root, Names, NameKey);
    }

    [Fact]
    public void Removing_every_key_leaves_an_empty_root_that_saves_and_reads_back()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        PdfNumberTree tree = document.GetNumberTree(document.Catalog[PageLabels])!;

        foreach (int key in tree.Select(entry => entry.Key).ToArray())
        {
            Assert.True(tree.Remove(key));
        }

        Assert.Equal("<< /Nums [] >>", tree.Root.ToString());
        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Empty(reopened.GetNumberTree(reopened.Catalog[PageLabels])!);
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void Editing_a_damaged_tree_rebuilds_it_balanced_from_what_the_reader_reads()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("name-tree-broken.pdf"));
        PdfNameTree tree = document.Names!.Dests!;
        CosReference page = PageReference(document);
        Dictionary<string, string> expected = tree.ToDictionary(entry => Encoding.ASCII.GetString(entry.Key.Bytes), entry => entry.Value.ToString());

        tree.Set(new CosString("j"u8), Destination(page, 10));
        expected["j"] = Destination(page, 10).ToString();

        TreeShape.AssertWellFormed(document, tree.Root, Names, NameKey);
        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        PdfNameTree saved = reopened.Names!.Dests!;
        Assert.Equal(
            expected.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            saved.Select(entry => KeyValuePair.Create(Encoding.ASCII.GetString(entry.Key.Bytes), entry.Value.ToString())));
        Assert.Empty(reopened.Diagnostics);
        AssertQpdfPasses(file);
    }

    [Fact]
    public void A_root_holding_its_pairs_splits_into_two_leaves_when_it_grows_past_64_keys()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfNumberTree tree = document.CreateNumberTree(Enumerable.Range(0, 64).Select(key => KeyValuePair.Create(key, (CosObject)Label(key))));
        Assert.Equal((0, 1), TreeShape.AssertWellFormed(document, tree.Root, Nums, NumberKey));

        tree.Set(64, Label(64));

        Assert.Equal((1, 3), TreeShape.AssertWellFormed(document, tree.Root, Nums, NumberKey));
        Assert.Equal(Enumerable.Range(0, 65), tree.Select(entry => entry.Key));
        Assert.True(tree.TryGetValue(64, out CosObject? value));
        Assert.Equal(64, Start(value));
    }

    private static void AssertTreeHolds(PdfNameTree tree, SortedDictionary<byte[], int> expected)
    {
        Assert.Equal(expected.Keys.Select(Convert.ToHexString), tree.Select(entry => Convert.ToHexString(entry.Key.Bytes)));
        Assert.Equal(expected.Values, tree.Select(entry => Top(entry.Value)));
        foreach ((byte[] key, int top) in expected)
        {
            Assert.True(tree.TryGetValue(key, out CosObject? value));
            Assert.Equal(top, Top(value));
        }
    }

    /// <summary>Every node of a tree, root first, read through the public COS objects.</summary>
    private static List<CosDictionary> Nodes(PdfDocument document, CosDictionary root)
    {
        var nodes = new List<CosDictionary> { root };
        for (int index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].TryGetValue(Kids, out CosObject? kids))
            {
                nodes.AddRange(((CosArray)document.Resolve(kids)).Select(kid => (CosDictionary)document.Resolve(kid)));
            }
        }

        return nodes;
    }

    private static CosReference PageReference(PdfDocument document) => Assert.IsType<CosReference>(document.Pages[0].Reference);

    private static CosArray Destination(CosReference page, int top) => [page, new CosName("XYZ"), new CosInteger(0), new CosInteger(top), CosNull.Instance];

    private static CosDictionary Label(int start) => new() { [new CosName("S")] = new CosName("D"), [St] = new CosInteger(start + 1) };

    private static int Start(CosObject value) => (int)Assert.IsType<CosInteger>(Assert.IsType<CosDictionary>(value)[St]).Value - 1;

    private static IComparable NumberKey(CosObject key) => Assert.IsType<CosInteger>(key).Value;

    private static int Top(CosObject value) => (int)Assert.IsType<CosInteger>(Assert.IsType<CosArray>(value)[3]).Value;

    private static IComparable NameKey(CosObject key) => Convert.ToHexString(Assert.IsType<CosString>(key).Bytes);

    /// <summary>Distinct printable ASCII keys (where qpdf's text order and the byte order agree), shuffled.</summary>
#pragma warning disable CA5394 // Reproducible test data, not security.
    private static byte[][] AsciiKeys(int count, int seed)
    {
        var random = new Random(seed);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (keys.Count < count)
        {
            int length = random.Next(1, 7);
            var key = new StringBuilder(length);
            for (int index = 0; index < length; index++)
            {
                // Letters and digits only: no parentheses or backslashes, so the bytes are what the reader sees.
                key.Append("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"[random.Next(62)]);
            }

            keys.Add(key.ToString());
        }

        return [.. keys.Select(Encoding.ASCII.GetBytes)];
    }
#pragma warning restore CA5394

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    private static void AssertQpdfPassesOrComparesKeysAsText(byte[] file)
    {
        // qpdf 12 compares name-tree keys as decoded text, not bytes as ISO 32000-2 §7.9.6 requires, and warns (exit code 3) on
        // name-tree-deep.pdf's PDFDocEncoding and UTF-16BE keys; qpdf 11 compares bytes. Any other warning fails.
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode is 0 or 3, output);
        Assert.All(
            output.Split('\n').Where(line => line.StartsWith("WARNING", StringComparison.Ordinal)),
            line => Assert.Contains("keys are not sorted", line, StringComparison.Ordinal));
    }

    /// <summary>Orders byte strings as ISO 32000-2 Annex J.3.3 does: byte by byte, a prefix first.</summary>
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y)
        {
            int length = Math.Min(x!.Length, y!.Length);
            for (int index = 0; index < length; index++)
            {
                if (x[index] != y[index])
                {
                    return x[index] < y[index] ? -1 : 1;
                }
            }

            return x.Length.CompareTo(y.Length);
        }
    }
}
