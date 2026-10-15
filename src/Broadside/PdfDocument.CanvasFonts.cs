using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside;

/// <summary>Writing text (issue #149): the fonts a <see cref="PdfCanvas"/> shows text with.</summary>
public sealed partial class PdfDocument
{
    /// <summary>Returns a non-embedded Standard 14 font a <see cref="PdfCanvas"/> of this document can show text with, adding its font dictionary the first time.</summary>
    /// <param name="font">The font.</param>
    /// <returns>
    /// The font: the twelve Latin fonts in <c>WinAnsiEncoding</c>, Symbol and ZapfDingbats in their built-in encodings. Every call for
    /// the same font uses one font dictionary of the document (with <c>Widths</c> and a <c>FontDescriptor</c> from the AFM metrics,
    /// and no font program).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="font"/> is not a defined font.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.6.2.2 (Standard 14 fonts), §9.6.2.1 (Table 109) and Annex D (WinAnsiEncoding, the Symbol and ZapfDingbats
    /// encodings). A font that is not embedded is drawn by the reader's own copy of the font or a substitute; PDF/A and PDF/UA require
    /// embedded fonts. Text the font's encoding has no code for needs an embedded font. A mutation (the first call for a font).
    /// </remarks>
    public PdfCanvasFont GetStandard14Font(Standard14Font font)
    {
        if (!Enum.IsDefined(font))
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Not a Standard 14 font.");
        }

