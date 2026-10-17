using System.Globalization;
using Broadside.Layout;

namespace Broadside.Viewer;

/// <summary>
/// A node of an <see cref="AccessibilityBridge"/>'s accessible tree: a role, a name, a value, table structure, and bounds on its
/// page, for a shell to map onto an ARIA element, a UI Automation peer or an NSAccessibility element.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 (standard structure types), §14.8.4.8 (table attributes), §14.9.3 (alternate descriptions), Spec #33
/// viewer capability 8. Content nodes come from the page's <see cref="LayoutTree"/> (<see cref="PdfPage.ExtractLayout()"/>):
/// structure order on tagged pages, detected order on untagged ones, artifacts excluded. A structure element spanning pages has
/// one node on each page. Dynamic state (focus, offscreen) and viewport bounds come from the bridge.
/// </remarks>
public sealed class AccessibleNode
{
    private static readonly AccessibleNode[] NoNodes = [];
    private string? _name;
    private bool _named;

    internal AccessibleNode(AccessibilityBridge owner, AccessibleRole role, int pageIndex, AccessibleNode? parent)
    {
        Owner = owner;
        Role = role;
        PageIndex = pageIndex;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <summary>Gets the node's role.</summary>
    public AccessibleRole Role { get; }

    /// <summary>
    /// Gets the node's accessible name: the alternate description (Alt) when there is one; otherwise, for roles named from their
    /// content (headings, paragraphs, cells, links, list items and markers, captions, notes, text), the node's text; the document
    /// title for the document; the element's name for elements. Null when there is none (a figure without Alt is unnamed).
    /// </summary>
    public string? Name
    {
        get
        {
            if (!_named)
            {
                _name = ComputeName();
                _named = true;
            }

            return _name;
        }
    }

    /// <summary>Gets the node's value: an element's value; null for content.</summary>
    public string? Value { get; internal init; }

    /// <summary>Gets the node's text, ActualText applied, as the layout tree reads it; an element's value or name.</summary>
    public string Text => LayoutNode?.Text ?? Value ?? (IsElement ? Name : null) ?? string.Empty;

    /// <summary>Gets the natural language of the node's content (Lang), when given.</summary>
    public string? Language { get; internal init; }

    /// <summary>Gets a heading's level (1 to 6 for Hn, the number of enclosing sections for H, 0 for a title); 0 otherwise.</summary>
    public int Level { get; internal init; }

    /// <summary>Gets the zero-based page the node is on; -1 for the document.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the node's position in its page's reading order; -1 for the document and pages.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>Gets the parent node; null for the document.</summary>
    public AccessibleNode? Parent { get; }

    /// <summary>Gets the child nodes in reading order. A page's children are empty until the page is loaded.</summary>
    public IReadOnlyList<AccessibleNode> Children => ChildArray;

    /// <summary>Gets a table's number of rows; 0 otherwise.</summary>
    public int RowCount { get; internal init; }

    /// <summary>Gets a table's number of columns; 0 otherwise.</summary>
    public int ColumnCount { get; internal init; }

    /// <summary>Gets a cell's zero-based row in its table; -1 otherwise.</summary>
    public int Row { get; internal init; } = -1;

    /// <summary>Gets a cell's zero-based column in its table; -1 otherwise.</summary>
    public int Column { get; internal init; } = -1;

    /// <summary>Gets the number of rows a cell spans; 0 otherwise.</summary>
    public int RowSpan { get; internal init; }

    /// <summary>Gets the number of columns a cell spans; 0 otherwise.</summary>
    public int ColumnSpan { get; internal init; }

    /// <summary>Gets a cell's header cells (Headers, §14.8.5.7) on the same page; empty otherwise.</summary>
    public IReadOnlyList<AccessibleNode> Headers => HeaderArray;

    /// <summary>Gets the node's rectangle on its page in default user space (the union of its content); null when it has none.</summary>
    public PdfRectangle? PageBounds { get; internal init; }

    /// <summary>Gets the layout node a content node was built from; null for the document, pages and elements.</summary>
    public LayoutNode? LayoutNode { get; internal init; }

    /// <summary>Gets the object an element stands for (<see cref="AccessibleElement.Source"/>); null for content.</summary>
    public object? Source { get; internal init; }

    internal AccessibilityBridge Owner { get; }

    internal int Depth { get; }

    internal AccessibleStates StaticStates { get; init; }

    internal bool IsElement { get; init; }

    internal PdfDocument? Document { get; init; }

    internal AccessibleNode[] ChildArray { get; set; } = NoNodes;

    internal AccessibleNode[] HeaderArray { get; set; } = NoNodes;

    /// <inheritdoc/>
    public override string ToString() => Role switch
    {
        AccessibleRole.Document => "Document",
        AccessibleRole.Page => string.Create(CultureInfo.InvariantCulture, $"Page {PageIndex + 1}"),
        AccessibleRole.Heading => string.Create(CultureInfo.InvariantCulture, $"Heading {Level}: {Name}"),
        _ => Name is { } name ? $"{Role}: {name}" : Role.ToString(),
    };

    private string? ComputeName()
    {
        if (IsElement)
        {
            return _name;
        }

        if (Document is { } document)
        {
            return document.Information?.Title;
        }

        if (LayoutNode is not { } layout)
        {
            return null;
        }

        if (layout.AlternateDescription is { Length: > 0 } alternate)
        {
            return alternate;
        }

        return Role switch
        {
            AccessibleRole.Heading or AccessibleRole.Paragraph or AccessibleRole.Cell or AccessibleRole.ColumnHeader
                or AccessibleRole.RowHeader or AccessibleRole.Link or AccessibleRole.ListItem or AccessibleRole.ListMarker
                or AccessibleRole.Caption or AccessibleRole.Note or AccessibleRole.Text => Trimmed(layout.Text),
            _ => null,
        };
    }

    internal void SetElementName(string? name)
    {
        _name = name;
        _named = true;
    }

    private static string? Trimmed(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
