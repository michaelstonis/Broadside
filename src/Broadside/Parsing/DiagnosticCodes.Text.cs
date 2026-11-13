using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for mapping character codes to Unicode (issue #58; ISO 32000-2 §9.10).</summary>
internal static partial class DiagnosticCodes
{
    // The ToUnicode entry and its CMap (§9.10.3), reported on the ToUnicode stream (or the font, for a name).
    public static string ToUnicodeInvalid => TextDiagnostics.ToUnicodeInvalid.Name;
    public static string ToUnicodeIdentityName => TextDiagnostics.ToUnicodeIdentityName.Name;
    public static string ToUnicodeDestinationInvalid => TextDiagnostics.ToUnicodeDestinationInvalid.Name;
    public static string ToUnicodeBfRangeOverflow => TextDiagnostics.ToUnicodeBfRangeOverflow.Name;
    public static string ToUnicodeCidMapping => TextDiagnostics.ToUnicodeCidMapping.Name;

    // Looking up shown codes (§9.10.2), each reported once per font.
    public static string ToUnicodeCodeLengthMismatch => TextDiagnostics.ToUnicodeCodeLengthMismatch.Name;
    public static string GlyphNameNonStandard => TextDiagnostics.GlyphNameNonStandard.Name;
    public static string TextUcs2CmapMissing => TextDiagnostics.TextUcs2CmapMissing.Name;
    public static string TextUnicodeUnmapped => TextDiagnostics.TextUnicodeUnmapped.Name;

    // A Registry-Ordering-UCS2 resource a font resolver supplied (§9.10.2 step d).
    public static string CidToUnicodeResourceInvalid => TextDiagnostics.CidToUnicodeResourceInvalid.Name;
}
