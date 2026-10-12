namespace Broadside;

/// <summary>The style flags of an outline item, the bits of its <c>F</c> entry (PDF 1.4).</summary>
/// <remarks>
/// ISO 32000-2 §12.3.3, Table 152. Bit n of the entry is <c>1 &lt;&lt; (n − 1)</c>; bits the table does not define are kept as stored
/// (<see cref="PdfOutlineItem.Flags"/>).
/// </remarks>
[Flags]
public enum PdfOutlineItemFlags
{
    /// <summary>No flag set (the default).</summary>
    None = 0,

    /// <summary>Bit 1: display the item's text in italic.</summary>
    Italic = 1 << 0,

    /// <summary>Bit 2: display the item's text in bold.</summary>
    Bold = 1 << 1,
}
