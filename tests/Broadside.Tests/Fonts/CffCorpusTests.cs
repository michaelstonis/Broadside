using Broadside.Fonts;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Glyph outlines and glyph selection of the embedded CFF corpus files, through the public document API. Expected outlines are
/// the coordinates <c>minimal_cff()</c> in <c>tests/Corpus/generate.py</c> documents (cross-checked with fontTools).
/// ISO 32000-2 §9.6.5.2, §9.9.
/// </summary>
public sealed class CffCorpusTests
{
    private const string H = "M 100,0 L 100,700 L 300,700 L 300,400 L 500,400 L 500,700 L 700,700 L 700,0 L 500,0 L 500,300 L 300,300 L 300,0 Z";
    private const string I = "M 100,0 L 100,700 L 300,700 L 300,0 Z M 50,0 L 350,0 L 350,50 L 50,50 Z";
    private const string O = "M 400,0 C 550,0 650,200 650,400 C 650,600 550,800 400,800 C 250,800 150,600 150,400 C 150,200 250,0 400,0 Z";
    private const string A = "M 0,0 L 300,700 L 600,0 Z";
    private const string Acute = "M 0,0 L 100,100 L 150,50 Z";
    private const string Aacute = "M 0,0 L 300,700 L 600,0 Z M 150,750 L 250,850 L 300,800 Z";
    private const string S = "M 0,300 C 100,350 200,400 300,400 C 400,400 500,350 600,300 L 600,0 L 0,0 Z";

    [Fact]
    public void Every_code_of_the_embedded_CFF_page_selects_its_glyph_through_the_built_in_encoding_and_has_its_cubic_outline()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cff-embedded.pdf"));
        PdfType1Font font = Font(document);
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(font.Program);

        Assert.Equal(FontProgramFormat.Cff, program.Format);
        Assert.Equal(8, program.GlyphCount);
        Assert.Equal("BroadsideCff", program.PostScriptName);
        Assert.Equal(0.001, program.FontMatrix.A, 9);
        Assert.Equal([H, I, O, Aacute, A, S], "HIOÁaS".Select(code => OutlineText.Of(program, font.GetGlyphId((byte)code))));
        Assert.Equal([1, 2, 3, 6, 4, 7], "HIOÁaS".Select(code => font.GetGlyphId((byte)code)));
        Assert.Equal(["H", "I", "O", "Aacute", "A", "S"], "HIOÁaS".Select(code => font.GetGlyphName((byte)code)));
        Assert.Equal(Acute, OutlineText.Of(program, font.GetGlyphId(0xC2)));
        Assert.Equal(0, font.GetGlyphId(0x42));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Program_advances_follow_the_charstring_width_rule_and_match_the_font_widths()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cff-embedded.pdf"));
        PdfType1Font font = Font(document);
        FontProgram program = font.Program!;

        foreach (byte code in "HIOÁaSÂ"u8.ToArray().Length == 0 ? [] : new byte[] { 0x48, 0x49, 0x4F, 0xC1, 0x61, 0x53, 0xC2 })
        {
            Assert.Equal(font.GetWidth(code), program.GetMetrics(font.GetGlyphId(code)).AdvanceWidth);
        }

        Assert.Equal([800, 600, 700, 200], new[] { 1, 2, 3, 5 }.Select(glyph => program.GetMetrics(glyph).AdvanceWidth));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_OpenType_CFF_program_selects_glyphs_by_name_through_the_CFF_charset_not_its_cmap()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-opentype-cff-embedded.pdf"));
        PdfType1Font font = Font(document);
        FontProgram program = Assert.IsAssignableFrom<FontProgram>(font.Program);

        Assert.Equal(FontProgramFormat.OpenType, program.Format);
        Assert.Equal("BroadsideCff", program.PostScriptName);
        Assert.Single(program.CharacterMaps);
        Assert.Equal([1, 2, 3, 6, 4, 7, 5], new byte[] { 0x48, 0x49, 0x4F, 0xC1, 0x41, 0x53, 0xB4 }.Select(font.GetGlyphId));
        Assert.Equal([H, I, O, Aacute, A, S, Acute], new byte[] { 0x48, 0x49, 0x4F, 0xC1, 0x41, 0x53, 0xB4 }.Select(code => OutlineText.Of(program, font.GetGlyphId(code))));
        Assert.Equal(0, font.GetGlyphId(0x61));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Differences_over_an_embedded_CFF_font_take_the_program_built_in_encoding_as_their_base()
    {
        // ISO 32000-2 Table 112: without BaseEncoding, an embedded font's base encoding is its built-in encoding.
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cff-embedded.pdf"));
        PdfType1Font font = Font(document);
        font.Dictionary[new CosName("Encoding")] = new CosDictionary { [new CosName("Differences")] = new CosArray([new CosInteger(72), new CosName("O")]) };

        Assert.Equal(["O", "I", "Aacute", "A"], new byte[] { 0x48, 0x49, 0xC1, 0x61 }.Select(font.GetGlyphName));
        Assert.Equal([3, 2, 6, 4], new byte[] { 0x48, 0x49, 0xC1, 0x61 }.Select(font.GetGlyphId));
    }

    [Fact]
    public void A_multiple_master_instance_with_a_CFF_program_selects_its_glyphs_by_name()
    {
        // ISO 32000-2 §9.6.2.3: an MMType1 instance's embedded program is a snapshot, here a FontFile3 Type1C program.
        byte[] program = new CffBuilder
        {
            Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2(0, 0, "rmoveto", 300, 700, 300, -700, "rlineto", "endchar")) },
        }.Build();
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /MMType1 /BaseFont /Test_400_wt /FirstChar 65 /LastChar 65 /Widths [600] /Encoding /WinAnsiEncoding /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /Test_400_wt /Flags 32 /FontBBox [0 0 600 700] /ItalicAngle 0 /Ascent 700 /Descent 0 /StemV 80 /FontFile3 6 0 R >>",
            $"<< /Length {program.Length} /Subtype /Type1C >>\nstream\n{System.Text.Encoding.Latin1.GetString(program)}\nendstream");
        PdfType1Font font = Assert.IsType<PdfType1Font>(FontPdf.Font(document));

        Assert.True(font.IsMultipleMaster);
        Assert.Equal(FontProgramFormat.Cff, font.Program!.Format);
        Assert.Equal(A, OutlineText.Of(font.Program, font.GetGlyphId(0x41)));
        Assert.Empty(document.Diagnostics);
    }

    private static PdfType1Font Font(PdfDocument document) => Assert.IsType<PdfType1Font>(document.Pages[0].GetFont("F1"));
}
