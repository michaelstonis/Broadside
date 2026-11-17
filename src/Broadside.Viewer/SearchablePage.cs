using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Viewer;

/// <summary>
/// One page of <see cref="DocumentSearch"/>'s index: the page's words in reading order joined by single spaces (lines too, so a
/// phrase matches across a line break), each character's glyph, and the glyphs' boxes and lines. Folded forms of the text, one per
/// combination of case and diacritics, are made on first use. Immutable apart from that cache, so safe to share between threads.
/// </summary>
internal sealed class SearchablePage
{
    private const int ContextLength = 32;

    private readonly string _text;
    private readonly int[] _glyphOf;
    private readonly Quad[] _quads;
    private readonly int[] _lineOf;
    private readonly Folded?[] _folded = new Folded?[4];

    private SearchablePage(string text, int[] glyphOf, Quad[] quads, int[] lineOf)
    {
        _text = text;
        _glyphOf = glyphOf;
        _quads = quads;
        _lineOf = lineOf;
    }

    public static SearchablePage Empty { get; } = new(string.Empty, [], [], []);

    /// <summary>Builds a page's index entry from its extracted text (§14.8.2.6.2 words, §9.10 text).</summary>
    public static SearchablePage Create(TextPage page)
    {
        IReadOnlyList<TextGlyph> glyphs = page.Glyphs;
        IReadOnlyList<TextWord> words = page.Words;
        IReadOnlyList<TextLine> lines = page.Lines;
        var quads = new Quad[glyphs.Count];
        var lineOf = new int[glyphs.Count];
        lineOf.AsSpan().Fill(-1);
        var text = new StringBuilder(page.Text.Length + words.Count);
        var glyphOf = new List<int>(page.Text.Length + words.Count);
        for (int line = 0; line < lines.Count; line++)
        {
            TextLine textLine = lines[line];
            for (int w = textLine.WordStart; w < textLine.WordStart + textLine.WordCount; w++)
            {
                if (text.Length > 0)
                {
                    text.Append(' ');
                    glyphOf.Add(-1);
                }

                TextWord word = words[w];
                for (int g = word.GlyphStart; g < word.GlyphStart + word.GlyphCount; g++)
                {
                    TextGlyph glyph = glyphs[g];
                    quads[g] = glyph.Quad;
                    lineOf[g] = line;
                    foreach (char c in page.GetGlyphText(glyph))
                    {
                        text.Append(char.IsWhiteSpace(c) ? ' ' : c);
                        glyphOf.Add(g);
                    }
                }
            }
        }

        return new SearchablePage(text.ToString(), [.. glyphOf], quads, lineOf);
    }

    /// <summary>Folds a query as the page's text is folded for <paramref name="options"/>: white space collapsed to single spaces.</summary>
    public static string FoldQuery(string query, TextSearchOptions options)
    {
        string collapsed = string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var folded = new StringBuilder(collapsed.Length);
        foreach (char c in collapsed)
        {
            Fold(c, options.MatchCase, options.MatchDiacritics, folded);
        }

        return folded.ToString();
    }

    /// <summary>Adds the page's matches of a folded query (non-empty) to <paramref name="matches"/>, in reading order.</summary>
    public void Find(int pageIndex, string needle, TextSearchOptions options, List<SearchMatch> matches, CancellationToken token)
    {
        if (_text.Length == 0 || needle.Length == 0)
        {
            return;
        }

        Folded folded = GetFolded(options.MatchCase, options.MatchDiacritics);
        string haystack = folded.Text;
        var run = new List<Quad>();
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at, StringComparison.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            int end = at + needle.Length;
            if (options.WholeWord && ((at > 0 && IsWordCharacter(haystack[at - 1])) || (end < haystack.Length && IsWordCharacter(haystack[end]))))
            {
                at++;
                continue;
            }

            int first = folded.Source[at], last = folded.Source[end - 1];
            matches.Add(new SearchMatch(pageIndex, GetQuads(first, last, run), _text[first..(last + 1)], GetContext(first, last + 1)));
            at = end;
        }
    }

    private static bool IsWordCharacter(char c) =>
        char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    /// <summary>
    /// Appends the folded form of one character: its full compatibility decomposition (<see cref="SearchFoldingData"/>, so the
    /// result does not depend on the host's globalization data), combining marks dropped unless they count, case lowered unless it
    /// counts, white space as a plain space.
    /// </summary>
    private static void Fold(char c, bool matchCase, bool matchDiacritics, StringBuilder destination)
    {
        if (c < 0x80 || char.IsSurrogate(c))
        {
            destination.Append(char.IsWhiteSpace(c) ? ' ' : matchCase ? c : char.ToLowerInvariant(c));
            return;
        }

        ReadOnlySpan<char> parts = Decompose(in c);
        foreach (char part in parts)
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

    private Folded GetFolded(bool matchCase, bool matchDiacritics)
    {
        int slot = (matchCase ? 1 : 0) | (matchDiacritics ? 2 : 0);
        if (Volatile.Read(ref _folded[slot]) is { } known)
        {
            return known;
        }

        var text = new StringBuilder(_text.Length);
        var source = new List<int>(_text.Length);
        for (int i = 0; i < _text.Length; i++)
        {
            int before = text.Length;
            Fold(_text[i], matchCase, matchDiacritics, text);
            for (int added = before; added < text.Length; added++)
            {
                source.Add(i);
            }
        }

        var folded = new Folded(text.ToString(), [.. source]);
        return Interlocked.CompareExchange(ref _folded[slot], folded, null) ?? folded;
    }

    /// <summary>The quads of the glyphs behind text characters <paramref name="first"/> to <paramref name="last"/>, one per line run.</summary>
    private Quad[] GetQuads(int first, int last, List<Quad> run)
    {
        run.Clear();
        int start = -1, previous = -1;
        for (int i = first; i <= last; i++)
        {
            int glyph = _glyphOf[i];
            if (glyph < 0 || glyph == previous)
            {
                continue;
            }

            if (start >= 0 && _lineOf[glyph] != _lineOf[start])
            {
                run.Add(Span(start, previous));
                start = -1;
            }

            if (start < 0)
            {
                start = glyph;
            }

            previous = glyph;
        }

        if (start >= 0)
        {
            run.Add(Span(start, previous));
        }

        return [.. run];
    }

    /// <summary>One quad from the first glyph's left edge to the last glyph's right edge, along the first glyph's baseline frame.</summary>
    private Quad Span(int first, int last)
    {
        Quad a = _quads[first], b = _quads[last];
        return first == last ? a : new Quad(a.BottomLeft, b.BottomRight, b.TopRight, a.TopLeft);
    }

    private string GetContext(int start, int end)
    {
        int from = Math.Max(0, start - ContextLength);
        int to = Math.Min(_text.Length, end + ContextLength);
        if (from > 0 && _text.LastIndexOf(' ', start - 1, start - from) is int space and >= 0)
        {
            from = space + 1;
        }

        if (to < _text.Length && _text.IndexOf(' ', end, to - end) is int after and >= 0)
        {
            to = after;
        }

        return string.Concat(from > 0 ? "…" : string.Empty, _text.AsSpan(from, to - from), to < _text.Length ? "…" : string.Empty);
    }

    private sealed record Folded(string Text, int[] Source);
}
