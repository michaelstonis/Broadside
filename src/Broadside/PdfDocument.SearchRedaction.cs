using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Redaction;
using Broadside.Text;

namespace Broadside;

/// <content>Search and redact: queries and presets to Redact annotations or applied redaction (issue #633).</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Marks every match of a query for review: one Redact annotation per match, with a quadrilateral per line the match covers.
    /// Nothing is removed until <see cref="Redact(PdfRedactionOptions?)"/> applies the annotations.
    /// </summary>
    /// <param name="query">What to find, such as a <see cref="TextPatterns"/> preset.</param>
    /// <param name="options">
    /// The overlay the annotations describe (<c>IC</c>, <c>OverlayText</c>, <c>Repeat</c>) and the text searched
    /// (<see cref="PdfRedactionOptions.TextExtractionOptions"/>); <see langword="null"/> for a black fill over everything a page holds.
    /// </param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <returns>The new annotations, in page order and reading order within a page; empty when nothing matched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="InvalidOperationException">A page with a match is not an indirect object.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in a page's content, its fonts or the structure tree.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; no annotation is added.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.6.23 (Table 195: <c>QuadPoints</c>, <c>IC</c>, <c>OverlayText</c>) and §9.10. The matched text is written into
    /// the annotation's <c>Contents</c> only when <see cref="PdfRedactionOptions.OverlayText"/> is set, so by default the value being
    /// redacted is not copied into the file. Applying the annotations later removes what lies under their quadrilaterals, by area; to
    /// remove the matched glyphs exactly, use <see cref="Redact(TextQuery, PdfRedactionOptions?, Range?)"/>.
    /// </para>
    /// <para>Every page is searched before the first annotation is added. Records PDF 1.7. A mutation.</para>
    /// </remarks>
    public IReadOnlyList<PdfRedactAnnotation> AddRedactAnnotations(TextQuery query, PdfRedactionOptions? options = null, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        options ??= new PdfRedactionOptions();
        List<RedactionPageMatches> found = FindForRedaction([query], options, pages ?? Range.All);
        List<PdfRedactAnnotation> annotations = [];
        foreach (RedactionPageMatches page in found)
        {
            foreach (TextMatch match in page.Matches)
            {
                List<PdfQuadrilateral> quads = [.. match.Quads.Select(static quad => new PdfQuadrilateral(quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft))];
                if (quads.Count == 0)
                {
                    continue;
                }

                PdfRedactAnnotation annotation = page.Page.AddRedactAnnotation(quads, options);
                if (options.OverlayText is not null)
                {
                    annotation.Contents = match.Text;
                }

                annotations.Add(annotation);
            }
        }

        return annotations;
    }

    /// <summary>Finds every match of a query on every page and applies redaction to it at once, with the default options.</summary>
    /// <param name="query">What to find, such as a <see cref="TextPatterns"/> preset.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. As <see cref="Redact(TextQuery, PdfRedactionOptions?, Range?)"/>.</remarks>
    public PdfRedactionResult Redact(TextQuery query) => Redact(query, null, null);

    /// <summary>Finds every match of a query on every page and applies redaction to it at once.</summary>
    /// <param name="query">What to find, such as a <see cref="TextPatterns"/> preset.</param>
    /// <param name="options">The overlay, what is removed besides the glyphs, and the text searched; <see langword="null"/> for the defaults.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. As <see cref="Redact(TextQuery, PdfRedactionOptions?, Range?)"/>.</remarks>
    public PdfRedactionResult Redact(TextQuery query, PdfRedactionOptions? options) => Redact(query, options, null);

    /// <summary>
    /// Finds every match of a query and applies redaction to it at once: the matched glyphs are removed from the content, and, unless
    /// <see cref="PdfRedactionOptions.GlyphsOnly"/> is set, the images, paths and annotations under the match quads; the overlay is
    /// drawn over each quad.
    /// </summary>
    /// <param name="query">What to find, such as a <see cref="TextPatterns"/> preset.</param>
    /// <param name="options">The overlay, what is removed besides the glyphs, and the text searched; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>
    /// As <see cref="Redact(IEnumerable{TextQuery}, PdfRedactionOptions?, Range?)"/> with one query. ISO 32000-2 §12.5.6.23, §9.4.3
    /// and §9.10.
    /// </remarks>
    public PdfRedactionResult Redact(TextQuery query, PdfRedactionOptions? options, Range? pages)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Redact((IEnumerable<TextQuery>)[query], options, pages);
    }

    /// <summary>Finds every match of several queries on every page and applies redaction to them at once, with the default options.</summary>
    /// <param name="queries">What to find, such as several <see cref="TextPatterns"/> presets.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="queries"/> is or holds <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. As <see cref="Redact(IEnumerable{TextQuery}, PdfRedactionOptions?, Range?)"/>.</remarks>
    public PdfRedactionResult Redact(IEnumerable<TextQuery> queries) => Redact(queries, null, null);

    /// <summary>Finds every match of several queries on every page and applies redaction to them at once.</summary>
    /// <param name="queries">What to find, such as several <see cref="TextPatterns"/> presets.</param>
    /// <param name="options">The overlay, what is removed besides the glyphs, and the text searched; <see langword="null"/> for the defaults.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="queries"/> is or holds <see langword="null"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. As <see cref="Redact(IEnumerable{TextQuery}, PdfRedactionOptions?, Range?)"/>.</remarks>
    public PdfRedactionResult Redact(IEnumerable<TextQuery> queries, PdfRedactionOptions? options) => Redact(queries, options, null);

    /// <summary>
    /// Finds every match of several queries, each page's text extracted once for all of them, merges matches that overlap, and applies
    /// redaction to them at once, as <see cref="Redact(TextQuery, PdfRedactionOptions?, Range?)"/> does.
    /// </summary>
    /// <param name="queries">What to find, such as several <see cref="TextPatterns"/> presets.</param>
    /// <param name="options">The overlay, what is removed besides the glyphs, and the text searched; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based pages to search, such as <c>0..3</c>; every page when <see langword="null"/>.</param>
    /// <returns>What was removed, and the diagnostics recorded; nothing is changed when there is no match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="queries"/> is or holds <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is not within the document's pages.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <exception cref="System.Text.RegularExpressions.RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on a page; nothing is changed.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. A glyph is removed by identity, its character code and the corner of its box, not by
    /// area: a neighbor whose box overlaps a match through tight kerning or italics is kept. A match inside <c>ActualText</c> covers
    /// every glyph of the replaced sequence (§14.9.4), and they all go. The search includes hidden content and artifacts unless
    /// <see cref="PdfRedactionOptions.TextExtractionOptions"/> narrows it, so an invisible OCR layer (rendering mode 3) is removed with
    /// what it lies over.
    /// </para>
    /// <para>
    /// Lenient reading: when a match's glyphs cannot all be found again in the content, everything under its quads is removed instead and
    /// <c>RedactionMatchNotLocated</c> is recorded; in strict mode it throws and nothing is changed. Diagnostics never hold the matched
    /// text.
    /// </para>
    /// <para>
    /// Every page is searched before anything is changed. The removed content stays recoverable from the original bytes until the
    /// document is saved with <see cref="PdfRedactionResult.CreateSaveOptions"/>: never save a redacted document as an incremental
    /// update. A mutation.
    /// </para>
    /// </remarks>
    public PdfRedactionResult Redact(IEnumerable<TextQuery> queries, PdfRedactionOptions? options, Range? pages)
    {
        ArgumentNullException.ThrowIfNull(queries);
        List<TextQuery> list = [.. queries];
        foreach (TextQuery query in list)
        {
            ArgumentNullException.ThrowIfNull(query, nameof(queries));
        }

        options ??= new PdfRedactionOptions();
        return RedactionSession.ApplyMatches(this, FindForRedaction(list, options, pages ?? Range.All), options);
    }

    /// <summary>Runs the queries on each page's text, extracted once per page as redaction searches it, and merges overlapping matches.</summary>
    private List<RedactionPageMatches> FindForRedaction(List<TextQuery> queries, PdfRedactionOptions options, Range pages)
    {
        (int first, int count) = pages.GetOffsetAndLength(Pages.Count);
        TextExtractionOptions extraction = options.TextExtractionOptions ?? RedactionSession.DefaultExtraction();
        CancellationToken cancellationToken = extraction.ContentOptions?.CancellationToken ?? default;
        List<RedactionPageMatches> found = [];
        for (int index = first; index < first + count; index++)
        {
            if (queries.Count == 0)
            {
                break;
            }

            PdfPage page = Pages[index];
            TextPage text = page.ExtractText(extraction, null);
            List<TextMatch> matches = [];
            foreach (TextQuery query in queries)
            {
                matches.AddRange(text.Find(query, index, cancellationToken));
            }

            if (matches.Count > 0)
            {
                found.Add(new RedactionPageMatches(page, text, queries.Count == 1 ? matches : RedactionSession.Merge(matches)));
            }
        }

        return found;
    }
}
