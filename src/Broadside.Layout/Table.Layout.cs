using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// Table layout: cells are placed on a grid (spans skip occupied slots), columns sized from their cells' content widths, rows sized
/// from their cells' measured content, and the rows drawn group by group (rows joined by row spans form one group). A row that does
/// not fit splits across pages, each cell's content laid out on virtual pages of a region context and drawn one fragment per page.
/// </summary>
public sealed partial class Table
{
    /// <summary>Slack for rounding when a row ends exactly at the bottom margin.</summary>
    private const double Tolerance = 1e-9;

    /// <summary>The height of a virtual page that never breaks.</summary>
    private const double Unbounded = 1e9;

    /// <summary>The deepest nesting of tables (through cells and list items) accepted.</summary>
    private const int MaxDepth = 64;

    [ThreadStatic]
    private static int _depth;

    /// <summary>The cells that head <paramref name="cell"/> (for the <c>Headers</c> attribute of tagged output), top to bottom then left to right.</summary>
    /// <remarks>ISO 32000-2 §14.8.5.7, Table 384: column headers above the cell in its columns, row headers left of it in its rows.</remarks>
    internal IReadOnlyList<TableCell> HeadersOf(TableCell cell)
    {
        Placement[] cells = Place(-1);
        int index = Array.FindIndex(cells, placement => ReferenceEquals(placement.Cell, cell));
        if (index < 0)
        {
            return [];
        }

        Placement target = cells[index];
        var headers = new List<TableCell>();
        foreach (Placement header in cells)
        {
            TableHeaderScope scope = ScopeOf(header);
            bool heads = scope switch
            {
                TableHeaderScope.Column => ColumnHeads(header, target),
                TableHeaderScope.Row => RowHeads(header, target),
                TableHeaderScope.Both => ColumnHeads(header, target) || RowHeads(header, target),
                _ => false,
            };
            if (heads && !ReferenceEquals(header.Cell, cell))
            {
                headers.Add(header.Cell);
            }
        }

        return headers;

        static bool ColumnHeads(Placement header, Placement target) =>
            header.Row < target.Row && header.Column < target.Column + target.Cell.ColumnSpan && target.Column < header.Column + header.Cell.ColumnSpan;

        static bool RowHeads(Placement header, Placement target) =>
            header.Column < target.Column && header.Row < target.Row + target.Cell.RowSpan && target.Row < header.Row + header.Cell.RowSpan;
    }

    /// <summary>The scope of <paramref name="cell"/> as tagged: its own, or <see cref="TableHeaderScope.Column"/> in a header row.</summary>
    internal TableHeaderScope ScopeOf(TableCell cell)
    {
        foreach (Placement placement in Place(-1))
        {
            if (ReferenceEquals(placement.Cell, cell))
            {
                return ScopeOf(placement);
            }
        }

        return cell.Scope;
    }

    internal override void Validate(int index)
    {
        _depth++;
        try
        {
            if (_depth > MaxDepth)
            {
                throw new ArgumentException($"The table of block {index} nests more than {MaxDepth} levels deep (or contains itself).");
            }

            if (HeaderRows > _rows.Count)
            {
                throw new ArgumentException($"The table of block {index} has {HeaderRows} header rows but only {_rows.Count} rows.");
            }

            Place(index);
            foreach (TableRow row in _rows)
            {
                foreach (TableCell cell in row.Cells)
                {
                    foreach (FlowBlock block in cell.Blocks)
                    {
                        block.Validate(index);
                    }
                }
            }
        }
        finally
        {
            _depth--;
        }
    }

    internal override ContentWidths MeasureWidths(FlowServices services)
    {
        (double[] min, double[] max) = ColumnContent(Place(-1), services);
        double sumMin = 0;
        double sumMax = 0;
        for (int column = 0; column < Columns.Count; column++)
        {
            ColumnWidth width = Columns[column];
            sumMin += width.Kind == ColumnWidthKind.Fixed ? width.Value : min[column];
            sumMax += width.Kind == ColumnWidthKind.Fixed ? width.Value : max[column];
        }

        return new ContentWidths(sumMin, sumMax);
    }

