using System.Buffers;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>
/// Writing unencrypted wrapper documents: an encrypted PDF file embedded as the encrypted payload of an ordinary PDF 2.0 document
/// whose collection presents it, read back through <see cref="PdfDocument.EncryptedPayload"/> (ISO 32000-2 §7.6.7, Table 28).
/// </summary>
public sealed class UnencryptedWrapperWritingTests
{
    private static readonly DateTimeOffset Modified = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_wrapper_written_from_an_encrypted_document_reopens_to_the_payload_pages_and_text()
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");

        byte[] saved = Wrap(original);

        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        using PdfDocument wrapper = PdfDocument.Open(saved);
        Assert.False(wrapper.IsEncrypted);
        Assert.True(wrapper.IsUnencryptedWrapper);
        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        Assert.Equal(new CosName("Standard"), payload.CryptographicFilter);
        Assert.True(payload.IsCryptographicFilterAvailable);
        Assert.Equal(original, Decoded(payload));
        using PdfDocument expected = PdfDocument.Open(original);
        using PdfDocument opened = payload.Open();
        Assert.True(opened.IsEncrypted);
        Assert.Equal(expected.Pages.Count, opened.Pages.Count);
        for (int index = 0; index < expected.Pages.Count; index++)
        {
            Assert.Equal(expected.Pages[index].ExtractText().Text, opened.Pages[index].ExtractText().Text);
        }

