using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>
/// A list of a layout tree: its items are its <see cref="LayoutNodeKind.ListItem"/> children, each with an optional
/// <see cref="LayoutNodeKind.Label"/> and a <see cref="LayoutNodeKind.ListBody"/> that holds any nested list.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.8.2, Table 370 (<c>L</c>, <c>LI</c>, <c>LBody</c>) and Table 368 (<c>Lbl</c>); §14.8.5.5, Table 385 (<c>ListNumbering</c>).</remarks>
public sealed class LayoutList : LayoutNode
{
    internal LayoutList(PdfStructureElement? element, string numbering)
        : base(LayoutNodeKind.List, element)
    {
        Numbering = numbering;
    }

    /// <summary>
    /// Gets how the items are numbered, as a <c>ListNumbering</c> value: <c>None</c>, <c>Unordered</c>, <c>Description</c>,
    /// <c>Disc</c>, <c>Circle</c>, <c>Square</c>, <c>Ordered</c>, <c>Decimal</c>, <c>UpperRoman</c>, <c>LowerRoman</c>,
    /// <c>UpperAlpha</c> or <c>LowerAlpha</c>. For a tagged list it is the element's attribute in effect (<c>None</c> by default, or
    /// whatever name the file holds); for a detected list it is read from the items' markers (<c>Unordered</c> for a bullet that is
    /// not a disc, circle or square).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.5.5, Table 385 (<c>ListNumbering</c>; PDF 2.0 adds <c>Unordered</c>, <c>Description</c> and <c>Ordered</c>).</remarks>
    public string Numbering { get; }
}
