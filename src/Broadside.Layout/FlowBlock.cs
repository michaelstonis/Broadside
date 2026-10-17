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

    /// <summary>Checks the block before anything is written; throws <see cref="ArgumentException"/> naming the block by its <paramref name="index"/> in the flow.</summary>
    internal abstract void Validate(int index);

    /// <summary>Lays the block out from the context's cursor, breaking pages through it as needed.</summary>
    internal abstract void LayOut(FlowContext context);

    /// <summary>The block's least width (its widest unbreakable content) and preferred width (unwrapped), for content-sized table columns.</summary>
    internal abstract ContentWidths MeasureWidths(FlowServices services);
}
