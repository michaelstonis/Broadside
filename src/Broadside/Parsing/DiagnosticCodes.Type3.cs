using Broadside.Diagnostics;

namespace Broadside.Parsing;

/// <summary>Diagnostic codes for Type 3 fonts and their glyph descriptions (issue #57; ISO 32000-2 §9.6.4).</summary>
internal static partial class DiagnosticCodes
{
    // The font dictionary (Table 110), reported on the font's object.
    public static string FontType3FontMatrixInvalid => Type3Diagnostics.FontType3FontMatrixInvalid.Name;
    public static string FontType3CharProcsInvalid => Type3Diagnostics.FontType3CharProcsInvalid.Name;

    // Glyph descriptions run as content streams (Table 111), reported once per glyph description.
    public static string ContentType3GlyphMetricsMissing => Type3Diagnostics.ContentType3GlyphMetricsMissing.Name;
    public static string ContentType3GlyphMetricsMisplaced => Type3Diagnostics.ContentType3GlyphMetricsMisplaced.Name;
    public static string ContentType3GlyphRecursion => Type3Diagnostics.ContentType3GlyphRecursion.Name;
}
