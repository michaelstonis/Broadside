using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Tests.Fonts;

/// <summary>
/// ToUnicode CMaps (ISO 32000-2 §9.10.3; Adobe TN 5014 §7.4) through <see cref="PdfFont.GetUnicode(CharacterCode)"/>: bfchar and
/// bfrange (string and array forms), ligatures, surrogate pairs, the "last byte" rule, the lenient forms real files use, the
/// per-code fallback of §9.10.2, and the vectors of ISO Example 2, pdf.js's cmap_spec.js and PDFBox's malformed bfrange files
/// (rewritten here; expected values from the spec text and those projects' assertions).
/// </summary>
public class ToUnicodeTests
{
    [Fact]
    public void The_example_of_ISO_32000_2_9_10_3_maps_a_range_ligatures_and_a_surrogate_pair()
    {
        using PdfDocument document = OpenType0(ToUnicode(
            "2 beginbfrange\n<0000> <005E> <0020>\n<005F> <0061> [<00660066> <00660069> <00660066006C>]\nendbfrange\n"
            + "1 beginbfchar\n<3A51> <D840DC3E>\nendbfchar"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(" ", Map(font, 0x0000).Text);
        Assert.Equal("A", Map(font, 0x0021).Text);
        Assert.Equal("~", Map(font, 0x005E).Text);
        Assert.Equal("ff", Map(font, 0x005F).Text);
        Assert.Equal("fi", Map(font, 0x0060).Text);
        Assert.Equal("ffl", Map(font, 0x0061).Text);
        Assert.Equal(("\U0002003E", UnicodeSource.ToUnicode), Map(font, 0x3A51));
        Assert.Empty(ToUnicodeCodes(document));
    }

    [Fact]
    public void The_pdfjs_bfchar_and_bfrange_cases_map_with_integer_destinations_read_as_Unicode_values()
    {
        // pdf.js test/unit/cmap_spec.js "parses beginbfchar", "parses beginbfrange with range", "... with array".
        using PdfDocument document = OpenType0(ToUnicode(
            "2 beginbfchar\n<03> <00>\n<04> <01>\nendbfchar\n1 beginbfrange\n<06> <0B> 0\nendbfrange\n1 beginbfrange\n<0D> <12> [ 0 1 2 3 4 5 ]\nendbfrange",
            codespace: "<00> <FF>"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("\u0000", Map(font, 0x03, 1).Text);
        Assert.Equal("\u0001", Map(font, 0x04, 1).Text);
        Assert.Equal(UnicodeSource.Unmapped, Map(font, 0x05, 1).Source);
        Assert.Equal("\u0000", Map(font, 0x06, 1).Text);
        Assert.Equal("\u0005", Map(font, 0x0B, 1).Text);
        Assert.Equal("\u0000", Map(font, 0x0D, 1).Text);
        Assert.Equal("\u0005", Map(font, 0x12, 1).Text);
        Assert.Contains("ToUnicodeDestinationInvalid", ToUnicodeCodes(document));
    }

    [Fact]
    public void A_range_past_the_last_byte_rule_increments_the_whole_character_with_one_diagnostic()
    {
        // <0000> <01FF> <0000> breaks "last byte <= 255 - (srcCode2 - srcCode1)"; producers mean U+0000 to U+01FF.
        // PDFBox CMapMalformedbfrange2: <0232> <0432> <0041> crosses 256 codes the same way.
        using PdfDocument document = OpenType0(ToUnicode("2 beginbfrange\n<0000> <01FF> <0000>\n<0232> <0432> <0041>\nendbfrange"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("Ā", Map(font, 0x0100).Text);
        Assert.Equal("ǿ", Map(font, 0x01FF).Text);
        Assert.Equal("A", Map(font, 0x0232).Text);
        Assert.Equal("Ɂ", Map(font, 0x0432).Text);
        Assert.Equal(["ToUnicodeBfRangeOverflow"], ToUnicodeCodes(document));
    }

    [Fact]
    public void A_range_whose_upper_code_is_below_its_lower_code_is_dropped_and_the_rest_of_the_block_is_kept()
    {
        // PDFBox CMapMalformedbfrange1 has <0109> <0100>; PDFBox abandons the block, Broadside drops only the line.
        using PdfDocument document = OpenType0(ToUnicode("2 beginbfrange\n<0109> <0100> <0041>\n<0200> <0201> <0061>\nendbfrange"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(UnicodeSource.Unmapped, Map(font, 0x0105).Source);
        Assert.Equal("b", Map(font, 0x0201).Text);
        Assert.Contains("CMapEntryInvalid", ToUnicodeCodes(document));
    }

    [Fact]
    public void A_surrogate_pair_destination_increments_as_one_character_and_never_into_the_surrogate_range()
    {
        using PdfDocument document = OpenType0(ToUnicode("2 beginbfrange\n<0001> <0003> <D840DC3E>\n<1000> <1002> <D7FF>\nendbfrange"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(["\U0002003E", "\U0002003F", "\U00020040"], [Map(font, 1).Text, Map(font, 2).Text, Map(font, 3).Text]);
        Assert.Equal(["퟿", "�", "�"], [Map(font, 0x1000).Text, Map(font, 0x1001).Text, Map(font, 0x1002).Text]);
    }

    [Fact]
    public void A_range_over_every_two_byte_code_is_one_entry_and_skips_the_surrogates()
    {
        using PdfDocument document = OpenType0(ToUnicode("1 beginbfrange\n<0000> <FFFF> <0000>\nendbfrange"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("中", Map(font, 0x4E2D).Text);
        Assert.Equal("�", Map(font, 0xD800).Text);
        Assert.Equal("", Map(font, 0xE000).Text);
        Assert.Equal("￿", Map(font, 0xFFFF).Text);
    }

    [Fact]
    public void Lenient_destinations_are_read_with_diagnostics()
    {
        string data =
            "6 beginbfchar\n<0001> <41>\n<0002> <D800>\n<0003> <>\n<0004> /Lcommaaccent\n<0005> (\0B)\n<0006> 8364\nendbfchar\n"
            + "1 beginbfrange\n<0010> <0013> [<0041> <0042>]\nendbfrange";
        using PdfDocument document = OpenType0(ToUnicode(data));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("A", Map(font, 1).Text);
        Assert.Equal("�", Map(font, 2).Text);
        Assert.Equal((string.Empty, UnicodeSource.ToUnicode), Map(font, 3));
        Assert.Equal("Ļ", Map(font, 4).Text);
        Assert.Equal("B", Map(font, 5).Text);
        Assert.Equal("€", Map(font, 6).Text);
        Assert.Equal(["A", "B"], [Map(font, 0x10).Text, Map(font, 0x11).Text]);
        Assert.Equal(UnicodeSource.Unmapped, Map(font, 0x12).Source);
        Assert.Equal(["ToUnicodeDestinationInvalid"], ToUnicodeCodes(document));
    }

    [Fact]
    public void A_destination_longer_than_512_bytes_is_cut_with_a_diagnostic()
    {
        string longText = string.Concat(Enumerable.Repeat("0041", 300));
        using PdfDocument document = OpenType0(ToUnicode($"1 beginbfchar\n<0001> <{longText}>\nendbfchar"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(new string('A', 256), Map(font, 1).Text);
        Assert.Equal(["ToUnicodeDestinationInvalid"], ToUnicodeCodes(document));
    }

    [Fact]
    public void A_code_the_ToUnicode_CMap_has_only_under_another_length_is_matched_by_value_with_one_diagnostic()
    {
        // A simple font's ToUnicode with two-byte codes (§9.10.3 wants one byte): matched by value.
        using PdfDocument simple = OpenSimple(ToUnicode("1 beginbfchar\n<0041> <0062>\nendbfchar"));
        PdfFont helvetica = FontPdf.Font(simple);

        Assert.Equal(("b", UnicodeSource.ToUnicode), Map(helvetica, 0x41, 1));
        Assert.Equal(("b", UnicodeSource.ToUnicode), Map(helvetica, 0x41, 1));
        Assert.Equal(["ToUnicodeCodeLengthMismatch"], ToUnicodeCodes(simple));

        // A Type 0 font's two-byte codes with a one-byte ToUnicode.
        using PdfDocument composite = OpenType0(ToUnicode("1 beginbfchar\n<41> <0063>\nendbfchar", codespace: "<00> <FF>"));
        Assert.Equal(("c", UnicodeSource.ToUnicode), Map(FontPdf.Font(composite), 0x0041));
        Assert.Equal(["ToUnicodeCodeLengthMismatch"], ToUnicodeCodes(composite));
    }

    [Fact]
    public void A_code_the_ToUnicode_CMap_lacks_falls_through_to_the_glyph_name()
    {
        using PdfDocument document = OpenSimple(ToUnicode("1 beginbfchar\n<41> <0062>\nendbfchar", codespace: "<00> <FF>"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(("b", UnicodeSource.ToUnicode), Map(font, 0x41, 1));
        Assert.Equal(("B", UnicodeSource.GlyphName), Map(font, 0x42, 1));
        Assert.Empty(ToUnicodeCodes(document));
    }

    [Fact]
    public void A_ToUnicode_entry_naming_Identity_H_reads_each_code_as_its_own_UTF_16_value_with_a_diagnostic()
    {
        using PdfDocument document = OpenType0(toUnicode: null, fontEntries: "/ToUnicode /Identity-H");
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(("中", UnicodeSource.ToUnicode), Map(font, 0x4E2D));
        Assert.Equal(["ToUnicodeIdentityName"], ToUnicodeCodes(document));
    }

    [Fact]
    public void A_ToUnicode_entry_that_is_not_a_stream_is_ignored_with_a_diagnostic()
    {
        using PdfDocument document = OpenType0(toUnicode: null, fontEntries: "/ToUnicode 42");

        Assert.Equal(UnicodeSource.Unmapped, Map(FontPdf.Font(document), 0x41).Source);
        Assert.Equal(["ToUnicodeInvalid"], ToUnicodeCodes(document));
    }

    [Fact]
    public void CID_mappings_in_a_ToUnicode_CMap_map_codes_to_the_Unicode_value_of_their_CID()
    {
        using PdfDocument document = OpenType0(ToUnicode("1 begincidrange\n<0000> <FFFF> 0\nendcidrange\n1 beginnotdefchar\n<0001> 66\nendnotdefchar"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("A", Map(font, 0x41).Text);
        Assert.Equal("\u0001", Map(font, 0x01).Text);
        Assert.Equal(["ToUnicodeCidMapping"], ToUnicodeCodes(document));
    }

    [Fact]
    public void A_ToUnicode_CMap_using_another_takes_its_mappings_and_its_own_win()
    {
        string parent = ToUnicode("2 beginbfchar\n<0001> <0058>\n<0002> <0042>\nendbfchar");
        string child = ToUnicode("1 beginbfchar\n<0001> <0041>\nendbfchar", entries: "/UseCMap 7 0 R");
        using PdfDocument document = OpenType0(child, extra: parent);
        PdfFont font = FontPdf.Font(document);

        Assert.Equal("A", Map(font, 1).Text);
        Assert.Equal("B", Map(font, 2).Text);
        Assert.Empty(ToUnicodeCodes(document));
    }

    [Fact]
    public void Overlapping_entries_take_the_later_one()
    {
        using PdfDocument document = OpenType0(ToUnicode("1 beginbfrange\n<0001> <0003> <0061>\nendbfrange\n1 beginbfchar\n<0002> <0058>\nendbfchar"));
        PdfFont font = FontPdf.Font(document);

        Assert.Equal(["a", "X", "c"], [Map(font, 1).Text, Map(font, 2).Text, Map(font, 3).Text]);
    }

    [Fact]
    public void Strict_mode_throws_for_a_malformed_ToUnicode_CMap_but_not_for_an_unmapped_code()
    {
        using PdfDocument malformed = OpenType0(ToUnicode("1 beginbfchar\n<0001> <41>\nendbfchar"), new PdfOptions().UseStrict());
        DiagnosticException thrown = Assert.Throws<DiagnosticException>(() => FontPdf.Font(malformed).GetUnicode(new CharacterCode(1, 2, IsValid: true)));
        Assert.Equal("ToUnicodeDestinationInvalid", thrown.Diagnostic.Code);

        using PdfDocument wellFormed = OpenType0(ToUnicode("1 beginbfchar\n<0001> <0041>\nendbfchar"), new PdfOptions().UseStrict());
        Assert.Equal(("�", UnicodeSource.Unmapped), Map(FontPdf.Font(wellFormed), 2));
    }

    [Fact]
    public void A_changed_ToUnicode_entry_is_read_again()
    {
        using PdfDocument document = OpenType0(ToUnicode("1 beginbfchar\n<0001> <0041>\nendbfchar"), extra: ToUnicode("1 beginbfchar\n<0001> <005A>\nendbfchar"));
        PdfFont font = FontPdf.Font(document);
        Assert.Equal("A", Map(font, 1).Text);

        font.Dictionary[new CosName("ToUnicode")] = new CosReference(7, 0);

        Assert.Equal("Z", Map(font, 1).Text);
    }

    [Fact]
    public void Concurrent_lookups_agree_and_record_one_diagnostic()
    {
        using PdfDocument document = OpenType0(ToUnicode("1 beginbfrange\n<0000> <00FF> <0041>\nendbfrange"));
        PdfFont font = FontPdf.Font(document);
        var results = new string[4096];

        Parallel.For(0, results.Length, index => results[index] = font.GetUnicode(new CharacterCode((uint)(index % 512), 2, IsValid: true)));

        for (int index = 0; index < results.Length; index++)
        {
            int code = index % 512;
            Assert.Equal(code <= 0xFF ? char.ConvertFromUtf32(0x41 + code) : "�", results[index]);
        }

        Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }

    /// <summary>A ToUnicode CMap stream with the given blocks.</summary>
    internal static string ToUnicode(string body, string codespace = "<0000> <FFFF>", string entries = "") =>
        Stream(
            $"/Type /CMap /CMapName /Test-UCS {entries}",
            "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Test-UCS def /CMapType 2 def\n"
            + $"1 begincodespacerange {codespace} endcodespacerange\n{body}\nendcmap CMapName currentdict /CMap defineresource pop end end");

    internal static string Stream(string entries, string data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream");

    /// <summary>A Type 0 font (object 4, Identity-H) over a CIDFontType0 without a program (object 5), ToUnicode object 6, extra objects from 7.</summary>
    internal static PdfDocument OpenType0(string? toUnicode, PdfOptions? options = null, string fontEntries = "/ToUnicode 6 0 R", string ordering = "Identity", string encoding = "/Identity-H", string? extra = null)
    {
        List<string> objects = [$"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /Test /CIDSystemInfo << /Registry (Adobe) /Ordering ({ordering}) /Supplement 0 >> >>"];
        objects.Add(toUnicode ?? "null");
        if (extra is not null)
        {
            objects.Add(extra);
        }

        return FontPdf.Open($"<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding {encoding} /DescendantFonts [5 0 R] {fontEntries} >>", options ?? new PdfOptions(), [.. objects]);
    }

    /// <summary>Non-embedded Helvetica with WinAnsiEncoding (object 4) and the given ToUnicode (object 5).</summary>
    internal static PdfDocument OpenSimple(string toUnicode) =>
        FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode 5 0 R >>", toUnicode);

    internal static (string Text, UnicodeSource Source) Map(PdfFont font, uint code, int length = 2)
    {
        Span<char> buffer = stackalloc char[512];
        int written = font.GetUnicode(new CharacterCode(code, length, IsValid: true), buffer, out UnicodeSource source);
        return (new string(buffer[..written]), source);
    }

    /// <summary>The distinct codes of the ToUnicode, CMap and text diagnostics, in order.</summary>
    internal static string[] ToUnicodeCodes(PdfDocument document) =>
        [.. document.Diagnostics
            .Select(diagnostic => diagnostic.Code)
            .Where(code => code.StartsWith("ToUnicode", StringComparison.Ordinal) || code.StartsWith("CMap", StringComparison.Ordinal))
            .Distinct()];
}
