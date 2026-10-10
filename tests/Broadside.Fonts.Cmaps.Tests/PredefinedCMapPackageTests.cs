using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broadside.Tests.Fonts;

namespace Broadside.Fonts.Cmaps.Tests;

/// <summary>
/// The predefined CMaps package through the document API: one options call gives every Type 0 font that names a Table 116 CMap
/// that CMap's mappings, read by the core's CMap parser. ISO 32000-2 §9.7.5.2 (Table 116), §9.7.6.2-§9.7.6.3; Adobe TN 5014, 5099.
/// </summary>
public class PredefinedCMapPackageTests
{
    /// <summary>ISO 32000-2:2020 Table 116, in table order.</summary>
    private static readonly string[] Table116 =
    [
        "GB-EUC-H", "GB-EUC-V", "GBpc-EUC-H", "GBpc-EUC-V", "GBK-EUC-H", "GBK-EUC-V", "GBKp-EUC-H", "GBKp-EUC-V", "GBK2K-H", "GBK2K-V",
        "UniGB-UCS2-H", "UniGB-UCS2-V", "UniGB-UTF16-H", "UniGB-UTF16-V",
        "B5pc-H", "B5pc-V", "HKscs-B5-H", "HKscs-B5-V", "ETen-B5-H", "ETen-B5-V", "ETenms-B5-H", "ETenms-B5-V", "CNS-EUC-H", "CNS-EUC-V",
        "UniCNS-UCS2-H", "UniCNS-UCS2-V", "UniCNS-UTF16-H", "UniCNS-UTF16-V",
        "83pv-RKSJ-H", "90ms-RKSJ-H", "90ms-RKSJ-V", "90msp-RKSJ-H", "90msp-RKSJ-V", "90pv-RKSJ-H", "Add-RKSJ-H", "Add-RKSJ-V", "EUC-H",
        "EUC-V", "Ext-RKSJ-H", "Ext-RKSJ-V", "H", "V", "UniJIS-UCS2-H", "UniJIS-UCS2-V", "UniJIS-UCS2-HW-H", "UniJIS-UCS2-HW-V",
        "UniJIS-UTF16-H", "UniJIS-UTF16-V",
        "KSC-EUC-H", "KSC-EUC-V", "KSCms-UHC-H", "KSCms-UHC-V", "KSCms-UHC-HW-H", "KSCms-UHC-HW-V", "KSCpc-EUC-H", "UniKS-UCS2-H",
        "UniKS-UCS2-V", "UniKS-UTF16-H", "UniKS-UTF16-V",
        "Identity-H", "Identity-V",
    ];

    public static TheoryData<string> Table116Names => [.. Table116];

    public static TheoryData<string> PackagedFiles => [.. PackageManifest.All.Select(row => $"{row.Kind}/{row.Name}")];

    [Fact]
    public void Table_116_has_61_names_and_the_package_has_each_one_but_the_two_built_into_the_core()
    {
        Assert.Equal(61, Table116.Distinct().Count());
        Assert.Equal(Table116[..^2], PackageManifest.All.Where(row => row.Kind == FontResourceKind.CMap).Select(row => row.Name));
    }

    [Theory]
    [MemberData(nameof(Table116Names))]
    public void Every_Table_116_CMap_resolves_with_its_character_collection_writing_mode_and_mappings(string name)
    {
        bool builtIn = name.StartsWith("Identity", StringComparison.Ordinal);
        PackageManifest? row = builtIn ? null : PackageManifest.CMap(name);
        string ordering = row?.Ordering ?? "Identity";
        using PdfDocument document = Open("/" + name, ordering, row?.Supplement ?? 0);
        PdfType0Font font = Font(document);

        CMap cmap = font.Encoding;

        Assert.Equal(name, cmap.Name);
        Assert.Equal(builtIn, cmap.IsIdentity);
        Assert.Equal(("Adobe", ordering, row?.Supplement ?? 0), (cmap.SystemInfo?.Registry, cmap.SystemInfo?.Ordering, cmap.SystemInfo?.Supplement));
        Assert.Equal(name.EndsWith('V') ? WritingMode.Vertical : WritingMode.Horizontal, font.WritingMode);
        Assert.Equal(row?.WritingMode ?? (name.EndsWith('V') ? 1 : 0), (int)font.WritingMode);
        Assert.Equal(row?.UseCMap, cmap.Parent?.Name);
        foreach ((string code, int cid) in row?.Samples ?? [("4E00", 0x4E00)])
        {
            CidGlyph glyph = font.ReadGlyph(Convert.FromHexString(code));
            Assert.Equal((code.Length / 2, true, cid), (glyph.Code.Length, glyph.Code.IsValid, glyph.Cid));
        }

        Assert.Empty(DiagnosticsBesidesNoProgram(document));
    }

