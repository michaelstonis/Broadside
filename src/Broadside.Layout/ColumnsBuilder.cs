using System.Diagnostics.CodeAnalysis;

namespace Broadside.Layout.Flow;

/// <summary>The columns of a <see cref="ContainerBuilder.Columns"/> block in the document builder, left to right.</summary>
/// <remarks>ISO 32000-2 §14.8.4.4: each column is tagged as one <c>Div</c> holding its content.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ColumnsBuilder
{
    private readonly BuildContext? _context;
    private readonly ColumnsNode? _columns;

    internal ColumnsBuilder(BuildContext context, ColumnsNode columns)
    {
        _context = context;
        _columns = columns;
    }

    /// <summary>Adds a column of an equal share of the width the other columns leave.</summary>
    /// <param name="build">Describes the column's content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public void Column(Action<ContainerBuilder> build) => Column(ColumnWidth.Proportional(), build);

    /// <summary>Adds a column of <paramref name="width"/>: fixed (a <see cref="Length"/> converts to one), a percentage of the width, or a proportional share.</summary>
    /// <param name="width">The width.</param>
    /// <param name="build">Describes the column's content.</param>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public void Column(ColumnWidth width, Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        BuildContext context = BuildContext.Check(_context);
        var column = new ColumnNode(width);
        _columns!.Columns.Add(column);
        build(new ContainerBuilder(context, column.Content));
    }
}

/// <summary>A columns block's modifiers in the document builder: the gap, its spacing and the style its columns inherit.</summary>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct ColumnsElement
{
    private readonly BuildContext? _context;
    private readonly ColumnsNode? _columns;

    internal ColumnsElement(BuildContext context, ColumnsNode columns)
    {
        _context = context;
        _columns = columns;
    }

    private ColumnsNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _columns!;
        }
    }

    /// <summary>Sets the space between two columns; 12 points by default.</summary>
    /// <param name="gap">The gap.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gap"/> is negative.</exception>
    public ColumnsElement Gap(Length gap)
    {
        Node.Gap = gap.ToPoints() >= 0 ? gap : throw new ArgumentOutOfRangeException(nameof(gap), gap, "The gap shall not be negative.");
        return this;
    }

    /// <summary>Sets the space above the columns.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ColumnsElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Flow.Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the columns.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public ColumnsElement SpaceAfter(Length space)
    {
        Node.Local = (Node.Local ?? Flow.Style.Empty) with { SpaceAfter = space };
        return this;
    }

    /// <summary>Keeps the columns on one page when they fit on one.</summary>
    /// <returns>This builder.</returns>
    public ColumnsElement KeepTogether()
    {
        Node.KeepTogether = true;
        return this;
    }

    /// <summary>Keeps the columns on the same page as the element after them.</summary>
    /// <returns>This builder.</returns>
    public ColumnsElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Applies a partial style that every column's content inherits (its text properties; space before and after are not inherited).</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public ColumnsElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        ColumnsNode node = Node;
        node.Scope = (node.Scope ?? Flow.Style.Empty).Merge(style);
        return this;
    }
}

/// <summary>A drawing's modifiers in the document builder: its placement, spacing and caption.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.5 (<c>Figure</c>) and §14.8.4.4 (<c>Caption</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct DrawingElement
{
    private readonly BuildContext? _context;
    private readonly DrawingNode? _drawing;

    internal DrawingElement(BuildContext context, DrawingNode drawing)
    {
        _context = context;
        _drawing = drawing;
    }

    private DrawingNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _drawing!;
        }
    }

    /// <summary>Scales the drawing to the width of the space it is in, keeping its aspect ratio; the callback's box is the scaled size.</summary>
    /// <returns>This builder.</returns>
    public DrawingElement FillWidth()
    {
        Node.FillWidth = true;
        return this;
    }

    /// <summary>Places the drawing at the left edge (the default).</summary>
    /// <returns>This builder.</returns>
    public DrawingElement AlignLeft() => Align(TextAlignment.Left);

    /// <summary>Centers the drawing.</summary>
    /// <returns>This builder.</returns>
    public DrawingElement AlignCenter() => Align(TextAlignment.Center);

    /// <summary>Places the drawing at the right edge.</summary>
    /// <returns>This builder.</returns>
    public DrawingElement AlignRight() => Align(TextAlignment.Right);

    /// <summary>Sets the space above the drawing.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public DrawingElement SpaceBefore(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceBefore = space };
        return this;
    }

    /// <summary>Sets the space below the drawing.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public DrawingElement SpaceAfter(Length space)
    {
        Node.Local = (Node.Local ?? Style.Empty) with { SpaceAfter = space };
        return this;
    }

    /// <summary>Keeps the drawing (and its caption) on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public DrawingElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Adds a caption below the drawing in the theme's caption style, tagged as the figure's <c>Caption</c>; the drawing stays on its caption's page.</summary>
    /// <param name="caption">The caption.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="caption"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4, Table 368.</remarks>
    public DrawingElement Caption(string caption)
    {
        ArgumentException.ThrowIfNullOrEmpty(caption);
        Node.Caption = caption;
        return this;
    }

    /// <summary>Names the element, so links and page references can point at it.</summary>
    /// <param name="id">The name, unique in the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §12.3.2.4 (named destinations, PDF 1.2).</remarks>
    public DrawingElement Id(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Node.Id = id;
        return this;
    }

    private DrawingElement Align(TextAlignment alignment)
    {
        Node.Alignment = alignment;
        return this;
    }
}
