using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The state of one document builder run: the document being built, the document-level settings and the tree of content the lambdas
/// describe. The builder handles are views over it; the tree is resolved into flow blocks after the outermost lambda returns.
/// </summary>
internal sealed class BuildContext(PdfDocument document, bool appending)
{
    public PdfDocument Document { get; } = document;

    /// <summary>True when the pages are appended to an opened document rather than a new one.</summary>
    public bool Appending { get; } = appending;

    public Theme Theme { get; set; } = Theme.Default;

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string? Language { get; set; }

    public bool Tagging { get; set; } = true;

    public List<SectionNode> Sections { get; } = [];

    /// <summary>The section <see cref="DocumentBuilder.Content"/> adds to: the last section, or a new one.</summary>
    public SectionNode CurrentSection
    {
        get
        {
            if (Sections.Count == 0)
            {
                Sections.Add(new SectionNode());
            }

            return Sections[^1];
        }
    }

    /// <summary>What the builder found worth reporting before layout.</summary>
    public List<Diagnostic> Diagnostics { get; } = [];

    /// <summary>The strings of the plain rows added through the fast path, back to back.</summary>
    public List<string> PlainCells { get; } = [];

    /// <summary>True once the build has finished: a handle used after that throws.</summary>
    public bool Closed { get; set; }

    public static BuildContext Check(BuildContext? context)
    {
        if (context is null)
        {
            throw new InvalidOperationException("The builder is not part of a document build: a default builder value cannot be used.");
        }

        if (context.Closed)
        {
            throw new InvalidOperationException("The builder was used after its document build finished.");
        }

        return context;
    }

    public void Report(DiagnosticCode code, string message) => Diagnostics.Add(new Diagnostic(code.Name, code.DefaultSeverity, message));
}

/// <summary>A section: its page setup, header and footer variants, scope style and content.</summary>
internal sealed class SectionNode
{
    public PageSize? Size { get; set; }

    public bool? Landscape { get; set; }

    public PageMargins? Margins { get; set; }

    public Style? Style { get; set; }

    public HeaderFooterNode? Header { get; set; }

    public HeaderFooterNode? Footer { get; set; }

    public HeaderFooterNode? FirstPageHeader { get; set; }

    public HeaderFooterNode? FirstPageFooter { get; set; }

    public HeaderFooterNode? EvenPageHeader { get; set; }

    public HeaderFooterNode? EvenPageFooter { get; set; }

    public Action<PdfCanvas, PageInfo>? Decoration { get; set; }

    public PdfColor? BackgroundColor { get; set; }

    public ImageSource? BackgroundImage { get; set; }

    public string? Watermark { get; set; }

    public Style? WatermarkStyle { get; set; }

    public ContainerNode Content { get; } = new();
}

/// <summary>A header or footer: three slots, a style and an offset.</summary>
internal sealed class HeaderFooterNode
{
    public PageText? Left { get; set; }

    public PageText? Center { get; set; }

    public PageText? Right { get; set; }

    public Style? Style { get; set; }

    public Length? Offset { get; set; }
}

/// <summary>The blocks of a scope, in order.</summary>
internal sealed class ContainerNode
{
    public List<BlockNode> Blocks { get; } = [];
}

/// <summary>An element of a container, with the partial style its modifiers set.</summary>
internal abstract class BlockNode
{
    public Style? Local { get; set; }

    /// <summary>True when the element stays on the page of the element after it.</summary>
    public bool KeepWithNext { get; set; }

    /// <summary>True when the element stays on one page when it fits on one.</summary>
    public bool KeepTogether { get; set; }
}

/// <summary>A paragraph or a heading: plain text or spans.</summary>
internal sealed class ParagraphNode : BlockNode
{
    public string? Text { get; set; }

    public List<SpanNode>? Spans { get; set; }

    /// <summary>The heading level, 1 to 6, or 0 for a paragraph.</summary>
    public int Level { get; set; }

    public string? Id { get; set; }
}

/// <summary>A span of a paragraph: text, a link or a line break.</summary>
internal sealed class SpanNode
{
    public required string Text { get; init; }

    public Style? Local { get; set; }

    public Uri? Link { get; init; }

    public bool LineBreak { get; init; }
}

/// <summary>A list and its items.</summary>
internal sealed class ListNode : BlockNode
{
    public List<ItemNode> Items { get; } = [];

    public ListNumbering Numbering { get; set; }

    public string? Bullet { get; set; }

    public int StartAt { get; set; } = 1;