    internal override void LayOut(FlowContext context)
    {
        context.EnsureRoom(0);
        context.AddSpace(SpaceBefore);
        Grid grid = Prepare(context);
        int headerEnd = Math.Min(HeaderRows, _rows.Count);
        double header = grid.Height(0, headerEnd);
        double first = headerEnd < _rows.Count ? grid.Height(headerEnd, grid.GroupEnd[headerEnd]) : 0;
        var state = new PageState(header, headerEnd, !context.AtPageTop);
        if (!context.AtPageTop && !FitsHere(context, grid, state, first))
        {
            // Not even the header and the first line of the first row fit: start on the next page, header and all.
            context.BreakPage();
            state.CanBreak = false;
        }

        context.PlaceMarkersAtTop();
        context.BeginBlock(this);
        if (headerEnd > 0)
        {
            context.BeginPart(new FlowPart("THead", this, null));
            DrawRows(context, grid, 0, headerEnd, tagged: true);
            context.EndPart();
        }

        if (headerEnd < _rows.Count)
        {
            context.BeginPart(new FlowPart("TBody", this, null));
            LayOutBody(context, grid, state);
            context.EndPart();
        }

        context.EndBlock();
        context.AddSpace(SpaceAfter);
    }

    private static double Span(double[] values, int start, int count)
    {
        double sum = 0;
        for (int index = start; index < start + count; index++)
        {
            sum += values[index];
        }

        return sum;
    }

    private static CellResult LayOutCell(FlowContext context, TableCell cell, CellPadding padding, double x, double width, double top, double[] heights, int drawPage)
    {
        double left = x + padding.Left;
        double right = Math.Max(left, x + width - padding.Right);
        using var region = new FlowContext(context, left, right, top - padding.Top, heights, drawPage);
        foreach (FlowBlock block in cell.Blocks)
        {
            block.LayOut(region);
        }

        return new CellResult(region.PageCount, region.Top - region.CursorY, region.OverflowedFirstPage);
    }

    private TableHeaderScope ScopeOf(Placement placement) =>
        placement.Cell.Scope == TableHeaderScope.None && placement.Row < HeaderRows ? TableHeaderScope.Column : placement.Cell.Scope;

    private CellPadding PaddingOf(TableCell cell) => cell.Padding ?? Style.Padding;

    private void LayOutBody(FlowContext context, Grid grid, PageState state)
    {
        int row = state.HeaderEnd;
        while (row < _rows.Count)
        {
            int end = grid.GroupEnd[row];
            double height = grid.Height(row, end);
            if (height <= context.CursorY - context.Bottom + Tolerance)
            {
                DrawRows(context, grid, row, end, tagged: true);
                state.BodyOnPage = true;
                row = end;
                continue;
            }

            bool breakable = state.BodyOnPage || state.CanBreak;
            bool keep = end > row + 1 || (_rows[row].KeepTogether ?? Style.KeepRowsTogether);
            if (keep && breakable && height <= FreshHeight(context, state) + Tolerance)
            {
                NewPage(context, grid, state);
                continue;
            }

            if (end > row + 1)
            {
                // Rows joined by a row span and taller than a page: from the top of a page, overflowing its bottom.
                if (breakable)
                {
                    NewPage(context, grid, state);
                    continue;
                }

                DrawRows(context, grid, row, end, tagged: true);
                state.BodyOnPage = true;
                row = end;
                continue;
            }

            if (SplitRow(context, grid, row, breakable, state))
            {
                state.BodyOnPage = true;
                row++;
            }
            else
            {
                NewPage(context, grid, state);
            }
        }
    }

    /// <summary>True when the header rows and the first body rows (whole, or their first lines when the row may split) fit below the cursor.</summary>
    private bool FitsHere(FlowContext context, Grid grid, PageState state, double first)
    {
        double room = context.CursorY - context.Bottom;
        if (state.HeaderHeight + first <= room + Tolerance)
        {
            return true;
        }

        int row = state.HeaderEnd;
        if (row >= _rows.Count)
        {
            return false;
        }

        bool keep = grid.GroupEnd[row] > row + 1 || (_rows[row].KeepTogether ?? Style.KeepRowsTogether);
        return (!keep || first > FreshHeight(context, state) + Tolerance) && grid.GroupEnd[row] == row + 1
            && PlanSplit(context, grid, row, room - state.HeaderHeight, FreshHeight(context, state), requireFirstLines: true) is not null;
    }

