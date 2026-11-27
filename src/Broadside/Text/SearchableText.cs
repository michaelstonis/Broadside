using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>
/// A page's text prepared for search: its words in reading order joined by single spaces (lines too, so a phrase matches across a
/// line break), each character's glyph, and the glyphs' quads and line segments, without the glyphs themselves. It keeps a fraction of a
/// <see cref="TextPage"/>'s memory, so an application that searches the same pages again (a viewer's find bar) keeps these and lets
/// the pages go.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.10 (the Unicode text a page shows), §14.8.2.6.2 (word breaks) and §14.9.4 (replacement text). Folded forms of the
/// text, one per combination of <see cref="TextQuery.MatchCase"/> and <see cref="TextQuery.MatchDiacritics"/>, are made on first use.
/// Immutable apart from that cache, so <see cref="Find"/> may run on several threads at once.
/// </remarks>
public sealed class SearchableText
{
    /// <summary>The flags of a glyph whose text stands for a whole sequence of glyphs.</summary>
    private const TextGlyphFlags Replaced = TextGlyphFlags.ActualText | TextGlyphFlags.AlternateDescription | TextGlyphFlags.Expansion;

    private readonly string _text;
    private readonly int[] _glyphOf;
    private readonly Quad[] _quads;
    private readonly int[] _segmentOf;
    private readonly bool[] _continues;
    private readonly Folded?[] _folded = new Folded?[4];

    private SearchableText(string text, int[] glyphOf, Quad[] quads, int[] segmentOf, bool[] continues)
    {
        _text = text;
        _glyphOf = glyphOf;
        _quads = quads;
        _segmentOf = segmentOf;
        _continues = continues;
    }

    /// <summary>Gets an empty page's searchable text.</summary>
    public static SearchableText Empty { get; } = new(string.Empty, [], [], [], []);

    /// <summary>
    /// Gets the text searched: the page's words in reading order joined by single spaces, each white-space character inside a word
    /// as a space. It has the length of <see cref="TextPage.PlainText"/>, with a space where that has a line feed;
    /// <see cref="TextMatch.TextStart"/> indexes it.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2.</remarks>
    public string Text => _text;

    /// <summary>Prepares a page's extracted text for search.</summary>
    /// <param name="page">The page's text, as <see cref="PdfPage.ExtractText()"/> returns it.</param>
    /// <returns>A new searchable text; it keeps no reference to <paramref name="page"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4.</remarks>
    public static SearchableText Create(TextPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        IReadOnlyList<TextGlyph> glyphs = page.Glyphs;
        IReadOnlyList<TextWord> words = page.Words;
        IReadOnlyList<TextLine> lines = page.Lines;
        if (words.Count == 0)
        {
            return Empty;
        }

        var quads = new Quad[glyphs.Count];
        var segmentOf = new int[glyphs.Count];
        var continues = new bool[glyphs.Count];
        segmentOf.AsSpan().Fill(-1);
        int segment = -1, anchor = -1, previous = -1;
        int length = page.PlainText.Length;
        var text = new StringBuilder(length);
        var glyphOf = new List<int>(length);
        for (int line = 0; line < lines.Count; line++)
        {
            TextLine textLine = lines[line];
            anchor = -1;
            for (int w = textLine.WordStart; w < textLine.WordStart + textLine.WordCount; w++)
            {
                if (text.Length > 0)
                {
                    glyphOf.Add(-1);
                    text.Append(' ');
                }

                TextWord word = words[w];
                for (int g = word.GlyphStart; g < word.GlyphStart + word.GlyphCount; g++)
                {
                    TextGlyph glyph = glyphs[g];
                    if (anchor < 0 || TextLayout.Measure(glyphs[previous], glyph, glyphs[anchor]) == TextLayout.Break.Line)
                    {
                        // A new line, or a glyph off the line's baseline inside one word (the parts of an ActualText sequence painted
                        // on two lines): a quad of its own.
                        segment++;
                        anchor = g;
                    }

                    quads[g] = glyph.Quad;
                    segmentOf[g] = segment;
                    previous = g;
                    continues[g] = g > word.GlyphStart && glyph.TextLength == 0 && (glyphs[g - 1].Flags & glyph.Flags & Replaced) != 0;
                    foreach (char c in page.GetGlyphText(glyph))
                    {
                        glyphOf.Add(g);
                        text.Append(char.IsWhiteSpace(c) ? ' ' : c);
                    }
                }
            }
        }

        Debug.Assert(text.Length == length, "The searchable text has the plain text's length.");
        return new SearchableText(text.ToString(), [.. glyphOf], quads, segmentOf, continues);
    }

    /// <summary>Finds every occurrence of a query, in reading order.</summary>
    /// <param name="query">What to find and how to compare.</param>
    /// <param name="pageIndex">The page to report in <see cref="TextMatch.PageIndex"/>; -1 when there is none.</param>
    /// <param name="cancellationToken">Stops the search between matches.</param>
    /// <returns>The matches; empty when there is none. Matches do not overlap: the search goes on after the end of each.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="RegexMatchTimeoutException">A regular expression ran longer than <see cref="TextQuery.RegexTimeout"/> on this page.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <remarks>ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4. The matching rules are <see cref="TextQuery"/>'s.</remarks>
    public IReadOnlyList<TextMatch> Find(TextQuery query, int pageIndex = -1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_text.Length == 0)
        {
            return [];
        }

