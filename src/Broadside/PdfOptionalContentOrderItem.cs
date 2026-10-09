namespace Broadside;

/// <summary>
/// One node of a configuration's <c>Order</c> tree, as a user interface presents it: a group (whose children are its sublayers),
/// a labelled collection, or an unlabelled list.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.11.4.3, Table 99 and Examples 1 and 2. A nested array whose first element is a text string is a labelled
/// collection (<see cref="Label"/>, not nesting of content); a nested array without a label that follows a group holds that
/// group's sublayers; any other nested array is an unlabelled list. Exposed as written; groups not listed are not added.
/// </remarks>
public sealed class PdfOptionalContentOrderItem
{
    internal PdfOptionalContentOrderItem(PdfOptionalContentGroup? group, string? label, IReadOnlyList<PdfOptionalContentOrderItem> children)
    {
        Group = group;
        Label = label;
        Children = children;
    }

    /// <summary>Gets the group this node presents, or <see langword="null"/> for a collection or list.</summary>
    public PdfOptionalContentGroup? Group { get; }

    /// <summary>Gets the non-selectable label of a collection, or <see langword="null"/>.</summary>
    public string? Label { get; }

    /// <summary>Gets the nodes under this one: a group's sublayers or a collection's members.</summary>
    public IReadOnlyList<PdfOptionalContentOrderItem> Children { get; }

    /// <inheritdoc/>
    public override string ToString() => Group?.Name ?? Label ?? "[]";
}
