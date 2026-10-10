using System.Globalization;
using System.Security.Cryptography;
using Broadside.Fonts.Cff;
using Broadside.Fonts.TrueType;
using Broadside.Tests.Fonts;
using Broadside.TestSupport;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// The Standard 14 glyph package through the document API: one options call gives every non-embedded Standard 14 font the glyphs
/// of its Liberation or Foxit stand-in. ISO 32000-2 §9.6.2.2, §9.6.5.2, §9.6.5.4, §9.8; ADR 0007.
/// </summary>
public sealed class Standard14PackageTests
{
    public static TheoryData<Standard14Font, string, string> Programs => new()
    {
        { Standard14Font.Courier, "LiberationMono", "f2b83c763e8afd21709333370bed4774337fae82267937e2b5aea7e2fbd922c1" },
        { Standard14Font.CourierBold, "LiberationMono-Bold", "bd62a0672d0b9b6710b01df434c80ad54fa5f0835207eb7b17b7a761463067bb" },
        { Standard14Font.CourierOblique, "LiberationMono-Italic", "605c01c711b44480a7508d349dfbf3264e81fa43d69e61cfa7d10b86e764c4d1" },
        { Standard14Font.CourierBoldOblique, "LiberationMono-BoldItalic", "79451f3c09fe25116098853b7a2ca6e2436220ccc11af022979adbcf195be130" },
        { Standard14Font.Helvetica, "LiberationSans", "76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8" },
        { Standard14Font.HelveticaBold, "LiberationSans-Bold", "788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173" },
        { Standard14Font.HelveticaOblique, "LiberationSans-Italic", "e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0" },
        { Standard14Font.HelveticaBoldOblique, "LiberationSans-BoldItalic", "698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee" },
        { Standard14Font.TimesRoman, "LiberationSerif", "058ea80864aef09a23f45cbec2bb5400bc3dfbdea01c3f10538a21fcb497fb74" },
        { Standard14Font.TimesBold, "LiberationSerif-Bold", "d754ba427cfe0bca54ae052384baa8f842da5bd6550ad4da024ac441e7a7d5ce" },
        { Standard14Font.TimesItalic, "LiberationSerif-Italic", "0e3dea9f8d613e006ccfa62201f33e265d19167bd0907725c3e145368b04fc2e" },
        { Standard14Font.TimesBoldItalic, "LiberationSerif-BoldItalic", "f17db8af71e24d2066b587546021d4f0b296be389512b658dec3c09affeb11a7" },
        { Standard14Font.Symbol, "FoxitSymbol", "47967d055530e7357088a08403115425643ec2cdfd6201ba8af0fbd7116c1539" },
        { Standard14Font.ZapfDingbats, "FoxitDingbats", "845c752392b6c914fb989c75a08b7792b88f542d2499042ef2889f8c814a16ed" },
    };

