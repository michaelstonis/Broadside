using System.Security.Cryptography;
using System.Text;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// The security handler extension point: handlers chosen by <c>Filter</c> or <c>SubFilter</c>, the standard handler as the
/// default, and the errors for encryption no handler implements. ISO 32000-2 §7.6.2, Table 20; §7.6.6, Table 25.
/// </summary>
public class SecurityHandlerExtensionPointTests
{
    /// <summary>The file encryption key generate.py fixed for encrypted-aes-256.pdf.</summary>
    private static readonly byte[] Aes256FileKey = SHA256.HashData("broadside-corpus:encrypted-aes-256:file-key:0"u8);

    [Fact]
    public void The_standard_handler_is_registered_by_default()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));

        Assert.Equal(new StandardSecurityHandler().Filter, document.Security!.Handler);
    }

    [Fact]
    public void A_handler_registered_for_standard_replaces_the_default()
    {
        var handler = new CountingHandler(new StandardSecurityHandler());

        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"), new PdfOptions().UseSecurityHandler(handler));

        Assert.Equal(1, handler.Calls);
        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256 (R6)) Tj ET", EncryptedCorpusTests.ContentText(document));
    }

    [Fact]
    public void Two_engines_with_different_handlers_do_not_interfere()
    {
        var handler = new CountingHandler(new StandardSecurityHandler());
        var custom = new PdfEngine(new PdfOptions().UseSecurityHandler(handler));

        using (PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf")))
        {
            Assert.NotNull(document.Security);
        }

        using (PdfDocument document = custom.Open(Corpus.Path("encrypted-aes-256.pdf")))
        {
            Assert.NotNull(document.Security);
        }

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void A_handler_that_supplies_the_key_another_way_decrypts_the_document()
    {
        var escrow = new KeyEscrowHandler(new CosName("Escrow"), [], Aes256FileKey);

        using PdfDocument document = PdfDocument.Open(WithEncryption("encrypted-aes-256.pdf", "/Filter /Standard", "/Filter /Escrow"), new PdfOptions().UseSecurityHandler(escrow));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256 (R6)) Tj ET", EncryptedCorpusTests.ContentText(document));
        Assert.Equal(new CosName("Escrow"), document.Security!.Handler);
        Assert.Null(document.Security.Revision);
        Assert.Equal(PdfPermissions.Print, document.Permissions);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_handler_is_chosen_by_subfilter_when_no_handler_has_the_filter_name()
    {
        var escrow = new KeyEscrowHandler(new CosName("Escrow"), [new CosName("x.escrow")], Aes256FileKey);

        using PdfDocument document = PdfDocument.Open(
            WithEncryption("encrypted-aes-256.pdf", "/Filter /Standard", "/Filter /Vendor /SubFilter /x.escrow"),
            new PdfOptions().UseSecurityHandler(escrow));

        Assert.Equal(new CosName("Escrow"), document.Security!.Handler);
        Assert.Equal(new CosName("x.escrow"), document.Security.SubFilter);
    }

    [Fact]
    public void A_document_whose_handler_is_not_registered_is_not_supported()
    {
        var exception = Assert.Throws<PdfEncryptionNotSupportedException>(
            () => PdfDocument.Open(WithEncryption("encrypted-aes-256.pdf", "/Filter /Standard", "/Filter /Vendor")));

        Assert.Equal(PdfEncryptionNotSupportedReason.Handler, exception.Reason);
    }

    [Fact]
    public void An_unknown_crypt_filter_method_is_an_unsupported_algorithm()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-256.pdf")).Replace("/CFM /AESV3", "/CFM /AESV9", StringComparison.Ordinal);

        var exception = Assert.Throws<PdfEncryptionNotSupportedException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(text)));

        Assert.Equal(PdfEncryptionNotSupportedReason.Algorithm, exception.Reason);
    }

    [Fact]
    public void An_undefined_revision_is_an_unsupported_algorithm()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-256.pdf")).Replace("/R 6", "/R 9", StringComparison.Ordinal);

        var exception = Assert.Throws<PdfEncryptionNotSupportedException>(() => PdfDocument.Open(Encoding.Latin1.GetBytes(text)));

        Assert.Equal(PdfEncryptionNotSupportedReason.Algorithm, exception.Reason);
    }

    [Fact]
    public void Registering_no_handler_is_an_argument_error() =>
        Assert.Throws<ArgumentNullException>(() => new PdfOptions().UseSecurityHandler(null!));

    [Fact]
    public void The_standard_handler_reads_permissions_by_revision()
    {
        // Table 22: revision 2 knows bits 3-6 only; later bits follow the one that covered them.
        Assert.Equal(PdfPermissions.All, StandardSecurityHandler.UserPermissions(-4, 2));
        Assert.Equal(PdfPermissions.Print | PdfPermissions.PrintHighQuality, StandardSecurityHandler.UserPermissions(-60, 2));
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Extract, StandardSecurityHandler.UserPermissions(-3372, 3));
        Assert.Equal(PdfPermissions.Annotate | PdfPermissions.FillForms, StandardSecurityHandler.UserPermissions(unchecked((int)0xFFFFF0E0), 3));
        Assert.Equal(PdfPermissions.FillForms | PdfPermissions.Assemble, StandardSecurityHandler.UserPermissions(unchecked((int)0xFFFFF5C0), 4));
        Assert.Equal(PdfPermissions.None, StandardSecurityHandler.UserPermissions(unchecked((int)0xFFFFF8C0) & ~(1 << 11), 6));
    }

    /// <summary>Replaces the encryption dictionary (object 6) through an incremental update: its strings are never encrypted.</summary>
    private static byte[] WithEncryption(string fileName, string find, string replace)
    {
        byte[] original = Corpus.Bytes(fileName);
        string text = Encoding.Latin1.GetString(original);
        int start = text.IndexOf("6 0 obj\n", StringComparison.Ordinal) + "6 0 obj\n".Length;
        string encryption = text[start..text.IndexOf("\nendobj", start, StringComparison.Ordinal)];
        int idStart = text.IndexOf("/ID [", StringComparison.Ordinal);
        string id = text[idStart..(text.IndexOf(']', idStart) + 1)];
        return TestPdf.AppendUpdate(original, $"/Size 7 /Root 1 0 R /Encrypt 6 0 R {id}", (6, 0, encryption.Replace(find, replace, StringComparison.Ordinal)));
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

    private sealed class KeyEscrowHandler(CosName filter, IReadOnlyCollection<CosName> subFilters, byte[] key) : ISecurityHandler
    {
        public CosName Filter => filter;

        public IReadOnlyCollection<CosName> SubFilters => subFilters;

        public SecurityHandlerResult Authenticate(SecurityHandlerContext context)
        {
            Assert.NotNull(context.EncryptionDictionary);
            Assert.Equal(16, context.DocumentId.Length);
            return new SecurityHandlerResult(key, PdfAccessLevel.User, PdfPermissions.Print, -4);
        }
    }
}
