using Broadside.Graphics;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>A match of a search on one page, as offsets of the page's <see cref="SelectableText"/> (what <see cref="TextSelection.Select"/> takes).</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="Start">The first matched reading position.</param>
/// <param name="End">The position after the last matched one.</param>
/// <param name="Context">The match with a few words around it.</param>
public sealed record SearchHit(int PageIndex, int Start, int End, string Context);

/// <summary>
/// Finds text through the viewer's <see cref="TextSelection"/>: each page's text in reading order (<see cref="SelectableText.GetText"/>,
/// which puts spaces and line breaks between words and lines), with every reading position's place in that string, so a match maps
/// back to positions the viewer selects and draws.
/// </summary>
public static class TextSearch
{
    /// <summary>Finds every occurrence of <paramref name="query"/> on a page, ignoring case (a line break matches a space).</summary>
    public static void FindOnPage(SelectableText text, int pageIndex, string query, List<SearchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(hits);
        if (text.Length == 0 || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        string full = text.GetText(0, text.Length);
        int[] starts = MapPositions(text, full);
        string haystack = Fold(full);
        string needle = Fold(Collapse(query));
        for (int at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            int start = PositionAt(starts, at);
            int end = PositionAt(starts, at + needle.Length - 1) + 1;
            if (start < 0 || end <= start)
            {
                continue;
            }

            int from = Math.Max(0, at - 30);
            int to = Math.Min(full.Length, at + needle.Length + 30);
            string context = (from > 0 ? "…" : "") + Collapse(full[from..to]) + (to < full.Length ? "…" : "");
            hits.Add(new SearchHit(pageIndex, start, end, context));
        }
    }

    /// <summary>The union of a selection's quads on a page, in default user space.</summary>
    public static PdfRectangle? Bounds(TextSelection selection, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Span<Quad> quads = stackalloc Quad[32];
        int count = Math.Min(selection.GetQuads(pageIndex, quads), quads.Length);
        if (count == 0)
        {
            return null;
        }

        PdfRectangle bounds = quads[0].Bounds;
        for (int i = 1; i < count; i++)
        {
            PdfRectangle next = quads[i].Bounds;
            bounds = new PdfRectangle(
                Math.Min(bounds.Left, next.Left), Math.Min(bounds.Bottom, next.Bottom), Math.Max(bounds.Right, next.Right), Math.Max(bounds.Top, next.Top));
        }

        return bounds;
    }

    // The character index in `full` where each reading position's text starts (-1 when it cannot be placed, e.g. a replaced sequence).
    private static int[] MapPositions(SelectableText text, string full)
    {
        int[] starts = new int[text.Length];
        int cursor = 0;
        for (int position = 0; position < text.Length; position++)
        {
            ReadOnlySpan<char> glyph = text.GetGlyphText(position).Trim();
            if (glyph.IsEmpty)
            {
                starts[position] = cursor;
                continue;
            }

            int found = full.AsSpan(cursor).IndexOf(glyph, StringComparison.Ordinal);
            if (found < 0 || found > 8)
            {
                starts[position] = -1;
                continue;
            }

            starts[position] = cursor + found;
            cursor += found + glyph.Length;
        }

        return starts;
    }

    // The last reading position whose text starts at or before a character index.
    private static int PositionAt(int[] starts, int index)
    {
        int best = -1;
        for (int position = 0; position < starts.Length; position++)
        {
            if (starts[position] < 0)
            {
                continue;
            }

            if (starts[position] > index)
            {
                break;
            }

            best = position;
        }

        return best;
    }

    // Lower case with every white space character as a plain space (same length, so indices stay valid).
    private static string Fold(string value) => string.Create(value.Length, value, static (span, source) =>
    {
        for (int i = 0; i < source.Length; i++)
        {
            span[i] = char.IsWhiteSpace(source[i]) ? ' ' : char.ToLowerInvariant(source[i]);
        }
    });

    private static string Collapse(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