        return Standard14CanvasFont.Get(this, font);
    }

    /// <summary>Embeds a TrueType program a <see cref="PdfCanvas"/> of this document can show any text its glyphs cover with, subset to the glyphs shown.</summary>
    /// <param name="program">The TrueType (<c>glyf</c>) program: a <c>.ttf</c> file, or a TrueType collection (<c>.ttc</c>) with <paramref name="faceIndex"/>.</param>
    /// <param name="faceIndex">The face of a collection; 0 for a single font.</param>
    /// <returns>
    /// The font. Calls with the same program memory and face return fonts over one font of the document, so every canvas and page
    /// that shows the program shares one embedded subset.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// The program cannot be embedded: it is not a TrueType program, has CFF outlines (embed it with <see cref="EmbedFont(ReadOnlyMemory{byte}, int)"/>), has
    /// no Unicode <c>cmap</c> subtable, is damaged, or its <c>OS/2</c> <c>fsType</c> says its licence restricts embedding.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.7.6 (a Type 0 font with the <c>Identity-H</c> CMap, §9.7.5.2: each character is a two-byte code), §9.7.4
    /// (its CIDFontType2 descendant, whose CIDs are the program's glyph ids, with a <c>W</c> array and a <c>CIDToGIDMap</c> stream),
    /// §9.8 (the font descriptor, with metrics from the program's <c>head</c>, <c>hhea</c>, <c>OS/2</c> and <c>post</c> tables), §9.9
    /// (the program as <c>FontFile2</c>), §9.9.2 (a subset with a six-letter tag that names its glyph set) and §9.10.3 (a
    /// <c>ToUnicode</c> CMap mapping each glyph back to the character it was shown for, so the text extracts as written).
    /// </para>
    /// <para>
    /// Each shown glyph is added to the subset as it is drawn; a save reduces the program to the glyphs shown, glyph 0 and the
    /// glyphs their composites are built from, renumbered, with the same bytes for the same glyphs on every run. Until then the
    /// document holds the whole program, so it renders and extracts before it is saved. A program whose <c>fsType</c> forbids
    /// subsetting is embedded whole. Needs PDF 1.2 (Type 0 fonts and <c>ToUnicode</c>); a save raises an older document. A mutation:
    /// the font's objects are added on the first call for a program.
    /// </para>
    /// </remarks>
    public PdfCanvasFont EmbedTrueTypeFont(ReadOnlyMemory<byte> program, int faceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        var resolution = new FontResolution(program, "embedded", FontMatchKind.Exact) { FaceIndex = faceIndex };
        if (EmbeddedCanvasFont.Get(this, resolution, [], out _, out string? problem) is not { } font)
        {
            throw new ArgumentException($"The font program cannot be embedded: {problem} (ISO 32000-2 §9.9, Table 124).", nameof(program));
        }

        RequireFeature(Writing.VersionedFeatures.EmbeddedTrueTypeFont);
        return font;
    }

    /// <summary>Embeds the TrueType program the document's font resolvers find for <paramref name="query"/>, as <see cref="EmbedTrueTypeFont(ReadOnlyMemory{byte}, int)"/> does.</summary>
    /// <param name="query">What to look for: a PostScript or family name, and optionally style, weight and a Standard 14 stand-in.</param>
    /// <returns>The font, or <see langword="null"/> when no resolver finds a program for the query or the program it finds cannot be embedded (not TrueType, restricted).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// ISO 32000-2 §9.7.4–§9.7.6, §9.8, §9.9 and §9.10.3. The resolvers are the ones the document was opened or created with
    /// (<c>PdfOptions.UseFontResolver</c>, <c>UseStandard14Fonts</c>, the system font resolver); set the query's <c>FontType</c> to
    /// TrueType to ask for a TrueType program. A mutation the first time a program is embedded.
    /// </remarks>
    public PdfCanvasFont? EmbedTrueTypeFont(FontQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (FontResolvers.ResolveFont(query) is not { } resolution || EmbeddedCanvasFont.Get(this, resolution, [], out _, out _) is not { } font)
        {
            return null;
        }

        RequireFeature(Writing.VersionedFeatures.EmbeddedTrueTypeFont);
        return font;
    }

    /// <summary>
    /// Embeds a TrueType, OpenType or CFF program a <see cref="PdfCanvas"/> of this document can show any text its glyphs cover with,
    /// subset to the glyphs shown.
    /// </summary>
    /// <param name="program">
    /// The program: a TrueType or OpenType file (<c>.ttf</c>, <c>.otf</c>, with <c>glyf</c> or <c>CFF </c> outlines), a collection
    /// (<c>.ttc</c>, <c>.otc</c>) with <paramref name="faceIndex"/>, or a bare CFF program with Type 2 charstrings.
    /// </param>
    /// <param name="faceIndex">The face of a collection; 0 for a single font.</param>
    /// <returns>The font. Calls with the same program memory and face return fonts over one font of the document.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    /// <exception cref="ArgumentException">
    /// The program cannot be embedded: it is none of those formats, has no Unicode <c>cmap</c> subtable (a bare CFF program: no glyph
    /// names the Adobe Glyph List maps), is damaged, or its <c>OS/2</c> <c>fsType</c> says its licence restricts embedding.
    /// </exception>
    /// <remarks>
    /// <para>
    /// TrueType outlines embed as <see cref="EmbedTrueTypeFont(ReadOnlyMemory{byte}, int)"/> does. CFF outlines embed as a Type 0 font
    /// (§9.7.6, <c>Identity-H</c>) over a CIDFontType0 (§9.7.4) whose CIDs are the program's glyph ids, with a <c>W</c> array, a
    /// <c>ToUnicode</c> CMap (§9.10.3) and a subset tag (§9.9.2); its CFF program (an OpenType program's <c>CFF </c> table) is
    /// <c>FontFile3</c> with subtype <c>CIDFontType0C</c> (§9.9, Table 124), which needs PDF 1.3. A save subsets it to the
    /// glyphs shown and glyph 0 as a CID-keyed CFF font whose charset maps each CID to its glyph (§9.7.4.2), with the charstrings
    /// copied unchanged and only the subroutines they reach (Adobe Technical Notes #5176 and #5177); until then the document holds
    /// the whole program. A save raises an older document to the version the font needs. A mutation: the font's objects are added on
    /// the first call for a program.
    /// </para>
    /// </remarks>
    public PdfCanvasFont EmbedFont(ReadOnlyMemory<byte> program, int faceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        var resolution = new FontResolution(program, "embedded", FontMatchKind.Exact) { FaceIndex = faceIndex };
        if (EmbeddedCanvasFont.Get(this, resolution, [], out _, out string? problem, cff: true) is not { } font)
        {
            throw new ArgumentException($"The font program cannot be embedded: {problem} (ISO 32000-2 §9.9, Table 124).", nameof(program));
        }

        RequireFeature(font.Feature);
        return font;
    }

    /// <summary>Embeds the program the document's font resolvers find for <paramref name="query"/>, as <see cref="EmbedFont(ReadOnlyMemory{byte}, int)"/> does.</summary>
    /// <param name="query">What to look for: a PostScript or family name, and optionally style, weight and a Standard 14 stand-in.</param>
    /// <returns>The font, or <see langword="null"/> when no resolver finds a program for the query or the program it finds cannot be embedded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <remarks>ISO 32000-2 §9.7.4–§9.7.6, §9.8, §9.9 and §9.10.3. A mutation the first time a program is embedded.</remarks>
    public PdfCanvasFont? EmbedFont(FontQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (FontResolvers.ResolveFont(query) is not { } resolution || EmbeddedCanvasFont.Get(this, resolution, [], out _, out _, cff: true) is not { } font)
        {
            return null;
        }

        RequireFeature(font.Feature);
        return font;
    }
}
