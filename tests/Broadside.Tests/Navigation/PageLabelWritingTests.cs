using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Navigation;

/// <summary>Writing page labels (ISO 32000-2 §12.4.2, Table 161) as ranges in the catalog's <c>PageLabels</c> number tree (§7.9.7).</summary>
public sealed class PageLabelWritingTests
{
    private static readonly CosName PageLabels = new("PageLabels");

    [Fact]
    public void Page_labels_written_as_ranges_re_read_as_the_expected_label_strings()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        PdfPageLabels labels = document.CreatePageLabels();

        // Out of order: the tree sorts the keys.
        labels.SetRange(11, PdfPageLabelStyle.None, "Back cover");
        labels.SetRange(9, PdfPageLabelStyle.Arabic, "A-", 8);
        labels.SetRange(0, PdfPageLabelStyle.LowercaseRoman);
        labels.SetRange(4, PdfPageLabelStyle.Arabic);
        labels.SetRange(7, PdfPageLabelStyle.UppercaseLetters, firstNumber: 26);

        byte[] file = Save(document);
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(["i", "ii", "iii", "iv", "1", "2", "3", "Z", "AA", "A-8", "A-9", "Back cover"], reopened.PageLabels!.GetLabels());
        Assert.Equal([0, 4, 7, 9, 11], reopened.PageLabels.Ranges.Select(range => range.StartPageIndex));
        Assert.Equal("<< /S /A /St 26 >>", reopened.PageLabels.Ranges[2].Dictionary.ToString());
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void New_page_labels_number_every_page_in_decimal_until_ranges_are_set()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        CosObject old = document.Catalog[PageLabels];

        PdfPageLabels labels = document.CreatePageLabels();

        Assert.NotEqual(old, document.Catalog[PageLabels]);
        Assert.Equal(Enumerable.Range(1, 12).Select(number => number.ToString(System.Globalization.CultureInfo.InvariantCulture)), labels.GetLabels());
        Assert.Equal("<< /S /D >>", Assert.Single(labels.Ranges).Dictionary.ToString());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Editing_the_ranges_of_page_labels_pdf_in_an_incremental_update_relabels_the_pages()
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("page-labels.pdf"));
        using PdfDocument document = PdfDocument.Open(original);
        PdfPageLabels labels = document.PageLabels!;

        // Before: i ii iii 1 2 IV V Z AA A-zz A-aaa Cover. Pages 5 and 6 join the decimal range, page 6 starts a new one.
        Assert.True(labels.RemoveRange(5));
        Assert.False(labels.RemoveRange(6));
        labels.SetRange(6, PdfPageLabelStyle.UppercaseRoman, "Annex ");
        labels.SetRange(11, PdfPageLabelStyle.None, "Back");

        byte[] file = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, file.AsSpan(0, original.Length).ToArray());
        using PdfDocument reopened = PdfDocument.Open(file);
        Assert.Equal(["i", "ii", "iii", "1", "2", "3", "Annex I", "Z", "AA", "A-zz", "A-aaa", "Back"], reopened.PageLabels!.GetLabels());
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void Range_values_outside_the_clause_are_refused()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        PdfPageLabels labels = document.PageLabels!;

        Assert.Throws<ArgumentOutOfRangeException>(() => labels.SetRange(-1, PdfPageLabelStyle.Arabic));
        Assert.Throws<ArgumentOutOfRangeException>(() => labels.SetRange(0, PdfPageLabelStyle.Arabic, firstNumber: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => labels.SetRange(0, (PdfPageLabelStyle)42));
        Assert.Throws<ArgumentException>(() => labels.SetRange(0, PdfPageLabelStyle.Arabic, "a\0b"));
        Assert.Equal(["i", "ii", "iii", "1", "2", "IV", "V", "Z", "AA", "A-zz", "A-aaa", "Cover"], labels.GetLabels());
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }
}
