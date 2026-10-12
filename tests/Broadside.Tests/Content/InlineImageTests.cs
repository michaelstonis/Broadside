using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Inline images (ISO 32000-2 §8.9.7): one image event with the dictionary as written and the data between <c>ID</c> and
/// <c>EI</c>, found without tokenizing it: by <c>L</c>, by the computed length of unfiltered data, by the first filter's end of data,
/// or by an <c>EI</c> followed by content operators (issue #60's finder, through the interpreter here). The scan vectors are
/// PDFBox's (PDFStreamParserTest, Apache-2.0).
/// </summary>
public sealed class InlineImageTests
{
    public static TheoryData<string, string> DataVectors => new()
    {
        // Abbreviated keys and names (Tables 91, 92), unfiltered: the computed length 3.
        { "BI /W 1 /H 1 /CS /RGB /BPC 8 /I true /D [1 0 1 0 1 0] ID \u0001\u0002\u0003 EI Q", "\u0001\u0002\u0003" },

        // EI inside unfiltered data: the computed length (8 x 1 x 8 bits) wins over the first EI.
        { "BI /W 8 /H 1 /CS /G /BPC 8 ID aEI Qbcd EI Q", "aEI Qbcd" },

        // L (PDF 2.0) points past an EI inside filtered data.
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /Fl /L 10 ID xx EI Q yy EI Q", "xx EI Q yy" },

        // ASCII85 and ASCIIHex data may follow ID after more than one white-space (kept in the data, which the decoders skip); their EOD ends them.
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /A85 ID\n\n  !!~> EI Q", "\n  !!~>" },
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F [/AHx /Fl] ID   0 0 F F > EI Q", "  0 0 F F >" },

        // A JPEG's EOI marker ends DCT data, whatever bytes its entropy-coded segment holds.
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /DCT ID ÿØÿà\u0000\u0004abÿÚ\u0000\u0002EI QÿÙ EI Q", "ÿØÿà\u0000\u0004abÿÚ\u0000\u0002EI QÿÙ" },

        // The scan (no L, filtered): an EI followed by white-space and an operator.
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /Fl ID\n12345EI Q", "12345" },
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /Fl ID\n12EI5EI Q", "12EI5" },
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /Fl ID\n12EI5EIQEI Q", "12EI5EIQ" },
        { "BI /W 1 /H 1 /CS /G /BPC 8 /F /Fl ID\nab EI 7 EI 1 0 0 1 0 0 cm Q", "ab EI 7" },
    };

    [Fact]
    public void The_inline_image_of_inline_image_pdf_is_one_image_event()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("inline-image.pdf"));
        var images = new ImageRecorder();

        document.Pages[0].ProcessContent(images);

        (bool inline, string name, bool stencil, Matrix ctm, int? structParent, _) = Assert.Single(images.Images);
        Assert.True(inline);
        Assert.Equal(string.Empty, name);
        Assert.False(stencil);
        Assert.Equal(new Matrix(100, 0, 0, 100, 72, 600), ctm);
        Assert.Null(structParent);
        (byte[] data, int entries) = Assert.Single(images.Inline);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0xFF, 0x00 }, data);
        Assert.Equal(4, entries);
        Assert.Equal((2, 2, false), Assert.Single(images.Models));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(DataVectors))]
    public void The_data_of_an_inline_image_ends_where_8_9_7_says(string content, string expected)
    {
        (byte[] data, string[] operators, var diagnostics) = Run(content);

        Assert.Equal(Encoding.Latin1.GetBytes(expected), data);
        Assert.Equal("BI", operators[0]);
        Assert.DoesNotContain("EI", operators);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_l_that_does_not_lead_to_ei_is_reported_and_the_end_is_found_otherwise()
    {
        (byte[] data, _, var diagnostics) = Run("BI /W 4 /H 1 /CS /G /BPC 8 /L 3 ID abcd EI Q");

        Assert.Equal("abcd"u8.ToArray(), data);
        Assert.Equal(["ContentInlineImageInvalid"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void An_image_mask_is_a_stencil_and_forbidden_filters_are_reported()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("BI /IM true /W 8 /H 1 /F /JPXDecode ID ª EI"));
        var images = new ImageRecorder();

        document.Pages[0].ProcessContent(images);

        Assert.True(Assert.Single(images.Images).Stencil);
        Assert.Equal(["InlineImageFilterNotAllowed"], ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void An_inline_image_without_l_in_a_pdf_2_0_file_is_noted()
    {
        byte[] file = ContentPdf.Build("BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI");
        file = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(file).Replace("%PDF-1.7", "%PDF-2.0", StringComparison.Ordinal));
        using PdfDocument document = PdfDocument.Open(file);

        document.Pages[0].ProcessContent(new ImageRecorder());

        Diagnostics.Diagnostic noted = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Content", StringComparison.Ordinal));
        Assert.Equal("ContentInlineImageLengthMissing", noted.Code);
        Assert.Equal(Diagnostics.DiagnosticSeverity.Information, noted.Severity);
    }

    private static (byte[] Data, string[] Operators, IReadOnlyList<Diagnostics.Diagnostic> Diagnostics) Run(string content)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q " + content));
        var images = new ImageRecorder();
        var operators = new RecordingProcessor(ContentEvents.Operators);
        document.Pages[0].ProcessContent(new CompositeContentProcessor(images, operators));
        string[] keywords = [.. operators.Body.Skip(1).Select(line => line.Split(' ')[^1])];
        return (Assert.Single(images.Inline).Data, keywords, document.Diagnostics);
    }
}
