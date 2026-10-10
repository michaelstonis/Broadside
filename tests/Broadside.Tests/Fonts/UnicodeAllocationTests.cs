using Broadside.Content;
using Broadside.Fonts;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>Mapping shown codes to Unicode runs once per glyph: once the font's tables are built it allocates nothing (CLAUDE.md, hot paths).</summary>
[Collection(HeavyTestCollection.Name)]
public sealed class UnicodeAllocationTests
{
    [Theory]
    [InlineData("text-standard14.pdf")]
    [InlineData("text-tounicode-bf.pdf")]
    [InlineData("text-glyph-names.pdf")]
    [InlineData("text-cid-embedded-cmap.pdf")]
    [InlineData("text-cidcff-predefined-cmap.pdf")]
    public void Reading_the_Unicode_text_of_every_glyph_allocates_nothing_once_warm(string file)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(Corpus.Path(file)));
        PdfPage page = document.Pages[0];
        var counter = new Counter();

        long allocated = Allocations.Measure(() => page.ProcessContent(counter), 50);

        Assert.Equal(0, allocated);
        Assert.True(counter.Units > 0);
    }

    [Fact]
    public void Looking_up_codes_in_a_ToUnicode_CMap_allocates_nothing_once_warm()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tounicode-bf.pdf"));
        PdfFont font = document.Pages[0].GetFont("F1")!;
        char[] buffer = new char[64];
        int total = 0;

        long allocated = Allocations.Measure(
            () =>
            {
                for (uint code = 0; code < 1024; code++)
                {
                    total += font.GetUnicode(new CharacterCode(code, 2, IsValid: true), buffer, out _);
                }
            },
            50);

        Assert.Equal(0, allocated);
        Assert.True(total > 0);
    }

    private sealed class Counter : ContentProcessor
    {
        public long Units { get; private set; }

        public override ContentEvents Events => ContentEvents.Glyphs;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => Units += glyph.Unicode.Length + (int)glyph.UnicodeSource;
    }
}
