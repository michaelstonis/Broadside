using Broadside.Annotations;
using Broadside.Layout;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Layout;

/// <summary>The layout tree of tagged files, built from the structure tree. ISO 32000-2 §14.8.4, §14.8.5.4.3, §14.8.5.7; ISO/TS 32005.</summary>
public sealed class LayoutTreeTests
{
    [Theory]
    [InlineData("tagged-structure.pdf")]
    [InlineData("layout-tagged.pdf")]
    [InlineData("text-tagged-replacements.pdf")]
    [InlineData("text-tagged-spanning.pdf")]
    public Task A_tagged_file_yields_the_expected_layout_tree(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        LayoutTree tree = document.ExtractLayout();

        Assert.Empty(document.Diagnostics);
        return Verify(LayoutProjection.Of(tree)).UseParameters(fileName);
    }

    [Fact]
    public void Custom_roles_mapped_through_the_role_maps_produce_standard_node_kinds()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        LayoutTree tree = document.ExtractLayout();

        // Chapter is mapped by its namespace's RoleMapNS to Sect (2.0), Para by the root's RoleMap to P (14.8.6.2).
        LayoutNode chapter = Assert.Single(tree.Root.Children);
        Assert.Equal(LayoutNodeKind.Section, chapter.Kind);
        Assert.Equal("Chapter", chapter.StructureElement!.StructureType!.Value);
        Assert.Equal(
            [LayoutNodeKind.Heading, LayoutNodeKind.Paragraph, LayoutNodeKind.List, LayoutNodeKind.Table],
            chapter.Children.Select(static node => node.Kind));
        LayoutNode paragraph = chapter.Children[1];
        Assert.Equal("Para", paragraph.StructureElement!.StructureType!.Value);
        Assert.Equal("A paragraph with a link.", paragraph.Text);
        Assert.Equal(1, Assert.IsType<LayoutHeading>(chapter.Children[0]).Level);
    }

    [Fact]
    public void The_single_document_element_is_the_root_and_its_language_applies()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));

        LayoutTree tree = document.ExtractLayout();

        Assert.Equal(LayoutNodeKind.Document, tree.Root.Kind);
        Assert.Equal("Document", tree.Root.StructureElement!.StructureType!.Value);
        Assert.Null(tree.Root.Parent);
        Assert.Equal("en-US", tree.Root.Language);
        LayoutPage page = Assert.Single(tree.Pages);
        Assert.Same(document.Pages[0], page.Page);
        Assert.Equal(0, page.PageIndex);
    }

    [Fact]
    public void Table_attributes_become_cell_spans_grid_positions_and_header_links()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));

        LayoutTable table = Assert.IsType<LayoutTable>(Single(document.ExtractLayout(), LayoutNodeKind.Table));

        // THead and TBody group rows but are not nodes; the four TR rows are the table's children (Table 371).
        Assert.Equal(4, table.Children.Count);
        Assert.All(table.Children, static row => Assert.Equal(LayoutNodeKind.TableRow, row.Kind));
        Assert.Equal(4, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        LayoutTableCell[] cells = [.. table.Children.SelectMany(static row => row.Children).Cast<LayoutTableCell>()];
        Assert.Equal(["Name", "First half", "Q1", "Q2", "Alpha", "1", "2", "Beta", string.Empty, "4"], cells.Select(static cell => cell.Text));
        Assert.Equal([(0, 0), (0, 1), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2), (3, 0), (3, 1), (3, 2)], cells.Select(static cell => (cell.Row, cell.Column)));
        Assert.Equal([true, true, true, true, false, false, false, false, false, false], cells.Select(static cell => cell.IsHeader));
        Assert.Equal((2, 1), (cells[0].RowSpan, cells[0].ColumnSpan));
        Assert.Equal((1, 2), (cells[1].RowSpan, cells[1].ColumnSpan));
        Assert.Equal((1, 1), (cells[4].RowSpan, cells[4].ColumnSpan));

        // Headers name header cells by ID (Table 384), most specific first as written.
        Assert.Equal([cells[2], cells[1]], cells[5].Headers);
        Assert.Equal([cells[3], cells[1]], cells[6].Headers);
        Assert.Equal([cells[3]], cells[9].Headers);
        Assert.Empty(cells[4].Headers);

        // The empty cell is kept so the grid stays whole; it shows nothing.
        Assert.Empty(cells[8].Extents);
    }

    [Fact]
    public void A_figure_carries_its_alternate_description_caption_and_image_bounds()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));
        LayoutTree tree = document.ExtractLayout();

        LayoutNode[] figures = [.. All(tree.Root).Where(static node => node.Kind == LayoutNodeKind.Figure)];

        Assert.Equal(2, figures.Length);
        LayoutNode figure = figures[0];
        Assert.Equal("A grey square", figure.AlternateDescription);
        LayoutNode caption = Assert.Single(figure.Children);
        Assert.Equal(LayoutNodeKind.Caption, caption.Kind);
        Assert.Equal("Figure 1. A grey square", caption.Text);

        // The image is drawn at 72 580 scaled 100 (§8.9.4: the unit square through the CTM); the figure's extent holds it and its caption.
        PdfRectangle image = Assert.Single(figure.Extents).Bounds!.Value;
        Assert.Equal(72, image.Left, 3);
        Assert.Equal(680, image.Top, 3);
        Assert.True(image.Bottom < 565, "the caption's line lies below the image");

        // The formula shows nothing; its Layout BBox attribute places it (§14.8.5.4.3).
        LayoutNode formula = figures[1];
        Assert.Equal("E = mc2", formula.AlternateDescription);
        Assert.Equal("Formula", formula.StructureElement!.StructureType!.Value);
        LayoutExtent extent = Assert.Single(formula.Extents);
        Assert.Equal(new PdfRectangle(200, 580, 300, 620), extent.Bounds);
        Assert.Equal(0, extent.GlyphCount);
        Assert.Equal(0, extent.TextLength);
    }

    [Fact]
    public void Content_that_shows_no_glyph_is_placed_among_the_glyphs_in_logical_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));
        LayoutTree tree = document.ExtractLayout();
        LayoutNode[] figures = [.. All(tree.Root).Where(static node => node.Kind == LayoutNodeKind.Figure)];
        LayoutNode caption = figures[0].Children[0];

        // The formula comes after the caption and before the table in logical order (14.8.2.5.1).
        LayoutExtent formula = Assert.Single(figures[1].Extents);
        LayoutExtent captionExtent = Assert.Single(caption.Extents);
        Assert.Equal(captionExtent.GlyphStart + captionExtent.GlyphCount, formula.GlyphStart);
        Assert.Equal(captionExtent.TextStart + captionExtent.TextLength, formula.TextStart);
    }

    [Fact]
    public void Heading_levels_come_from_the_type_or_from_section_nesting()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));

        LayoutHeading heading = Assert.IsType<LayoutHeading>(Single(document.ExtractLayout(), LayoutNodeKind.Heading));

        // H inside one Sect is a first-level heading (Table 366: its level is derived from the nesting).
        Assert.Equal(1, heading.Level);
        Assert.Equal("Results", heading.Text);
    }

    [Fact]
    public void Links_notes_and_spans_are_nodes_and_grouping_without_structure_is_not()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));
        LayoutTree tree = document.ExtractLayout();
        LayoutNode section = Assert.Single(tree.Root.Children);

        // NonStruct's descendants are processed normally (Table 365) and the Artifact element is left out (14.8.4.8.7).
        Assert.Equal(
            [LayoutNodeKind.Heading, LayoutNodeKind.Paragraph, LayoutNodeKind.Note, LayoutNodeKind.Figure, LayoutNodeKind.Figure, LayoutNodeKind.Table, LayoutNodeKind.Paragraph],
            section.Children.Select(static node => node.Kind));
        Assert.Equal("Grouped without structure.", section.Children[^1].Text);
        Assert.DoesNotContain(All(tree.Root), static node => node.Text.Contains("Draft", StringComparison.Ordinal));

        LayoutNode paragraph = section.Children[1];
        Assert.Equal("See the report.", paragraph.Text);
        LayoutNode link = Assert.Single(paragraph.Children);
        Assert.Equal(LayoutNodeKind.Link, link.Kind);
        Assert.Equal("report", link.Text);
        PdfLinkAnnotation annotation = Assert.IsType<PdfLinkAnnotation>(Assert.Single(link.Annotations));
        Assert.Same(document.Resolve(((CosArray)document.Pages[0].Dictionary[new CosName("Annots")])[0]), annotation.Dictionary);
        Assert.Equal("1 Fetched in 2026.", section.Children[2].Text);
    }

    [Fact]
    public void Text_ranges_index_the_page_text_in_logical_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        LayoutTree tree = document.ExtractLayout();
        TextPage text = tree.Pages[0].Text;

        // The content paints the table before the heading; the page text and the ranges follow the structure tree.
        Assert.StartsWith("Broadside", text.Text, StringComparison.Ordinal);
        foreach (LayoutNode node in All(tree.Root))
        {
            foreach (LayoutExtent extent in node.Extents)
            {
                string glyphText = string.Concat(Enumerable.Range(extent.GlyphStart, extent.GlyphCount).Select(index => text.GetGlyphText(text.Glyphs[index]).ToString()));
                Assert.Equal(glyphText, text.Text.Substring(extent.TextStart, extent.TextLength));
            }
        }

        LayoutNode table = Single(tree, LayoutNodeKind.Table);
        LayoutExtent range = Assert.Single(table.Extents);
        Assert.Equal("NameValueAlpha1", text.Text.Substring(range.TextStart, range.TextLength));
        Assert.Equal(range.TextStart + range.TextLength, text.Text.Length);
    }

    [Fact]
    public void An_element_spanning_pages_has_an_extent_on_each()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tagged-spanning.pdf"));
        LayoutTree tree = document.ExtractLayout();

        LayoutNode span = Single(tree, LayoutNodeKind.Span);

        Assert.Equal([0, 1], span.Extents.Select(static extent => extent.Page.PageIndex));
        Assert.Equal(2, tree.Pages.Count);
        Assert.Equal([0, 1], tree.Root.Extents.Select(static extent => extent.Page.PageIndex));
    }

    [Fact]
    public void A_page_tree_holds_only_the_nodes_with_content_on_that_page()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tagged-spanning.pdf"));

        LayoutTree tree = document.Pages[1].ExtractLayout();

        LayoutPage page = Assert.Single(tree.Pages);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal([LayoutNodeKind.Span, LayoutNodeKind.Figure, LayoutNodeKind.Paragraph], tree.Root.Children.Select(static node => node.Kind));
        Assert.All(All(tree.Root), static node => Assert.All(node.Extents, static extent => Assert.Equal(1, extent.Page.PageIndex)));
    }

    [Fact]
    public void An_untagged_document_with_detection_off_has_only_the_root_and_its_page_text()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        LayoutTree tree = document.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.None));

        Assert.Single(document.ExtractLayout().Root.Children);
        Assert.Empty(tree.Root.Children);
        Assert.Null(tree.Root.StructureElement);
        Assert.NotEmpty(Assert.Single(tree.Pages).Text.Glyphs);
    }

    [Fact]
    public void An_edit_to_the_structure_tree_is_seen_by_the_next_extraction()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        Assert.Equal(LayoutNodeKind.Paragraph, Single(document.ExtractLayout(), LayoutNodeKind.Paragraph).Kind);
        PdfStructureElement paragraph = document.StructureTree!.FindElement(document.Pages[0], 1)!;

        paragraph.SetStructureType(new PdfStructureType("H2", PdfStructureNamespace.Pdf20));

        LayoutTree tree = document.ExtractLayout();
        Assert.DoesNotContain(All(tree.Root), static node => node.Kind == LayoutNodeKind.Paragraph);
        LayoutHeading[] headings = [.. All(tree.Root).OfType<LayoutHeading>()];
        Assert.Equal([1, 2], headings.Select(static heading => heading.Level));
    }

    [Fact]
    public void Options_reach_the_content_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("tagged-structure.pdf"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = new LayoutOptions().WithHiddenContent().WithContentOptions(new Broadside.Content.ContentOptions { CancellationToken = cancellation.Token });

        Assert.True(options.IncludeHiddenContent);
        Assert.Throws<OperationCanceledException>(() => document.ExtractLayout(options));
    }

    private static IEnumerable<LayoutNode> All(LayoutNode node)
    {
        yield return node;
        foreach (LayoutNode child in node.Children)
        {
            foreach (LayoutNode descendant in All(child))
            {
                yield return descendant;
            }
        }
    }

    private static LayoutNode Single(LayoutTree tree, LayoutNodeKind kind) => Assert.Single(All(tree.Root), node => node.Kind == kind);
}
