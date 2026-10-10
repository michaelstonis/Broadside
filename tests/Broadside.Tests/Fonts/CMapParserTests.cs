using System.Diagnostics;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Fonts;

namespace Broadside.Tests.Fonts;

#pragma warning disable IDE0230 // Codes are written as byte values, not as text.

/// <summary>
/// CMap files parsed stand-alone through <see cref="CMap.Parse(ReadOnlySpan{byte}, CMapContext)"/>, with vectors from Adobe TN 5014
/// (Figure 6, §5.2, §5.4, §7.4), TN 5099 §1.5, ISO 32000-2 §9.7.6.2-§9.7.6.3, and the cases FontBox's <c>TestCMapParser</c> and
/// pdf.js's <c>cmap_spec.js</c> cover (rewritten here, not copied).
/// </summary>
public class CMapParserTests
{
    /// <summary>The codespace of 90ms-RKSJ-H (ISO 32000-2 §9.7.6.3 example; TN 5014 Figure 6): one- and two-byte Shift-JIS.</summary>
    private const string ShiftJisCodespace = "4 begincodespacerange <00> <80> <8140> <9FFC> <A0> <DF> <E040> <FCFC> endcodespacerange\n";

    [Fact]
    public void Codes_match_a_codespace_range_byte_by_byte_not_as_numbers()
    {
        CMap cmap = Parse(ShiftJisCodespace);

        Assert.Equal(new CharacterCode(0x86A9, 2, IsValid: true), cmap.ReadCode([0x86, 0xA9]));
        Assert.Equal(new CharacterCode(0x41, 1, IsValid: true), cmap.ReadCode([0x41, 0x86, 0xA9]));
        Assert.Equal(new CharacterCode(0xB0, 1, IsValid: true), cmap.ReadCode([0xB0]));
        Assert.Equal(new CharacterCode(0xE040, 2, IsValid: true), cmap.ReadCode([0xE0, 0x40]));

        // 82 10 lies between 8140 and 9FFC as a number, but its second byte is below 40: invalid, and the partial match on the
        // first byte gives the two-byte length (§9.7.6.3 b).
        Assert.Equal(new CharacterCode(0x8210, 2, IsValid: false), cmap.ReadCode([0x82, 0x10, 0x41]));
    }

    [Fact]
    public void An_invalid_code_takes_the_length_of_the_best_partial_match_or_of_the_shortest_codes()
    {
        CMap cmap = Parse(ShiftJisCodespace);

        // FD matches the first byte of no range: the shortest codes (one byte) are used (§9.7.6.3 a), so 41 is read next.
        Assert.Equal(new CharacterCode(0xFD, 1, IsValid: false), cmap.ReadCode([0xFD, 0x41]));
        Assert.Equal(new CharacterCode(0x41, 1, IsValid: true), cmap.ReadCode([0x41]));

        // A lone lead byte at the end of the string: never more bytes than the string has.
        Assert.Equal(new CharacterCode(0x85, 1, IsValid: false), cmap.ReadCode([0x85]));
        Assert.Equal(default, cmap.ReadCode([]));
    }

    [Fact]
    public void One_byte_and_four_byte_codespaces_are_read_in_order_of_length()
    {
        CMap cmap = Parse("2 begincodespacerange <00> <7F> <8EA1A1A1> <8EA1FEFE> endcodespacerange 1 begincidrange <8EA1A1A1> <8EA1A1FE> 500 endcidrange");

        CharacterCode code = cmap.ReadCode([0x8E, 0xA1, 0xA1, 0xA3, 0x20]);

        Assert.Equal(new CharacterCode(0x8EA1A1A3, 4, IsValid: true), code);
        Assert.Equal(502, cmap.GetCid(code));
        Assert.Equal(new CharacterCode(0x20, 1, IsValid: true), cmap.ReadCode([0x20]));
    }

    [Fact]
    public void Character_mappings_cover_cidchar_cidrange_and_notdef_ranges()
    {
        CMap cmap = Parse(
            "1 begincodespacerange <0000> <FFFF> endcodespacerange\n" +
            "2 begincidrange <0000> <00FF> 0 <0300> <0300> 300 endcidrange\n" +
            "1 begincidchar <0208> 520 endcidchar\n" +
            "1 beginnotdefrange <1000> <10FF> 7 endnotdefrange\n" +
            "1 beginnotdefchar <2000> 8 endnotdefchar\n");

        Assert.Equal(65, Cid(cmap, 0x00, 0x41));
        Assert.Equal(520, Cid(cmap, 0x02, 0x08));
        Assert.Equal(300, Cid(cmap, 0x03, 0x00));
        Assert.Equal(7, Cid(cmap, 0x10, 0x80));
        Assert.Equal(8, Cid(cmap, 0x20, 0x00));
        Assert.Equal(0, Cid(cmap, 0x30, 0x00));
        Assert.False(cmap.TryGetCid(cmap.ReadCode([0x10, 0x80]), out _));
        Assert.True(cmap.TryGetNotdefCid(cmap.ReadCode([0x10, 0x80]), out int notdef));
        Assert.Equal(7, notdef);
    }

