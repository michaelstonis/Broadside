using System.Diagnostics;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Type 0 fonts and CIDFonts the corpus has no file for: metrics edge cases, glyph selection fallbacks, CMap streams that use other
/// CMaps, and damaged dictionaries, built in memory and read through the public document API. ISO 32000-2 §9.7.
/// </summary>
public class CompositeFontTests
{
    private const string Type0 = "<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideMinimal /Encoding /Identity-H /DescendantFonts [5 0 R] >>";
    private const string SystemInfo = "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>";

    private static readonly Lazy<string> TrueTypeProgram = new(() =>
    {
        using PdfDocument document = TrueTypeCorpusTests.Open("text-truetype-embedded.pdf");
        var stream = (CosStream)document.Resolve(new CosReference(7, 0));
        return Encoding.Latin1.GetString(stream.EncodedData.Span);
    });

    [Fact]
    public void A_W_range_over_every_CID_costs_one_entry_and_the_first_specification_of_a_CID_wins()
    {
        var stopwatch = Stopwatch.StartNew();
        using PdfDocument document = Open(cidEntries: "/W [0 2147483647 500 5 [700]]");
        using PdfDocument reversed = Open(cidEntries: "/W [5 [700] 0 10 500 4 [1]]");

        PdfCidFont font = Descendant(document);
        PdfCidFont other = Descendant(reversed);

        Assert.Equal([500, 500, 500], new[] { 0, 5, 65535 }.Select(font.GetWidth));
        Assert.Equal([500, 700, 500, 500, 1000], new[] { 4, 5, 6, 10, 11 }.Select(other.GetWidth));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Empty(Codes(document));
        Assert.Empty(Codes(reversed));
    }

    [Fact]
    public void Malformed_W_and_DW_keep_what_can_be_read_with_diagnostics()
    {
        using PdfDocument document = Open(cidEntries: "/DW (wide) /W [1 [600 /x 650] 4 /y]");
        PdfCidFont font = Descendant(document);

        Assert.Equal([1000, 600, 1000, 650, 1000], new[] { 0, 1, 2, 3, 4 }.Select(font.GetWidth));
        Assert.Equal(["CidFontWidthsInvalid"], Codes(document));
    }

    [Fact]
    public void W2_groups_give_vertical_metrics_and_other_CIDs_take_DW2_with_half_their_width()
    {
        using PdfDocument document = Open(cidEntries: "/W [1 [600]] /DW2 [800 -900] /W2 [1 [-1000 300 700 -1100 310 710] 10 12 -950 250 650 20 [1 2]]");
        PdfCidFont font = Descendant(document);

        Assert.Equal(new CidVerticalMetrics(-1000, 300, 700), font.GetVerticalMetrics(1));
        Assert.Equal(new CidVerticalMetrics(-1100, 310, 710), font.GetVerticalMetrics(2));
        Assert.Equal(new CidVerticalMetrics(-950, 250, 650), font.GetVerticalMetrics(11));
        Assert.Equal(new CidVerticalMetrics(-900, 500, 800), font.GetVerticalMetrics(3));
        Assert.Equal(new CidVerticalMetrics(-900, 500, 800), font.GetVerticalMetrics(20));
        Assert.Equal(["CidFontVerticalMetricsInvalid"], Codes(document));
    }

    [Fact]
    public void Without_DW2_the_default_vertical_metrics_are_880_and_minus_1000()
    {
        using PdfDocument document = Open(cidEntries: "/DW 600");

        Assert.Equal(new CidVerticalMetrics(-1000, 300, 880), Descendant(document).GetVerticalMetrics(7));
    }

    [Fact]
    public void A_glyph_map_stream_of_odd_length_drops_its_last_byte_and_CIDs_past_its_end_show_CID_0()
    {
        using PdfDocument document = OpenEmbedded("/CIDToGIDMap 7 0 R", Stream(string.Empty, "\0\0\0\u0002\0\u0001\0"));
        PdfCidFont font = Descendant(document);

        Assert.Equal([0, 2, 1, 0], new[] { 0, 1, 2, 3 }.Select(font.GetGlyphId));
        Assert.Equal(["CidToGidMapInvalid"], Codes(document));
    }

