using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The font resolver extension point through the document API: a non-embedded font gets a program from the configured resolvers,
/// then the system fonts, then a Standard 14 stand-in, or records that none was found. ISO 32000-2 §9.6.2.2, §9.8.
/// </summary>
public sealed class FontResolverTests
{
    [Fact]
    public void Without_a_resolver_or_system_fonts_a_non_embedded_Helvetica_records_that_no_program_was_found()
    {
        var options = new PdfOptions().UseSystemFontResolver(null);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Null(font.Substitute);
        Assert.Equal(0, font.GetGlyphId((byte)'H'));
        Assert.Equal(722, font.GetWidth((byte)'H'));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontProgramNotFound", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("/Helvetica", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("UseStandard14Fonts", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_does_not_throw_when_no_program_is_found()
    {
        var options = new PdfOptions().UseStrict().UseSystemFontResolver(null);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Null(font.Substitute);
        Assert.Equal("FontProgramNotFound", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_registered_resolver_supplies_the_program_of_a_non_embedded_font_and_glyphs_are_selected_by_name()
    {
        var queries = new List<FontQuery>();
        byte[] program = OneGlyphProgram('H');
        var options = new PdfOptions()
            .UseSystemFontResolver(null)
            .UseFontResolver(new FakeResolver(query =>
            {
                queries.Add(query);
                return new FontResolution(program, "BroadsideTest", FontMatchKind.Exact);
            }));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal("BroadsideTest", substitute.Name);
        Assert.Equal("Helvetica", substitute.RequestedName);
        Assert.Equal(FontMatchKind.Exact, substitute.MatchKind);
        Assert.Null(font.Program);
        Assert.Equal(1, font.GetGlyphId((byte)'H'));
        Assert.Equal(0, font.GetGlyphId((byte)'e'));
        Assert.Equal(600, substitute.GetWidth(1));
        Assert.Equal(722, font.GetWidth((byte)'H'));
        Assert.Empty(document.Diagnostics);

        FontQuery asked = Assert.Single(queries);
        Assert.Equal("Helvetica", asked.Name);
        Assert.Equal(Standard14Font.Helvetica, asked.Standard14);
        Assert.Equal(PdfFontType.Type1, asked.FontType);
        Assert.False(asked.IsSerif);
        Assert.False(asked.IsBold);
    }

    [Fact]
    public void Resolvers_are_asked_in_registration_order_and_the_first_answer_wins()
    {
        var asked = new List<string>();
        byte[] first = OneGlyphProgram('H');
        byte[] second = OneGlyphProgram('e');
        var options = new PdfOptions()
            .UseSystemFontResolver(new FakeResolver(_ =>
            {
                asked.Add("system");
                return null;
            }))
            .UseFontResolver(new FakeResolver(_ =>
            {
                asked.Add("none");
                return null;
            }))
            .UseFontResolver(new FakeResolver(_ =>
            {
                asked.Add("first");
                return new FontResolution(first, "First", FontMatchKind.Exact);
            }))
            .UseFontResolver(new FakeResolver(_ =>
            {
                asked.Add("second");
                return new FontResolution(second, "Second", FontMatchKind.Exact);
            }));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Equal("First", font.Substitute?.Name);
        Assert.Equal(["none", "first"], asked);
    }

    [Fact]
    public void The_system_resolver_is_asked_after_every_registered_resolver()
    {
        var asked = new List<string>();
        var options = new PdfOptions()
            .UseSystemFontResolver(new FakeResolver(_ =>
            {
                asked.Add("system");
                return new FontResolution(OneGlyphProgram('H'), "SystemFont", FontMatchKind.Standard14);
            }))
            .UseFontResolver(new FakeResolver(_ =>
            {
                asked.Add("registered");
                return null;
            }));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Equal("SystemFont", font.Substitute?.Name);
        Assert.Equal(FontMatchKind.Standard14, font.Substitute?.MatchKind);
        Assert.Equal(["registered", "system"], asked);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_font_no_resolver_has_gets_the_most_similar_Standard_14_font_and_a_diagnostic_naming_both()
    {
        var queries = new List<FontQuery>();
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(query =>
        {
            queries.Add(query);
            return query.Standard14 is { } font ? new FontResolution(OneGlyphProgram('A'), "Stand-in-" + font, FontMatchKind.Standard14) : null;
        }));
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /Calibri,Bold /FirstChar 65 /LastChar 65 /Widths [600] /Encoding /WinAnsiEncoding /FontDescriptor 5 0 R >>",
            options,
            "<< /Type /FontDescriptor /FontName /Calibri,Bold /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 750 /Descent -250 /CapHeight 700 /StemV 80 >>");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.Font(document));

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal(FontMatchKind.Similar, substitute.MatchKind);
        Assert.Equal("Stand-in-HelveticaBold", substitute.Name);
        Assert.Equal(1, font.GetGlyphId((byte)'A'));
        Assert.Equal(["Calibri,Bold", "Helvetica-Bold"], queries.Select(query => query.Name));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontSubstituted", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("/Calibri,Bold", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Stand-in-HelveticaBold", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Helvetica-Bold", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/BroadsideMono", 1, 0, "Courier")]
    [InlineData("/BroadsideMono-Bold", 1, 0, "Courier-Bold")]
    [InlineData("/BroadsideSerif-Italic", 98, -12, "Times-Italic")]
    [InlineData("/BroadsideSerif", 34, 0, "Times-Roman")]
    [InlineData("/Garamond", 32, 0, "Times-Roman")]
    [InlineData("/Calibri", 32, 0, "Helvetica")]
    [InlineData("/Calibri-Light", 32, -10, "Helvetica-Oblique")]
    [InlineData("/BroadsideSans", 262176, 0, "Helvetica-Bold")]
    public void The_similar_Standard_14_font_follows_the_flags_the_italic_angle_and_the_name(string baseFont, int flags, int italicAngle, string expected)
    {
        var asked = new List<string>();
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(query =>
        {
            asked.Add(query.Name);
            return null;
        }));
        using PdfDocument document = FontPdf.Open(
            $"<< /Type /Font /Subtype /Type1 /BaseFont {baseFont} /FirstChar 65 /LastChar 65 /Widths [600] /FontDescriptor 5 0 R >>",
            options,
            $"<< /Type /FontDescriptor /FontName {baseFont} /Flags {flags} /FontBBox [0 0 1000 1000] /ItalicAngle {italicAngle} /Ascent 750 /Descent -250 /CapHeight 700 /StemV 80 >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Null(font.Substitute);
        Assert.Equal([baseFont[1..], expected], asked);
        Assert.Equal(["FontProgramNotFound"], FontPdf.Codes(document));
    }

