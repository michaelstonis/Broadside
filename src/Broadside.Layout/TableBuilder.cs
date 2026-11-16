using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The columns and rows of a table in the document builder. <see cref="Header"/> rows repeat on every page and are tagged <c>TH</c> with
/// <c>Column</c> scope; <see cref="RowBuilder.HeaderCell(string)"/> makes a row header (<c>TH</c> with <c>Row</c> scope).
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3 (<c>Table</c>, <c>THead</c>, <c>TBody</c>, <c>TR</c>, <c>TH</c>, <c>TD</c>) and §14.8.5.7, Table 384.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct TableBuilder
{
    private readonly BuildContext? _context;
    private readonly TableNode? _table;

    internal TableBuilder(BuildContext context, TableNode table)
    {
        _context = context;
        _table = table;
    }

    private TableNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _table!;
        }
    }

    /// <summary>
    /// Sets the columns: fixed widths (a <see cref="Length"/> converts), percentages, proportional shares or content-sized. Without it, the
    /// table has as many equal proportional columns as its widest row covers.
    /// </summary>
    /// <param name="columns">The column widths.</param>
    /// <exception cref="ArgumentException"><paramref name="columns"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A row already added covers more columns.</exception>
    public void Columns(params ReadOnlySpan<ColumnWidth> columns)
    {
        if (columns.IsEmpty)
        {
            throw new ArgumentException("A table shall have at least one column.", nameof(columns));
        }

        TableNode table = Node;
        foreach (RowNode row in (IEnumerable<RowNode>)[.. table.HeaderRows, .. table.BodyRows])
        {
            if (row.Span > columns.Length)
            {
                throw new InvalidOperationException($"A row already added covers {row.Span} columns; the table cannot have {columns.Length}.");
            }
        }

        table.Columns = columns.ToArray();
    }

    /// <summary>Adds a header row: repeated at the top of every page the table continues on, its cells tagged <c>TH</c> with <c>Column</c> scope.</summary>
    /// <param name="build">Describes the row's cells.</param>
    /// <returns>The row's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A body row was already added.</exception>
    public RowElement Header(Action<RowBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        TableNode table = Node;
        if (table.BodyRows.Count > 0)
        {
            throw new InvalidOperationException("Header rows come before the body rows.");
        }

        var row = new RowNode { Cells = [] };
        table.HeaderRows.Add(row);
        build(new RowBuilder(_context!, table, row, header: true));
        return new RowElement(_context!, row);
    }

    /// <summary>Adds a body row.</summary>
    /// <param name="build">Describes the row's cells.</param>
    /// <returns>The row's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public RowElement Row(Action<RowBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        TableNode table = Node;
        var row = new RowNode { Cells = [] };
        table.BodyRows.Add(row);
        build(new RowBuilder(_context!, table, row, header: false));
        return new RowElement(_context!, row);
    }

    /// <summary>Adds a body row with a state argument, so a <see langword="static"/> lambda allocates no closure in a loop.</summary>
    /// <typeparam name="TState">The state's type.</typeparam>
    /// <param name="state">The state passed to <paramref name="build"/>.</param>
    /// <param name="build">Describes the row's cells from the state.</param>
    /// <returns>The row's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public RowElement Row<TState>(TState state, Action<RowBuilder, TState> build)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(build);
        TableNode table = Node;
        var row = new RowNode { Cells = [] };
        table.BodyRows.Add(row);
        build(new RowBuilder(_context!, table, row, header: false), state);
        return new RowElement(_context!, row);
    }

    /// <summary>Adds a body row of plain text cells, one per string: the fast path for large tables, with no closure.</summary>
    /// <param name="cells">The cells' text.</param>
    /// <exception cref="ArgumentException"><paramref name="cells"/> is empty, holds a null string, or covers more columns than the table has.</exception>
    public void Row(params ReadOnlySpan<string> cells)
    {
        if (cells.IsEmpty)
        {
            throw new ArgumentException("A row shall have at least one cell.", nameof(cells));
        }

        TableNode table = Node;
        if (table.Columns is { } columns && cells.Length > columns.Length)
        {
            throw new ArgumentException($"The row covers {cells.Length} columns; the table has {columns.Length}.", nameof(cells));
        }

        List<string> plain = _context!.PlainCells;
        int start = plain.Count;
        foreach (string cell in cells)
        {
            plain.Add(cell ?? throw new ArgumentException("A cell's text shall not be null.", nameof(cells)));
        }

        table.BodyRows.Add(new RowNode { PlainStart = start, PlainCount = cells.Length });
    }
}

