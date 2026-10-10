using Broadside.Diagnostics;
using Broadside.Fonts.CharStrings;
using Broadside.Graphics;
using Broadside.Parsing;

namespace Broadside.Fonts.Cff;

/// <summary>
/// A parsed CFF program, bare (<c>FontFile3</c> <c>Type1C</c>) or the "CFF " table of an OpenType program: tables located once,
/// charstrings interpreted on request from the program's bytes.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.9 (Tables 124 and 125); Adobe Technical Notes #5176 and #5177. Glyphs are named by the charset and encoded by the
/// CFF encoding (§9.6.5.2: for a Type 1 font with a CFF program, also inside OpenType, the "cmap" table is not used). Immutable and
/// safe for concurrent use.
/// </remarks>
internal sealed class CffFontProgram : FontProgram
{
    private readonly CffFont _font;
    private readonly FontProgramContext _context;
    private readonly CharStringReporter _reporter;
    private readonly int _budget;

    internal CffFontProgram(CffFont font, FontProgramContext context, FontProgramFormat format, string? postScriptName, IReadOnlyList<FontCharacterMap> characterMaps)
    {
        _font = font;
        _context = context;
        _reporter = new CharStringReporter(context, format == FontProgramFormat.OpenType ? "OpenType CFF" : "CFF");
        _budget = Math.Max(1, context.MaxCharStringOperators);
        Format = format;
        PostScriptName = postScriptName ?? font.Name;
        CharacterMaps = characterMaps;
    }

    /// <inheritdoc/>
    public override FontProgramFormat Format { get; }

    /// <inheritdoc/>
    public override int GlyphCount => _font.GlyphCount;

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5176, Top DICT FontMatrix (default <c>[0.001 0 0 0.001 0 0]</c>); in a CID-keyed font, the first Font DICT's FontMatrix concatenated with it (Adobe Technical Note #5014 §4.2).</remarks>
    public override Matrix FontMatrix => _font.FontMatrix;

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5176, Top DICT FontBBox.</remarks>
    public override PdfRectangle FontBBox => _font.FontBBox;

    /// <inheritdoc/>
    /// <remarks>The OpenType "name" table's PostScript name, else the CFF Name INDEX entry (Adobe Technical Note #5176 §7).</remarks>
    public override string? PostScriptName { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<FontCharacterMap> CharacterMaps { get; }

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5176 §12, Note 4: StandardEncoding, ExpertEncoding or a custom encoding.</remarks>
    public override IReadOnlyList<string>? BuiltInEncoding => _font.BuiltInEncoding;

    /// <summary>Gets the parsed tables (a CIDFontType0 maps CIDs to glyphs through them, §9.7.4.2).</summary>
    internal CffFont Font => _font;

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5177 §4: the Type 2 charstring of the glyph; hints are skipped.</remarks>
    public override GlyphOutlineStatus GetOutline(int glyphId, GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        outline.Clear();
        if ((uint)glyphId >= (uint)_font.GlyphCount)
        {
            return GlyphOutlineStatus.Invalid;
        }

        if (_font.OutlinesUnsupported is not null)
        {
            return GlyphOutlineStatus.Invalid;
        }

        if (!Type2CharStringInterpreter.Interpret(_font, glyphId, outline, _reporter, _budget, out _))
        {
            outline.Clear();
            return GlyphOutlineStatus.Invalid;
        }

        if (_font.TryGetMatrixAdjustment(glyphId, out Matrix adjustment))
        {
            outline.Transform(adjustment);
        }

        return outline.IsEmpty ? GlyphOutlineStatus.Empty : GlyphOutlineStatus.Complete;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Adobe Technical Note #5177 §3.1 and Note 4: the advance is nominalWidthX plus the charstring's width argument, or
    /// defaultWidthX. Type 2 glyphs have no side bearing (their origin is the glyph origin), so the left side bearing is 0.
    /// </remarks>
    public override GlyphMetrics GetMetrics(int glyphId)
    {
        if ((uint)glyphId >= (uint)_font.GlyphCount || _font.OutlinesUnsupported is not null)
        {
            return default;
        }

        Type2CharStringInterpreter.Interpret(_font, glyphId, null, _reporter, _budget, out double width);
        if (_font.TryGetMatrixAdjustment(glyphId, out Matrix adjustment))
        {
            width = adjustment.TransformVector(width, 0).X;
        }

        return new GlyphMetrics(width, 0);
    }

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5176 §13: the charset's SID of the glyph; CID-keyed fonts have no glyph names.</remarks>
    public override string? GetGlyphName(int glyphId) => _font.GetGlyphName(glyphId);

    /// <inheritdoc/>
    /// <remarks>Adobe Technical Note #5176 §13; the first glyph of a name wins when several share it.</remarks>
    public override bool TryGetGlyphId(string glyphName, out int glyphId)
    {
        ArgumentNullException.ThrowIfNull(glyphName);
        return _font.TryGetGlyphId(glyphName, out glyphId);
    }
}