    [Theory]
    [InlineData("90ms-RKSJ-H", "41", 264)]
    [InlineData("90ms-RKSJ-H", "88EA", 1200)]
    [InlineData("90ms-RKSJ-H", "93FA", 3284)]
    [InlineData("90ms-RKSJ-H", "967B", 3722)]
    [InlineData("90ms-RKSJ-H", "1F", 231)]
    [InlineData("90ms-RKSJ-V", "8141", 7887)]
    [InlineData("90ms-RKSJ-V", "88EA", 1200)]
    [InlineData("UniJIS-UTF16-H", "4E00", 1200)]
    [InlineData("UniJIS-UTF16-H", "0041", 34)]
    [InlineData("UniJIS-UTF16-V", "4E00", 1200)]
    [InlineData("GBK2K-H", "8232F530", 26370)]
    [InlineData("ETenms-B5-V", "A140", 99)]
    public void Known_codes_select_the_CIDs_Adobe_assigns_them(string name, string code, int cid)
    {
        using PdfDocument document = Open("/" + name, PackageManifest.CMap(name).Ordering, 7);

        Assert.Equal(cid, Font(document).ReadGlyph(Convert.FromHexString(code)).Cid);
    }

    [Fact]
    public void A_V_CMap_resolves_its_usecmap_chain_through_the_package()
    {
        using PdfDocument document = Open("/ETenms-B5-V", "CNS1", 0);

        CMap cmap = Font(document).Encoding;

        Assert.Equal(["ETenms-B5-V", "ETenms-B5-H", "ETen-B5-H"], [cmap.Name, cmap.Parent?.Name, cmap.Parent?.Parent?.Name]);
    }

    [Fact]
    public void An_embedded_CMap_that_uses_a_predefined_CMap_gets_its_mappings_from_the_package()
    {
        string body = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Child-H def /90ms-RKSJ-H usecmap 1 begincidchar <41> 5 endcidchar endcmap end end";
        string stream = string.Create(CultureInfo.InvariantCulture, $"<< /Type /CMap /CMapName /Child-H /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >> /Length {body.Length} >>\nstream\n{body}\nendstream");
        using PdfDocument document = Open("7 0 R", "Japan1", 2, stream);
        PdfType0Font font = Font(document);

        Assert.Equal([5, 3284], new byte[][] { [0x41], [0x93, 0xFA] }.Select(code => font.ReadGlyph(code).Cid));
        Assert.Empty(DiagnosticsBesidesNoProgram(document));
    }

    [Theory]
    [MemberData(nameof(PackagedFiles))]
    public void Every_packaged_file_is_the_unmodified_upstream_file(string file)
    {
        PackageManifest row = PackageManifest.All.Single(r => $"{r.Kind}/{r.Name}" == file);
        IFontResolver resolver = new PredefinedCMapResolver();

        Assert.True(resolver.TryResolveResource(row.Kind, row.Name, out ReadOnlyMemory<byte> data));

        Assert.Equal(row.Size, data.Length);
        Assert.Equal(row.Sha256, Convert.ToHexStringLower(SHA256.HashData(data.Span)));
        Assert.Contains($"/CMapName /{row.Name} def", Encoding.ASCII.GetString(data.Span), StringComparison.Ordinal);
        Assert.True(resolver.TryResolveResource(row.Kind, row.Name, out ReadOnlyMemory<byte> again));
        Assert.True(data.Span == again.Span, "The same memory is returned on every call.");
    }

    [Theory]
    [InlineData(FontResourceKind.CMap, "Identity-H")]
    [InlineData(FontResourceKind.CMap, "Adobe-Japan1-UCS2")]
    [InlineData(FontResourceKind.CidToUnicode, "90ms-RKSJ-H")]
    [InlineData(FontResourceKind.CMap, "90ms-rksj-h")]
    [InlineData(FontResourceKind.CMap, "../90ms-RKSJ-H")]
    [InlineData(FontResourceKind.CMap, "UniJIS2004-UTF16-H")]
    [InlineData(FontResourceKind.CMap, "")]
    [InlineData((FontResourceKind)7, "90ms-RKSJ-H")]
    public void The_package_answers_only_its_own_names_of_the_kind_asked(FontResourceKind kind, string name)
    {
        IFontResolver resolver = new PredefinedCMapResolver();

        Assert.False(resolver.TryResolveResource(kind, name, out ReadOnlyMemory<byte> data));
        Assert.True(data.IsEmpty);
        Assert.Null(resolver.ResolveFont(new FontQuery("MS-Mincho")));
    }

