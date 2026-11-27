namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about saving: the version written, object numbers, and PDF/A, PDF/UA and Well-Tagged PDF conformance.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.5; ISO 19005; ISO 14289. Each code is reported under <see cref="DiagnosticArea.Saving"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class SavingDiagnostics
{
    /// <summary>Gets the <c>VersionRaised</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2, §7.7.2 Table 29</remarks>
    public static DiagnosticCode VersionRaised { get; } = new(nameof(VersionRaised), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2, §7.7.2 Table 29",
        "The save needs features that belong to a later PDF version than the document declares. Broadside raises the saved version, or in strict mode writes nothing.");

    /// <summary>Gets the <c>ObjectNumberOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.6, Annex C Table C.1</remarks>
    public static DiagnosticCode ObjectNumberOutOfRange { get; } = new(nameof(ObjectNumberOutOfRange), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4, §7.5.6, Annex C Table C.1",
        "The file holds, or the save would produce, an object number above the limit allowed for cross-reference data. New objects use free lower numbers, a full save renumbers, and an incremental update that cannot renumber writes nothing.");

    /// <summary>Gets the <c>PdfANonConformant</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANonConformant { get; } = new(nameof(PdfANonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 19005 (PDF/A); veraPDF validation profiles",
        "The file cannot conform to the requested PDF/A level because a validation rule is violated. The violation is reported and the save continues.");

    /// <summary>Gets the <c>PdfANotIdentified</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANotIdentified { get; } = new(nameof(PdfANotIdentified), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles",
        "The file is saved without PDF/A identification or output intent because it cannot conform to the requested level. This records that no PDF/A claim is made.");

    /// <summary>Gets the <c>PdfAConverted</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfAConverted { get; } = new(nameof(PdfAConverted), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles",
        "The document was converted toward the requested PDF/A level. This records the changes the conversion made.");

    /// <summary>Gets the <c>PdfANotConverted</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANotConverted { get; } = new(nameof(PdfANotConverted), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles",
        "The conversion toward the requested PDF/A level left problems it could not fix. This records what remains non-conforming.");

    /// <summary>Gets the <c>PdfUANonConformant</c> code.</summary>
    /// <remarks>ISO 14289-1 (PDF/UA-1); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfUANonConformant { get; } = new(nameof(PdfUANonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 14289-1 (PDF/UA-1); veraPDF validation profiles",
        "The file cannot conform to the requested PDF/UA level because a validation rule is violated. The violation is reported and the save continues.");

    /// <summary>Gets the <c>PdfUANotIdentified</c> code.</summary>
    /// <remarks>ISO 14289-1 (PDF/UA-1); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfUANotIdentified { get; } = new(nameof(PdfUANotIdentified), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 14289-1 (PDF/UA-1); veraPDF validation profiles",
        "The file is saved without PDF/UA identification because it cannot conform to the requested level. This records that no PDF/UA claim is made.");

    /// <summary>Gets the <c>WellTaggedNonConformant</c> code.</summary>
    /// <remarks>Well-Tagged PDF 1.0; veraPDF validation profiles</remarks>
    public static DiagnosticCode WellTaggedNonConformant { get; } = new(nameof(WellTaggedNonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "Well-Tagged PDF 1.0; veraPDF validation profiles",
        "The file cannot conform to the requested Well-Tagged PDF level because a validation rule is violated. The violation is reported and the save continues.");

    /// <summary>Gets the <c>WellTaggedNotDeclared</c> code.</summary>
    /// <remarks>Well-Tagged PDF 1.0; veraPDF validation profiles</remarks>
    public static DiagnosticCode WellTaggedNotDeclared { get; } = new(nameof(WellTaggedNotDeclared), DiagnosticArea.Saving, DiagnosticSeverity.Information, "Well-Tagged PDF 1.0; veraPDF validation profiles",
        "The file is saved without the Well-Tagged PDF declaration because it cannot conform to the requested level. This records that no Well-Tagged claim is made.");

    /// <summary>Gets the <c>ConformanceOverridden</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); ISO 14289 (PDF/UA)</remarks>
    public static DiagnosticCode ConformanceOverridden { get; } = new(nameof(ConformanceOverridden), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); ISO 14289 (PDF/UA)",
        "The PDF/A or PDF/UA conformance options given for a save differ from the document default save options, and the save options take precedence. This records the override.");

    /// <summary>Gets the <c>InvoiceNotPdfA3</c> code.</summary>
    /// <remarks>ISO 19005-3 §6.8; Factur-X 1.0 and ZUGFeRD 2.x</remarks>
    public static DiagnosticCode InvoiceNotPdfA3 { get; } = new(nameof(InvoiceNotPdfA3), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 19005-3 §6.8; Factur-X 1.0 and ZUGFeRD 2.x",
        "A document with an attached e-invoice is saved without a conformant PDF/A-3 identification. The invoice and its XMP properties are written, but the file is not a valid Factur-X or ZUGFeRD invoice.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        VersionRaised,
        ObjectNumberOutOfRange,
        PdfANonConformant,
        PdfANotIdentified,
        PdfAConverted,
        PdfANotConverted,
        PdfUANonConformant,
        PdfUANotIdentified,
        WellTaggedNonConformant,
        WellTaggedNotDeclared,
        ConformanceOverridden,
        InvoiceNotPdfA3,
    ];
}