/// <summary>The cells of a table row in the document builder.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3 (<c>TH</c>, <c>TD</c>) and §14.8.5.7, Table 384 (<c>RowSpan</c>, <c>ColSpan</c>, <c>Scope</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct RowBuilder
{
    private readonly BuildContext? _context;
    private readonly TableNode? _table;
    private readonly RowNode? _row;
    private readonly bool _header;

    internal RowBuilder(BuildContext context, TableNode table, RowNode row, bool header)
    {
        _context = context;
        _table = table;
        _row = row;
        _header = header;
    }

    /// <summary>Adds a cell of plain text: a data cell (<c>TD</c>), or a column header (<c>TH</c>) in a header row.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The cell's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public CellElement Cell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Add(new CellNode { Text = text, Header = _header });
    }

    /// <summary>Adds a cell of any content: paragraphs, lists, images, nested tables.</summary>
    /// <param name="build">Describes the cell's content.</param>
    /// <returns>The cell's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public CellElement Cell(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var cell = new CellNode { Content = new ContainerNode(), Header = _header };
        CellElement element = Add(cell);
        build(new ContainerBuilder(_context!, cell.Content));
        return element;
    }

    /// <summary>Adds a header cell of plain text (<c>TH</c>), in the theme's table header style: a row header in a body row.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The cell's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public CellElement HeaderCell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Add(new CellNode { Text = text, Header = true });
    }

    /// <summary>Adds a header cell of any content (<c>TH</c>).</summary>
    /// <param name="build">Describes the cell's content.</param>
    /// <returns>The cell's builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    public CellElement HeaderCell(Action<ContainerBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var cell = new CellNode { Content = new ContainerNode(), Header = true };
        CellElement element = Add(cell);
        build(new ContainerBuilder(_context!, cell.Content));
        return element;
    }

    private CellElement Add(CellNode cell)
    {
        BuildContext context = BuildContext.Check(_context);
        if (_table!.Columns is { } columns && _row!.Span + 1 > columns.Length)
        {
            throw new InvalidOperationException($"The row already covers its table's {columns.Length} columns.");
        }

        _row!.Cells!.Add(cell);
        return new CellElement(context, _table, _row, cell);
    }
}

