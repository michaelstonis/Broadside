using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>How a <see cref="ColumnWidth"/> sizes its column.</summary>
/// <remarks>ISO 32000-2 §14.8.5.4.2 (the <c>Width</c> layout attribute): the widths a layout chooses.</remarks>
public enum ColumnWidthKind
{
    /// <summary>Sized to its content: its widest unbreakable content at least, its unwrapped content when there is room.</summary>
    Auto,

    /// <summary>A fixed width in points.</summary>
    Fixed,

    /// <summary>A percentage of the width available to the table.</summary>
    Percent,

    /// <summary>A share, by weight, of the width left after the other columns.</summary>
    Proportional,
}

/// <summary>The width of one column of a <see cref="Table"/>: fixed, a percentage, a proportional share, or content-sized.</summary>
/// <remarks>ISO 32000-2 §14.8.5.4.2 (the <c>Width</c> layout attribute).</remarks>
public readonly record struct ColumnWidth
{
    private ColumnWidth(ColumnWidthKind kind, double value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>A content-sized column (the default value).</summary>
    public static ColumnWidth Auto => default;

    /// <summary>How the column is sized.</summary>
    public ColumnWidthKind Kind { get; }

    /// <summary>Points for <see cref="ColumnWidthKind.Fixed"/>, percent for <see cref="ColumnWidthKind.Percent"/>, the weight for <see cref="ColumnWidthKind.Proportional"/>, 0 for <see cref="ColumnWidthKind.Auto"/>.</summary>
    public double Value { get; }

    /// <summary>A column <paramref name="points"/> wide.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="points"/> is not a positive finite number.</exception>
    public static ColumnWidth Fixed(double points) => new(ColumnWidthKind.Fixed, ImageBlock.Positive(points, nameof(points)));

    /// <summary>A column <paramref name="percent"/> percent of the table's available width wide.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is not in (0, 100].</exception>
    public static ColumnWidth Percent(double percent) =>
        percent is > 0 and <= 100 ? new(ColumnWidthKind.Percent, percent) : throw new ArgumentOutOfRangeException(nameof(percent), percent, "The percentage shall be in (0, 100].");

    /// <summary>A column taking a share, by <paramref name="weight"/>, of the width the other columns leave.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is not a positive finite number.</exception>
    public static ColumnWidth Proportional(double weight = 1) => new(ColumnWidthKind.Proportional, ImageBlock.Positive(weight, nameof(weight)));
}

/// <summary>The space between a cell's border and its content, per side, in points.</summary>
/// <remarks>ISO 32000-2 §14.8.5.4.2 (the <c>Padding</c> layout attribute).</remarks>
public readonly record struct CellPadding
{
    /// <summary>The same padding on all four sides.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="all"/> is negative or not finite.</exception>
    public CellPadding(double all)
        : this(all, all, all, all)
    {
    }

    /// <summary>Padding per side.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative or not finite.</exception>
    public CellPadding(double top, double right, double bottom, double left)
    {
        Top = ImageBlock.NotNegative(top, nameof(top));
        Right = ImageBlock.NotNegative(right, nameof(right));
        Bottom = ImageBlock.NotNegative(bottom, nameof(bottom));
        Left = ImageBlock.NotNegative(left, nameof(left));
    }

    /// <summary>The padding above the content.</summary>
    public double Top { get; }

    /// <summary>The padding right of the content.</summary>
    public double Right { get; }

    /// <summary>The padding below the content.</summary>
    public double Bottom { get; }

    /// <summary>The padding left of the content.</summary>
    public double Left { get; }
}

/// <summary>The border stroked around a cell: its line width and colour. A width of 0 draws no border.</summary>
/// <remarks>ISO 32000-2 §14.8.5.4.2 (the <c>BorderThickness</c> and <c>BorderColor</c> layout attributes).</remarks>
public sealed record TableBorder
{
    /// <summary>A border <paramref name="width"/> points wide in <paramref name="color"/> (null for black).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative or not finite.</exception>
    public TableBorder(double width, PdfColor? color = null)
    {
        Width = ImageBlock.NotNegative(width, nameof(width));
        Color = color;
    }

    /// <summary>No border.</summary>
    public static TableBorder None { get; } = new(0);

    /// <summary>The line width in points.</summary>
    public double Width { get; }

    /// <summary>The stroke colour, or null for black.</summary>
    public PdfColor? Color { get; }
}

/// <summary>
/// The look of a <see cref="Table"/>: default cell padding and border, backgrounds, whether rows may split across pages, and whether
/// header rows repeat at the top of every page. Immutable; the <c>With*</c> methods return changed copies.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.5.4.2 (the <c>Padding</c>, <c>BorderThickness</c>, <c>BorderColor</c> and <c>BackgroundColor</c> layout attributes).</remarks>
public sealed class TableStyle
{
    private TableStyle()
    {
    }

    private TableStyle(TableStyle other)
    {
        Padding = other.Padding;
        Border = other.Border;
        Background = other.Background;
        HeaderBackground = other.HeaderBackground;
        KeepRowsTogether = other.KeepRowsTogether;
        RepeatHeaderRows = other.RepeatHeaderRows;
    }

    /// <summary>Padding 4 points, a 0.5 point black border, no backgrounds, rows split across pages, header rows repeated.</summary>
    public static TableStyle Default { get; } = new();

    /// <summary>The padding of cells that set none.</summary>
    public CellPadding Padding { get; private init; } = new(4);

    /// <summary>The border of cells that set none.</summary>
    public TableBorder Border { get; private init; } = new(0.5);

    /// <summary>The background of body cells that set none (and whose row sets none), or null for none.</summary>
    public PdfColor? Background { get; private init; }

    /// <summary>The background of header-row cells that set none (and whose row sets none), or null for <see cref="Background"/>.</summary>
    public PdfColor? HeaderBackground { get; private init; }

    /// <summary>True to move a row that does not fit to the next page whole instead of splitting it (a row taller than a page still splits).</summary>
    public bool KeepRowsTogether { get; private init; }

    /// <summary>True to draw the header rows again at the top of every page the table continues on.</summary>
    public bool RepeatHeaderRows { get; private init; } = true;

    /// <summary>Returns a copy with default cell padding <paramref name="padding"/>.</summary>
    public TableStyle WithPadding(CellPadding padding) => new(this) { Padding = padding };

    /// <summary>Returns a copy with default cell border <paramref name="border"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="border"/> is null.</exception>
    public TableStyle WithBorder(TableBorder border)
    {
        ArgumentNullException.ThrowIfNull(border);
        return new(this) { Border = border };
    }

    /// <summary>Returns a copy with body background <paramref name="color"/> (null for none).</summary>
    public TableStyle WithBackground(PdfColor? color) => new(this) { Background = color };

    /// <summary>Returns a copy with header-row background <paramref name="color"/> (null for the body background).</summary>
    public TableStyle WithHeaderBackground(PdfColor? color) => new(this) { HeaderBackground = color };

    /// <summary>Returns a copy that keeps rows together (true) or splits them across pages (false).</summary>
    public TableStyle WithKeepRowsTogether(bool keep) => new(this) { KeepRowsTogether = keep };

    /// <summary>Returns a copy that repeats header rows on every page (true) or draws them once (false).</summary>
    public TableStyle WithRepeatHeaderRows(bool repeat) => new(this) { RepeatHeaderRows = repeat };
}

/// <summary>Which cells a header cell is the header of, for tagged output.</summary>
/// <remarks>ISO 32000-2 §14.8.5.7, Table 384 (the <c>Scope</c> attribute of <c>TH</c>: <c>Row</c>, <c>Column</c> or <c>Both</c>).</remarks>
public enum TableHeaderScope
{
    /// <summary>Not a header cell, unless it is in a header row (then <see cref="Column"/>).</summary>
    None,

    /// <summary>A header of the cells in its rows.</summary>
    Row,

    /// <summary>A header of the cells in its columns.</summary>
    Column,

    /// <summary>A header of the cells in its rows and its columns.</summary>
    Both,
}
