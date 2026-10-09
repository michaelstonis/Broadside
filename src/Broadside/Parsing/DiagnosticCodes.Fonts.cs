namespace Broadside.Parsing;

/// <summary>Diagnostic codes for font dictionaries, simple-font encodings and widths, and font descriptors (issue #49).</summary>
internal static partial class DiagnosticCodes
{
    // Font dictionaries, Standard 14 fonts, encodings, widths and font descriptors (§9.5, §9.6, §9.8).
    public const string FontTypeInvalid = nameof(FontTypeInvalid);
    public const string FontSubtypeInvalid = nameof(FontSubtypeInvalid);
    public const string FontBaseFontMissing = nameof(FontBaseFontMissing);
    public const string FontStandard14Alias = nameof(FontStandard14Alias);
    public const string FontStandard14EntriesIncomplete = nameof(FontStandard14EntriesIncomplete);
    public const string FontDescriptorMissing = nameof(FontDescriptorMissing);
    public const string FontDescriptorInvalid = nameof(FontDescriptorInvalid);
    public const string FontWidthsMissing = nameof(FontWidthsMissing);
    public const string FontWidthsInvalid = nameof(FontWidthsInvalid);
    public const string FontEncodingInvalid = nameof(FontEncodingInvalid);
    public const string FontEncodingIgnored = nameof(FontEncodingIgnored);
    public const string FontDifferencesInvalid = nameof(FontDifferencesInvalid);
    public const string FontBuiltInEncodingUnavailable = nameof(FontBuiltInEncodingUnavailable);
    public const string FontGlyphMissing = nameof(FontGlyphMissing);

    // Font programs and the font program parser extension point (§9.9; issue #50).
    public const string FontProgramUnsupported = nameof(FontProgramUnsupported);
    public const string FontProgramFormatMismatch = nameof(FontProgramFormatMismatch);
    public const string FontProgramInvalid = nameof(FontProgramInvalid);
    public const string FontProgramTruncated = nameof(FontProgramTruncated);
    public const string FontTableInvalid = nameof(FontTableInvalid);
    public const string FontGlyphInvalid = nameof(FontGlyphInvalid);
    public const string FontCmapInvalid = nameof(FontCmapInvalid);

    // Character code to glyph id for TrueType fonts (§9.6.5.4; issue #50).
    public const string FontGlyphMappingFallback = nameof(FontGlyphMappingFallback);
}
