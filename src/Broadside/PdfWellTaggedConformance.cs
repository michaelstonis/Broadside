namespace Broadside;

/// <summary>
/// The Well-Tagged PDF (WTPDF 1.0) conformance levels a save can declare (<see cref="PdfSaveOptions.WellTaggedConformance"/>); the
/// levels combine.
/// </summary>
/// <remarks>
/// Well-Tagged PDF 1.0 (PDF Association, 2024), §6.1: a conforming file declares each level it meets with a PDF Declaration
/// (<see cref="PdfDeclaration"/>) in its document XMP metadata. Its requirements are those of ISO 14289-2 less the PDF/UA
/// identification (accessibility), or less those that serve assistive technology only (reuse), cited by the rule identifiers of
/// veraPDF's WTPDF 1.0 validation profiles.
/// </remarks>
[Flags]
public enum PdfWellTaggedConformance
{
    /// <summary>No WTPDF declaration: the save neither checks nor declares any.</summary>
    None = 0,

    /// <summary>
    /// The conformance level for reuse (WTPDF 1.0 §6.1.2): tagging good enough to extract, reflow and repurpose the content, declared as
    /// <see cref="PdfDeclaration.WellTaggedReuse"/>.
    /// </summary>
    /// <remarks>WTPDF 1.0 §6.1.2 (veraPDF WTPDF 1.0 Reuse validation profile, rule 6.1.2-1).</remarks>
    Reuse = 1,

    /// <summary>
    /// The conformance level for accessibility (WTPDF 1.0 §6.1.3): every requirement of PDF/UA-2 but its identification, declared as
    /// <see cref="PdfDeclaration.WellTaggedAccessibility"/>.
    /// </summary>
    /// <remarks>WTPDF 1.0 §6.1.3 (veraPDF WTPDF 1.0 Accessibility validation profile, rule 6.1.3-1).</remarks>
    Accessibility = 2,
}
