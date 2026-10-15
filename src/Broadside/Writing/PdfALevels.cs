namespace Broadside.Writing;

/// <summary>What each <see cref="PdfAConformance"/> level stands for: the part of ISO 19005 and the conformance level (issues #201, #202).</summary>
/// <remarks>ISO 19005-2 and ISO 19005-3, 6.6.4 (veraPDF rules 6.6.4-2 and 6.6.4-3: <c>pdfaid:part</c>, <c>pdfaid:conformance</c>).</remarks>
internal static class PdfALevels
{
    /// <summary>The part of ISO 19005 the level belongs to: 2 or 3.</summary>
    public static int Part(this PdfAConformance conformance) =>
        conformance is PdfAConformance.PdfA3b or PdfAConformance.PdfA3u ? 3 : 2;

    /// <summary>The conformance level, as <c>pdfaid:conformance</c> states it: <c>B</c> or <c>U</c>.</summary>
    public static string Level(this PdfAConformance conformance) =>
        conformance is PdfAConformance.PdfA2u or PdfAConformance.PdfA3u ? "U" : "B";

    /// <summary>Whether the level needs a Unicode value for every glyph shown (level U, 6.2.11.7.2).</summary>
    public static bool NeedsUnicode(this PdfAConformance conformance) => conformance.Level() == "U";

    /// <summary>Whether the level allows any embedded file as an associated file (PDF/A-3, 6.8), and with it <c>AF</c> on PDF 1.7.</summary>
    public static bool AllowsAssociatedFiles(this PdfAConformance conformance) => conformance.Part() == 3;

    /// <summary>The level's name, as in <c>PDF/A-2b</c>.</summary>
    public static string Describe(this PdfAConformance conformance) =>
        conformance == PdfAConformance.None ? "no PDF/A" : $"PDF/A-{conformance.Part()}{conformance.Level().ToLowerInvariant()}";
}
