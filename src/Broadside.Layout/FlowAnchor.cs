namespace Broadside.Layout.Flow;

/// <summary>Where a block with an <see cref="FlowBlock.Id"/> landed: its page and the top left of its first line.</summary>
/// <remarks>ISO 32000-2 §12.3.2.2 and §12.3.2.4: the flow writes a named destination of the id showing this point (<c>XYZ</c>).</remarks>
public sealed class FlowAnchor
{
    internal FlowAnchor(string id, PdfPage page, int pageNumber, double left, double top)
    {
        Id = id;
        Page = page;
        PageNumber = pageNumber;
        Left = left;
        Top = top;
    }

    /// <summary>Gets the block's id, which is also the name of its destination.</summary>
    public string Id { get; }

    /// <summary>Gets the page the block starts on.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the number of that page among the pages the flow added, from 1 (what <see cref="PageReference"/> shows).</summary>
    public int PageNumber { get; }

    /// <summary>Gets the left edge of the content area where the block starts, in default user space.</summary>
    public double Left { get; }

    /// <summary>Gets the top of the block's first line, in default user space.</summary>
    public double Top { get; }
}
