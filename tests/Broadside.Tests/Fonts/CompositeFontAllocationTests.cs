using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>Reading glyphs of a composite font is a hot path: once the font's tables are built it allocates nothing (CLAUDE.md, hot paths).</summary>
public class CompositeFontAllocationTests
{
    [Theory]
    [InlineData("text-cid-identity-h.pdf")]
    [InlineData("text-cid-identity-v.pdf")]
    [InlineData("text-cid-embedded-cmap.pdf")]
    public void Reading_codes_CIDs_glyph_ids_and_metrics_allocates_nothing_once_warm(string file)
    {
        using PdfDocument document = TrueTypeCorpusTests.Open(file);
        PdfType0Font font = CompositeFontCorpusTests.Font(document);
        byte[] text = new byte[4096];
        for (int index = 0; index < text.Length; index++)
        {
            text[index] = (byte)(0x40 + (index * 7 % 0x60));
        }

        double total = 0;
        for (int warmUp = 0; warmUp < 50; warmUp++)
        {
            total += Read(font, text);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        total += Read(font, text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(total > 0);
    }

    private static double Read(PdfType0Font font, ReadOnlySpan<byte> text)
    {
        double sum = 0;
        while (!text.IsEmpty)
        {
            CidGlyph glyph = font.ReadGlyph(text);
            sum += glyph.Width + glyph.GlyphId - glyph.VerticalMetrics.VerticalAdvance;
            text = text[glyph.Code.Length..];
        }

        return sum;
    }
}