/// <summary>A table's modifiers in the document builder: its caption, striping, borders, padding and the style its cells inherit.</summary>
/// <remarks>The table's style starts from the theme's <see cref="Theme.Table"/>. ISO 32000-2 §14.8.4.8.3 and §14.8.4.4 (<c>Caption</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct TableElement
{
    private readonly BuildContext? _context;
    private readonly TableNode? _table;

    internal TableElement(BuildContext context, TableNode table)
    {
        _context = context;
        _table = table;
    }

    private TableNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _table!;
        }
    }

    /// <summary>Adds a caption above the table in the theme's caption style, tagged as the table's <c>Caption</c>.</summary>
    /// <param name="caption">The caption.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="caption"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.4, Table 368.</remarks>
    public TableElement Caption(string caption)
    {
        ArgumentException.ThrowIfNullOrEmpty(caption);
        Node.Caption = caption;
        return this;
    }

    /// <summary>Sets the table's width, which its columns share instead of the width of the space it is in.</summary>
    /// <param name="width">The width; one wider than the space is narrowed to it.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    public TableElement Width(Length width)
    {
        Node.Width = width.ToPoints() > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width), width, "The width shall be positive.");
        return this;
    }

    /// <summary>Places a table narrower than the space it is in at the left edge (the default).</summary>
    /// <returns>This builder.</returns>
    public TableElement AlignLeft() => Align(TextAlignment.Left);

    /// <summary>Centers a table narrower than the space it is in.</summary>
    /// <returns>This builder.</returns>
    public TableElement AlignCenter() => Align(TextAlignment.Center);

    /// <summary>Places a table narrower than the space it is in at the right edge, such as the totals of an invoice.</summary>
    /// <returns>This builder.</returns>
    public TableElement AlignRight() => Align(TextAlignment.Right);

    /// <summary>Fills alternate body rows (the second, fourth, ...) with the theme's <see cref="Theme.Subtle"/> color.</summary>
    /// <param name="striped">False to turn striping off.</param>
    /// <returns>This builder.</returns>
    public TableElement Striped(bool striped = true)
    {
        Node.Striped = striped;
        return this;
    }

    /// <summary>Sets the border of every cell.</summary>
    /// <param name="width">The line width; 0 draws none.</param>
    /// <param name="color">The color, or null for black.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative.</exception>
    public TableElement Border(Length width, PdfColor? color = null) => Border(new TableBorder(width.ToPoints(), color));

    /// <summary>Sets the border of every cell, such as <see cref="TableBorder.None"/>.</summary>
    /// <param name="border">The border.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="border"/> is null.</exception>
    public TableElement Border(TableBorder border)
    {
        ArgumentNullException.ThrowIfNull(border);
        Node.Border = border;
        return this;
    }

    /// <summary>Sets the padding of every cell.</summary>
    /// <param name="padding">The padding on each side.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="padding"/> is negative.</exception>
    public TableElement Padding(Length padding)
    {
        Node.Padding = padding.ToPoints() >= 0 ? padding : throw new ArgumentOutOfRangeException(nameof(padding), padding, "The padding shall not be negative.");
        return this;
    }

    /// <summary>Sets the background of the header rows.</summary>
    /// <param name="color">The color, or null for none.</param>
    /// <returns>This builder.</returns>
    public TableElement HeaderBackground(PdfColor? color)
    {
        Node.HeaderBackground = color;
        Node.HasHeaderBackground = true;
        return this;
    }

    /// <summary>Sets whether the header rows repeat on every page the table continues on; they do by default.</summary>
    /// <param name="repeat">Whether to repeat them.</param>
    /// <returns>This builder.</returns>
    public TableElement RepeatHeaderRows(bool repeat)
    {
        Node.RepeatHeaderRows = repeat;
        return this;
    }

    /// <summary>Keeps each row on one page when it fits on one (a row taller than a page is still split).</summary>
    /// <param name="keep">Whether to keep rows together.</param>
    /// <returns>This builder.</returns>
    public TableElement KeepRowsTogether(bool keep = true)
    {
        Node.KeepRowsTogether = keep;
        return this;
    }

    /// <summary>Keeps the whole table on one page when it fits on one.</summary>
    /// <returns>This builder.</returns>
    public TableElement KeepTogether()
    {
        Node.KeepTogether = true;
        return this;
    }

    /// <summary>Keeps the table on the same page as the element after it.</summary>
    /// <returns>This builder.</returns>
    public TableElement KeepWithNext()
    {
        Node.KeepWithNext = true;
        return this;
    }

    /// <summary>Sets the space above the table.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public TableElement SpaceBefore(Length space)
    {
        Node.SpaceBefore = space.ToPoints() >= 0 ? space : throw new ArgumentOutOfRangeException(nameof(space), space, "The space shall not be negative.");
        return this;
    }

    /// <summary>Sets the space below the table.</summary>
    /// <param name="space">The space.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative.</exception>
    public TableElement SpaceAfter(Length space)
    {
        Node.SpaceAfter = space.ToPoints() >= 0 ? space : throw new ArgumentOutOfRangeException(nameof(space), space, "The space shall not be negative.");
        return this;
    }

    /// <summary>Sets the font size of the table's text.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public TableElement FontSize(double size)
    {
        Node.Scope = (_table!.Scope ?? Flow.Style.Empty) with { FontSize = size };
        return this;
    }

    /// <summary>Sets the table's natural language, written as <c>Lang</c> when it differs from its parent's.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public TableElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Node.Scope = (_table!.Scope ?? Flow.Style.Empty) with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style that the table's cells inherit.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public TableElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Node.Scope = (_table!.Scope ?? Flow.Style.Empty).Merge(style);
        return this;
    }

    /// <summary>Names the element, so links and page references can point at it.</summary>
    /// <param name="id">The name, unique in the document.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §12.3.2.4 (named destinations, PDF 1.2).</remarks>
    public TableElement Id(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Node.Id = id;
        return this;
    }

    private TableElement Align(TextAlignment alignment)
    {
        Node.Alignment = alignment;
        return this;
    }
}

/// <summary>A row's modifiers in the document builder: its background, pagination and the style its cells inherit.</summary>
/// <remarks>ISO 32000-2 §14.8.4: a row is tagged <c>TR</c>.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct RowElement
{
    private readonly BuildContext? _context;
    private readonly RowNode? _row;

    internal RowElement(BuildContext context, RowNode row)
    {
        _context = context;
        _row = row;
    }

    private RowNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _row!;
        }
    }

    /// <summary>Sets the background of the row's cells.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public RowElement Background(PdfColor color)
    {
        Node.Background = color;
        return this;
    }

    /// <summary>Keeps the row on one page when it fits on one, or lets it split.</summary>
    /// <param name="keep">Whether to keep the row together.</param>
    /// <returns>This builder.</returns>
    public RowElement KeepTogether(bool keep = true)
    {
        Node.KeepTogether = keep;
        return this;
    }

    /// <summary>Sets the least height of the row.</summary>
    /// <param name="height">The height.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is negative.</exception>
    public RowElement MinHeight(Length height)
    {
        Node.MinHeight = height.ToPoints() >= 0 ? height : throw new ArgumentOutOfRangeException(nameof(height), height, "The height shall not be negative.");
        return this;
    }

    /// <summary>Uses the bold face for the row's text.</summary>
    /// <returns>This builder.</returns>
    public RowElement Bold()
    {
        Node.Scope = (_row!.Scope ?? Flow.Style.Empty) with { Bold = true };
        return this;
    }

    /// <summary>Sets the color of the row's text.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public RowElement Color(PdfColor color)
    {
        Node.Scope = (_row!.Scope ?? Flow.Style.Empty) with { Color = color };
        return this;
    }

    /// <summary>Applies a partial style that the row's cells inherit.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public RowElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Node.Scope = (_row!.Scope ?? Flow.Style.Empty).Merge(style);
        return this;
    }
}

