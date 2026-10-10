using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>
/// A Type 0 font that names a predefined CMap when no font resolver supplies it (the Broadside.Fonts.Cmaps package is not
/// configured): the document opens, one diagnostic names the CMap and the package, and codes are still split by the CMap's
/// codespace (Table 116 CMaps mix 1-, 2- and 4-byte codes) and read in its writing mode, each selecting CID 0.
/// ISO 32000-2 §9.7.5.2 (Table 116), §9.7.6.2 and §9.7.6.3.
/// </summary>
public class PredefinedCMapFallbackTests
{
    private const string SystemInfo = "/CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >>";

    [Fact]
    public void A_missing_predefined_CMap_is_named_in_a_diagnostic_and_its_codespace_still_splits_codes()
    {
        using PdfDocument document = Open("/90ms-RKSJ-H");
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph[] glyphs = CompositeFontCorpusTests.Glyphs(font, [0x41, 0x93, 0xFA, 0x96, 0x7B]);

        Assert.Equal([1, 2, 2], glyphs.Select(glyph => glyph.Code.Length));
        Assert.Equal([0x41u, 0x93FAu, 0x967Bu], glyphs.Select(glyph => glyph.Code.Value));
        Assert.All(glyphs, glyph => Assert.True(glyph.Code.IsValid));
        Assert.All(glyphs, glyph => Assert.Equal(0, glyph.Cid));
        Assert.Equal(("90ms-RKSJ-H", "Japan1", 2, WritingMode.Horizontal), (font.Encoding.Name, font.Encoding.SystemInfo?.Ordering, font.Encoding.SystemInfo?.Supplement, font.WritingMode));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("CMapUnavailable", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Contains("90ms-RKSJ-H", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("UsePredefinedCMaps()", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UniGB-UTF16-V", "GB1", "D800DC00 4E00", "4 2", WritingMode.Vertical)]
    [InlineData("GBK2K-H", "GB1", "81308130 41 8140", "4 1 2", WritingMode.Horizontal)]
    [InlineData("CNS-EUC-V", "CNS1", "8EA2A1A1 A1A1 20", "4 2 1", WritingMode.Vertical)]
    [InlineData("KSCpc-EUC-H", "Korea1", "84 A141 FE", "1 2 1", WritingMode.Horizontal)]
    [InlineData("H", "Japan1", "2121 7E7E", "2 2", WritingMode.Horizontal)]
    [InlineData("ETenms-B5-V", "CNS1", "41 A140", "1 2", WritingMode.Vertical)]
    public void Each_missing_CMap_keeps_its_character_collection_writing_mode_and_code_lengths(string name, string ordering, string codes, string lengths, WritingMode mode)
    {
        using PdfDocument document = Open("/" + name);
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph[] glyphs = CompositeFontCorpusTests.Glyphs(font, Convert.FromHexString(codes.Replace(" ", string.Empty, StringComparison.Ordinal)));

        Assert.Equal(lengths, string.Join(' ', glyphs.Select(glyph => glyph.Code.Length.ToString(CultureInfo.InvariantCulture))));
        Assert.All(glyphs, glyph => Assert.True(glyph.Code.IsValid));
        Assert.Equal((ordering, mode), (font.Encoding.SystemInfo?.Ordering, font.WritingMode));
    }

    [Fact]
    public void A_name_that_is_not_a_predefined_CMap_reads_codes_as_Identity_H()
    {
        using PdfDocument document = Open("/Broadside-Unknown-H");
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        Assert.Same(CMap.IdentityH, font.Encoding);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("CMapUnavailable", diagnostic.Code);
        Assert.Contains("Broadside-Unknown-H", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Identity-H", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_embedded_CMap_that_uses_a_missing_predefined_CMap_takes_its_codespace()
    {
        string body = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Child-H def /90ms-RKSJ-H usecmap 1 begincidchar <41> 5 endcidchar endcmap end end";
        string cmap = string.Create(CultureInfo.InvariantCulture, $"<< /Type /CMap /CMapName /Child-H {SystemInfo} /UseCMap /90ms-RKSJ-H /Length {body.Length} >>\nstream\n{body}\nendstream");
        using PdfDocument document = Open("7 0 R", cmap);
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph[] glyphs = CompositeFontCorpusTests.Glyphs(font, [0x41, 0x93, 0xFA]);

        Assert.Equal([(1, 5), (2, 0)], glyphs.Select(glyph => (glyph.Code.Length, glyph.Cid)));
        Assert.Equal("90ms-RKSJ-H", font.Encoding.Parent?.Name);
        Assert.Contains("90ms-RKSJ-H", Assert.Single(document.Diagnostics, d => d.Code == "CMapUnavailable").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_reads_a_document_whose_predefined_CMap_is_missing()
    {
        using PdfDocument document = Open("/UniJIS-UTF16-H", options: new PdfOptions().UseStrict());

        Assert.Equal("UniJIS-UTF16-H", CompositeFontCorpusTests.Font(document).Encoding.Name);
        Assert.Equal(DiagnosticSeverity.Information, Assert.Single(document.Diagnostics).Severity);
    }

    /// <summary>A Type 0 font (4) with the given Encoding over a non-embedded CIDFontType0 (5) of Adobe-Japan1-2; extra objects from 6.</summary>
    private static PdfDocument Open(string encoding, string? extra = null, PdfOptions? options = null) =>
        FontPdf.Open(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideCID /Encoding {encoding} /DescendantFonts [5 0 R] >>",
            (options ?? new PdfOptions()).UseSystemFontResolver(null),
            [$"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /BroadsideCID {SystemInfo} >>", "null", .. extra is null ? Array.Empty<string>() : [extra]]);
}
