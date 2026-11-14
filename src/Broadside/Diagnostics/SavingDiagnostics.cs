namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about saving: the version written, object numbers, and PDF/A, PDF/UA and Well-Tagged PDF conformance.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.5; ISO 19005; ISO 14289. Each code is reported under <see cref="DiagnosticArea.Saving"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class SavingDiagnostics
{
    /// <summary>Gets the <c>VersionRaised</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.2, §7.7.2 Table 29</remarks>
    public static DiagnosticCode VersionRaised { get; } = new(nameof(VersionRaised), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.2, §7.7.2 Table 29");

    /// <summary>Gets the <c>ObjectNumberOutOfRange</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.6, Annex C Table C.1</remarks>
    public static DiagnosticCode ObjectNumberOutOfRange { get; } = new(nameof(ObjectNumberOutOfRange), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 32000-2 §7.5.4, §7.5.6, Annex C Table C.1");

    /// <summary>Gets the <c>PdfANonConformant</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANonConformant { get; } = new(nameof(PdfANonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 19005 (PDF/A); veraPDF validation profiles");

    /// <summary>Gets the <c>PdfANotIdentified</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANotIdentified { get; } = new(nameof(PdfANotIdentified), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles");

    /// <summary>Gets the <c>PdfAConverted</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfAConverted { get; } = new(nameof(PdfAConverted), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles");

    /// <summary>Gets the <c>PdfANotConverted</c> code.</summary>
    /// <remarks>ISO 19005 (PDF/A); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfANotConverted { get; } = new(nameof(PdfANotConverted), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 19005 (PDF/A); veraPDF validation profiles");

    /// <summary>Gets the <c>PdfUANonConformant</c> code.</summary>
    /// <remarks>ISO 14289-1 (PDF/UA-1); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfUANonConformant { get; } = new(nameof(PdfUANonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "ISO 14289-1 (PDF/UA-1); veraPDF validation profiles");

    /// <summary>Gets the <c>PdfUANotIdentified</c> code.</summary>
    /// <remarks>ISO 14289-1 (PDF/UA-1); veraPDF validation profiles</remarks>
    public static DiagnosticCode PdfUANotIdentified { get; } = new(nameof(PdfUANotIdentified), DiagnosticArea.Saving, DiagnosticSeverity.Information, "ISO 14289-1 (PDF/UA-1); veraPDF validation profiles");

    /// <summary>Gets the <c>WellTaggedNonConformant</c> code.</summary>
    /// <remarks>Well-Tagged PDF 1.0; veraPDF validation profiles</remarks>
    public static DiagnosticCode WellTaggedNonConformant { get; } = new(nameof(WellTaggedNonConformant), DiagnosticArea.Saving, DiagnosticSeverity.Warning, "Well-Tagged PDF 1.0; veraPDF validation profiles");

    /// <summary>Gets the <c>WellTaggedNotDeclared</c> code.</summary>
    /// <remarks>Well-Tagged PDF 1.0; veraPDF validation profiles</remarks>
    public static DiagnosticCode WellTaggedNotDeclared { get; } = new(nameof(WellTaggedNotDeclared), DiagnosticArea.Saving, DiagnosticSeverity.Information, "Well-Tagged PDF 1.0; veraPDF validation profiles");

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
    ];
}
