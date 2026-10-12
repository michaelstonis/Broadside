namespace Broadside.Fonts;

/// <summary>One subtable of a TrueType or OpenType program's "cmap" table: a mapping from character codes to glyph ids.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.5.4: a subtable is identified by its platform and encoding ids, such as (3, 1) for Microsoft Unicode, (3, 0) for
/// Microsoft Symbol and (1, 0) for Macintosh Roman; the PDF font layer chooses among them (OpenType "cmap" table). Not to be
/// confused with a PDF CMap (§9.7.5).
/// </remarks>
public abstract class FontCharacterMap
{
    /// <summary>Initializes a new instance of the <see cref="FontCharacterMap"/> class.</summary>
    protected FontCharacterMap()
    {
    }

    /// <summary>Gets the platform id: 0 Unicode, 1 Macintosh, 3 Windows.</summary>
    public abstract int PlatformId { get; }

    /// <summary>Gets the platform-specific encoding id, such as 1 for Unicode BMP or 0 for Symbol on the Windows platform.</summary>
    public abstract int EncodingId { get; }

    /// <summary>Gets the subtable format: 0, 2, 4, 6, 12 or 13 (OpenType "cmap" table).</summary>
    public abstract int Format { get; }

    /// <summary>Looks up the glyph id of a character code.</summary>
    /// <param name="code">The character code: a byte for Macintosh subtables, a Unicode scalar value for Unicode subtables.</param>
    /// <returns>The glyph id; 0 (the missing glyph) when the subtable does not map the code.</returns>
    public abstract int GetGlyphId(int code);
}