    [Theory]
    [MemberData(nameof(Programs))]
    public void Each_Standard_14_font_is_served_by_its_unmodified_upstream_program(Standard14Font font, string name, string sha256)
    {
        var resolver = new Standard14FontResolver();

        FontResolution resolution = Assert.IsType<FontResolution>(resolver.ResolveFont(new FontQuery(Standard14Name(font)) { Standard14 = font }));

        Assert.Equal(name, resolution.Name);
        Assert.Equal(FontMatchKind.Standard14, resolution.MatchKind);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(resolution.Data.Span)));
        Assert.Equal(resolution.Data, Standard14FontResolver.GetProgram(font));
    }

    [Fact]
    public void The_package_answers_only_Standard_14_fonts_and_its_own_font_names()
    {
        var resolver = new Standard14FontResolver();

        Assert.Null(resolver.ResolveFont(new FontQuery("Calibri")));
        Assert.Equal(FontMatchKind.Exact, resolver.ResolveFont(new FontQuery("LiberationSerif-Bold"))?.MatchKind);
        Assert.Equal("LiberationSerif-Bold", resolver.ResolveFont(new FontQuery("ABCDEF+LiberationSerif-Bold"))?.Name);
        Assert.False(((IFontResolver)resolver).TryResolveResource(FontResourceKind.CMap, "90ms-RKSJ-H", out _));
    }

    [Fact]
    public void Text_standard14_pdf_has_no_glyphs_without_the_package_on_a_machine_without_a_matching_system_font()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), new PdfOptions().UseSystemFontResolver(new SystemFontResolver([])));
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Null(font.Substitute);
        Assert.All("Hello, Broadside"u8.ToArray(), code => Assert.Equal(0, font.GetGlyphId(code)));
        Assert.Equal(722, font.GetWidth((byte)'H'));
        Assert.Equal("FontProgramNotFound", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Text_standard14_pdf_has_Liberation_Sans_outlines_with_the_package()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));
        FontProgram liberation = new TrueTypeFontProgramParser().Parse(Standard14FontResolver.GetProgram(Standard14Font.Helvetica), new FontProgramContext())!;

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal("LiberationSans", substitute.Name);
        Assert.Equal(FontMatchKind.Standard14, substitute.MatchKind);
        foreach (byte code in "Hello, Broadside"u8)
        {
            int glyph = font.GetGlyphId(code);
            Assert.True(liberation.TryGetGlyphId(font.GetGlyphName(code), out int expected));
            Assert.Equal(expected, glyph);
            Assert.Equal(OutlineText.Of(liberation, expected), OutlineText.Of(substitute.Program, glyph));
            Assert.Equal(code == ' ', OutlineText.Of(substitute.Program, glyph) == "Empty");
        }

        Assert.Equal(722, font.GetWidth((byte)'H'));
        Assert.Equal(1479.0 / 2048 * 1000, substitute.GetWidth(font.GetGlyphId((byte)'H')), 6);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_resolver_registered_before_the_package_wins()
    {
        byte[] own = OneGlyphProgram('H');
        var options = new PdfOptions()
            .UseSystemFontResolver(null)
            .UseFontResolver(new OwnResolver(own))
            .UseStandard14Fonts();
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), options);
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        Assert.Equal("CompanyHelvetica", font.Substitute?.Name);
        Assert.Equal(FontMatchKind.Exact, font.Substitute?.MatchKind);
        Assert.Equal(1, font.GetGlyphId((byte)'H'));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("text-standard14-alias.pdf", "F1", "LiberationSans-Bold", "Hi!")]
    [InlineData("text-standard14-macroman.pdf", "F1", "LiberationSerif", "\u0080¥")]
    [InlineData("text-standard14-widths.pdf", "F1", "LiberationMono", "ABC")]
    [InlineData("text-standard14-symbol.pdf", "F1", "FoxitSymbol", "abg ")]
    [InlineData("text-standard14-symbol.pdf", "F2", "FoxitSymbol", "abg ")]
    [InlineData("text-standard14-symbol-differences.pdf", "F1", "FoxitSymbol", "aB")]
    [InlineData("text-standard14-zapfdingbats.pdf", "F1", "FoxitDingbats", "!l\u0080\u008d")]
    public void Every_shown_code_of_the_Standard_14_corpus_files_has_an_outline(string file, string resource, string expected, string codes)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file), new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
        PdfSimpleFont font = Assert.IsAssignableFrom<PdfSimpleFont>(Assert.Single(document.Pages).GetFont(resource));

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal(expected, substitute.Name);
        foreach (char code in codes)
        {
            int glyph = GlyphId(font, (byte)code);
            Assert.True(glyph > 0, $"code 0x{(int)code:X2} ({font.GetGlyphName((byte)code)}) has no glyph");
            Assert.StartsWith("M ", OutlineText.Of(substitute.Program, glyph), StringComparison.Ordinal);
        }

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is "FontProgramNotFound" or "FontSubstituted");
    }

    [Theory]
    [InlineData("Symbol", Standard14Font.Symbol, "ChromSymbolOTF", 191)]
    [InlineData("ZapfDingbats", Standard14Font.ZapfDingbats, "ChromDingbatsOTF", 203)]
    public void The_Foxit_programs_have_every_glyph_of_the_AFM_file_with_its_width(string afmName, Standard14Font font, string programName, int glyphCount)
    {
        FontProgram program = new CffFontProgramParser().Parse(Standard14FontResolver.GetProgram(font), new FontProgramContext())!;
        List<(string Name, double Width)> glyphs = Afm(afmName);

        Assert.Equal(programName, program.PostScriptName);
        Assert.Equal(glyphCount, program.GlyphCount);
        Assert.Equal(glyphCount - 1, glyphs.Count);
        foreach ((string name, double width) in glyphs)
        {
            Assert.True(program.TryGetGlyphId(name, out int glyph), name);
            Assert.Equal(width, program.GetMetrics(glyph).AdvanceWidth * program.FontMatrix.A * 1000, 3);
        }
    }

    [Theory]
    [InlineData("Courier")]
    [InlineData("Courier-Bold")]
    [InlineData("Courier-Oblique")]
    [InlineData("Courier-BoldOblique")]
    [InlineData("Helvetica")]
    [InlineData("Helvetica-Bold")]
    [InlineData("Helvetica-Oblique")]
    [InlineData("Helvetica-BoldOblique")]
    [InlineData("Times-Roman")]
    [InlineData("Times-Bold")]
    [InlineData("Times-Italic")]
    [InlineData("Times-BoldItalic")]
    public void Every_glyph_of_the_Latin_AFM_files_but_commaaccent_has_a_Liberation_glyph(string fontName)
    {
        string[] names = [.. Afm(fontName).Select(glyph => glyph.Name).Where(name => name != "commaaccent")];
        foreach (string[] chunk in names.Chunk(255))
        {
            string differences = string.Join(' ', chunk.Select(name => "/" + name));
            using PdfDocument document = FontPdf.Open(
                $"<< /Type /Font /Subtype /Type1 /BaseFont /{fontName} /Encoding << /Differences [1 {differences}] >> >>",
                new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
            PdfType1Font font = Assert.IsType<PdfType1Font>(FontPdf.Font(document));
            for (int index = 0; index < chunk.Length; index++)
            {
                Assert.True(font.GetGlyphId((byte)(index + 1)) > 0, $"{fontName} /{chunk[index]}");
            }
        }
    }

    [Fact]
    public void A_non_embedded_font_of_another_name_gets_the_package_font_its_descriptor_describes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-nonembedded-substitute.pdf"), new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
        PdfType1Font font = Assert.IsType<PdfType1Font>(Assert.Single(document.Pages).GetFont("F1"));

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal("LiberationSerif-Italic", substitute.Name);
        Assert.Equal(FontMatchKind.Similar, substitute.MatchKind);
        Assert.Equal("BroadsideSerif-Italic", substitute.RequestedName);
        Assert.True(font.GetGlyphId((byte)'S') > 0);
        Assert.Equal(611, font.GetWidth((byte)'S'));
        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontSubstituted", diagnostic.Code);
        Assert.Contains("/BroadsideSerif-Italic", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("LiberationSerif-Italic", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Times-Italic", diagnostic.Message, StringComparison.Ordinal);
    }

    private static int GlyphId(PdfSimpleFont font, byte code) => font switch
    {
        PdfType1Font type1 => type1.GetGlyphId(code),
        PdfTrueTypeFont trueType => trueType.GetGlyphId(code),
        _ => 0,
    };

    private static string Standard14Name(Standard14Font font) => font switch
    {
        Standard14Font.Courier => "Courier",
        Standard14Font.CourierBold => "Courier-Bold",
        Standard14Font.CourierOblique => "Courier-Oblique",
        Standard14Font.CourierBoldOblique => "Courier-BoldOblique",
        Standard14Font.Helvetica => "Helvetica",
        Standard14Font.HelveticaBold => "Helvetica-Bold",
        Standard14Font.HelveticaOblique => "Helvetica-Oblique",
        Standard14Font.HelveticaBoldOblique => "Helvetica-BoldOblique",
        Standard14Font.TimesRoman => "Times-Roman",
        Standard14Font.TimesBold => "Times-Bold",
        Standard14Font.TimesItalic => "Times-Italic",
        Standard14Font.TimesBoldItalic => "Times-BoldItalic",
        Standard14Font.Symbol => "Symbol",
        _ => "ZapfDingbats",
    };

    /// <summary>The glyph names and widths of an Adobe Core 14 AFM file (TN 5004 §8: the <c>N</c> and <c>WX</c> of each metrics line).</summary>
    private static List<(string Name, double Width)> Afm(string fontName)
    {
        string path = Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside", "Fonts", "Data", "Afm", fontName + ".afm");
        var glyphs = new List<(string, double)>();
        bool inMetrics = false;
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("StartCharMetrics", StringComparison.Ordinal))
            {
                inMetrics = true;
            }
            else if (line.StartsWith("EndCharMetrics", StringComparison.Ordinal))
            {
                break;
            }
            else if (inMetrics && !string.IsNullOrWhiteSpace(line))
            {
                string? name = null;
                double width = 0;
                foreach (string part in line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens[0] == "N")
                    {
                        name = tokens[1];
                    }
                    else if (tokens[0] == "WX")
                    {
                        width = double.Parse(tokens[1], CultureInfo.InvariantCulture);
                    }
                }

                glyphs.Add((name!, width));
            }
        }

        return glyphs;
    }

    private static byte[] OneGlyphProgram(char character)
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((character, character, 1 - character)))),
        };
        builder.Glyphs.Add([]);
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(100, 0, 500, 700));
        return builder.Build();
    }

    private sealed class OwnResolver(byte[] program) : IFontResolver
    {
        public FontResolution? ResolveFont(FontQuery query) =>
            query.Standard14 == Standard14Font.Helvetica ? new FontResolution(program, "CompanyHelvetica", FontMatchKind.Exact) : null;
    }
}
