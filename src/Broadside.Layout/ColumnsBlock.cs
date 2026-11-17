namespace Broadside.Layout.Flow;

/// <summary>One column of a <see cref="ColumnsBlock"/>: its width and the blocks laid out in it, top to bottom.</summary>
/// <remarks>ISO 32000-2 §14.8.4.4: tagged as one grouping element (<c>Div</c> by default) holding its blocks.</remarks>
public sealed class FlowColumn
{
    /// <summary>Creates a column of an equal share of the width left by the other columns.</summary>
    /// <param name="blocks">The blocks, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> or one of them is null.</exception>
    public FlowColumn(params IEnumerable<FlowBlock> blocks)
        : this(ColumnWidth.Proportional(), blocks)
    {
    }

    /// <summary>Creates a column of <paramref name="width"/>.</summary>
    /// <param name="width">The width: fixed points, a percentage of the content area, or a proportional share of what the other columns
    /// leave (<see cref="ColumnWidth.Auto"/> is a share of weight 1).</param>
    /// <param name="blocks">The blocks, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> or one of them is null.</exception>
    public FlowColumn(ColumnWidth width, params IEnumerable<FlowBlock> blocks)
    {
        Width = width;
        Blocks = BlockGroup.Copy(blocks, nameof(blocks));
    }

    /// <summary>The width.</summary>
    public ColumnWidth Width { get; }

    /// <summary>The blocks, in order.</summary>
    public IReadOnlyList<FlowBlock> Blocks { get; }
}

/// <summary>
/// Blocks placed side by side in columns across the content area, such as an address beside an invoice number: each column flows its
/// own blocks from the same top, and the block ends below the longest column. A column longer than the room left on the page continues
/// at the top of the next page, the others beside it. This is not newspaper-style flow from one column into the next.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4.4 (grouping structure types): tagged output makes each column one element of <see cref="FlowBlock.StructureType"/>
/// (<c>Div</c> by default) holding its blocks, in column order, so a reader reads one column after the other and never as a table; the
/// block itself adds no element. A column that continues on the next page stays one element.
/// </para>
/// <para>Widths: fixed columns take their points and percentage columns their share of the content width less the gaps; proportional
/// columns share what is left by weight. When fixed and percentage columns ask for more than the width, they are scaled down to fit.</para>
/// </remarks>
public sealed class ColumnsBlock : FlowBlock
{
    private const double Tolerance = 1e-9;

    [ThreadStatic]
    private static int _depth;

    /// <summary>Creates columns side by side, each tagged <c>Div</c>.</summary>
    /// <param name="columns">The columns, left to right; at least one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> or one of them is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="columns"/> is empty.</exception>
    public ColumnsBlock(params IEnumerable<FlowColumn> columns)
        : base("Div")
    {
        ArgumentNullException.ThrowIfNull(columns);
        FlowColumn[] array = [.. columns];
        foreach (FlowColumn column in array)
        {
            ArgumentNullException.ThrowIfNull(column, nameof(columns));
        }

        if (array.Length == 0)
        {
            throw new ArgumentException("Columns need at least one column.", nameof(columns));
        }

        Columns = array;
    }

    /// <summary>The columns, left to right.</summary>
    public IReadOnlyList<FlowColumn> Columns { get; }

