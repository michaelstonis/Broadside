using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Layout;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Layout;

/// <summary>
/// Auto-tagging (issue #191): an untagged document is tagged from its detected layout tree, and the tree read back from the written
/// structure equals the detected one, its text reads in the detected order, and the pages render as they did.
/// </summary>
public sealed class AutoTaggingTests
{
    public static TheoryData<string> LayoutFiles =>
        ["layout-two-column-untagged.pdf", "layout-three-column.pdf", "layout-headings-lists.pdf", "layout-tables.pdf", "layout-figures.pdf"];

    [Theory]
    [MemberData(nameof(LayoutFiles))]
    public void An_auto_tagged_file_reads_back_the_detected_layout_tree_and_text_order(string name)
    {
        byte[] original = File.ReadAllBytes(Corpus.Path(name));
        using PdfDocument document = PdfDocument.Open(original);
        LayoutTree detected = document.ExtractLayout();

        AutoTaggingResult result = document.AutoTag();
        byte[] tagged = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(result.IsTagged);
        Assert.Equal(original, tagged.AsSpan(0, original.Length).ToArray());
        using PdfDocument reread = PdfDocument.Open(tagged);
        Assert.NotNull(reread.StructureTree);
        Assert.True(reread.MarkInfo.Marked);
        LayoutTree read = reread.ExtractLayout();
        Assert.All(read.Pages, static page => Assert.False(page.IsDetected));
        Assert.Equal(Shape(detected.Root), Shape(read.Root));
        for (int page = 0; page < detected.Pages.Count; page++)
        {
            Assert.Equal(ContentText(detected.Pages[page].Text), ContentText(reread.Pages[page].ExtractText()));
        }

        AssertQpdfPasses(tagged);
    }

    [Theory]
    [MemberData(nameof(LayoutFiles))]
    public void Poppler_renders_every_page_identically_before_and_after_tagging(string name)
    {
        byte[] original = File.ReadAllBytes(Corpus.Path(name));
        using PdfDocument document = PdfDocument.Open(original);

        document.AutoTag();
        byte[] tagged = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        for (int page = 1; page <= document.Pages.Count; page++)
        {
            Assert.Equal(Render(original, page), Render(tagged, page));
        }
    }

    [Fact]
    public void A_show_holding_two_columns_is_split_between_their_paragraphs()
    {
        string gap = new(' ', 20);
        byte[] original = TextSamples.Page(Encoding.ASCII.GetBytes(
            "BT /F1 10 Tf 12 TL 72 700 Td\n"
            + "[(Columns are found by) -20000 (and each column is)] TJ\n"
            + $"(the white space that{gap}read from the top in) '\n"
            + $"0 0 (runs down the middle{gap}turn after the first.) \"\n"
            + "ET"));
        using PdfDocument document = PdfDocument.Open(original);
        LayoutTree detected = document.ExtractLayout();

        document.AutoTag();
        byte[] tagged = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.Equal(2, detected.Root.Children.Count);
        Assert.StartsWith("Columns are found by", detected.Root.Children[0].Text, StringComparison.Ordinal);
        using PdfDocument reread = PdfDocument.Open(tagged);
        Assert.Equal(Shape(detected.Root), Shape(reread.ExtractLayout().Root));
        Assert.Equal(ContentText(detected.Pages[0].Text), ContentText(reread.Pages[0].ExtractText()));
        Assert.Equal(Render(original, 1), Render(tagged, 1));
        AssertQpdfPasses(tagged);
    }

    [Fact]
    public void Running_headers_and_footers_become_pagination_artifacts()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-three-column.pdf"));

        document.AutoTag();
        using PdfDocument reread = PdfDocument.Open(Save(document));

