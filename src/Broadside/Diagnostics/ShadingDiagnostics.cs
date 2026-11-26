namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about shadings, patterns and the sh operator.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.7. Each code is reported under <see cref="DiagnosticArea.Shading"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ShadingDiagnostics
{
    /// <summary>Gets the <c>ShadingTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingTypeInvalid { get; } = new(nameof(ShadingTypeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A shading is not a dictionary or stream, or its ShadingType is missing or not 1 to 7; the shading is not usable and paints nothing.");

    /// <summary>Gets the <c>ShadingColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingColorSpaceInvalid { get; } = new(nameof(ShadingColorSpaceInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A shading has no ColorSpace, a ColorSpace that is not a color space, or one that is not allowed such as Pattern or Indexed with a function; the shading is not usable.");

    /// <summary>Gets the <c>ShadingColorSpaceAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingColorSpaceAbbreviated { get; } = new(nameof(ShadingColorSpaceAbbreviated), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A shading names its color space with CS instead of ColorSpace; the value is used as if the full name had been written.");

    /// <summary>Gets the <c>ShadingFunctionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingFunctionInvalid { get; } = new(nameof(ShadingFunctionInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A shading has no Function where its type requires one, or the Function does not fit the expected inputs and outputs; surplus outputs are ignored, otherwise the shading is not usable.");

    /// <summary>Gets the <c>ShadingCoordsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingCoordsInvalid { get; } = new(nameof(ShadingCoordsInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The Coords entry of an axial or radial shading has the wrong number of values, or a radial shading has a negative radius; a negative radius is read as zero.");

    /// <summary>Gets the <c>ShadingEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingEntryInvalid { get; } = new(nameof(ShadingEntryInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "An entry of a shading, such as Domain, BBox, AntiAlias or VerticesPerRow, is missing or has the wrong form; the default for that entry is used.");

    /// <summary>Gets the <c>ShadingExtendInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingExtendInvalid { get; } = new(nameof(ShadingExtendInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The Extend entry of a shading is not an array of two booleans; neither end of the shading is extended.");

    /// <summary>Gets the <c>ShadingBackgroundInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingBackgroundInvalid { get; } = new(nameof(ShadingBackgroundInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The Background entry of a shading does not have one number per component of its color space; it is ignored.");

    /// <summary>Gets the <c>ShadingDecodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingDecodeInvalid { get; } = new(nameof(ShadingDecodeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The Decode entry of a mesh shading is missing or has too few numbers; the mesh cannot be decoded and the shading is not usable.");

    /// <summary>Gets the <c>ShadingBitsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingBitsInvalid { get; } = new(nameof(ShadingBitsInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A mesh bits-per-coordinate, component or flag entry is missing, outside 1 to 32, or not one of the allowed values; the value is read as given or the mesh is not decoded.");

    /// <summary>Gets the <c>ShadingNotStream</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode ShadingNotStream { get; } = new(nameof(ShadingNotStream), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A mesh shading of Type 4 to 7 is a dictionary instead of a stream, so it has no vertex data; the shading is not usable.");

    /// <summary>Gets the <c>MeshDataTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshDataTruncated { get; } = new(nameof(MeshDataTruncated), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The data of a mesh shading ends inside a vertex, triangle or patch; the complete elements before the end are kept.");

    /// <summary>Gets the <c>MeshEdgeFlagInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshEdgeFlagInvalid { get; } = new(nameof(MeshEdgeFlagInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "An edge flag in a mesh shading is not allowed or continues a triangle or patch that does not exist; the data it covers is skipped.");

    /// <summary>Gets the <c>MeshLatticeIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshLatticeIncomplete { get; } = new(nameof(MeshLatticeIncomplete), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The last row of a lattice mesh is incomplete, or the lattice has fewer than two rows; the incomplete row is dropped.");

    /// <summary>Gets the <c>MeshSizeExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshSizeExceeded { get; } = new(nameof(MeshSizeExceeded), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "A mesh shading has more vertices than the supported limit; the remaining vertices are dropped.");

    /// <summary>Gets the <c>MeshPatchesUnpadded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4, Tables 77 to 85</remarks>
    public static DiagnosticCode MeshPatchesUnpadded { get; } = new(nameof(MeshPatchesUnpadded), DiagnosticArea.Shading, DiagnosticSeverity.Information, "ISO 32000-2 §8.7.4, Tables 77 to 85",
        "The patches of a mesh shading are not padded to whole bytes as the specification requires. This is recorded only; the data is read back to back.");

    /// <summary>Gets the <c>PatternTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternTypeInvalid { get; } = new(nameof(PatternTypeInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Error, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75",
        "A pattern is not a dictionary or stream, a tiling pattern is not a stream, or the PatternType is missing or not 1 or 2; the pattern is not usable.");

    /// <summary>Gets the <c>PatternEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternEntryInvalid { get; } = new(nameof(PatternEntryInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75",
        "A pattern entry such as PaintType, TilingType, Matrix or the Shading of a shading pattern is missing or invalid; a default is used or the pattern is not usable.");

    /// <summary>Gets the <c>TilingPatternStepInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode TilingPatternStepInvalid { get; } = new(nameof(TilingPatternStepInvalid), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75",
        "The XStep or YStep of a tiling pattern is missing or zero; the bounding box width or height is used, or the pattern paints nothing if there is no box.");

    /// <summary>Gets the <c>TilingPatternBBoxMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode TilingPatternBBoxMissing { get; } = new(nameof(TilingPatternBBoxMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75",
        "A tiling pattern has no usable BBox; a box of zero to XStep and zero to YStep is used.");

    /// <summary>Gets the <c>PatternResourcesMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75</remarks>
    public static DiagnosticCode PatternResourcesMissing { get; } = new(nameof(PatternResourcesMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.2, §8.7.3, Tables 74 and 75",
        "A tiling pattern has no Resources dictionary; its cell uses the resources of the stream that paints with it.");

    /// <summary>Gets the <c>ContentShadingMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentShadingMissing { get; } = new(nameof(ContentShadingMissing), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3",
        "An sh operator names a shading that is not in the resources Shading dictionary; nothing is painted.");

    /// <summary>Gets the <c>ContentPatternRecursion</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentPatternRecursion { get; } = new(nameof(ContentPatternRecursion), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3",
        "A tiling pattern cell paints with the same pattern, directly or through other patterns or forms; the inner cell is left empty.");

    /// <summary>Gets the <c>ContentNestingTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.2, §8.7.3</remarks>
    public static DiagnosticCode ContentNestingTooDeep { get; } = new(nameof(ContentNestingTooDeep), DiagnosticArea.Shading, DiagnosticSeverity.Warning, "ISO 32000-2 §8.7.4.2, §8.7.3",
        "Form XObjects, patterns, Type 3 glyphs or soft masks nest deeper than the limit; the innermost one is not run.");

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
