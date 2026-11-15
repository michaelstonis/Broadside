namespace Broadside.Layout.Flow;

/// <summary>
/// Ends the page: the next block starts at the top of a new page. It draws nothing and adds no structure element. Before the first
/// content of the flow or of a section it does nothing (a section starts on a new page anyway), and inside a table cell or a column it
/// does nothing; two in a row leave a blank page between them.
/// </summary>
/// <remarks>ISO 32000-2 §7.7.3.3: the next page is a new page object of the section's template.</remarks>
public sealed class PageBreak : FlowBlock
{
    /// <summary>Creates a page break.</summary>
    public PageBreak()
        : base("Artifact")
    {
    }

    internal override void Validate(int index)
    {
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => default;

    internal override void LayOut(FlowContext context) => context.BreakPageExplicitly();
}

/// <summary>
/// Empty vertical space between blocks, like a paragraph's space before or after: it is dropped at the top of a page, and space that
/// reaches past the bottom of a page ends there (the next block starts on the next page). It draws nothing and adds no structure element.
/// </summary>
/// <remarks>ISO 32000-2 §7.7.3.3: only the position of what follows on the page changes.</remarks>
public sealed class Spacer : FlowBlock
{
    /// <summary>Creates a spacer of <paramref name="height"/> points.</summary>
    /// <param name="height">The height in points.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is negative or not finite.</exception>
    public Spacer(double height)
        : base("Artifact")
    {
        Height = ImageBlock.NotNegative(height, nameof(height));
    }

    /// <summary>The height in points.</summary>
    public double Height { get; }

    internal override void Validate(int index)
    {
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => default;

    internal override void LayOut(FlowContext context) => context.AddSpace(Height);
}
