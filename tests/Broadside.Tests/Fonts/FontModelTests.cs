using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Font dictionaries and font descriptors as typed, live views: the type each <c>Subtype</c> selects, the descriptor's Table 120
/// entries and Table 121 flags, Standard 14 name matching, and the diagnostics of malformed dictionaries. ISO 32000-2 §9.5
/// Table 108, §9.6.1, §9.6.2, §9.8.
/// </summary>
public sealed class FontModelTests
{
    private const string Descriptor =
        "<< /Type /FontDescriptor /FontName /Plain /Flags 32 /FontBBox [ 0 -200 1000 800 ] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>";

    [Theory]
    [InlineData("Type1", typeof(PdfType1Font), PdfFontType.Type1)]
    [InlineData("MMType1", typeof(PdfType1Font), PdfFontType.MMType1)]
    [InlineData("TrueType", typeof(PdfTrueTypeFont), PdfFontType.TrueType)]
    [InlineData("Type3", typeof(PdfType3Font), PdfFontType.Type3)]
    [InlineData("Type0", typeof(PdfType0Font), PdfFontType.Type0)]
    public void The_subtype_selects_the_type_of_the_view(string subtype, Type expectedType, PdfFontType expectedFontType)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /{subtype} /BaseFont /Plain >>");
        PdfFont font = FontPdf.Font(document);

        Assert.IsType(expectedType, font);
        Assert.Equal(expectedFontType, font.FontType);
        Assert.Equal(new CosReference(4, 0), font.Reference);
        Assert.Same(document.Resolve(new CosReference(4, 0)), font.Dictionary);
        Assert.Equal("Plain", font.BaseFont);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_multiple_master_instance_is_a_type_1_font()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /MMType1 /BaseFont /MinionMM_366_465_11_ /FirstChar 32 /LastChar 32 /Widths [ 187 ] /FontDescriptor 5 0 R >>", Descriptor);
        PdfType1Font font = Assert.IsType<PdfType1Font>(FontPdf.Font(document));

        Assert.True(font.IsMultipleMaster);
        Assert.Equal(187, font.GetWidth(32));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Helvetica >>", "FontSubtypeInvalid")]
    [InlineData("<< /Type /Font /BaseFont /Helvetica >>", "FontSubtypeInvalid")]
    [InlineData("<< /Type /XObject /Subtype /Type1 /BaseFont /Helvetica >>", "FontTypeInvalid")]
    [InlineData("<< /Subtype /Type1 /BaseFont /Helvetica >>", "FontTypeInvalid")]
    public void A_dictionary_that_is_not_quite_a_font_is_read_as_a_type_1_font_with_a_diagnostic(string dictionary, string expectedCode)
    {
        using PdfDocument document = FontPdf.Open(dictionary);
        PdfType1Font font = Assert.IsType<PdfType1Font>(FontPdf.Font(document));

        Assert.Equal(PdfFontType.Type1, font.FontType);
        Assert.Equal(278, font.GetWidth((byte)' '));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(new CosReference(4, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void The_same_dictionary_gives_the_same_view_and_a_non_dictionary_none()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        PdfPage page = Assert.Single(document.Pages);

        Assert.Same(page.GetFont("F1"), page.GetFont("F1"));
        Assert.Same(page.GetFont("F1"), document.GetFont(new CosReference(4, 0)));
        Assert.Same(page.GetFont("F1"), document.GetFont(document.Resolve(new CosReference(4, 0))));
        Assert.Null(page.GetFont("F2"));
        Assert.Null(document.GetFont(new CosInteger(1)));
        Assert.Null(document.GetFont(null));
    }

    [Fact]
    public void The_descriptor_view_reads_every_entry_of_table_120()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /AGaramond-Semibold /FirstChar 32 /LastChar 32 /Widths [ 255 ] /FontDescriptor 5 0 R >>",
            "<< /Type /FontDescriptor /FontName /AGaramond-Semibold /FontFamily (Adobe Garamond) /FontStretch /SemiCondensed /FontWeight 600 /Flags 262178 /FontBBox [ 1123 866 -177 -269 ] /MissingWidth 255 /StemV 105 /StemH 45 /CapHeight 660 /XHeight 394 /Ascent 720 /Descent -270 /Leading 83 /MaxWidth 1212 /AvgWidth 478 /ItalicAngle -12.5 /CharSet (/a/b) /FontFile2 6 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream");
        PdfFont font = FontPdf.Font(document);
        PdfFontDescriptor descriptor = font.Descriptor!;

        Assert.False(descriptor.IsSynthesized);
        Assert.Equal(new CosReference(5, 0), descriptor.Reference);
        Assert.Equal("AGaramond-Semibold", descriptor.FontName);
        Assert.Equal("Adobe Garamond", descriptor.FontFamily);
        Assert.Equal(PdfFontStretch.SemiCondensed, descriptor.FontStretch);
        Assert.Equal(600, descriptor.FontWeight);
        Assert.Equal(PdfFontFlags.Serif | PdfFontFlags.Nonsymbolic | PdfFontFlags.ForceBold, descriptor.Flags);
        Assert.Equal(new PdfRectangle(-177, -269, 1123, 866), descriptor.FontBBox);
        Assert.Equal(-12.5, descriptor.ItalicAngle);
        Assert.Equal([720, -270, 83, 660, 394, 105, 45, 478, 1212, 255], new[]
        {
            descriptor.Ascent, descriptor.Descent, descriptor.Leading, descriptor.CapHeight, descriptor.XHeight, descriptor.StemV,
            descriptor.StemH, descriptor.AvgWidth, descriptor.MaxWidth, descriptor.MissingWidth,
        });
        Assert.Equal("/a/b", descriptor.CharSet);
        Assert.NotNull(descriptor.FontFile2);
        Assert.Null(descriptor.FontFile);
        Assert.Null(descriptor.FontFile3);
        Assert.True(font.IsEmbedded);
        Assert.Same(descriptor, font.Descriptor);
    }

