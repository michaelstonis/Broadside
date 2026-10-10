using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>An attribute object owned by <c>List</c>, on an <c>L</c> element.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.5.5, Table 382. Each property is this object's own value, <see langword="null"/> when absent; the value in effect
/// (<c>ListNumbering</c> is inheritable, default <c>None</c>) comes from <see cref="PdfStructureElement.GetAttributeValue(CosName, CosName)"/>.
/// </remarks>
public sealed class PdfListAttributes : PdfAttributeObject
{
    private static readonly CosName ListNumberingName = new("ListNumbering");
    private static readonly CosName ContinuedListName = new("ContinuedList");
    private static readonly CosName ContinuedFromName = new("ContinuedFrom");

    internal PdfListAttributes(StructureContext context, CosObject source, CosReference? reference, int revision)
        : base(context, source, reference, revision)
    {
    }

    /// <summary>
    /// Gets <c>ListNumbering</c>: None, Unordered, Description, Disc, Circle, Square, Ordered, Decimal, UpperRoman, LowerRoman,
    /// UpperAlpha or LowerAlpha.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 382 (Unordered, Description and Ordered are PDF 2.0).</remarks>
    public string? ListNumbering => NameValue(ListNumberingName);

    /// <summary>Gets a value indicating whether <see cref="ListNumbering"/> marks an ordered list: Ordered, Decimal, UpperRoman, LowerRoman, UpperAlpha or LowerAlpha.</summary>
    /// <remarks>ISO 32000-2 Table 382.</remarks>
    public bool IsOrdered => ListNumbering is "Ordered" or "Decimal" or "UpperRoman" or "LowerRoman" or "UpperAlpha" or "LowerAlpha";

    /// <summary>Gets <c>ContinuedList</c>: whether the list continues a previous one (PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 Table 382.</remarks>
    public bool? ContinuedList => ViewReading.Boolean(Document, Dictionary, ContinuedListName);

    /// <summary>Gets <c>ContinuedFrom</c>: the ID of the list this one continues (PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 Table 382.</remarks>
    public CosString? ContinuedFrom => GetValue(ContinuedFromName) as CosString;
}
