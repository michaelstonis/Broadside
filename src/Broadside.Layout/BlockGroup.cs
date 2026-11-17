using System.Globalization;
using Broadside.Diagnostics;

namespace Broadside.Layout.Flow;

/// <summary>
/// Blocks laid out one after the other as a unit: optionally kept on one page (<see cref="KeepTogether"/>) and optionally tagged as one
/// grouping element (<see cref="IsTagged"/>). By default the group adds no structure element, so its blocks are tagged as if they stood
/// in the flow directly.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.4 (grouping structure types: a tagged group is a <c>Div</c> unless <see cref="FlowBlock.StructureType"/>
/// says otherwise) and §7.7.3.3 (pagination).</remarks>
public sealed class BlockGroup : FlowBlock
{
    /// <summary>The deepest nesting of groups and columns accepted.</summary>
    internal const int MaxDepth = 64;

    [ThreadStatic]
    private static int _depth;

    /// <summary>Creates a group of <paramref name="blocks"/>, structure type <c>Div</c> when tagged.</summary>
    /// <param name="blocks">The blocks, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="blocks"/> or one of them is null.</exception>
    public BlockGroup(params IEnumerable<FlowBlock> blocks)
        : base("Div")
    {
        Blocks = Copy(blocks, nameof(blocks));
    }

    /// <summary>The blocks, in order.</summary>
    public IReadOnlyList<FlowBlock> Blocks { get; }

    /// <summary>
    /// Gets a value indicating whether the group stays on one page: when it does not fit below the cursor but fits on a new page, the page
    /// breaks before it. A group taller than a page breaks across pages anyway and records <see cref="LayoutDiagnostics.KeepTogetherTooTall"/>.
    /// </summary>
    public bool KeepTogether { get; init; }

    /// <summary>Gets a value indicating whether the group is tagged as one element of <see cref="FlowBlock.StructureType"/> holding its blocks;
    /// false (the default) adds no element.</summary>
    public bool IsTagged { get; init; }

    internal override void Validate(int index)
    {
        _depth++;
        try
        {
            if (_depth > MaxDepth)
            {
                throw new ArgumentException($"The group of block {index} nests more than {MaxDepth} levels deep (or contains itself).");
            }

            foreach (FlowBlock block in Blocks)
            {
                block.Validate(index);
            }
        }
        finally
        {
            _depth--;
        }
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => Widest(Blocks, services);

    internal override void LayOut(FlowContext context)
    {
        if (KeepTogether)
        {
            KeepOnOnePage(context);
        }

        if (IsTagged)
        {
            context.BeginBlock(this);
        }

        context.LayOutBlocks(Blocks);
        if (IsTagged)
        {
            context.EndBlock();
        }
    }

    /// <summary>The widest least and preferred widths of <paramref name="blocks"/>.</summary>
    internal static ContentWidths Widest(IReadOnlyList<FlowBlock> blocks, FlowServices services)
    {
        double min = 0;
        double max = 0;
        foreach (FlowBlock block in blocks)
        {
            ContentWidths content = block.MeasureWidths(services);
            min = Math.Max(min, content.Min);
            max = Math.Max(max, content.Max);
        }

        return new(min, max);
    }

    /// <summary>Copies <paramref name="blocks"/>, refusing a null one.</summary>
    internal static FlowBlock[] Copy(IEnumerable<FlowBlock> blocks, string name)
    {
        ArgumentNullException.ThrowIfNull(blocks, name);
        FlowBlock[] array = [.. blocks];
        foreach (FlowBlock block in array)
        {
            ArgumentNullException.ThrowIfNull(block, name);
        }

        return array;
    }

    /// <summary>Breaks the page first when the group does not fit below the cursor but fits on a new page; records a diagnostic when it fits on no page.</summary>
    private void KeepOnOnePage(FlowContext context)
    {
        context.EnsureRoom(0);
        double page = context.Top - context.Bottom;
        if (!context.AtPageTop)
        {
            if (context.MeasureHeight(Blocks, continuing: true) <= context.CursorY - context.Bottom + 1e-9)
            {
                return;
            }

            page = context.NextPageHeight;
        }

        double height = context.MeasureHeight(Blocks, continuing: false);
        if (height <= page + 1e-9)
        {
            if (!context.AtPageTop)
            {
                context.BreakPage();
            }

            return;
        }

        if (context.Drawing)
        {
            context.Diagnostics.Add(new Diagnostic(
                LayoutDiagnostics.KeepTogetherTooTall.Name,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"A group kept together is {height:0.##} points tall, taller than the {page:0.##}-point body of a page; it breaks across pages.")));
        }
    }
}
