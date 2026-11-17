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
/// <see cref="Quantized"/> (the default) rounds the offset to the nearest quarter pixel horizontally and vertically, as most glyph
/// caches do, so almost every repeated glyph is reused after its first few showings; each glyph moves by at most an eighth of a pixel
/// each way from where the text matrix puts it (the error does not accumulate along a line) and its edge pixels change slightly.
/// <see cref="Exact"/> keeps every glyph where the text matrix puts it, for output that must equal an uncached fill; the cache then
/// helps where a glyph repeats at the same sub-pixel offset (the same page drawn again, tiles of one page, aligned columns and
/// monospaced text). Both give the same bytes on every platform. Measurements: <c>docs/benchmarks/glyph-cache.md</c>.
/// </para>
/// </remarks>
public enum GlyphPositioning
{
    /// <summary>At the exact position the text rendering matrix gives: the same pixels with or without the cache.</summary>
    Exact = 0,

    /// <summary>At the nearest quarter pixel, horizontally and vertically: the default; faster text, within an eighth of a pixel of the exact position.</summary>
    Quantized = 1,
}
