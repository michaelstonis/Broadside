using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>A table of a layout tree: its rows are its <see cref="LayoutNodeKind.TableRow"/> children, their cells <see cref="LayoutTableCell"/>s.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.4.8.3, Table 371. The row groups <c>THead</c>, <c>TBody</c> and <c>TFoot</c> make no node: their rows are the
/// table's children, in order. A <see cref="LayoutNodeKind.Caption"/> may be its first or last child.
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
}
