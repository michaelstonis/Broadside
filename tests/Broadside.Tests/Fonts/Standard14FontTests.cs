using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// Non-embedded Standard 14 fonts: glyph names from the encoding, widths from the AFM metrics. ISO 32000-2 §9.6.2.1 (Table 109
/// and the paragraph after it), §9.6.2.2, §9.6.5.
/// </summary>
public sealed class Standard14FontTests
{
    [Fact]
    public void Text_standard14_pdf_gives_each_code_its_glyph_name_and_its_Helvetica_width()
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, "text-standard14.pdf"));
        PdfPage page = Assert.Single(document.Pages);
        PdfType1Font font = Assert.IsType<PdfType1Font>(page.GetFont("F1"));

        string[] expectedNames = ["H", "e", "l", "l", "o", "comma", "space", "B", "r", "o", "a", "d", "s", "i", "d", "e"];
        double[] expectedWidths = [722, 556, 222, 222, 556, 278, 278, 667, 333, 556, 556, 556, 500, 222, 556, 556];
        byte[] codes = "Hello, Broadside"u8.ToArray();

        Assert.Equal(expectedNames, codes.Select(font.GetGlyphName));
        Assert.Equal(expectedWidths, codes.Select(font.GetWidth));
        Assert.Equal(7336, codes.Sum(font.GetWidth));
        Assert.Equal(Standard14Font.Helvetica, font.Standard14);
        Assert.Equal("Helvetica", font.BaseFont);
        Assert.Equal(PdfFontType.Type1, font.FontType);
        Assert.False(font.IsEmbedded);
        Assert.Empty(document.Diagnostics);
    }
}
