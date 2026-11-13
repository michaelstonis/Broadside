namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about Type 3 fonts and their glyph descriptions.</summary>
/// <remarks>
/// <para>ISO 32000-2 §9.6.4. Each code is reported under <see cref="DiagnosticArea.Type3"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class Type3Diagnostics
{
    /// <summary>Gets the <c>FontType3FontMatrixInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 110</remarks>
    public static DiagnosticCode FontType3FontMatrixInvalid { get; } = new(nameof(FontType3FontMatrixInvalid), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 110");

    /// <summary>Gets the <c>FontType3CharProcsInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 110</remarks>
    public static DiagnosticCode FontType3CharProcsInvalid { get; } = new(nameof(FontType3CharProcsInvalid), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 110");

    /// <summary>Gets the <c>ContentType3GlyphMetricsMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphMetricsMissing { get; } = new(nameof(ContentType3GlyphMetricsMissing), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111");

    /// <summary>Gets the <c>ContentType3GlyphMetricsMisplaced</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphMetricsMisplaced { get; } = new(nameof(ContentType3GlyphMetricsMisplaced), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111");

    /// <summary>Gets the <c>ContentType3GlyphRecursion</c> code.</summary>
    /// <remarks>ISO 32000-2 §9.6.4 Table 111</remarks>
    public static DiagnosticCode ContentType3GlyphRecursion { get; } = new(nameof(ContentType3GlyphRecursion), DiagnosticArea.Type3, DiagnosticSeverity.Warning, "ISO 32000-2 §9.6.4 Table 111");

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
