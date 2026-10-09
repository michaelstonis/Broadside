using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Simple-font encodings: the predefined encodings of Annex D (rows checked against Tables D.2 and D.4), encoding dictionaries with
/// <c>BaseEncoding</c> and <c>Differences</c>, and how malformed entries are repaired. ISO 32000-2 §9.6.5.1, §9.6.5.2, Annex D.
/// </summary>
public class EncodingTests
{
    public static TheoryData<string, int, string> PredefinedRows => new()
    {
        // Table D.2, STD column (octal 047 quoteright, 140 quoteleft, 256 fi, 341 AE, 350 Lslash).
        { "", 0x27, "quoteright" },
        { "", 0x60, "quoteleft" },
        { "", 0xAE, "fi" },
        { "", 0xE1, "AE" },
        { "", 0xE8, "Lslash" },
        { "", 0x80, ".notdef" },

        // Table D.2, WIN column and notes 1 to 3 (0x18 is defined in PDFDocEncoding only).
        { "/Encoding /WinAnsiEncoding", 0x18, ".notdef" },
        { "/Encoding /WinAnsiEncoding", 0x80, "Euro" },
        { "/Encoding /WinAnsiEncoding", 0x8A, "Scaron" },
        { "/Encoding /WinAnsiEncoding", 0x92, "quoteright" },
        { "/Encoding /WinAnsiEncoding", 0x9F, "Ydieresis" },
        { "/Encoding /WinAnsiEncoding", 0x9D, "bullet" },
        { "/Encoding /WinAnsiEncoding", 0xFF, "ydieresis" },

        // Table D.2, MAC column and note 6; 0xF0 apple and 0xDB Euro are Mac OS Roman only (§9.6.5.4 Table 113).
        { "/Encoding /MacRomanEncoding", 0x8A, "adieresis" },
        { "/Encoding /MacRomanEncoding", 0xD8, "ydieresis" },
        { "/Encoding /MacRomanEncoding", 0xDE, "fi" },
        { "/Encoding /MacRomanEncoding", 0xDB, "currency" },
        { "/Encoding /MacRomanEncoding", 0xF0, ".notdef" },
        { "/Encoding /MacRomanEncoding", 0xCA, "space" },

        // Table D.4 (octal 047 Acutesmall, 064 fouroldstyle, 276 AEsmall, 330 Ydieresissmall).
        { "/Encoding /MacExpertEncoding", 0x27, "Acutesmall" },
        { "/Encoding /MacExpertEncoding", 0x34, "fouroldstyle" },
        { "/Encoding /MacExpertEncoding", 0xBE, "AEsmall" },
        { "/Encoding /MacExpertEncoding", 0xD8, "Ydieresissmall" },
        { "/Encoding /MacExpertEncoding", 0x41, ".notdef" },
    };

    [Theory]
    [MemberData(nameof(PredefinedRows))]
    public void A_predefined_encoding_gives_the_name_annex_D_lists(string encoding, int code, string expected)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica {encoding} >>");
        Assert.Equal(expected, FontPdf.SimpleFont(document).GetGlyphName((byte)code));

