using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// ISO 32000-2 §9.10: every text corpus file maps to the text it was written with (the strings and names in generate.py, and
/// what poppler's pdftotext prints where it agrees), read through <see cref="Content.GlyphEvent.Unicode"/>.
/// </summary>
public sealed class UnicodeCorpusTests
{
    private const string Replacement = "�";

    /// <summary>File, then the text of each page.</summary>
    public static TheoryData<string, string[]> KnownText => new()
    {
        { "text-standard14.pdf", ["Hello, Broadside"] },
        { "text-truetype-embedded.pdf", ["HI"] },
        { "flate-stream.pdf", ["FlateDecode"] },
        { "lzw-stream.pdf", ["LZWDecode"] },
        { "ascii85-stream.pdf", ["ASCII85Decode"] },
        { "asciihex-stream.pdf", ["ASCIIHexDecode"] },
        { "runlength-stream.pdf", ["RunLengthDecode"] },
        { "filter-chain.pdf", ["ASCII85 then Flate"] },
        { "encrypted-rc4-40.pdf", ["RC4 40-bit (R2)"] },
        { "encrypted-rc4-128.pdf", ["RC4 128-bit (R3)"] },
        { "encrypted-aes-128.pdf", ["AES-128 (R4)"] },
        { "encrypted-aes-256.pdf", ["AES-256 (R6)"] },
        { "page-tree-inherited.pdf", ["Page 1", "Page 2"] },
        { "wrong-stream-length.pdf", ["Length is wrong"] },
        { "text-standard14-differences.pdf", ["'‘€•Ä"] },
        { "text-standard14-winansi-quirks.pdf", ["'`••€ -Ž"] },
        { "text-standard14-symbol.pdf", ["αβγ€αβγ€"] },
        { "text-standard14-symbol-differences.pdf", ["αΓ"] },
        { "text-standard14-zapfdingbats.pdf", ["✁●❨❵"] },
        { "text-standard14-widths.pdf", ["ABC"] },
        { "text-standard14-alias.pdf", ["Hi!"] },
        { "text-nonembedded-substitute.pdf", ["Serif"] },
        { "text-type1-symbolic-noencoding.pdf", ["ABC"] },
        { "text-truetype-composite.pdf", ["Ioc0123456789:;<"] },
        { "text-truetype-symbolic.pdf", ["ABCD"] },
        { "text-truetype-loca-long.pdf", ["HI"] },
        { "text-cff-embedded.pdf", ["HIOÁAS"] },
        { "text-opentype-cff-embedded.pdf", ["HIOÁAS´"] },
        { "text-type1-embedded.pdf", ["HHIOFETAÁ"] },
        { "text-type1-pfb.pdf", ["HHIOFETAÁ"] },
        { "text-type1-hex-eexec.pdf", ["HHIOFETAÁ"] },
        { "text-type1-bad-lengths.pdf", ["HHIOFETAÁ"] },
        { "text-cid-identity-h.pdf", ["H I"] },
        { "text-cid-identity-v.pdf", ["HI "] },
        { "text-cid-embedded-cmap.pdf", ["HIIH" + Replacement] },
        { "text-tounicode-bf.pdf", ["Hfi\U0001F600ABC"] },
        { "text-glyph-names.pdf", ["HIfi\U0001F600ABĻ"] },
        { "text-type3.pdf", ["■▲●■▲●"] },
        { "text-type3-recursive.pdf", ["arn"] },
        { "text-standard14-macroman.pdf", ["Ä ¤•" + Replacement] },
        { "text-truetype-macroman.pdf", ["€é" + Replacement] },
        { "text-cidcff-predefined-cmap.pdf", [Replacement + Replacement + Replacement] },
    };

    [Theory]
    [MemberData(nameof(KnownText))]
    public void Every_text_corpus_file_maps_to_its_known_text(string file, string[] pages)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(file));

        string[] text = UnicodeRecorder.PagesOf(document);

        Assert.Equal(pages, text);
    }

    [Fact]
    public void Text_tounicode_bf_maps_bfchar_bfrange_arrays_increments_and_a_surrogate_pair_through_the_ToUnicode_CMap()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-tounicode-bf.pdf"));

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        Assert.Equal(["H", "fi", "\U0001F600", "A", "B", "C"], recorder.Glyphs.Select(glyph => glyph.Text));
        Assert.All(recorder.Glyphs, glyph => Assert.Equal(UnicodeSource.ToUnicode, glyph.Source));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Text_glyph_names_maps_each_name_by_the_Adobe_Glyph_List_algorithm_without_warnings()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-glyph-names.pdf"));

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        // H.sc (suffix dropped), uni0049, f_i (two components, not U+FB01), u1F600, uni00410042 (two groups), Lcommaaccent.
        Assert.Equal(["H", "I", "fi", "\U0001F600", "AB", "Ļ"], recorder.Glyphs.Select(glyph => glyph.Text));
        Assert.All(recorder.Glyphs, glyph => Assert.Equal(UnicodeSource.GlyphName, glyph.Source));
        Assert.Equal(["FontGlyphMissing"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_simple_font_without_ToUnicode_maps_through_its_glyph_names()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        Assert.All(recorder.Glyphs, glyph => Assert.Equal(UnicodeSource.GlyphName, glyph.Source));
    }

    [Fact]
    public void A_code_nothing_maps_is_U_FFFD_with_one_information_diagnostic_per_font()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cid-embedded-cmap.pdf"));

        UnicodeRecorder first = UnicodeRecorder.FirstPageOf(document);
        UnicodeRecorder second = UnicodeRecorder.FirstPageOf(document);

        // The invalid <8210> selects CID 0; the ToUnicode CMap does not map it, the collection is Adobe-Identity and CID 0 has
        // no code point in the program's cmap.
        Assert.Equal((Replacement, UnicodeSource.Unmapped), (first.Glyphs[^1].Text, first.Glyphs[^1].Source));
        Assert.Equal(first.Glyphs, second.Glyphs);
        Diagnostic unmapped = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
        Assert.Equal(DiagnosticSeverity.Information, unmapped.Severity);
        Assert.Equal(5, unmapped.ObjectReference?.ObjectNumber);
    }

    [Fact]
    public void Strict_mode_does_not_throw_for_an_unmapped_code()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-truetype-macroman.pdf"), new PdfOptions().UseStrict());

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        Assert.Equal(UnicodeSource.Unmapped, recorder.Glyphs[^1].Source);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }

    [Fact]
    public void Without_the_CMaps_package_a_Japan1_font_names_the_missing_UCS2_table_once_and_its_codes_are_U_FFFD()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-cidcff-predefined-cmap.pdf"));

        UnicodeRecorder recorder = UnicodeRecorder.FirstPageOf(document);

        Assert.All(recorder.Glyphs, glyph => Assert.Equal((Replacement, UnicodeSource.Unmapped), (glyph.Text, glyph.Source)));
        Diagnostic missing = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "TextUcs2CmapMissing");
        Assert.Equal(DiagnosticSeverity.Information, missing.Severity);
        Assert.Contains("Adobe-Japan1-UCS2", missing.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "TextUnicodeUnmapped");
    }
}
