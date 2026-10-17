using Broadside.Fonts;
using Broadside.Fonts.Cff;
using Broadside.Fonts.TrueType;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// <see cref="FontProgram.TryGetSfntData"/>: the bytes a backend hands its platform's font engine to draw glyphs natively (issue
/// #123, open decision 7): TrueType and OpenType programs read without repair, never other formats or repaired programs.
/// ISO 32000-2 §9.9, Table 124.
/// </summary>
public sealed class SfntDataTests
{
    private static readonly byte[] None = [];

    private static readonly byte[] Square = TrueTypeBuilder.Rectangle(0, 0, 100, 100);

    [Theory]
    [InlineData("text-truetype-embedded.pdf", 0x00010000u)]
    [InlineData("text-opentype-cff-embedded.pdf", 0x4F54544Fu)]
    public void An_embedded_TrueType_or_OpenType_program_gives_its_whole_font_file(string fileName, uint version)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        FontProgram program = document.Pages[0].GetFont("F1")!.Program!;

        Assert.True(program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex));
        Assert.Equal(0, faceIndex);
        Assert.Equal(version, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.Span));
        IFontProgramParser parser = version == 0x00010000u ? new TrueTypeFontProgramParser() : new CffFontProgramParser();
        Assert.Equal(program.GlyphCount, parser.Parse(data, new FontProgramContext())!.GlyphCount);
    }

    [Theory]
    [InlineData("text-cff-embedded.pdf")]
    [InlineData("text-type1-embedded.pdf")]
    public void A_bare_CFF_or_Type_1_program_gives_none(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        FontProgram program = document.Pages[0].GetFont("F1")!.Program!;

        Assert.False(program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex));
        Assert.True(data.IsEmpty);
        Assert.Equal(0, faceIndex);
    }

    [Fact]
    public void A_font_of_a_collection_gives_the_collection_and_its_index()
    {
        byte[] first = new TrueTypeBuilder { Glyphs = { None, Square }, Name = "First" }.Build();
        byte[] second = new TrueTypeBuilder { Glyphs = { None, Square, Square }, Name = "Second" }.Build();
        byte[] collection = TrueTypeBuilder.Collection(first, second);

        FontProgram program = new TrueTypeFontProgramParser().Parse(collection, new FontProgramContext { FaceName = "Second" })!;

        Assert.True(program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex));
        Assert.Equal(1, faceIndex);
        Assert.Equal(collection, data.ToArray());
    }

    [Fact]
    public void A_program_read_only_after_a_repair_gives_none()
    {
        // A table that runs past the end of the program is read up to the end, with a diagnostic: a platform engine would read the
        // damaged file differently, so its glyphs must come from the repaired outlines.
        byte[] font = new TrueTypeBuilder { Glyphs = { None, Square } }.Build();
        var context = new FontProgramContext();
        FontProgram program = new TrueTypeFontProgramParser().Parse(font.AsMemory(0, font.Length - 2), context)!;
        FontProgram intact = new TrueTypeFontProgramParser().Parse(font, new FontProgramContext())!;

        Assert.NotEmpty(context.Diagnostics);
        Assert.False(program.TryGetSfntData(out _, out _));
        Assert.True(intact.TryGetSfntData(out _, out _));
    }
}
