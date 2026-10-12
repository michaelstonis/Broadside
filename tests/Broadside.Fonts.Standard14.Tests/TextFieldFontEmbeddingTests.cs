using System.Text.RegularExpressions;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// A text field value its default appearance font (Helvetica, WinAnsi) cannot encode is shown through an embedded subset of the
/// Liberation Sans program the Standard 14 package resolves. ISO 32000-2 §12.7.4.3, §9.7, §9.9.2, §9.10.3.
/// </summary>
public sealed partial class TextFieldFontEmbeddingTests
{
    private const string Value = "Ωμέγα Привет";

    [Fact]
    public void A_non_latin_value_renders_through_an_embedded_subset_font()
    {
        byte[] saved = Fill(Value);

        // Reopened without the package: the appearance needs nothing but what the file embeds.
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfWidgetAnnotation widget = ((PdfTextField)reopened.AcroForm!.FindField("person.first")!).Widgets[0];
        Assert.Equal(Value, ((PdfTextField)reopened.AcroForm.FindField("person.first")!).Value);

        var extractor = new TextExtractor();
        Assert.True(widget.ProcessAppearance(widget.GetAppearance()!, extractor));
        TextPage text = extractor.ToTextPage();
        Assert.Equal(Value, text.Text);

        var font = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font);
        Assert.Matches(SubsetName(), font.BaseFont);
        PdfCidFont descendant = font.DescendantFont!;
        Assert.True(descendant.IsEmbedded);
        FontProgram program = descendant.Program!;
        Assert.Equal(FontProgramFormat.TrueType, program.Format);
        var outline = new GlyphOutline();
        foreach (TextGlyph glyph in text.Glyphs)
        {
            if (text.GetGlyphText(glyph) is not " ")
            {
                Assert.Equal(GlyphOutlineStatus.Complete, program.GetOutline((int)glyph.CharacterCode, outline));
                Assert.False(outline.IsEmpty);
            }
        }

        // The subset keeps the drawn glyphs and little else: far smaller than the 400 KB program.
        Assert.InRange(descendant.Descriptor!.FontFile2!.EncodedData.Length, 1, 40_000);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);

        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Matches(@"[A-Z]{6}\+LiberationSans\s+CID TrueType\s+Identity-H\s+yes\s+yes\s+yes", output);

        // poppler draws the glyphs: ink inside the widget with the value, none without.
        Assert.True(DarkPixels(saved, widget.Rect) > 10);
        Assert.Equal(0, DarkPixels(Fill(null), widget.Rect));
    }

    [Fact]
    public void The_embedded_subset_is_the_same_bytes_on_every_run()
    {
        Assert.Equal(Fill(Value), Fill(Value));
    }

    private static byte[] Fill(string? value)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"), new PdfOptions().UseStandard14Fonts());
        ((PdfTextField)document.AcroForm!.FindField("person.first")!).SetValue(value);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Counts the pixels darker than mid-grey poppler draws inside <paramref name="rect"/> on page 1 at 72 dpi (binary PGM).</summary>
    private static int DarkPixels(byte[] file, PdfRectangle rect)
    {
        (int exitCode, byte[] image) = ExternalTool.RunBinary("pdftoppm", file, "-r", "72", "-gray", "-f", "1", "-l", "1");
        Assert.Equal(0, exitCode);
        int offset = 0;
        int width = 0;
        for (int field = 0; field < 4; field++)
        {
            while (char.IsWhiteSpace((char)image[offset]))
            {
                offset++;
            }

            int start = offset;
            while (!char.IsWhiteSpace((char)image[offset]))
            {
                offset++;
            }

            if (field == 1)
            {
                width = int.Parse(image.AsSpan(start, offset - start), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        offset++;
        int count = 0;
        for (int y = 792 - (int)rect.Top; y < 792 - (int)rect.Bottom; y++)
        {
            for (int x = (int)rect.Left; x < (int)rect.Right; x++)
            {
                count += image[offset + (y * width) + x] < 128 ? 1 : 0;
            }
        }

        return count;
    }

    [GeneratedRegex(@"^[A-Z]{6}\+LiberationSans$")]
    private static partial Regex SubsetName();
}
