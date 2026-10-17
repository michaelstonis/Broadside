using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Forms;

/// <summary>
/// Every text field of the pdf.js form files filled with a Latin value: the generated appearances show it, the document saves and
/// the saved file re-reads every value. Skips when the corpus is not fetched. ISO 32000-2 §12.7.4.3, §12.7.5.3.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldTextFieldFillTests(ITestOutputHelper output)
{
    private const string Value = "Broadside 123";

    [Fact]
    public void Every_text_field_of_the_pdfjs_forms_fills_saves_and_re_reads()
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files("pdfjs");
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'pdfjs' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var options = new PdfOptions().UseSystemFontResolver(null);
        int forms = 0;
        int filled = 0;
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            int count = FillFile(file, options);
            forms += count > 0 ? 1 : 0;
            filled += count;
        }

        output.WriteLine($"{forms} forms, {filled} text fields filled");
        Assert.True(filled >= 100, $"Only {filled} text fields were filled; the corpus looks incomplete.");
    }

    /// <summary>Fills every text field of one file, checks each appearance, saves and re-reads the values; returns how many were filled.</summary>
    private static int FillFile(string file, PdfOptions options)
    {
        using PdfDocument? document = OpenOrNull(file, options);
        if (document is null || document.IsEncrypted || document.AcroForm is not { } form)
        {
            return 0;
        }

        var expected = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (PdfTextField field in form.TerminalFields.OfType<PdfTextField>())
        {
            string value = field.MaxLength is int length && length >= 0 && length < Value.Length ? Value[..length] : Value;
            field.SetValue(value);
            expected[field.FullyQualifiedName] = expected.ContainsKey(field.FullyQualifiedName) || field.IsPassword ? null : value;
            foreach (PdfWidgetAnnotation widget in field.Widgets)
            {
                var extractor = new TextExtractor();
                if (widget.ProcessAppearance(widget.GetAppearance()!, extractor) && !field.IsPassword && !field.IsComb && !field.IsMultiline)
                {
                    Assert.True(extractor.Text.SequenceEqual(value), $"{file} {field.FullyQualifiedName}: '{extractor.Text}'");
                }
            }
        }

        if (expected.Count == 0)
        {
            return 0;
        }

        using var stream = new MemoryStream();
        document.Save(stream);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray(), options);
        foreach (PdfTextField field in reopened.AcroForm!.TerminalFields.OfType<PdfTextField>())
        {
            if (expected.GetValueOrDefault(field.FullyQualifiedName) is { } value)
            {
                Assert.True(value == field.Value, $"{file} {field.FullyQualifiedName}: '{field.Value}'");
            }
        }

        return expected.Count;
    }

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