    public Length? Indent { get; set; }

    public string? Prefix { get; set; }

    public string? Suffix { get; set; }
}

/// <summary>A list item: text or nested content.</summary>
internal sealed class ItemNode
{
    public string? Text { get; init; }

    public ContainerNode? Content { get; init; }
}

/// <summary>An image, described or decorative.</summary>
internal sealed class ImageNode : BlockNode
{
    public required ImageSource Source { get; init; }

    public string? AlternateText { get; init; }

    public Length? Width { get; set; }

    public Length? Height { get; set; }

    public bool FillWidth { get; set; }

    public TextAlignment Alignment { get; set; }

    public string? Caption { get; set; }
}

/// <summary>A group: content sharing a scope style.</summary>
internal sealed class GroupNode : BlockNode
{
    public ContainerNode Content { get; } = new();

    public Style? Scope { get; set; }

    /// <summary>The structure type the group is tagged as, or null for a group that adds no element.</summary>
    public string? Role { get; set; }
}

/// <summary>A table: columns, header rows, body rows and its options.</summary>
internal sealed class TableNode : BlockNode
{
    public ColumnWidth[]? Columns { get; set; }

    public List<RowNode> HeaderRows { get; } = [];

    public List<RowNode> BodyRows { get; } = [];

    public Style? Scope { get; set; }

    public string? Caption { get; set; }

    public bool Striped { get; set; }

    public TableBorder? Border { get; set; }

    public Length? Padding { get; set; }

    public PdfColor? HeaderBackground { get; set; }

    public bool HasHeaderBackground { get; set; }

    public bool? RepeatHeaderRows { get; set; }

    public bool? KeepRowsTogether { get; set; }

    public Length? SpaceBefore { get; set; }

    public Length? SpaceAfter { get; set; }
}

/// <summary>A table row: built cells, or a run of plain strings in <see cref="BuildContext.PlainCells"/>.</summary>
internal sealed class RowNode
{
    public List<CellNode>? Cells { get; set; }

    public int PlainStart { get; init; }

    public int PlainCount { get; init; }

    public Style? Scope { get; set; }

    public PdfColor? Background { get; set; }

    public bool? KeepTogether { get; set; }

    public Length? MinHeight { get; set; }

    /// <summary>The number of columns the row's cells cover.</summary>
    public int Span
    {
        get
        {
            if (Cells is null)
            {
                return PlainCount;
            }

            int span = 0;
            foreach (CellNode cell in Cells)
            {
                span += cell.ColumnSpan;
            }

            return span;
        }
    }
}

/// <summary>A table cell: text or nested content.</summary>
internal sealed class CellNode
{
    public string? Text { get; init; }

    public ContainerNode? Content { get; init; }

    public bool Header { get; init; }

    public int ColumnSpan { get; set; } = 1;

    public int RowSpan { get; set; } = 1;

    public Style? Scope { get; set; }

    public PdfColor? Background { get; set; }

    public Length? Padding { get; set; }
}

/// <summary>Vertical space, dropped at the top of a page.</summary>
internal sealed class SpacerNode(Length height) : BlockNode
{
    public Length Height { get; } = height;
}

/// <summary>A page break.</summary>
internal sealed class PageBreakNode : BlockNode
{
}

/// <summary>A horizontal rule.</summary>
internal sealed class RuleNode : BlockNode
{
    public Length? Thickness { get; set; }

    public PdfColor? Color { get; set; }
}

/// <summary>An engine block added as it is.</summary>
internal sealed class RawNode(FlowBlock block) : BlockNode
{
    public FlowBlock Block { get; } = block;
}

/// <summary>A vector drawing: a box the callback paints, described or decorative.</summary>
internal sealed class DrawingNode : BlockNode
{
    public required Length Width { get; init; }

    public required Length Height { get; init; }

    public required Action<PdfCanvas, PdfRectangle> Draw { get; init; }

    public string? AlternateText { get; init; }

    public bool FillWidth { get; set; }

    public TextAlignment Alignment { get; set; }

    public string? Caption { get; set; }
}

/// <summary>Columns side by side, each its own scope of content.</summary>
internal sealed class ColumnsNode : BlockNode
{
    public List<ColumnNode> Columns { get; } = [];

    public Style? Scope { get; set; }

    public Length? Gap { get; set; }
}

/// <summary>One column: its width and content.</summary>
internal sealed class ColumnNode(ColumnWidth width)
{
    public ColumnWidth Width { get; } = width;

    public ContainerNode Content { get; } = new();
}
