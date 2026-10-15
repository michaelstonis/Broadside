namespace Broadside.Fonts;

/// <summary>
/// The character map of a program that has no Unicode <c>cmap</c> (a bare CFF program): each glyph name the Adobe Glyph List
/// maps to one character maps that character to the glyph; the lowest glyph id wins.
/// </summary>
/// <remarks>Adobe Glyph List Specification; ISO 32000-2 §9.10.2 (glyph names to Unicode). Reported as the (3, 1) Unicode map.</remarks>
internal sealed class GlyphNameCharacterMap : FontCharacterMap
{
    private readonly Dictionary<int, int> _glyphs;

    private GlyphNameCharacterMap(Dictionary<int, int> glyphs) => _glyphs = glyphs;

    /// <inheritdoc/>
    public override int PlatformId => 3;

    /// <inheritdoc/>
    public override int EncodingId => 1;

    /// <inheritdoc/>
    public override int Format => -1;

    /// <summary>The map of a program's glyph names, or <see langword="null"/> when no glyph has a name that maps to a character.</summary>
    public static GlyphNameCharacterMap? Create(FontProgram program)
    {
        var glyphs = new Dictionary<int, int>();
        for (int glyph = 1; glyph < program.GlyphCount; glyph++)
        {
            if (program.GetGlyphName(glyph) is { } name && AdobeGlyphList.TryGetScalar(name, zapfDingbats: false, out int scalar))
            {
                _ = glyphs.TryAdd(scalar, glyph);
            }
        }

        return glyphs.Count == 0 ? null : new GlyphNameCharacterMap(glyphs);
    }

    /// <inheritdoc/>
    public override int GetGlyphId(int code) => _glyphs.TryGetValue(code, out int glyph) ? glyph : 0;
}
