namespace Broadside.Parsing;

/// <summary>Diagnostic codes for Type 3 fonts and their glyph descriptions (issue #57; ISO 32000-2 §9.6.4).</summary>
internal static partial class DiagnosticCodes
{
    // The font dictionary (Table 110), reported on the font's object.
    public const string FontType3FontMatrixInvalid = nameof(FontType3FontMatrixInvalid);
    public const string FontType3CharProcsInvalid = nameof(FontType3CharProcsInvalid);

    // Glyph descriptions run as content streams (Table 111), reported once per glyph description.
    public const string ContentType3GlyphMetricsMissing = nameof(ContentType3GlyphMetricsMissing);
    public const string ContentType3GlyphMetricsMisplaced = nameof(ContentType3GlyphMetricsMisplaced);
    public const string ContentType3GlyphRecursion = nameof(ContentType3GlyphRecursion);
}
