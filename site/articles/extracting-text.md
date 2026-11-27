---
title: "Extracting text"
---

# Extracting text

This guide reads text out of pages: as plain text, as words and glyphs with their positions, in reading order, and as a layout tree of paragraphs, headings, lists, tables and figures. The code comes from the [`ExtractingText`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/ExtractingText) sample, which reads files from the repository's test corpus; CI builds and runs it on every change. Extraction only reads, so it works on files of any PDF version.

## Plain text

`PdfPage.ExtractText` returns a `TextPage`. Its `PlainText` puts one space between words and one line feed between lines; `Text` keeps the text of every glyph, as the content shows it.

<!-- snippet: samples/guides/ExtractingText/Program.cs#plain-text -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
foreach (PdfPage page in document.Pages)
{
    TextPage text = page.ExtractText();
    Console.WriteLine(text.PlainText); // words separated by a space, lines by a line feed
}
```
<!-- endsnippet -->

Characters come from the font's `ToUnicode` map, then its encoding and glyph names; ligatures and other glyphs that stand for several characters give all of them. `TextGlyph.UnicodeSource` says where each glyph's text came from.

## Words, lines and positions

`Glyphs`, `Words` and `Lines` give every glyph, word and line with a quadrilateral in page space (points, origin at the bottom left), so rotated and skewed text has an exact outline; `Quad.Bounds` is its bounding rectangle. Each glyph also has its font, size, origin, advance and rendering mode.

<!-- snippet: samples/guides/ExtractingText/Program.cs#positions -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
TextPage text = document.Pages[0].ExtractText();
foreach (TextWord word in text.Words)
{
    PdfRectangle box = word.Quad.Bounds; // in page space: points, origin at the bottom left
    Console.WriteLine($"'{text.GetWordText(word)}' at ({box.Left:0.#}, {box.Bottom:0.#}), {box.Width:0.#} x {box.Height:0.#}");
}

TextGlyph first = text.Glyphs[0];
Console.WriteLine($"The first glyph is {text.GetGlyphText(first)} in {first.Font.BaseFont} at {first.FontSize} points");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §9.4 (text objects), §9.10 (extraction of text content)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Reading order

A tagged page is read in the order of its structure tree: the order its author meant, whatever order the content draws in. `TextReadingOrder.Content` gives the order the page draws in instead. An untagged page has only content order; the layout tree below finds its reading order.

<!-- snippet: samples/guides/ExtractingText/Program.cs#reading-order -->
```csharp
// A tagged page reads in the order of its structure tree by default; content order is the order the page draws in.
using PdfDocument document = PdfDocument.Open("layout-two-column-tagged.pdf");
PdfPage page = document.Pages[0];
string logical = page.ExtractText().PlainText;
string drawn = page.ExtractText(new TextExtractionOptions().WithReadingOrder(TextReadingOrder.Content)).PlainText;
Console.WriteLine($"Structure order: {logical.ReplaceLineEndings(" ")[..60]}...");
Console.WriteLine($"Content order:   {drawn.ReplaceLineEndings(" ")[..60]}...");
```
<!-- endsnippet -->

Headers, footers and other artifacts are left out of a tagged page's text; `WithArtifacts()` includes them. An `ActualText` replacement always applies; `WithAlternateDescriptions()` and `WithExpansions()` also replace the text a structure element covers with its alternate description, or an abbreviation with its expansion. Text hidden by optional content is left out unless `WithHiddenContent()` is set.

Conformance: [ISO 32000-2 §14.7 (logical structure), §14.8.2.5 (reading order), §14.9 (accessibility support)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## The layout tree

`ExtractLayout` returns a tree of the document's (or a page's) content: sections, headings with their levels, paragraphs, lists and list items, tables with rows and cells, figures, captions, notes and links. A tagged page takes the tree from its structure. An untagged page is analyzed instead: columns from the white space between them, paragraphs from indents and spacing, headings from size and weight, lists from their markers, tables from rules or aligned white space, and running headers and footers that repeat across pages, which are left out.

<!-- snippet: samples/guides/ExtractingText/Program.cs#layout-tree -->
```csharp
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
```
<!-- endsnippet -->

Each node has its `Text`, its `Extents` (the page, bounds and glyphs it covers on each page it spans), its `Language` and `AlternateDescription`, and, on a tagged page, its `StructureElement`. `LayoutOptions.WithDetection` chooses which pages are analyzed: untagged pages (the default), all of them, or none.

## Tables

A `LayoutTable` has its row and column counts, and cells that span rows or columns appear once, in the row where they start. A table that continues on the next page is linked to its other parts through `NextPart` and `PreviousPart`.

<!-- snippet: samples/guides/ExtractingText/Program.cs#tables -->
```csharp
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
```
<!-- endsnippet -->

## Markdown and JSON for LLMs

Plain text loses the structure a language model or a search index needs. `ExtractMarkdown` (on the document or a page) and `LayoutTree.ToMarkdown` write the layout tree as Markdown: headings as `#` by level, paragraphs, bullet and numbered lists nested under their items, tables as pipe tables (a table split over pages becomes one), figures as image references or their alternate text followed by their caption, links with a URI as Markdown links, and notes as footnotes. Bold and italic come from the fonts. Headers, footers and page numbers are artifacts, so they are left out.

`MarkdownSaveOptions` chooses the details: `WithPageBreaks` adds a `<!-- page n -->` comment before each page's first block, `WithImages(false)` writes a figure's alternate text in italics instead of an image reference, `WithInlineEmphasis(false)` drops the bold and italic markers, and `WithTables(MarkdownTableStyle.Html)` writes a table with merged cells or no header row as an HTML table with `rowspan` and `colspan` (by default its text is repeated in each cell it covers).

`SaveJson` writes the tree itself as UTF-8 JSON, schema version 1: each node has its `kind`, its `text`, the `pages` it lies on and its `children`, plus `level` for a heading, `numbering` for a list, the grid for tables and cells, `uri` for a link and `alt` for a figure. `LayoutJsonOptions.WithBounds` adds each node's bounds per page. A later schema version only adds properties.

<!-- snippet: samples/guides/ExtractingText/Program.cs#markdown-json -->
```csharp
// Markdown keeps the headings, lists and tables a language model reads better with; JSON keeps the whole tree for a pipeline.
using PdfDocument document = PdfDocument.Open("layout-tables.pdf");
string markdown = document.ExtractMarkdown(new MarkdownSaveOptions().WithPageBreaks(true));
Console.WriteLine(markdown);

LayoutTree layout = document.ExtractLayout();
using var json = new MemoryStream();
layout.SaveJson(json, new LayoutJsonOptions().WithBounds(true));
Console.WriteLine($"{json.Length} bytes of layout JSON");
```
<!-- endsnippet -->

The same tree drives [automatic tagging](accessibility-and-conformance.md#tagging-an-existing-file), which writes it into the document as its structure.
