namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about CMaps and showing text in composite fonts.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.7.5, §9.7.6. Each code is reported under <see cref="DiagnosticArea.CMap"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CMapDiagnostics
{
    /// <summary>Gets the <c>CMapSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapSyntaxInvalid { get; } = new(nameof(CMapSyntaxInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapEntryInvalid { get; } = new(nameof(CMapEntryInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapEntryLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapEntryLimitExceeded { get; } = new(nameof(CMapEntryLimitExceeded), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapCodespaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapCodespaceMissing { get; } = new(nameof(CMapCodespaceMissing), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapOperatorNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapOperatorNotAllowed { get; } = new(nameof(CMapOperatorNotAllowed), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapUseCMapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapInvalid { get; } = new(nameof(CMapUseCMapInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapUseCMapCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapCycle { get; } = new(nameof(CMapUseCMapCycle), DiagnosticArea.CMap, DiagnosticSeverity.Error, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapUseCMapTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapTooDeep { get; } = new(nameof(CMapUseCMapTooDeep), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapWritingModeMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapWritingModeMismatch { get; } = new(nameof(CMapWritingModeMismatch), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapUnavailable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUnavailable { get; } = new(nameof(CMapUnavailable), DiagnosticArea.CMap, DiagnosticSeverity.Information, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7");

    /// <summary>Gets the <c>CMapCodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.6.2, §9.7.6.3</remarks>
    public static DiagnosticCode CMapCodeInvalid { get; } = new(nameof(CMapCodeInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.6.2, §9.7.6.3");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        CMapSyntaxInvalid,
        CMapEntryInvalid,
        CMapEntryLimitExceeded,
        CMapCodespaceMissing,
        CMapOperatorNotAllowed,
        CMapUseCMapInvalid,
        CMapUseCMapCycle,
        CMapUseCMapTooDeep,
        CMapWritingModeMismatch,
        CMapUnavailable,
        CMapCodeInvalid,
    ];
}
