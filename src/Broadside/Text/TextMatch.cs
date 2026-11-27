using Broadside.Graphics;

namespace Broadside.Text;

/// <summary>One occurrence of a <see cref="TextQuery"/> on a page: what it reads, which glyphs show it and where they are.</summary>
/// <remarks>
/// ISO 32000-2 §9.4.4 (glyph boxes), §9.10 (text) and §14.9.4 (replacement text). Positions are in default user space, as the glyphs'
/// (<see cref="TextGlyph.Quad"/>).
/// </remarks>
/// <param name="PageIndex">The zero-based page, or -1 when the match was found in a <see cref="TextPage"/> searched on its own.</param>
/// <param name="Text">The page's text the match covers, as the page shows it (whatever the query folded), white space as single spaces.</param>
/// <param name="TextStart">Where <paramref name="Text"/> starts in the page's searchable text (<see cref="SearchableText.Text"/>), which has the length and word positions of <see cref="TextPage.PlainText"/>.</param>
/// <param name="GlyphStart">The index of the first glyph of the match in <see cref="TextPage.Glyphs"/>.</param>
/// <param name="GlyphCount">
/// The number of glyphs from <paramref name="GlyphStart"/> to the last glyph of the match, white-space glyphs between words included.
/// A match that covers part of an <c>ActualText</c>, <c>Alt</c> or <c>E</c> replacement covers every glyph the replacement stands for.
/// </param>
/// <param name="Quads">The match's quads, one per line it covers, each from its first glyph's left edge to its last glyph's right edge along the line's baseline.</param>
/// <param name="Bounds">The smallest rectangle holding <paramref name="Quads"/>.</param>
public readonly record struct TextMatch(
    int PageIndex,
    string Text,
    int TextStart,
    int GlyphStart,
    int GlyphCount,
    IReadOnlyList<Quad> Quads,
    PdfRectangle Bounds);
