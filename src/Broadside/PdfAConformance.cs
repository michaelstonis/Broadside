namespace Broadside;

/// <summary>A PDF/A conformance level a save can target (<see cref="PdfSaveOptions.PdfAConformance"/>).</summary>
/// <remarks>
/// ISO 19005 (PDF/A). The standard is not in the repository; the levels and their requirements are cited by the rule identifiers of
/// veraPDF's validation profiles (clause and test number, e.g. 6.2.11.4.1-1), the reference validator Broadside's output is checked
/// with in CI (issues #201, #202, open decision 12).
/// </remarks>
public enum PdfAConformance
{
    /// <summary>No PDF/A conformance: the save neither checks nor claims any.</summary>
    None,

    /// <summary>
    /// PDF/A-2b (ISO 19005-2, level B, basic): reliable visual reproduction. PDF 1.7 at most, every font program embedded, an
    /// output intent, XMP metadata with the PDF/A identification, no encryption, no JavaScript or other forbidden actions, an
    /// appearance for every annotation.
    /// </summary>
    /// <remarks>ISO 19005-2 (veraPDF PDF/A-2B validation profile).</remarks>
    PdfA2b,

    /// <summary>
    /// PDF/A-2u (ISO 19005-2, level U, Unicode): PDF/A-2b, and every glyph shown maps to a Unicode value, through the font's
    /// <c>ToUnicode</c> CMap or the other means the standard allows (a standard encoding, glyph names from the Adobe Glyph List, a
    /// predefined CJK character collection), never to U+0000, U+FEFF or U+FFFE: the text can be searched and extracted.
    /// </summary>
    /// <remarks>ISO 19005-2, 6.2.11.7.2 (veraPDF PDF/A-2U validation profile, rules 6.2.11.7.2-1 and -2).</remarks>
    PdfA2u,

    /// <summary>
    /// PDF/A-3b (ISO 19005-3, level B): PDF/A-2b, except that a file of any kind may be embedded when it is an associated file: each
    /// embedded file's specification has an <c>AFRelationship</c>, is listed in an <c>AF</c> array, and its stream names a MIME type.
    /// </summary>
    /// <remarks>ISO 19005-3, 6.8 (veraPDF PDF/A-3B validation profile, rules 6.8-1 to 6.8-4).</remarks>
    PdfA3b,

    /// <summary>PDF/A-3u (ISO 19005-3, level U): PDF/A-3b, and every glyph shown maps to a Unicode value, as for <see cref="PdfA2u"/>.</summary>
    /// <remarks>ISO 19005-3, 6.2.11.7.2 and 6.8 (veraPDF PDF/A-3U validation profile).</remarks>
    PdfA3u,
}
