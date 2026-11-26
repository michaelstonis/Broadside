using Broadside.Content;
using Broadside.Text;

namespace Broadside;

/// <content>Text search (issue #623): ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4.</content>
public sealed partial class PdfDocument
{
    /// <summary>Finds every occurrence of a query in the document's text, page by page, in reading order within a page.</summary>
    /// <param name="query">What to find and how to compare.</param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the search: checked while a page's content is read and between matches.</param>
    /// <returns>
    /// The matches, read lazily: each page is extracted as <see cref="PdfPage.FindText(TextQuery)"/> extracts it when the enumeration
    /// reaches it, and nothing of it is kept once its matches are returned.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages (when the enumeration starts).</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in a page's content, its fonts or the structure tree.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">
    /// A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; the matches of the pages before it have been returned.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4; the rules are <see cref="TextQuery"/>'s. Extraction's repairs are recorded in
    /// <see cref="Diagnostics"/> as usual. A read: other reads and renders may run concurrently, no mutation may.
    /// </remarks>
    public IEnumerable<TextMatch> FindText(TextQuery query, Range? pages = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FindTextIterator(query, pages ?? Range.All, cancellationToken);
    }

    private IEnumerable<TextMatch> FindTextIterator(TextQuery query, Range pages, CancellationToken cancellationToken)
    {
        (int first, int count) = pages.GetOffsetAndLength(Pages.Count);
        TextExtractionOptions options = new TextExtractionOptions().WithContentOptions(new ContentOptions().WithCancellation(cancellationToken));
        for (int index = first; index < first + count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextPage page = Pages[index].ExtractText(options, null);
            foreach (TextMatch match in page.Find(query, index, cancellationToken))
            {
                yield return match;
            }
        }
    }
}