    [Fact]
    public void An_embedded_CIDFontType2_without_a_glyph_map_uses_CIDs_as_glyph_ids_with_a_diagnostic()
    {
        using PdfDocument document = OpenEmbedded(string.Empty);
        PdfCidFont font = Descendant(document);

        Assert.Equal([0, 1, 2, 0], new[] { 0, 1, 2, 3 }.Select(font.GetGlyphId));
        Assert.Equal(["CidToGidMapMissing"], Codes(document));
    }

    [Fact]
    public void A_CID_without_a_glyph_shows_the_glyph_of_its_notdef_mapping_and_keeps_its_own_width()
    {
        string cmap = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange 2 begincidchar <41> 50 <42> 60 endcidchar 1 beginnotdefchar <41> 2 endnotdefchar");
        using PdfDocument document = OpenEmbedded("/CIDToGIDMap /Identity /W [50 [750]]", cmap, encoding: "7 0 R");
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph a = font.ReadGlyph("A"u8);
        CidGlyph b = font.ReadGlyph("B"u8);

        Assert.Equal((50, 2, 750.0), (a.Cid, a.GlyphId, a.Width));
        Assert.Equal((60, 0, 1000.0), (b.Cid, b.GlyphId, b.Width));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Word_spacing_applies_only_to_the_single_byte_code_32()
    {
        string cmap = CMapStream("2 begincodespacerange <00> <7F> <8000> <FFFF> endcodespacerange 1 begincidrange <00> <7F> 0 endcidrange");
        using PdfDocument oneByte = Open(encoding: "6 0 R", objects: cmap);
        using PdfDocument identity = Open();

        Assert.True(CompositeFontCorpusTests.Font(oneByte).ReadGlyph(" "u8).AppliesWordSpacing);
        Assert.False(CompositeFontCorpusTests.Font(identity).ReadGlyph([0x00, 0x20]).AppliesWordSpacing);
        Assert.False(CompositeFontCorpusTests.Font(identity).ReadGlyph([0x20, 0x20]).AppliesWordSpacing);
    }

    [Fact]
    public void A_non_embedded_TrueType_CIDFont_with_Identity_H_is_reported_and_without_a_substitute_has_no_glyphs()
    {
        // The glyph comes from the CIDFont's substitute (CidFontSubstitutionTests); with no resolver there is none, which is recorded.
        using PdfDocument document = Open(options: new PdfOptions().UseSystemFontResolver(null));
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph glyph = font.ReadGlyph([0x01, 0x02]);

        Assert.False(font.DescendantFont!.IsEmbedded);
        Assert.Null(font.DescendantFont.Program);
        Assert.Null(font.DescendantFont.Substitute);
        Assert.Equal((0x102, 0), (glyph.Cid, glyph.GlyphId));
        Assert.Equal(["Type0IdentityNotEmbedded", "FontProgramNotFound"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Theory]
    [InlineData("/GBK-EUC-H")]
    [InlineData(null)]
    public void A_non_embedded_TrueType_CIDFont_whose_encoding_only_falls_back_to_Identity_H_is_not_reported_for_using_Identity_H(string? encoding)
    {
        // Issue #80 triage: pdfium's FRC_8.2.2 files use /GBK-EUC-H with a non-embedded CIDFontType2; §9.7.5.2 forbids only Identity-H/V written as such.
        using PdfDocument document = Open(encoding: encoding);

        _ = CompositeFontCorpusTests.Font(document).ReadGlyph([0x01, 0x02]);

        Assert.DoesNotContain("Type0IdentityNotEmbedded", document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_missing_encoding_reads_codes_as_Identity_H_and_an_unavailable_predefined_CMap_by_its_codespace()
    {
        using PdfDocument missing = OpenEmbedded("/CIDToGIDMap /Identity", encoding: null);
        using PdfDocument predefined = OpenEmbedded("/CIDToGIDMap /Identity", encoding: "/90ms-RKSJ-H");

        Assert.Same(CMap.IdentityH, CompositeFontCorpusTests.Font(missing).Encoding);
        Assert.Equal(("90ms-RKSJ-H", false), (CompositeFontCorpusTests.Font(predefined).Encoding.Name, CompositeFontCorpusTests.Font(predefined).Encoding.IsIdentity));
        Assert.Equal(["Type0EncodingMissing"], Codes(missing));
        Diagnostic diagnostic = Assert.Single(predefined.Diagnostics, d => d.Code == "CMapUnavailable");
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);

        // The stand-in keeps the CMap's character collection, so a CIDFont of another collection is still noticed (§9.7.3).
        Assert.Equal(["CMapUnavailable", "CidSystemInfoMismatch"], Codes(predefined));
    }

    [Fact]
    public void DescendantFonts_given_as_a_dictionary_is_used_with_a_diagnostic_and_strict_mode_throws()
    {
        string type0 = $"<< /Type /Font /Subtype /Type0 /BaseFont /X /Encoding /Identity-H /DescendantFonts << /Type /Font /Subtype /CIDFontType2 /BaseFont /X {SystemInfo} /W [1 [640]] >> >>";
        using PdfDocument document = FontPdf.Open(type0);
        using PdfDocument strict = FontPdf.Open(type0, new PdfOptions().UseStrict());

        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        Assert.Equal(640, font.ReadGlyph([0x00, 0x01]).Width);
        Assert.Null(font.DescendantFont!.Reference);
        Assert.Contains("Type0DescendantFontsInvalid", Codes(document));
        DiagnosticException error = Assert.Throws<DiagnosticException>(() => CompositeFontCorpusTests.Font(strict).DescendantFont);
        Assert.Equal("Type0DescendantFontsInvalid", error.Diagnostic.Code);
    }

    [Fact]
    public void A_Type_0_font_without_a_descendant_shows_glyph_0_with_default_metrics()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type0 /BaseFont /X /Encoding /Identity-H >>");
        PdfType0Font font = CompositeFontCorpusTests.Font(document);

        CidGlyph glyph = font.ReadGlyph([0x00, 0x05]);

        Assert.Null(font.DescendantFont);
        Assert.Equal((5, 0, 1000.0, new CidVerticalMetrics(-1000, 500, 880)), (glyph.Cid, glyph.GlyphId, glyph.Width, glyph.VerticalMetrics));
        Assert.Equal(["Type0DescendantFontsInvalid"], Codes(document));
    }

    [Fact]
    public void A_CMap_stream_using_another_stream_inherits_its_codespace_and_mappings()
    {
        string child = CMapStream("/Parent-H usecmap 1 begincidchar <41> 9 endcidchar", "/UseCMap 7 0 R", name: "Child-H");
        string parent = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidrange <41> <5A> 100 endcidrange", name: "Parent-H");
        using PdfDocument document = Open(encoding: "6 0 R", objects: [child, parent]);
        CMap cmap = CompositeFontCorpusTests.Font(document).Encoding;

        Assert.Equal("Parent-H", cmap.Parent?.Name);
        Assert.Equal([9, 101], new[] { "A"u8.ToArray(), "B"u8.ToArray() }.Select(text => cmap.GetCid(cmap.ReadCode(text))));
        Assert.Empty(Codes(document));
    }

    [Fact]
    public void An_in_file_usecmap_naming_another_CMap_than_the_UseCMap_stream_is_reported_and_the_stream_wins()
    {
        string child = CMapStream("/Other-H usecmap", "/UseCMap 7 0 R", name: "Child-H");
        string parent = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange", name: "Parent-H");
        using PdfDocument document = Open(encoding: "6 0 R", objects: [child, parent]);

        Assert.Equal("Parent-H", CompositeFontCorpusTests.Font(document).Encoding.Parent?.Name);
        Assert.Equal(["CMapUseCMapInvalid"], Codes(document));
    }

    [Fact]
    public void A_UseCMap_cycle_is_cut_with_a_diagnostic()
    {
        string first = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidchar <41> 1 endcidchar", "/UseCMap 7 0 R", name: "A");
        string second = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidchar <42> 2 endcidchar", "/UseCMap 6 0 R", name: "B");
        using PdfDocument document = Open(encoding: "6 0 R", objects: [first, second]);
        CMap cmap = CompositeFontCorpusTests.Font(document).Encoding;

        Assert.Equal(("A", "B"), (cmap.Name, cmap.Parent?.Name));
        Assert.Null(cmap.Parent!.Parent);
        Assert.Equal([1, 2], new[] { "A"u8.ToArray(), "B"u8.ToArray() }.Select(text => cmap.GetCid(cmap.ReadCode(text))));
        Assert.Equal(["CMapUseCMapCycle"], Codes(document));
    }

    [Fact]
    public void UseCMap_chains_deeper_than_five_levels_are_cut_with_a_diagnostic()
    {
        var streams = new List<string>();
        for (int level = 0; level <= 6; level++)
        {
            string use = level < 6 ? $"/UseCMap {7 + level} 0 R" : string.Empty;
            string body = level == 6 ? "1 begincodespacerange <00> <FF> endcodespacerange" : string.Empty;
            streams.Add(CMapStream(body + $" 1 begincidchar <{0x41 + level:X2}> {level + 1} endcidchar", use, name: $"L{level}"));
        }

        using PdfDocument document = Open(encoding: "6 0 R", objects: [.. streams]);
        CMap cmap = CompositeFontCorpusTests.Font(document).Encoding;
        int depth = 0;
        for (CMap? parent = cmap.Parent; parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        Assert.Equal(5, depth);
        Assert.Contains("CMapUseCMapTooDeep", Codes(document));
    }

    [Fact]
    public void A_UseCMap_naming_a_predefined_CMap_no_resolver_supplies_is_reported_as_unavailable_and_gives_only_its_codespace()
    {
        string cmap = CMapStream("1 begincodespacerange <00> <FF> endcodespacerange", "/UseCMap /UniJIS-UCS2-H");
        using PdfDocument document = Open(encoding: "6 0 R", objects: cmap);

        CMap? parent = CompositeFontCorpusTests.Font(document).Encoding.Parent;
        Assert.Equal("UniJIS-UCS2-H", parent?.Name);
        Assert.Equal(0, parent!.GetCid(parent.ReadCode([0x4E, 0x00])));
        Assert.Contains("CMapUnavailable", Codes(document));
    }

    [Fact]
    public void The_CMap_files_WMode_wins_over_its_streams()
    {
        string cmap = CMapStream("/WMode 1 def 1 begincodespacerange <00> <FF> endcodespacerange", "/WMode 0");
        using PdfDocument document = Open(encoding: "6 0 R", objects: cmap);

        Assert.Equal(WritingMode.Vertical, CompositeFontCorpusTests.Font(document).WritingMode);
        Assert.Contains("CMapWritingModeMismatch", Codes(document));
    }

    [Fact]
    public void A_CMap_of_another_character_collection_than_the_CIDFont_is_reported()
    {
        string cmap = CMapStream("/CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 6 >> def 1 begincodespacerange <00> <FF> endcodespacerange");
        using PdfDocument document = Open(encoding: "6 0 R", objects: cmap);

        Assert.NotNull(CompositeFontCorpusTests.Font(document).Encoding);
        Assert.Contains("CidSystemInfoMismatch", Codes(document));
    }

    [Fact]
    public void Metrics_follow_changes_to_the_CIDFont_dictionary_and_its_arrays()
    {
        using PdfDocument document = Open(cidEntries: "/W [1 [600]]");
        PdfCidFont font = Descendant(document);
        Assert.Equal(600, font.GetWidth(1));

        var widths = (CosArray)font.Dictionary[new CosName("W")];
        ((CosArray)widths[1])[0] = new CosInteger(650);
        Assert.Equal(650, font.GetWidth(1));

        font.Dictionary[new CosName("DW")] = new CosInteger(300);
        Assert.Equal(300, font.GetWidth(2));
    }

    [Fact]
    public void A_CIDFont_with_an_unknown_subtype_is_read_as_CIDFontType2_with_a_diagnostic()
    {
        using PdfDocument document = Open(subtype: "/CIDFontType9");

        Assert.IsType<PdfCidFontType2>(CompositeFontCorpusTests.Font(document).DescendantFont);
        Assert.Contains("CidFontSubtypeInvalid", Codes(document));
    }

    [Fact]
    public void A_CIDFontType0_without_a_program_reads_its_metrics_and_has_no_glyphs()
    {
        using PdfDocument document = Open(subtype: "/CIDFontType0", cidEntries: "/W [1 [450]]");
        PdfCidFont font = Assert.IsType<PdfCidFontType0>(CompositeFontCorpusTests.Font(document).DescendantFont);

        Assert.Equal(PdfCidFontType.CidFontType0, font.CidFontType);
        Assert.Equal(450, font.GetWidth(1));
        Assert.Equal(0, font.GetGlyphId(1));
    }

    private static PdfDocument Open(string cidEntries = "", string? encoding = "/Identity-H", string subtype = "/CIDFontType2", PdfOptions? options = null, params string[] objects)
    {
        string type0 = encoding is null ? Type0.Replace(" /Encoding /Identity-H", string.Empty, StringComparison.Ordinal) : Type0.Replace("/Identity-H", encoding, StringComparison.Ordinal);
        return FontPdf.Open(type0, options ?? new PdfOptions(), [$"<< /Type /Font /Subtype {subtype} /BaseFont /BroadsideMinimal {SystemInfo} {cidEntries} >>", .. objects]);
    }

    /// <summary>A Type 0 font over an embedded CIDFontType2: objects 4 (font), 5 (CIDFont), 6 (descriptor), 7 (program); or with <paramref name="extra"/> (such as a map stream) as object 7, the program as 8.</summary>
    private static PdfDocument OpenEmbedded(string cidEntries, string? extra = null, string? encoding = "/Identity-H", PdfOptions? options = null)
    {
        int program = extra is null ? 7 : 8;
        string type0 = encoding is null ? Type0.Replace(" /Encoding /Identity-H", string.Empty, StringComparison.Ordinal) : Type0.Replace("/Identity-H", encoding, StringComparison.Ordinal);
        string font = TrueTypeProgram.Value;
        List<string> objects =
        [
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /BroadsideMinimal {SystemInfo} /FontDescriptor 6 0 R {cidEntries} >>",
            $"<< /Type /FontDescriptor /FontName /BroadsideMinimal /Flags 4 /FontBBox [0 0 700 700] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 200 /FontFile2 {program} 0 R >>",
        ];
        if (extra is not null)
        {
            objects.Add(extra);
        }

        objects.Add(Stream($"/Length1 {font.Length}", font));
        return FontPdf.Open(type0, options ?? new PdfOptions(), [.. objects]);
    }

    private static string CMapStream(string body, string entries = "", string name = "Test-H") =>
        Stream(
            $"/Type /CMap /CMapName /{name} {SystemInfo} {entries}",
            $"/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /{name} def {body}\nendcmap CMapName currentdict /CMap defineresource pop end end");

    private static string Stream(string entries, string data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream");

    private static PdfCidFont Descendant(PdfDocument document) =>
        Assert.IsAssignableFrom<PdfCidFont>(CompositeFontCorpusTests.Font(document).DescendantFont);

    /// <summary>The distinct diagnostic codes, without the one every non-embedded font with Identity-H here gets (tested on its own).</summary>
    private static string[] Codes(PdfDocument document) =>
        [.. document.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => code != "Type0IdentityNotEmbedded").Distinct()];
}
