namespace Broadside;

/// <summary>A PDF/UA conformance a save can target (<see cref="PdfSaveOptions.PdfUAConformance"/>).</summary>
/// <remarks>
/// ISO 14289 (PDF/UA, universal accessibility). The standard is not in the repository; its machine-checkable requirements are cited by
/// the rule identifiers of veraPDF's PDF/UA validation profile (clause and test number, e.g. 7.3-1), which follow the failure conditions
/// of the Matterhorn Protocol 1.1, the reference validator Broadside's output is checked with in CI (issue #204, open decision 12).
/// </remarks>
public enum PdfUAConformance
{
    /// <summary>No PDF/UA conformance: the save neither checks nor claims any.</summary>
    None,

    /// <summary>
    /// PDF/UA-1 (ISO 14289-1): a tagged document on PDF 1.7 whose structure tree covers every piece of real content (the rest is marked
    /// as artifacts), with standard or role-mapped structure types, a title shown in the window, a determinable natural language,
    /// alternate text on figures, header cells with a scope, links and annotations tagged and described, every font embedded and
    /// mapped to Unicode, and the <c>pdfuaid</c> identification in the XMP metadata.
    /// </summary>
    /// <remarks>ISO 14289-1, clauses 5 to 7 (veraPDF PDF/UA-1 validation profile; Matterhorn Protocol 1.1).</remarks>
    PdfUA1,
}
