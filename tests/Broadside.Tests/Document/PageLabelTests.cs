using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Page labels: ranges in a number tree, numbered in a style with a prefix. ISO 32000-2 §12.4.2, Table 161; §7.9.7.</summary>
public sealed partial class PageLabelTests
{
    private static readonly string[] PageLabelsPdfLabels = ["i", "ii", "iii", "1", "2", "IV", "V", "Z", "AA", "A-zz", "A-aaa", "Cover"];

    [Fact]
    public void Page_labels_pdf_labels_every_page_in_every_style()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        PdfPageLabels labels = document.PageLabels!;

        Assert.Equal(PageLabelsPdfLabels, labels.GetLabels());
        Assert.Equal(PageLabelsPdfLabels, Enumerable.Range(0, 12).Select(labels.GetLabel));
        Assert.Empty(document.Diagnostics);
        Assert.False(document.Catalog.IsDirty);
    }

    [Fact]
    public void Page_labels_pdf_lists_its_ranges_in_page_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-labels.pdf"));
        IReadOnlyList<PdfPageLabelRange> ranges = document.PageLabels!.Ranges;

        Assert.Equal([0, 3, 5, 7, 9, 11], ranges.Select(range => range.StartPageIndex));
        Assert.Equal(
            [PdfPageLabelStyle.LowercaseRoman, PdfPageLabelStyle.Arabic, PdfPageLabelStyle.UppercaseRoman, PdfPageLabelStyle.UppercaseLetters, PdfPageLabelStyle.LowercaseLetters, PdfPageLabelStyle.None],
            ranges.Select(range => range.Style));
        Assert.Equal([1, 1, 4, 26, 52, 1], ranges.Select(range => range.FirstNumber));
        Assert.Equal(["", "", "", "", "A-", "Cover"], ranges.Select(range => range.Prefix));
        Assert.Equal(new CosName("PageLabel"), ranges[2].Dictionary[new CosName("Type")]);
    }

    [Fact]
    public void Number_tree_deep_pdf_finds_each_page_s_range_through_kids_and_limits()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("number-tree-deep.pdf"));
        PdfPageLabels labels = document.PageLabels!;
        PdfNumberTree tree = document.GetNumberTree(document.Catalog[new CosName("PageLabels")])!;

        // Each page's label comes from the range with the greatest key at or below its index (§12.4.2).
        IReadOnlyList<string> expected = [.. Enumerable.Range(0, document.Pages.Count).Select(index =>
        {
            Assert.True(tree.TryGetFloor(index, out int start, out CosObject? value));
            return Expected((CosDictionary)value, index - start);
        })];
        Assert.Equal(expected, labels.GetLabels());
        Assert.Equal(tree.Select(pair => pair.Key), labels.Ranges.Select(range => range.StartPageIndex));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_document_without_page_labels_has_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Null(document.PageLabels);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(2, "B")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(28, "BB")]
    [InlineData(52, "ZZ")]
    [InlineData(53, "AAA")]
    [InlineData(702, "ZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    [InlineData(703, "AAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Letters_repeat_rather_than_carry(int number, string expected)
    {
        using PdfDocument document = OnePageLabelled($"/S /A /St {number}");

        Assert.Equal(expected, document.PageLabels!.GetLabel(0));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(1, "I")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(14, "XIV")]
    [InlineData(40, "XL")]
    [InlineData(90, "XC")]
    [InlineData(400, "CD")]
    [InlineData(1994, "MCMXCIV")]
    [InlineData(3999, "MMMCMXCIX")]
    [InlineData(4000, "MMMM")]
    [InlineData(12345, "MMMMMMMMMMMMCCCXLV")]
    public void Roman_numerals_follow_the_subtractive_form_and_add_an_m_per_thousand_beyond_3999(int number, string expected)
    {
        using PdfDocument document = OnePageLabelled($"/S /R /St {number}");

        Assert.Equal(expected, document.PageLabels!.GetLabel(0));
    }

    [Fact]
    public void Every_roman_numeral_from_1_to_3999_is_canonical_and_reads_back_as_its_number()
    {
        using PdfDocument document = OnePageLabelled("/S /r /St 1");
        CosDictionary range = (CosDictionary)((CosArray)((CosDictionary)document.Catalog[new CosName("PageLabels")])[new CosName("Nums")])[1];

        for (int number = 1; number <= 3999; number++)
        {
            range[new CosName("St")] = new CosInteger(number);
            string label = document.PageLabels!.GetLabel(0);
            Assert.Matches(CanonicalRoman(), label.ToUpperInvariant());
            Assert.Equal(number, ParseRoman(label));
            Assert.Equal(label.ToLowerInvariant(), label);
        }
    }

    [Theory]
    [InlineData("/S /d", "PageLabelStyleInvalid", "1")]
    [InlineData("/S /D /St 0", "PageLabelStartInvalid", "1")]
    [InlineData("/S /D /St -5", "PageLabelStartInvalid", "1")]
    [InlineData("/S /D /St 3.0", "PageLabelStartInvalid", "3")]
    [InlineData("/S /D /St (7)", "PageLabelStartInvalid", "1")]
    [InlineData("/S /D /P 5", "PageLabelPrefixInvalid", "1")]
    [InlineData("/S /D /P (ab\\000cd)", "PageLabelPrefixInvalid", "ab1")]
    [InlineData("/Type /Label /S /D", "PageLabelInvalid", "1")]
    [InlineData("/S /A /St 2147483647", "PageLabelTooLong", "2147483647")]
    [InlineData("/S /R /St 2147483647", "PageLabelTooLong", "2147483647")]
    public void A_malformed_page_label_is_repaired_with_a_warning(string entries, string code, string expected)
    {
        using PdfDocument document = OnePageLabelled(entries);

        Assert.Equal(expected, document.PageLabels!.GetLabel(0));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    // The fuzz harness assumed prefixes under 4 KB and reported an 8 KB one as a finding (issue #48): Table 161 sets no length on P,
    // so the label is the whole prefix followed by the number.
    [Fact]
    public void A_long_prefix_is_kept_whole()
    {
        string prefix = new('x', 8192);
        using PdfDocument document = OnePageLabelled($"/S /D /P ({prefix}) /St 7");

        Assert.Equal(prefix + "7", document.PageLabels!.GetLabel(0));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Pages_before_the_first_range_are_numbered_in_decimal_with_a_warning()
    {
        using PdfDocument document = Labelled(3, "2 << /S /r >>");
        PdfPageLabels labels = document.PageLabels!;

        Assert.Equal(["1", "2", "i"], labels.GetLabels());
        Assert.Equal("PageLabelsMissingZeroKey", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_range_whose_value_is_not_a_dictionary_is_skipped_and_the_previous_range_continues()
    {
        using PdfDocument document = Labelled(4, "0 << /S /D >> 2 /NotALabel");

        Assert.Equal(["1", "2", "3", "4"], document.PageLabels!.GetLabels());
        Assert.Equal(["PageLabelInvalid"], document.Diagnostics.Select(d => d.Code).Distinct());
        Assert.Equal([0], document.PageLabels.Ranges.Select(range => range.StartPageIndex));
    }

    [Fact]
    public void A_negative_key_is_ignored_with_a_warning()
    {
        using PdfDocument document = Labelled(2, "-1 << /S /R >> 1 << /S /a >>");

        Assert.Equal(["1", "a"], document.PageLabels!.GetLabels());
        Assert.Contains("PageLabelInvalid", document.Diagnostics.Select(d => d.Code));
        Assert.Equal([1], document.PageLabels.Ranges.Select(range => range.StartPageIndex));
    }

    [Fact]
    public void A_page_index_outside_the_document_is_refused()
    {
        using PdfDocument document = OnePageLabelled("/S /D");

        Assert.Throws<ArgumentOutOfRangeException>(() => document.PageLabels!.GetLabel(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.PageLabels!.GetLabel(-1));
    }

    [Fact]
    public void Labels_are_read_live_from_the_number_tree()
    {
        using PdfDocument document = OnePageLabelled("/S /D");
        var range = (CosDictionary)((CosArray)((CosDictionary)document.Catalog[new CosName("PageLabels")])[new CosName("Nums")])[1];

        range[new CosName("P")] = new CosString("p-"u8);

        Assert.Equal("p-1", document.PageLabels!.GetLabel(0));
    }

    [Fact]
    public void Strict_mode_throws_from_the_label_that_needs_a_repair()
    {
        byte[] file = LabelledFile(1, "0 << /S /d >>");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        Assert.Equal("PageLabelStyleInvalid", Assert.Throws<DiagnosticException>(() => document.PageLabels!.GetLabel(0)).Diagnostic.Code);
    }

    /// <summary>An independent rendering of Table 161 for the deep-tree check: decimal, roman by table lookup, repeated letters.</summary>
    private static string Expected(CosDictionary range, int offset)
    {
        int number = (range.TryGetValue(new CosName("St"), out CosObject? start) ? (int)((CosInteger)start).Value : 1) + offset;
        string prefix = range.TryGetValue(new CosName("P"), out CosObject? p) ? ((CosString)p).DecodeText() : string.Empty;
        string style = range.TryGetValue(new CosName("S"), out CosObject? s) ? ((CosName)s).Value : string.Empty;
        string[] ones = ["", "i", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix"];
        string[] tens = ["", "x", "xx", "xxx", "xl", "l", "lx", "lxx", "lxxx", "xc"];
        string roman = new string('m', number / 1000) + new[] { "", "c", "cc", "ccc", "cd", "d", "dc", "dcc", "dccc", "cm" }[number / 100 % 10] + tens[number / 10 % 10] + ones[number % 10];
        string letters = new((char)('a' + ((number - 1) % 26)), ((number - 1) / 26) + 1);
        return prefix + style switch
        {
            "D" => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "r" => roman,
            "R" => roman.ToUpperInvariant(),
            "a" => letters,
            "A" => letters.ToUpperInvariant(),
            _ => string.Empty,
        };
    }

    private static int ParseRoman(string text)
    {
        var values = new Dictionary<char, int> { ['i'] = 1, ['v'] = 5, ['x'] = 10, ['l'] = 50, ['c'] = 100, ['d'] = 500, ['m'] = 1000 };
        int total = 0;
        for (int index = 0; index < text.Length; index++)
        {
            int value = values[text[index]];
            total += index + 1 < text.Length && values[text[index + 1]] > value ? -value : value;
        }

        return total;
    }

    [GeneratedRegex("^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$")]
    private static partial Regex CanonicalRoman();

    private static PdfDocument OnePageLabelled(string entries) => PdfDocument.Open(LabelledFile(1, $"0 << {entries} >>"));

    private static PdfDocument Labelled(int pages, string nums) => PdfDocument.Open(LabelledFile(pages, nums));

    private static byte[] LabelledFile(int pages, string nums)
    {
        string kids = string.Join(' ', Enumerable.Range(3, pages).Select(n => $"{n} 0 R"));
        string[] objects =
        [
            $"<< /Type /Catalog /Pages 2 0 R /PageLabels << /Nums [{nums}] >> >>",
            $"<< /Type /Pages /Kids [{kids}] /Count {pages} >>",
            .. Enumerable.Repeat("<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>", pages),
        ];
        return new TestPdf().Build(objects);
    }
}
