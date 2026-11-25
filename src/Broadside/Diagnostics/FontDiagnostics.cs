namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about font dictionaries, encodings, widths, font descriptors, font programs, Type 0 fonts and CIDFonts, and font substitution.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.5 to §9.9. Each code is reported under <see cref="DiagnosticArea.Font"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FontDiagnostics
{
    /// <summary>Gets the <c>Type0EncodingMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0EncodingMissing { get; } = new(nameof(Type0EncodingMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A Type 0 font has no valid Encoding CMap name or stream; its character codes are read as Identity-H.");

    /// <summary>Gets the <c>Type0DescendantFontsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0DescendantFontsInvalid { get; } = new(nameof(Type0DescendantFontsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A Type 0 font's DescendantFonts entry is not a one-element array holding a CIDFont. Extra or wrongly typed entries are tolerated where possible; otherwise the font's glyphs cannot be shown.");

    /// <summary>Gets the <c>Type0IdentityNotEmbedded</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0IdentityNotEmbedded { get; } = new(nameof(Type0IdentityNotEmbedded), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "Identity-H or Identity-V is used with a TrueType CIDFont that is not embedded, which the specification does not allow. The font is still processed as written.");

    /// <summary>Gets the <c>CidSystemInfoInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidSystemInfoInvalid { get; } = new(nameof(CidSystemInfoInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A CIDFont or CMap has a missing or malformed CIDSystemInfo dictionary lacking Registry, Ordering or Supplement. A malformed one in a CMap is ignored.");

    /// <summary>Gets the <c>CidSystemInfoMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidSystemInfoMismatch { get; } = new(nameof(CidSystemInfoMismatch), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "The character collection (Registry and Ordering) of a CMap differs from that of its CIDFont. The CIDs are used as they are.");

    /// <summary>Gets the <c>CidFontSubtypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontSubtypeInvalid { get; } = new(nameof(CidFontSubtypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A CIDFont's Subtype is missing or is neither CIDFontType0 nor CIDFontType2. The font is read as CIDFontType2.");

    /// <summary>Gets the <c>CidFontWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontWidthsInvalid { get; } = new(nameof(CidFontWidthsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A CIDFont's DW or W entry has the wrong type or malformed groups. The default width of 1000 or the DW value is used for the affected CIDs.");

    /// <summary>Gets the <c>CidFontVerticalMetricsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontVerticalMetricsInvalid { get; } = new(nameof(CidFontVerticalMetricsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A CIDFont's DW2 or W2 vertical metrics entry has the wrong type or malformed groups. Default vertical metrics or the DW2 value are used for the affected CIDs.");

    /// <summary>Gets the <c>CidToGidMapMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidToGidMapMissing { get; } = new(nameof(CidToGidMapMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "An embedded CIDFontType2 font has no usable CIDToGIDMap, which must be a stream or the name Identity. CIDs are used directly as glyph ids.");

    /// <summary>Gets the <c>CidToGidMapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidToGidMapInvalid { get; } = new(nameof(CidToGidMapInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1",
        "A CIDToGIDMap stream has an odd byte count or an entry that is neither a stream nor the name Identity. A trailing odd byte is ignored, otherwise CIDs are used as glyph ids.");

    /// <summary>Gets the <c>FontSubstituted</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontSubstituted { get; } = new(nameof(FontSubstituted), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2",
        "A font is not embedded, so a substitute font program is used in its place. Text keeps the widths declared by the original font.");

    /// <summary>Gets the <c>FontProgramNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontProgramNotFound { get; } = new(nameof(FontProgramNotFound), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2",
        "No font program is embedded or can be found for a font. Its glyphs are not drawn until a Standard 14 fonts package or a font resolver is configured.");

    /// <summary>Gets the <c>FontSubstituteUnreadable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontSubstituteUnreadable { get; } = new(nameof(FontSubstituteUnreadable), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2",
        "A substitute font program was found for a non-embedded font but cannot be read by the available font program parsers. The glyphs of that font are not drawn.");

    /// <summary>Gets the <c>FontTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontTypeInvalid { get; } = new(nameof(FontTypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font dictionary's Type entry is missing or is not Font. The dictionary is read as a font anyway.");

    /// <summary>Gets the <c>FontSubtypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontSubtypeInvalid { get; } = new(nameof(FontSubtypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font dictionary's Subtype is missing or is not one of the defined font types. The font is read as a Type 1 font.");

    /// <summary>Gets the <c>FontBaseFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontBaseFontMissing { get; } = new(nameof(FontBaseFontMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font dictionary has no BaseFont name. The font is processed without it, which can limit font substitution.");

    /// <summary>Gets the <c>FontStandard14Alias</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontStandard14Alias { get; } = new(nameof(FontStandard14Alias), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font that is not embedded is treated as one of the Standard 14 fonts. The metrics of that standard font are used for it.");

    /// <summary>Gets the <c>FontStandard14EntriesIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontStandard14EntriesIncomplete { get; } = new(nameof(FontStandard14EntriesIncomplete), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A Standard 14 font supplies only some of FirstChar, LastChar, Widths and FontDescriptor instead of all or none. The entries that are present are used.");

    /// <summary>Gets the <c>FontDescriptorMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDescriptorMissing { get; } = new(nameof(FontDescriptorMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A simple font that is not a Standard 14 font has no font descriptor. Default font metrics and flags are used.");

    /// <summary>Gets the <c>FontDescriptorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDescriptorInvalid { get; } = new(nameof(FontDescriptorInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font descriptor is not a dictionary or has entries that violate the font descriptor requirements. The offending entries are ignored and defaults are used.");

    /// <summary>Gets the <c>FontWidthsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontWidthsMissing { get; } = new(nameof(FontWidthsMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A simple font that is not a Standard 14 font has no Widths array. Each code takes the width of its glyph from the font program, or 0 if none is known.");

    /// <summary>Gets the <c>FontWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontWidthsInvalid { get; } = new(nameof(FontWidthsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A font's Widths entry is not an array or contains invalid values. A non-array entry is ignored, and other deviations are repaired where possible.");

    /// <summary>Gets the <c>FontEncodingInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontEncodingInvalid { get; } = new(nameof(FontEncodingInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A simple font's Encoding entry deviates from the allowed forms. The encoding is repaired where possible and otherwise the font's built-in encoding is used.");

    /// <summary>Gets the <c>FontEncodingIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontEncodingIgnored { get; } = new(nameof(FontEncodingIgnored), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A named encoding is specified for a non-embedded symbolic font. The encoding is ignored and the font's built-in encoding is used, as other viewers do.");

    /// <summary>Gets the <c>FontDifferencesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDifferencesInvalid { get; } = new(nameof(FontDifferencesInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8",
        "An encoding's Differences array contains entries that are not allowed, such as a bad code or a missing glyph name. Offending entries are skipped.");

    /// <summary>Gets the <c>FontBuiltInEncodingUnavailable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontBuiltInEncodingUnavailable { get; } = new(nameof(FontBuiltInEncodingUnavailable), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8",
        "The font's built-in encoding cannot be determined, either because the embedded program's encoding is not read or because a non-embedded symbolic font has none. StandardEncoding stands in.");

    /// <summary>Gets the <c>FontGlyphMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontGlyphMissing { get; } = new(nameof(FontGlyphMissing), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8",
        "A Standard 14 font has no glyph for a requested glyph name. The glyph has a width of 0 and nothing is drawn for it.");

    /// <summary>Gets the <c>FontProgramUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramUnsupported { get; } = new(nameof(FontProgramUnsupported), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.9",
        "The font program is in a format that no available parser reads, or uses outline features that are not supported. The font's glyphs are not available.");

    /// <summary>Gets the <c>FontProgramFormatMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramFormatMismatch { get; } = new(nameof(FontProgramFormatMismatch), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9",
        "The font file stream is declared as one font program format but its data is another. The data is read as the format it actually holds.");

    /// <summary>Gets the <c>FontProgramInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramInvalid { get; } = new(nameof(FontProgramInvalid), DiagnosticArea.Font, DiagnosticSeverity.Error, "ISO 32000-2 §9.9",
        "A font program cannot be parsed or is unusable, for example it is malformed or lacks required tables. The font's glyphs are not available.");

    /// <summary>Gets the <c>FontProgramTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramTruncated { get; } = new(nameof(FontProgramTruncated), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9",
        "The font file stream's declared Length1 is larger than its data. The bytes that are present are read.");

    /// <summary>Gets the <c>FontTableInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontTableInvalid { get; } = new(nameof(FontTableInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9",
        "A TrueType or OpenType table directory is malformed, such as an unknown sfnt version, a bad font collection index, a wrong table count or a table past the end. The usable tables are read.");

    /// <summary>Gets the <c>FontGlyphInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontGlyphInvalid { get; } = new(nameof(FontGlyphInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9",
        "A glyph in a font program deviates from the font format, such as a malformed TrueType glyph description. The glyph is repaired or dropped.");

    /// <summary>Gets the <c>FontCmapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontCmapInvalid { get; } = new(nameof(FontCmapInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9",
        "A TrueType cmap table has subtables that map codes to glyphs past the end of the glyph list or point outside their data. The missing glyph is used for those codes.");

    /// <summary>Gets the <c>FontGlyphMappingFallback</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.5.4</remarks>
    public static DiagnosticCode FontGlyphMappingFallback { get; } = new(nameof(FontGlyphMappingFallback), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.5.4",
        "The specification leaves the glyph selection for a TrueType font to the processor, for example when conflicting flags are set. Broadside records which mapping it chose.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        Type0EncodingMissing,
        Type0DescendantFontsInvalid,
        Type0IdentityNotEmbedded,
        CidSystemInfoInvalid,
        CidSystemInfoMismatch,
        CidFontSubtypeInvalid,
        CidFontWidthsInvalid,
        CidFontVerticalMetricsInvalid,
        CidToGidMapMissing,
        CidToGidMapInvalid,
        FontSubstituted,
        FontProgramNotFound,
        FontSubstituteUnreadable,
        FontTypeInvalid,
        FontSubtypeInvalid,
        FontBaseFontMissing,
        FontStandard14Alias,
        FontStandard14EntriesIncomplete,
        FontDescriptorMissing,
        FontDescriptorInvalid,
        FontWidthsMissing,
        FontWidthsInvalid,
        FontEncodingInvalid,
        FontEncodingIgnored,
        FontDifferencesInvalid,
        FontBuiltInEncodingUnavailable,
        FontGlyphMissing,
        FontProgramUnsupported,
        FontProgramFormatMismatch,
        FontProgramInvalid,
        FontProgramTruncated,
        FontTableInvalid,
        FontGlyphInvalid,
        FontCmapInvalid,
        FontGlyphMappingFallback,
    ];
}
