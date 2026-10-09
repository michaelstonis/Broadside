using Broadside.Graphics;

namespace Broadside.Fonts;

/// <summary>
/// A parsed font program: glyph outlines and metrics by glyph id, and the lookups its format has (glyph names, "cmap" subtables).
/// What an <see cref="IFontProgramParser"/> returns.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9. A program is selected by glyph id; mapping character codes to glyph ids is the PDF font's job (§9.6.5 for simple
/// fonts, §9.7.4.2 for CIDFonts), which reads the lookups a program exposes. A lookup the format does not have returns its "not
/// available" value (an empty list, <see langword="false"/>, <see langword="null"/>); none of them throw.
/// </para>
/// <para>
/// Thread safety: a program is immutable once returned by its parser and shared by every page and thread of its document; tables are
/// located when it is parsed and read lazily, once. Deviations found later (a damaged glyph) are reported through the
/// <see cref="FontProgramContext"/> it was parsed with; in strict mode the call that finds one throws.
/// </para>
/// </remarks>
public abstract class FontProgram
{
    /// <summary>Initializes a new instance of the <see cref="FontProgram"/> class.</summary>
    protected FontProgram()
    {
    }

    /// <summary>Gets the format of the program.</summary>
    public abstract FontProgramFormat Format { get; }

    /// <summary>Gets the number of glyphs: valid glyph ids are 0 to <see cref="GlyphCount"/> − 1, and glyph 0 is the missing glyph (<c>.notdef</c>).</summary>
    public abstract int GlyphCount { get; }

    /// <summary>Gets the matrix that maps glyph space to text space, such as <c>[1/unitsPerEm 0 0 1/unitsPerEm 0 0]</c> for TrueType.</summary>
    /// <remarks>ISO 32000-2 §9.2.4.</remarks>
    public abstract Matrix FontMatrix { get; }

    /// <summary>Gets the PostScript name of the program, or <see langword="null"/> when it records none.</summary>
    /// <remarks>ISO 32000-2 §9.6.3: a TrueType font's <c>BaseFont</c> comes from it (OpenType "name" table, name id 6).</remarks>
    public virtual string? PostScriptName => null;

    /// <summary>Gets the bounding box of all glyphs, in glyph space; empty at the origin when the program records none.</summary>
    public virtual PdfRectangle FontBBox => default;

    /// <summary>Gets the typographic ascender, in glyph space units; 0 when the program records none.</summary>
    public virtual double Ascender => 0;

    /// <summary>Gets the typographic descender (usually negative), in glyph space units; 0 when the program records none.</summary>
    public virtual double Descender => 0;

    /// <summary>Gets the typographic line gap, in glyph space units; 0 when the program records none.</summary>
    public virtual double LineGap => 0;

    /// <summary>Gets the subtables of the program's "cmap" table; empty when it has none (or its format has no such table).</summary>
    /// <remarks>ISO 32000-2 §9.6.5.4.</remarks>
    public virtual IReadOnlyList<FontCharacterMap> CharacterMaps => [];

    /// <summary>
    /// Gets the program's built-in encoding: the glyph name of each of the 256 character codes, <c>.notdef</c> where it maps none;
    /// <see langword="null"/> when the format has no built-in encoding (TrueType) or the program records none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.6.5.1 and §9.6.5.2: a Type 1 program's <c>/Encoding</c> (a CFF program's Encoding). A simple font with an
    /// embedded program uses it when its <c>Encoding</c> entry gives no base encoding.
    /// </remarks>
    public virtual IReadOnlyList<string>? BuiltInEncoding => null;

    /// <summary>Writes a glyph's outline into <paramref name="outline"/>, which is cleared first.</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="outline">The buffer to fill; reuse one for many glyphs.</param>
    /// <returns>Whether the glyph has an outline, draws nothing, or cannot be drawn.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the glyph's data deviates from its format.</exception>
    public abstract GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline);

    /// <summary>Gets a glyph's horizontal metrics, in glyph space units; zero when the program records none or the glyph id is out of range.</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The advance width and left side bearing.</returns>
    public virtual GlyphMetrics GetMetrics(int glyphId) => default;

    /// <summary>Looks up a glyph by name: the "post" table for TrueType, the charset for CFF, the CharStrings for Type 1.</summary>
    /// <param name="glyphName">The glyph name.</param>
    /// <param name="glyphId">The glyph id; 0 when not found.</param>
    /// <returns><see langword="true"/> when the program has a glyph of that name.</returns>
    /// <remarks>ISO 32000-2 §9.6.5.4: the "post" fallback for TrueType fonts.</remarks>
    public virtual bool TryGetGlyphId(string glyphName, out int glyphId)
    {
        glyphId = 0;
        return false;
    }

    /// <summary>Gets a glyph's name, or <see langword="null"/> when the program does not name it.</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The name.</returns>
    public virtual string? GetGlyphName(int glyphId) => null;
}
