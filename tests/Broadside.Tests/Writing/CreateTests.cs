using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>Creating a document from scratch. ISO 32000-2 §7.5.2, §7.7.2, §7.7.3, §14.4.</summary>
public class CreateTests
{
    [Fact]
    public void A_created_document_has_one_empty_letter_page_and_is_PDF_2_0()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfPage page = Assert.Single(document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), page.MediaBox);
        Assert.Empty(Assert.IsType<CosDictionary>(page.Resources));
        Assert.False(page.Dictionary.ContainsKey(new CosName("Contents")));
        Assert.Equal(new PdfVersion(2, 0), document.Version);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_created_document_is_the_same_through_an_engine_and_in_strict_mode()
    {
        using PdfDocument document = new PdfEngine(new PdfOptions().UseStrict()).Create();

        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(document.Pages).MediaBox);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_saved_created_document_passes_qpdf_check()
    {
        using PdfDocument document = PdfDocument.Create();

        (int exitCode, string output) = ExternalTool.Run("qpdf", Saved(document), "--check");

        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void A_saved_created_document_opens_in_pdfinfo_as_one_letter_page()
    {
        using PdfDocument document = PdfDocument.Create();

        (int exitCode, string output) = ExternalTool.Run("pdfinfo", Saved(document));

        Assert.True(exitCode == 0, output);
        Assert.Matches(@"Pages:\s+1\n", output);
        Assert.Matches(@"Page size:\s+612 x 792 pts \(letter\)", output);
        Assert.Matches(@"PDF version:\s+2\.0", output);
    }

    [Fact]
    public void A_saved_created_document_reopens_with_two_equal_file_identifiers()
    {
        using PdfDocument document = PdfDocument.Create();

        using PdfDocument reopened = PdfDocument.Open(Saved(document));

        // §14.4: "When a PDF file is first written, both identifiers shall be set to the same value"; Table 15 requires ID in 2.0.
        var id = Assert.IsType<CosArray>(reopened.Trailer[new CosName("ID")]);
        Assert.Equal(id[0], id[1]);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), Assert.Single(reopened.Pages).MediaBox);
        Assert.Empty(reopened.Diagnostics);
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