        Assert.Empty(wrapper.Diagnostics);
        Assert.Empty(opened.Diagnostics);
    }

    [Fact]
    public void The_wrapper_is_laid_out_as_the_clause_requires()
    {
        byte[] saved = Wrap(Corpus.Bytes("encrypted-aes-256.pdf"));

        using PdfDocument wrapper = PdfDocument.Open(saved, new PdfOptions().UseStrict());
        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        Assert.Equal("Standard Protected PDF.pdf", payload.Name);
        Assert.Equal("Standard Protected PDF.pdf", payload.File.FileName);
        Assert.Equal(PdfFileRelationship.EncryptedPayload, payload.File.Relationship);
        Assert.Equal(new CosName("EncryptedPayload"), payload.Dictionary![new CosName("Type")]);
        Assert.Equal("application/pdf", payload.File.EmbeddedFile!.MediaType);
        Assert.NotNull(payload.File.EmbeddedFile.Parameters?.ModificationDate);
        Assert.Contains("Standard", payload.File.Description, StringComparison.Ordinal);
        Assert.Equal(payload.Name, wrapper.Collection!.InitialDocument);
        Assert.Single(wrapper.EmbeddedFiles);
        Assert.Same(payload.File.Dictionary, Assert.Single(wrapper.AssociatedFiles).Dictionary);
        Assert.Empty(wrapper.Diagnostics);
    }

    [Fact]
    public void The_wrapper_declares_pdf_2_0()
    {
        byte[] saved = Wrap(Corpus.Bytes("encrypted-aes-256.pdf"));

        Assert.StartsWith("%PDF-2.0", Encoding.ASCII.GetString(saved, 0, 8), StringComparison.Ordinal);
        using PdfDocument wrapper = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), wrapper.Version);
    }

    [Fact]
    public void Without_a_cover_the_wrapper_has_one_blank_page()
    {
        using PdfDocument wrapper = PdfDocument.Open(Wrap(Corpus.Bytes("encrypted-aes-256.pdf")));

        PdfPage page = Assert.Single(wrapper.Pages);
        Assert.Equal(string.Empty, page.ExtractText().Text.Trim());
    }

    [Fact]
    public void A_caller_supplied_cover_document_is_used_unchanged_as_the_wrapper_pages()
    {
        using PdfDocument cover = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"));
        string coverText = cover.Pages[0].ExtractText().Text;

        byte[] saved = Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), new PdfUnencryptedWrapperOptions().WithCover(cover));

        using PdfDocument wrapper = PdfDocument.Open(saved);
        Assert.Equal(cover.Pages.Count, wrapper.Pages.Count);
        Assert.Equal(coverText, wrapper.Pages[0].ExtractText().Text);
        Assert.Equal(cover.Pages[0].MediaBox, wrapper.Pages[0].MediaBox);
        Assert.Equal("Standard Protected PDF.pdf", Assert.Single(wrapper.EmbeddedFiles).Name);
        Assert.Equal(coverText, cover.Pages[0].ExtractText().Text);
        Assert.Empty(wrapper.Diagnostics);
    }

    [Fact]
    public void The_cryptographic_filter_name_version_and_description_can_be_given()
    {
        var options = new PdfUnencryptedWrapperOptions()
            .WithCryptographicFilter("AcmeCustomCrypto")
            .WithCryptographicFilterVersion("1.0")
            .WithName("AcmeCustomCrypto Protected PDF.pdf")
            .WithDescription("This embedded file is encrypted using the Acme Custom Crypto filter")
            .WithModificationDate(Modified);

        using PdfDocument wrapper = PdfDocument.Open(Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), options));

        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        Assert.Equal(new CosName("AcmeCustomCrypto"), payload.CryptographicFilter);
        Assert.Equal("1.0", payload.CryptographicFilterVersion);
        Assert.Equal("AcmeCustomCrypto Protected PDF.pdf", payload.Name);
        Assert.Equal("This embedded file is encrypted using the Acme Custom Crypto filter", payload.File.Description);
        Assert.Equal(Modified, payload.File.EmbeddedFile!.Parameters!.ModificationDate!.Value.Value);
        Assert.Equal("EncryptedPayloadFilterUnknown", Assert.Single(wrapper.Diagnostics).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("1..0")]
    [InlineData("1.0a")]
    [InlineData("-1")]
    public void A_version_that_is_not_integers_separated_by_periods_is_refused(string version)
    {
        Assert.Throws<ArgumentException>(() => new PdfUnencryptedWrapperOptions().WithCryptographicFilterVersion(version));
    }

    [Fact]
    public void Strict_mode_refuses_a_payload_that_is_not_encrypted()
    {
        var engine = new PdfEngine(new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => engine.CreateUnencryptedWrapper(
            Corpus.Bytes("embedded-files.pdf"),
            new PdfUnencryptedWrapperOptions().WithCryptographicFilter("Standard")));

        Assert.Equal("EncryptedPayloadNotEncrypted", exception.Diagnostic.Code);
    }

    [Fact]
    public void Strict_mode_accepts_an_encrypted_payload()
    {
        var engine = new PdfEngine(new PdfOptions().UseStrict());

        using PdfDocument wrapper = engine.CreateUnencryptedWrapper(Corpus.Bytes("encrypted-aes-256.pdf"));

        Assert.Empty(wrapper.Diagnostics);
        Assert.True(wrapper.IsUnencryptedWrapper);
    }

    [Fact]
    public void A_payload_that_is_not_encrypted_is_wrapped_and_reported_when_lenient()
    {
        using PdfDocument wrapper = PdfDocument.CreateUnencryptedWrapper(
            Corpus.Bytes("embedded-files.pdf"),
            new PdfUnencryptedWrapperOptions().WithCryptographicFilter("Standard"));

        Diagnostic diagnostic = Assert.Single(wrapper.Diagnostics);
        Assert.Equal("EncryptedPayloadNotEncrypted", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        using PdfDocument reopened = PdfDocument.Open(Save(wrapper));
        Assert.True(reopened.IsUnencryptedWrapper);
        using PdfDocument payload = reopened.EncryptedPayload!.Open();
        Assert.False(payload.IsEncrypted);
    }

    [Fact]
    public void A_payload_that_names_no_cryptographic_filter_needs_one_given()
    {
        Assert.Throws<ArgumentException>(() => PdfDocument.CreateUnencryptedWrapper(Corpus.Bytes("embedded-files.pdf")));
    }

    private static byte[] Wrap(byte[] payload, PdfUnencryptedWrapperOptions? options = null)
    {
        using PdfDocument wrapper = PdfDocument.CreateUnencryptedWrapper(payload, options ?? new PdfUnencryptedWrapperOptions());
        return Save(wrapper);
    }

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static byte[] Decoded(PdfEncryptedPayload payload)
    {
        var bytes = new ArrayBufferWriter<byte>();
        payload.File.EmbeddedFile!.Decode(bytes);
        return bytes.WrittenSpan.ToArray();
    }
}
