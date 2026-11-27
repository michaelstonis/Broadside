using System.Text;

namespace Broadside.Layout;

/// <content>Markdown and JSON serialization of the layout tree (issue #629): ISO 32000-2 §14.8.4, §14.9.3 and §14.8.2.2.</content>
public sealed partial class LayoutTree
{
    /// <summary>
    /// Writes the tree as Markdown (CommonMark 0.31 with the GitHub Flavored Markdown table and footnote extensions), lines ended by
    /// line feeds: headings, paragraphs, lists, tables, figures and captions, links and notes, in reading order.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="options">What to write; <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.8.4: each node kind is the Markdown block its standard structure type stands for. A <see cref="LayoutNodeKind.Heading"/>
    /// is <c>#</c> repeated its level (a title, level 0, as 1; at most 6); a paragraph or caption is a paragraph; a list is a bullet or an
    /// ordered list by its <see cref="LayoutList.Numbering"/>, nested lists indented under their item; a table is a pipe table whose
    /// header is its first row, or, with spans or no header row, as <see cref="MarkdownSaveOptions.Tables"/> says, and the parts of a
    /// table split over pages (<see cref="LayoutTable.NextPart"/>) are one table. A figure is an image reference or its alternate
    /// description (§14.9.3), followed by its caption. A link with a URI action (§12.6.4.8) is a Markdown link; a link to a destination in
    /// the document is its text. A note inside a block of text is a footnote reference with its text at the end; any other note is a
    /// footnote definition where it stands. Bold and italic come from the fonts of each word's glyphs (§9.8). Words are joined by one
    /// space, lines of a block too, and right-to-left text stays in logical order.
    /// </para>
    /// <para>
    /// Artifacts are not in the tree (§14.8.2.2), so running headers, footers and page numbers are not written. Text that Markdown would
    /// read as markup is backslash-escaped. An empty tree writes nothing. The file is not read again: nothing is recorded in
    /// <see cref="PdfDocument.Diagnostics"/>. Safe to call from several threads on one tree.
    /// </para>
    /// </remarks>
    public void SaveMarkdown(TextWriter writer, MarkdownSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        MarkdownWriter.Write(this, writer, options ?? new MarkdownSaveOptions());
    }

    /// <summary>Returns the tree as Markdown, as <see cref="SaveMarkdown"/> writes it.</summary>
    /// <param name="options">What to write; <see langword="null"/> for the defaults.</param>
    /// <returns>The Markdown; empty for an empty tree.</returns>
    /// <remarks>ISO 32000-2 §14.8.4, §14.9.3 and §14.8.2.2; see <see cref="SaveMarkdown"/>.</remarks>
    public string ToMarkdown(MarkdownSaveOptions? options = null)
    {
        var writer = new StringWriter(new StringBuilder(), System.Globalization.CultureInfo.InvariantCulture);
        SaveMarkdown(writer, options);
        return writer.ToString();
    }

    /// <summary>
    /// Writes the tree as UTF-8 JSON, version 1 of the layout schema: the root node, with <c>"schema": 1</c>, and its descendants as
    /// nested objects.
    /// </summary>
    /// <param name="stream">Where to write; left open.</param>
    /// <param name="options">What to write; <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.8.4. Each node is an object with, in this order and only when they apply: <c>"kind"</c> (the
    /// <see cref="LayoutNodeKind"/> in camel case: <c>"document"</c>, <c>"heading"</c>, <c>"listItem"</c>, <c>"tableCell"</c>...);
    /// <c>"structureType"</c> (the structure element's type as the file names it, §14.7.2); <c>"level"</c> for a heading;
    /// <c>"numbering"</c> for a list; <c>"rows"</c>, <c>"columns"</c>, <c>"previousPart"</c> and <c>"nextPart"</c> (<see langword="true"/>
    /// when the table continues one on the page before, or on the next) for a table; <c>"row"</c>, <c>"column"</c>, <c>"rowSpan"</c>,
    /// <c>"columnSpan"</c> and <c>"header"</c> for a cell; <c>"uri"</c> for a link with a URI action (§12.6.4.8); <c>"alt"</c>, the
    /// alternate description (§14.9.3); <c>"lang"</c>, when it differs from the parent's (§14.9.2); <c>"text"</c>, the node's
    /// <see cref="LayoutNode.Text"/>, on every node but a document, section, list, list item, list body, table or row that has
    /// children (their text is their children's); <c>"pages"</c>, the page numbers (from 1) the node lies on; <c>"bounds"</c> with
    /// <see cref="LayoutJsonOptions.IncludeBounds"/>, one <c>[left, bottom, right, top]</c> or <see langword="null"/> per page; and
    /// <c>"children"</c>, when it has any.
    /// </para>
    /// <para>
    /// Strings are written unescaped beyond what JSON requires, so text in any script stays readable. A later schema version only adds
    /// properties. The file is not read again: nothing is recorded in <see cref="PdfDocument.Diagnostics"/>. Safe to call from several
    /// threads on one tree.
    /// </para>
    /// </remarks>
    public void SaveJson(Stream stream, LayoutJsonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        LayoutJsonWriter.Write(this, stream, options ?? new LayoutJsonOptions());
    }
}
