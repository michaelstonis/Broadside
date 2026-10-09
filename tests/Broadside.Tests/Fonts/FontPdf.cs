using Broadside.Fonts;
using Broadside.Tests.Document;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Builds a one-page file in memory whose resources name one font, <c>/F1</c>, object 4; extra objects are numbered from 5. For
/// font dictionary variations the corpus does not need a file for.
/// </summary>
internal static class FontPdf
{
    /// <summary>Opens a file whose font F1 has the given dictionary body.</summary>
    public static PdfDocument Open(string fontDictionary, params string[] objects) => Open(fontDictionary, new PdfOptions(), objects);

    /// <summary>Opens a file whose font F1 has the given dictionary body, with options.</summary>
    public static PdfDocument Open(string fontDictionary, PdfOptions options, params string[] objects) =>
        PdfDocument.Open(
            new TestPdf().Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> >>",
                fontDictionary,
                .. objects,
            ]),
            options);

    /// <summary>Gets font F1 of the file's page.</summary>
    public static PdfFont Font(PdfDocument document) => Assert.IsAssignableFrom<PdfFont>(Assert.Single(document.Pages).GetFont("F1"));

    /// <summary>Gets font F1 of the file's page as a simple font.</summary>
    public static PdfSimpleFont SimpleFont(PdfDocument document) => Assert.IsAssignableFrom<PdfSimpleFont>(Font(document));

    /// <summary>The codes of the diagnostics recorded so far.</summary>
    public static string[] Codes(PdfDocument document) => [.. document.Diagnostics.Select(diagnostic => diagnostic.Code)];
}
