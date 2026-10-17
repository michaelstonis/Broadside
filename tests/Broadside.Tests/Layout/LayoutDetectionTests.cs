using Broadside.Layout;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Layout;

/// <summary>The layout tree of untagged pages, detected from geometry: blocks, columns, paragraphs, reading order and running artifacts.</summary>
public sealed class LayoutDetectionTests
{
    [Fact]
    public void An_untagged_two_column_page_yields_the_tree_of_its_tagged_twin()
    {
        using PdfDocument tagged = PdfDocument.Open(Corpus.Path("layout-two-column-tagged.pdf"));
        using PdfDocument untagged = PdfDocument.Open(Corpus.Path("layout-two-column-untagged.pdf"));

        LayoutTree expected = tagged.ExtractLayout();
        LayoutTree detected = untagged.ExtractLayout();

        Assert.Equal(Shape(expected.Root), Shape(detected.Root));
        Assert.Equal(4, detected.Root.Children.Count);
        Assert.False(expected.Pages[0].IsDetected);
        Assert.True(detected.Pages[0].IsDetected);
    }

    [Fact]
    public void A_three_column_page_reads_its_full_width_title_first_then_the_columns_left_to_right()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-three-column.pdf"));

        LayoutTree tree = document.ExtractLayout();

        string[] firstPage = [.. tree.Root.Children.Where(static node => node.Extents[0].Page.PageIndex == 0).Select(static node => node.Text)];
        Assert.Equal(
            [
                "Three Columns Under One Title",
                "Column one opens page 1 with\na first paragraph of three\nshort lines.",
                "Its second paragraph follows\nright after the first one\nand closes the column.",
                "Column two opens page 1 with\na first paragraph of three\nshort lines.",
                "Its second paragraph follows\nright after the first one\nand closes the column.",
                "Column three opens page 1 with\na first paragraph of three\nshort lines.",
                "Its second paragraph follows\nright after the first one\nand closes the column.",
            ],
            firstPage);
        // The 18-point title over 9-point columns is a heading (#188); the rest are paragraphs.
        Assert.Equal(1, Assert.IsType<LayoutHeading>(tree.Root.Children[0]).Level);
        Assert.All(tree.Root.Children.Skip(1), static node => Assert.Equal(LayoutNodeKind.Paragraph, node.Kind));
        Assert.Equal(19, tree.Root.Children.Count);
    }

    [Fact]
    public void Running_headers_and_footers_repeated_on_every_page_are_marked_as_artifacts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-three-column.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.DoesNotContain(tree.Root.Children, static node => node.Text.Contains("Broadside Quarterly", StringComparison.Ordinal)
            || node.Text.Contains("Issue 7", StringComparison.Ordinal) || node.Text.StartsWith("Page ", StringComparison.Ordinal));
        foreach (LayoutPage page in tree.Pages)
        {
            string artifacts = string.Concat(page.Text.Glyphs
                .Where(static glyph => (glyph.Flags & TextGlyphFlags.Artifact) != 0)
                .Select(glyph => page.Text.GetGlyphText(glyph).ToString()));
            Assert.Equal($"BroadsideQuarterlyIssue7Page{page.PageIndex + 1}", artifacts.Replace(" ", string.Empty, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void One_page_alone_has_no_running_artifacts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-three-column.pdf"));

        LayoutTree tree = document.Pages[1].ExtractLayout();

        Assert.Equal("Broadside Quarterly", tree.Root.Children[0].Text);
        Assert.Equal("Issue 7", tree.Root.Children[1].Text);
        Assert.Equal("Page 2", tree.Root.Children[^1].Text);
    }

    [Fact]
    public void Detection_can_be_turned_off_or_run_over_tagged_pages()
    {
        using PdfDocument untagged = PdfDocument.Open(Corpus.Path("layout-two-column-untagged.pdf"));
        using PdfDocument tagged = PdfDocument.Open(Corpus.Path("layout-two-column-tagged.pdf"));

        LayoutTree none = untagged.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.None));
        LayoutTree all = tagged.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.AllPages));

        Assert.Empty(none.Root.Children);
        Assert.False(none.Pages[0].IsDetected);
        Assert.True(all.Pages[0].IsDetected);
        Assert.Equal(Shape(untagged.ExtractLayout().Root), Shape(all.Root));
        Assert.All(all.Root.Children, static node => Assert.Null(node.StructureElement));
    }

    [Fact]
    public void Detection_is_deterministic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-three-column.pdf"));

        Assert.Equal(Shape(document.ExtractLayout().Root), Shape(document.ExtractLayout().Root));
    }

    /// <summary>The node kinds and texts of a subtree, depth first.</summary>
    private static string Shape(LayoutNode node) =>
        $"{node.Kind}[{node.Text.Replace('\n', '|')}]({string.Join(", ", node.Children.Select(Shape))})";
}
