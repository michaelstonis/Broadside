using Broadside.Fonts;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The second method of ISO 32000-2 §9.10.2: a simple font's glyph names through the Adobe Glyph List specification (§2
/// algorithm, §3 examples), and what happens past the methods the specification lists: the program's Unicode "cmap" (beyond the
/// specification), and U+FFFD for names that mean nothing, such as a Type 3 font's own names (no pdf.js-style guessing).
/// </summary>
public class GlyphNameUnicodeTests
{
    /// <summary>Glyph name, expected text (empty: maps to nothing, so U+FFFD).</summary>
    public static TheoryData<string, string> SpecificationExamples => new()
    {
        { "Lcommaaccent", "Ļ" },
        { "uni20AC0308", "€̈" },
        { "u1040C", "\U0001040C" },
        { "uniD801DC0C", string.Empty },
        { "Lcommaaccent_uni20AC0308_u1040C.alternate", "Ļ€̈\U0001040C" },
        { ".notdef", string.Empty },
        { "foo", string.Empty },
        { "a.sc", "a" },
        { "f_f_i", "ffi" },
        { "dalethatafpatah", "דֲ" },
        { "uni0041.ss01", "A" },
        { "u0041", "A" },
        { "uD800", string.Empty },
        { "u110000", string.Empty },
        { "uni004", string.Empty },
        { "a1", string.Empty },
    };

    [Theory]
    [MemberData(nameof(SpecificationExamples))]
    public void Glyph_names_map_as_the_Adobe_Glyph_List_specification_examples_say(string name, string expected)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [65 /{name}] >> >>");

        (string text, UnicodeSource source) = ToUnicodeTests.Map(FontPdf.Font(document), 65, 1);

        Assert.Equal(expected.Length == 0 ? ("�", UnicodeSource.Unmapped) : (expected, UnicodeSource.GlyphName), (text, source));
    }

    [Fact]
    public void Lowercase_hexadecimal_digits_are_accepted_with_an_information_diagnostic()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [65 /uni20ac 66 /u1f600] >> >>");
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(("€", UnicodeSource.GlyphName), ToUnicodeTests.Map(font, 65, 1));
        Assert.Equal(("\U0001F600", UnicodeSource.GlyphName), ToUnicodeTests.Map(font, 66, 1));
        Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "GlyphNameNonStandard" && diagnostic.Severity == Diagnostics.DiagnosticSeverity.Information);
    }

    [Fact]
    public void The_Zapf_Dingbats_list_applies_only_to_ZapfDingbats()
    {
        using PdfDocument zapf = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>");
        using PdfDocument helvetica = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [33 /a1] >> >>");

        // ZapfDingbats's built-in encoding gives code 0x21 the name a1, which its list maps to U+2701.
        Assert.Equal(("✁", UnicodeSource.GlyphName), ToUnicodeTests.Map(FontPdf.Font(zapf), 0x21, 1));
        Assert.Equal(("�", UnicodeSource.Unmapped), ToUnicodeTests.Map(FontPdf.Font(helvetica), 0x21, 1));
    }

    [Fact]
    public void A_simple_font_code_given_as_more_than_one_byte_maps_to_nothing()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        Assert.Equal(("�", UnicodeSource.Unmapped), ToUnicodeTests.Map(FontPdf.Font(document), 0x41, 2));
    }

    [Fact]
    public void A_Type_3_font_without_ToUnicode_whose_names_mean_nothing_shows_U_FFFD_with_one_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-type3.pdf"));
        PdfFont font = Assert.IsType<PdfType3Font>(document.Pages[0].GetFont("T3a"));
        font.Dictionary.Remove(new CosName("ToUnicode"));

        // The glyph names are square, triangle and bitmap: none is in the Adobe Glyph List.
        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        Assert.Equal("���■▲●", recorder.Text);
        Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }

    [Fact]
    public void A_simple_TrueType_font_whose_names_mean_nothing_maps_through_its_program_s_Unicode_cmap()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-loca-long.pdf"));
        PdfFont font = Assert.Single(document.Pages).GetFont("F1")!;
        font.Dictionary[new CosName("Encoding")] = CosObject.Parse("<< /Differences [72 /g1 /g2] >>"u8);
        font.Descriptor!.Dictionary![new CosName("Flags")] = new CosInteger(4);

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        // Symbolic, so §9.6.5.4 selects glyphs by code; the program has only a (3,10) subtable, which maps 0x48 to glyph 1
        // and both 0x49 and U+1F600 to glyph 2 (the smallest code point wins).
        Assert.Equal([("H", UnicodeSource.FontProgram), ("I", UnicodeSource.FontProgram)], recorder.Glyphs.Select(glyph => (glyph.Text, glyph.Source)));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }

    [Fact]
    public void A_TrueType_CIDFont_without_ToUnicode_maps_its_glyphs_through_the_program_s_Unicode_cmap()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-identity-h.pdf"));
        PdfFont font = Assert.Single(document.Pages).GetFont("F1")!;
        font.Dictionary.Remove(new CosName("ToUnicode"));

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        // CIDs 1, 3, 2: glyphs 1 (H), 0 (CID 3 is past the program: nothing to map), 2 (I).
        Assert.Equal(
            [("H", UnicodeSource.FontProgram), ("�", UnicodeSource.Unmapped), ("I", UnicodeSource.FontProgram)],
            recorder.Glyphs.Select(glyph => (glyph.Text, glyph.Source)));
    }

    [Fact]
    public void A_default_glyph_event_has_no_text()
    {
        var glyph = default(Broadside.Content.GlyphEvent);

        Assert.True(glyph.Unicode.IsEmpty);
        Assert.Equal(UnicodeSource.Unmapped, glyph.UnicodeSource);
    }
}
