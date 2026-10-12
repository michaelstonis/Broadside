using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>Lookups a renderer or extractor makes per glyph through the font program contract allocate nothing once warm (CLAUDE.md, hot paths).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class FontProgramAllocationTests
{
    [Fact]
    public void Looking_up_TrueType_glyph_names_by_id_and_by_name_allocates_nothing_once_warm()
    {
        // ISO 32000-2 §9.6.5.4: the "post" names are the last step of glyph selection by name.
        byte[] square = TrueTypeBuilder.Rectangle(0, 0, 100, 100);
        var builder = new TrueTypeBuilder { Post = TrueTypeBuilder.Post2(4, ".notdef", "A", "custom", "B") };
        builder.Glyphs.AddRange([[], square, square, square]);
        FontProgram program = new TrueTypeFontProgramParser().Parse(builder.Build(), new FontProgramContext())!;
        int found = 0;

        long allocated = Allocations.Measure(
            () =>
            {
                for (int glyph = 0; glyph < 4; glyph++)
                {
                    found += program.GetGlyphName(glyph)!.Length;
                }

                found += program.TryGetGlyphId("custom", out int custom) ? custom : 0;
                found += program.TryGetGlyphId("missing", out _) ? 1 : 0;
            },
            50);

        Assert.Equal(0, allocated);
        Assert.True(found > 0);
    }
}
