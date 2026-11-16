namespace Broadside.Layout.Flow;

/// <summary>
/// The number of the page a block of the same flow lands on, shown as text in a paragraph: the block's <see cref="FlowBlock.Id"/>
/// names it. The number counts the pages of the flow from 1, as a header's <see cref="HeaderFooter.PageNumberField"/> does.
/// </summary>
/// <remarks>
/// <para>
/// A forward reference: the page is known only once the flow is laid out, so a flow with page references is laid out again with the
/// numbers the previous layout found until they no longer change (at most <see cref="FlowDocument.MaxLayoutPasses"/> times), then
/// drawn. An id no block of the flow has shows <c>?</c> and is reported (<c>AnchorNotFound</c>).
/// </para>
/// <para>ISO 32000-2 §14.8.4.7.2: the number is tagged as text of its paragraph, or as <see cref="InlineElement.StructureType"/> when one is given.</para>
/// </remarks>
public sealed class PageReference : InlineElement
{
    /// <summary>Creates a reference shown in the paragraph's text style.</summary>
    /// <param name="id">The <see cref="FlowBlock.Id"/> of the block whose page number is shown.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    public PageReference(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
    }

    /// <summary>Creates a reference shown in <paramref name="style"/>.</summary>
    /// <param name="id">The <see cref="FlowBlock.Id"/> of the block whose page number is shown.</param>
    /// <param name="style">The text style of the number.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public PageReference(string id, TextStyle style)
        : this(id)
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
    }

    internal PageReference(FlowBlock target, TextStyle? style)
    {
        Id = target.Id ?? string.Empty;
        Target = target;
        Style = style;
    }

    /// <summary>The id of the block whose page number is shown.</summary>
    public string Id { get; }

    /// <summary>The style, or null for the paragraph's.</summary>
    public TextStyle? Style { get; }

    /// <summary>The block a reference made by the layout points at (a table of contents entry's heading), or null to find it by <see cref="Id"/>.</summary>
    internal FlowBlock? Target { get; }

    private protected override string DefaultStructureType => "Span";
}

/// <summary>
/// A leader: a run of a repeated character (dots by default) that fills the rest of its line, pushing what follows to the right edge,
/// as in a table of contents entry. A line holding a leader is neither aligned nor justified.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.2.2: the leader repeats no content, so it is marked as a layout artifact. It is measured as nothing when lines
/// are broken, so the line it ends takes as much text as fits; the space left is what it fills.
/// </remarks>
public sealed class Leader : InlineElement
{
    /// <summary>Creates a leader of <paramref name="text"/> repeated in <paramref name="style"/>.</summary>
    /// <param name="text">The text repeated, such as <c>.</c> or <c>. </c>.</param>
    /// <param name="style">The text style of the leader.</param>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public Leader(string text, TextStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(style);
        Text = text;
        Style = style;
    }

    /// <summary>The text repeated.</summary>
    public string Text { get; }

    /// <summary>The text style.</summary>
    public TextStyle Style { get; }

    private protected override string DefaultStructureType => "Artifact";
}
