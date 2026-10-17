using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>A table of a layout tree: its rows are its <see cref="LayoutNodeKind.TableRow"/> children, their cells <see cref="LayoutTableCell"/>s.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.4.8.3, Table 371. The row groups <c>THead</c>, <c>TBody</c> and <c>TFoot</c> make no node: their rows are the
/// table's children, in order. A <see cref="LayoutNodeKind.Caption"/> may be its first or last child. A table detected from a page's
/// geometry (ruling lines, or aligned white space) has the same shape: a row per grid row, each cell in the row it starts in, spans
/// and header cells as found, and a cell for every grid position no cell covers.
/// </remarks>
public sealed class LayoutTable : LayoutNode
{
    internal LayoutTable(PdfStructureElement? element)
        : base(LayoutNodeKind.Table, element)
    {
    }

    /// <summary>Gets the number of rows of the table's grid: its rows, and more when a cell's row span reaches past the last.</summary>
    /// <remarks>ISO 32000-2 Table 384 (<c>RowSpan</c>).</remarks>
    public int RowCount { get; internal set; }

    /// <summary>Gets the number of columns of the table's grid: the most any row reaches, column spans included.</summary>
    /// <remarks>ISO 32000-2 Table 384 (<c>ColSpan</c>).</remarks>
    public int ColumnCount { get; internal set; }

    /// <summary>
    /// Gets the part of the same table on the page before, when this table, detected from a page's geometry, continues one that ends
    /// that page; <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §14.8.4.8.3 (Table 371): a table broken across pages is one <c>Table</c> element. Detection reads each page on its
    /// own, so the parts are separate tables linked here: a table that ends a page and one that starts the next with the same columns.
    /// Tables of a structure tree are never split, and have no parts.
    /// </remarks>
    public LayoutTable? PreviousPart { get; internal set; }

    /// <summary>Gets the part of the same table on the next page, when one continues this table there; <see langword="null"/> otherwise.</summary>
    /// <remarks>ISO 32000-2 §14.8.4.8.3 (Table 371); see <see cref="PreviousPart"/>.</remarks>
    public LayoutTable? NextPart { get; internal set; }
}
