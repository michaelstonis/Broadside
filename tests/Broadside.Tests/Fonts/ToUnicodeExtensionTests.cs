using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Replacing text in a Type 0 font with codes found through the embedded program's "cmap" (issue #641): the font's ToUnicode CMap is
/// extended, or created, so the new codes read back as the new text, and other pages that use the font read as before. ISO 32000-2
/// §9.10.3, §9.7.4.2 and §9.7.6.2.
/// </summary>
public sealed class ToUnicodeExtensionTests
{
    [Fact]
    public void An_extended_CMap_reads_back_the_new_codes_and_other_pages_using_the_font_are_unaffected()
    {
        using PdfDocument document = PdfDocument.Open(TwoPages(withToUnicode: true));
        PdfPage first = document.Pages[0], second = document.Pages[1];
        List<(uint Code, UnicodeSource Source)> before = [.. second.ExtractText().Glyphs.Select(static glyph => (glyph.CharacterCode, glyph.UnicodeSource))];

        PdfTextEditResult result = first.ReplaceText(TextReplaceTests.Single(first, "Hello"), "Help");

        Assert.Equal((1, 0), (result.Replaced, result.UsedFallbackFont));
        Assert.Equal(TextEditDiagnostics.TextEditToUnicodeExtended.Name, Assert.Single(result.Diagnostics).Code);
        Assert.Equal("Help", TextReplaceTests.Text(first));
        TextGlyph p = first.ExtractText().Glyphs[^1];
        Assert.Equal((uint)('p' - 31), p.CharacterCode);
        Assert.Equal(UnicodeSource.ToUnicode, p.UnicodeSource);
        Assert.Equal("Hello", TextReplaceTests.Text(second));
        Assert.Equal(before, [.. second.ExtractText().Glyphs.Select(static glyph => (glyph.CharacterCode, glyph.UnicodeSource))]);

        using PdfDocument reopened = PdfDocument.Open(TextReplaceTests.Save(document));
        Assert.Equal("Help", TextReplaceTests.Text(reopened.Pages[0]));
        Assert.Equal("Hello", TextReplaceTests.Text(reopened.Pages[1]));
        Assert.Equal(UnicodeSource.ToUnicode, reopened.Pages[0].ExtractText().Glyphs[^1].UnicodeSource);
    }

    [Fact]
    public void A_font_without_a_ToUnicode_CMap_gets_one_for_the_new_codes()
    {
        using PdfDocument document = PdfDocument.Open(TwoPages(withToUnicode: false));
        PdfPage first = document.Pages[0];

        PdfTextEditResult result = first.ReplaceText(TextReplaceTests.Single(first, "Hello"), "Hey");

        Assert.Equal(1, result.Replaced);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == TextEditDiagnostics.TextEditToUnicodeExtended.Name);
        var font = Assert.IsType<PdfType0Font>(first.ExtractText().Glyphs[0].Font);
        CosStream cmap = Assert.IsType<CosStream>(document.Resolve(font.Dictionary[new CosName("ToUnicode")]));
        string data = Encoding.Latin1.GetString(document.DecodeStream(cmap).Span);
        Assert.Contains("<0000> <FFFF>", data, StringComparison.Ordinal);
        Assert.Contains("<005A> <0079>", data, StringComparison.Ordinal);
        Assert.Equal("Hey", TextReplaceTests.Text(first));
        Assert.Equal("Hello", TextReplaceTests.Text(document.Pages[1]));
    }

    /// <summary>
    /// Two pages showing "Hello" in one Type 0 font (Identity-H, CIDToGIDMap Identity) over <see cref="TextSamples.FontProgram"/>,
    /// whose glyph for a printable ASCII character is its code less 31; the ToUnicode CMap, when present, maps only the codes shown.
    /// </summary>
    internal static byte[] TwoPages(bool withToUnicode)
    {
        byte[] program = TextSamples.FontProgram;
        string hello = Hex("Hello");
        string toUnicode = withToUnicode ? " /ToUnicode 11 0 R" : string.Empty;
        string cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Test-UCS def /CMapType 2 def\n"
            + "1 begincodespacerange <0000> <FFFF> endcodespacerange\n"
            + "4 beginbfchar <0029> <0048> <0046> <0065> <004D> <006C> <0050> <006F> endbfchar\n"
            + "endcmap CMapName currentdict /CMap defineresource pop end end";
        string stream = string.Create(CultureInfo.InvariantCulture, $"<< /Length {program.Length} /Length1 {program.Length} >>\nstream\n") + Encoding.Latin1.GetString(program) + "\nendstream";
        return ContentSamples.File(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 7 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            ContentSamples.Stream($"BT /F1 12 Tf 72 700 Td <{hello}> Tj ET"),
            ContentSamples.Stream($"BT /F1 12 Tf 72 600 Td <{hello}> Tj ET"),
            $"<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideSample /Encoding /Identity-H /DescendantFonts [8 0 R]{toUnicode} >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /BroadsideSample /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 9 0 R /DW 600 /CIDToGIDMap /Identity >>",
            "<< /Type /FontDescriptor /FontName /BroadsideSample /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 100 /FontFile2 10 0 R >>",
            stream,
            ContentSamples.Stream(cmap),
        ]);
    }

    private static string Hex(string text) => string.Concat(text.Select(static character => (character - 31).ToString("X4", CultureInfo.InvariantCulture)));
}
