using Broadside.Layout;
using Broadside.TestSupport;

namespace Broadside.Tests.Layout;

/// <summary>
/// The layout tree written as Markdown (issue #629): one test per node kind, tables with spans both ways, escaping and emphasis.
/// ISO 32000-2 §14.8.4 (the structure types), §14.9.3 (alternate descriptions), §14.8.2.2 (artifacts left out); CommonMark 0.31.
/// </summary>
public sealed class MarkdownWriterTests
{
    private static readonly MarkdownSaveOptions Plain = new MarkdownSaveOptions().WithInlineEmphasis(false);

    [Fact]
    public void Headings_take_their_level_clamped_to_six_and_an_unnested_heading_is_level_one()
    {
        Element deep = new("H", new Run("Seventh level"));
        for (int level = 0; level < 7; level++)
        {
            deep = new Element("Sect", deep);
        }

        string markdown = Markdown(new Element("Document", new Element("H", new Run("The title")), new Element("H1", new Run("One")), new Element("H3", new Run("Three")), deep));

        Assert.Equal("# The title\n\n# One\n\n### Three\n\n###### Seventh level\n", markdown);
    }

    [Fact]
    public void Paragraphs_and_sections_are_blocks_separated_by_a_blank_line()
    {
        string markdown = Markdown(new Element(
            "Document",
            new Element("Sect", new Element("P", new Run("First paragraph.")), new Element("P", new Run("Second "), new Run("paragraph."))),
            new Element("P", new Run("Third."))));

        Assert.Equal("First paragraph.\n\nSecond paragraph.\n\nThird.\n", markdown);
    }

    [Fact]
    public void Lists_are_bullets_or_numbers_by_their_numbering_and_nest_under_their_item()
    {
        string markdown = Markdown(new Element(
            "Document",
            new Element(
                "L",
                Item("1.", "First"),
                new Element("LI", new Element("Lbl", new Run("2.")), new Element("LBody", new Run("Second"), new Element(
                    "L",
                    Item("-", "Nested a"),
                    Item("-", "Nested b")) { Entries = "/A << /O /List /ListNumbering /Disc >>" })),
                Item("3.", "Third")) { Entries = "/A << /O /List /ListNumbering /Decimal >>" },
            new Element("L", Item("*", "Bullet")) { Entries = "/A << /O /List /ListNumbering /Unordered >>" }));

        Assert.Equal("1. First\n2. Second\n   - Nested a\n   - Nested b\n3. Third\n\n- Bullet\n", markdown);
    }

    [Fact]
    public void A_list_without_numbering_is_numbered_when_its_labels_are_numbers()
    {
        string numbered = Markdown(new Element("Document", new Element("L", Item("a)", "Lettered"), Item("b)", "Next"))));
        string bulleted = Markdown(new Element("Document", new Element("L", Item("\x95", "Bullet"), Item("\x95", "Next"))));

        Assert.Equal("1. Lettered\n2. Next\n", numbered);
        Assert.Equal("- Bullet\n- Next\n", bulleted);
    }

    [Fact]
    public void A_table_with_a_header_row_is_a_pipe_table()
    {
        string markdown = Markdown(new Element("Document", Table(
            Row(Cell("TH", "Name"), Cell("TH", "Qty")),
            Row(Cell("TD", "Apple"), Cell("TD", "3")),
            Row(Cell("TD", "Pear | Plum"), Cell("TD", "5")))));

        Assert.Equal("| Name | Qty |\n| --- | --- |\n| Apple | 3 |\n| Pear \\| Plum | 5 |\n", markdown);
    }

    [Fact]
    public void A_table_with_spans_is_flattened_with_a_comment_as_a_pipe_table()
    {
        string markdown = Markdown(SpannedTable());

        Assert.Equal(
            "<!-- merged cells: a spanned cell's text is repeated in each cell it covers -->\n"
            + "| Name | Half | Half |\n| --- | --- | --- |\n| Name | Q1 | Q2 |\n| Alpha | 1 | 2 |\n",
            markdown);
    }

    [Fact]
    public void A_table_with_spans_is_an_html_table_in_the_html_style()
    {
        string markdown = Markdown(SpannedTable(), Plain.WithTables(MarkdownTableStyle.Html));

        Assert.Equal(
            "<table>\n<tr><th rowspan=\"2\">Name</th><th colspan=\"2\">Half</th></tr>\n<tr><th>Q1</th><th>Q2</th></tr>\n"
            + "<tr><td>Alpha</td><td>1</td><td>2</td></tr>\n</table>\n",
            markdown);
    }

    [Fact]
    public void A_table_without_a_header_row_takes_its_first_row_or_is_html()
    {
        Element document = new("Document", Table(Row(Cell("TD", "a & b"), Cell("TD", "c")), Row(Cell("TD", "d"), Cell("TD", "e"))));

        Assert.Equal("<!-- no header row: the first row is written as the header -->\n| a & b | c |\n| --- | --- |\n| d | e |\n", Markdown(document));
        Assert.Equal(
            "<table>\n<tr><td>a &amp; b</td><td>c</td></tr>\n<tr><td>d</td><td>e</td></tr>\n</table>\n",
            Markdown(document, Plain.WithTables(MarkdownTableStyle.Html)));
    }

