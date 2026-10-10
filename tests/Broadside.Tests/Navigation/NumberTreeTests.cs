using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>Number trees (ISO 32000-2 §7.9.7, Table 37) read through <see cref="PdfDocument.GetNumberTree"/>.</summary>
public sealed class NumberTreeTests
{
    private static readonly CosName PageLabels = new("PageLabels");

    [Fact]
    public void A_three_level_page_labels_tree_enumerates_its_keys_in_order_with_no_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));

        PdfNumberTree tree = Labels(document);

        Assert.Equal([0, 1, 2, 4, 5, 7, 8, 10, 11], tree.Select(entry => entry.Key));
        Assert.Equal("<< /S /D /P (B-) /St 1 >>", tree.Last(entry => entry.Key == 10).Value.ToString());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Exact_lookups_find_keys_through_the_limits_and_miss_keys_between_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        PdfNumberTree tree = Labels(document);

        Assert.True(tree.TryGetValue(7, out CosObject? seven));
        Assert.Equal("<< /S /a /P (x) >>", seven.ToString());
        Assert.True(tree.ContainsKey(0));
        Assert.True(tree.ContainsKey(11));
        Assert.False(tree.ContainsKey(3));
        Assert.False(tree.ContainsKey(12));
        Assert.False(tree.ContainsKey(-1));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(6, 5)]
    [InlineData(9, 8)]
    [InlineData(11, 11)]
    [InlineData(1000, 11)]
    public void A_floor_lookup_finds_the_greatest_key_not_greater_than_the_one_asked_for(int key, int expectedFloor)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        PdfNumberTree tree = Labels(document);

        Assert.True(tree.TryGetFloor(key, out int floor, out CosObject? value));

        Assert.Equal(expectedFloor, floor);
        Assert.True(tree.TryGetValue(floor, out CosObject? exact));
        Assert.Same(exact, value);
        Assert.False(tree.TryGetFloor(-1, out _, out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Keys_that_are_not_integers_are_repaired_or_skipped_with_a_diagnostic()
    {
        byte[] file = TestPdf.OnePage(
            pageEntries: string.Empty,
            catalogEntries: "/PageLabels << /Nums [0 (a) 2.0 (b) 1 (c) /x (d) 3000000000 (e) 3 (f)] >>");
        using PdfDocument document = PdfDocument.Open(file);
        PdfNumberTree tree = Labels(document);

        Assert.Equal([0, 2, 1, 3], tree.Select(entry => entry.Key));
        Assert.Equal(["NumberTreeKeyInvalid", "NumberTreeKeysUnsorted"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.True(tree.TryGetValue(1, out CosObject? one));
        Assert.Equal("(c)", one.ToString());
        Assert.True(tree.TryGetFloor(2, out int floor, out _));
        Assert.Equal(2, floor);
    }

    [Fact]
    public void A_cycle_through_the_kids_ends_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /PageLabels 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Kids [5 0 R] >>",
            "<< /Limits [0 9] /Kids [6 0 R 5 0 R] >>",
            "<< /Limits [0 0] /Nums [0 (a)] >>");
        using PdfDocument document = PdfDocument.Open(file);
        PdfNumberTree tree = Labels(document);

        Assert.Equal([0], tree.Select(entry => entry.Key));
        Assert.False(tree.TryGetValue(5, out _));
        Assert.Equal(["NumberTreeCycle 5"], document.Diagnostics.Select(Describe));
    }

    [Fact]
    public void A_chain_deeper_than_the_cap_ends_with_a_diagnostic()
    {
        const int levels = 100;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /PageLabels 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Kids [5 0 R] >>",
        };
        for (int level = 1; level < levels; level++)
        {
            objects.Add(string.Create(CultureInfo.InvariantCulture, $"<< /Limits [0 0] /Kids [{5 + level} 0 R] >>"));
        }

        objects.Add("<< /Limits [0 0] /Nums [0 (deep)] >>");
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build([.. objects]));
        PdfNumberTree tree = Labels(document);

        Assert.Empty(tree);
        Assert.False(tree.TryGetValue(0, out _));
        Diagnostic tooDeep = Assert.Single(document.Diagnostics);
        Assert.Equal("NumberTreeTooDeep", tooDeep.Code);
        Assert.Equal(DiagnosticSeverity.Error, tooDeep.Severity);
    }

    private static PdfNumberTree Labels(PdfDocument document) =>
        Assert.IsType<PdfNumberTree>(document.GetNumberTree(document.Catalog[PageLabels]));

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(CultureInfo.InvariantCulture) ?? "-"}";
}