        // Helvetica has no glyphs for the expert set: those widths are 0, recorded as information only.
        Assert.All(document.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity));
    }

    [Fact]
    public void The_example_of_annex_D_maps_codes_to_the_no_break_space_and_soft_hyphen_names_whose_widths_are_the_space_and_hyphen()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [ 160 /nonbreakingspace 173 /softhyphen ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal("nonbreakingspace", font.GetGlyphName(160));
        Assert.Equal("softhyphen", font.GetGlyphName(173));
        Assert.Equal(278, font.GetWidth(160));
        Assert.Equal(333, font.GetWidth(173));
        Assert.Equal("Euro", font.GetGlyphName(0x80));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_glyph_name_the_metrics_do_not_use_is_measured_through_its_Unicode_value()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [ 1 /uni00C4 /nbspace /afii10017 /u1F600 /Aogonek ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(667, font.GetWidth(1));
        Assert.Equal(278, font.GetWidth(2));
        Assert.Equal(0, font.GetWidth(3));
        Assert.Equal(0, font.GetWidth(4));
        Assert.Equal(667, font.GetWidth(5));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontGlyphMissing", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void Differences_take_codes_as_integers_or_whole_reals_and_later_runs_win()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [ 65 /X /Y 66.0 /Z 200 /Adieresis ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(["X", "Z", "C", "Adieresis"], new[] { font.GetGlyphName(65), font.GetGlyphName(66), font.GetGlyphName(67), font.GetGlyphName(200) });
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Malformed_differences_are_skipped_with_a_diagnostic()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [ /Orphan 65 (text) /B 300 /Far 254 /thorn /ydieresis /Beyond ] >> >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal("B", font.GetGlyphName(65));
        Assert.Equal("thorn", font.GetGlyphName(254));
        Assert.Equal("ydieresis", font.GetGlyphName(255));
        Assert.Equal(".notdef", font.GetGlyphName(0));
        Assert.Equal(["FontDifferencesInvalid"], FontPdf.Codes(document));
    }

    [Fact]
    public void An_encoding_dictionary_with_a_base_encoding_applies_its_differences_to_it()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding 5 0 R >>",
            "<< /Type /Encoding /BaseEncoding /MacRomanEncoding /Differences 6 0 R >>",
            "[ 128 /Euro ]");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal("Euro", font.GetGlyphName(0x80));
        Assert.Equal("Aring", font.GetGlyphName(0x81));
        Assert.Equal(556, font.GetWidth(0x80));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void StandardEncoding_by_name_is_used_with_a_diagnostic_since_it_is_not_a_predefined_name()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /StandardEncoding >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal("quoteright", font.GetGlyphName(0x27));
        Assert.Equal(["FontEncodingInvalid"], FontPdf.Codes(document));
    }

    [Theory]
    [InlineData("/Encoding /SymbolEncoding")]
    [InlineData("/Encoding 12")]
    [InlineData("/Encoding << /BaseEncoding (WinAnsiEncoding) >>")]
    [InlineData("/Encoding << /BaseEncoding /PDFDocEncoding >>")]
    public void An_unknown_or_malformed_encoding_falls_back_to_the_built_in_encoding_with_a_diagnostic(string encoding)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica {encoding} >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal("quoteright", font.GetGlyphName(0x27));
        Assert.Equal(["FontEncodingInvalid"], FontPdf.Codes(document));
    }

    [Fact]
    public void An_embedded_font_without_an_encoding_uses_StandardEncoding_until_its_program_is_read()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 32 /LastChar 32 /Widths [ 300 ] /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /Helvetica /Flags 32 /FontBBox [ 0 0 1000 1000 ] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile 6 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.True(font.IsEmbedded);
        Assert.Null(font.Standard14);
        Assert.Equal("quoteright", font.GetGlyphName(0x27));
        Assert.Equal(300, font.GetWidth(0x20));
        Assert.Equal(0, font.GetWidth(0x41));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontBuiltInEncodingUnavailable", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void A_type_3_font_has_only_the_glyph_names_its_encoding_dictionary_gives()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type3 /FontBBox [ 0 0 1 1 ] /FontMatrix [ 0.001 0 0 0.001 0 0 ] /CharProcs << >> /Encoding << /Type /Encoding /Differences [ 65 /square /circle ] >> /FirstChar 65 /LastChar 66 /Widths [ 1000 500 ] >>");
        PdfType3Font font = Assert.IsType<PdfType3Font>(FontPdf.Font(document));

        Assert.Equal(["square", "circle", ".notdef"], new[] { font.GetGlyphName(65), font.GetGlyphName(66), font.GetGlyphName(0x20) });
        Assert.Equal([1000.0, 500, 0], new[] { font.GetWidth(65), font.GetWidth(66), font.GetWidth(67) });
        Assert.Null(font.Standard14);
        Assert.Null(font.Descriptor);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_type_3_font_without_an_encoding_dictionary_has_no_glyph_names_and_a_diagnostic()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type3 /BaseFont /Helvetica /FontBBox [ 0 0 1 1 ] /FontMatrix [ 0.001 0 0 0.001 0 0 ] /CharProcs << >> /FirstChar 65 /LastChar 65 /Widths [ 1000 ] >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(".notdef", font.GetGlyphName(65));
        Assert.Null(font.Standard14);
        Assert.Equal(["FontEncodingInvalid"], FontPdf.Codes(document));
    }
}
