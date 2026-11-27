using Broadside.Graphics;
using Broadside.Redaction;
using Broadside.Text;

namespace Broadside;

/// <content>Applying redaction to search matches (issue #633).</content>
public sealed partial class PdfPage
{
    /// <summary>Applies redaction to matches found on this page with the default options: a black fill over each match quad.</summary>
    /// <param name="matches">The matches, from this page's <see cref="FindText(TextQuery)"/>; empty does nothing.</param>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="matches"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A match names another page.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the page is then unchanged.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. As <see cref="Redact(IReadOnlyList{TextMatch}, PdfRedactionOptions?)"/>.</remarks>
    public PdfRedactionResult Redact(IReadOnlyList<TextMatch> matches) => Redact(matches, null);

    /// <summary>
    /// Applies redaction to matches found on this page (<see cref="FindText(TextQuery)"/>): the matched glyphs are removed from the
    /// content, and, unless <see cref="PdfRedactionOptions.GlyphsOnly"/> is set, the images, paths and annotations under the match
    /// quads; the overlay is drawn over each quad.
    /// </summary>
    /// <param name="matches">The matches, from this page's <see cref="FindText(TextQuery)"/> or <see cref="FindText(TextQuery, TextExtractionOptions)"/>; empty does nothing.</param>
    /// <param name="options">The overlay, what is removed besides the glyphs, and the text the matches came from; <see langword="null"/> for the defaults.</param>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="matches"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A match names another page.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the page is then unchanged.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.6.23, §9.4.3 and §9.10. The page's text is extracted again to find each match's glyphs: as
    /// <see cref="PdfRedactionOptions.TextExtractionOptions"/> describes (everything by default), then as <see cref="FindText(TextQuery)"/>
    /// extracts it, and a match is taken from the first extraction whose text reads the same at the match's place. Its glyphs are then
    /// removed by identity, as <see cref="PdfDocument.Redact(TextQuery, PdfRedactionOptions?, Range?)"/> removes them; overlapping
    /// matches are merged.
    /// </para>
    /// <para>
    /// Lenient reading: a match found in neither extraction, or whose glyphs are not all in the content, is removed by area over its quads and
    /// recorded as <c>RedactionMatchNotLocated</c>; in strict mode that throws and the page is unchanged. A mutation.
    /// </para>
    /// </remarks>
    public PdfRedactionResult Redact(IReadOnlyList<TextMatch> matches, PdfRedactionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(matches);
        options ??= new PdfRedactionOptions();
        int pageIndex = _document.Pages.IndexOf(this);
        foreach (TextMatch match in matches)
        {
            if (match.PageIndex >= 0 && match.PageIndex != pageIndex)
            {
                throw new ArgumentException($"A match on page index {match.PageIndex} cannot be redacted on page index {pageIndex}.", nameof(matches));
            }
        }

        if (matches.Count == 0)
        {
            return RedactionSession.ApplyMatches(_document, [], options);
        }

        TextExtractionOptions[] candidates = [options.TextExtractionOptions ?? RedactionSession.DefaultExtraction(), new TextExtractionOptions()];
        var groups = new List<TextMatch>?[candidates.Length];
        var texts = new TextPage?[candidates.Length];
        var searchables = new SearchableText?[candidates.Length];
        List<TextMatch> unlocated = [];
        foreach (TextMatch match in matches)
        {
            int chosen = -1;
            for (int candidate = 0; candidate < candidates.Length && chosen < 0; candidate++)
            {
                texts[candidate] ??= ExtractText(candidates[candidate], null);
                searchables[candidate] ??= SearchableText.Create(texts[candidate]!);
                if (Locates(texts[candidate]!, searchables[candidate]!, match))
                {
                    chosen = candidate;
                }
            }

            if (chosen < 0)
            {
                unlocated.Add(match);
            }
            else
            {
                (groups[chosen] ??= []).Add(match);
            }
        }

        List<RedactionPageMatches> pages = [];
        for (int candidate = 0; candidate < candidates.Length; candidate++)
        {
            if (groups[candidate] is { } group)
            {
                pages.Add(new RedactionPageMatches(this, texts[candidate]!, RedactionSession.Merge(group), pages.Count == 0 ? unlocated : null));
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(new RedactionPageMatches(this, texts[0]!, [], unlocated));
        }

        return RedactionSession.ApplyMatches(_document, pages, options);
    }

    /// <summary>Whether a match's glyph range indexes <paramref name="text"/>: the same text at its place, its first glyph within its bounds.</summary>
    private static bool Locates(TextPage text, SearchableText searchable, TextMatch match)
    {
        if (match.GlyphStart < 0 || match.GlyphCount <= 0 || match.GlyphStart + match.GlyphCount > text.Glyphs.Count
            || match.TextStart < 0 || match.Text is null || match.TextStart + match.Text.Length > searchable.Text.Length
            || !searchable.Text.AsSpan(match.TextStart, match.Text.Length).SequenceEqual(match.Text))
        {
            return false;
        }

        PdfRectangle first = text.Glyphs[match.GlyphStart].Quad.Bounds, bounds = match.Bounds;
        const double tolerance = 0.01;
        return first.Left <= bounds.Right + tolerance && first.Right >= bounds.Left - tolerance
            && first.Bottom <= bounds.Top + tolerance && first.Top >= bounds.Bottom - tolerance;
    }
}
