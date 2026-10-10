using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Character code to glyph id for simple TrueType fonts (ISO 32000-2 §9.6.5.4) in the cases the corpus files do not cover, and the
/// font program as a document-level object: parsed from the font file stream, diagnostics on that stream, reparsed after a change.
/// </summary>
public class TrueTypeGlyphSelectionTests
{
    private static readonly byte[] None = [];

    [Fact]
    public void A_symbolic_font_whose_program_has_no_cmap_uses_the_code_as_the_glyph_id()
    {
        var builder = new TrueTypeBuilder { Glyphs = { None, Rectangle(1), Rectangle(2), Rectangle(3) } };
        builder.Overrides["cmap"] = null;
        using PdfDocument document = Open(builder.Build(), flags: 4, encoding: null);
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        Assert.Equal([1, 2, 3, 0], [font.GetGlyphId(1), font.GetGlyphId(2), font.GetGlyphId(3), font.GetGlyphId(4)]);
        Assert.Equal(
            [("FontBuiltInEncodingUnavailable", new CosReference(4, 0)), ("FontGlyphMappingFallback", new CosReference(4, 0)), ("FontTableInvalid", new CosReference(6, 0))],
            document.Diagnostics.Select(d => (d.Code, d.ObjectReference)).Order());
    }

    [Fact]
    public void A_symbolic_font_with_WinAnsiEncoding_and_only_a_unicode_cmap_falls_back_to_glyph_names()
    {
        var builder = new TrueTypeBuilder
        {
            Glyphs = { None, Rectangle(1), Rectangle(2) },
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x41, 0x41, 2 - 0x41), (0x20AC, 0x20AC, 1 - 0x20AC)))),
        };
        using PdfDocument document = Open(builder.Build(), flags: 4, encoding: "/WinAnsiEncoding");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        Assert.Equal([1, 2], [font.GetGlyphId(0x80), font.GetGlyphId(0x41)]);
        Assert.All(document.Diagnostics, d => Assert.Equal(DiagnosticSeverity.Information, d.Severity));
    }

    [Fact]
    public void Both_flags_with_a_Differences_array_select_by_name_as_for_a_nonsymbolic_font()
    {
        var builder = new TrueTypeBuilder
        {
            Glyphs = { None, Rectangle(1), Rectangle(2) },
            Cmap = TrueTypeBuilder.CmapTable((3, 0, TrueTypeBuilder.Format4((0xF041, 0xF041, 1 - 0xF041))), (3, 1, TrueTypeBuilder.Format4((0x42, 0x42, 2 - 0x42)))),
        };
        using PdfDocument document = Open(builder.Build(), flags: 4 | 32, encoding: "<< /Differences [65 /B] >>");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        Assert.Equal(2, font.GetGlyphId(0x41));
        Assert.Contains(document.Diagnostics, d => d.Code == "FontGlyphMappingFallback");
    }

    [Fact]
    public void A_nonsymbolic_font_without_an_encoding_reads_names_from_StandardEncoding()
    {
        var builder = new TrueTypeBuilder
        {
            Glyphs = { None, Rectangle(1) },
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x2019, 0x2019, 1 - 0x2019)))),
        };
        using PdfDocument document = Open(builder.Build(), flags: 32, encoding: null);
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        // StandardEncoding 0x27 is quoteright (U+2019); WinAnsiEncoding would give quotesingle.
        Assert.Equal(1, font.GetGlyphId(0x27));
    }

    [Fact]
    public void A_font_without_a_program_selects_no_glyph()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /TrueType /BaseFont /Arial >>");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        Assert.Null(font.Program);
        Assert.Equal(0, font.GetGlyphId(0x41));
    }

    [Fact]
    public void Diagnostics_of_the_program_are_recorded_against_the_font_file_stream()
    {
        byte[] program = new TrueTypeBuilder { Glyphs = { None, Rectangle(1) } }.Build();
        using PdfDocument document = Open(program, flags: 32, encoding: "/WinAnsiEncoding", length1: program.Length + 10);
        FontProgram parsed = FontPdf.Font(document).Program!;

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("FontProgramTruncated", new CosReference(6, 0)), (diagnostic.Code, diagnostic.ObjectReference));
        Assert.Equal(2, parsed.GlyphCount);
    }

    [Fact]
    public void Strict_mode_throws_from_the_call_that_meets_a_damaged_glyph()
    {
        byte[] program = new TrueTypeBuilder { Glyphs = { None, TrueTypeBuilder.Rectangle(0, 0, 10, 10)[..^3] } }.Build();
        using PdfDocument document = Open(program, flags: 32, encoding: "/WinAnsiEncoding", options: new PdfOptions().UseStrict());
        FontProgram parsed = FontPdf.Font(document).Program!;

        Assert.Equal("FontGlyphInvalid", Assert.Throws<DiagnosticException>(() => parsed.GetOutline(1, new GlyphOutline())).Diagnostic.Code);
    }

    [Fact]
    public void A_CFF_OpenType_program_is_read_by_the_CFF_parser_and_its_cmap_selects_the_glyphs()
    {
        var cff = new CffBuilder { Glyphs = { (".notdef", CffBuilder.T2("endchar")), ("A", CffBuilder.T2(0, 0, "rmoveto", 10, 0, "rlineto", "endchar")) } };
        byte[] cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x41, 0x41, 1 - 0x41))));
        byte[] program = CffBuilder.OpenType(cff.Build(), new Dictionary<string, byte[]> { ["cmap"] = cmap });
        using PdfDocument document = Open(program, flags: 32, encoding: "/WinAnsiEncoding", key: "FontFile3", subtype: "/OpenType");
        var font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        Assert.Equal(FontProgramFormat.OpenType, font.Program!.Format);
        Assert.Equal(1, font.GetGlyphId(0x41));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_TrueType_program_in_a_FontFile3_OpenType_stream_is_read()
    {
        byte[] program = new TrueTypeBuilder { Glyphs = { None, Rectangle(1) }, Version = 0x4F54544F }.Build();
        using PdfDocument document = Open(program, flags: 32, encoding: "/WinAnsiEncoding", key: "FontFile3", subtype: "/OpenType");

        Assert.Equal(FontProgramFormat.OpenType, FontPdf.Font(document).Program!.Format);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Data_of_another_format_than_declared_is_read_by_its_parser_with_a_diagnostic()
    {
        byte[] program = new TrueTypeBuilder { Glyphs = { None, Rectangle(1) } }.Build();
        using PdfDocument document = Open(program, flags: 32, encoding: "/WinAnsiEncoding", key: "FontFile3", subtype: "/Type1C");

        Assert.NotNull(FontPdf.Font(document).Program);
        Assert.Equal("FontProgramFormatMismatch", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_changed_font_file_stream_is_parsed_again()
    {
        byte[] first = new TrueTypeBuilder { Glyphs = { None, Rectangle(1) } }.Build();
        byte[] second = new TrueTypeBuilder { Glyphs = { None, Rectangle(1), Rectangle(2) } }.Build();
        using PdfDocument document = Open(first, flags: 32, encoding: "/WinAnsiEncoding");
        PdfFont font = FontPdf.Font(document);
        FontProgram before = font.Program!;

        font.Descriptor!.FontFile2!.EncodedData = second;

        Assert.Equal(2, before.GlyphCount);
        Assert.Equal(3, font.Program!.GlyphCount);
    }

    private static byte[] Rectangle(int size) => TrueTypeBuilder.Rectangle(0, 0, 100 * size, 100);

    private static PdfDocument Open(
        byte[] program,
        int flags,
        string? encoding,
        long? length1 = null,
        string key = "FontFile2",
        string? subtype = null,
        PdfOptions? options = null)
    {
        string encodingEntry = encoding is null ? string.Empty : $" /Encoding {encoding}";
        string subtypeEntry = subtype is null ? string.Empty : $" /Subtype {subtype}";
        return FontPdf.Open(
            $"<< /Type /Font /Subtype /TrueType /BaseFont /Test /FirstChar 0 /LastChar 0 /Widths [500]{encodingEntry} /FontDescriptor 5 0 R >>",
            options ?? new PdfOptions(),
            $"<< /Type /FontDescriptor /FontName /Test /Flags {flags} /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /StemV 80 /{key} 6 0 R >>",
            $"<< /Length {program.Length} /Length1 {length1 ?? program.Length}{subtypeEntry} >>\nstream\n{Encoding.Latin1.GetString(program)}\nendstream");
    }
}
