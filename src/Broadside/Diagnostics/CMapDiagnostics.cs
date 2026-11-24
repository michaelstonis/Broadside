namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about CMaps and showing text in composite fonts.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.7.5, §9.7.6. Each code is reported under <see cref="DiagnosticArea.CMap"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class CMapDiagnostics
{
    /// <summary>Gets the <c>CMapSyntaxInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapSyntaxInvalid { get; } = new(nameof(CMapSyntaxInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap file contains syntax that cannot be parsed. The parser skips the damaged part and continues.");

    /// <summary>Gets the <c>CMapEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapEntryInvalid { get; } = new(nameof(CMapEntryInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap entry is malformed, such as a WMode other than 0 or 1, a code that is not a hexadecimal string, a code longer than four bytes or a non-integer CID. The entry is dropped or repaired.");

    /// <summary>Gets the <c>CMapEntryLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapEntryLimitExceeded { get; } = new(nameof(CMapEntryLimitExceeded), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap has more entries than the configured limit. The remaining entries are dropped.");

    /// <summary>Gets the <c>CMapCodespaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapCodespaceMissing { get; } = new(nameof(CMapCodespaceMissing), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap defines no codespace range. Codes take the lengths used by its mappings.");

    /// <summary>Gets the <c>CMapOperatorNotAllowed</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapOperatorNotAllowed { get; } = new(nameof(CMapOperatorNotAllowed), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap uses an operator that is not allowed in a PDF CMap, such as rearranged font blocks, a nonzero usefont operand, or bfchar in an encoding CMap. The block is skipped or handled as the nearest valid form.");

    /// <summary>Gets the <c>CMapUseCMapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapInvalid { get; } = new(nameof(CMapUseCMapInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap refers to another CMap through UseCMap in an invalid way, such as a wrong type or a conflict with the stream's own entry. The reference is ignored or the stream's UseCMap is used.");

    /// <summary>Gets the <c>CMapUseCMapCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapCycle { get; } = new(nameof(CMapUseCMapCycle), DiagnosticArea.CMap, DiagnosticSeverity.Error, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap uses itself through its UseCMap chain. The chain is cut at the repeat.");

    /// <summary>Gets the <c>CMapUseCMapTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUseCMapTooDeep { get; } = new(nameof(CMapUseCMapTooDeep), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "CMaps nest through usecmap deeper than the allowed limit. The deeper CMaps are ignored.");

    /// <summary>Gets the <c>CMapWritingModeMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapWritingModeMismatch { get; } = new(nameof(CMapWritingModeMismatch), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap stream's WMode entry differs from the writing mode in the CMap file. The value in the file is used.");

    /// <summary>Gets the <c>CMapUnavailable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7</remarks>
    public static DiagnosticCode CMapUnavailable { get; } = new(nameof(CMapUnavailable), DiagnosticArea.CMap, DiagnosticSeverity.Information, "ISO 32000-2 §9.7.5; Adobe Technical Note #5014 §7",
        "A CMap that is referenced by name is not available, for example a predefined CMap that is not installed. The CMap is read without it, or a fallback such as Identity is used.");

    /// <summary>Gets the <c>CMapCodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.6.2, §9.7.6.3</remarks>
    public static DiagnosticCode CMapCodeInvalid { get; } = new(nameof(CMapCodeInvalid), DiagnosticArea.CMap, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.6.2, §9.7.6.3",
        "A shown string has a code that lies in no codespace range of the font's CMap. The code consumes bytes as the CMap allows and shows the glyph of CID 0.");

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
