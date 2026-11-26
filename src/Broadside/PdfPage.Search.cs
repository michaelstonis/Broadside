using Broadside.Text;

namespace Broadside;

/// <content>Text search (issue #623): ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4.</content>
public sealed partial class PdfPage
{
    /// <summary>Finds every occurrence of a query in the page's text, in reading order.</summary>
    /// <param name="query">What to find and how to compare.</param>
    /// <returns>The matches, with this page's index (-1 when the page is no longer in its document); empty when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4. Searches the text <see cref="ExtractText()"/> returns (artifacts and hidden content
    /// left out), so the matches' glyph indices index its <see cref="TextPage.Glyphs"/>; extraction's repairs are recorded in
    /// <see cref="PdfDocument.Diagnostics"/> as usual. The rules are <see cref="TextQuery"/>'s.
    /// </remarks>
    public IReadOnlyList<TextMatch> FindText(TextQuery query) => FindText(query, new TextExtractionOptions());

    /// <summary>Finds every occurrence of a query in the text extracted with options, in reading order.</summary>
    /// <param name="query">What to find and how to compare.</param>
    /// <param name="options">What to extract (artifacts, hidden content, reading order) and the content run's options.</param>
    /// <returns>The matches, with this page's index (-1 when the page is no longer in its document); empty when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <exception cref="OperationCanceledException">The content options' cancellation token was canceled.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4. As <see cref="FindText(TextQuery)"/>, over <see cref="ExtractText(TextExtractionOptions)"/>.</remarks>
    public IReadOnlyList<TextMatch> FindText(TextQuery query, TextExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);
        TextPage page = ExtractText(options, null);
        return page.Find(query, _document.Pages.IndexOf(this), options.ContentOptions?.CancellationToken ?? default);
    }
}
