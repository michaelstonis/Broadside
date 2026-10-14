using System.Text.RegularExpressions;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Fonts.Standard14.Tests;

/// <summary>
/// Fields created in a new form (issue #169) show what the form's Helvetica cannot through the same embedded subset as filled fields:
/// one Liberation Sans subset shared by a created text field and a created combo box. ISO 32000-2 §12.7.3, §12.7.4.3, §9.7, §9.9.2.
/// </summary>
public sealed partial class TextFieldFontEmbeddingTests
{
    [Fact]
    public void Created_fields_with_non_latin_text_render_through_one_embedded_subset()
    {
        var name = new PdfRectangle(72, 700, 300, 724);
        var city = new PdfRectangle(72, 650, 300, 674);
        byte[] saved;
        using (PdfDocument document = PdfDocument.Create(new PdfOptions().UseStandard14Fonts()))
        {
            PdfAcroForm form = document.CreateAcroForm();
            form.AddTextField("name", document.Pages[0], name).SetValue(Value);
            PdfComboBoxField combo = form.AddComboBox("city", document.Pages[0], city, [new PdfChoiceOption("msk", "Москва"), new PdfChoiceOption("ath", "Αθήνα")]);
            combo.SetValue("ath");
            using var stream = new MemoryStream();
            document.Save(stream);
            saved = stream.ToArray();
        }

        // Reopened without the package: the appearances need nothing but what the file embeds.
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(Value, ShownText(reopened, "name"));
        Assert.Equal("Αθήνα", ShownText(reopened, "city"));
        Assert.False(reopened.AcroForm!.NeedAppearances);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);

        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        (exitCode, output) = ExternalTool.Run("pdffonts", saved);
        Assert.Equal(0, exitCode);
        Assert.Single(EmbeddedSubset().Matches(output));
        Assert.True(DarkPixels(saved, name) > 10);
        Assert.True(DarkPixels(saved, city) > 10);
    }

    private static string ShownText(PdfDocument document, string field)
    {
        PdfWidgetAnnotation widget = ((PdfTerminalField)document.AcroForm!.FindField(field)!).Widgets[0];
        var extractor = new TextExtractor();
        Assert.True(widget.ProcessAppearance(widget.GetAppearance()!, extractor));
        TextPage text = extractor.ToTextPage();
        PdfType0Font font = Assert.IsType<PdfType0Font>(text.Glyphs[0].Font);
        Assert.True(font.DescendantFont!.IsEmbedded);
        return text.Text;
    }

    [GeneratedRegex(@"[A-Z]{6}\+LiberationSans\s+CID TrueType\s+Identity-H\s+yes\s+yes\s+yes")]
    private static partial Regex EmbeddedSubset();
}
