using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for Type 1 font programs (issue #52): layout, clear text, private dictionary and charstrings.</summary>
internal static partial class DiagnosticCodes
{
    // Program layout (ISO 32000-2 §9.9, Table 125; Type 1 Font Format §7.2; TN 5040 §3.3).
    public static string FontType1PfbWrapped => Type1Diagnostics.FontType1PfbWrapped.Name;
    public static string FontType1Length1Repaired => Type1Diagnostics.FontType1Length1Repaired.Name;
    public static string FontType1Length2Repaired => Type1Diagnostics.FontType1Length2Repaired.Name;
    public static string FontType1HexEexec => Type1Diagnostics.FontType1HexEexec.Name;
    public static string FontType1NoEexec => Type1Diagnostics.FontType1NoEexec.Name;

    // Font and private dictionaries (Type 1 Font Format chapters 2, 5 and 10).
    public static string FontType1FontMatrixInvalid => Type1Diagnostics.FontType1FontMatrixInvalid.Name;
    public static string FontType1EncodingInvalid => Type1Diagnostics.FontType1EncodingInvalid.Name;
    public static string FontType1PrivateMissing => Type1Diagnostics.FontType1PrivateMissing.Name;
    public static string FontType1EntryInvalid => Type1Diagnostics.FontType1EntryInvalid.Name;
    public static string FontType1CharStringsMissing => Type1Diagnostics.FontType1CharStringsMissing.Name;
    public static string FontType1NotdefMissing => Type1Diagnostics.FontType1NotdefMissing.Name;

    // Charstrings (Type 1 Font Format chapters 6 and 8; TN 5015): what has no Type 2 counterpart. The rest are the shared
    // FontCharstring* codes (DiagnosticCodes.CharStrings.cs).
    public static string FontType1CharstringTruncated => Type1Diagnostics.FontType1CharstringTruncated.Name;
    public static string FontType1FlexMalformed => Type1Diagnostics.FontType1FlexMalformed.Name;
    public static string FontType1BlendUnavailable => Type1Diagnostics.FontType1BlendUnavailable.Name;
    public static string FontType1NoWidth => Type1Diagnostics.FontType1NoWidth.Name;
    public static string FontType1GlyphTooComplex => Type1Diagnostics.FontType1GlyphTooComplex.Name;
}
