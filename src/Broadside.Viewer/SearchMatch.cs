using Broadside.Graphics;

namespace Broadside.Viewer;

/// <summary>One occurrence of a <see cref="DocumentSearch"/> query: where it is on its page and what it reads. Immutable.</summary>
/// <remarks>ISO 32000-2 §9.4.4 (glyph boxes) and §9.10 (text). Spec #33 viewer capability 7.</remarks>
public sealed class SearchMatch
{
    private readonly Quad[] _quads;

    internal SearchMatch(int pageIndex, Quad[] quads, string text, string context, int textStart = -1)
    {
        PageIndex = pageIndex;
        TextStart = textStart;
        _quads = quads;
        Text = text;
        Context = context;
        PdfRectangle bounds = quads[0].Bounds;
        for (int i = 1; i < quads.Length; i++)
        {
            PdfRectangle next = quads[i].Bounds;
            bounds = new PdfRectangle(
                Math.Min(bounds.Left, next.Left), Math.Min(bounds.Bottom, next.Bottom), Math.Max(bounds.Right, next.Right), Math.Max(bounds.Top, next.Top));
        }

        Bounds = bounds;
    }

    /// <summary>Gets the zero-based page.</summary>
    public int PageIndex { get; }

    /// <summary>
    /// Gets the match's quads in default user space, one per line it covers, each along its line's baseline (rotated text gives rotated
    /// quads): what a shell highlights.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4.</remarks>
    public IReadOnlyList<Quad> Quads => _quads;

    /// <summary>Gets the smallest rectangle holding <see cref="Quads"/>, in default user space: what a shell scrolls into view.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the page's text the match covers (as the page shows it, whatever the options folded).</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public string Text { get; }

    /// <summary>Gets the match with a few words of the page's text around it, for a list of results.</summary>
    public string Context { get; }

    internal ReadOnlySpan<Quad> QuadSpan => _quads;

    /// <summary>Gets where the match starts in its page's searchable text, or -1 when unknown.</summary>
    internal int TextStart { get; }
}
