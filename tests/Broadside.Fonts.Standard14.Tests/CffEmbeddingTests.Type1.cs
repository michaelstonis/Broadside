using Broadside.Fonts.Cff;
using Broadside.Fonts.Type1;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Type 1 programs embedded through <see cref="PdfDocument.EmbedFont(ReadOnlyMemory{byte}, int)"/> (issue #152): converted to a CFF
/// program with Type 2 charstrings (subroutines, flex and <c>seac</c> resolved), then embedded and subset as CFF (#151). The
/// converted outlines equal the Type 1 outlines (#52) within 2^-16 of a glyph space unit (16.16 fixed-point operands). Type 1 Font
/// Format chapters 6 and 8; Adobe Technical Notes #5176 and #5177; ISO 32000-2 §9.9 and §9.9.2.
/// </summary>
public sealed partial class CffEmbeddingTests
{
    /// <summary>The largest difference allowed between a Type 1 and a converted coordinate or width, in glyph space units.</summary>
    private const double Type1Tolerance = 1.0 / 65536;

    [Theory]
    [InlineData(Standard14Font.Symbol, "αβγ∑∫≠∞")]
    [InlineData(Standard14Font.ZapfDingbats, "✓✔✈❤➔")]
    public void Every_glyph_of_the_foxit_symbol_and_dingbats_programs_converts_to_the_type1_outline_and_the_subset_renders_in_poppler(Standard14Font standard, string sample)
    {
        // The package's Foxit programs are bare CFF: they are rebuilt as Type 1 programs (hsbw, hint replacement, flex, div).
        FontProgram foxit = Parse(Standard14FontResolver.GetProgram(standard).ToArray());
        byte[] type1 = Type1TestFonts.FromCff(Standard14FontResolver.GetProgram(standard));
        FontProgram original = new Type1FontProgramParser().Parse(type1, new FontProgramContext())!;
        Assert.Equal(foxit.GlyphCount, original.GlyphCount);

        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.EmbedFont(type1);
        Show(document.Pages[0], font, sample);

        // Before a save the document holds the whole converted program: every glyph, in the Type 1 program's order.
        TextPage pending = document.Pages[0].ExtractText();
        Assert.Equal(sample, pending.Text);
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(pending.Glyphs[0].Font).DescendantFont!;
        FontProgram converted = Parse(document.DecodeStream(descendant.Descriptor!.FontFile3!).ToArray());
        Assert.Equal(original.GlyphCount, converted.GlyphCount);
        for (int glyph = 0; glyph < original.GlyphCount; glyph++)
        {
            AssertSameType1Glyph(original, glyph, converted, glyph);
            AssertSameType1Glyph(foxit, glyph, converted, glyph, tolerance: 1e-3, widths: false);
        }

        byte[] saved = Saved(document);
        AssertPopplerDraws(saved, [sample], "CID Type 0C");
        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        PdfCidFont subsetFont = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram subset = Parse(reopened.DecodeStream(subsetFont.Descriptor!.FontFile3!).ToArray());
        Assert.Equal(sample.Length + 1, subset.GlyphCount);
        foreach (TextGlyph glyph in text.Glyphs)
        {
            AssertSameType1Glyph(original, (int)glyph.CharacterCode, subset, subsetFont.GetGlyphId((int)glyph.CharacterCode));
        }
    }

    [Fact]
    public void A_seac_glyph_converts_to_a_composed_outline_so_its_subset_needs_no_components()
    {
        byte[] type1 = new Type1Builder()
            .Glyph(".notdef", "0 500 hsbw endchar")
            .Glyph("A", "20 600 hsbw 0 0 rmoveto 280 700 rlineto 280 -700 rlineto closepath endchar")
            .Glyph("acute", "100 300 hsbw 0 750 rmoveto 100 120 rlineto 40 -20 rlineto closepath endchar")
            .Glyph("Aacute", "20 600 hsbw 100 160 50 65 194 seac")
            .Glyph("x", "10 500 hsbw 0 0 rmoveto 0 -20 30 -40 50 -40 rrcurveto 400 0 rlineto 0 400 rlineto closepath endchar")
            .BuildPfb();
        FontProgram original = new Type1FontProgramParser().Parse(type1, new FontProgramContext())!;

        byte[] saved = Draw(document => document.EmbedFont(type1), "Áx");

        using PdfDocument reopened = PdfDocument.Open(saved);
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal("Áx", text.Text);
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        Assert.Equal(new CosName("CIDFontType0C"), descendant.Descriptor!.FontFile3!.Dictionary[new CosName("Subtype")]);
        FontProgram subset = Parse(reopened.DecodeStream(descendant.Descriptor.FontFile3).ToArray());

        // .notdef, Aacute and x only: the accent and its base are drawn by Aacute's own charstring, not referenced.
        Assert.Equal(3, subset.GlyphCount);
        Assert.True(original.TryGetGlyphId("Aacute", out int aacute));
        Assert.Equal(aacute, (int)text.Glyphs[0].CharacterCode);
        AssertSameType1Glyph(original, aacute, subset, descendant.GetGlyphId(aacute));
        var outline = new GlyphOutline();
        _ = subset.GetOutline(descendant.GetGlyphId(aacute), outline);
        Assert.Equal(2, outline.Path.Verbs.ToArray().Count(verb => verb == PathVerb.MoveTo));
        AssertPopplerDraws(saved, ["Áx"], "CID Type 0C");
    }

    [Fact]
    public void A_type1_program_with_a_skewed_font_matrix_keeps_it_and_embedding_is_deterministic()
    {
        var builder = new Type1Builder { FontMatrix = "[0.001 0 0.0002 0.001 0 0] readonly def" }
            .Glyph(".notdef", "0 500 hsbw endchar")
            .Glyph("H", "50 700 hsbw 0 0 rmoveto 600 hlineto 700 vlineto -600 hlineto closepath endchar");
        byte[] type1 = builder.BuildPfb();

        byte[] first = Draw(document => document.EmbedFont(type1), "HH");
        Assert.Equal(first, Draw(document => document.EmbedFont(type1), "HH"));

        using PdfDocument reopened = PdfDocument.Open(first);
        TextPage text = reopened.Pages[0].ExtractText();
        PdfCidFont descendant = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font).DescendantFont!;
        FontProgram subset = Parse(reopened.DecodeStream(descendant.Descriptor!.FontFile3!).ToArray());
        Assert.Equal(new Matrix(0.001, 0, 0.0002, 0.001, 0, 0), subset.FontMatrix);
        Assert.Equal(700, descendant.GetWidth((int)text.Glyphs[0].CharacterCode), 3);
        AssertPopplerDraws(first, ["HH"], "CID Type 0C");
    }

    /// <summary>A Type 1 glyph and a converted glyph draw the same verbs, points and advance within <see cref="Type1Tolerance"/>.</summary>
    private static void AssertSameType1Glyph(FontProgram original, int originalGlyph, FontProgram converted, int convertedGlyph, double tolerance = Type1Tolerance, bool widths = true)
    {
        var before = new GlyphOutline();
        var after = new GlyphOutline();
        Assert.True(original.GetOutline(originalGlyph, before) is GlyphOutlineStatus.Complete or GlyphOutlineStatus.Empty);
        Assert.True(converted.GetOutline(convertedGlyph, after) is GlyphOutlineStatus.Complete or GlyphOutlineStatus.Empty);
        string name = original.GetGlyphName(originalGlyph) ?? originalGlyph.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Type 2 closes a contour implicitly: compare the drawing verbs.
        PathVerb[] expected = before.Path.Verbs.ToArray().Where(verb => verb != PathVerb.Close).ToArray();
        PathVerb[] actual = after.Path.Verbs.ToArray().Where(verb => verb != PathVerb.Close).ToArray();
        Assert.True(expected.SequenceEqual(actual), $"{name}: verbs differ");
        Assert.Equal(before.Path.Points.Length, after.Path.Points.Length);
        for (int index = 0; index < before.Path.Points.Length; index++)
        {
            PathPoint p = before.Path.Points[index];
            PathPoint q = after.Path.Points[index];
            Assert.True(Math.Abs(p.X - q.X) <= tolerance && Math.Abs(p.Y - q.Y) <= tolerance, $"{name} point {index}: {p} vs {q}");
        }

        Assert.Equal(original.FontMatrix, converted.FontMatrix);
        Assert.True(!widths || Math.Abs(original.GetMetrics(originalGlyph).AdvanceWidth - converted.GetMetrics(convertedGlyph).AdvanceWidth) <= tolerance, $"{name}: width");
    }
}
