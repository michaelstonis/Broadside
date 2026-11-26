namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 3 fonts and their glyph descriptions.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.6.4. Each code is reported under <see cref="DiagnosticArea.Type3"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class Type3Diagnostics
{
    /// <summary>Gets the <c>FontType3FontMatrixInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 110</remarks>
    public static DiagnosticCode FontType3FontMatrixInvalid { get; } = new(nameof(FontType3FontMatrixInvalid), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 110",
        "A Type 3 font's FontMatrix is not an array of six numbers. The default matrix of 0.001 scale is used.");

    /// <summary>Gets the <c>FontType3CharProcsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 110</remarks>
    public static DiagnosticCode FontType3CharProcsInvalid { get; } = new(nameof(FontType3CharProcsInvalid), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 110",
        "A Type 3 font has no CharProcs dictionary, or a CharProcs entry is not a content stream. The affected glyphs paint nothing.");

    /// <summary>Gets the <c>ContentType3GlyphMetricsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphMetricsMissing { get; } = new(nameof(ContentType3GlyphMetricsMissing), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111",
        "A Type 3 glyph description does not begin with a d0 or d1 operator. It runs as if it began with d0, so the glyph sets its own color; the advance still comes from Widths.");

    /// <summary>Gets the <c>ContentType3GlyphMetricsMisplaced</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphMetricsMisplaced { get; } = new(nameof(ContentType3GlyphMetricsMisplaced), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111",
        "A d0 or d1 operator appears in a Type 3 glyph description somewhere other than as its first operator. The misplaced operator is ignored.");

    /// <summary>Gets the <c>ContentType3GlyphRecursion</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphRecursion { get; } = new(nameof(ContentType3GlyphRecursion), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111",
        "A Type 3 glyph description shows text in its own font, so the glyph would run inside itself. The inner glyph is not run again but still advances.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        FontType3FontMatrixInvalid,
        FontType3CharProcsInvalid,
        ContentType3GlyphMetricsMissing,
        ContentType3GlyphMetricsMisplaced,
        ContentType3GlyphRecursion,
    ];
}
