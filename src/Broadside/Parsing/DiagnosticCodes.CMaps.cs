namespace Broadside.Parsing;

/// <summary>Diagnostic codes for CMaps, Type 0 fonts and CIDFonts (issue #53).</summary>
internal static partial class DiagnosticCodes
{
    // CMap files (§9.7.5; Adobe TN 5014 §7).
    public const string CMapSyntaxInvalid = nameof(CMapSyntaxInvalid);
    public const string CMapEntryInvalid = nameof(CMapEntryInvalid);
    public const string CMapEntryLimitExceeded = nameof(CMapEntryLimitExceeded);
    public const string CMapCodespaceMissing = nameof(CMapCodespaceMissing);
    public const string CMapOperatorNotAllowed = nameof(CMapOperatorNotAllowed);
    public const string CMapUseCMapInvalid = nameof(CMapUseCMapInvalid);
    public const string CMapUseCMapCycle = nameof(CMapUseCMapCycle);
    public const string CMapUseCMapTooDeep = nameof(CMapUseCMapTooDeep);
    public const string CMapWritingModeMismatch = nameof(CMapWritingModeMismatch);
    public const string CMapUnavailable = nameof(CMapUnavailable);
    public const string CMapInvalid = nameof(CMapInvalid);

    // Showing text in a composite font (§9.7.6.2, §9.7.6.3).
    public const string CMapCodeInvalid = nameof(CMapCodeInvalid);

    // Type 0 font and CIDFont dictionaries (§9.7.3, §9.7.4, §9.7.6.1).
    public const string Type0EncodingMissing = nameof(Type0EncodingMissing);
    public const string Type0DescendantFontsInvalid = nameof(Type0DescendantFontsInvalid);
    public const string Type0IdentityNotEmbedded = nameof(Type0IdentityNotEmbedded);
    public const string CidSystemInfoInvalid = nameof(CidSystemInfoInvalid);
    public const string CidSystemInfoMismatch = nameof(CidSystemInfoMismatch);
    public const string CidFontSubtypeInvalid = nameof(CidFontSubtypeInvalid);
    public const string CidFontWidthsInvalid = nameof(CidFontWidthsInvalid);
    public const string CidFontVerticalMetricsInvalid = nameof(CidFontVerticalMetricsInvalid);
    public const string CidToGidMapMissing = nameof(CidToGidMapMissing);
    public const string CidToGidMapInvalid = nameof(CidToGidMapInvalid);
    public const string CidFontNotEmbedded = nameof(CidFontNotEmbedded);
}