    private double FreshHeight(FlowContext context, PageState state) =>
        context.NextPageHeight - (Style.RepeatHeaderRows ? state.HeaderHeight : 0);

    /// <summary>Breaks the page and draws the header rows again, as a pagination artifact.</summary>
    private void NewPage(FlowContext context, Grid grid, PageState state)
    {
        context.BreakPage();
        state.BodyOnPage = false;
        state.CanBreak = false;
        if (Style.RepeatHeaderRows && state.HeaderEnd > 0)
        {
            context.BeginPart(new FlowPart("Artifact", this, null));
            DrawRows(context, grid, 0, state.HeaderEnd, tagged: false);
            context.EndPart();
        }
    }

    /// <summary>Draws rows <paramref name="first"/> to <paramref name="end"/> whole at the cursor and moves past them.</summary>
    private void DrawRows(FlowContext context, Grid grid, int first, int end, bool tagged)
    {
        double top = context.CursorY;
        double left = context.Left;
        var boxes = new List<Box>();
        double offset = 0;
        for (int row = first; row < end; row++)
        {
            foreach (Placement placement in grid.RowCells[row])
            {
                boxes.Add(new Box(
                    placement,
                    left + grid.Offsets[placement.Column],
                    top - offset,
                    Span(grid.Widths, placement.Column, placement.Cell.ColumnSpan),
                    Span(grid.Heights, row, placement.Cell.RowSpan)));
            }

            offset += grid.Heights[row];
        }

        Paint(context, boxes, borders: false, tagged);
        int next = 0;
        for (int row = first; row < end; row++)
        {
            if (tagged)
            {
                context.BeginPart(new FlowPart("TR", _rows[row], null));
            }

            for (; next < boxes.Count && boxes[next].Placement.Row == row; next++)
            {
                Box box = boxes[next];
                TableCell cell = box.Placement.Cell;
                if (tagged)
                {
                    context.BeginPart(new FlowPart(ScopeOf(box.Placement) == TableHeaderScope.None ? "TD" : "TH", cell, null));
                }

                LayOutCell(context, cell, PaddingOf(cell), box.X, box.Width, box.Top, [Unbounded], 0);
                if (tagged)
                {
                    context.EndPart();
                }
            }

            if (tagged)
            {
                context.EndPart();
            }
        }

        Paint(context, boxes, borders: true, tagged);
        context.Advance(offset);
    }

    /// <summary>
    /// Splits a row across pages: each cell's content is measured on virtual pages as tall as the room on this page and on fresh
    /// pages, then drawn one fragment per page. Returns false (nothing drawn) when a cell's first line does not fit here and the
    /// page may break first.
    /// </summary>
    private bool SplitRow(FlowContext context, Grid grid, int row, bool breakable, PageState state)
    {
        if (PlanSplit(context, grid, row, context.CursorY - context.Bottom, FreshHeight(context, state), breakable) is not { } plan)
        {
            return false;
        }

        List<Placement> cells = grid.RowCells[row];
        (double[][] heights, int[] pages, double[] last, int fragments) = plan;
        for (int fragment = 0; fragment < fragments; fragment++)
        {
            if (fragment > 0)
            {
                NewPage(context, grid, state);
            }

            double top = context.CursorY;
            double height = 0;
            if (fragment < fragments - 1)
            {
                height = top - context.Bottom;
            }
            else
            {
                for (int index = 0; index < cells.Count; index++)
                {
                    CellPadding padding = PaddingOf(cells[index].Cell);
                    height = Math.Max(height, padding.Top + padding.Bottom + (pages[index] - 1 == fragment ? last[index] : 0));
                }
            }

            var boxes = new List<Box>(cells.Count);
            foreach (Placement placement in cells)
            {
                boxes.Add(new Box(placement, context.Left + grid.Offsets[placement.Column], top, Span(grid.Widths, placement.Column, placement.Cell.ColumnSpan), height));
            }

            Paint(context, boxes, borders: false, tagged: true);
            context.BeginPart(new FlowPart("TR", _rows[row], null));
            for (int index = 0; index < boxes.Count; index++)
            {
                Box box = boxes[index];
                TableCell cell = box.Placement.Cell;
                context.BeginPart(new FlowPart(ScopeOf(box.Placement) == TableHeaderScope.None ? "TD" : "TH", cell, null));
                LayOutCell(context, cell, PaddingOf(cell), box.X, box.Width, top, heights[index], fragment);
                context.EndPart();
            }

            context.EndPart();
            Paint(context, boxes, borders: true, tagged: true);
            context.Advance(height);
        }

        return true;
    }

