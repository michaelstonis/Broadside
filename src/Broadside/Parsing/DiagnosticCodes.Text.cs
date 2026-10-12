namespace Broadside.Parsing;

/// <summary>Diagnostic codes for mapping character codes to Unicode (issue #58; ISO 32000-2 §9.10).</summary>
internal static partial class DiagnosticCodes
{
    // The ToUnicode entry and its CMap (§9.10.3), reported on the ToUnicode stream (or the font, for a name).
    public const string ToUnicodeInvalid = nameof(ToUnicodeInvalid);
    public const string ToUnicodeIdentityName = nameof(ToUnicodeIdentityName);
    public const string ToUnicodeDestinationInvalid = nameof(ToUnicodeDestinationInvalid);
    public const string ToUnicodeBfRangeOverflow = nameof(ToUnicodeBfRangeOverflow);
    public const string ToUnicodeCidMapping = nameof(ToUnicodeCidMapping);

    // Looking up shown codes (§9.10.2), each reported once per font.
    public const string ToUnicodeCodeLengthMismatch = nameof(ToUnicodeCodeLengthMismatch);
    public const string GlyphNameNonStandard = nameof(GlyphNameNonStandard);
    public const string TextUcs2CmapMissing = nameof(TextUcs2CmapMissing);
    public const string TextUnicodeUnmapped = nameof(TextUnicodeUnmapped);

    // A Registry-Ordering-UCS2 resource a font resolver supplied (§9.10.2 step d).
    public const string CidToUnicodeResourceInvalid = nameof(CidToUnicodeResourceInvalid);
}
