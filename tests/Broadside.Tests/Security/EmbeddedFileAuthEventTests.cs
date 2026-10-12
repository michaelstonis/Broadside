using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// A crypt filter whose <c>AuthEvent</c> is <c>EFOpen</c> authenticates the user when embedded files are accessed, not when the
/// document opens (ISO 32000-2 §7.6.5, Table 25). <c>encrypted-embedded-file-open.pdf</c>: <c>StmF</c> and <c>StrF</c> are
/// <c>Identity</c>, <c>EFF</c> names <c>/StdCF</c> (<c>AuthEvent /EFOpen</c>, AES-256), user password <c>pässwort</c>.
/// </summary>
public sealed class EmbeddedFileAuthEventTests
{
    private const string FileName = "encrypted-embedded-file-open.pdf";
    private static readonly CosReference EmbeddedFile = new(7, 0);

    [Fact]
    public void A_document_whose_only_password_protected_data_is_its_embedded_files_opens_without_the_password()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(FileName));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (Attachment needs a password) Tj ET", EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Equal(5, document.Security.Version);
        Assert.Equal(6, document.Security.Revision);
        Assert.DoesNotContain(document.Diagnostics, static diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
    }

    [Fact]
    public void Without_the_password_an_embedded_file_stays_encrypted_with_an_information_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(FileName), new PdfOptions().UseStrict());
        var stream = Assert.IsType<CosStream>(document.Resolve(EmbeddedFile));

        ReadOnlyMemory<byte> decoded = document.DecodeStream(stream);

        Assert.True(decoded.Span.SequenceEqual(stream.EncodedData.Span));
        Assert.NotEqual("Embedded secret", Encoding.Latin1.GetString(decoded.Span));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("CryptFilterNotAuthorized", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void The_user_password_decrypts_the_embedded_file()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path(FileName), new PdfPassword("pässwort"));

        var stream = Assert.IsType<CosStream>(document.Resolve(EmbeddedFile));

        Assert.Equal("Embedded secret", Encoding.Latin1.GetString(document.DecodeStream(stream).Span));
        Assert.Empty(document.Diagnostics);
    }
}
