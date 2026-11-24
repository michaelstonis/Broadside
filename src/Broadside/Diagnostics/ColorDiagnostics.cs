namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about color spaces, color operators and color conversion.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.6. Each code is reported under <see cref="DiagnosticArea.Color"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ColorDiagnostics
{
    /// <summary>Gets the <c>ColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceInvalid { get; } = new(nameof(ColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "A color space definition is malformed, for example a Separation colorant that is not a name, an Indexed base or hival that is invalid, or a DeviceN with no names; DeviceGray is used instead.");

    /// <summary>Gets the <c>ColorSpaceEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceEntryInvalid { get; } = new(nameof(ColorSpaceEntryInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "An entry in a color space dictionary, such as CalGray or CalRGB Gamma, Matrix or a Lab Range, is invalid; a default value is used for it.");

    /// <summary>Gets the <c>ColorSpaceCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceCycle { get; } = new(nameof(ColorSpaceCycle), DiagnosticArea.Color, DiagnosticSeverity.Error, "ISO 32000-2 §8.6",
        "A color space refers back to itself while it is being read; the cyclic reference is cut and DeviceGray is used.");

    /// <summary>Gets the <c>ColorSpaceTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceTooDeep { get; } = new(nameof(ColorSpaceTooDeep), DiagnosticArea.Color, DiagnosticSeverity.Error, "ISO 32000-2 §8.6",
        "Color spaces nest deeper than the supported depth, or a value is not a color space name or array; DeviceGray is used instead.");

    /// <summary>Gets the <c>IccProfileInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IccProfileInvalid { get; } = new(nameof(IccProfileInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "An ICCBased stream does not hold a usable ICC profile, for example it is too short, lacks the signature, has an unsupported class or claims more data than exists. The alternate space is used or the profile is truncated.");

    /// <summary>Gets the <c>IccComponentCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IccComponentCountMismatch { get; } = new(nameof(IccComponentCountMismatch), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "The N entry of an ICCBased stream differs from the component count in the ICC profile header; the count from the profile is used.");

    /// <summary>Gets the <c>IndexedLookupInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IndexedLookupInvalid { get; } = new(nameof(IndexedLookupInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "An Indexed color space has no lookup string or stream, or the lookup table length does not match hival and the base space. A missing table falls back to DeviceGray; short tables read as zeros and extra bytes are ignored.");

    /// <summary>Gets the <c>TintTransformInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode TintTransformInvalid { get; } = new(nameof(TintTransformInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "The tint transform of a Separation or DeviceN color space is not a usable function or has the wrong input or output count; tints are converted as subtractive ink on the alternate space, or missing outputs read as zero.");

    /// <summary>Gets the <c>DeviceNColorantsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode DeviceNColorantsInvalid { get; } = new(nameof(DeviceNColorantsInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "The colorant names array of a DeviceN color space holds a non-name, repeats a colorant or names All; invalid entries are skipped.");

    /// <summary>Gets the <c>DefaultColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode DefaultColorSpaceInvalid { get; } = new(nameof(DefaultColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "A default color space (DefaultGray, DefaultRGB or DefaultCMYK) has the wrong component count or is a Lab, Indexed or Pattern space; it is ignored.");

    /// <summary>Gets the <c>GroupColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode GroupColorSpaceInvalid { get; } = new(nameof(GroupColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "A transparency group names a color space that cannot be used as a blending color space; the group blends in its parent color space instead.");

    /// <summary>Gets the <c>SoftMaskColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode SoftMaskColorSpaceMissing { get; } = new(nameof(SoftMaskColorSpaceMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6",
        "The group of a luminosity soft mask has no usable color space; the number of components of the backdrop decides the blending space.");

    /// <summary>Gets the <c>ContentColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorSpaceMissing { get; } = new(nameof(ContentColorSpaceMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "A color space operator names a color space that is neither a family name nor in the resources ColorSpace dictionary; DeviceGray is used.");

    /// <summary>Gets the <c>ContentColorSpaceAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorSpaceAbbreviated { get; } = new(nameof(ContentColorSpaceAbbreviated), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "A color space name that is an inline image abbreviation (G, RGB or CMYK) is used outside an inline image; it is read as the matching device space.");

    /// <summary>Gets the <c>ContentColorOperandCount</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperandCount { get; } = new(nameof(ContentColorOperandCount), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "A color operator has fewer or more components than its color space needs; with too few the operator is skipped, with too many the last ones are used.");

    /// <summary>Gets the <c>ContentColorOperatorMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorMismatch { get; } = new(nameof(ContentColorOperatorMismatch), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "The sc or SC operator is used in an ICCBased, Separation or DeviceN color space, which expect scn and SCN; the color is set anyway.");

    /// <summary>Gets the <c>ContentColorOperatorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorInvalid { get; } = new(nameof(ContentColorOperatorInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "A color operator is not valid for the current color space, such as a pattern name in a non-Pattern space or other operands in a Pattern space; the operator is skipped.");

    /// <summary>Gets the <c>ContentColorOperatorIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorIgnored { get; } = new(nameof(ContentColorOperatorIgnored), DiagnosticArea.Color, DiagnosticSeverity.Information, "ISO 32000-2 §8.6.8 Table 73",
        "A color operator or colored painting appears inside a d1 glyph or an uncolored tiling pattern, where colors must not be specified. This is recorded only; the operator or object is ignored as the specification requires.");

    /// <summary>Gets the <c>ContentPatternMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentPatternMissing { get; } = new(nameof(ContentPatternMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73",
        "An scn or SCN operator names a pattern that is not in the resources Pattern dictionary; the color paints nothing.");

    /// <summary>Gets the <c>ColorComponentLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ColorComponentLimitExceeded { get; } = new(nameof(ColorComponentLimitExceeded), DiagnosticArea.Color, DiagnosticSeverity.Information, "ISO 32000-2 §8.6.8 Table 73",
        "A DeviceN color space has more colorants than the supported limit. This is recorded only; colors set in it are painted through its alternate color space.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        ColorSpaceInvalid,
        ColorSpaceEntryInvalid,
        ColorSpaceCycle,
        ColorSpaceTooDeep,
        IccProfileInvalid,
        IccComponentCountMismatch,
        IndexedLookupInvalid,
        TintTransformInvalid,
        DeviceNColorantsInvalid,
        DefaultColorSpaceInvalid,
        GroupColorSpaceInvalid,
        SoftMaskColorSpaceMissing,
        ContentColorSpaceMissing,
        ContentColorSpaceAbbreviated,
        ContentColorOperandCount,
        ContentColorOperatorMismatch,
        ContentColorOperatorInvalid,
        ContentColorOperatorIgnored,
        ContentPatternMissing,
        ColorComponentLimitExceeded,
    ];
}
