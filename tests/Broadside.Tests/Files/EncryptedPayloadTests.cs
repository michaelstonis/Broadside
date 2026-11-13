using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>
/// Reading unencrypted wrapper documents: an ordinary PDF 2.0 file whose collection presents an embedded encrypted payload
/// described by an encrypted payload dictionary, and the payload opened as its own document (ISO 32000-2 §7.6.7, Table 28).
/// </summary>
public sealed class EncryptedPayloadTests
{
    private const string PayloadPassword = "pässwort";

    private static readonly DateTimeOffset Modified = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_wrapper_opens_without_a_password_and_describes_its_payload()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"));

        Assert.False(wrapper.IsEncrypted);
        Assert.True(wrapper.IsUnencryptedWrapper);
        Assert.Equal("Open with the Standard security handler", wrapper.Pages[0].ExtractText().Text.Trim());
        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        Assert.Equal("Standard Protected PDF.pdf", payload.Name);
        Assert.Equal(new CosName("Standard"), payload.CryptographicFilter);
        Assert.Equal("6", payload.CryptographicFilterVersion);
        Assert.True(payload.IsCryptographicFilterAvailable);
        Assert.Equal(PdfFileRelationship.EncryptedPayload, payload.File.Relationship);
        Assert.Same(payload.File.Dictionary, Assert.Single(wrapper.EmbeddedFiles).File.Dictionary);
        Assert.Empty(wrapper.Diagnostics);
    }

    [Fact]
    public void The_payload_opens_with_the_user_password_to_its_pages_and_text()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"));

        using PdfDocument payload = wrapper.EncryptedPayload!.Open(new PdfPassword(PayloadPassword));

        Assert.True(payload.IsEncrypted);
        Assert.Equal(PdfAccessLevel.User, payload.Security!.Access);
        Assert.Equal(2, payload.Pages.Count);
        Assert.Equal("Payload page 1", payload.Pages[0].ExtractText().Text.Trim());
        Assert.Equal("Payload page 2", payload.Pages[1].ExtractText().Text.Trim());
        Assert.Empty(payload.Diagnostics);
    }

    [Fact]
    public void The_payload_opens_with_the_password_the_wrapper_options_carry()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"), new PdfOptions().WithPassword(PayloadPassword));

        using PdfDocument payload = wrapper.EncryptedPayload!.Open();

        Assert.Equal(2, payload.Pages.Count);
    }

    [Fact]
    public void The_payload_without_its_password_asks_for_one()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"));

        PdfPasswordException exception = Assert.Throws<PdfPasswordException>(() => wrapper.EncryptedPayload!.Open());

        Assert.Equal(PdfPasswordFailure.Required, exception.Failure);
    }

    [Fact]
    public void Strict_mode_accepts_a_well_formed_wrapper()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"), new PdfOptions().UseStrict());

        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        using PdfDocument opened = payload.Open(new PdfPassword(PayloadPassword));

        Assert.Equal("Payload page 2", opened.Pages[1].ExtractText().Text.Trim());
        Assert.Empty(wrapper.Diagnostics);
    }

    [Fact]
    public void A_payload_naming_an_unknown_cryptographic_filter_is_reported_and_the_wrapper_still_opens()
    {
        using PdfDocument wrapper = PdfDocument.Open(Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), filter: "AcmeCustomCrypto", version: "1.0"));

        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;

        Assert.Equal(new CosName("AcmeCustomCrypto"), payload.CryptographicFilter);
        Assert.Equal("1.0", payload.CryptographicFilterVersion);
        Assert.False(payload.IsCryptographicFilterAvailable);
        Diagnostic diagnostic = Assert.Single(wrapper.Diagnostics);
        Assert.Equal("EncryptedPayloadFilterUnknown", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("AcmeCustomCrypto", diagnostic.Message, StringComparison.Ordinal);
        Assert.Single(wrapper.Pages);
    }

    [Fact]
    public void Strict_mode_reports_an_unknown_cryptographic_filter_without_throwing()
    {
        using PdfDocument wrapper = PdfDocument.Open(Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), filter: "AcmeCustomCrypto"), new PdfOptions().UseStrict());

        Assert.False(wrapper.EncryptedPayload!.IsCryptographicFilterAvailable);
        Assert.Equal("EncryptedPayloadFilterUnknown", Assert.Single(wrapper.Diagnostics).Code);
    }

    [Fact]
    public void A_security_handler_registered_on_the_engine_provides_the_cryptographic_filter()
    {
        var handler = new NamedHandler(new CosName("AcmeCustomCrypto"), new StandardSecurityHandler());

        using PdfDocument wrapper = PdfDocument.Open(Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), filter: "AcmeCustomCrypto"), new PdfOptions().UseSecurityHandler(handler));

        Assert.True(wrapper.EncryptedPayload!.IsCryptographicFilterAvailable);
        Assert.Empty(wrapper.Diagnostics);
    }

    [Fact]
    public void The_payload_is_opened_through_the_wrapper_engine_security_handlers()
    {
        var handler = new CountingHandler(new StandardSecurityHandler());
        var engine = new PdfEngine(new PdfOptions().UseSecurityHandler(handler));
        using PdfDocument wrapper = engine.Open(Corpus.Path("encrypted-wrapper.pdf"));

        using PdfDocument payload = wrapper.EncryptedPayload!.Open(new PdfPassword(PayloadPassword));

        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, payload.Pages.Count);
    }

    [Fact]
    public void The_payload_is_read_with_the_wrapper_strictness()
    {
        byte[] damaged = WithBrokenStartXref(Corpus.Bytes("encrypted-aes-256.pdf"));
        byte[] file = Wrap(damaged);

        using (PdfDocument lenient = PdfDocument.Open(file))
        using (PdfDocument payload = lenient.EncryptedPayload!.Open())
        {
            Assert.NotEmpty(payload.Diagnostics);
            Assert.Single(payload.Pages);
        }

        using PdfDocument strict = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfEncryptedPayload described = strict.EncryptedPayload!;
        Assert.Throws<DiagnosticException>(() => described.Open());
    }

    [Theory]
    [InlineData("embedded-files.pdf")]
    [InlineData("collection-portfolio.pdf")]
    [InlineData("associated-files.pdf")]
    [InlineData("encrypted-aes-256.pdf")]
    public void A_document_without_an_encrypted_payload_is_not_a_wrapper(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        int before = document.Diagnostics.Count;

        Assert.False(document.IsUnencryptedWrapper);
        Assert.Null(document.EncryptedPayload);
        Assert.Equal(before, document.Diagnostics.Count);
    }

    [Theory]
    [InlineData(false, true, false, "View")]
    [InlineData(true, false, false, "AF")]
    [InlineData(true, true, true, "exactly one")]
    public void A_wrapper_that_deviates_from_the_clause_is_read_and_reported(bool hidden, bool associated, bool extraFile, string mentioned)
    {
        byte[] file = Wrap(Corpus.Bytes("encrypted-aes-256.pdf"), hidden: hidden, associated: associated, extraFile: extraFile);

        using (PdfDocument lenient = PdfDocument.Open(file))
        {
            PdfEncryptedPayload payload = lenient.EncryptedPayload!;
            Diagnostic diagnostic = Assert.Single(lenient.Diagnostics, diagnostic => diagnostic.Code == "UnencryptedWrapperInvalid");
            Assert.Contains(mentioned, diagnostic.Message, StringComparison.Ordinal);
            using PdfDocument opened = payload.Open();
            Assert.Single(opened.Pages);
        }

        using PdfDocument strict = PdfDocument.Open(file, new PdfOptions().UseStrict());
        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => strict.EncryptedPayload);
        Assert.Equal("UnencryptedWrapperInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void A_payload_whose_file_specification_embeds_nothing_cannot_be_opened()
    {
        using PdfDocument wrapper = PdfDocument.Open(Corpus.Path("encrypted-wrapper.pdf"));
        PdfEncryptedPayload payload = wrapper.EncryptedPayload!;
        payload.File.Dictionary!.Remove(new CosName("EF"));

        Assert.Throws<InvalidOperationException>(() => payload.Open());
    }

    /// <summary>Builds an unencrypted wrapper around <paramref name="payload"/> through the public writing API, as §7.6.7 lays it out.</summary>
    private static byte[] Wrap(byte[] payload, string filter = "Standard", string? version = null, bool hidden = true, bool associated = true, bool extraFile = false)
    {
        const string name = "Protected PDF.pdf";
        using PdfDocument wrapper = PdfDocument.Create();
        PdfFileSpecification file = wrapper.CreateEmbeddedFile(
            name,
            payload,
            new PdfEmbeddedFileOptions().WithMediaType("application/pdf").WithModificationDate(Modified).WithRelationship(PdfFileRelationship.EncryptedPayload));
        var description = new CosDictionary { [new CosName("Type")] = new CosName("EncryptedPayload"), [new CosName("Subtype")] = new CosName(filter) };
        if (version is not null)
        {
            description[new CosName("Version")] = new CosName(version);
        }

        file.Dictionary![new CosName("EP")] = description;
        wrapper.AddEmbeddedFile(name, file);
        if (associated)
        {
            wrapper.AddAssociatedFile(wrapper.Catalog, file);
        }

        if (extraFile)
        {
            wrapper.AddEmbeddedFile("readme.txt", "Install the handler."u8, new PdfEmbeddedFileOptions().WithModificationDate(Modified));
        }

        wrapper.Catalog[new CosName("Collection")] = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Collection"),
            [new CosName("D")] = new CosString(Encoding.ASCII.GetBytes(name)),
            [new CosName("View")] = new CosName(hidden ? "H" : "D"),
        };
        using var output = new MemoryStream();
        wrapper.Save(output);
        return output.ToArray();
    }

    /// <summary>Points the last <c>startxref</c> one byte past the cross-reference section, a repairable deviation.</summary>
    private static byte[] WithBrokenStartXref(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file);
        int keyword = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        int end = text.IndexOf('\n', keyword);
        long offset = long.Parse(text[keyword..end], System.Globalization.CultureInfo.InvariantCulture);
        return Encoding.Latin1.GetBytes(text[..keyword] + (offset + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + text[end..]);
    }

    private sealed class CountingHandler(ISecurityHandler inner) : ISecurityHandler
    {
        public int Calls { get; private set; }

        public CosName Filter => inner.Filter;

        public IReadOnlyCollection<CosName> SubFilters => inner.SubFilters;

        public SecurityHandlerResult Authenticate(SecurityHandlerContext context)
        {
            Calls++;
            return inner.Authenticate(context);
        }
    }

    private sealed class NamedHandler(CosName filter, ISecurityHandler inner) : ISecurityHandler
    {
        public CosName Filter => filter;

        public IReadOnlyCollection<CosName> SubFilters => [];

        public SecurityHandlerResult Authenticate(SecurityHandlerContext context) => inner.Authenticate(context);
    }
}
