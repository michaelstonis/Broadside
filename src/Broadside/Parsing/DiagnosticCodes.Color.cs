using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for colour spaces, colour operators and colour conversion (issue #77).</summary>
internal static partial class DiagnosticCodes
{
    // Colour spaces (§8.6).
    public static string ColorSpaceInvalid => ColorDiagnostics.ColorSpaceInvalid.Name;
    public static string ColorSpaceEntryInvalid => ColorDiagnostics.ColorSpaceEntryInvalid.Name;
    public static string ColorSpaceCycle => ColorDiagnostics.ColorSpaceCycle.Name;
    public static string ColorSpaceTooDeep => ColorDiagnostics.ColorSpaceTooDeep.Name;
    public static string IccProfileInvalid => ColorDiagnostics.IccProfileInvalid.Name;
    public static string IccComponentCountMismatch => ColorDiagnostics.IccComponentCountMismatch.Name;
    public static string IndexedLookupInvalid => ColorDiagnostics.IndexedLookupInvalid.Name;
    public static string TintTransformInvalid => ColorDiagnostics.TintTransformInvalid.Name;
    public static string DeviceNColorantsInvalid => ColorDiagnostics.DeviceNColorantsInvalid.Name;
    public static string DefaultColorSpaceInvalid => ColorDiagnostics.DefaultColorSpaceInvalid.Name;
    public static string GroupColorSpaceInvalid => ColorDiagnostics.GroupColorSpaceInvalid.Name;
    public static string SoftMaskColorSpaceMissing => ColorDiagnostics.SoftMaskColorSpaceMissing.Name;

    // Colour operators in content streams (§8.6.8, Table 73).
    public static string ContentColorSpaceMissing => ColorDiagnostics.ContentColorSpaceMissing.Name;
    public static string ContentColorSpaceAbbreviated => ColorDiagnostics.ContentColorSpaceAbbreviated.Name;
    public static string ContentColorOperandCount => ColorDiagnostics.ContentColorOperandCount.Name;
    public static string ContentColorOperatorMismatch => ColorDiagnostics.ContentColorOperatorMismatch.Name;
    public static string ContentColorOperatorInvalid => ColorDiagnostics.ContentColorOperatorInvalid.Name;
    public static string ContentColorOperatorIgnored => ColorDiagnostics.ContentColorOperatorIgnored.Name;
    public static string ContentPatternMissing => ColorDiagnostics.ContentPatternMissing.Name;
    public static string ColorComponentLimitExceeded => ColorDiagnostics.ColorComponentLimitExceeded.Name;
}