        List<TextMatch>? matches = query.IsRegex
            ? FindRegex(query, pageIndex, cancellationToken)
            : FindLiteral(query, pageIndex, cancellationToken);
        if (matches is not null && query.MatchFilter is not null)
        {
            Filter(matches, query.MatchFilter);
        }

        return matches is null or { Count: 0 } ? Array.Empty<TextMatch>() : matches;
    }

    /// <summary>Drops the matches <paramref name="filter"/> rejects (<see cref="TextQuery.MatchFilter"/>); apart, so a search without one allocates no closure.</summary>
    private static void Filter(List<TextMatch> matches, Func<TextMatch, bool> filter) => matches.RemoveAll(match => !filter(match));

    /// <summary>Whether a character is part of a word, for <see cref="TextQuery.WholeWord"/>.</summary>
    internal static bool IsWordCharacter(char c) =>
        char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static bool IsWholeWord(string text, int start, int end) =>
        (start == 0 || !IsWordCharacter(text[start - 1])) && (end == text.Length || !IsWordCharacter(text[end]));

    /// <summary>
    /// Appends the folded form of one character: its full compatibility decomposition (<see cref="SearchFoldingData"/>, so the result
    /// does not depend on the host's globalization data), combining marks dropped unless they count, case lowered unless it counts,
    /// white space as a plain space.
    /// </summary>
    private static void Fold(char c, bool matchCase, bool matchDiacritics, ref CharBuffer destination)
    {
        if (c < 0x80 || char.IsSurrogate(c))
        {
            destination.Append(char.IsWhiteSpace(c) ? ' ' : matchCase ? c : char.ToLowerInvariant(c));
            return;
        }

        foreach (char part in Decompose(in c))
        {
            if (!matchDiacritics && CharUnicodeInfo.GetUnicodeCategory(part) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            destination.Append(char.IsWhiteSpace(part) ? ' ' : matchCase ? part : char.ToLowerInvariant(part));
        }
    }

    private static ReadOnlySpan<char> Decompose(ref readonly char c)
    {
        int index = SearchFoldingData.Keys.BinarySearch(c);
        if (index < 0)
        {
            return new ReadOnlySpan<char>(in c);
        }

        ReadOnlySpan<ushort> offsets = SearchFoldingData.Offsets;
        return SearchFoldingData.Values[offsets[index]..offsets[index + 1]];
    }

    /// <summary>Folds a literal query as the text is folded, runs of white space collapsed to single spaces and trimmed, into a pooled buffer.</summary>
    private static void FoldQuery(string query, bool matchCase, bool matchDiacritics, ref CharBuffer destination)
    {
        bool space = false;
        foreach (char c in query)
        {
            if (char.IsWhiteSpace(c))
            {
                space = destination.Length > 0;
                continue;
            }

            if (space)
            {
                destination.Append(' ');
                space = false;
            }

            Fold(c, matchCase, matchDiacritics, ref destination);
        }
    }

    private List<TextMatch>? FindLiteral(TextQuery query, int pageIndex, CancellationToken cancellationToken)
    {
        bool matchCase = query.MatchCase, matchDiacritics = query.MatchDiacritics, wholeWord = query.WholeWord;
        var needleBuffer = new CharBuffer(query.Pattern.Length * 2);
        try
        {
            FoldQuery(query.Pattern, matchCase, matchDiacritics, ref needleBuffer);
            ReadOnlySpan<char> needle = needleBuffer.Span;
            if (needle.IsEmpty)
            {
                return null;
            }

            Folded folded = GetFolded(matchCase, matchDiacritics);
            string haystack = folded.Text;
            List<TextMatch>? matches = null;
            int at = 0;
            while (at <= haystack.Length - needle.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int found = haystack.AsSpan(at).IndexOf(needle, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                int start = at + found, end = start + needle.Length;
                if (wholeWord && !IsWholeWord(haystack, start, end))
                {
                    at = start + 1;
                    continue;
                }

                Add(pageIndex, folded.Source[start], folded.Source[end - 1], ref matches);
                at = end;
            }

            return matches;
        }
        finally
        {
            needleBuffer.Dispose();
        }
    }

    private List<TextMatch>? FindRegex(TextQuery query, int pageIndex, CancellationToken cancellationToken)
    {
        Regex regex = query.GetRegex();
        TimeSpan timeout = query.RegexTimeout;
        bool wholeWord = query.WholeWord;
        long started = Stopwatch.GetTimestamp();
        List<TextMatch>? matches = null;
        foreach (ValueMatch match in regex.EnumerateMatches(_text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout != Regex.InfiniteMatchTimeout && Stopwatch.GetElapsedTime(started) > timeout)
            {
                // The expression's own timeout bounds each match; this bounds the page.
                throw new RegexMatchTimeoutException(_text, query.Pattern, timeout);
            }

            if (match.Length == 0 || (wholeWord && !IsWholeWord(_text, match.Index, match.Index + match.Length)))
            {
                continue;
            }

            Add(pageIndex, match.Index, match.Index + match.Length - 1, ref matches);
        }

        return matches;
    }

    /// <summary>Adds the match of text characters <paramref name="first"/> to <paramref name="last"/>, trimmed to the characters glyphs show.</summary>
    private void Add(int pageIndex, int first, int last, ref List<TextMatch>? matches)
    {
        while (first <= last && _glyphOf[first] < 0)
        {
            first++;
        }

        while (last >= first && _glyphOf[last] < 0)
        {
            last--;
        }

        if (first > last)
        {
            return;
        }

        int firstGlyph = _glyphOf[first], lastGlyph = _glyphOf[last];
        while (lastGlyph + 1 < _continues.Length && _continues[lastGlyph + 1])
        {
            // §14.9.4: the replacement on a sequence's first glyph stands for all of its glyphs.
            lastGlyph++;
        }

        Quad[] quads = GetQuads(firstGlyph, lastGlyph);
        PdfRectangle bounds = quads[0].Bounds;
        for (int i = 1; i < quads.Length; i++)
        {
            PdfRectangle next = quads[i].Bounds;
            bounds = new PdfRectangle(
                Math.Min(bounds.Left, next.Left), Math.Min(bounds.Bottom, next.Bottom), Math.Max(bounds.Right, next.Right), Math.Max(bounds.Top, next.Top));
        }

        matches ??= [];
        matches.Add(new TextMatch(pageIndex, _text[first..(last + 1)], first, firstGlyph, lastGlyph - firstGlyph + 1, quads, bounds));
    }

    /// <summary>The quads of glyphs <paramref name="first"/> to <paramref name="last"/>, one per line segment; glyphs in no word are skipped.</summary>
    private Quad[] GetQuads(int first, int last)
    {
        int count = 0, segment = -1;
        for (int g = first; g <= last; g++)
        {
            if (_segmentOf[g] >= 0 && _segmentOf[g] != segment)
            {
                segment = _segmentOf[g];
                count++;
            }
        }

        var quads = new Quad[count];
        int index = 0, start = -1, previous = -1;
        for (int g = first; g <= last; g++)
        {
            if (_segmentOf[g] < 0)
            {
                continue;
            }

            if (start >= 0 && _segmentOf[g] != _segmentOf[start])
            {
                quads[index++] = Span(start, previous);
                start = -1;
            }

            if (start < 0)
            {
                start = g;
            }

            previous = g;
        }

        quads[index] = Span(start, previous);
        return quads;
    }

    /// <summary>One quad from the first glyph's left edge to the last glyph's right edge, along the first glyph's baseline frame.</summary>
    private Quad Span(int first, int last)
    {
        Quad a = _quads[first], b = _quads[last];
        return first == last ? a : new Quad(a.BottomLeft, b.BottomRight, b.TopRight, a.TopLeft);
    }

    private Folded GetFolded(bool matchCase, bool matchDiacritics)
    {
        int slot = (matchCase ? 1 : 0) | (matchDiacritics ? 2 : 0);
        if (Volatile.Read(ref _folded[slot]) is { } known)
        {
            return known;
        }

        var text = new CharBuffer(_text.Length);
        var source = new List<int>(_text.Length);
        try
        {
            for (int i = 0; i < _text.Length; i++)
            {
                int before = text.Length;
                Fold(_text[i], matchCase, matchDiacritics, ref text);
                for (int added = before; added < text.Length; added++)
                {
                    source.Add(i);
                }
            }

            var folded = new Folded(new string(text.Span), [.. source]);

            // Immutable once built: a thread losing the race returns the first one published, an equal copy.
            return Interlocked.CompareExchange(ref _folded[slot], folded, null) ?? folded;
        }
        finally
        {
            text.Dispose();
        }
    }

    /// <summary>The text folded for one combination of options, and the character of <see cref="_text"/> each of its characters comes from.</summary>
    private sealed record Folded(string Text, int[] Source);

    /// <summary>A growable character buffer rented from the shared pool.</summary>
    private struct CharBuffer(int capacity) : IDisposable
    {
        private char[] _array = ArrayPool<char>.Shared.Rent(Math.Max(16, capacity));

        public int Length { get; private set; }

        public readonly ReadOnlySpan<char> Span => _array.AsSpan(0, Length);

        public void Append(char c)
        {
            if (Length == _array.Length)
            {
                char[] grown = ArrayPool<char>.Shared.Rent(_array.Length * 2);
                _array.AsSpan(0, Length).CopyTo(grown);
                ArrayPool<char>.Shared.Return(_array);
                _array = grown;
            }

            _array[Length++] = c;
        }

        public void Dispose()
        {
            ArrayPool<char>.Shared.Return(_array);
            _array = [];
        }
    }
}
