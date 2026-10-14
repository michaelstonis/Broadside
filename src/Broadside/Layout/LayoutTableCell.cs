using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>A cell of a table: a header or data cell, with its place in the table's grid, its spans and its header cells.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3 (Table 371, <c>TH</c> and <c>TD</c>) and §14.8.5.7 (Table 384, <c>RowSpan</c>, <c>ColSpan</c>, <c>Headers</c>).</remarks>
public sealed class LayoutTableCell : LayoutNode
{
    private static readonly LayoutTableCell[] NoHeaders = [];
    private List<LayoutTableCell>? _headers;

    internal LayoutTableCell(PdfStructureElement? element, bool isHeader, int rowSpan, int columnSpan)
        : base(LayoutNodeKind.TableCell, element)
    {
        IsHeader = isHeader;
        RowSpan = rowSpan;
        ColumnSpan = columnSpan;
    }

    /// <summary>Gets a value indicating whether the cell is a header cell (<c>TH</c>) rather than a data cell (<c>TD</c>).</summary>
    /// <remarks>ISO 32000-2 Table 371.</remarks>
    public bool IsHeader { get; }

    /// <summary>Gets the number of rows the cell spans, at least 1 and at most the rows left in the table.</summary>
    /// <remarks>ISO 32000-2 Table 384 (<c>RowSpan</c>, default 1).</remarks>
    public int RowSpan { get; internal set; }

    /// <summary>Gets the number of columns the cell spans, at least 1.</summary>
    /// <remarks>ISO 32000-2 Table 384 (<c>ColSpan</c>, default 1).</remarks>
    public int ColumnSpan { get; }

    /// <summary>
    /// Gets the index of the grid row the cell starts in, counting the table's rows from 0; -1 when the cell is not in a row of a table.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.4.8.3: rows in order; a cell above whose row span reaches this row takes its place.</remarks>
    public int Row { get; internal set; } = -1;

    /// <summary>
    /// Gets the index of the grid column the cell starts in, from 0: the first column of its row that no cell before it, in its row
    /// or spanning down from a row above, occupies; -1 when the cell is not in a row of a table.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 384 (<c>RowSpan</c>, <c>ColSpan</c>).</remarks>
    public int Column { get; internal set; } = -1;

    /// <summary>Gets the header cells the cell's <c>Headers</c> attribute names by their element IDs, in the order given; empty when it names none.</summary>
    /// <remarks>
    /// ISO 32000-2 Table 384 (<c>Headers</c>) and §14.8.4.8.3. IDs that name no header cell of the tree are skipped. Headers found by
    /// the implicit algorithm of §14.8.4.8.3 (from <c>Scope</c> and position) are not added.
    /// </remarks>
    public IReadOnlyList<LayoutTableCell> Headers => (IReadOnlyList<LayoutTableCell>?)_headers ?? NoHeaders;

    internal void AddHeader(LayoutTableCell header) => (_headers ??= []).Add(header);

    internal void KeepHeaders(Predicate<LayoutTableCell> keep) => _headers?.RemoveAll(header => !keep(header));
}
