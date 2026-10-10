namespace Broadside.Parsing;

/// <summary>Diagnostic codes for Type 1 font programs (issue #52): layout, clear text, private dictionary and charstrings.</summary>
internal static partial class DiagnosticCodes
{
    // Program layout (ISO 32000-2 §9.9, Table 125; Type 1 Font Format §7.2; TN 5040 §3.3).
    public const string FontType1PfbWrapped = nameof(FontType1PfbWrapped);
    public const string FontType1Length1Repaired = nameof(FontType1Length1Repaired);
    public const string FontType1Length2Repaired = nameof(FontType1Length2Repaired);
    public const string FontType1HexEexec = nameof(FontType1HexEexec);
    public const string FontType1NoEexec = nameof(FontType1NoEexec);

    // Font and private dictionaries (Type 1 Font Format chapters 2, 5 and 10).
    public const string FontType1FontMatrixInvalid = nameof(FontType1FontMatrixInvalid);
    public const string FontType1EncodingInvalid = nameof(FontType1EncodingInvalid);
    public const string FontType1PrivateMissing = nameof(FontType1PrivateMissing);
    public const string FontType1EntryInvalid = nameof(FontType1EntryInvalid);
    public const string FontType1CharStringsMissing = nameof(FontType1CharStringsMissing);
    public const string FontType1NotdefMissing = nameof(FontType1NotdefMissing);

    // Charstrings (Type 1 Font Format chapters 6 and 8; TN 5015): what has no Type 2 counterpart. The rest are the shared
    // FontCharstring* codes (DiagnosticCodes.CharStrings.cs).
    public const string FontType1CharstringTruncated = nameof(FontType1CharstringTruncated);
    public const string FontType1FlexMalformed = nameof(FontType1FlexMalformed);
    public const string FontType1BlendUnavailable = nameof(FontType1BlendUnavailable);
    public const string FontType1NoWidth = nameof(FontType1NoWidth);
    public const string FontType1GlyphTooComplex = nameof(FontType1GlyphTooComplex);
}
