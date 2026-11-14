using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Walking the page tree and inheriting page attributes. ISO 32000-2 §7.7.3.1 to §7.7.3.4.</summary>
public sealed class PageTreeTests
{
    [Fact]
    public void Pages_come_in_kids_order_depth_first_across_intermediate_nodes()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 3 /MediaBox [0 0 100 100] >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 3 0 R /Resources << >> /UserUnit 1 >>",
            "<< /Type /Page /Parent 3 0 R /Resources << >> /UserUnit 2 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /UserUnit 3 >>"));

        Assert.Equal([1.0, 2.0, 3.0], document.Pages.Select(static page => page.UserUnit));
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), document.Pages[2].MediaBox);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_nearest_ancestor_wins_and_values_are_inherited_as_is_without_merging()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 2 /Rotate 90 /MediaBox [0 0 100 100] /Resources << /Font << /F1 7 0 R >> >> >>",
            "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 /Rotate 180 /Resources << /XObject << >> >> >>",
            "<< /Type /Page /Parent 3 0 R >>",
            "<< /Type /Page /Parent 3 0 R /Rotate 270 /MediaBox [0 0 50 50] /Resources << >> >>",
            "null",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        PdfPage first = document.Pages[0];
        Assert.Equal(180, first.Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), first.MediaBox);
        CosDictionary resources = Assert.IsType<CosDictionary>(first.Resources);
        Assert.True(resources.ContainsKey(new CosName("XObject")));
        Assert.False(resources.ContainsKey(new CosName("Font")));

        PdfPage second = document.Pages[1];
        Assert.Equal(270, second.Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 50, 50), second.MediaBox);
        Assert.Empty(Assert.IsType<CosDictionary>(second.Resources));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_change_to_an_ancestor_node_shows_through_the_pages_that_inherit_from_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        PdfPage page = document.Pages[1];

        var node = (CosDictionary)document.Resolve(new CosReference(3, 0));
        node[new CosName("MediaBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(595), new CosInteger(842)]);
        node[new CosName("Rotate")] = new CosInteger(-90);

        Assert.Equal(new PdfRectangle(0, 0, 595, 842), page.MediaBox);
        Assert.Equal(270, page.Rotation);
    }

    [Fact]
    public void Page_count_comes_from_the_kids_not_the_count_entry_which_is_checked()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 5 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>"));

        Assert.Single(document.Pages);
        Assert.Equal("PageTreeCountMismatch", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_node_referenced_twice_is_read_once_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 3 0 R 2 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>"));

        Assert.Single(document.Pages);
        Assert.Equal(["PageTreeCycle", "PageTreeCycle"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Nodes_without_a_type_are_pages_when_they_have_no_kids_and_nodes_when_they_do()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Kids [3 0 R] /Count 1 >>",
            "<< /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>"));

        PdfPage page = Assert.Single(document.Pages);
        Assert.Equal(new CosReference(3, 0), page.Reference);
        Assert.Equal(["PageTreeNodeTypeInvalid", "PageTreeNodeTypeInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Theory]
    [InlineData("/Kids null")]
    [InlineData("/Kids 9 0 R")]
    [InlineData("/Kids 4 0 R")]
    public void A_node_without_a_type_whose_kids_entry_is_null_is_a_page_as_if_it_had_no_kids(string kids)
    {
        // §7.3.7: an entry whose value is null is absent; §7.3.10: a reference to an object that does not exist is null. A save that
        // renumbers writes such a reference as null, so the node must read the same before and after (fuzz finding, save target).
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< {kids} /Count 1 /Resources << >> /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "null");
        using PdfDocument document = PdfDocument.Open(file);

        PdfPage page = Assert.Single(document.Pages);
        Assert.Equal(new CosReference(2, 0), page.Reference);
        Assert.Equal(["PageTreeNodeTypeInvalid"], document.Diagnostics.Select(static diagnostic => diagnostic.Code));

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithObjectNumbering(PdfObjectNumbering.Compact));
        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        Assert.Single(saved.Pages);
    }

    [Fact]
    public void A_root_that_is_itself_a_page_is_the_only_page()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Page /Resources << >> /MediaBox [0 0 612 792] >>"));

        Assert.Equal(new CosReference(2, 0), Assert.Single(document.Pages).Reference);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Kids_that_are_direct_dictionaries_are_used_and_kids_that_are_not_dictionaries_are_skipped()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 10 10] >> 9 0 R 3 0 R (text)] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>"));

        Assert.Equal(2, document.Pages.Count);
        Assert.Null(document.Pages[0].Reference);
        Assert.Equal(new PdfRectangle(0, 0, 10, 10), document.Pages[0].MediaBox);
        Assert.Equal(
            ["PageTreeKidNotIndirect", "PageTreeKidInvalid", "PageTreeKidInvalid"],
            document.Diagnostics.Select(static diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_parent_entry_that_disagrees_with_the_tree_is_reported_and_the_tree_wins()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 4 0 R /Resources << >> >>",
            "<< /Type /Pages /Kids [] /Count 0 /MediaBox [0 0 1 1] >>"));

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Equal("PageTreeParentMismatch", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_catalog_without_pages_has_no_pages_and_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build("<< /Type /Catalog >>"));

        Assert.Empty(document.Pages);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("PagesMissing", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_catalog_whose_pages_entry_the_cross_reference_table_misses_is_read_through_a_rebuilt_table()
    {
        // The table lists object 2, the page tree root, as free: the scan finds it.
        byte[] file = OnePageWithEntry(2, "0000000000 65535 f ");

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(new PdfRectangle(0, 0, 200, 300), Assert.Single(document.Pages).MediaBox);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("PagesMissing", DiagnosticSeverity.Warning), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_catalog_without_pages_reads_the_page_tree_and_page_objects_the_file_holds_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog >>",
            "<< /Type /Pages /Kids [4 0 R 3 0 R] /Count 2 /MediaBox [0 0 100 100] >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /UserUnit 2 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /UserUnit 1 >>",
            "<< /Type /Page /Parent 6 0 R /Resources << >> /UserUnit 3 >>",
            "<< /Type /Pages /Parent 7 0 R /Count 0 /MediaBox [0 0 50 50] >>",
            "<< /Type /Pages /Kids [6 0 R] /Count 0 >>"));

        // The tree under 2 in Kids order, then the page its tree does not list, inheriting through its Parent chain.
        Assert.Equal([1.0, 2.0, 3.0], document.Pages.Select(static page => page.UserUnit));
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), document.Pages[0].MediaBox);
        Assert.Equal(new PdfRectangle(0, 0, 50, 50), document.Pages[2].MediaBox);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, static diagnostic => diagnostic.Code == "PagesMissing");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Strict_mode_throws_for_a_catalog_whose_pages_entry_does_not_resolve()
    {
        PdfOptions strict = new PdfOptions().UseStrict();
        byte[] rebuildable = OnePageWithEntry(2, "0000000000 65535 f ");
        byte[] withoutPages = new TestPdf().Build("<< /Type /Catalog >>", "<< /Type /Page /Resources << >> /MediaBox [0 0 1 1] >>");

        Assert.Equal("PagesMissing", Assert.Throws<DiagnosticException>(() => PdfDocument.Open(rebuildable, strict)).Diagnostic.Code);
        Assert.Equal("PagesMissing", Assert.Throws<DiagnosticException>(() => PdfDocument.Open(withoutPages, strict)).Diagnostic.Code);
    }

    /// <summary>
    /// The real-world files whose catalog has no usable Pages entry, with the page count each reads as and the severity of its
    /// PagesMissing diagnostic. The triage against pdf.js, poppler and qpdf is in docs/research/pages-missing-triage.md (issue #258).
    /// </summary>
    public static TheoryData<string, int, DiagnosticSeverity> PagesMissingFiles => new()
    {
        { "pdfjs/PDFBOX-3148-2-fuzzed.pdf", 1, DiagnosticSeverity.Warning },
        { "pdfjs/issue9418.pdf", 1, DiagnosticSeverity.Warning },
        { "pdfjs/poppler-395-0-fuzzed.pdf", 1, DiagnosticSeverity.Warning },
        { "qpdf/append-xref-loop.pdf", 30, DiagnosticSeverity.Warning },
        { "qpdf/i3.pdf", 1, DiagnosticSeverity.Warning },
        { "qpdf/issue-119.pdf", 0, DiagnosticSeverity.Error },
        { "qpdf/issue-120.pdf", 1, DiagnosticSeverity.Warning },
        { "qpdf/issue-143.pdf", 0, DiagnosticSeverity.Error },
        { "qpdf/minimal-linearize-pass1.pdf", 1, DiagnosticSeverity.Warning },
    };

    [Theory]
    [Trait("Category", "Corpus")]
    [MemberData(nameof(PagesMissingFiles))]
    public void A_real_world_file_without_a_usable_page_tree_reads_the_pages_its_triage_justifies(string file, int pages, DiagnosticSeverity severity)
    {
        string corpus = file[..file.IndexOf('/', StringComparison.Ordinal)];
        if (!RealWorldCorpus.Files(corpus).Contains(file))
        {
            Assert.Skip($"{file} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        using PdfDocument document = PdfDocument.Open(RealWorldCorpus.PathOf(file));

        Assert.Equal(pages, document.Pages.Count);
        Assert.Equal(severity, Assert.Single(document.Diagnostics, static diagnostic => diagnostic.Code == "PagesMissing").Severity);
        Assert.Throws<DiagnosticException>(() =>
        {
            using PdfDocument strict = PdfDocument.Open(RealWorldCorpus.PathOf(file), new PdfOptions().UseStrict());
            _ = strict.Pages.Count;
        });
    }

    /// <summary>A one-page file whose cross-reference entry for <paramref name="number"/> is replaced by <paramref name="entry"/>.</summary>
    private static byte[] OnePageWithEntry(int number, string entry)
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 300] >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> >>");
        string text = System.Text.Encoding.Latin1.GetString(file);
        int table = text.IndexOf("xref\n", StringComparison.Ordinal);
        int start = text.IndexOf('\n', table + "xref\n".Length) + 1 + (number * 20);
        return System.Text.Encoding.Latin1.GetBytes(string.Concat(text.AsSpan(0, start), entry, text.AsSpan(start + entry.Length)));
    }

    [Fact]
    public void A_page_without_resources_on_itself_or_an_ancestor_is_reported()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"));

        Assert.Null(Assert.Single(document.Pages).Resources);
        Assert.Equal("PageResourcesMissing", Assert.Single(document.Diagnostics).Code);
    }
}
