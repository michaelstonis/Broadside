using System.Runtime.InteropServices;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>Tables among the detected content (spec story 36, issue #189).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4.8.3 (table elements, Table 371) and §14.8.5.7 (<c>RowSpan</c>, <c>ColSpan</c>, <c>Headers</c>, Table 384) are what
/// this part recovers. A <em>lattice</em> table is read from ruling lines (<see cref="RulingCollector"/>): the lines are merged where they
/// continue each other (within <see cref="RuleSnap"/>), grouped into sets of lines that cross, and a set with at least two distinct
/// horizontal and two distinct vertical positions is a grid. Neighbouring grid cells with no line between them (covering at least half
/// of their shared side) are one cell, spanning rows or columns. A grid is a table when at least <see cref="MinFilled"/> of its cells
/// hold text and it is not a frame around running text (most of its text in cells of more than <see cref="MaxCellFragments"/> fragments);
/// its text is every fragment whose centre lies inside it, and a line splits a fragment it crosses.
/// </para>
/// <para>
/// A <em>stream</em> table is read from aligned white space, before a region is cut into columns and in runs of rows of a block that
/// have several fragments: the region has at least <see cref="MinTableRows"/> rows, most of them with several fragments; the columns are
/// the white space at least <see cref="ColumnGap"/> wide that crosses every row having the most common number of fragments; a fragment
/// over several columns spans them. It is a table unless most columns hold running text (cells of <see cref="ProseWords"/> words or
/// more), a third of its rows are one cell across the table, or it has fewer than three columns and no column of numbers
/// (<see cref="NumericShare"/> of its cells). A row with an empty first column whose cells all sit under cells of the row above
/// continues that row.
/// </para>
/// <para>
/// Header rows are the top rows, while each has a cell spanning columns or is set in bold over a body that is not; failing that, the
/// first row when it holds no number over a column of numbers, or (unruled) when a ruling separates it from the second. Every data
/// cell's <see cref="LayoutTableCell.Headers"/> are the filled header cells above it in its columns. A table that ends a page and one that
/// starts the next with the same columns (within <see cref="ContinuedAlign"/>) are parts of one table
/// (<see cref="LayoutTable.PreviousPart"/>, <see cref="LayoutTable.NextPart"/>).
/// </para>
/// </remarks>
internal sealed partial class GeometryLayout
{
    /// <summary>How close, in <c>h</c>, rulings are to be one line, or to meet.</summary>
    internal const double RuleSnap = 0.3;

    /// <summary>The fewest rows of a table found from white space.</summary>
    internal const int MinTableRows = 3;

    /// <summary>The narrowest white space, in <c>h</c>, between the columns of a table found from white space.</summary>
    internal const double ColumnGap = 0.5;

    /// <summary>The median number of words of a column's cells from which it holds running text.</summary>
    internal const int ProseWords = 4;

    /// <summary>The share of a column's cells that are numbers for it to be a column of numbers.</summary>
    internal const double NumericShare = 0.6;

    /// <summary>The smallest share of a ruled grid's cells that hold text.</summary>
    internal const double MinFilled = 0.25;

    /// <summary>The most fragments a ruled cell holds before it is a box of running text.</summary>
    internal const int MaxCellFragments = 6;

    /// <summary>How far, in <c>h</c>, the columns of a table continued on the next page may sit from those of its first part.</summary>
    internal const double ContinuedAlign = 2;

    /// <summary>The most merged rulings in each direction a page's grids are looked for in.</summary>
    private const int MaxLatticeLines = 1024;

    /// <summary>The most cells of a grid.</summary>
    private const int MaxGridCells = 16384;

    private readonly List<Ruling> _rulesH = [];
    private readonly List<Ruling> _rulesV = [];
    private readonly List<Ruling> _componentH = [];
    private readonly List<Ruling> _componentV = [];
    private readonly List<double> _xs = [];
    private readonly List<double> _ys = [];
    private readonly List<(double Low, double High)> _intervals = [];
    private readonly List<(double Low, double High)> _tableGutters = [];
    private readonly List<(int Start, int Count)> _tableRows = [];
    private readonly List<StreamCell> _streamCells = [];
    private readonly List<(int Item, int Cell)> _claimed = [];
    private readonly List<int> _pieces = [];
    private readonly List<int> _splits = [];
    private readonly List<Fragment> _pending = [];
    private int[] _parents = [];
    private int[] _counts = [];
    private int[] _grid = [];
    private bool[] _edges = [];
    private int _fragmentCount;
    private List<TablePlan>? _tables;
    private Analysis? _current;

    /// <summary>Whether a vertical ruling crosses the white space between <paramref name="from"/> and <paramref name="to"/> at height <paramref name="y"/>.</summary>
    private static bool CrossesRule(Ruling[] vertical, double from, double to, double y)
    {
        int low = 0;
        int high = vertical.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (vertical[middle].Position <= from)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (int index = low; index < vertical.Length && vertical[index].Position < to; index++)
        {
            if (vertical[index].Low <= y && vertical[index].High >= y)
            {
                return true;
            }
        }

        return false;
    }

    private static Ruling[] Sorted(List<Ruling>? rulings)
    {
        if (rulings is null || rulings.Count == 0)
        {
            return [];
        }

        rulings.Sort(static (x, y) =>
        {
            int order = x.Position.CompareTo(y.Position);
            if (order == 0)
            {
                order = x.Low.CompareTo(y.Low);
            }

            return order != 0 ? order : x.High.CompareTo(y.High);
        });
        return [.. rulings];
    }

    private static int Find(Span<int> parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    /// <summary>Whether lines at <paramref name="position"/> cover at least half of <c>[low, high]</c>.</summary>
    private static bool Covered(List<Ruling> lines, double position, double low, double high, double snap)
    {
        double covered = 0;
        foreach (Ruling line in lines)
        {
            if (Math.Abs(line.Position - position) <= snap)
            {
                covered += Math.Max(0, Math.Min(high, line.High) - Math.Max(low, line.Low));
            }
        }

        return covered >= 0.5 * (high - low);
    }

    /// <summary>Distinct positions of <paramref name="lines"/>, ascending, those within <paramref name="snap"/> of the one before taken as it.</summary>
    private static void Positions(List<Ruling> lines, List<double> positions, double snap)
    {
        positions.Clear();
        foreach (Ruling line in lines)
        {
            positions.Add(line.Position);
        }

        positions.Sort();
        int kept = 0;
        for (int index = 0; index < positions.Count; index++)
        {
            if (kept == 0 || positions[index] - positions[kept - 1] > snap)
            {
                positions[kept++] = positions[index];
            }
        }

        positions.RemoveRange(kept, positions.Count - kept);
    }

    /// <summary>Removes grid rows and columns no cell starts in (every cell there spans in from before), so each row and column has a cell.</summary>
    private static void Compact(TablePlan table)
    {
        Span<int> rowMap = table.Rows <= 256 ? stackalloc int[table.Rows + 1] : new int[table.Rows + 1];
        Span<int> columnMap = table.Columns <= 256 ? stackalloc int[table.Columns + 1] : new int[table.Columns + 1];
        rowMap.Clear();
        columnMap.Clear();
        foreach (CellPlan cell in table.Cells)
        {
            rowMap[cell.Row] = 1;
            columnMap[cell.Column] = 1;
        }

        int rows = Prefix(rowMap[..table.Rows]);
        int columns = Prefix(columnMap[..table.Columns]);
        rowMap[table.Rows] = rows;
        columnMap[table.Columns] = columns;
        if (rows == table.Rows && columns == table.Columns)
        {
            return;
        }

        foreach (CellPlan cell in table.Cells)
        {
            int end = cell.Row + cell.RowSpan;
            int columnEnd = cell.Column + cell.ColumnSpan;
            cell.Row = rowMap[cell.Row];
            cell.Column = columnMap[cell.Column];
            cell.RowSpan = Math.Max(1, rowMap[end] - cell.Row);
            cell.ColumnSpan = Math.Max(1, columnMap[columnEnd] - cell.Column);
        }

        var edges = new double[columns];
        for (int column = 0; column < table.Columns; column++)
        {
            if (columnMap[column + 1] != columnMap[column])
            {
                edges[columnMap[column]] = table.Edges[column];
            }
        }

        table.Rows = rows;
        table.Columns = columns;
        table.Edges = edges;
    }

    /// <summary>Turns flags (1 where a row or column is kept) into new indices: each entry becomes the number of kept ones before it; returns how many are kept.</summary>
    private static int Prefix(Span<int> flags)
    {
        int kept = 0;
        for (int index = 0; index < flags.Length; index++)
        {
            int flag = flags[index];
            flags[index] = kept;
            kept += flag;
        }

        return kept;
    }

    /// <summary>Links each table ending a page to the table starting the next page when their columns agree.</summary>
    private void LinkContinuedTables()
    {
        for (int index = 1; index < _pages.Count; index++)
        {
            Analysis before = _pages[index - 1];
            Analysis after = _pages[index];
            if (after.PageIndex != before.PageIndex + 1 || Edge(before, last: true) is not { Node: { } first } ending
                || Edge(after, last: false) is not { Node: { } second } starting || ending.Columns != starting.Columns)
            {
                continue;
            }

            double align = ContinuedAlign * Math.Max(before.Height, after.Height);
            bool same = true;
            for (int column = 0; column < ending.Columns && same; column++)
            {
                same = Math.Abs(ending.Edges[column] - starting.Edges[column]) <= align;
            }

            if (same)
            {
                first.NextPart = second;
                second.PreviousPart = first;
            }
        }
    }

    /// <summary>The table a page's horizontal content ends (or starts) with, if it does.</summary>
    private static TablePlan? Edge(Analysis analysis, bool last)
    {
        Paragraph[] paragraphs = analysis.Paragraphs;
        for (int step = 0; step < paragraphs.Length; step++)
        {
            ref readonly Paragraph paragraph = ref paragraphs[last ? paragraphs.Length - 1 - step : step];
            if (!analysis.Fragments[analysis.Order[paragraph.Start]].Horizontal)
            {
                continue;
            }

            return paragraph.Table >= 0 ? analysis.Tables![paragraph.Table] : null;
        }

        return null;
    }

    /// <summary>The layout node of a detected table: its rows, their cells, and each data cell's header cells.</summary>
    private static LayoutTable TableNode(Placement placement, int index)
    {
        TablePlan table = placement.Tables![placement.Paragraphs[index].Table];
        var node = new LayoutTable(null) { RowCount = table.Rows, ColumnCount = table.Columns };
        node.AddExtent(OwnerExtent(placement, index));
        AddCaption(placement, index, node, above: true);
        var cells = new LayoutTableCell[table.Cells.Count];
        int next = 0;
        for (int row = 0; row < table.Rows; row++)
        {
            LayoutNode rowNode = LayoutNode.Create(LayoutNodeKind.TableRow, null);
            int first = next;
            PdfRectangle? bounds = null;
            while (next < table.Cells.Count && table.Cells[next].Row == row)
            {
                CellPlan cell = table.Cells[next];
                var cellNode = new LayoutTableCell(null, cell.Header, cell.RowSpan, cell.ColumnSpan) { Row = cell.Row, Column = cell.Column };
                if (cell.Bounds is { } box)
                {
                    cellNode.AddExtent(new LayoutExtent
                    {
                        Page = placement.Page,
                        Bounds = box,
                        GlyphStart = cell.GlyphStart,
                        GlyphCount = cell.GlyphCount,
                        TextStart = cell.TextStart,
                        TextLength = cell.TextLength,
                    });
                    bounds = Union(bounds, box);
                }

                rowNode.Add(cellNode);
                cells[next++] = cellNode;
            }

            if (bounds is { } rowBounds)
            {
                CellPlan head = table.Cells[first];
                CellPlan tail = table.Cells[next - 1];
                rowNode.AddExtent(new LayoutExtent
                {
                    Page = placement.Page,
                    Bounds = rowBounds,
                    GlyphStart = head.GlyphStart,
                    GlyphCount = tail.GlyphStart + tail.GlyphCount - head.GlyphStart,
                    TextStart = head.TextStart,
                    TextLength = tail.TextStart + tail.TextLength - head.TextStart,
                });
            }

            node.Add(rowNode);
        }

        AddCaption(placement, index, node, above: false);

        // A data cell's headers are the filled header cells above it in its columns, top to bottom.
        for (int position = 0; position < cells.Length; position++)
        {
            CellPlan cell = table.Cells[position];
            if (cell.Header)
            {
                continue;
            }

            for (int header = 0; header < cells.Length; header++)
            {
                CellPlan above = table.Cells[header];
                if (above.Header && above.Fragments.Count > 0 && above.Column < cell.Column + cell.ColumnSpan && cell.Column < above.Column + above.ColumnSpan)
                {
                    cells[position].AddHeader(cells[header]);
                }
            }
        }

        table.Node = node;
        return node;
    }

    /// <summary>Appends a fragment standing for a table in the cut, and makes room for it.</summary>
    private int AddProxy(TablePlan table)
    {
        _tables ??= [];
        _tables.Add(table);
        if (_fragmentCount == _fragments.Length)
        {
            Array.Resize(ref _fragments, Math.Max(4, _fragments.Length * 2));
        }

        _fragments[_fragmentCount] = new Fragment
        {
            Left = table.Left,
            Bottom = table.Bottom,
            Right = table.Right,
            Top = table.Top,
            Horizontal = true,
            BodyLeft = double.NaN,
            Table = _tables.Count,
        };
        return _fragmentCount++;
    }

    /// <summary>Starts a paragraph that is the table a proxy fragment stands for.</summary>
    private void AddTableParagraph(int proxy)
    {
        _paragraphs.Add(new Paragraph(_order.Count) { Rows = 1, Tabular = true, Table = _fragments[proxy].Table - 1 });
        _order.Add(proxy);
    }

    /// <summary>Starts a paragraph that is the table or figure a proxy fragment stands for.</summary>
    private void AddProxyParagraph(int proxy)
    {
        if (_fragments[proxy].Figure != 0)
        {
            _paragraphs.Add(new Paragraph(_order.Count) { Rows = 1, Figure = _fragments[proxy].Figure - 1 });
            _order.Add(proxy);
            return;
        }

        AddTableParagraph(proxy);
    }

    /// <summary>Finds the ruled tables of the page among <paramref name="items"/>, takes their fragments out and adds a proxy for each; returns how many items are left.</summary>
    private int Lattices(Analysis analysis, Span<int> items)
    {
        if (analysis.Horizontal.Length < 2 || analysis.Vertical.Length < 2)
        {
            return items.Length;
        }

        double snap = Math.Max(1, RuleSnap * _height);
        Merge(analysis.Horizontal, _rulesH, snap);
        Merge(analysis.Vertical, _rulesV, snap);
        int horizontal = _rulesH.Count;
        int vertical = _rulesV.Count;
        if (horizontal < 2 || vertical < 2 || horizontal > MaxLatticeLines || vertical > MaxLatticeLines)
        {
            return items.Length;
        }

        Span<int> parents = Grow(ref _parents, horizontal + vertical);
        for (int index = 0; index < parents.Length; index++)
        {
            parents[index] = index;
        }

        for (int h = 0; h < horizontal; h++)
        {
            Ruling across = _rulesH[h];
            for (int v = 0; v < vertical; v++)
            {
                Ruling down = _rulesV[v];
                if (down.Position >= across.Low - snap && down.Position <= across.High + snap && across.Position >= down.Low - snap && across.Position <= down.High + snap)
                {
                    int a = Find(parents, h);
                    int b = Find(parents, horizontal + v);
                    if (a != b)
                    {
                        parents[Math.Max(a, b)] = Math.Min(a, b);
                    }
                }
            }
        }

        int count = items.Length;
        for (int root = 0; root < horizontal; root++)
        {
            if (Find(parents, root) != root)
            {
                continue;
            }

            _componentH.Clear();
            _componentV.Clear();
            for (int h = root; h < horizontal; h++)
            {
                if (Find(parents, h) == root)
                {
                    _componentH.Add(_rulesH[h]);
                }
            }

            for (int v = 0; v < vertical; v++)
            {
                if (Find(parents, horizontal + v) == root)
                {
                    _componentV.Add(_rulesV[v]);
                }
            }

            if (_componentH.Count >= 2 && _componentV.Count >= 2)
            {
                count = Lattice(analysis, items[..count], snap);
            }
        }

        return count;
    }

    /// <summary>Merges rulings (sorted by position) that continue each other into <paramref name="merged"/>.</summary>
    private void Merge(Ruling[] rulings, List<Ruling> merged, double snap)
    {
        merged.Clear();
        for (int start = 0; start < rulings.Length;)
        {
            int end = start + 1;
            double sum = rulings[start].Position;
            while (end < rulings.Length && rulings[end].Position - rulings[end - 1].Position <= snap)
            {
                sum += rulings[end++].Position;
            }

            double position = sum / (end - start);
            _intervals.Clear();
            for (int index = start; index < end; index++)
            {
                _intervals.Add((rulings[index].Low, rulings[index].High));
            }

            _intervals.Sort();
            (double low, double high) = _intervals[0];
            for (int index = 1; index < _intervals.Count; index++)
            {
                if (_intervals[index].Low <= high + snap)
                {
                    high = Math.Max(high, _intervals[index].High);
                    continue;
                }

                merged.Add(new Ruling(position, low, high));
                (low, high) = _intervals[index];
            }

            merged.Add(new Ruling(position, low, high));
            start = end;
        }
    }

    /// <summary>Reads a grid from the current set of crossing lines and, when it is a table, claims its fragments; returns how many items are left.</summary>
    private int Lattice(Analysis analysis, Span<int> items, double snap)
    {
        Positions(_componentV, _xs, snap);
        Positions(_componentH, _ys, snap);
        _ys.Reverse();
        int columns = _xs.Count - 1;
        int rows = _ys.Count - 1;
        if (columns < 2 || rows < 2 || rows * columns > MaxGridCells)
        {
            return items.Length;
        }

        // Which sides of the grid's cells have a line: vertical sides by row, then horizontal sides by boundary.
        int verticalSides = rows * (columns + 1);
        Span<bool> sides = Grow(ref _edges, verticalSides + ((rows + 1) * columns));
        for (int row = 0; row < rows; row++)
        {
            for (int x = 0; x <= columns; x++)
            {
                sides[(row * (columns + 1)) + x] = Covered(_componentV, _xs[x], _ys[row + 1], _ys[row], snap);
            }
        }

        for (int y = 0; y <= rows; y++)
        {
            for (int column = 0; column < columns; column++)
            {
                sides[verticalSides + (y * columns) + column] = Covered(_componentH, _ys[y], _xs[column], _xs[column + 1], snap);
            }
        }

        var table = new TablePlan
        {
            Ruled = true,
            Left = _xs[0],
            Right = _xs[^1],
            Top = _ys[0],
            Bottom = _ys[^1],
            Rows = rows,
            Columns = columns,
            Edges = [.. _xs.GetRange(0, columns)],
        };
        Span<int> grid = Grow(ref _grid, rows * columns);
        grid.Fill(-1);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                if (grid[(row * columns) + column] >= 0)
                {
                    continue;
                }

                int span = 1;
                while (column + span < columns && grid[(row * columns) + column + span] < 0 && !sides[(row * (columns + 1)) + column + span])
                {
                    span++;
                }

                int down = 1;
                while (row + down < rows && Free(sides, grid, row + down, column, span, rows, columns))
                {
                    down++;
                }

                for (int r = row; r < row + down; r++)
                {
                    grid.Slice((r * columns) + column, span).Fill(table.Cells.Count);
                }

                table.Cells.Add(new CellPlan(row, column, down, span)
                {
                    Box = new PdfRectangle(_xs[column], _ys[row + down], _xs[column + span], _ys[row]),
                });
            }
        }

        // The fragments whose centres lie in the grid, by cell.
        _claimed.Clear();
        for (int position = 0; position < items.Length; position++)
        {
            ref readonly Fragment fragment = ref _fragments[items[position]];
            double x = (fragment.Left + fragment.Right) / 2;
            double y = (fragment.Bottom + fragment.Top) / 2;
            if (fragment.IsProxy || x <= table.Left || x >= table.Right || y <= table.Bottom || y >= table.Top)
            {
                continue;
            }

            int column = 0;
            while (column + 1 < columns && x >= _xs[column + 1])
            {
                column++;
            }

            int row = 0;
            while (row + 1 < rows && y <= _ys[row + 1])
            {
                row++;
            }

            _claimed.Add((position, grid[(row * columns) + column]));
        }

        // A table has text in enough of its cells, and is not a frame around running text.
        Span<int> counts = Grow(ref _counts, table.Cells.Count);
        counts.Clear();
        foreach ((_, int cell) in _claimed)
        {
            counts[cell]++;
        }

        int filled = 0;
        int crowded = 0;
        foreach (int count in counts)
        {
            filled += count > 0 ? 1 : 0;
            crowded += count > MaxCellFragments ? count : 0;
        }

        if (filled < 2 || filled < MinFilled * table.Cells.Count || crowded * 2 > _claimed.Count)
        {
            return items.Length;
        }

        foreach ((int position, int cell) in _claimed)
        {
            table.Cells[cell].Fragments.Add(items[position]);
        }

        // Take the claimed fragments out of the cut (claimed positions ascend).
        int kept = 0;
        int next = 0;
        for (int position = 0; position < items.Length; position++)
        {
            if (next < _claimed.Count && _claimed[next].Item == position)
            {
                next++;
                continue;
            }

            items[kept++] = items[position];
        }

        Compact(table);
        FinishTable(table);
        items[kept++] = AddProxy(table);
        return kept;
    }

    /// <summary>Whether a grid row can take the next row of a cell spanning <paramref name="span"/> columns from <paramref name="column"/>: no line above it there, and none between those columns.</summary>
    private static bool Free(Span<bool> sides, Span<int> grid, int row, int column, int span, int rows, int columns)
    {
        int verticalSides = rows * (columns + 1);
        for (int index = column; index < column + span; index++)
        {
            if (grid[(row * columns) + index] >= 0 || sides[verticalSides + (row * columns) + index]
                || (index > column && sides[(row * (columns + 1)) + index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads <paramref name="items"/> (a region, or a run of rows of a block) as a table found from white space; when it is one, adds
    /// it and returns its proxy fragment, else -1.
    /// </summary>
    private int StreamTable(Span<int> items)
    {
        if (items.Length < 4)
        {
            return -1;
        }

        items.Sort(new ByTop(_fragments));
        _tableRows.Clear();
        for (int index = 0; index < items.Length;)
        {
            int start = index;
            ref readonly Fragment first = ref _fragments[items[index++]];
            if (first.IsProxy)
            {
                return -1;
            }

            double top = first.Top;
            double bottom = first.Bottom;
            while (index < items.Length)
            {
                ref readonly Fragment next = ref _fragments[items[index]];
                if (next.IsProxy || Math.Min(top, next.Top) - Math.Max(bottom, next.Bottom) < 0.5 * Math.Min(top - bottom, next.Top - next.Bottom))
                {
                    break;
                }

                top = Math.Max(top, next.Top);
                bottom = Math.Min(bottom, next.Bottom);
                index++;
            }

            items[start..index].Sort(new ByLeft(_fragments));
            _tableRows.Add((start, index - start));
        }

        int rows = _tableRows.Count;
        if (rows < MinTableRows)
        {
            return -1;
        }

        // Rows with several fragments (a list marker set apart does not count), and the most common number of them.
        Span<int> counts = Grow(ref _counts, rows);
        int multi = 0;
        for (int row = 0; row < rows; row++)
        {
            (int start, int count) = _tableRows[row];
            counts[row] = _fragments[items[start]].IsMarkerOnly && count > 1 ? count - 1 : count;
            multi += counts[row] > 1 ? 1 : 0;
        }

        if (multi < 2 || multi * 2 < rows)
        {
            return -1;
        }

        int modal = Modal(counts[..rows]);

        // The columns: white space crossing every row with the common number of fragments.
        _intervals.Clear();
        for (int row = 0; row < rows; row++)
        {
            if (counts[row] == modal)
            {
                (int start, int count) = _tableRows[row];
                foreach (int item in items.Slice(start, count))
                {
                    _intervals.Add((_fragments[item].Left, _fragments[item].Right));
                }
            }
        }

        _intervals.Sort();
        _tableGutters.Clear();
        double reach = _intervals[0].High;
        for (int index = 1; index < _intervals.Count; index++)
        {
            if (_intervals[index].Low - reach >= ColumnGap * _height)
            {
                _tableGutters.Add((reach, _intervals[index].Low));
            }

            reach = Math.Max(reach, _intervals[index].High);
        }

        int columns = _tableGutters.Count + 1;
        if (columns < 2)
        {
            return -1;
        }

        // The cells: each row's fragments by the columns they reach, those reaching a column already taken merged into that cell.
        _streamCells.Clear();
        _pieces.Clear();
        int fullRows = 0;
        int wideRows = 0;
        for (int row = 0; row < rows; row++)
        {
            (int start, int count) = _tableRows[row];
            int cellsBefore = _streamCells.Count;
            for (int position = start; position < start + count; position++)
            {
                // A fragment over several columns whose words break where the columns do is one piece per column.
                int from = _pieces.Count;
                (int first, int last) = ColumnsOf(_fragments[items[position]]);
                Split(items[position], first, last);
                for (int piece = from; piece < _pieces.Count; piece++)
                {
                    (first, last) = ColumnsOf(At(_pieces[piece]));
                    if (_streamCells.Count > cellsBefore && first <= _streamCells[^1].Last)
                    {
                        StreamCell previous = _streamCells[^1];
                        _streamCells[^1] = previous with { Last = Math.Max(previous.Last, last), Count = previous.Count + 1 };
                        continue;
                    }

                    _streamCells.Add(new StreamCell(row, first, last, piece, 1));
                }
            }

            int made = _streamCells.Count - cellsBefore;
            fullRows += made == 1 && _streamCells[^1].First == 0 && _streamCells[^1].Last == columns - 1 ? 1 : 0;
            wideRows += made >= 3 ? 1 : 0;
        }

        if (fullRows * 3 > rows || !Tabular(columns, wideRows))
        {
            _pending.Clear();
            return -1;
        }

        CommitPieces();

        var table = new TablePlan { Columns = columns, Edges = new double[columns] };
        table.Edges[0] = double.PositiveInfinity;
        for (int column = 1; column < columns; column++)
        {
            table.Edges[column] = _tableGutters[column - 1].High;
        }

        // Rows: a row with an empty first column whose cells all sit under cells of the row above continues it.
        int merged = -1;
        int cell = 0;
        CellPlan? lastInRow = null;
        for (int row = 0; row < rows; row++)
        {
            int first = cell;
            while (cell < _streamCells.Count && _streamCells[cell].Row == row)
            {
                cell++;
            }

            if (merged >= 1 && _streamCells[first].First > 0 && Continues(table, merged, first, cell))
            {
                for (int index = first; index < cell; index++)
                {
                    StreamCell part = _streamCells[index];
                    CellPlan target = table.Cells.FindLast(plan => plan.Row == merged && plan.Column <= part.First && part.Last < plan.Column + plan.ColumnSpan)!;
                    Claim(target, part, table);
                }

                continue;
            }

            merged++;
            for (int index = first; index < cell; index++)
            {
                StreamCell part = _streamCells[index];
                lastInRow = new CellPlan(merged, part.First, 1, part.Last - part.First + 1);
                table.Cells.Add(lastInRow);
                Claim(lastInRow, part, table);
            }
        }

        table.Rows = merged + 1;

        // Empty positions of the grid are empty cells.
        Span<int> taken = Grow(ref _grid, table.Rows * columns);
        taken.Clear();
        foreach (CellPlan plan in table.Cells)
        {
            taken.Slice((plan.Row * columns) + plan.Column, plan.ColumnSpan).Fill(1);
        }

        for (int index = 0; index < taken.Length; index++)
        {
            if (taken[index] == 0)
            {
                table.Cells.Add(new CellPlan(index / columns, index % columns, 1, 1));
            }
        }

        table.Cells.Sort(static (x, y) => x.Row != y.Row ? x.Row.CompareTo(y.Row) : x.Column.CompareTo(y.Column));
        FinishTable(table);
        return AddProxy(table);
    }

    /// <summary>The columns of the current stream table a fragment reaches; one nearest its centre when it lies in a gutter.</summary>
    private (int First, int Last) ColumnsOf(in Fragment fragment)
    {
        int first = 0;
        while (first < _tableGutters.Count && _tableGutters[first].Low <= fragment.Left)
        {
            first++;
        }

        int last = _tableGutters.Count;
        while (last > 0 && _tableGutters[last - 1].High >= fragment.Right)
        {
            last--;
        }

        if (first > last)
        {
            // Inside a gutter: the side its centre is nearer.
            (double low, double high) = _tableGutters[last];
            first = last = (fragment.Left + fragment.Right) / 2 - low < high - ((fragment.Left + fragment.Right) / 2) ? last : last + 1;
        }

        return (first, last);
    }

    /// <summary>Whether the stream cells read as a table rather than running text in columns, a form or a list.</summary>
    private bool Tabular(int columns, int wideRows)
    {
        int prose = 0;
        bool numeric = false;
        for (int column = 0; column < columns; column++)
        {
            Span<double> lengths = Grow(ref _values, _streamCells.Count);
            int count = 0;
            int numbers = 0;
            int filled = 0;
            foreach (StreamCell cell in _streamCells)
            {
                if (cell.First != column || cell.Last != column)
                {
                    continue;
                }

                CellStats stats = Stats(Pieces(cell));
                lengths[count++] = stats.Words;
                if (cell.Row > 0)
                {
                    filled++;
                    numbers += stats.IsNumber ? 1 : 0;
                }
            }

            prose += count > 0 && Median(lengths[..count], 0) >= ProseWords ? 1 : 0;
            numeric |= filled >= 2 && numbers >= NumericShare * filled;
        }

        return prose * 2 <= columns && (numeric || (columns >= 3 && wideRows >= 2));
    }

    /// <summary>Whether the stream cells <c>[first, end)</c> of a row all sit under cells of row <paramref name="row"/> of the table.</summary>
    private bool Continues(TablePlan table, int row, int first, int end)
    {
        for (int index = first; index < end; index++)
        {
            StreamCell part = _streamCells[index];
            if (!table.Cells.Exists(plan => plan.Row == row && plan.Fragments.Count > 0 && plan.Column <= part.First && part.Last < plan.Column + plan.ColumnSpan))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Adds a stream cell's fragments to a planned cell and grows the table's bounds.</summary>
    private void Claim(CellPlan target, StreamCell part, TablePlan table)
    {
        foreach (int item in Pieces(part))
        {
            ref readonly Fragment fragment = ref _fragments[item];
            target.Fragments.Add(item);
            bool first = table.Right < table.Left;
            table.Left = first ? fragment.Left : Math.Min(table.Left, fragment.Left);
            table.Bottom = first ? fragment.Bottom : Math.Min(table.Bottom, fragment.Bottom);
            table.Right = first ? fragment.Right : Math.Max(table.Right, fragment.Right);
            table.Top = first ? fragment.Top : Math.Max(table.Top, fragment.Top);
            table.Edges[0] = Math.Min(table.Edges[0], fragment.Left);
        }
    }

    /// <summary>The fragments of a stream cell.</summary>
    private ReadOnlySpan<int> Pieces(StreamCell cell) => CollectionsMarshal.AsSpan(_pieces).Slice(cell.Start, cell.Count);

    /// <summary>
    /// Adds a fragment reaching columns <paramref name="first"/> to <paramref name="last"/> of the stream table to <see cref="_pieces"/>:
    /// as it is, or, when gaps between its words fall in the gutters it crosses, as new fragments split there (its glyphs partitioned,
    /// white space staying with the piece before).
    /// </summary>
    private void Split(int item, int first, int last)
    {
        _splits.Clear();
        if (first < last)
        {
            Fragment whole = _fragments[item];
            TextPage text = _current!.Text;
            ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
            double previous = double.NaN;
            int gutter = first;
            for (int glyph = whole.GlyphStart; glyph < whole.GlyphEnd && gutter < last; glyph++)
            {
                if (!Visible(text, glyphs[glyph]))
                {
                    continue;
                }

                PdfRectangle box = glyphs[glyph].Quad.Bounds;
                if (!double.IsNaN(previous) && box.Left - previous > 0.2 * (box.Top - box.Bottom))
                {
                    while (gutter < last && _tableGutters[gutter].High <= previous)
                    {
                        gutter++;
                    }

                    if (gutter < last && _tableGutters[gutter].Low < box.Left)
                    {
                        _splits.Add(glyph);
                        gutter++;
                    }
                }

                previous = box.Right;
            }
        }

        if (_splits.Count == 0)
        {
            _pieces.Add(item);
            return;
        }

        _splits.Add(_fragments[item].GlyphEnd);
        int start = _fragments[item].GlyphStart;
        foreach (int end in _splits)
        {
            _pieces.Add(Piece(item, start, end));
            start = end;
        }
    }

    /// <summary>Appends a fragment holding glyphs <c>[start, end)</c> of fragment <paramref name="item"/>, bounded by its visible glyphs.</summary>
    private int Piece(int item, int start, int end)
    {
        Fragment whole = _fragments[item];
        TextPage text = _current!.Text;
        ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
        double left = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        for (int glyph = start; glyph < end; glyph++)
        {
            if (Visible(text, glyphs[glyph]))
            {
                PdfRectangle box = glyphs[glyph].Quad.Bounds;
                left = Math.Min(left, box.Left);
                right = Math.Max(right, box.Right);
            }
        }

        _pending.Add(whole with
        {
            GlyphStart = start,
            GlyphEnd = end,
            Left = left <= right ? left : whole.Left,
            Right = left <= right ? right : whole.Left,
            MarkerEnd = 0,
            BodyLeft = double.NaN,
            Marker = default,
        });
        return _fragmentCount + _pending.Count - 1;
    }

    /// <summary>A fragment, or a piece split off one that is still pending (its index past the fragments in use).</summary>
    private ref readonly Fragment At(int index) =>
        ref index < _fragmentCount ? ref _fragments[index] : ref CollectionsMarshal.AsSpan(_pending)[index - _fragmentCount];

    /// <summary>Makes the pending pieces fragments, at the indices they were given.</summary>
    private void CommitPieces()
    {
        if (_fragmentCount + _pending.Count > _fragments.Length)
        {
            Array.Resize(ref _fragments, Math.Max(_fragmentCount + _pending.Count + 4, _fragments.Length * 2));
        }

        CollectionsMarshal.AsSpan(_pending).CopyTo(_fragments.AsSpan(_fragmentCount));
        _fragmentCount += _pending.Count;
        _pending.Clear();
    }

    private static bool Visible(TextPage text, in TextGlyph glyph)
    {
        foreach (char character in text.GetGlyphText(glyph))
        {
            if (!char.IsWhiteSpace(character))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The most common value, the larger on a tie.</summary>
    private static int Modal(Span<int> values)
    {
        Span<int> sorted = values.Length <= 512 ? stackalloc int[values.Length] : new int[values.Length];
        values.CopyTo(sorted);
        sorted.Sort();
        int best = sorted[0];
        int bestRun = 0;
        for (int start = 0; start < sorted.Length;)
        {
            int end = start + 1;
            while (end < sorted.Length && sorted[end] == sorted[start])
            {
                end++;
            }

            if (sorted[start] > 1 && end - start >= bestRun)
            {
                best = sorted[start];
                bestRun = end - start;
            }

            start = end;
        }

        return best;
    }

    /// <summary>What the glyphs of some fragments hold: characters, digits, letters, and visible glyphs set in bold.</summary>
    private CellStats Stats(ReadOnlySpan<int> fragments)
    {
        TextPage text = _current!.Text;
        ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan;
        CellStats stats = default;
        foreach (int item in fragments)
        {
            ref readonly Fragment fragment = ref At(item);
            double right = double.NaN;
            for (int glyph = fragment.GlyphStart; glyph < fragment.GlyphEnd; glyph++)
            {
                bool visible = false;
                PdfRectangle box = glyphs[glyph].Quad.Bounds;
                foreach (char character in text.GetGlyphText(glyphs[glyph]))
                {
                    if (char.IsWhiteSpace(character))
                    {
                        right = double.NaN;
                        continue;
                    }

                    // A word starts after white space, or after a gap of a fifth of the glyph's height.
                    if (!visible && (double.IsNaN(right) || box.Left - right > 0.2 * (box.Top - box.Bottom)))
                    {
                        stats.Words++;
                    }

                    visible = true;
                    stats.Characters++;
                    stats.Digits += char.IsDigit(character) ? 1 : 0;
                    stats.Letters += char.IsLetter(character) ? 1 : 0;
                }

                if (visible)
                {
                    right = box.Right;
                    stats.Visible++;
                    stats.Bold += IsBold(glyphs[glyph].Font) ? 1 : 0;
                }
            }
        }

        return stats;
    }

    /// <summary>Measures each cell and finds the header rows.</summary>
    private void FinishTable(TablePlan table)
    {
        foreach (CellPlan cell in table.Cells)
        {
            cell.Stats = Stats(CollectionsMarshal.AsSpan(cell.Fragments));
        }

        int headers = 0;
        while (headers < table.Rows - 1 && (SpansColumns(table, headers) || BoldOverPlain(table, headers)))
        {
            headers++;
        }

        if (headers == 0 && table.Rows > 1 && (OverNumbers(table) || (!table.Ruled && RuledOff(table))))
        {
            headers = 1;
        }

        foreach (CellPlan cell in table.Cells)
        {
            cell.Header = cell.Row < headers;
        }
    }

    private static bool SpansColumns(TablePlan table, int row) =>
        table.Cells.Exists(cell => cell.Row == row && cell.ColumnSpan > 1 && cell.Fragments.Count > 0);

    /// <summary>Whether every filled cell of a row is bold and some cell below is not.</summary>
    private static bool BoldOverPlain(TablePlan table, int row)
    {
        bool any = false;
        bool plainBelow = false;
        foreach (CellPlan cell in table.Cells)
        {
            if (cell.Stats.Visible == 0)
            {
                continue;
            }

            if (cell.Row == row)
            {
                if (!cell.Stats.IsBold)
                {
                    return false;
                }

                any = true;
            }
            else if (cell.Row > row && !cell.Stats.IsBold)
            {
                plainBelow = true;
            }
        }

        return any && plainBelow;
    }

    /// <summary>Whether the first row holds no number and stands over a column of numbers.</summary>
    private static bool OverNumbers(TablePlan table)
    {
        if (table.Cells.Exists(cell => cell.Row == 0 && cell.Stats.IsNumber) || !table.Cells.Exists(cell => cell.Row == 0 && cell.Stats.Letters > 0))
        {
            return false;
        }

        for (int column = 0; column < table.Columns; column++)
        {
            int filled = 0;
            int numbers = 0;
            foreach (CellPlan cell in table.Cells)
            {
                if (cell.Row > 0 && cell.Column == column && cell.Stats.Visible > 0)
                {
                    filled++;
                    numbers += cell.Stats.IsNumber ? 1 : 0;
                }
            }

            if (filled >= 2 && numbers >= NumericShare * filled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a horizontal ruling across most of the table lies between its first and second rows.</summary>
    private bool RuledOff(TablePlan table)
    {
        double above = double.PositiveInfinity;
        double below = double.NegativeInfinity;
        foreach (CellPlan cell in table.Cells)
        {
            foreach (int item in cell.Fragments)
            {
                if (cell.Row == 0)
                {
                    above = Math.Min(above, _fragments[item].Bottom);
                }
                else if (cell.Row == 1)
                {
                    below = Math.Max(below, _fragments[item].Top);
                }
            }
        }

        foreach (Ruling ruling in _current!.Horizontal)
        {
            if (ruling.Position < above && ruling.Position > below
                && Math.Min(ruling.High, table.Right) - Math.Max(ruling.Low, table.Left) >= 0.5 * (table.Right - table.Left))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a cell's glyphs hold.</summary>
    private struct CellStats
    {
        public int Characters;
        public int Words;
        public int Digits;
        public int Letters;
        public int Visible;
        public int Bold;

        /// <summary>Gets a value indicating whether the text is a number: digits, with at most one letter (a unit or a sign).</summary>
        public readonly bool IsNumber => Digits > 0 && Letters <= 1;

        /// <summary>Gets a value indicating whether most of the visible glyphs are bold.</summary>
        public readonly bool IsBold => Visible > 0 && Bold >= DominantShare * Visible;
    }

    /// <summary>A cell of a stream table being read: its row, the columns it reaches, and its fragments at <c>[Start, Start + Count)</c> of <see cref="_pieces"/>.</summary>
    private readonly record struct StreamCell(int Row, int First, int Last, int Start, int Count);

    /// <summary>A detected table: its grid, its cells in row-major order, and the node made from it.</summary>
    private sealed class TablePlan
    {
        public double Left = double.PositiveInfinity;
        public double Bottom = double.PositiveInfinity;
        public double Right = double.NegativeInfinity;
        public double Top = double.NegativeInfinity;

        public int Rows { get; set; }

        public int Columns { get; set; }

        /// <summary>Gets or sets the left edge of each column.</summary>
        public double[] Edges { get; set; } = [];

        /// <summary>Gets or sets a value indicating whether the table was read from ruling lines.</summary>
        public bool Ruled { get; set; }

        public List<CellPlan> Cells { get; } = [];

        public LayoutTable? Node { get; set; }

        public PdfRectangle Bounds => new(Left, Bottom, Right, Top);
    }

    /// <summary>A cell of a detected table: its place, its spans, its fragments, and where its glyphs went in the rebuilt text.</summary>
    private sealed class CellPlan(int row, int column, int rowSpan, int columnSpan)
    {
        public int Row { get; set; } = row;

        public int Column { get; set; } = column;

        public int RowSpan { get; set; } = rowSpan;

        public int ColumnSpan { get; set; } = columnSpan;

        public bool Header { get; set; }

        /// <summary>Gets the ruled rectangle of the cell; null for a table found from white space.</summary>
        public PdfRectangle? Box { get; init; }

        public List<int> Fragments { get; } = [];

        public CellStats Stats { get; set; }

        public int GlyphStart { get; set; }

        public int GlyphCount { get; set; }

        public int TextStart { get; set; }

        public int TextLength { get; set; }

        /// <summary>Gets or sets the cell's bounds: its ruled box, else its text's; null for an empty cell found from white space.</summary>
        public PdfRectangle? Bounds { get; set; }
    }
}