    /// <summary>
    /// Measures each cell of <paramref name="row"/> on virtual pages <paramref name="room"/> tall and then <paramref name="fresh"/>
    /// tall (less its padding); null when <paramref name="requireFirstLines"/> and a cell's first line does not fit in the room.
    /// </summary>
    private SplitPlan? PlanSplit(FlowContext context, Grid grid, int row, double room, double fresh, bool requireFirstLines)
    {
        List<Placement> cells = grid.RowCells[row];
        var heights = new double[cells.Count][];
        var pages = new int[cells.Count];
        var last = new double[cells.Count];
        int fragments = 1;
        for (int index = 0; index < cells.Count; index++)
        {
            Placement placement = cells[index];
            CellPadding padding = PaddingOf(placement.Cell);
            double vertical = padding.Top + padding.Bottom;
            if (room - vertical <= Tolerance && requireFirstLines)
            {
                return null;
            }

            heights[index] = [Math.Max(room - vertical, 0), Math.Max(fresh - vertical, 0)];
            double width = Span(grid.Widths, placement.Column, placement.Cell.ColumnSpan);
            CellResult measured = LayOutCell(context, placement.Cell, padding, 0, width, 0, heights[index], -1);
            if (measured.OverflowedFirstPage && requireFirstLines)
            {
                return null;
            }

            pages[index] = measured.Pages;
            last[index] = measured.Used;
            fragments = Math.Max(fragments, pages[index]);
        }

        return new SplitPlan(heights, pages, last, fragments);
    }

    /// <summary>Fills the cell backgrounds or strokes the cell borders of <paramref name="boxes"/>, as an artifact when tagged.</summary>
    private void Paint(FlowContext context, List<Box> boxes, bool borders, bool tagged)
    {
        if (!context.Drawing)
        {
            return;
        }

        PdfCanvas? canvas = null;
        foreach (Box box in boxes)
        {
            TableCell cell = box.Placement.Cell;
            TableRow row = _rows[box.Placement.Row];
            if (borders)
            {
                TableBorder border = cell.Border ?? Style.Border;
                if (border.Width <= 0)
                {
                    continue;
                }

                canvas ??= Begin();
                canvas.SetLineWidth(border.Width);
                canvas.SetStrokeColor(border.Color ?? PdfColor.FromGray(0));
                canvas.Rectangle(box.X, box.Top - box.Height, box.Width, box.Height);
                canvas.Stroke();
            }
            else
            {
                if ((cell.Background ?? row.Background ?? (box.Placement.Row < HeaderRows ? Style.HeaderBackground ?? Style.Background : Style.Background)) is not { } color)
                {
                    continue;
                }

                canvas ??= Begin();
                canvas.SetFillColor(color);
                canvas.Rectangle(box.X, box.Top - box.Height, box.Width, box.Height);
                canvas.Fill();
            }
        }

        if (canvas is not null)
        {
            canvas.RestoreState();
            if (tagged)
            {
                context.EndPart();
            }
        }

        PdfCanvas Begin()
        {
            if (tagged)
            {
                context.BeginPart(new FlowPart("Artifact", this, null));
            }

            PdfCanvas opened = context.Canvas;
            opened.SaveState();
            return opened;
        }
    }

