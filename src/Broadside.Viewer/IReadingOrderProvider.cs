using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Decides the order a page's glyphs are selected and copied in: the replaceable seam between the hit-test index (glyphs in
/// content order) and the reading order a text selection follows.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.5 (page content order and logical content order). Built-in providers: <see cref="ContentReadingOrder"/>
/// (page content order) and <see cref="LayoutReadingOrder"/> (the layout tree's order: structure order on tagged pages,
/// detected columns and paragraphs on untagged ones). Spec #33 viewer capability 4.
/// </remarks>
public interface IReadingOrderProvider
{
    /// <summary>Gives the selectable glyphs of a page in reading order.</summary>
    /// <param name="page">The page.</param>
    /// <param name="index">The page's hit-test index; its glyph indices are what the order lists.</param>
    /// <returns>
    /// Glyph indices of <paramref name="index"/> (0 to <see cref="HitTestIndex.GlyphCount"/> − 1), each at most once, in reading
    /// order. A glyph left out cannot be selected.
    /// </returns>
    int[] GetReadingOrder(PdfPage page, HitTestIndex index);
}
