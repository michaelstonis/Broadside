using Broadside;
using Broadside.Layout;
using Broadside.Samples;
using Broadside.Text;

Sample.Start("Extracting text");

ExtractPlainText();
ExtractWordsWithPositions();
ChooseTheReadingOrder();
WalkTheLayoutTree();
FindTables();
WriteMarkdownAndJson();
SearchText();
SearchWithARegularExpression();
Sample.Finish();

static void ExtractPlainText()
{
    #region plain-text
    using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
    foreach (PdfPage page in document.Pages)
    {
        TextPage text = page.ExtractText();
        Console.WriteLine(text.PlainText); // words separated by a space, lines by a line feed
    }
    #endregion

    Sample.Expect(document.Pages[0].ExtractText().PlainText == "Hello, Broadside", "the page's text");
}

static void ExtractWordsWithPositions()
{
    #region positions
    using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
    TextPage text = document.Pages[0].ExtractText();
    foreach (TextWord word in text.Words)
    {
        PdfRectangle box = word.Quad.Bounds; // in page space: points, origin at the bottom left
        Console.WriteLine($"'{text.GetWordText(word)}' at ({box.Left:0.#}, {box.Bottom:0.#}), {box.Width:0.#} x {box.Height:0.#}");
    }

    TextGlyph first = text.Glyphs[0];
    Console.WriteLine($"The first glyph is {text.GetGlyphText(first)} in {first.Font.BaseFont} at {first.FontSize} points");
    #endregion

    Sample.Expect(text.Words.Count == 2 && text.GetWordText(text.Words[1]).SequenceEqual("Broadside"), "two words");
}

static void ChooseTheReadingOrder()
{
    #region reading-order
    // A tagged page reads in the order of its structure tree by default; content order is the order the page draws in.
    using PdfDocument document = PdfDocument.Open("layout-two-column-tagged.pdf");
    PdfPage page = document.Pages[0];
    string logical = page.ExtractText().PlainText;
    string drawn = page.ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content)).PlainText;
    Console.WriteLine($"Structure order: {logical.ReplaceLineEndings(" ")[..60]}...");
    Console.WriteLine($"Content order:   {drawn.ReplaceLineEndings(" ")[..60]}...");
    #endregion

    Sample.Expect(logical != drawn, "the two orders differ on a two-column page drawn row by row");
}

static void WalkTheLayoutTree()
{
    #region layout-tree
    // An untagged page has no structure, so the layout tree detects it: columns, paragraphs, headings, lists, tables and figures.
    using PdfDocument document = PdfDocument.Open("layout-two-column-untagged.pdf");
    LayoutTree layout = document.ExtractLayout();
    Print(layout.Root, depth: 0);

    static void Print(LayoutNode node, int depth)
    {
        string text = node.Text.Length > 50 ? node.Text[..50] + "..." : node.Text;
        Console.WriteLine($"{new string(' ', depth * 2)}{node.Kind}: {text.ReplaceLineEndings(" ")}");
        foreach (LayoutNode child in node.Children)
        {
            Print(child, depth + 1);
        }
    }
    #endregion

    int paragraphs = Count(layout.Root, LayoutNodeKind.Paragraph);
    Sample.Expect(paragraphs == 4, $"four paragraphs detected (found {paragraphs})");

    static int Count(LayoutNode node, LayoutNodeKind kind) => (node.Kind == kind ? 1 : 0) + node.Children.Sum(child => Count(child, kind));
}

static void FindTables()
{
    #region tables
    using PdfDocument document = PdfDocument.Open("layout-tables.pdf");
    LayoutTree layout = document.Pages[0].ExtractLayout();
    foreach (LayoutTable table in Descendants(layout.Root).OfType<LayoutTable>())
    {
        Console.WriteLine($"A table of {table.RowCount} rows and {table.ColumnCount} columns");
        foreach (LayoutNode row in table.Children.Where(child => child.Kind == LayoutNodeKind.TableRow))
        {
            Console.WriteLine("  | " + string.Join(" | ", row.Children.Select(cell => cell.Text.ReplaceLineEndings(" "))));
        }
    }

    static IEnumerable<LayoutNode> Descendants(LayoutNode node) => node.Children.SelectMany(child => Descendants(child).Prepend(child));
    #endregion

    Sample.Expect(Descendants(layout.Root).OfType<LayoutTable>().Any(), "a table detected");
}

static void WriteMarkdownAndJson()
{
    #region markdown-json
    // Markdown keeps the headings, lists and tables a language model reads better with; JSON keeps the whole tree for a pipeline.
    using PdfDocument document = PdfDocument.Open("layout-tables.pdf");
    string markdown = document.ExtractMarkdown(new MarkdownSaveOptions().WithPageBreaks(true));
    Console.WriteLine(markdown);

    LayoutTree layout = document.ExtractLayout();
    using var json = new MemoryStream();
    layout.SaveJson(json, new LayoutJsonOptions().WithBounds(true));
    Console.WriteLine($"{json.Length} bytes of layout JSON");
    #endregion

    Sample.Expect(markdown.Contains("| --- |", StringComparison.Ordinal), "a pipe table written");
}

static void SearchText()
{
    #region search
    using PdfDocument document = PdfDocument.Open("text-lines-rotated.pdf");
    foreach (TextMatch match in document.FindText(new TextQuery("degrees second")))
    {
        // A phrase matches across a line break: one quad per line, each along its own baseline.
        Console.WriteLine($"Page {match.PageIndex + 1}: \"{match.Text}\" on {match.Quads.Count} lines, glyphs {match.GlyphStart} to {match.GlyphStart + match.GlyphCount - 1}");
    }
    #endregion

    int found = document.FindText(new TextQuery("degrees second")).Count();
    Sample.Expect(found == 3, $"three matches across line breaks (found {found})");
}

static void SearchWithARegularExpression()
{
    #region search-regex
    using PdfDocument document = PdfDocument.Open("text-lines-rotated.pdf");
    TextQuery query = TextQuery.FromRegex(@"\w+ty").WithWholeWord();
    foreach (TextMatch match in document.Pages[0].FindText(query))
    {
        Console.WriteLine($"{match.Text} at {match.Bounds}");
    }
    #endregion

    string[] words = [.. document.Pages[0].FindText(query).Select(match => match.Text)];
    Sample.Expect(words.SequenceEqual(["Ninety", "Thirty"]), $"the words ending in \"ty\" (found {string.Join(", ", words)})");
}
