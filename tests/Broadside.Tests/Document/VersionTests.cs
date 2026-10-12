using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Tests.Document;

/// <summary>The document's effective version: the header's, overridden by the catalog's Version when later. ISO 32000-2 §7.5.2, §7.7.2 Table 29.</summary>
public sealed class VersionTests
{
    [Theory]
    [InlineData("%PDF-1.4", "/Version /1.7", "1.7")]
    [InlineData("%PDF-1.7", "/Version /1.4", "1.7")]
    [InlineData("%PDF-1.7", "/Version /2.0", "2.0")]
    [InlineData("%PDF-2.0", "", "2.0")]
    [InlineData("%PDF-1.0", "", "1.0")]
    public void The_catalog_version_overrides_the_header_only_when_later(string header, string catalogEntries, string expected)
    {
        // Table 15: a PDF 2.0 file's trailer shall have an ID, so every variant carries one.
        byte[] file = new TestPdf { Header = header, TrailerEntries = "/ID [<0123> <0123>]" }.Build(
            $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(expected, document.Version.ToString());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Versions_compare_numerically_on_major_then_minor()
    {
        Assert.True(new PdfVersion(1, 10) > new PdfVersion(1, 9));
        Assert.True(new PdfVersion(2, 0) > new PdfVersion(1, 7));
        Assert.True(new PdfVersion(1, 4) <= new PdfVersion(1, 4));
        Assert.NotEqual(new PdfVersion(1, 7), new PdfVersion(1, 4));
    }

    [Fact]
    public void A_catalog_version_written_as_a_number_is_read_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Version 1.7", "%PDF-1.4"));

        Assert.Equal(new PdfVersion(1, 7), document.Version);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("CatalogVersionNotName", diagnostic.Code);
        Assert.Equal(new CosReference(1, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void A_malformed_catalog_version_is_ignored_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Version /latest", "%PDF-1.6"));

        Assert.Equal(new PdfVersion(1, 6), document.Version);
        Assert.Equal("CatalogVersionInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_header_without_a_valid_version_takes_the_catalog_version_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Version /1.5", "%PDF-x.y"));

        Assert.Equal(new PdfVersion(1, 5), document.Version);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("HeaderVersionInvalid", diagnostic.Code);
        Assert.Equal(0, diagnostic.Offset);
    }

    [Fact]
    public void The_version_is_read_from_the_catalog_on_every_call()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));

        document.Catalog[new CosName("Version")] = new CosName("1.6");

        Assert.Equal(new PdfVersion(1, 6), document.Version);
    }
}
