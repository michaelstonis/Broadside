using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Crypt filters: <c>StmF</c> and <c>StrF</c>, the <c>Identity</c> crypt filter, a stream that names its own crypt filter, and
/// <c>EncryptMetadata false</c>; and how damaged ciphertext is repaired. ISO 32000-2 §7.4.10, §7.6.3, §7.6.6; ISO/TS 32003 §5.2.
/// </summary>
public class CryptFilterTests
{
    [Fact]
    public void Each_content_stream_decrypts_with_the_crypt_filter_it_names_or_the_default_one()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-crypt-filters.pdf"));
        var contents = (CosArray)document.Pages[0].Dictionary[new CosName("Contents")];

        string[] texts = [.. contents.Select(reference => Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(reference)).Span))];

        Assert.Equal(
            [
                "BT /F1 24 Tf 72 700 Td (StmF StdCF) Tj ET",
                "BT /F1 24 Tf 72 660 Td (Identity crypt filter) Tj ET",
                "BT /F1 24 Tf 72 620 Td (StdCF crypt filter) Tj ET",
            ],
            texts);
        Assert.Equal("Crypt filters", EncryptedCorpusTests.InfoTitle(document));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Metadata_is_left_as_it_is_when_encrypt_metadata_is_false()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-crypt-filters.pdf"));
        var metadata = (CosStream)document.Resolve(document.Catalog[new CosName("Metadata")]);

        Assert.False(document.Security!.EncryptsMetadata);
        Assert.StartsWith("<?xpacket begin=", Encoding.UTF8.GetString(document.DecodeStream(metadata).Span), StringComparison.Ordinal);
        Assert.Equal(PdfAccessLevel.User, document.Security.Access);
    }

    [Fact]
    public void The_owner_password_opens_a_document_whose_metadata_is_not_encrypted()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-crypt-filters.pdf"), new PdfOptions().WithPassword("owner"));

        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
        Assert.Equal("Crypt filters", EncryptedCorpusTests.InfoTitle(document));
    }

    [Fact]
    public void A_stream_that_names_a_crypt_filter_the_document_does_not_define_is_left_encrypted_with_a_diagnostic()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-crypt-filters.pdf"))
            .Replace("/DecodeParms << /Type /CryptFilterDecodeParms /Name /StdCF >>", "/DecodeParms << /Type /CryptFilterDecodeParms /Name /XyzCF >>", StringComparison.Ordinal);
        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));
        var stream = (CosStream)document.Resolve(((CosArray)document.Pages[0].Dictionary[new CosName("Contents")])[2]);

        ReadOnlyMemory<byte> decoded = document.DecodeStream(stream);

        Assert.True(decoded.Span.SequenceEqual(stream.EncodedData.Span));
        Assert.Equal("CryptFilterUnsupported", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Metadata_in_plaintext_although_encrypt_metadata_is_true_is_read_as_it_is_with_a_diagnostic()
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        string id = Regex.Match(Encoding.Latin1.GetString(original), "/ID \\[<[0-9a-f]+> <[0-9a-f]+>\\]").Value;
        const string xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/><?xpacket end=\"w\"?>";
        byte[] file = TestPdf.AppendUpdate(
            original,
            $"/Size 9 /Root 1 0 R /Encrypt 6 0 R {id}",
            (1, 0, "<< /Type /Catalog /Pages 2 0 R /Metadata 8 0 R >>"),
            (8, 0, $"<< /Type /Metadata /Subtype /XML /Length {xmp.Length} >>\nstream\n{xmp}\nendstream"));

        using PdfDocument document = PdfDocument.Open(file);
        var metadata = (CosStream)document.Resolve(document.Catalog[new CosName("Metadata")]);

        Assert.StartsWith("<?xpacket begin=", Encoding.UTF8.GetString(metadata.EncodedData.Span), StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "MetadataNotEncrypted");
    }

    [Fact]
    public void Aes_gcm_data_that_fails_authentication_decrypts_to_nothing_with_an_error()
    {
        byte[] file = Corpus.Bytes("encrypted-aes-gcm.pdf");
        int data = IndexOf(file, "4 0 obj\n<<  /Length "u8) is var start and >= 0 ? IndexOf(file, "stream\n"u8, start) + "stream\n".Length : -1;
        file[data + 20] ^= 0x01;

        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(string.Empty, EncryptedCorpusTests.ContentText(document));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("EncryptedDataAuthenticationFailed", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(new CosReference(4, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void Aes_data_with_a_damaged_pad_is_kept_whole_with_a_diagnostic()
    {
        byte[] file = Corpus.Bytes("encrypted-aes-256.pdf");
        int start = IndexOf(file, "4 0 obj\n"u8);
        int end = IndexOf(file, "\nendstream"u8, start);
        file[end - 1] ^= 0x55;

        using PdfDocument document = PdfDocument.Open(file);
        string text = EncryptedCorpusTests.ContentText(document);

        Assert.StartsWith("BT /F1 24 Tf 72 700 Td (AES-256", text, StringComparison.Ordinal);
        Assert.Equal(48, text.Length);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("EncryptedDataInvalid", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Damaged_ciphertext_throws_in_strict_mode()
    {
        byte[] file = Corpus.Bytes("encrypted-aes-gcm.pdf");
        int start = IndexOf(file, "4 0 obj\n"u8);
        file[IndexOf(file, "stream\n"u8, start) + 30] ^= 0x01;

        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        var exception = Assert.Throws<DiagnosticException>(() => EncryptedCorpusTests.ContentText(document));
        Assert.Equal("EncryptedDataAuthenticationFailed", exception.Diagnostic.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_embedded_file_is_decrypted_with_eff_which_defaults_to_stmf(bool effIdentity)
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        string text = Encoding.Latin1.GetString(original);
        byte[] payload = "embedded file payload"u8.ToArray();
        byte[] data = payload;
        if (!effIdentity)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = System.Security.Cryptography.SHA256.HashData("broadside-corpus:encrypted-aes-256:file-key:0"u8);
            byte[] iv = new byte[16];
            data = [.. iv, .. aes.EncryptCbc(payload, iv)];
        }

        int start = text.IndexOf("6 0 obj\n", StringComparison.Ordinal) + "6 0 obj\n".Length;
        string encryption = text[start..text.IndexOf("\nendobj", start, StringComparison.Ordinal)];
        int idStart = text.IndexOf("/ID [", StringComparison.Ordinal);
        string id = text[idStart..(text.IndexOf(']', idStart) + 1)];
        byte[] file = TestPdf.AppendUpdate(
            original,
            $"/Size 9 /Root 1 0 R /Encrypt 6 0 R {id}",
            (6, 0, effIdentity ? encryption[..^2] + " /EFF /Identity >>" : encryption),
            (8, 0, $"<< /Type /EmbeddedFile /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream"));

        using PdfDocument document = PdfDocument.Open(file);
        var stream = (CosStream)document.Resolve(new CosReference(8, 0));

        Assert.Equal(payload, document.DecodeStream(stream).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int start = 0)
    {
        int index = haystack.AsSpan(start).IndexOf(needle);
        return index < 0 ? -1 : start + index;
    }
}
