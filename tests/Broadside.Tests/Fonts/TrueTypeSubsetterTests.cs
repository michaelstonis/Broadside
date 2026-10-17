using Broadside.Fonts;
using Broadside.Fonts.TrueType;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The TrueType subsetter (issue #150) over programs built in memory: a subset re-parses with exactly the glyphs it kept. ISO 32000-2
/// §9.9 (Table 124) and §9.9.2.
/// </summary>
public sealed class TrueTypeSubsetterTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(32)]
    public void A_damaged_table_version_does_not_move_the_subset_tables(int maxpLength)
    {
        // A fuzzing finding (truetype-subset, issue #343): "maxp" version 0x01000000 and an empty .notdef. In the subset the byte
        // before "maxp" is the zero low byte of the last "loca" offset, so a copied version read as a valid one a byte early and
        // numGlyphs as 0. Damaged "head" and "hhea" versions are copied the same way.
        byte[] maxp = new byte[maxpLength];
        maxp[0] = 0x01;
        maxp[5] = 2;
        byte[] program = new TrueTypeBuilder
        {
            Glyphs = { Array.Empty<byte>(), TrueTypeBuilder.Rectangle(0, 0, 100, 100) },
            LongOffsets = true,
            Overrides = { ["maxp"] = maxp },
        }.Build();
        program[Offset(program, "head") + 1] = 0x02;
        program[Offset(program, "hhea") + 1] = 0x02;
        Assert.Equal(2, Parse(program).GlyphCount);

        foreach (int[] glyphs in (int[][])[[], [1]])
        {
            SubsetProgram subset = Assert.IsType<SubsetProgram>(TrueTypeSubsetter.Subset(SfntFile.Open(program, new FontProgramContext())!, glyphs));
            var context = new FontProgramContext { IsCidFont = true };
            FontProgram result = Parse(subset.Program, context);

            Assert.Equal(subset.Glyphs.Length, result.GlyphCount);
            Assert.Empty(context.Diagnostics);
        }
    }

    private static FontProgram Parse(byte[] program, FontProgramContext? context = null) =>
        Assert.IsAssignableFrom<FontProgram>(new TrueTypeFontProgramParser().Parse(program, context ?? new FontProgramContext()));

    private static int Offset(byte[] font, string tag)
    {
        int count = (font[4] << 8) | font[5];
        for (int index = 0; index < count; index++)
        {
            int record = 12 + (16 * index);
            if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
            {
                return (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
            }
        }

        throw new InvalidOperationException(tag);
    }
}
