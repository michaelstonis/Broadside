namespace Broadside.Parsing;

/// <summary>Diagnostic codes for shadings, patterns and the <c>sh</c> operator (issue #79).</summary>
internal static partial class DiagnosticCodes
{
    // Shadings (§8.7.4, Tables 77 to 85).
    public const string ShadingTypeInvalid = nameof(ShadingTypeInvalid);
    public const string ShadingColorSpaceInvalid = nameof(ShadingColorSpaceInvalid);
    public const string ShadingColorSpaceAbbreviated = nameof(ShadingColorSpaceAbbreviated);
    public const string ShadingFunctionInvalid = nameof(ShadingFunctionInvalid);
    public const string ShadingCoordsInvalid = nameof(ShadingCoordsInvalid);
    public const string ShadingEntryInvalid = nameof(ShadingEntryInvalid);
    public const string ShadingExtendInvalid = nameof(ShadingExtendInvalid);
    public const string ShadingBackgroundInvalid = nameof(ShadingBackgroundInvalid);
    public const string ShadingDecodeInvalid = nameof(ShadingDecodeInvalid);
    public const string ShadingBitsInvalid = nameof(ShadingBitsInvalid);
    public const string ShadingNotStream = nameof(ShadingNotStream);
    public const string MeshDataTruncated = nameof(MeshDataTruncated);
    public const string MeshEdgeFlagInvalid = nameof(MeshEdgeFlagInvalid);
    public const string MeshLatticeIncomplete = nameof(MeshLatticeIncomplete);
    public const string MeshSizeExceeded = nameof(MeshSizeExceeded);

    // Patterns (§8.7.2, §8.7.3, Tables 74 and 75).
    public const string PatternTypeInvalid = nameof(PatternTypeInvalid);
    public const string PatternEntryInvalid = nameof(PatternEntryInvalid);
    public const string TilingPatternStepInvalid = nameof(TilingPatternStepInvalid);
    public const string TilingPatternBBoxMissing = nameof(TilingPatternBBoxMissing);
    public const string PatternResourcesMissing = nameof(PatternResourcesMissing);

    // Content streams (§8.7.4.2 sh, §8.7.3 pattern cells).
    public const string ContentShadingMissing = nameof(ContentShadingMissing);
    public const string ContentPatternRecursion = nameof(ContentPatternRecursion);
    public const string ContentNestingTooDeep = nameof(ContentNestingTooDeep);
}
