using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>The kinds of node in a parsed Markdown document: the CommonMark 0.31 blocks and inlines, and the GFM extensions.</summary>
internal enum MarkdownKind
{
    Document,
    BlockQuote,
    List,
    Item,
    Paragraph,
    Heading,
    ThematicBreak,
    CodeBlock,
    HtmlBlock,
    Table,
    TableRow,
    TableCell,
    Text,
    SoftBreak,
    LineBreak,
    Code,
    Emphasis,
    Strong,
    Strikethrough,
    Link,
    Image,
    HtmlInline,
}

/// <summary>The alignment of a GFM table column, from the colons of its delimiter row.</summary>
internal enum MarkdownAlignment
{
    None,
    Left,
    Center,
    Right,
}

/// <summary>What a list or list item's marker says: bullet or ordered, its character, start number and the content's indent.</summary>
internal sealed class MarkdownListData
{
    public bool Ordered { get; set; }

    public bool Tight { get; set; } = true;

    public char BulletChar { get; set; }

    public int Start { get; set; } = 1;

    public char Delimiter { get; set; }

    public int Padding { get; set; }

    public int MarkerOffset { get; set; }

    public bool Matches(MarkdownListData item) =>
        Ordered == item.Ordered && Delimiter == item.Delimiter && BulletChar == item.BulletChar;
}

/// <summary>
/// A node of the Markdown syntax tree, a doubly linked tree in the shape of the CommonMark reference implementation's, so moving
/// a run of siblings under a new parent (emphasis, links) costs one step per node and never recurses.
/// </summary>
internal sealed class MarkdownNode(MarkdownKind kind)
{
    public MarkdownKind Kind { get; set; } = kind;

    public MarkdownNode? Parent { get; private set; }

    public MarkdownNode? FirstChild { get; private set; }

    public MarkdownNode? LastChild { get; private set; }

    public MarkdownNode? Previous { get; private set; }

    public MarkdownNode? Next { get; private set; }

    /// <summary>The text of a text, code, HTML or code block node.</summary>
    public string Literal { get; set; } = string.Empty;

    /// <summary>The raw lines of a block still being parsed, or its inline source until inlines are parsed.</summary>
    public StringBuilder? Content { get; set; }

    public bool IsOpen { get; set; } = true;

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    /// <summary>The heading level, 1 to 6.</summary>
    public int Level { get; set; }

    public string Destination { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The info string of a fenced code block.</summary>
    public string Info { get; set; } = string.Empty;

    public bool IsFenced { get; set; }

    public char FenceChar { get; set; }

    public int FenceLength { get; set; }

    public int FenceOffset { get; set; }

    public int HtmlBlockType { get; set; }

    public MarkdownListData? ListData { get; set; }

    /// <summary>A GFM task list item's state: null for an ordinary item.</summary>
    public bool? TaskChecked { get; set; }

    /// <summary>A GFM table's column alignments, or a cell's alignment in its one element.</summary>
    public MarkdownAlignment[]? Alignments { get; set; }

    /// <summary>Whether a table row is the header row.</summary>
    public bool IsHeader { get; set; }

    public void AppendChild(MarkdownNode child)
    {
        child.Unlink();
        child.Parent = this;
        if (LastChild is { } last)
        {
            last.Next = child;
            child.Previous = last;
            LastChild = child;
        }
        else
        {
            FirstChild = child;
            LastChild = child;
        }
    }

    public void InsertAfter(MarkdownNode sibling)
    {
        sibling.Unlink();
        sibling.Next = Next;
        if (sibling.Next is { } next)
        {
            next.Previous = sibling;
        }

        sibling.Previous = this;
        Next = sibling;
        sibling.Parent = Parent;
        if (sibling.Next is null && sibling.Parent is { } parent)
        {
            parent.LastChild = sibling;
        }
    }

    public void InsertBefore(MarkdownNode sibling)
    {
        sibling.Unlink();
        sibling.Previous = Previous;
        if (sibling.Previous is { } previous)
        {
            previous.Next = sibling;
        }

        sibling.Next = this;
        Previous = sibling;
        sibling.Parent = Parent;
        if (sibling.Previous is null && sibling.Parent is { } parent)
        {
            parent.FirstChild = sibling;
        }
    }

    public void Unlink()
    {
        if (Previous is { } previous)
        {
            previous.Next = Next;
        }
        else if (Parent is { } parent)
        {
            parent.FirstChild = Next;
        }

        if (Next is { } next)
        {
            next.Previous = Previous;
        }
        else if (Parent is { } parent)
        {
            parent.LastChild = Previous;
        }

        Parent = null;
        Next = null;
        Previous = null;
    }

    /// <summary>The plain text of the node's inlines: text, code and the text of nested inlines, line breaks as spaces.</summary>
    public string PlainText()
    {
        var text = new StringBuilder();
        foreach ((MarkdownNode node, bool entering) in Walk())
        {
            if (!entering)
            {
                continue;
            }

            switch (node.Kind)
            {
                case MarkdownKind.Text or MarkdownKind.Code:
                    text.Append(node.Literal);
                    break;
                case MarkdownKind.SoftBreak or MarkdownKind.LineBreak:
                    text.Append(' ');
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>The nodes under this one in document order, each entered and (for one with children) left; iterative, so depth costs nothing.</summary>
    public IEnumerable<(MarkdownNode Node, bool Entering)> Walk()
    {
        MarkdownNode? current = this;
        bool entering = true;
        while (current is not null)
        {
            yield return (current, entering);
            if (entering && current.FirstChild is { } child)
            {
                current = child;
                continue;
            }

            if (entering && IsContainer(current.Kind))
            {
                yield return (current, false);
            }

            if (ReferenceEquals(current, this))
            {
                yield break;
            }

            if (current.Next is { } next)
            {
                current = next;
                entering = true;
            }
            else
            {
                current = current.Parent;
                entering = false;
                if (ReferenceEquals(current, this))
                {
                    yield return (current!, false);
                    yield break;
                }
            }
        }
    }

    /// <summary>Whether nodes of <paramref name="kind"/> hold other nodes, so a walk leaves them as well as entering them.</summary>
    public static bool IsContainer(MarkdownKind kind) => kind is not (MarkdownKind.Text or MarkdownKind.SoftBreak or MarkdownKind.LineBreak
        or MarkdownKind.Code or MarkdownKind.HtmlInline or MarkdownKind.ThematicBreak or MarkdownKind.CodeBlock or MarkdownKind.HtmlBlock);
}