        foreach (PdfPage page in reread.Pages)
        {
            var recorder = new ArtifactRecorder();
            page.ProcessContent(recorder);
            Assert.Contains("Pagination/Header", recorder.Artifacts);
            Assert.Contains("Pagination/Footer", recorder.Artifacts);
            TextPage text = page.ExtractText(new TextExtractionOptions { IncludeArtifacts = true });
            string artifacts = string.Concat(text.Glyphs.Where(static glyph => (glyph.Flags & TextGlyphFlags.Artifact) != 0).Select(glyph => text.GetGlyphText(glyph).ToString()));
            Assert.Equal($"BroadsideQuarterlyIssue7Page{reread.Pages.IndexOf(page) + 1}", artifacts.Replace(" ", string.Empty, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Figures_get_their_box_a_placeholder_description_and_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"));
        LayoutFigure[] detected = [.. Nodes(document.ExtractLayout().Root).OfType<LayoutFigure>()];

        AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithFigureAlternateText("Describe me"));

        Assert.NotEmpty(detected);
        Assert.Equal(detected.Length, result.Figures.Count);
        Assert.All(result.Figures, static figure => Assert.Equal("Describe me", figure.AlternateDescription));
        Assert.Equal(detected.Length, result.Diagnostics.Count(static diagnostic => diagnostic.Code == "FigureAlternateTextPlaceholder"));
        Assert.Equal(result.Figures.Select(static figure => figure.Reference), result.Diagnostics.Select(static diagnostic => diagnostic.ObjectReference));
        using PdfDocument reread = PdfDocument.Open(Save(document));
        LayoutFigure[] read = [.. Nodes(reread.ExtractLayout().Root).OfType<LayoutFigure>()];
        Assert.Equal(detected.Select(static figure => figure.BoundingBox), read.Select(static figure => figure.BoundingBox));
        Assert.All(read, static figure => Assert.Equal("Figure", figure.StructureElement!.StructureType!.Value));
    }

    [Fact]
    public void Table_cells_carry_their_spans_and_header_scope()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));
        LayoutTableCell[] detected = [.. Nodes(document.ExtractLayout().Root).OfType<LayoutTableCell>()];

        document.AutoTag();
        using PdfDocument reread = PdfDocument.Open(Save(document));

        LayoutTableCell[] cells = [.. Nodes(reread.ExtractLayout().Root).OfType<LayoutTableCell>()];
        Assert.Equal(detected.Length, cells.Length);
        Assert.Contains(detected, static cell => cell.RowSpan > 1);
        Assert.Contains(detected, static cell => cell.ColumnSpan > 1);
        var table = new CosName("Table");
        foreach ((LayoutTableCell expected, LayoutTableCell cell) in detected.Zip(cells))
        {
            PdfStructureElement element = cell.StructureElement!;
            Assert.Equal(expected.IsHeader ? "TH" : "TD", element.StructureType!.Value);
            Assert.Equal(expected.RowSpan, (element.GetAttributeValue(table, new CosName("RowSpan")) as CosInteger)?.Value ?? 1);
            Assert.Equal(expected.ColumnSpan, (element.GetAttributeValue(table, new CosName("ColSpan")) as CosInteger)?.Value ?? 1);
            Assert.Equal(expected.IsHeader, element.GetAttributeValue(table, new CosName("Scope")) is CosName);
        }
    }

    [Fact]
    public void A_tagged_document_is_left_alone_unless_forced()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-two-column-tagged.pdf"));
        PdfStructureElement before = document.StructureTree!.Children[0];
        LayoutTree detected = document.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.AllPages));

        AutoTaggingResult left = document.AutoTag();

        Assert.False(left.IsTagged);
        Assert.Null(left.Layout);
        Assert.Same(before.Dictionary, document.StructureTree!.Children[0].Dictionary);

        AutoTaggingResult forced = document.AutoTag(new AutoTaggingOptions().WithForce().WithLanguage("en-US"));
        byte[] tagged = Save(document);

        Assert.True(forced.IsTagged);
        using PdfDocument reread = PdfDocument.Open(tagged);
        Assert.Equal("en-US", reread.Language);
        Assert.Equal(Shape(detected.Root), Shape(reread.ExtractLayout().Root));
        Assert.Equal(ContentText(detected.Pages[0].Text), ContentText(reread.Pages[0].ExtractText()));
        AssertQpdfPasses(tagged);
    }

    [Theory]
    [Trait("Category", "Corpus")]
    [InlineData("pdfjs/tracemonkey.pdf")]
    [InlineData("pdfium-tests/third_party/tcpdf/example_011.pdf")]
    [InlineData("pdfium-tests/third_party/tcpdf/example_048.pdf")]
    public void Real_world_untagged_files_tag_without_changing_their_rendering(string path)
    {
        if (RealWorldCorpus.Directory(path.Split('/')[0]) is null)
        {
            Assert.Skip($"Real-world corpora not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        byte[] original = File.ReadAllBytes(RealWorldCorpus.PathOf(path));
        using PdfDocument document = PdfDocument.Open(original);
        LayoutTree detected = document.ExtractLayout();

        Assert.True(document.AutoTag().IsTagged);
        byte[] tagged = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        // A form XObject is tagged as a whole, so the order inside one (an included figure, a header template) is its content
        // order: every glyph is still in the structure or an artifact exactly once.
        AssertQpdfPasses(tagged);
        using PdfDocument reread = PdfDocument.Open(tagged);
        Assert.All(reread.ExtractLayout().Pages, static page => Assert.False(page.IsDetected));
        for (int page = 0; page < detected.Pages.Count; page++)
        {
            Assert.Equal(Sorted(ContentText(detected.Pages[page].Text)), Sorted(ContentText(reread.Pages[page].ExtractText())));
            Assert.Equal(Render(original, page + 1), Render(tagged, page + 1));
        }
    }

    private static string Sorted(string text)
    {
        char[] characters = text.ToCharArray();
        Array.Sort(characters);
        return new string(characters);
    }

    private static IEnumerable<LayoutNode> Nodes(LayoutNode node) => node.Children.SelectMany(Nodes).Prepend(node);

    private static byte[] Render(byte[] file, int page)
    {
        string number = page.ToString(CultureInfo.InvariantCulture);
        (int exitCode, byte[] image, string error) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-f", number, "-l", number);
        Assert.True(exitCode == 0, error);
        return image;
    }

    /// <summary>Records each page-level artifact sequence as <c>Type/Subtype</c>.</summary>
    private sealed class ArtifactRecorder : ContentProcessor
    {
        public List<string> Artifacts { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            if (mc.Tag.SequenceEqual("Artifact"u8) && mc.InlineProperties.Kind == ContentOperandKind.Dictionary && mc.InlineProperties.ToCosObject() is CosDictionary properties)
            {
                Artifacts.Add($"{(properties.TryGetValue(new CosName("Type"), out CosObject? type) ? (type as CosName)?.Value : null)}/{(properties.TryGetValue(new CosName("Subtype"), out CosObject? subtype) ? (subtype as CosName)?.Value : null)}");
            }
        }
    }

    /// <summary>The node kinds, heading levels, table geometry and texts of a subtree, depth first; no element names or extents.</summary>
    private static string Shape(LayoutNode node)
    {
        var output = new StringBuilder();
        output.Append(node.Kind);
        switch (node)
        {
            case LayoutHeading heading:
                output.Append(CultureInfo.InvariantCulture, $" {heading.Level}");
                break;
            case LayoutList list:
                output.Append(' ').Append(list.Numbering);
                break;
            case LayoutTable table:
                output.Append(CultureInfo.InvariantCulture, $" {table.RowCount}x{table.ColumnCount}");
                break;
            case LayoutTableCell cell:
                output.Append(CultureInfo.InvariantCulture, $" ({cell.Row},{cell.Column}) {cell.RowSpan}x{cell.ColumnSpan}{(cell.IsHeader ? " header" : string.Empty)}");
                output.AppendJoin(' ', cell.Headers.Select(static header => $"h({header.Row},{header.Column})"));
                break;
            case LayoutFigure figure when figure.BoundingBox is { } box:
                output.Append(CultureInfo.InvariantCulture, $" [{box.Left:0.#} {box.Bottom:0.#} {box.Right:0.#} {box.Top:0.#}]");
                break;
        }

        output.Append('[').Append(node.Text.Replace('\n', '|')).Append("](");
        output.AppendJoin(", ", node.Children.Select(Shape));
        return output.Append(')').ToString();
    }

    /// <summary>The text of a page's glyphs that are not artifacts, in their order.</summary>
    private static string ContentText(TextPage page)
    {
        var output = new StringBuilder();
        foreach (TextGlyph glyph in page.Glyphs)
        {
            if ((glyph.Flags & TextGlyphFlags.Artifact) == 0)
            {
                output.Append(page.GetGlyphText(glyph));
            }
        }

        return output.ToString();
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var stream = new MemoryStream();
        document.Save(stream, options ?? new PdfSaveOptions());
        return stream.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }
}
