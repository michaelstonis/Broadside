using System.Text;
using Broadside.Fonts.TrueType;

namespace Broadside.Fonts;

/// <summary>
/// An embedded TrueType program a canvas (or a form field appearance) shows text with: a Type 0 font with <c>Identity-H</c> two-byte
/// codes over a CIDFontType2 whose CIDs are the program's glyph ids, backed by the document's one growing subset of the program.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7.4–§9.7.6, §9.8, §9.9 and §9.10.3; see <see cref="SharedFontSubset"/> for the objects written and how the subset
/// grows and is completed before a save. A character maps to the glyph the program's Unicode <c>cmap</c> subtable gives it ((3, 10),
/// else (3, 1), else (0, any)); every glyph the canvas writes is recorded in the subset with the character it was written for, which
/// the <c>ToUnicode</c> CMap maps it back to. The font has no kerning of its own (the program's <c>kern</c> and <c>GPOS</c> tables are
/// not read).
/// </remarks>
internal sealed class TrueTypeCanvasFont : PdfCanvasFont
{
    private readonly SharedFontSubset _subset;

    private TrueTypeCanvasFont(PdfDocument document, SharedFontSubset subset)
        : base(document, subset.Font) => _subset = subset;

    /// <inheritdoc/>
    public override string Name => _subset.PostScriptName;

    /// <inheritdoc/>
    public override double Ascent => _subset.Ascent;

    /// <inheritdoc/>
    public override double Descent => _subset.Descent;

    /// <inheritdoc/>
    public override double CapHeight => _subset.CapHeight;

    /// <inheritdoc/>
    public override double XHeight => _subset.XHeight;

    /// <inheritdoc/>
    public override PdfRectangle FontBBox => _subset.FontBBox;

    /// <inheritdoc/>
    internal override int CodeLength => 2;

    /// <summary>
    /// Returns the document's font for a TrueType program, adding the subset font to the document the first time the program is
    /// embedded; the glyphs of <paramref name="text"/> are added to its subset at once.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="resolution">The program's bytes and face (from a caller or a font resolver).</param>
    /// <param name="text">Text the font must be able to show, whose glyphs it includes at once; may be empty.</param>
    /// <param name="uncovered">The first character of <paramref name="text"/> the program has no glyph for, else <see langword="null"/>.</param>
    /// <param name="problem">Why the program cannot be embedded, else <see langword="null"/>.</param>
    /// <returns>The font, or <see langword="null"/> when <paramref name="uncovered"/> or <paramref name="problem"/> is set.</returns>
    public static TrueTypeCanvasFont? Get(PdfDocument document, FontResolution resolution, IReadOnlyList<string> text, out string? uncovered, out string? problem)
    {
        uncovered = null;
        problem = null;
        Dictionary<int, int>? glyphs;
        if (document.FontSubsets.Find(document, resolution) is { } shared)
        {
            if ((glyphs = shared.Cover(text, out uncovered)) is null)
            {
                return null;
            }

            shared.Include(document, glyphs);
            return new TrueTypeCanvasFont(document, shared);
        }

        if (SfntFile.Open(resolution.Data, new FontProgramContext { FaceIndex = resolution.FaceIndex }) is not { } sfnt)
        {
            problem = "it is not a TrueType or OpenType program (or the face index is out of range)";
            return null;
        }

        if (!sfnt.Has(SfntFile.Tag("glyf")))
        {
            problem = "it has no glyf table (an OpenType program with CFF outlines, which embeds as FontFile3)";
            return null;
        }

        if (document.GetSubstituteProgram(resolution, reference: null) is not { } program || PickCharacterMap(program) is not { } cmap)
        {
            problem = "it has no Unicode cmap subtable (3, 10), (3, 1) or (0, any) to map characters to glyphs";
            return null;
        }

        if ((glyphs = SharedFontSubset.Cover(cmap, program.GlyphCount, text, out uncovered)) is null
            || SharedFontSubset.Create(document, sfnt, program, cmap, glyphs, out problem) is not { } created)
        {
            return null;
        }

        document.FontSubsets.Add(resolution, created);
        return new TrueTypeCanvasFont(document, created);
    }

    /// <inheritdoc/>
    internal override bool TryGetGlyph(int scalar, out CanvasGlyph glyph)
    {
        int id = _subset.GlyphFor(scalar);
        glyph = id > 0 ? new CanvasGlyph((uint)id, _subset.WidthOf(id), id, scalar) : default;
        return id > 0;
    }

    /// <inheritdoc/>
    internal override void OnShown(in CanvasGlyph glyph) => _subset.Include(Document, glyph.Scalar, glyph.Key);

    /// <inheritdoc/>
    internal override string DescribeUnencodable(Rune character, int index) =>
        $"The character {Describe(character)} at index {index} has no glyph in the embedded font {Name}: its Unicode cmap maps it to none "
        + "(ISO 32000-2 §9.7.4.2). Show it in a font whose program has a glyph for it.";

    /// <summary>The Unicode subtable: (3, 10), else (3, 1), else (0, any).</summary>
    private static FontCharacterMap? PickCharacterMap(FontProgram program)
    {
        FontCharacterMap? best = null;
        int rank = 0;
        foreach (FontCharacterMap map in program.CharacterMaps)
        {
            int candidate = (map.PlatformId, map.EncodingId) switch
            {
                (3, 10) => 3,
                (3, 1) => 2,
                (0, _) => 1,
                _ => 0,
            };
            if (candidate > rank)
            {
                (best, rank) = (map, candidate);
            }
        }

        return best;
    }
}