    [Fact]
    public void Where_mappings_overlap_the_later_one_wins()
    {
        CMap rangeFirst = Parse("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidrange <00> <FF> 1000 endcidrange 1 begincidchar <41> 5 endcidchar");
        CMap charFirst = Parse("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidchar <41> 5 endcidchar 1 begincidrange <00> <FF> 1000 endcidrange");
        CMap nested = Parse("1 begincodespacerange <00> <FF> endcodespacerange 3 begincidrange <00> <FF> 1000 <40> <4F> 2000 <44> <45> 3000 endcidrange");

        Assert.Equal([1064, 5, 1066], [Cid(rangeFirst, 0x40), Cid(rangeFirst, 0x41), Cid(rangeFirst, 0x42)]);
        Assert.Equal(1065, Cid(charFirst, 0x41));
        Assert.Equal([1063, 2000, 2003, 3000, 3001, 2006, 2015, 1080], new[] { 0x3F, 0x40, 0x43, 0x44, 0x45, 0x46, 0x4F, 0x50 }.Select(code => Cid(nested, (byte)code)));
    }

    [Fact]
    public void A_using_CMap_keeps_its_parent_and_its_own_mappings_win()
    {
        CMap cmap = Parse("/Identity-H usecmap\n1 begincidchar <0041> 7 endcidchar\n1 begincidrange <0100> <0101> 9000 endcidrange");

        Assert.Same(CMap.IdentityH, cmap.Parent);
        Assert.Equal(7, Cid(cmap, 0x00, 0x41));
        Assert.Equal(0x42, Cid(cmap, 0x00, 0x42));
        Assert.Equal(9001, Cid(cmap, 0x01, 0x01));
        Assert.Equal(new CharacterCode(0xFFFF, 2, IsValid: true), cmap.ReadCode([0xFF, 0xFF]));
    }

    [Fact]
    public void A_usecmap_naming_a_predefined_CMap_that_is_not_built_in_is_reported_as_unavailable_and_gives_only_its_codespace()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(Bytes("/90ms-RKSJ-H usecmap 1 begincidchar <41> 7 endcidchar"), context);

