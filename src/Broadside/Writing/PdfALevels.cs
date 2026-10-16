namespace Broadside.Writing;

/// <summary>What each <see cref="PdfAConformance"/> level stands for: the part of ISO 19005 and the conformance level (issues #201, #202, #203).</summary>
/// <remarks>
/// ISO 19005-2 and ISO 19005-3, 6.6.4 (veraPDF rules 6.6.4-2 and 6.6.4-3: <c>pdfaid:part</c>, <c>pdfaid:conformance</c>); ISO 19005-4,
/// 6.7.3 (rules 6.7.3-2, 6.7.3-3 and 6.7.3-5: <c>pdfaid:part</c>, <c>pdfaid:conformance</c> only for 4e and 4f, <c>pdfaid:rev</c>).
/// </remarks>
internal static class PdfALevels
{
    private static readonly PdfVersion Pdf17 = new(1, 7);
    private static readonly PdfVersion Pdf20 = new(2, 0);

    /// <summary>The part of ISO 19005 the level belongs to: 2, 3 or 4.</summary>
    public static int Part(this PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA3b or PdfAConformance.PdfA3u => 3,
        PdfAConformance.PdfA4 or PdfAConformance.PdfA4f => 4,
        _ => 2,
    };

    /// <summary>
    /// The conformance level, as <c>pdfaid:conformance</c> states it: <c>B</c>, <c>U</c> or <c>F</c>; <see langword="null"/> for
    /// PDF/A-4, which states none (6.7.3-3).
    /// </summary>
    public static string? Level(this PdfAConformance conformance) => conformance switch
    {
        PdfAConformance.PdfA2u or PdfAConformance.PdfA3u => "U",
        PdfAConformance.PdfA4 => null,
        PdfAConformance.PdfA4f => "F",
        _ => "B",
    };

    /// <summary>The revision year <c>pdfaid:rev</c> states (PDF/A-4, 6.7.3-5), or <see langword="null"/> for the parts that have none.</summary>
    public static string? Revision(this PdfAConformance conformance) => conformance.Part() == 4 ? "2020" : null;

    /// <summary>The PDF version the part is based on, the latest a conformant file may be: 1.7 for PDF/A-2 and -3, 2.0 for PDF/A-4.</summary>
    public static PdfVersion BaseVersion(this PdfAConformance conformance) => conformance.Part() == 4 ? Pdf20 : Pdf17;

    /// <summary>Whether the level needs a Unicode value for every glyph shown (level U, 6.2.11.7.2).</summary>
    public static bool NeedsUnicode(this PdfAConformance conformance) => conformance.Level() == "U";

    /// <summary>
    /// Whether the level allows <c>AF</c> arrays: PDF/A-3 (6.8) on its PDF 1.7 base, and PDF/A-4, whose PDF 2.0 base defines them.
    /// </summary>
    public static bool AllowsAssociatedFiles(this PdfAConformance conformance) => conformance.Part() >= 3;

    /// <summary>Whether the level allows an embedded file of any kind (PDF/A-3, 6.8; PDF/A-4f, 6.9), each with its relationship and MIME type.</summary>
    public static bool AllowsAnyEmbeddedFile(this PdfAConformance conformance) =>
        conformance.Part() == 3 || conformance == PdfAConformance.PdfA4f;

    /// <summary>The level's name, as in <c>PDF/A-2b</c> or <c>PDF/A-4</c>.</summary>
    public static string Describe(this PdfAConformance conformance) =>
        conformance == PdfAConformance.None ? "no PDF/A" : $"PDF/A-{conformance.Part()}{conformance.Level()?.ToLowerInvariant()}";
}
