namespace Broadside.Fonts;

/// <summary>The glyph id of each one-byte character code of a simple font, worked out on first use and kept.</summary>
/// <remarks>Thread-safe: concurrent readers may each work a code out; they find the same glyph, and the last write wins.</remarks>
internal sealed class GlyphIdCache
{
    private readonly int[] _glyphs = new int[256];

    /// <summary>Initializes a new instance of the <see cref="GlyphIdCache"/> class with no code worked out.</summary>
    public GlyphIdCache() => Array.Fill(_glyphs, -1);

    /// <summary>Gets the glyph id of a code, if it was worked out.</summary>
    public bool TryGet(byte code, out int glyphId)
    {
        glyphId = Volatile.Read(ref _glyphs[code]);
        return glyphId >= 0;
    }

    /// <summary>Keeps the glyph id of a code.</summary>
    public void Set(byte code, int glyphId) => Volatile.Write(ref _glyphs[code], glyphId);
}
