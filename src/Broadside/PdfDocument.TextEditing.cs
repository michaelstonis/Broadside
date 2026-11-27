using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Text;
using Broadside.Text.Editing;

namespace Broadside;

/// <content>Find and replace across a document (issue #641): ISO 32000-2 §9.4.3, §9.10.3 and §14.9.4.</content>
public sealed partial class PdfDocument
{
    /// <summary>Finds every match of a query and replaces it with other text, in the content that shows it.</summary>
    /// <param name="query">What to find.</param>
    /// <param name="replacement">
    /// The new text; empty deletes the matches. For a regular expression query it may name groups as
    /// <see cref="Regex.Replace(string, string)"/> does (<c>$1</c>, <c>${name}</c>, <c>$$</c>); for a literal query it is taken as it is.
    /// </param>
    /// <param name="options">The fallback font, where later glyphs go, and whether replacement text is updated; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <returns>How many matches were replaced, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="replacement"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first <see cref="TextEditDiagnostics"/> code; the document is then unchanged.</exception>
    /// <exception cref="RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.4.3, §9.9.2, §9.10.3 and §14.9.4. Each page's text is searched as <see cref="FindText"/> searches it, every page
    /// before anything is changed, and each match is replaced as <see cref="PdfPage.ReplaceText"/> replaces one. A mutation.
    /// </remarks>
    public PdfTextEditResult ReplaceText(TextQuery query, string replacement, PdfTextReplaceOptions? options = null, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        (int first, int count) = (pages ?? Range.All).GetOffsetAndLength(Pages.Count);
        var session = new TextEditSession(this, options ?? new PdfTextReplaceOptions());
        Regex? regex = query.IsRegex ? query.GetRegex() : null;
        for (int index = first; index < first + count; index++)
        {
            PdfPage page = Pages[index];
            TextPage text = page.ExtractText(new TextExtractionOptions(), null);
            IReadOnlyList<TextMatch> matches = text.Find(query, index, default);
            if (matches.Count == 0)
            {
                continue;
            }

            string searchable = text.Searchable.Text;
            session.AddPage(page, text, matches.Select(match => (match, Expand(regex, searchable, match, replacement))));
        }

        return session.Run();
    }

    /// <summary>Finds every match of a query and deletes it from the content that shows it: <see cref="ReplaceText"/> with an empty replacement.</summary>
    /// <param name="query">What to find.</param>
    /// <param name="options">Where later glyphs go (<see cref="PdfTextFit.ShiftLine"/> closes the gap) and whether replacement text is updated; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <returns>How many matches were deleted, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="DiagnosticException">In strict mode, for the first <see cref="TextEditDiagnostics"/> code; the document is then unchanged.</exception>
    /// <exception cref="RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §9.4.3 and §14.9.4. As <see cref="ReplaceText"/>.</remarks>
    public PdfTextEditResult DeleteText(TextQuery query, PdfTextReplaceOptions? options = null, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ReplaceText(query, string.Empty, options, pages);
    }

    /// <summary>A regular expression match's replacement with its groups substituted; a literal query's replacement as it is.</summary>
    private static string Expand(Regex? regex, string searchable, TextMatch match, string replacement)
    {
        if (regex is null || match.TextStart < 0 || match.TextStart > searchable.Length)
        {
            return replacement;
        }

        Match found = regex.Match(searchable, match.TextStart);
        return found.Success && found.Index == match.TextStart && found.Length == match.Text.Length ? found.Result(replacement) : replacement;
    }
}
