using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Non-embedded fonts in the display list (ISO 32000-2 §9.6.2.2, §9.2.4; ADR 0006, 0007, 0009): with the Standard 14 package and
/// the operating system's fonts switched off, their glyphs are drawn from the package's programs, each stretched to the width the
/// PDF gives it.
/// </summary>
public sealed class SubstituteGlyphTests
{
    [Theory]
    [InlineData("text-standard14.pdf", "Hello, Broadside")]
    [InlineData("text-standard14-symbol.pdf", null)]
    [InlineData("text-standard14-zapfdingbats.pdf", null)]
    public void Standard_14_text_is_drawn_from_the_package_without_system_fonts(string fileName, string? text)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.NotEmpty(recorder.Glyphs);
        Assert.All(recorder.Glyphs, glyph => Assert.NotNull(glyph.Program));
        Assert.All(recorder.Glyphs, glyph => Assert.NotEqual(0, glyph.GlyphId));
        if (text is not null)
        {
            // Every glyph but the space has an outline.
            Assert.Equal(text.Length, recorder.Glyphs.Count);
            Assert.Equal(text.Select(c => c != ' '), recorder.Glyphs.Select(glyph => glyph.Outline.Length > 0));
        }
        else
        {
            Assert.All(recorder.Glyphs, glyph => Assert.NotEmpty(glyph.Outline));
        }
    }

    [Fact]
    public void A_substituted_glyph_is_stretched_to_the_width_the_pdf_gives_it()
    {
        using PdfDocument document = PdfDocument.Open(
            Corpus.Path("text-nonembedded-substitute.pdf"),
            new PdfOptions().UseStandard14Fonts().UseSystemFontResolver(null));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        // "Serif" in Widths S 611, others 500, drawn with Liberation Serif Italic, whose advances are Times-Italic's (AFM: S 500,
        // e 444, r 389, i 278, f 278).
        string line = Assert.Single(recorder.Operations);
        double[] scales = [.. line.Split(' ').Where(word => word.StartsWith('x')).Select(word => double.Parse(word[1..], System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Equal([611 / 500.0, 500 / 444.0, 500 / 389.0, 500 / 278.0, 500 / 278.0], scales, (a, b) => Math.Abs(a - b) < 0.01);
    }
}
