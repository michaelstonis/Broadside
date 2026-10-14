namespace Broadside.Layout;

/// <summary>
/// Where a layout node lies on one page: its bounds in page space and the range of the page's glyphs and text it holds, in reading
/// order.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.5.1 (logical content order) and §14.8.5.4.3 (<c>BBox</c>). <see cref="GlyphStart"/>, <see cref="GlyphCount"/>
/// index <see cref="LayoutPage.Text"/>'s <see cref="Text.TextPage.Glyphs"/>; <see cref="TextStart"/>, <see cref="TextLength"/> index its
/// <see cref="Text.TextPage.Text"/>. A node holds the content of its descendants, so its range covers theirs.
/// </para>
/// <para>
/// A node whose content on the page shows no glyph (an image, a path, an empty sequence) has empty ranges that start where the
/// content falls among the page's glyphs in logical order.
/// </para>
/// </remarks>
public readonly record struct LayoutExtent
{
    /// <summary>Gets the page.</summary>
    public LayoutPage Page { get; internal init; }

    /// <summary>
    /// Gets the bounds of what the node shows on the page, in page space (default user space, like <see cref="Text.TextGlyph.Quad"/>): its
    /// glyphs' boxes, the paths and images its content paints, the rectangles of its annotations, or for a figure that paints nothing
    /// measurable, its <c>BBox</c> attribute; <see langword="null"/> when none of these is known.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4, §8.5.3, §8.9.4, §12.5.2 (<c>Rect</c>) and §14.8.5.4.3 (<c>BBox</c>).</remarks>
    public PdfRectangle? Bounds { get; internal init; }

    /// <summary>Gets the index of the node's first glyph on the page.</summary>
    public int GlyphStart { get; internal init; }

    /// <summary>Gets the number of the node's glyphs on the page, 0 when its content there shows none.</summary>
    public int GlyphCount { get; internal init; }

    /// <summary>Gets the index of the node's first UTF-16 unit in the page's text.</summary>
    public int TextStart { get; internal init; }

    /// <summary>Gets the number of UTF-16 units of the node's text on the page.</summary>
    public int TextLength { get; internal init; }
}
