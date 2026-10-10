using Broadside.Content;
using Broadside.Fonts;
using Broadside.Tests.Fonts;
using Broadside.TestSupport;

namespace Broadside.Fonts.Cmaps.Tests;

/// <summary>
/// The third method of ISO 32000-2 §9.10.2: a composite font of a known character collection maps its CIDs through the
/// collection's Registry-Ordering-UCS2 table, which the package supplies (mapping-resources-pdf 2dd5e53f). Expected values are the
/// tables' own lines (spot-checked in pdf2unicode/Adobe-Japan1-UCS2 and Adobe-KR-UCS2) and the corpus file's known text.
/// </summary>
public sealed class CidToUnicodePackageTests
{
    [Fact]
    public void Text_cidcff_predefined_cmap_maps_its_CIDs_through_Adobe_Japan1_UCS2_with_the_package()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cidcff-predefined-cmap.pdf"), new PdfOptions().UsePredefinedCMaps());
        var recorder = new Recorder();

        document.Pages[0].ProcessContent(recorder);

        // 90ms-RKSJ-H gives CIDs 264, 3284, 3722; Adobe-Japan1-UCS2 maps them to U+0041, U+65E5, U+672C.
        Assert.Equal([("A", UnicodeSource.CidCollection), ("日", UnicodeSource.CidCollection), ("本", UnicodeSource.CidCollection)], recorder.Glyphs);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "TextUcs2CmapMissing" or "TextUnicodeUnmapped");
    }

    [Theory]
    [InlineData(0x003D, "¥")]
    [InlineData(0x00E6, "0︀")]
    [InlineData(0x046D, "逢\U000E0100")]
    public void A_Japan1_CIDFont_with_Identity_H_maps_its_CIDs_through_the_collection_table(int cid, string expected)
    {
        using PdfDocument document = Open("Japan1");

        Assert.Equal(expected, FontPdf.Font(document).GetUnicode(new CharacterCode((uint)cid, 2, IsValid: true)));
    }

    [Fact]
    public void CID_0_which_the_tables_map_to_U_FFFD_counts_as_unmapped()
    {
        using PdfDocument document = Open("Japan1");
        Span<char> buffer = stackalloc char[8];

        int written = FontPdf.Font(document).GetUnicode(new CharacterCode(0, 2, IsValid: true), buffer, out UnicodeSource source);

        Assert.Equal(("�", UnicodeSource.Unmapped), (new string(buffer[..written]), source));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }

    [Fact]
    public void Adobe_KR_added_in_PDF_2_0_maps_through_its_table()
    {
        using PdfDocument document = Open("KR");

        Assert.Equal("간", FontPdf.Font(document).GetUnicode(new CharacterCode(0x00DF, 2, IsValid: true)));
    }

    [Fact]
    public void An_Identity_collection_does_not_use_a_table()
    {
        using PdfDocument document = Open("Identity");

        Assert.Equal("�", FontPdf.Font(document).GetUnicode(new CharacterCode(0x003D, 2, IsValid: true)));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "TextUcs2CmapMissing");
    }

    private static PdfDocument Open(string ordering) =>
        FontPdf.Open(
            "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H /DescendantFonts [5 0 R] >>",
            new PdfOptions().UsePredefinedCMaps(),
            $"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement 0 >> >>");

    private sealed class Recorder : ContentProcessor
    {
        public List<(string Text, UnicodeSource Source)> Glyphs { get; } = [];

        public override ContentEvents Events => ContentEvents.Glyphs;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => Glyphs.Add((new string(glyph.Unicode), glyph.UnicodeSource));
    }
}