    [Fact]
    public void Optional_descriptor_entries_default_and_the_view_follows_changes()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /BaseFont /Plain /FirstChar 32 /LastChar 32 /Widths [ 250 ] /FontDescriptor 5 0 R >>", Descriptor);
        PdfFontDescriptor descriptor = FontPdf.Font(document).Descriptor!;

        Assert.Null(descriptor.FontFamily);
        Assert.Null(descriptor.FontStretch);
        Assert.Null(descriptor.FontWeight);
        Assert.Null(descriptor.CharSet);
        Assert.Equal(0, descriptor.Leading + descriptor.XHeight + descriptor.StemH + descriptor.AvgWidth + descriptor.MaxWidth + descriptor.MissingWidth);
        Assert.False(FontPdf.Font(document).IsEmbedded);

        descriptor.Dictionary![new CosName("FontWeight")] = new CosReal(350);
        descriptor.Dictionary[new CosName("FontStretch")] = new CosName("Wide");
        descriptor.Dictionary[new CosName("XHeight")] = new CosInteger(450);
        Assert.Null(descriptor.FontWeight);
        Assert.Null(descriptor.FontStretch);
        Assert.Equal(450, descriptor.XHeight);
    }

    [Fact]
    public void A_descriptor_missing_required_entries_or_with_two_programs_is_reported_once()
    {
        using PdfDocument document = FontPdf.Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Plain /FirstChar 32 /LastChar 32 /Widths [ 250 ] /FontDescriptor 5 0 R >>",
            "<< /FontName /Plain /Flags 4.5 /FontFile 6 0 R /FontFile3 6 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(250, font.GetWidth(32));
        Assert.Equal(PdfFontFlags.None, font.Descriptor!.Flags);
        Assert.Equal(default, font.Descriptor.FontBBox);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "FontDescriptorInvalid");
        Assert.Contains("Type", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("FontBBox", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("FontFile", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_simple_font_that_is_not_a_standard_14_font_needs_a_descriptor_and_a_base_font()
    {
        using PdfDocument document = FontPdf.Open("<< /Type /Font /Subtype /Type1 /FirstChar 32 /LastChar 32 /Widths [ 250 ] >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Equal(250, font.GetWidth(32));
        Assert.Null(font.Descriptor);
        Assert.Null(font.BaseFont);
        Assert.Equal(["FontBaseFontMissing", "FontDescriptorMissing"], FontPdf.Codes(document));
    }

    [Theory]
    [InlineData("Helvetica", Standard14Font.Helvetica, false)]
    [InlineData("Times-BoldItalic", Standard14Font.TimesBoldItalic, false)]
    [InlineData("ZapfDingbats", Standard14Font.ZapfDingbats, false)]
    [InlineData("TimesNewRomanPS-BoldItalicMT", Standard14Font.TimesBoldItalic, true)]
    [InlineData("TimesNewRoman,Italic", Standard14Font.TimesItalic, true)]
    [InlineData("Times,Bold", Standard14Font.TimesBold, true)]
    [InlineData("CourierNew", Standard14Font.Courier, true)]
    [InlineData("CourierNewPS-BoldItalicMT", Standard14Font.CourierBoldOblique, true)]
    [InlineData("Arial-ItalicMT", Standard14Font.HelveticaOblique, true)]
    [InlineData("Helvetica-BoldItalic", Standard14Font.HelveticaBoldOblique, true)]
    [InlineData("Symbol,Bold", Standard14Font.Symbol, true)]
    [InlineData("helvetica", Standard14Font.Helvetica, true)]
    [InlineData("ABCDEF+Courier", Standard14Font.Courier, true)]
    public void A_non_embedded_font_named_after_a_standard_14_font_takes_its_metrics(string baseFont, Standard14Font expected, bool isAlias)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont.Replace(",", "#2C", StringComparison.Ordinal)} >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        string[] expectedCodes = isAlias ? ["FontStandard14Alias"] : [];
        Assert.Equal(expected, font.Standard14);
        Assert.Equal(expectedCodes, FontPdf.Codes(document));
    }

    [Theory]
    [InlineData("ArialNarrow")]
    [InlineData("Arial-Black")]
    [InlineData("Helvetica-Narrow")]
    [InlineData("ABCDE+Helvetica")]
    public void Fonts_whose_metrics_differ_from_the_standard_14_are_not_matched(string baseFont)
    {
        using PdfDocument document = FontPdf.Open($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>");
        PdfSimpleFont font = FontPdf.SimpleFont(document);

        Assert.Null(font.Standard14);
        Assert.Equal(0, font.GetWidth(65));
        Assert.Equal(["FontWidthsMissing", "FontDescriptorMissing"], FontPdf.Codes(document));
    }
}
