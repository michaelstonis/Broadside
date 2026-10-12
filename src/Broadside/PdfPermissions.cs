namespace Broadside;

/// <summary>The operations a user may perform on a document, as its security handler grants them.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.4.2, Table 22 (standard security handler) and §7.6.5.2, Table 24 (public-key security handlers). Each value is
/// the bit of the <c>P</c> entry that grants it (bit 3 is <c>1 &lt;&lt; 2</c>), so a <c>P</c> value of a revision 3 or later handler
/// masked with <see cref="All"/> is the set of permissions it grants.
/// </para>
/// <para>
/// A document opened with the owner password, and an unencrypted document, has <see cref="All"/>. For a revision 2 handler, which
/// controls only bits 3 to 6, <see cref="FillForms"/> follows <see cref="Annotate"/>, <see cref="Assemble"/> follows
/// <see cref="Modify"/> and <see cref="PrintHighQuality"/> follows <see cref="Print"/>. PDF readers "should behave as if"
/// <see cref="Extract"/> were granted when they provide content to assistive technology (Table 22, bit 5); bit 10 is ignored (PDF 2.0).
/// Readers are expected to respect these permissions; nothing in PDF enforces them (§7.6.4.1).
/// </para>
/// </remarks>
[Flags]
public enum PdfPermissions
{
    /// <summary>No permission.</summary>
    None = 0,

    /// <summary>Print the document, possibly at degraded quality unless <see cref="PrintHighQuality"/> is also granted (bit 3).</summary>
    Print = 1 << 2,

    /// <summary>Modify the contents by operations other than annotating, filling forms and assembling (bit 4).</summary>
    Modify = 1 << 3,

    /// <summary>Copy or otherwise extract text and graphics (bit 5).</summary>
    Extract = 1 << 4,

    /// <summary>Add or modify text annotations and fill in form fields; with <see cref="Modify"/>, create or modify form fields (bit 6).</summary>
    Annotate = 1 << 5,

    /// <summary>Fill in existing form fields, signature fields included, even without <see cref="Annotate"/> (bit 9).</summary>
    FillForms = 1 << 8,

    /// <summary>Assemble the document: insert, rotate or delete pages, create outline items or thumbnails (bit 11).</summary>
    Assemble = 1 << 10,

    /// <summary>Print to a representation from which a faithful digital copy could be generated (bit 12).</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>Every permission: the access of the owner, and of anyone to an unencrypted document.</summary>
    All = Print | Modify | Extract | Annotate | FillForms | Assemble | PrintHighQuality,
}
