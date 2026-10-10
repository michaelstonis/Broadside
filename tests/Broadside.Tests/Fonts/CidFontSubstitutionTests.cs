using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// CIDFonts that are not embedded go through the font resolver chain like simple fonts: a CID selects the substitute's glyph through
/// the character collection's CID-to-Unicode table and the substitute's Unicode "cmap". ISO 32000-2 §9.7.4, §9.10.2; ADR 0009.
/// </summary>
public sealed class CidFontSubstitutionTests
{
    private const string Type0 = "<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideMincho /Encoding /Identity-H /DescendantFonts [5 0 R] >>";
    private const string Descriptor = "<< /Type /FontDescriptor /FontName /BroadsideMincho /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 880 /Descent -120 /CapHeight 700 /StemV 80 >>";

    // Adobe-Japan1: CID 34 is A, CID 3284 is 日 (U+65E5).
    private const string Ucs2 = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Adobe-Japan1-UCS2 def /CMapType 2 def\n"
        + "1 begincodespacerange <0000> <FFFF> endcodespacerange\n2 beginbfchar <0022> <0041> <0CD4> <65E5> endbfchar\n"
        + "endcmap CMapName currentdict /CMap defineresource pop end end";

    private static string CidFont(string subtype) =>
        $"<< /Type /Font /Subtype {subtype} /BaseFont /BroadsideMincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> /FontDescriptor 6 0 R >>";

    [Theory]
    [InlineData("/CIDFontType2")]
    [InlineData("/CIDFontType0")]
    public void A_non_embedded_CIDFont_draws_with_the_resolvers_program_through_its_collections_Unicode_values(string subtype)
    {
        var resolver = new Resolver(SubstituteProgram(), Ucs2);
        using PdfDocument document = FontPdf.Open(Type0, new PdfOptions().UseSystemFontResolver(null).UseFontResolver(resolver), CidFont(subtype), Descriptor);
        PdfType0Font font = Assert.IsType<PdfType0Font>(FontPdf.Font(document));
        PdfCidFont cidFont = font.DescendantFont!;

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(cidFont.Substitute);
        Assert.Null(cidFont.Program);
        Assert.Equal(("BroadsideMincho", "BroadsideMincho", FontMatchKind.Exact), (substitute.Name, substitute.RequestedName, substitute.MatchKind));
        Assert.Equal([1, 2, 0], new[] { 3284, 34, 5 }.Select(cidFont.GetGlyphId));
        Assert.Equal(1, font.ReadGlyph([0x0C, 0xD4]).GlyphId);
        Assert.Equal("BroadsideMincho", Assert.Single(resolver.Queries).Name);
        Assert.Equal(new CidSystemInfo("Adobe", "Japan1", 2), resolver.Queries[0].SystemInfo);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("FontProgram", StringComparison.Ordinal));
    }

    [Fact]
    public void A_non_embedded_CIDFont_no_resolver_has_records_an_information_diagnostic_and_has_no_glyphs()
    {
        // A predefined CMap (not Identity-H, which a non-embedded TrueType CIDFont shall not use): strict mode does not throw.
        using PdfDocument document = FontPdf.Open(
            Type0.Replace("/Identity-H", "/UniJIS-UCS2-H", StringComparison.Ordinal),
            new PdfOptions().UseStrict().UseSystemFontResolver(null),
            CidFont("/CIDFontType2"),
            Descriptor);
        PdfCidFont cidFont = Assert.IsType<PdfType0Font>(FontPdf.Font(document)).DescendantFont!;

        Assert.Null(cidFont.Substitute);
        Assert.Equal(0, cidFont.GetGlyphId(3284));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "FontProgramNotFound");
        Assert.Equal((DiagnosticSeverity.Information, 5), (diagnostic.Severity, diagnostic.ObjectReference?.ObjectNumber));
    }

    [Fact]
    public void An_exact_substitute_of_an_Identity_ordered_TrueType_CIDFont_takes_CIDs_as_glyph_ids()
    {
        // Adobe-Identity has no Unicode table: the CIDs of an Identity CIDFontType2 are the glyph ids of the font it was made from.
        var resolver = new Resolver(SubstituteProgram(), ucs2: null);
        using PdfDocument document = FontPdf.Open(
            Type0,
            new PdfOptions().UseSystemFontResolver(null).UseFontResolver(resolver),
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /BroadsideMincho /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 6 0 R >>",
            Descriptor);
        PdfCidFont cidFont = Assert.IsType<PdfType0Font>(FontPdf.Font(document)).DescendantFont!;

        Assert.Equal([2, 0], new[] { 2, 3 }.Select(cidFont.GetGlyphId));
    }

    /// <summary>A TrueType program: glyph 1 mapped from U+65E5, glyph 2 from U+0041.</summary>
    private static byte[] SubstituteProgram()
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((0x41, 0x41, 2 - 0x41), (0x65E5, 0x65E5, 1 - 0x65E5)))),
            Metrics = [(500, 0), (1000, 0), (600, 0)],
        };
        builder.Glyphs.Add([]);
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 900, 900));
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 500, 700));
        return builder.Build();
    }

    private sealed class Resolver(byte[] program, string? ucs2) : IFontResolver
    {
        public List<FontQuery> Queries { get; } = [];

        public FontResolution? ResolveFont(FontQuery query)
        {
            Queries.Add(query);
            return query.Name == "BroadsideMincho" ? new FontResolution(program, "BroadsideMincho", FontMatchKind.Exact) : null;
        }

        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            data = ucs2 is not null && kind == FontResourceKind.CidToUnicode && name == "Adobe-Japan1-UCS2" ? Encoding.ASCII.GetBytes(ucs2) : default;
            return !data.IsEmpty;
        }
    }
}
