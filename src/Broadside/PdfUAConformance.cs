namespace Broadside;

/// <summary>A PDF/UA conformance a save can target (<see cref="PdfSaveOptions.PdfUAConformance"/>).</summary>
/// <remarks>
/// ISO 14289 (PDF/UA, universal accessibility). The standard is not in the repository; its machine-checkable requirements are cited by
/// the rule identifiers of veraPDF's PDF/UA validation profile (clause and test number, e.g. 7.3-1), which follow the failure conditions
/// of the Matterhorn Protocol 1.1, the reference validator Broadside's output is checked with in CI (issue #204, open decision 12);
/// PDF/UA-2 (issue #205) by the rule identifiers of veraPDF's PDF/UA-2 profile (ISO 14289-2 clause and test number, e.g. 8.2.5.25-1,
/// and the ISO/TS 32005 containment rules, e.g. <c>Table 5. L-P-1</c>).
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

    /// <summary>
    /// PDF/UA-2 (ISO 14289-2): a tagged document on PDF 2.0 whose structure tree has a single <c>Document</c> element of the PDF 2.0
    /// structure namespace as its root, covers every piece of real content (the rest is marked as artifacts) with standard or
    /// role-mapped structure types nested as ISO/TS 32005 allows, uses neither <c>H</c> nor <c>Note</c>, gives lists with labels a
    /// <c>ListNumbering</c>, has a title shown in the window, a determinable natural language, alternate text on figures, header cells
    /// connected to their data cells, links and markup annotations tagged, every font embedded and mapped to Unicode, and the
    /// <c>pdfuaid</c> identification (<c>part</c> 2, <c>rev</c> 2024) in the XMP metadata. Flow layout produces it with
    /// <c>FlowStructureNamespace.Pdf20</c>, its default; it combines with PDF/A-4.
    /// </summary>
    /// <remarks>ISO 14289-2:2024, clauses 5 to 8 (veraPDF PDF/UA-2 validation profile, with the ISO/TS 32005 containment rules).</remarks>
    PdfUA2,
}
