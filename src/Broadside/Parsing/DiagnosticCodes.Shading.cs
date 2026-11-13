using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for shadings, patterns and the <c>sh</c> operator (issue #79).</summary>
internal static partial class DiagnosticCodes
{
    // Shadings (§8.7.4, Tables 77 to 85).
    public static string ShadingTypeInvalid => ShadingDiagnostics.ShadingTypeInvalid.Name;
    public static string ShadingColorSpaceInvalid => ShadingDiagnostics.ShadingColorSpaceInvalid.Name;
    public static string ShadingColorSpaceAbbreviated => ShadingDiagnostics.ShadingColorSpaceAbbreviated.Name;
    public static string ShadingFunctionInvalid => ShadingDiagnostics.ShadingFunctionInvalid.Name;
    public static string ShadingCoordsInvalid => ShadingDiagnostics.ShadingCoordsInvalid.Name;
    public static string ShadingEntryInvalid => ShadingDiagnostics.ShadingEntryInvalid.Name;
    public static string ShadingExtendInvalid => ShadingDiagnostics.ShadingExtendInvalid.Name;
    public static string ShadingBackgroundInvalid => ShadingDiagnostics.ShadingBackgroundInvalid.Name;
    public static string ShadingDecodeInvalid => ShadingDiagnostics.ShadingDecodeInvalid.Name;
    public static string ShadingBitsInvalid => ShadingDiagnostics.ShadingBitsInvalid.Name;
    public static string ShadingNotStream => ShadingDiagnostics.ShadingNotStream.Name;
    public static string MeshDataTruncated => ShadingDiagnostics.MeshDataTruncated.Name;
    public static string MeshEdgeFlagInvalid => ShadingDiagnostics.MeshEdgeFlagInvalid.Name;
    public static string MeshLatticeIncomplete => ShadingDiagnostics.MeshLatticeIncomplete.Name;
    public static string MeshSizeExceeded => ShadingDiagnostics.MeshSizeExceeded.Name;
    public static string MeshPatchesUnpadded => ShadingDiagnostics.MeshPatchesUnpadded.Name;

    // Patterns (§8.7.2, §8.7.3, Tables 74 and 75).
    public static string PatternTypeInvalid => ShadingDiagnostics.PatternTypeInvalid.Name;
    public static string PatternEntryInvalid => ShadingDiagnostics.PatternEntryInvalid.Name;
    public static string TilingPatternStepInvalid => ShadingDiagnostics.TilingPatternStepInvalid.Name;
    public static string TilingPatternBBoxMissing => ShadingDiagnostics.TilingPatternBBoxMissing.Name;
    public static string PatternResourcesMissing => ShadingDiagnostics.PatternResourcesMissing.Name;

    // Content streams (§8.7.4.2 sh, §8.7.3 pattern cells).
    public static string ContentShadingMissing => ShadingDiagnostics.ContentShadingMissing.Name;
    public static string ContentPatternRecursion => ShadingDiagnostics.ContentPatternRecursion.Name;
    public static string ContentNestingTooDeep => ShadingDiagnostics.ContentNestingTooDeep.Name;
}
