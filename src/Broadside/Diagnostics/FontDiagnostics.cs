namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about font dictionaries, encodings, widths, font descriptors, font programs, Type 0 fonts and CIDFonts, and font substitution.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.5 to §9.9. Each code is reported under <see cref="DiagnosticArea.Font"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class FontDiagnostics
{
    /// <summary>Gets the <c>Type0EncodingMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0EncodingMissing { get; } = new(nameof(Type0EncodingMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>Type0DescendantFontsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0DescendantFontsInvalid { get; } = new(nameof(Type0DescendantFontsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>Type0IdentityNotEmbedded</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode Type0IdentityNotEmbedded { get; } = new(nameof(Type0IdentityNotEmbedded), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidSystemInfoInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidSystemInfoInvalid { get; } = new(nameof(CidSystemInfoInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidSystemInfoMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidSystemInfoMismatch { get; } = new(nameof(CidSystemInfoMismatch), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidFontSubtypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontSubtypeInvalid { get; } = new(nameof(CidFontSubtypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidFontWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontWidthsInvalid { get; } = new(nameof(CidFontWidthsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidFontVerticalMetricsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidFontVerticalMetricsInvalid { get; } = new(nameof(CidFontVerticalMetricsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidToGidMapMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidToGidMapMissing { get; } = new(nameof(CidToGidMapMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>CidToGidMapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1</remarks>
    public static DiagnosticCode CidToGidMapInvalid { get; } = new(nameof(CidToGidMapInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.7.3, §9.7.4, §9.7.6.1");

    /// <summary>Gets the <c>FontSubstituted</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontSubstituted { get; } = new(nameof(FontSubstituted), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2");

    /// <summary>Gets the <c>FontProgramNotFound</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontProgramNotFound { get; } = new(nameof(FontProgramNotFound), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2");

    /// <summary>Gets the <c>FontSubstituteUnreadable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2</remarks>
    public static DiagnosticCode FontSubstituteUnreadable { get; } = new(nameof(FontSubstituteUnreadable), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.2.2, §9.8.1, §9.8.2");

    /// <summary>Gets the <c>FontTypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontTypeInvalid { get; } = new(nameof(FontTypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontSubtypeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontSubtypeInvalid { get; } = new(nameof(FontSubtypeInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontBaseFontMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontBaseFontMissing { get; } = new(nameof(FontBaseFontMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontStandard14Alias</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontStandard14Alias { get; } = new(nameof(FontStandard14Alias), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontStandard14EntriesIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontStandard14EntriesIncomplete { get; } = new(nameof(FontStandard14EntriesIncomplete), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontDescriptorMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDescriptorMissing { get; } = new(nameof(FontDescriptorMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontDescriptorInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDescriptorInvalid { get; } = new(nameof(FontDescriptorInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontWidthsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontWidthsMissing { get; } = new(nameof(FontWidthsMissing), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontWidthsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontWidthsInvalid { get; } = new(nameof(FontWidthsInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontEncodingInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontEncodingInvalid { get; } = new(nameof(FontEncodingInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontEncodingIgnored</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontEncodingIgnored { get; } = new(nameof(FontEncodingIgnored), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontDifferencesInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontDifferencesInvalid { get; } = new(nameof(FontDifferencesInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontBuiltInEncodingUnavailable</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontBuiltInEncodingUnavailable { get; } = new(nameof(FontBuiltInEncodingUnavailable), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontGlyphMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.5, §9.6, §9.8</remarks>
    public static DiagnosticCode FontGlyphMissing { get; } = new(nameof(FontGlyphMissing), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.5, §9.6, §9.8");

    /// <summary>Gets the <c>FontProgramUnsupported</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramUnsupported { get; } = new(nameof(FontProgramUnsupported), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontProgramFormatMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramFormatMismatch { get; } = new(nameof(FontProgramFormatMismatch), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontProgramInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramInvalid { get; } = new(nameof(FontProgramInvalid), DiagnosticArea.Font, DiagnosticSeverity.Error, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontProgramTruncated</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontProgramTruncated { get; } = new(nameof(FontProgramTruncated), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontTableInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontTableInvalid { get; } = new(nameof(FontTableInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontGlyphInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontGlyphInvalid { get; } = new(nameof(FontGlyphInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontCmapInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.9</remarks>
    public static DiagnosticCode FontCmapInvalid { get; } = new(nameof(FontCmapInvalid), DiagnosticArea.Font, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9");

    /// <summary>Gets the <c>FontGlyphMappingFallback</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.5.4</remarks>
    public static DiagnosticCode FontGlyphMappingFallback { get; } = new(nameof(FontGlyphMappingFallback), DiagnosticArea.Font, DiagnosticSeverity.Information, "ISO 32000-2 §9.6.5.4");

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
