using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The glyph name and width of each code of the simple-font corpus files. Expected widths are the AFM WX values of the Adobe Core
/// 14 files; expected names come from ISO 32000-2 Annex D. ISO 32000-2 §9.6.2.1, §9.6.2.2, §9.6.5.1, §9.8, Annex D.2, D.5, D.6.
/// </summary>
public sealed class SimpleFontCorpusTests
{
    [Fact]
    public void Differences_without_a_base_encoding_change_StandardEncoding_for_a_nonsymbolic_font()
    {
        using PdfDocument document = Open("text-standard14-differences.pdf");
        PdfSimpleFont font = Font(document, "F1");

        Assert.Equal(["quotesingle", "quoteleft", "Euro", "bullet", "Adieresis"], Names(font, 0x27, 0x60, 0x80, 0x81, 0xC8));
        Assert.Equal([191, 222, 556, 350, 667], Widths(font, 0x27, 0x60, 0x80, 0x81, 0xC8));
        Assert.Equal("A", font.GetGlyphName(0x41));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void WinAnsiEncoding_applies_the_notes_of_table_D2()
    {
        using PdfDocument document = Open("text-standard14-winansi-quirks.pdf");
        PdfSimpleFont font = Font(document, "F1");

        int[] codes = [0x27, 0x60, 0x7F, 0x81, 0x80, 0xA0, 0xAD, 0x8E];
        Assert.Equal(["quotesingle", "grave", "bullet", "bullet", "Euro", "space", "hyphen", "Zcaron"], Names(font, codes));
        Assert.Equal([191, 333, 350, 350, 556, 278, 333, 611], Widths(font, codes));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void MacRomanEncoding_is_table_D2_and_not_Mac_OS_Roman()
    {
        using PdfDocument document = Open("text-standard14-macroman.pdf");
        PdfSimpleFont font = Font(document, "F1");

        int[] codes = [0x80, 0xCA, 0xDB, 0xA5, 0xAD];
        Assert.Equal(["Adieresis", "space", "currency", "bullet", ".notdef"], Names(font, codes));
        Assert.Equal([722, 250, 500, 350, 250], Widths(font, codes));
        Assert.Equal(Standard14Font.TimesRoman, font.Standard14);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_Symbol_font_uses_its_built_in_encoding_and_ignores_a_named_encoding()
    {
        using PdfDocument document = Open("text-standard14-symbol.pdf");
        PdfSimpleFont plain = Font(document, "F1");
        PdfSimpleFont named = Font(document, "F2");

        foreach (PdfSimpleFont font in new[] { plain, named })
        {
            Assert.Equal(["alpha", "beta", "gamma", "Euro"], Names(font, 0x61, 0x62, 0x67, 0xA0));
            Assert.Equal([631, 549, 411, 750], Widths(font, 0x61, 0x62, 0x67, 0xA0));
            Assert.Equal(Standard14Font.Symbol, font.Standard14);
        }

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontEncodingIgnored", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal(named.Reference, diagnostic.ObjectReference);
    }

    [Fact]
    public void Differences_on_the_Symbol_font_apply_to_its_built_in_encoding()
    {
        using PdfDocument document = Open("text-standard14-symbol-differences.pdf");
        PdfSimpleFont font = Font(document, "F1");

        Assert.Equal(["alpha", "Gamma"], Names(font, 0x61, 0x42));
        Assert.Equal([631, 603], Widths(font, 0x61, 0x42));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_ZapfDingbats_font_encodes_the_codes_its_metrics_list_beyond_table_D6()
    {
        using PdfDocument document = Open("text-standard14-zapfdingbats.pdf");
        PdfSimpleFont font = Font(document, "F1");

        Assert.Equal(["a1", "a71", "a89", "a96"], Names(font, 0x21, 0x6C, 0x80, 0x8D));
        Assert.Equal([974, 791, 390, 334], Widths(font, 0x21, 0x6C, 0x80, 0x8D));
        Assert.Equal(".notdef", font.GetGlyphName(0x7F));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Widths_override_the_standard_14_metrics_and_other_codes_take_MissingWidth()
    {
        using PdfDocument document = Open("text-standard14-widths.pdf");
        PdfSimpleFont font = Font(document, "F1");

        Assert.Equal([500, 700, 777], Widths(font, 0x41, 0x42, 0x43));
        Assert.Equal(777, font.GetWidth(0x20));
        Assert.Equal(Standard14Font.Courier, font.Standard14);
        Assert.False(font.Descriptor!.IsSynthesized);
        Assert.Equal(777, font.Descriptor.MissingWidth);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_non_embedded_Arial_bold_takes_the_Helvetica_bold_metrics_with_an_information_diagnostic()
    {
        using PdfDocument document = Open("text-standard14-alias.pdf");
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(document.Pages[0].GetFont("F1"));

        Assert.Equal(Standard14Font.HelveticaBold, font.Standard14);
        Assert.Equal("Arial,Bold", font.BaseFont);
        Assert.Equal([722, 278, 333], Widths(font, 'H', 'i', '!'));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontStandard14Alias", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void A_symbolic_font_without_its_program_or_an_encoding_reads_names_from_StandardEncoding_with_a_diagnostic()
    {
        using PdfDocument document = Open("text-type1-symbolic-noencoding.pdf");
        PdfSimpleFont font = Font(document, "F1");

        Assert.Equal([600, 650, 700], Widths(font, 0x41, 0x42, 0x43));
        Assert.Equal(["A", "B", "C"], Names(font, 0x41, 0x42, 0x43));
        Assert.Null(font.Standard14);
        Assert.Equal(PdfFontFlags.Symbolic, font.Descriptor!.Flags);
        Assert.Equal(0, font.GetWidth(0x44));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FontBuiltInEncodingUnavailable", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void Strict_mode_does_not_reject_the_compatibility_choices_on_legal_files()
    {
        foreach (string name in new[] { "text-standard14-symbol.pdf", "text-standard14-alias.pdf", "text-type1-symbolic-noencoding.pdf" })
        {
            using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, name), new PdfOptions().UseStrict());
            foreach (string resource in new[] { "F1", "F2" })
            {
                if (document.Pages[0].GetFont(resource) is PdfSimpleFont font)
                {
                    _ = font.GetWidth(0x41);
                }
            }
        }
    }

    private static PdfDocument Open(string name) => PdfDocument.Open(Path.Combine(Corpus.Directory, name));

    private static PdfSimpleFont Font(PdfDocument document, string resource) =>
        Assert.IsAssignableFrom<PdfSimpleFont>(Assert.Single(document.Pages).GetFont(resource));

    private static string[] Names(PdfSimpleFont font, params int[] codes) => [.. codes.Select(code => font.GetGlyphName((byte)code))];

    private static double[] Widths(PdfSimpleFont font, params int[] codes) => [.. codes.Select(code => font.GetWidth((byte)code))];
}
