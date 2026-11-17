namespace Broadside.Layout.Flow;

/// <summary>
/// One block of a <see cref="FlowDocument"/>: something laid out top to bottom in the content area, breaking across pages. Every
/// block names the structure element it becomes when the output is tagged.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 (standard structure types) and §14.9.2 (natural language specification): <see cref="StructureType"/> and
/// <see cref="Language"/> are what tagged output writes for the block.
/// </remarks>
public abstract class FlowBlock
{
    private protected FlowBlock(string structureType)
    {
        StructureType = structureType;
    }

    /// <summary>The standard structure type of the block when tagged, such as <c>P</c> or <c>H1</c>.</summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public string StructureType
    {
        get;
        init => field = string.IsNullOrEmpty(value) ? throw new ArgumentException("The structure type shall not be empty.", nameof(value)) : value;
    }

    /// <summary>The natural language of the block as a BCP 47 tag (such as <c>en-US</c>), or null to inherit the document's.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the block's anchor: a name that links (<see cref="TextRun.Destination"/>) and page references (<see cref="PageReference"/>)
    /// reach it by, or null for none. Rendering writes it as a named destination at the top left of the block's first line and reports
    /// where it landed in <see cref="FlowLayoutResult.Anchors"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <remarks>ISO 32000-2 §12.3.2.4 (named destinations, PDF 1.2) and §12.3.2.2 (an <c>XYZ</c> destination).</remarks>
    public string? Id
    {
        get;
        init => field = value is { Length: 0 } ? throw new ArgumentException("An anchor id shall not be empty.", nameof(value)) : value;
    }

    /// <summary>
    /// Gets a value indicating whether the block stays on the page of the block after it: when this block, and the first line of the
    /// next, do not both fit below the cursor but would fit on a new page, the page breaks before this block. Consecutive blocks with it
    /// set move as one chain. It has no effect on the last block of a flow, list item, group or column, or at the top of a page.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.3.3: pagination only; the content of each page is unchanged.</remarks>
    public bool KeepWithNext { get; init; }

    /// <summary>Checks the block before anything is written; throws <see cref="ArgumentException"/> naming the block by its <paramref name="index"/> in the flow.</summary>
    internal abstract void Validate(int index);

    /// <summary>Lays the block out from the context's cursor, breaking pages through it as needed.</summary>
    internal abstract void LayOut(FlowContext context);

    /// <summary>The block's least width (its widest unbreakable content) and preferred width (unwrapped), for content-sized table columns.</summary>
    internal abstract ContentWidths MeasureWidths(FlowServices services);
}
