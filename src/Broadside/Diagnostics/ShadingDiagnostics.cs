namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about shadings, patterns and the sh operator.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.7. Each code is reported under <see cref="DiagnosticArea.Shading"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ShadingDiagnostics
{
    /// <summary>Gets the <c>ShadingTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingTypeInvalid { get; } = new(nameof(ShadingTypeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingColorSpaceInvalid { get; } = new(nameof(ShadingColorSpaceInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingColorSpaceAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingColorSpaceAbbreviated { get; } = new(nameof(ShadingColorSpaceAbbreviated), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingFunctionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingFunctionInvalid { get; } = new(nameof(ShadingFunctionInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingCoordsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingCoordsInvalid { get; } = new(nameof(ShadingCoordsInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingEntryInvalid { get; } = new(nameof(ShadingEntryInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingExtendInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingExtendInvalid { get; } = new(nameof(ShadingExtendInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingBackgroundInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingBackgroundInvalid { get; } = new(nameof(ShadingBackgroundInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingDecodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingDecodeInvalid { get; } = new(nameof(ShadingDecodeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingBitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingBitsInvalid { get; } = new(nameof(ShadingBitsInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>ShadingNotStream</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingNotStream { get; } = new(nameof(ShadingNotStream), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>MeshDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshDataTruncated { get; } = new(nameof(MeshDataTruncated), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>MeshEdgeFlagInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshEdgeFlagInvalid { get; } = new(nameof(MeshEdgeFlagInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>MeshLatticeIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshLatticeIncomplete { get; } = new(nameof(MeshLatticeIncomplete), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>MeshSizeExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshSizeExceeded { get; } = new(nameof(MeshSizeExceeded), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>MeshPatchesUnpadded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshPatchesUnpadded { get; } = new(nameof(MeshPatchesUnpadded), DiagnosticArea.Shading, DiagnosticSeverity.Information, "ISO 32000-2 §8.7.4, Tables 77 to 85");

    /// <summary>Gets the <c>PatternTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternTypeInvalid { get; } = new(nameof(PatternTypeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75");

    /// <summary>Gets the <c>PatternEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternEntryInvalid { get; } = new(nameof(PatternEntryInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75");

    /// <summary>Gets the <c>TilingPatternStepInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode TilingPatternStepInvalid { get; } = new(nameof(TilingPatternStepInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75");

    /// <summary>Gets the <c>TilingPatternBBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode TilingPatternBBoxMissing { get; } = new(nameof(TilingPatternBBoxMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75");

    /// <summary>Gets the <c>PatternResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternResourcesMissing { get; } = new(nameof(PatternResourcesMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75");

    /// <summary>Gets the <c>ContentShadingMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentShadingMissing { get; } = new(nameof(ContentShadingMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3");

    /// <summary>Gets the <c>ContentPatternRecursion</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentPatternRecursion { get; } = new(nameof(ContentPatternRecursion), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3");

    /// <summary>Gets the <c>ContentNestingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentNestingTooDeep { get; } = new(nameof(ContentNestingTooDeep), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ShadingTypeInvalid,
        ShadingColorSpaceInvalid,
        ShadingColorSpaceAbbreviated,
        ShadingFunctionInvalid,
        ShadingCoordsInvalid,
        ShadingEntryInvalid,
        ShadingExtendInvalid,
        ShadingBackgroundInvalid,
        ShadingDecodeInvalid,
        ShadingBitsInvalid,
        ShadingNotStream,
        MeshDataTruncated,
        MeshEdgeFlagInvalid,
        MeshLatticeIncomplete,
        MeshSizeExceeded,
        MeshPatchesUnpadded,
        PatternTypeInvalid,
        PatternEntryInvalid,
        TilingPatternStepInvalid,
        TilingPatternBBoxMissing,
        PatternResourcesMissing,
        ContentShadingMissing,
        ContentPatternRecursion,
        ContentNestingTooDeep,
    ];
}
