namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about color spaces, color operators and color conversion.</summary>
/// <remarks>
/// <para>ISO 32000-2 §8.6. Each code is reported under <see cref="DiagnosticArea.Color"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class ColorDiagnostics
{
    /// <summary>Gets the <c>ColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceInvalid { get; } = new(nameof(ColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>ColorSpaceEntryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceEntryInvalid { get; } = new(nameof(ColorSpaceEntryInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>ColorSpaceCycle</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceCycle { get; } = new(nameof(ColorSpaceCycle), DiagnosticArea.Color, DiagnosticSeverity.Error, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>ColorSpaceTooDeep</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode ColorSpaceTooDeep { get; } = new(nameof(ColorSpaceTooDeep), DiagnosticArea.Color, DiagnosticSeverity.Error, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>IccProfileInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IccProfileInvalid { get; } = new(nameof(IccProfileInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>IccComponentCountMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IccComponentCountMismatch { get; } = new(nameof(IccComponentCountMismatch), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>IndexedLookupInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode IndexedLookupInvalid { get; } = new(nameof(IndexedLookupInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>TintTransformInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode TintTransformInvalid { get; } = new(nameof(TintTransformInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>DeviceNColorantsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode DeviceNColorantsInvalid { get; } = new(nameof(DeviceNColorantsInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>DefaultColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode DefaultColorSpaceInvalid { get; } = new(nameof(DefaultColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>GroupColorSpaceInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode GroupColorSpaceInvalid { get; } = new(nameof(GroupColorSpaceInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>SoftMaskColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6</remarks>
    public static DiagnosticCode SoftMaskColorSpaceMissing { get; } = new(nameof(SoftMaskColorSpaceMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6");

    /// <summary>Gets the <c>ContentColorSpaceMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorSpaceMissing { get; } = new(nameof(ContentColorSpaceMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentColorSpaceAbbreviated</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorSpaceAbbreviated { get; } = new(nameof(ContentColorSpaceAbbreviated), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentColorOperandCount</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperandCount { get; } = new(nameof(ContentColorOperandCount), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentColorOperatorMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorMismatch { get; } = new(nameof(ContentColorOperatorMismatch), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentColorOperatorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorInvalid { get; } = new(nameof(ContentColorOperatorInvalid), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentColorOperatorIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentColorOperatorIgnored { get; } = new(nameof(ContentColorOperatorIgnored), DiagnosticArea.Color, DiagnosticSeverity.Information, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ContentPatternMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ContentPatternMissing { get; } = new(nameof(ContentPatternMissing), DiagnosticArea.Color, DiagnosticSeverity.Warning, "ISO 32000-2 §8.6.8 Table 73");

    /// <summary>Gets the <c>ColorComponentLimitExceeded</c> code.</summary>
    /// <remarks>ISO 32000-2 §8.6.8 Table 73</remarks>
    public static DiagnosticCode ColorComponentLimitExceeded { get; } = new(nameof(ColorComponentLimitExceeded), DiagnosticArea.Color, DiagnosticSeverity.Information, "ISO 32000-2 §8.6.8 Table 73");

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