    /// <summary>Places the cells on the grid; throws <see cref="ArgumentException"/> naming block <paramref name="index"/> when they do not fit.</summary>
    private Placement[] Place(int index)
    {
        int columns = Columns.Count;
        var occupied = new bool[_rows.Count][];
        for (int row = 0; row < occupied.Length; row++)
        {
            occupied[row] = new bool[columns];
        }

        var placed = new List<Placement>();
        for (int row = 0; row < _rows.Count; row++)
        {
            int column = 0;
            IReadOnlyList<TableCell> cells = _rows[row].Cells;
            for (int position = 0; position < cells.Count; position++)
            {
                TableCell cell = cells[position];
                while (column < columns && occupied[row][column])
                {
                    column++;
                }

                string where = $"Cell {position} of row {row} of the table of block {index}";
                if (column + cell.ColumnSpan > columns)
                {
                    throw new ArgumentException($"{where} does not fit in the table's {columns} columns.");
                }

                if (row + cell.RowSpan > _rows.Count)
                {
                    throw new ArgumentException($"{where} spans past the last row.");
                }

                if (row < HeaderRows && row + cell.RowSpan > HeaderRows)
                {
                    throw new ArgumentException($"{where} spans from the header rows into the body.");
                }

                for (int spanned = row; spanned < row + cell.RowSpan; spanned++)
                {
                    for (int across = column; across < column + cell.ColumnSpan; across++)
                    {
                        if (occupied[spanned][across])
                        {
                            throw new ArgumentException($"{where} overlaps a cell spanning into it.");
                        }

                        occupied[spanned][across] = true;
                    }
                }

                placed.Add(new Placement(cell, row, column));
                column += cell.ColumnSpan;
            }
        }

        return [.. placed];
    }

    /// <summary>The least and preferred widths of each column, padding included, from the cells' content.</summary>
    private (double[] Min, double[] Max) ColumnContent(Placement[] cells, FlowServices services)
    {
        int columns = Columns.Count;
        var min = new double[columns];
        var max = new double[columns];
        var widths = new ContentWidths[cells.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            TableCell cell = cells[index].Cell;
            CellPadding padding = PaddingOf(cell);
            double cellMin = 0;
            double cellMax = 0;
            foreach (FlowBlock block in cell.Blocks)
            {
                ContentWidths content = block.MeasureWidths(services);
                cellMin = Math.Max(cellMin, content.Min);
                cellMax = Math.Max(cellMax, content.Max);
            }

            widths[index] = new ContentWidths(cellMin + padding.Left + padding.Right, cellMax + padding.Left + padding.Right);
            if (cell.ColumnSpan == 1)
            {
                int column = cells[index].Column;
                min[column] = Math.Max(min[column], widths[index].Min);
                max[column] = Math.Max(max[column], widths[index].Max);
            }
        }

        // Spanning cells widen the columns they span evenly when those are narrower than the cell's content.
        for (int index = 0; index < cells.Length; index++)
        {
            Placement placement = cells[index];
            int span = placement.Cell.ColumnSpan;
            if (span == 1)
            {
                continue;
            }

            double spannedMin = Span(min, placement.Column, span);
            double spannedMax = Span(max, placement.Column, span);
            for (int column = placement.Column; column < placement.Column + span; column++)
            {
                min[column] += Math.Max(0, widths[index].Min - spannedMin) / span;
                max[column] += Math.Max(0, widths[index].Max - spannedMax) / span;
            }
        }

        for (int column = 0; column < columns; column++)
        {
            max[column] = Math.Max(max[column], min[column]);
        }

        return (min, max);
    }