        Assert.Equal("90ms-RKSJ-H", cmap.Parent?.Name);
        Assert.Equal((2, 0), (cmap.ReadCode([0x93, 0xFA]).Length, cmap.GetCid(cmap.ReadCode([0x93, 0xFA]))));
        Assert.Equal(7, cmap.GetCid(cmap.ReadCode("A"u8)));
        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal(("CMapUnavailable", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void The_name_writing_mode_and_system_info_come_from_the_file_in_both_dictionary_forms()
    {
        CMap inline = Parse("/CIDSystemInfo 3 dict dup begin /Registry (Adobe) def /Ordering (Japan1) def /Supplement 6 def end def\n/CMapName /Test-V def /WMode 1 def\n1 begincodespacerange <00> <FF> endcodespacerange");
        CMap direct = Parse("/CIDSystemInfo << /Registry (Adobe) /Ordering (Korea1) /Supplement 2 >> def /CMapName /Test-H def");
        CMap array = Parse("/CIDSystemInfo [<< /Registry (Adobe) /Ordering (GB1) /Supplement 5 >> << /Registry (X) /Ordering (Y) /Supplement 0 >>] def");

        Assert.Equal(("Test-V", WritingMode.Vertical, new CidSystemInfo("Adobe", "Japan1", 6)), (inline.Name, inline.WritingMode, inline.SystemInfo));
        Assert.Equal(("Test-H", WritingMode.Horizontal, new CidSystemInfo("Adobe", "Korea1", 2)), (direct.Name, direct.WritingMode, direct.SystemInfo));
        Assert.Equal(new CidSystemInfo("Adobe", "GB1", 5), array.SystemInfo);
        Assert.True(new CidSystemInfo("Adobe", "Japan1", 2).IsCompatibleWith(new CidSystemInfo("Adobe", "Japan1", 6)));
        Assert.False(new CidSystemInfo("Adobe", "Japan1", 2).IsCompatibleWith(new CidSystemInfo("Adobe", "GB1", 2)));
    }

    [Theory]
    [InlineData("Identity-H", 0)]
    [InlineData("Identity-V", 1)]
    public void The_Adobe_Identity_resources_parse_to_the_same_mapping_as_the_built_in_CMaps(string name, int writingMode)
    {
        CMap parsed = Parse(AdobeIdentityResource(name, writingMode));
        CMap builtIn = name == "Identity-H" ? CMap.IdentityH : CMap.IdentityV;
        Span<byte> code = stackalloc byte[2];

        Assert.Equal((builtIn.Name, builtIn.WritingMode, builtIn.SystemInfo), (parsed.Name, parsed.WritingMode, parsed.SystemInfo));
        for (int value = 0; value <= 0xFFFF; value++)
        {
            code[0] = (byte)(value >> 8);
            code[1] = (byte)value;
            CharacterCode read = parsed.ReadCode(code);
            Assert.Equal(builtIn.ReadCode(code), read);
            Assert.Equal(value, parsed.GetCid(read));
            Assert.Equal(value, builtIn.GetCid(read));
        }
    }

    [Fact]
    public void Operators_glued_to_the_next_token_are_split_with_a_diagnostic()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(Bytes("/CMapName /Glued def1 begincodespacerange <00> <FF> endcodespacerange1 begincidchar <41> 66 endcidcharendcmap"), context);

        Assert.Equal("Glued", cmap.Name);
        Assert.Equal(66, Cid(cmap, 0x41));
        Assert.Equal(["CMapSyntaxInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Block_counts_are_not_trusted_and_a_block_without_its_end_closes_at_the_next_begin()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(
            Bytes("1 begincodespacerange <00> <FF> endcodespacerange 1 begincidchar <41> 1 <42> 2 <43> 3 1 begincidchar <44> 4 endcidchar"),
            context);

        Assert.Equal([1, 2, 3, 4], new byte[] { 0x41, 0x42, 0x43, 0x44 }.Select(code => Cid(cmap, code)));
        Assert.Equal(["CMapSyntaxInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Malformed_entries_are_dropped_with_diagnostics_and_the_rest_is_kept()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(
            Bytes(
                "1 begincodespacerange <00> <FF> <0102030405> <0102030406> endcodespacerange\n" +
                "1 begincidchar <41> 70000 <42> -1 <43> /Name <44> 4 <45> 5.7 endcidchar\n" +
                "1 begincidrange <50> <4F> 9 <0060> <61> 6 endcidrange"),
            context);

        Assert.Equal([0, 0, 0, 4, 5, 0, 6, 7], new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x50, 0x60, 0x61 }.Select(code => Cid(cmap, code)));
        Assert.Equal(["CMapEntryInvalid", "CMapSyntaxInvalid"], context.Diagnostics.Select(diagnostic => diagnostic.Code).Order());
    }

    [Fact]
    public void Bf_operators_in_an_encoding_CMap_map_to_CIDs_with_a_diagnostic()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(
            Bytes("1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfchar <0001> <0030> endbfchar 2 beginbfrange <0002> <0003> <0040> <0010> <0011> [<0100> <0200>] endbfrange"),
            context);

        Assert.Equal([0x30, 0x40, 0x41, 0x100, 0x200], new[] { 1, 2, 3, 0x10, 0x11 }.Select(code => Cid(cmap, 0, (byte)code)));
        Assert.Equal(["CMapOperatorNotAllowed"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Rearranged_fonts_and_use_matrices_are_skipped_and_usefont_must_be_0()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(
            Bytes("1 usefont 1 begincodespacerange <00> <FF> endcodespacerange beginrearrangedfont 1 begincidchar <41> 9 endcidchar endrearrangedfont 1 begincidchar <42> 2 endcidchar"),
            context);

        Assert.Equal([0, 2], new byte[] { 0x41, 0x42 }.Select(code => Cid(cmap, code)));
        Assert.Equal(["CMapOperatorNotAllowed"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_CMap_without_a_codespace_takes_the_lengths_its_mappings_use()
    {
        var context = new CMapContext();

        CMap cmap = CMap.Parse(Bytes("1 begincidchar <41> 3 endcidchar"), context);

        Assert.Equal(new CharacterCode(0x41, 1, IsValid: true), cmap.ReadCode([0x41, 0x42]));
        Assert.Equal(3, Cid(cmap, 0x41));
        Assert.Equal(["CMapCodespaceMissing"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(new CharacterCode(0x4142, 2, IsValid: true), Parse(string.Empty).ReadCode([0x41, 0x42]));
    }

    [Fact]
    public void Ranges_spanning_every_code_are_kept_as_ranges()
    {
        var stopwatch = Stopwatch.StartNew();

        CMap cmap = Parse("1 begincodespacerange <00000000> <FFFFFFFF> endcodespacerange 2 begincidrange <00000000> <FFFFFFFF> 0 <00000010> <FFFFFFF0> 5 endcidrange 1 beginnotdefrange <00000000> <FFFFFFFF> 1 endnotdefrange");

        Assert.Equal(5, Cid(cmap, 0, 0, 0, 0x10));
        Assert.Equal(15, Cid(cmap, 0, 0, 0, 0x0F));
        Assert.Equal(1, Cid(cmap, 0x7F, 0, 0, 0)); // past CID 65,535: the notdef mapping
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Entries_past_the_limit_are_dropped_with_a_diagnostic()
    {
        var context = new CMapContext { MaxEntries = 2 };

        CMap cmap = CMap.Parse(Bytes("1 begincodespacerange <00> <FF> endcodespacerange 2 begincidchar <41> 1 <42> 2 endcidchar"), context);

        Assert.Equal([1, 0], new byte[] { 0x41, 0x42 }.Select(code => Cid(cmap, code)));
        Assert.Equal(["CMapEntryLimitExceeded"], context.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Strict_mode_throws_for_the_first_deviation()
    {
        var context = new CMapContext { ReadingMode = PdfReadingMode.Strict };

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => CMap.Parse(Bytes("1 begincidchar <41> 70000 endcidchar"), context));

        Assert.Equal("CMapEntryInvalid", error.Diagnostic.Code);
    }

    [Fact]
    public void The_built_in_Identity_CMaps_read_two_byte_codes_as_CIDs()
    {
        Assert.Equal(("Identity-H", WritingMode.Horizontal, true), (CMap.IdentityH.Name, CMap.IdentityH.WritingMode, CMap.IdentityH.IsIdentity));
        Assert.Equal(("Identity-V", WritingMode.Vertical, true), (CMap.IdentityV.Name, CMap.IdentityV.WritingMode, CMap.IdentityV.IsIdentity));
        Assert.Equal(new CidSystemInfo("Adobe", "Identity", 0), CMap.IdentityH.SystemInfo);
        Assert.Equal(new CharacterCode(0x1234, 2, IsValid: true), CMap.IdentityH.ReadCode([0x12, 0x34, 0x56]));
        Assert.Equal(0x1234, CMap.IdentityV.GetCid(new CharacterCode(0x1234, 2, IsValid: true)));
        Assert.Equal(new CharacterCode(0x12, 1, IsValid: false), CMap.IdentityH.ReadCode([0x12]));
        Assert.Equal(0, CMap.IdentityH.GetCid(new CharacterCode(0x12, 1, IsValid: false)));
    }

    /// <summary>
    /// A CMap file laid out as Adobe's <c>cmap-resources</c> <c>Adobe-Identity-0/CMap/Identity-H</c> is (TN 5099 §1.5): one codespace
    /// range and 256 cidranges, one per high byte, in blocks of at most 100 entries.
    /// </summary>
    internal static string AdobeIdentityResource(string name, int writingMode)
    {
        var text = new StringBuilder();
        text.Append("%!PS-Adobe-3.0 Resource-CMap\n%%DocumentNeededResources: ProcSet (CIDInit)\n%%IncludeResource: ProcSet (CIDInit)\n");
        text.Append(CultureInfo.InvariantCulture, $"%%BeginResource: CMap ({name})\n/CIDInit /ProcSet findresource begin\n\n12 dict begin\n\nbegincmap\n\n");
        text.Append("/CIDSystemInfo 3 dict dup begin\n  /Registry (Adobe) def\n  /Ordering (Identity) def\n  /Supplement 0 def\nend def\n\n");
        text.Append(CultureInfo.InvariantCulture, $"/CMapName /{name} def\n/CMapVersion 1.006 def\n/CMapType 1 def\n\n/XUID [1 10 25404 9999] def\n\n/WMode {writingMode} def\n\n");
        text.Append("1 begincodespacerange\n  <0000> <FFFF>\nendcodespacerange\n");
        for (int block = 0; block < 256; block += 100)
        {
            int count = Math.Min(100, 256 - block);
            text.Append(CultureInfo.InvariantCulture, $"\n{count} begincidrange\n");
            for (int high = block; high < block + count; high++)
            {
                text.Append(CultureInfo.InvariantCulture, $"<{high:x2}00> <{high:x2}ff> {high * 256}\n");
            }

            text.Append("endcidrange\n");
        }

        text.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n\n%%EndResource\n%%EOF\n");
        return text.ToString();
    }

    private static CMap Parse(string text) => CMap.Parse(Bytes(text));

    private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

    private static int Cid(CMap cmap, params byte[] text)
    {
        CharacterCode code = cmap.ReadCode(text);
        Assert.Equal(text.Length, code.Length);
        return cmap.GetCid(code);
    }
}