    [Fact]
    public void The_CID_to_Unicode_tables_of_the_five_Adobe_CJK_collections_are_served_by_name()
    {
        IFontResolver resolver = new PredefinedCMapResolver();

        foreach (string name in new[] { "Adobe-CNS1-UCS2", "Adobe-GB1-UCS2", "Adobe-Japan1-UCS2", "Adobe-Korea1-UCS2", "Adobe-KR-UCS2" })
        {
            Assert.True(resolver.TryResolveResource(FontResourceKind.CidToUnicode, name, out ReadOnlyMemory<byte> data), name);
            Assert.Contains("beginbfrange", Encoding.ASCII.GetString(data.Span), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Concurrent_requests_inflate_each_file_once_and_share_it()
    {
        var resolver = new PredefinedCMapResolver();
        ReadOnlyMemory<byte>[] results = new ReadOnlyMemory<byte>[64];

        Parallel.For(0, results.Length, index => ((IFontResolver)resolver).TryResolveResource(FontResourceKind.CMap, "UniCNS-UCS2-H", out results[index]));

        Assert.All(results, result => Assert.True(result.Span == results[0].Span));
        Assert.Equal(326_349, results[0].Length);
    }

    [Fact]
    public void Resolvers_registered_before_the_package_win_and_the_package_answers_the_rest()
    {
        PdfOptions options = new PdfOptions().UseFontResolver(new OneCMapResolver()).UsePredefinedCMaps();
        using PdfDocument company = Open("/90ms-RKSJ-H", "Japan1", 2, options: options);
        using PdfDocument package = Open("/90ms-RKSJ-V", "Japan1", 2, options: options);

        // 90ms-RKSJ-V comes from the package; the 90ms-RKSJ-H it uses is the company's, found the same way.
        Assert.Equal(9, Font(company).ReadGlyph("A"u8).Cid);
        Assert.Equal((7887, 9), (Font(package).ReadGlyph([0x81, 0x41]).Cid, Font(package).ReadGlyph("A"u8).Cid));
    }

    [Fact]
    public void Text_cidcff_predefined_cmap_pdf_shows_its_three_glyphs_through_90ms_RKSJ_H_with_the_package()
    {
        using PdfDocument document = PdfDocument.Open(TestSupport.Corpus.Path("text-cidcff-predefined-cmap.pdf"), new PdfOptions().UsePredefinedCMaps());
        PdfType0Font font = Font(document);

        CidGlyph[] glyphs = [.. ReadAll(font, [0x41, 0x93, 0xFA, 0x96, 0x7B])];

        Assert.Equal([(264, 1, 500.0), (3284, 2, 1000.0), (3722, 3, 1000.0)], glyphs.Select(glyph => (glyph.Cid, glyph.GlyphId, glyph.Width)));
        Assert.Equal("90ms-RKSJ-H", font.Encoding.Name);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_notices_list_every_packaged_file_with_its_SHA_256()
    {
        string notices = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "THIRD-PARTY-NOTICES.txt"));

        Assert.All(PackageManifest.All, row => Assert.Contains($"{row.Sha256}  {row.UpstreamPath}", notices, StringComparison.Ordinal));
    }

    private static List<CidGlyph> ReadAll(PdfType0Font font, byte[] text)
    {
        var glyphs = new List<CidGlyph>();
        for (int position = 0; position < text.Length; position += glyphs[^1].Code.Length)
        {
            glyphs.Add(font.ReadGlyph(text.AsSpan(position)));
        }

        return glyphs;
    }

    /// <summary>
    /// The diagnostics other than the one a CIDFont that is not embedded records when its glyphs are asked for and no resolver has a
    /// program for it (FontProgramNotFound, Information; ADR 0009).
    /// </summary>
    private static IEnumerable<Diagnostics.Diagnostic> DiagnosticsBesidesNoProgram(PdfDocument document) =>
        document.Diagnostics.Where(diagnostic => diagnostic.Code != "FontProgramNotFound");

    private static PdfType0Font Font(PdfDocument document) => Assert.IsType<PdfType0Font>(Assert.Single(document.Pages).GetFont("F1"));

    /// <summary>A Type 0 font (4) with the given Encoding over a non-embedded CIDFontType0 (5) of the given Adobe collection; extra objects from 7.</summary>
    private static PdfDocument Open(string encoding, string ordering, int supplement, string? extra = null, PdfOptions? options = null) =>
        FontPdf.Open(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideCID /Encoding {encoding} /DescendantFonts [5 0 R] >>",
            (options ?? new PdfOptions().UsePredefinedCMaps()).UseSystemFontResolver(null),
            [$"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /BroadsideCID /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement {supplement} >> >>", "null", .. extra is null ? Array.Empty<string>() : [extra]]);

    /// <summary>A company resolver with its own 90ms-RKSJ-H, which maps A to CID 9 and uses nothing else.</summary>
    private sealed class OneCMapResolver : IFontResolver
    {
        public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
        {
            data = kind == FontResourceKind.CMap && name == "90ms-RKSJ-H"
                ? Encoding.ASCII.GetBytes("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /90ms-RKSJ-H def 2 begincodespacerange <00> <80> <8140> <FFFF> endcodespacerange 1 begincidchar <41> 9 endcidchar endcmap end end")
                : default;
            return !data.IsEmpty;
        }
    }
}
