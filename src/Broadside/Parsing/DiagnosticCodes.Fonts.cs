using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for font dictionaries, simple-font encodings and widths, and font descriptors (issue #49).</summary>
internal static partial class DiagnosticCodes
{
    // Font dictionaries, Standard 14 fonts, encodings, widths and font descriptors (§9.5, §9.6, §9.8).
    public static string FontTypeInvalid => FontDiagnostics.FontTypeInvalid.Name;
    public static string FontSubtypeInvalid => FontDiagnostics.FontSubtypeInvalid.Name;
    public static string FontBaseFontMissing => FontDiagnostics.FontBaseFontMissing.Name;
    public static string FontStandard14Alias => FontDiagnostics.FontStandard14Alias.Name;
    public static string FontStandard14EntriesIncomplete => FontDiagnostics.FontStandard14EntriesIncomplete.Name;
    public static string FontDescriptorMissing => FontDiagnostics.FontDescriptorMissing.Name;
    public static string FontDescriptorInvalid => FontDiagnostics.FontDescriptorInvalid.Name;
    public static string FontWidthsMissing => FontDiagnostics.FontWidthsMissing.Name;
    public static string FontWidthsInvalid => FontDiagnostics.FontWidthsInvalid.Name;
    public static string FontEncodingInvalid => FontDiagnostics.FontEncodingInvalid.Name;
    public static string FontEncodingIgnored => FontDiagnostics.FontEncodingIgnored.Name;
    public static string FontDifferencesInvalid => FontDiagnostics.FontDifferencesInvalid.Name;
    public static string FontBuiltInEncodingUnavailable => FontDiagnostics.FontBuiltInEncodingUnavailable.Name;
    public static string FontGlyphMissing => FontDiagnostics.FontGlyphMissing.Name;

    // Font programs and the font program parser extension point (§9.9; issue #50).
    public static string FontProgramUnsupported => FontDiagnostics.FontProgramUnsupported.Name;
    public static string FontProgramFormatMismatch => FontDiagnostics.FontProgramFormatMismatch.Name;
    public static string FontProgramInvalid => FontDiagnostics.FontProgramInvalid.Name;
    public static string FontProgramTruncated => FontDiagnostics.FontProgramTruncated.Name;
    public static string FontTableInvalid => FontDiagnostics.FontTableInvalid.Name;
    public static string FontGlyphInvalid => FontDiagnostics.FontGlyphInvalid.Name;
    public static string FontCmapInvalid => FontDiagnostics.FontCmapInvalid.Name;

    // Character code to glyph id for TrueType fonts (§9.6.5.4; issue #50).
    public static string FontGlyphMappingFallback => FontDiagnostics.FontGlyphMappingFallback.Name;
}
