using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// One cell of a <see cref="TableRow"/>: blocks (paragraphs, lists, images, tables) laid out inside its padding, spanning
/// <see cref="ColumnSpan"/> columns and <see cref="RowSpan"/> rows. Tagged as <c>TH</c> when in a header row or given a
/// <see cref="Scope"/>, otherwise <c>TD</c>.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3, Table 371 (<c>TH</c>, <c>TD</c>) and §14.8.5.7, Table 384 (<c>RowSpan</c>, <c>ColSpan</c>, <c>Scope</c>, <c>Headers</c>).</remarks>
public sealed class TableCell
{
    /// <summary>Creates a cell holding <paramref name="blocks"/> in order.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> or one of them is null.</exception>
    public TableCell(params IEnumerable<FlowBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        FlowBlock[] array = [.. blocks];
        foreach (FlowBlock block in array)
        {
            ArgumentNullException.ThrowIfNull(block, nameof(blocks));
        }

        Blocks = array;
    }

    /// <summary>Creates a cell holding one paragraph of <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TableCell(string text, ParagraphStyle style)
        : this(new Paragraph(text, style))
    {
    }

    /// <summary>The cell's blocks, top to bottom.</summary>
    public IReadOnlyList<FlowBlock> Blocks { get; }

    /// <summary>The number of columns the cell spans; 1 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int ColumnSpan
    {
        get;
        init => field = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A cell spans at least one column.");
    } = 1;

    /// <summary>The number of rows the cell spans; 1 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int RowSpan
    {
        get;
        init => field = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A cell spans at least one row.");
    } = 1;

    /// <summary>The cell's background, or null for its row's (then the table style's).</summary>
    public PdfColor? Background { get; init; }

    /// <summary>The cell's padding, or null for the table style's.</summary>
    public CellPadding? Padding { get; init; }

    /// <summary>The cell's border, or null for the table style's.</summary>
    public TableBorder? Border { get; init; }

    /// <summary>The cells this header cell heads; <see cref="TableHeaderScope.None"/> (the default) for a data cell outside header rows.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined scope.</exception>
    public TableHeaderScope Scope
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown scope.");
    }
}

/// <summary>One row of a <see cref="Table"/>: its cells left to right, skipping columns that cells of rows above span into.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3, Table 371 (<c>TR</c>).</remarks>
public sealed class TableRow
{
    /// <summary>Creates a row of <paramref name="cells"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> or one of them is null.</exception>
    public TableRow(params IEnumerable<TableCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        TableCell[] array = [.. cells];
        foreach (TableCell cell in array)
        {
            ArgumentNullException.ThrowIfNull(cell, nameof(cells));
        }

        Cells = array;
    }

    /// <summary>The row's cells, left to right.</summary>
    public IReadOnlyList<TableCell> Cells { get; }

    /// <summary>The least height of the row in points; 0 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double MinHeight
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>True to keep the row on one page, false to let it split, null (the default) for <see cref="TableStyle.KeepRowsTogether"/>.</summary>
    public bool? KeepTogether { get; init; }

    /// <summary>The background of the row's cells that set none, or null for the table style's.</summary>
    public PdfColor? Background { get; init; }
}

/// <summary>
/// A table: columns sized fixed, by percentage, proportionally or to their content; cells with padding, borders and backgrounds,
/// spanning rows and columns; header rows repeated at the top of every page; rows split across pages or kept together. Rows joined
/// by a row span stay on one page.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4.8.3, Table 371: the block is <c>Table</c>, header rows are in <c>THead</c> and the others in <c>TBody</c>,
/// rows are <c>TR</c>, cells <c>TH</c> or <c>TD</c>; §14.8.5.7, Table 384 (<c>RowSpan</c>, <c>ColSpan</c>, <c>Scope</c>,
/// <c>Headers</c>); §14.8.5.4.2 (<c>Width</c>, <c>Padding</c>, <c>BorderThickness</c>, <c>BorderColor</c>, <c>BackgroundColor</c>).
/// </remarks>
public sealed partial class Table : FlowBlock
{
    private readonly List<TableRow> _rows = [];

    /// <summary>Creates an empty table with structure type <c>Table</c> and the given <paramref name="columns"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="columns"/> is empty.</exception>
    public Table(params IEnumerable<ColumnWidth> columns)
        : base("Table")
    {
        ArgumentNullException.ThrowIfNull(columns);
        ColumnWidth[] array = [.. columns];
        if (array.Length == 0)
        {
            throw new ArgumentException("A table has at least one column.", nameof(columns));
        }

        Columns = array;
    }

    /// <summary>The column widths, left to right.</summary>
    public IReadOnlyList<ColumnWidth> Columns { get; }

    /// <summary>The rows, top to bottom.</summary>
    public IReadOnlyList<TableRow> Rows => _rows;

    /// <summary>The number of leading rows that are header rows (tagged <c>THead</c>, repeated on each page); 0 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int HeaderRows
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The header row count shall not be negative.");
    }

    /// <summary>The table's style; <see cref="TableStyle.Default"/> by default.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public TableStyle Style
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = TableStyle.Default;

    /// <summary>Space above the table, dropped at the top of a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>Space below the table.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>Appends <paramref name="row"/>; returns this table.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is null.</exception>
    public Table Add(TableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows.Add(row);
        return this;
    }

    /// <summary>Appends a row of <paramref name="cells"/>; returns this table.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> or one of them is null.</exception>
    public Table AddRow(params IEnumerable<TableCell> cells) => Add(new TableRow(cells));
}
