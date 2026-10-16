namespace Broadside;

/// <summary>A PDF/A conformance level a save can target (<see cref="PdfSaveOptions.PdfAConformance"/>).</summary>
/// <remarks>
/// ISO 19005 (PDF/A). The standard is not in the repository; the levels and their requirements are cited by the rule identifiers of
/// veraPDF's validation profiles (clause and test number, e.g. 6.2.11.4.1-1), the reference validator Broadside's output is checked
/// with in CI (issues #201, #202, #203, open decision 12).
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

    /// <summary>
    /// PDF/A-4 (ISO 19005-4:2020): based on PDF 2.0, so the file has a <c>%PDF-2.n</c> header and no 1.7 limit; the identification
    /// names the part and the revision year (<c>pdfaid:rev</c> 2020) and no conformance level; the trailer has no document information
    /// dictionary (a save moves its entries to the XMP packet), unless the catalog has <c>PieceInfo</c>, when it keeps only
    /// <c>ModDate</c>. Every font program embedded, an output intent, no encryption and no forbidden actions, as for PDF/A-2. An
    /// embedded file must itself be PDF/A-1, -2 or -4, which a save does not verify, so it reports any.
    /// </summary>
    /// <remarks>ISO 19005-4, 6.1.2, 6.1.3, 6.7.3 and 6.9 (veraPDF PDF/A-4 validation profile, rules 6.1.2-1, 6.1.3-4, 6.1.3-5, 6.7.3-2 to 6.7.3-5, 6.9-3).</remarks>
    PdfA4,

    /// <summary>
    /// PDF/A-4f (ISO 19005-4:2020, conformance F): PDF/A-4 with embedded files of any kind. The catalog's name dictionary has an
    /// <c>EmbeddedFiles</c> tree, and each embedded file's specification has <c>F</c> and <c>UF</c>, an <c>AFRelationship</c>, and a
    /// stream naming its MIME type; the identification states <c>pdfaid:conformance</c> F.
    /// </summary>
    /// <remarks>ISO 19005-4, 6.7.3 and 6.9 (veraPDF PDF/A-4F validation profile, rules 6.9-1, 6.9-2, 6.9-4 and 6.9-5).</remarks>
    PdfA4f,
}
