using System.Diagnostics.CodeAnalysis;

namespace Broadside.Layout.Flow;

/// <summary>
/// Converts Markdown to a tagged PDF through the document builder: <c>PdfDocument.CreateFromMarkdown(markdown)</c>. Visible once
/// <c>Broadside.Layout.Flow</c> is imported.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8 (tagged PDF) and ISO 14289-2 §8.2 (logical structure): with a language and
/// <see cref="DocumentBuilder.WithPdfUAConformance"/> set by <c>configure</c>, the document saves as PDF/UA-2. See
/// <see cref="ContainerBuilder.Markdown"/> for how each construct is written.
/// </remarks>
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "C# 14 extension blocks, which the analyzer sees as nested types.")]
public static class PdfDocumentMarkdownExtensions
{
    /// <summary>The entry points on the type.</summary>
    extension(PdfDocument)
    {
        /// <summary>
        /// Writes a new document holding <paramref name="markdown"/>. <paramref name="configure"/> runs first, for metadata, the theme,
        /// page setup and conformance; the Markdown is then added as content after anything it described. The first level-1 heading becomes the title
        /// when <paramref name="configure"/> sets none.
        /// </summary>
        /// <param name="markdown">The Markdown. Every string is valid Markdown.</param>
        /// <param name="options">The conversion options, or null for the defaults.</param>
        /// <param name="configure">Configures the document before the Markdown is added, or null.</param>
        /// <returns>The document, ready to save; dispose it when done.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="markdown"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A declared conformance's requirement is unmet, such as PDF/UA without a language.</exception>
        /// <remarks>ISO 32000-2 §14.3.3 (the title) and §14.8 (tagged PDF).</remarks>
        public static PdfDocument CreateFromMarkdown(string markdown, MarkdownOptions? options = null, Action<DocumentBuilder>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(markdown);
            return PdfDocument.Create(doc =>
            {
                configure?.Invoke(doc);
                doc.Content(c => c.AddMarkdown(markdown, options, titleFromHeading: true));
            });
        }
    }
}
