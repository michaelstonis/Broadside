using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Glyph selection in a CIDFontType0 through the document API: a CID-keyed CFF program maps CIDs to glyphs through its charset,
/// a name-keyed one uses the CID as the glyph id, and a CID without a glyph falls back to the notdef mapping, then CID 0.
/// ISO 32000-2 §9.7.4.2 (p.346-347), §9.7.6.3 and §9.9 (Table 124).
/// </summary>
public sealed class CidFontType0Tests
{
    private const string SystemInfo = "/CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >>";

    [Theory]
    [InlineData("/CIDFontType0C")]
    [InlineData("/OpenType")]
    public void A_CID_keyed_CFF_program_maps_CIDs_to_glyphs_through_its_charset(string subtype)
    {
        byte[] cff = CidCffProgramTests.TwoFontDicts([0, 0, 0, 1]).Build();
        using PdfDocument document = Open(subtype == "/OpenType" ? CffBuilder.OpenType(cff) : cff, subtype);
        PdfType0Font font = CompositeFontCorpusTests.Font(document);
        PdfCidFont cidFont = Assert.IsType<PdfCidFontType0>(font.DescendantFont);

        Assert.Equal([0, 1, 2, 0, 0], new[] { 0, 264, 3284, 1, 500 }.Select(cidFont.GetGlyphId));
        CidGlyph glyph = font.ReadGlyph([0x0C, 0xD4]);
        Assert.Equal((3284, 2, 600.0), (glyph.Cid, glyph.GlyphId, glyph.Width));
        Assert.Equal("M 10,20 L 210,20 L 210,220 Z", OutlineText.Of(Assert.IsAssignableFrom<FontProgram>(cidFont.Program), glyph.GlyphId));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_name_keyed_CFF_program_in_a_CIDFontType0_uses_the_CID_as_the_glyph_id()
    {
        var builder = new CffBuilder();
        builder.Glyphs.AddRange([(".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2("endchar")), ("B", CffBuilder.T2("endchar"))]);
        using PdfDocument document = Open(builder.Build(), "/CIDFontType0C");
        PdfCidFont cidFont = Assert.IsType<PdfCidFontType0>(CompositeFontCorpusTests.Font(document).DescendantFont);

        Assert.Equal([0, 1, 2, 0], new[] { 0, 1, 2, 3 }.Select(cidFont.GetGlyphId));
    }

    [Fact]
    public void A_CID_without_a_glyph_shows_the_glyph_of_its_notdef_mapping()
    {
        string cmap = Stream(
            "/Type /CMap /CMapName /Test-H " + SystemInfo,
            "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Test-H def 1 begincodespacerange <00> <FF> endcodespacerange "
            + "1 begincidchar <41> 999 endcidchar 1 beginnotdefchar <41> 264 endnotdefchar endcmap end end");
        using PdfDocument document = Open(CidCffProgramTests.TwoFontDicts([0, 0, 0, 1]).Build(), "/CIDFontType0C", encoding: "9 0 R", cmap);

        CidGlyph glyph = CompositeFontCorpusTests.Font(document).ReadGlyph("A"u8);

        Assert.Equal((999, 1), (glyph.Cid, glyph.GlyphId));
    }

    /// <summary>A Type 0 font (4) over a CIDFontType0 (5) with a descriptor (6) and its FontFile3 program (7); extra objects from 8.</summary>
    private static PdfDocument Open(byte[] program, string subtype, string encoding = "/Identity-H", params string[] extra)
    {
        string data = Encoding.Latin1.GetString(program);
        return FontPdf.Open(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideCID /Encoding {encoding} /DescendantFonts [5 0 R] >>",
            [
                $"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /BroadsideCID {SystemInfo} /FontDescriptor 6 0 R /DW 600 /W [264 [500]] >>",
                "<< /Type /FontDescriptor /FontName /BroadsideCID /Flags 4 /FontBBox [0 0 700 700] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 7 0 R >>",
                Stream($"/Subtype {subtype}", data),
                "null",
                .. extra,
            ]);
    }

    private static string Stream(string entries, string data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream");
}