    /// <summary>The space between two columns in points; 12 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Gap
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    } = 12;

    /// <summary>The space above in points, dropped at the top of a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>The space below in points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    internal override void Validate(int index)
    {
        _depth++;
        try
        {
            if (_depth > BlockGroup.MaxDepth)
            {
                throw new ArgumentException($"The columns of block {index} nest more than {BlockGroup.MaxDepth} levels deep (or contain themselves).");
            }

            foreach (FlowColumn column in Columns)
            {
                foreach (FlowBlock block in column.Blocks)
                {
                    block.Validate(index);
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
        double min = Gap * (Columns.Count - 1);
        double max = min;
        foreach (FlowColumn column in Columns)
        {
            ContentWidths content = BlockGroup.Widest(column.Blocks, services);
            min += column.Width.Kind == ColumnWidthKind.Fixed ? column.Width.Value : content.Min;
            max += column.Width.Kind == ColumnWidthKind.Fixed ? column.Width.Value : content.Max;
        }

        return new(min, max);
    }

    internal override void LayOut(FlowContext context)
    {
        context.AddSpace(SpaceBefore);
        context.EnsureRoom(0);
        (double[] offsets, double[] widths) = Place(context.Width);
        double fresh = context.NextPageHeight;
        double[] heights = [context.CursorY - context.Bottom, fresh];
        (int[] pages, double[] last, bool overflowed) = Measure(context, offsets, widths, heights);
        if (overflowed && !context.AtPageTop)
        {
            // Not even the first line of every column fits here: start on the next page.
            context.BreakPage();
            heights = [context.CursorY - context.Bottom, context.NextPageHeight];
            (pages, last, _) = Measure(context, offsets, widths, heights);
        }

        int fragments = pages.Max();

        // One structure owner per column of this placement, so the same columns added twice are two sets of elements.
        var parts = new ColumnPart[Columns.Count];
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = new ColumnPart(Columns[index]);
        }

        context.PlaceMarkersAtTop();
        for (int fragment = 0; fragment < fragments; fragment++)
        {
            if (fragment > 0)
            {
                context.BreakPage();
            }

            double top = context.CursorY;
            double height = 0;
            for (int index = 0; index < Columns.Count; index++)
            {
                FlowColumn column = Columns[index];
                context.BeginPart(new FlowPart(StructureType, parts[index], Language));
                using (var region = new FlowContext(context, context.Left + offsets[index], context.Left + offsets[index] + widths[index], top, heights, fragment))
                {
                    region.LayOutBlocks(column.Blocks);
                }

                context.EndPart();
                if (pages[index] - 1 == fragment)
                {
                    height = Math.Max(height, last[index]);
                }
            }

            context.Advance(fragment < fragments - 1 ? Math.Max(0, top - context.Bottom) : height);
        }

        context.AddSpace(SpaceAfter);
    }

    /// <summary>Lays every column out without drawing: the virtual pages each takes, the height used on its last, and whether a first line did not fit.</summary>
    private (int[] Pages, double[] Last, bool Overflowed) Measure(FlowContext context, double[] offsets, double[] widths, double[] heights)
    {
        var pages = new int[Columns.Count];
        var last = new double[Columns.Count];
        bool overflowed = heights[0] <= Tolerance;
        for (int index = 0; index < Columns.Count; index++)
        {
            using var region = new FlowContext(context, offsets[index], offsets[index] + widths[index], 0, heights, -1);
            region.LayOutBlocks(Columns[index].Blocks);
            pages[index] = region.PageCount;
            last[index] = region.Top - region.ContentBottom;
            overflowed |= region.OverflowedFirstPage;
        }

        return (pages, last, overflowed);
    }

    /// <summary>The left offset and width of each column in a content area <paramref name="available"/> points wide.</summary>
    private (double[] Offsets, double[] Widths) Place(double available)
    {
        int count = Columns.Count;
        double room = Math.Max(0, available - (Gap * (count - 1)));
        var widths = new double[count];
        double asked = 0;
        double weights = 0;
        for (int index = 0; index < count; index++)
        {
            ColumnWidth width = Columns[index].Width;
            switch (width.Kind)
            {
                case ColumnWidthKind.Fixed:
                    widths[index] = width.Value;
                    asked += width.Value;
                    break;
                case ColumnWidthKind.Percent:
                    widths[index] = room * width.Value / 100;
                    asked += widths[index];
                    break;
                default:
                    weights += width.Kind == ColumnWidthKind.Proportional ? width.Value : 1;
                    break;
            }
        }

        double scale = asked > room ? room / asked : 1;
        double left = room - (asked * scale);
        var offsets = new double[count];
        double x = 0;
        for (int index = 0; index < count; index++)
        {
            ColumnWidth width = Columns[index].Width;
            widths[index] = width.Kind switch
            {
                ColumnWidthKind.Fixed or ColumnWidthKind.Percent => widths[index] * scale,
                ColumnWidthKind.Proportional => weights > 0 ? left * width.Value / weights : 0,
                _ => weights > 0 ? left / weights : 0,
            };
            offsets[index] = x;
            x += widths[index] + Gap;
        }

        return (offsets, widths);
    }
}

/// <summary>The structure owner of one column of one placed <see cref="ColumnsBlock"/>: a column that continues on the next page begins again with the same owner.</summary>
internal sealed class ColumnPart(FlowColumn column)
{
    public FlowColumn Column { get; } = column;
}
