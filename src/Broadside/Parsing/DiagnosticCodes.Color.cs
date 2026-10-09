namespace Broadside.Parsing;

/// <summary>Diagnostic codes for colour spaces, colour operators and colour conversion (issue #77).</summary>
internal static partial class DiagnosticCodes
{
    // Colour spaces (§8.6).
    public const string ColorSpaceInvalid = nameof(ColorSpaceInvalid);
    public const string ColorSpaceEntryInvalid = nameof(ColorSpaceEntryInvalid);
    public const string ColorSpaceCycle = nameof(ColorSpaceCycle);
    public const string ColorSpaceTooDeep = nameof(ColorSpaceTooDeep);
    public const string IccProfileInvalid = nameof(IccProfileInvalid);
    public const string IccComponentCountMismatch = nameof(IccComponentCountMismatch);
    public const string IndexedLookupInvalid = nameof(IndexedLookupInvalid);
    public const string TintTransformInvalid = nameof(TintTransformInvalid);
    public const string DeviceNColorantsInvalid = nameof(DeviceNColorantsInvalid);
    public const string DefaultColorSpaceInvalid = nameof(DefaultColorSpaceInvalid);

    // Colour operators in content streams (§8.6.8, Table 73).
    public const string ContentColorSpaceMissing = nameof(ContentColorSpaceMissing);
    public const string ContentColorSpaceAbbreviated = nameof(ContentColorSpaceAbbreviated);
    public const string ContentColorOperandCount = nameof(ContentColorOperandCount);
    public const string ContentColorOperatorMismatch = nameof(ContentColorOperatorMismatch);
    public const string ContentColorOperatorInvalid = nameof(ContentColorOperatorInvalid);
    public const string ContentColorOperatorIgnored = nameof(ContentColorOperatorIgnored);
    public const string ContentPatternMissing = nameof(ContentPatternMissing);
    public const string ColorComponentLimitExceeded = nameof(ColorComponentLimitExceeded);
}