    [Theory]
    [InlineData("/Wingdings-Regular", 4, null)]
    [InlineData("/MyDingbats", 4, "ZapfDingbats")]
    [InlineData("/SymbolMT", 4, "Symbol")]
    public void A_symbolic_font_is_never_given_a_text_font_in_its_place(string baseFont, int flags, string? expected)
    {
        var asked = new List<string>();
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(query =>
        {
            asked.Add(query.Name);
            return null;
        }));
        using PdfDocument document = FontPdf.Open(
            $"<< /Type /Font /Subtype /TrueType /BaseFont {baseFont} /FirstChar 65 /LastChar 65 /Widths [600] /FontDescriptor 5 0 R >>",
            options,
            $"<< /Type /FontDescriptor /FontName {baseFont} /Flags {flags} /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 750 /Descent -250 /CapHeight 700 /StemV 80 >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Null(font.Substitute);
        Assert.Equal(expected is null ? [baseFont[1..]] : [baseFont[1..], expected], asked);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("FontProgram", StringComparison.Ordinal) || diagnostic.Code == "FontSubstituted");
        Assert.Equal("FontProgramNotFound", diagnostic.Code);
    }

    [Fact]
    public void An_embedded_font_with_a_usable_program_has_no_substitute_and_asks_no_resolver()
    {
        bool asked = false;
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(_ =>
        {
            asked = true;
            return null;
        }));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-embedded.pdf"), options);
        PdfSimpleFont font = Assert.IsAssignableFrom<PdfSimpleFont>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.NotNull(font.Program);
        Assert.Null(font.Substitute);
        Assert.False(asked);
    }

    [Fact]
    public void A_program_no_parser_reads_records_a_diagnostic_and_gives_no_substitute()
    {
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(_ => new FontResolution("not a font"u8.ToArray(), "Junk", FontMatchKind.Exact)));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Null(font.Substitute);
        Assert.Contains("FontSubstituteUnreadable", document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_symbolic_substitute_reads_its_3_0_subtable_with_the_high_bytes_in_the_order_an_embedded_program_is_read()
    {
        // ISO 32000-2 §9.6.5.4: the code with the high byte 0x00, 0xF0, 0xF1 or 0xF2 selects the glyph in the (3, 0) subtable. This
        // program maps both 0x0041 (glyph 1) and 0xF041 (glyph 2): substitute and embedded program agree on glyph 1.
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 0, TrueTypeBuilder.Format4((0x41, 0x41, 1 - 0x41), (0xF041, 0xF041, 2 - 0xF041)))),
            Metrics = [(500, 0), (600, 0), (600, 0)],
        };
        builder.Glyphs.Add([]);
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 500, 700));
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 400, 700));
        byte[] program = builder.Build();
        var options = new PdfOptions().UseSystemFontResolver(null).UseFontResolver(new FakeResolver(_ => new FontResolution(program, "BroadsideSymbol", FontMatchKind.Exact)));
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /BroadsideSymbol /FirstChar 65 /LastChar 65 /Widths [600] /FontDescriptor 5 0 R >>",
            options,
            "<< /Type /FontDescriptor /FontName /BroadsideSymbol /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 750 /Descent -250 /CapHeight 700 /StemV 80 >>");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(FontPdf.SimpleFont(document));

        Assert.NotNull(font.Substitute);
        Assert.Equal(1, font.GetGlyphId(0x41));
    }

    /// <summary>A TrueType program whose glyph 1 is a 600-unit-wide square mapped from one character by its (3, 1) "cmap".</summary>
    internal static byte[] OneGlyphProgram(char character)
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((character, character, 1 - character)))),
            Metrics = [(500, 0), (600, 100)],
        };
        builder.Glyphs.Add([]);
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(100, 0, 500, 700));
        return builder.Build();
    }
}

/// <summary>A resolver answering through a delegate.</summary>
internal sealed class FakeResolver(Func<FontQuery, FontResolution?> resolve) : IFontResolver
{
    public FontResolution? ResolveFont(FontQuery query) => resolve(query);
}
