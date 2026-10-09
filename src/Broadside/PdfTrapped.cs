namespace Broadside;

/// <summary>Whether the document has been modified to include trapping information: the Info dictionary's <c>Trapped</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §14.3.3, Table 349 (PDF 1.3; deprecated in PDF 2.0, use <c>pdf:Trapped</c> in XMP). A name, not a boolean; the
/// default is <see cref="Unknown"/>.
/// </remarks>
public enum PdfTrapped
{
    /// <summary>Unknown, or only partly trapped (<c>Unknown</c>, the default).</summary>
    Unknown,

    /// <summary>Fully trapped (<c>True</c>).</summary>
    True,

    /// <summary>Not trapped (<c>False</c>).</summary>
    False,
}