    [Fact]
    public void A_heading_in_a_table_cell_is_bold_text()
    {
        string markdown = Markdown(new Element("Document", Table(
            Row(Cell("TH", "Key"), Cell("TH", "Value")),
            Row(new Element("TD", new Element("H2", new Run("Section"))), Cell("TD", "x")))));

        Assert.Equal("| Key | Value |\n| --- | --- |\n| **Section** | x |\n", markdown);
    }

    [Fact]
    public void A_table_split_over_pages_is_one_table()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tables.pdf"));

        string markdown = document.ExtractLayout().ToMarkdown(Plain);

        string[] lines = markdown.Split('\n');
        int header = Array.IndexOf(lines, "| Item | Qty | Price |");
        Assert.True(header >= 0, markdown);
        Assert.Equal("| --- | --- | --- |", lines[header + 1]);
        string[] rows = [.. lines.Skip(header + 2).TakeWhile(static line => line.StartsWith('|'))];
        Assert.Equal("| Part 01 | 3 | 1.25 |", rows[0]);
        Assert.Contains("| Part 11 | 33 | 13.75 |", rows);
        Assert.Contains("| Part 12 | 36 | 15.00 |", rows);
        Assert.Equal(1, lines.Count(static line => line == "| Item | Qty | Price |"));
    }

    [Fact]
    public void A_figure_is_an_image_reference_or_its_alternate_description_followed_by_its_caption()
    {
        Element document = new(
            "Document",
            new Element("Figure", new Element("Caption", new Run("Figure 1. Sales"))) { Entries = "/Alt (A bar chart [Q1])" },
            new Element("Figure", new Run("label")) { Entries = "/Alt (A pie chart)" });

        Assert.Equal("![A bar chart \\[Q1\\]](page-1-figure-1)\n\nFigure 1. Sales\n\n![A pie chart](page-1-figure-2)\n", Markdown(document));
        Assert.Equal("*A bar chart \\[Q1\\]*\n\nFigure 1. Sales\n\n*A pie chart*\n", Markdown(document, Plain.WithImages(false)));
    }

    [Fact]
    public void A_link_with_a_uri_action_is_a_markdown_link_and_an_internal_link_is_text()
    {
        string markdown = Markdown(new Element(
            "Document",
            new Element("P", new Run("See "), new Element("Link", new Run("the site")) { LinkAction = "<< /S /URI /URI (https://example.com/a b\\(1\\)) >>" }, new Run(" now.")),
            new Element("P", new Run("Go to "), new Element("Link", new Run("page one")) { LinkAction = "<< /S /GoTo /D [5 0 R /Fit] >>" }, new Run("."))));

        Assert.Equal("See [the site](https://example.com/a%20b%281%29) now.\n\nGo to page one.\n", markdown);
    }

    [Fact]
    public void A_note_in_a_paragraph_is_a_footnote_and_one_between_blocks_is_a_definition()
    {
        string markdown = Markdown(new Element(
            "Document",
            new Element("P", new Run("A claim"), new Element("Note", new Run("Source: a survey.")), new Run(" holds.")),
            new Element("Note", new Run("A standalone note."))));

        Assert.Equal("A claim[^1] holds.\n\n[^2]: A standalone note.\n\n[^1]: Source: a survey.\n", markdown);
    }

    [Fact]
    public void A_span_is_inline_text_of_its_paragraph()
    {
        string markdown = Markdown(new Element("Document", new Element("P", new Run("Plain "), new Element("Span", new Run("spanned")), new Run(" text."))));

        Assert.Equal("Plain spanned text.\n", markdown);
    }

    [Fact]
    public void Artifacts_are_not_written()
    {
        byte[] file = TaggedSample.Build(new Element("Document", new Element("P", new Run("Body text."))), artifact: "Running header 7");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("Body text.\n", document.ExtractMarkdown());
    }

    [Fact]
    public void Lines_of_a_block_are_joined_by_a_space_and_a_word_hyphenated_at_a_line_end_is_rejoined()
    {
        string markdown = Markdown(new Element("Document", new Element("P", new Run("It is more difficult to com-\npile, and a well-\nKnown term."))));

        Assert.Equal("It is more difficult to compile, and a well- Known term.\n", markdown);
    }

    [Fact]
    public void Characters_markdown_reads_as_markup_are_escaped()
    {
        string markdown = Markdown(new Element(
            "Document",
            new Element("P", new Run("*stars* _under_ snake_case [link] <tag> `code` ~x~ R&D &amp; a > b \\ end")),
            new Element("P", new Run("1. not a list")),
            new Element("P", new Run("- not a bullet")),
            new Element("P", new Run("# not a heading")),
            new Element("H2", new Run("C# and #"))));

        Assert.Equal(
            "\\*stars\\* \\_under\\_ snake_case \\[link\\] \\<tag> \\`code\\` \\~x\\~ R&D \\&amp; a > b \\\\ end\n\n"
            + "1\\. not a list\n\n\\- not a bullet\n\n\\# not a heading\n\n## C# and \\#\n",
            markdown);
    }

    [Fact]
    public void Emphasis_comes_from_each_words_font_and_is_applied_per_run_of_words()
    {
        string markdown = Markdown(
            new Element(
                "Document",
                new Element("P", new Run("Plain "), new Run("bold words", "F2"), new Run(" then "), new Run("italic", "F3"), new Run(" and "), new Run("both", "F4"), new Run(".")),
                new Element("P", new Run("forced", "F5"), new Run(" "), new Run("weighted", "F6"), new Run(" "), new Run("angled", "F7"), new Run(" "), new Run("flagged", "F8"))),
            new MarkdownSaveOptions());

        Assert.Equal("Plain **bold words** then *italic* and ***both.***\n\n**forced weighted** *angled flagged*\n", markdown);
    }

    [Fact]
    public void Emphasis_can_be_turned_off_and_headings_carry_none()
    {
        Element document = new("Document", new Element("H1", new Run("Bold title", "F2")), new Element("P", new Run("bold", "F2")));

        Assert.Equal("# Bold title\n\n**bold**\n", Markdown(document, new MarkdownSaveOptions()));
        Assert.Equal("# Bold title\n\nbold\n", Markdown(document, Plain));
    }

    [Fact]
    public void Page_breaks_are_comments_before_the_first_block_of_each_page()
    {
        string markdown = Markdown(
            new Element("Document", new Element("P", new Run("On one.")), TaggedSample.PageBreak, new Element("P", new Run("On two."))),
            Plain.WithPageBreaks(true));

        Assert.Equal("<!-- page 1 -->\n\nOn one.\n\n<!-- page 2 -->\n\nOn two.\n", markdown);
    }

    [Fact]
    public void An_empty_tree_writes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        LayoutTree tree = document.ExtractLayout(new LayoutOptions().WithDetection(LayoutDetection.None));

        Assert.Equal(string.Empty, tree.ToMarkdown());
        using var writer = new StringWriter();
        tree.SaveMarkdown(writer);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void The_document_and_page_conveniences_write_their_layout_trees()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));

        Assert.Equal(document.ExtractLayout().ToMarkdown(), document.ExtractMarkdown());
        Assert.Equal(document.Pages[0].ExtractLayout().ToMarkdown(Plain), document.Pages[0].ExtractMarkdown(Plain));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Serializing_records_no_diagnostics_and_is_the_same_from_several_threads()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-headings-lists.pdf"));
        LayoutTree tree = document.ExtractLayout();
        int before = document.Diagnostics.Count;

        string[] results = new string[8];
        Parallel.For(0, results.Length, index => results[index] = tree.ToMarkdown());

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(before, document.Diagnostics.Count);
    }

    [Fact]
    public void Options_are_immutable_copies_and_reject_undefined_table_styles()
    {
        var options = new MarkdownSaveOptions();

        MarkdownSaveOptions changed = options.WithPageBreaks(true).WithImages(false).WithInlineEmphasis(false).WithTables(MarkdownTableStyle.Html);

        Assert.Equal((false, true, true, MarkdownTableStyle.Pipe), (options.IncludePageBreaks, options.IncludeImages, options.InlineEmphasis, options.Tables));
        Assert.Equal((true, false, false, MarkdownTableStyle.Html), (changed.IncludePageBreaks, changed.IncludeImages, changed.InlineEmphasis, changed.Tables));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithTables((MarkdownTableStyle)7));
        Assert.Throws<ArgumentNullException>(() => document().SaveMarkdown(null!));

        static LayoutTree document()
        {
            using PdfDocument pdf = PdfDocument.Open(Corpus.Path("layout-tagged.pdf"));
            return pdf.ExtractLayout();
        }
    }

    private static string Markdown(Element root, MarkdownSaveOptions? options = null)
    {
        using PdfDocument document = PdfDocument.Open(TaggedSample.Build(root));
        LayoutTree tree = document.ExtractLayout();
        Assert.Empty(document.Diagnostics);
        return tree.ToMarkdown(options ?? Plain);
    }

    private static Element Item(string label, string body) => new("LI", new Element("Lbl", new Run(label)), new Element("LBody", new Run(body)));

    private static Element Table(params Element[] rows) => new("Table", rows);

    private static Element Row(params Element[] cells) => new("TR", cells);

    private static Element Cell(string type, string text, string entries = "") => new(type, new Run(text)) { Entries = entries };

    private static Element SpannedTable() => new("Document", Table(
        Row(Cell("TH", "Name", "/A << /O /Table /RowSpan 2 >>"), Cell("TH", "Half", "/A << /O /Table /ColSpan 2 >>")),
        Row(Cell("TH", "Q1"), Cell("TH", "Q2")),
        Row(Cell("TD", "Alpha"), Cell("TD", "1"), Cell("TD", "2"))));
}
