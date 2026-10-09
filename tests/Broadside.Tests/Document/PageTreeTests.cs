using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>Walking the page tree and inheriting page attributes. ISO 32000-2 §7.7.3.1 to §7.7.3.4.</summary>
public class PageTreeTests
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
        Assert.Equal("PagesMissing", Assert.Single(document.Diagnostics).Code);
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
