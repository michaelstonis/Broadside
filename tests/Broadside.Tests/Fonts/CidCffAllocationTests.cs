using Broadside.Fonts;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The CID-keyed CFF hot paths allocate nothing once warm: outlining every glyph with its Font DICT's Private DICT (and the matrix
/// adjustment of a Font DICT with its own matrix) and looking CIDs up through the inverse charset. ISO 32000-2 §9.7.4.2.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class CidCffAllocationTests
{
    private static readonly int[] Cids = [0, 264, 3284, 3722, 1, 9000];

    [Fact]
    public void Outlining_glyphs_and_selecting_them_by_CID_allocates_nothing_once_warm()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cidcff-predefined-cmap.pdf"));
        PdfCidFont font = Assert.IsAssignableFrom<PdfCidFont>(CompositeFontCorpusTests.Font(document).DescendantFont);
        CffBuilder scaled = CidCffProgramTests.TwoFontDicts([0, 0, 0, 1]);
        scaled.FontDicts[1].FontMatrix = [0.0005, 0, 0, 0.0005, 0, 0];
        FontProgram adjusted = Assert.IsAssignableFrom<FontProgram>(new Broadside.Fonts.Cff.CffFontProgramParser().Parse(scaled.Build(), new FontProgramContext()));
        FontProgram program = font.Program!;
        var outline = new GlyphOutline();
        int total = 0;

        long allocated = Allocations.Measure(() => total = Run(font, program, adjusted, outline), 50);

        Assert.True(total > 0);
        Assert.Equal(0, allocated);

        static int Run(PdfCidFont font, FontProgram program, FontProgram adjusted, GlyphOutline outline)
        {
            int total = 0;
            for (int round = 0; round < 50; round++)
            {
                foreach (int cid in Cids)
                {
                    total += font.GetGlyphId(cid);
                }

                for (int glyph = 0; glyph < program.GlyphCount; glyph++)
                {
                    program.GetOutline(glyph, outline);
                    total += outline.Path.Verbs.Length;
                    adjusted.GetOutline(glyph % adjusted.GlyphCount, outline);
                    total += outline.Path.Verbs.Length + (int)adjusted.GetMetrics(glyph % adjusted.GlyphCount).AdvanceWidth;
                }
            }

            return total;
        }
    }
}