    /// <summary>Sizes the columns to <paramref name="context"/>'s width and the rows to their measured content.</summary>
    private Grid Prepare(FlowContext context)
    {
        Placement[] cells = Place(-1);
        (double[] min, double[] max) = ColumnContent(cells, context.Services);
        int columns = Columns.Count;
        double available = context.Width;
        var widths = new double[columns];
        double rest = available;
        double autoMin = 0;
        double autoMax = 0;
        double weights = 0;
        for (int column = 0; column < columns; column++)
        {
            ColumnWidth width = Columns[column];
            switch (width.Kind)
            {
                case ColumnWidthKind.Fixed:
                    widths[column] = width.Value;
                    rest -= width.Value;
                    break;
                case ColumnWidthKind.Percent:
                    widths[column] = available * width.Value / 100;
                    rest -= widths[column];
                    break;
                case ColumnWidthKind.Proportional:
                    weights += width.Value;
                    break;
                default:
                    autoMin += min[column];
                    autoMax += max[column];
                    break;
            }
        }

        for (int column = 0; column < columns; column++)
        {
            if (Columns[column].Kind != ColumnWidthKind.Auto)
            {
                continue;
            }

            // Preferred widths when they fit, the widest unbreakable content at least, and in between proportionally.
            widths[column] = autoMax <= rest ? max[column]
                : autoMin < rest ? min[column] + ((max[column] - min[column]) * (rest - autoMin) / (autoMax - autoMin))
                : min[column];
        }

        rest = Math.Max(0, rest - (autoMax <= rest ? autoMax : Math.Max(autoMin, rest)));
        for (int column = 0; column < columns; column++)
        {
            if (Columns[column].Kind == ColumnWidthKind.Proportional)
            {
                widths[column] = rest * Columns[column].Value / weights;
            }
        }

        var offsets = new double[columns + 1];
        for (int column = 0; column < columns; column++)
        {
            offsets[column + 1] = offsets[column] + widths[column];
        }

        var rowCells = new List<Placement>[_rows.Count];
        var heights = new double[_rows.Count];
        for (int row = 0; row < _rows.Count; row++)
        {
            rowCells[row] = [];
            heights[row] = _rows[row].MinHeight;
        }

        var content = new double[cells.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            Placement placement = cells[index];
            rowCells[placement.Row].Add(placement);
            CellPadding padding = PaddingOf(placement.Cell);
            CellResult measured = LayOutCell(context, placement.Cell, padding, 0, Span(widths, placement.Column, placement.Cell.ColumnSpan), 0, [Unbounded], -1);
            content[index] = measured.Used + padding.Top + padding.Bottom;
            if (placement.Cell.RowSpan == 1)
            {
                heights[placement.Row] = Math.Max(heights[placement.Row], content[index]);
            }
        }

        // A cell spanning rows taller than them grows the last row it spans.
        for (int index = 0; index < cells.Length; index++)
        {
            Placement placement = cells[index];
            int span = placement.Cell.RowSpan;
            double deficit = content[index] - Span(heights, placement.Row, span);
            if (span > 1 && deficit > 0)
            {
                heights[placement.Row + span - 1] += deficit;
            }
        }

        var groupEnd = new int[_rows.Count];
        for (int start = 0; start < _rows.Count;)
        {
            int end = start + 1;
            for (int row = start; row < end; row++)
            {
                foreach (Placement placement in rowCells[row])
                {
                    end = Math.Max(end, row + placement.Cell.RowSpan);
                }
            }

            for (int row = start; row < end; row++)
            {
                groupEnd[row] = end;
            }

            start = end;
        }

        return new Grid(widths, offsets, heights, rowCells, groupEnd);
    }

    /// <summary>What laying out a cell's content took: virtual pages, the height used on the last, and whether the first overflowed.</summary>
    private readonly record struct CellResult(int Pages, double Used, bool OverflowedFirstPage);

    /// <summary>A row's split: each cell's virtual page heights, pages used and height used on its last page, and the fragment count.</summary>
    private sealed record SplitPlan(double[][] Heights, int[] Pages, double[] Last, int Fragments);

    /// <summary>A cell at its grid position.</summary>
    private readonly record struct Placement(TableCell Cell, int Row, int Column);

    /// <summary>A cell's box on the page: left edge, top edge, width and height.</summary>
    private readonly record struct Box(Placement Placement, double X, double Top, double Width, double Height);

    /// <summary>The sized table: column widths and left offsets, row heights, cells by starting row, and where each row's group ends.</summary>
    private sealed record Grid(double[] Widths, double[] Offsets, double[] Heights, List<Placement>[] RowCells, int[] GroupEnd)
    {
        public double Height(int first, int end) => Span(Heights, first, end - first);
    }

    /// <summary>The table's progress on the current page.</summary>
    private sealed class PageState(double headerHeight, int headerEnd, bool canBreak)
    {
        public double HeaderHeight { get; } = headerHeight;

        public int HeaderEnd { get; } = headerEnd;

        /// <summary>True when the table did not start this page, so breaking before its first body row gains room.</summary>
        public bool CanBreak { get; set; } = canBreak;

        /// <summary>True once a body row is on this page.</summary>
        public bool BodyOnPage { get; set; }
    }
}
