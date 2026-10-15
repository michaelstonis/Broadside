namespace Broadside;

/// <summary>A PDF/A conformance level a save can target (<see cref="PdfSaveOptions.PdfAConformance"/>).</summary>
/// <remarks>
/// ISO 19005 (PDF/A). The standard is not in the repository; the levels and their requirements are cited by the rule identifiers of
/// veraPDF's validation profiles (clause and test number, e.g. 6.2.11.4.1-1), the reference validator Broadside's output is checked
/// with in CI (issue #201, open decision 12).
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
}