/// <summary>A cell's modifiers in the document builder: spans, background, padding, alignment and the style its content inherits.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8 (<c>TD</c> and <c>TH</c>) and §14.8.5.7, Table 384 (<c>RowSpan</c>, <c>ColSpan</c>).</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public readonly ref struct CellElement
{
    private readonly BuildContext? _context;
    private readonly TableNode? _table;
    private readonly RowNode? _row;
    private readonly CellNode? _cell;

    internal CellElement(BuildContext context, TableNode table, RowNode row, CellNode cell)
    {
        _context = context;
        _table = table;
        _row = row;
        _cell = cell;
    }

    private CellNode Node
    {
        get
        {
            _ = BuildContext.Check(_context);
            return _cell!;
        }
    }

    private Style Scope
    {
        get => Node.Scope ?? Flow.Style.Empty;
        set => _cell!.Scope = value;
    }

    /// <summary>Makes the cell span <paramref name="columns"/> columns.</summary>
    /// <param name="columns">The number of columns, at least 1.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> is less than 1, or the row would cover more columns than the table has.</exception>
    public CellElement ColumnSpan(int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        CellNode cell = Node;
        if (_table!.Columns is { } widths && _row!.Span - cell.ColumnSpan + columns > widths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), columns, $"The row would cover more than its table's {widths.Length} columns.");
        }

        cell.ColumnSpan = columns;
        return this;
    }

    /// <summary>Makes the cell span <paramref name="rows"/> rows.</summary>
    /// <param name="rows">The number of rows, at least 1.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is less than 1.</exception>
    public CellElement RowSpan(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        Node.RowSpan = rows;
        return this;
    }

    /// <summary>Sets the cell's background.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public CellElement Background(PdfColor color)
    {
        Node.Background = color;
        return this;
    }

    /// <summary>Sets the cell's padding on each side.</summary>
    /// <param name="padding">The padding.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="padding"/> is negative.</exception>
    public CellElement Padding(Length padding)
    {
        Node.Padding = padding.ToPoints() >= 0 ? padding : throw new ArgumentOutOfRangeException(nameof(padding), padding, "The padding shall not be negative.");
        return this;
    }

    /// <summary>Aligns the cell's lines to the left edge.</summary>
    /// <returns>This builder.</returns>
    public CellElement AlignLeft() => Align(TextAlignment.Left);

    /// <summary>Centers the cell's lines.</summary>
    /// <returns>This builder.</returns>
    public CellElement AlignCenter() => Align(TextAlignment.Center);

    /// <summary>Aligns the cell's lines to the right edge.</summary>
    /// <returns>This builder.</returns>
    public CellElement AlignRight() => Align(TextAlignment.Right);

    /// <summary>Uses the bold face for the cell's text.</summary>
    /// <returns>This builder.</returns>
    public CellElement Bold()
    {
        Scope = Scope with { Bold = true };
        return this;
    }

    /// <summary>Uses the italic face for the cell's text.</summary>
    /// <returns>This builder.</returns>
    public CellElement Italic()
    {
        Scope = Scope with { Italic = true };
        return this;
    }

    /// <summary>Sets the font size of the cell's text.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public CellElement FontSize(double size)
    {
        Scope = Scope with { FontSize = size };
        return this;
    }

    /// <summary>Sets the font family of the cell's text.</summary>
    /// <param name="family">The family.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public CellElement Font(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Scope = Scope with { FontFamily = family };
        return this;
    }

    /// <summary>Sets the color of the cell's text.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern.</exception>
    public CellElement Color(PdfColor color)
    {
        Scope = Scope with { Color = color };
        return this;
    }

    /// <summary>Sets the cell's natural language, written as <c>Lang</c> on its content when it differs from the table's.</summary>
    /// <param name="language">A BCP 47 language tag.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is null or empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public CellElement Language(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        Scope = Scope with { Language = language };
        return this;
    }

    /// <summary>Applies a partial style that the cell's content inherits.</summary>
    /// <param name="style">The style.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public CellElement Style(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Scope = Scope.Merge(style);
        return this;
    }

    private CellElement Align(TextAlignment alignment)
    {
        Scope = Scope with { Alignment = alignment };
        return this;
    }
}
