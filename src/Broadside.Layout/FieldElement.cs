using Broadside.Forms;

namespace Broadside.Layout.Flow;

/// <summary>A table of contents' modifiers in the document builder: its title, its depth and its leader.</summary>
/// <remarks>ISO 32000-2 §14.8.4.4 (<c>TOC</c>, <c>TOCI</c>).</remarks>
public readonly ref struct TableOfContentsElement
{
    private readonly BuildContext? _context;
    private readonly TocNode? _toc;

    internal TableOfContentsElement(BuildContext context, TocNode toc)
    {
        _context = context;
        _toc = toc;
    }

    private TocNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _toc!;
        }
    }

    /// <summary>
    /// Adds a title above the entries, in the theme's level-2 heading look and kept with them. It is tagged as a paragraph, not a
    /// heading, so the table of contents and the outline do not list it.
    /// </summary>
    /// <param name="title">The title, such as "Contents".</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="title"/> is null or empty.</exception>
    public TableOfContentsElement Title(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        Node.Title = title;
        return this;
    }

    /// <summary>Sets the deepest heading level listed; 3 by default.</summary>
    /// <param name="depth">The level, 1 to 6.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is outside 1 to 6.</exception>
    public TableOfContentsElement Depth(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 6);
        Node.Depth = depth;
        return this;
    }

    /// <summary>Sets the text repeated between an entry and its page number; "." by default.</summary>
    /// <param name="leader">The text.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="leader"/> is null or empty.</exception>
    public TableOfContentsElement Leader(string leader)
    {
        ArgumentException.ThrowIfNullOrEmpty(leader);
        Node.Leader = leader;
        return this;
    }

    /// <summary>Sets the space above the table of contents (or its title).</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public TableOfContentsElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Flow.Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Applies a partial style to the entries.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public TableOfContentsElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        TocNode node = Node;
        node.Local = (node.Local ?? Flow.Style.Empty).Merge(style);
        return this;
    }
}

/// <summary>A form field's modifiers in the document builder: its size, its flags, its initial value and where its label goes.</summary>
/// <remarks>
/// ISO 32000-2 §12.7 (interactive forms) and §14.8.4.7.3 (<c>Form</c>): the field is tagged <c>Form</c> with its label and its widget's
/// object reference; pages with fields get <c>/Tabs /S</c>.
/// </remarks>
public readonly ref struct FieldElement
{
    private readonly BuildContext? _context;
    private readonly FieldNode? _field;

    internal FieldElement(BuildContext context, FieldNode field)
    {
        _context = context;
        _field = field;
    }

    private FieldNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _field!;
        }
    }

    /// <summary>Sets the field's width; by default a text, choice or signature field fills the width of the space it is in.</summary>
    /// <param name="width">The width.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    public FieldElement Width(Length width)
    {
        Node.Width = width.ToPoints() > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width), width, "The width shall be positive.");
        return this;
    }

    /// <summary>Sets the field's height; by default one line of the label's size (four for a list box, three for a signature).</summary>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is not positive.</exception>
    public FieldElement Height(Length height)
    {
        Node.Height = height.ToPoints() > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height), height, "The height shall be positive.");
        return this;
    }

    /// <summary>Sets the field's width and height.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A side is not positive.</exception>
    public FieldElement Size(Length width, Length height) => Width(width).Height(height);

    /// <summary>Makes the field as wide as the space it is in (the default for text, choice and signature fields).</summary>
    /// <returns>This builder.</returns>
    public FieldElement FillWidth()
    {
        Node.Width = null;
        return this;
    }

    /// <summary>Makes a text field hold several lines, <paramref name="lines"/> lines tall unless a height is set.</summary>
    /// <param name="lines">The number of lines shown.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lines"/> is less than 1.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 228 (<c>Multiline</c>).</remarks>
    public FieldElement Multiline(int lines = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lines, 1);
        FieldNode node = Node;
        node.Flags |= PdfFieldFlags.Multiline;
        node.Lines = lines;
        return this;
    }

    /// <summary>Marks the field as required: a form submitted without it is refused.</summary>
    /// <returns>This builder.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227 (<c>Required</c>).</remarks>
    public FieldElement Required()
    {
        Node.Flags |= PdfFieldFlags.Required;
        return this;
    }

    /// <summary>Makes the field read-only.</summary>
    /// <returns>This builder.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227 (<c>ReadOnly</c>).</remarks>
    public FieldElement ReadOnly()
    {
        Node.Flags |= PdfFieldFlags.ReadOnly;
        return this;
    }

    /// <summary>Sets the initial value: a text field's text, or the choice selected in a combo box, list box or radio group.</summary>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public FieldElement Value(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Node.Value = value;
        return this;
    }

    /// <summary>Makes a check box start checked.</summary>
    /// <returns>This builder.</returns>
    public FieldElement Checked()
    {
        Node.Checked = true;
        return this;
    }

    /// <summary>Draws the label before the field on the same row instead of above it (a check box and radio buttons always have theirs beside).</summary>
    /// <returns>This builder.</returns>
    public FieldElement LabelBeside()
    {
        Node.LabelPosition = FieldLabelPosition.Before;
        return this;
    }

    /// <summary>Sets the space above the field and its label.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public FieldElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Flow.Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the field.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public FieldElement SpaceAfter(Length space)
    {
        Node.Local = (Node.Local ?? Flow.Style.Empty) with { SpaceAfter = space };
        return this;
    }

    /// <summary>Keeps the field on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public FieldElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Applies a partial style to the label.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public FieldElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        FieldNode node = Node;
        node.Local = (node.Local ?? Flow.Style.Empty).Merge(style);
        return this;
    }
}
