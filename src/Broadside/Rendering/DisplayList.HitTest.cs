namespace Broadside.Rendering;

/// <summary>The display list's text positions and the hit-test index it yields (viewer capability 4).</summary>
public sealed partial class DisplayList
{
    /// <summary>Gets every character code shown, in content order, with its box in page space and its slice of <see cref="Text"/>; set by the builder.</summary>
    internal DisplayTextGlyph[] TextGlyphs { get; init; } = [];

    /// <summary>Gets the Unicode text of the shown codes, in content order; set by the builder.</summary>
    internal char[] Text { get; init; } = [];

    /// <summary>Builds the hit-test index of the list's content: its glyphs, images and paths, queryable by point and by rectangle.</summary>
    /// <returns>The index; it holds no links, which are the page's (<see cref="PdfPage.CreateHitTestIndex(DisplayList, PdfOptionalContentState?)"/>).</returns>
    /// <remarks>
    /// Viewer capability 4; ADR 0006. Reads only the list: the glyph boxes and text the builder recorded for every code shown
    /// (ISO 32000-2 §9.4.4, §9.10.2) and a replay that collects image and path areas (§8.9.4, §8.5.3, §8.5.4). Allocates a fixed
    /// number of arrays, sized to the content, whatever the number of glyphs; see <see cref="HitTestIndex"/>.
    /// </remarks>
    public HitTestIndex CreateHitTestIndex() => HitTestIndex.Create(this, []);
}
