namespace Broadside.Rendering;

/// <summary>
/// Where the software rasterizer places a filled glyph relative to the pixel grid, which decides how often its glyph cache can paint
/// a glyph from coverage computed before (<see cref="SoftwareRasterizer.GlyphCacheBudget"/>).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.4 (the text rendering matrix places each glyph) and §10.7.4 (shapes are not rounded to pixels). A glyph's
/// anti-aliased edges depend on where its outline falls inside the pixels, so a glyph's coverage can be reused only where the glyph
/// lands at the same offset inside its pixel.
/// </para>
/// <para>
/// <see cref="Exact"/> keeps every glyph where the text matrix puts it; the cache then helps where a glyph repeats at the same
/// sub-pixel offset (the same page drawn again, tiles of one page, aligned columns and monospaced text). <see cref="Quantized"/>
/// rounds the offset to the nearest quarter pixel horizontally and vertically, as most glyph caches do, so almost every repeated
/// glyph is reused after its first few showings; glyphs move by at most an eighth of a pixel each way and their edge pixels change
/// slightly. Measurements: <c>docs/benchmarks/glyph-cache.md</c>.
/// </para>
/// </remarks>
public enum GlyphPositioning
{
    /// <summary>At the exact position the text rendering matrix gives: the default, the same pixels with or without the cache.</summary>
    Exact = 0,

    /// <summary>At the nearest quarter pixel, horizontally and vertically: faster text, within an eighth of a pixel of the exact position.</summary>
    Quantized = 1,
}
