using System.Text;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The font resolver's second duty: named resources. A predefined CMap a Type 0 font names comes from the resolvers, with the
/// CMap its <c>usecmap</c> names, so the CMaps package plugs in without another extension point. ISO 32000-2 §9.7.5.2, Table 116.
/// </summary>
public class FontResolverResourceTests
{
    private const string Horizontal = """
        %!PS-Adobe-3.0 Resource-CMap
        /CIDInit /ProcSet findresource begin
        12 dict begin
        begincmap
        /CIDSystemInfo 3 dict dup begin /Registry (Adobe) def /Ordering (Japan1) def /Supplement 2 def end def
        /CMapName /Broadside-Test-H def
        /CMapType 1 def
        1 begincodespacerange <00> <80> endcodespacerange
        1 begincidrange <20> <7e> 231 endcidrange
        endcmap
        CMapName currentdict /CMap defineresource pop
        end
        end
        """;

    private const string Vertical = """
        %!PS-Adobe-3.0 Resource-CMap
        /CIDInit /ProcSet findresource begin
        12 dict begin
        begincmap
        /Broadside-Test-H usecmap
        /CIDSystemInfo 3 dict dup begin /Registry (Adobe) def /Ordering (Japan1) def /Supplement 2 def end def
        /CMapName /Broadside-Test-V def
        /CMapType 1 def
        /WMode 1 def
        1 begincidchar <42> 7887 endcidchar
        endcmap
        CMapName currentdict /CMap defineresource pop
        end
        end
        """;

    [Fact]
    public void A_predefined_CMap_and_the_CMap_it_uses_come_from_the_font_resolvers()
    {
        var asked = new List<(FontResourceKind, string)>();
        var resolver = new ResourceResolver(asked, new Dictionary<string, string> { ["Broadside-Test-H"] = Horizontal, ["Broadside-Test-V"] = Vertical });
        using PdfDocument document = OpenType0("/Broadside-Test-V", new PdfOptions().UseSystemFontResolver(null).UseFontResolver(resolver));
        PdfType0Font font = Assert.IsType<PdfType0Font>(FontPdf.Font(document));

        Assert.Equal("Broadside-Test-V", font.Encoding.Name);
        Assert.Equal(WritingMode.Vertical, font.WritingMode);
        Assert.Equal(231 + 0x21, font.ReadGlyph("A"u8).Cid);
        Assert.Equal(7887, font.ReadGlyph("B"u8).Cid);
        Assert.Equal([(FontResourceKind.CMap, "Broadside-Test-V"), (FontResourceKind.CMap, "Broadside-Test-H")], asked);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "CMapUnavailable");
    }

    [Fact]
    public void A_predefined_CMap_no_resolver_has_is_unavailable_and_codes_read_as_Identity_H()
    {
        using PdfDocument document = OpenType0("/Broadside-Test-V", new PdfOptions().UseSystemFontResolver(null));
        PdfType0Font font = Assert.IsType<PdfType0Font>(FontPdf.Font(document));

        Assert.True(font.Encoding.IsIdentity);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "CMapUnavailable" && diagnostic.Message.Contains("/Broadside-Test-V", StringComparison.Ordinal));
    }

    [Fact]
    public void A_predefined_CMap_is_parsed_once_per_document()
    {
        var asked = new List<(FontResourceKind, string)>();
        var resolver = new ResourceResolver(asked, new Dictionary<string, string> { ["Broadside-Test-H"] = Horizontal });
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Broadside-Test-H /DescendantFonts [5 0 R] >>",
            new PdfOptions().UseSystemFontResolver(null).UseFontResolver(resolver),
            CidFont,
            Descriptor,
            "<< /Type /Font /Subtype /Type0 /BaseFont /Test2 /Encoding /Broadside-Test-H /DescendantFonts [5 0 R] >>");
        var first = Assert.IsType<PdfType0Font>(FontPdf.Font(document));
        var second = Assert.IsType<PdfType0Font>(document.GetFont(document.Resolve(new Broadside.Objects.CosReference(7, 0))));

        Assert.Same(first.Encoding, second.Encoding);
        Assert.Single(asked);
    }

    private const string CidFont = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> /FontDescriptor 6 0 R /DW 1000 >>";

    private const string Descriptor = "<< /Type /FontDescriptor /FontName /Test /Flags 4 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 880 /Descent -120 /CapHeight 700 /StemV 80 >>";

    private static PdfDocument OpenType0(string encoding, PdfOptions options) =>
        FontPdf.Open($"<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding {encoding} /DescendantFonts [5 0 R] >>", options, CidFont, Descriptor);

    private sealed class ResourceResolver(List<(FontResourceKind, string)> asked, Dictionary<string, string> cmaps) : IFontResolver
    {
        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            asked.Add((kind, name));
            data = kind == FontResourceKind.CMap && cmaps.TryGetValue(name, out string? text) ? Encoding.ASCII.GetBytes(text) : default;
            return !data.IsEmpty;
        }
    }
}
