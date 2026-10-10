using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;

namespace Broadside.Tests.Security;

/// <summary>
/// Documents encrypted for certificate recipients (ISO 32000-2 §7.6.5), built in memory with <c>System.Security.Cryptography.Pkcs</c>
/// and opened with a certificate and its private key.
/// </summary>
public sealed class PublicKeySecurityTests : IDisposable
{
    private const string S5Aes256 = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 /Recipients {0} >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>";
    private const string RestrictedText = "Only for some recipients";

    private readonly X509Certificate2 _reader = PublicKeyTestFile.Certificate("Broadside Reader");
    private readonly X509Certificate2 _other = PublicKeyTestFile.Certificate("Broadside Other");

    public void Dispose()
    {
        _reader.Dispose();
        _other.Dispose();
    }

    [Fact]
    public void A_document_encrypted_for_a_certificate_opens_with_that_certificate_and_its_private_key()
    {
        byte[] file = new PublicKeyTestFile
        {
            Encryption = S5Aes256,
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFF0E4, _reader)],
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PublicKeyTestFile.Title, EncryptedCorpusTests.InfoTitle(document));
        Assert.Equal(new CosName("Adobe.PubSec"), document.Security!.Handler);
        Assert.Equal(new CosName("adbe.pkcs7.s5"), document.Security.SubFilter);
        Assert.Equal(256, document.Security.KeyLength);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Another_public_key_handler_name_opens_through_its_subfilter()
    {
        byte[] file = new PublicKeyTestFile
        {
            Encryption = S5Aes256.Replace("/Adobe.PubSec", "/Adobe.PPKLite", StringComparison.Ordinal),
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(new CosName("Adobe.PubSec"), document.Security!.Handler);
    }

    [Fact]
    public void Permissions_are_read_most_significant_byte_first_with_the_meanings_of_revision_3()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFF0E4, _reader));

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        // 0xE4: bits 3 and 6 (print, annotate), and annotating implies filling forms (Table 24, bit 9). Read least significant byte
        // first, the word would set bit 2 and grant everything.
        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Annotate | PdfPermissions.FillForms, document.Permissions);
        Assert.Equal(unchecked((int)0xFFFFF0E4), document.Security.RawPermissions);
    }

    [Fact]
    public void Permission_bit_2_enables_every_permission()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0x00000002, _reader));

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
        Assert.Equal(PdfPermissions.All, document.Permissions);
    }

    [Theory]
    [InlineData("adbe.pkcs7.s3", PdfPermissions.Print | PdfPermissions.Annotate | PdfPermissions.FillForms | PdfPermissions.PrintHighQuality)]
    [InlineData("adbe.pkcs7.s4", PdfPermissions.Print | PdfPermissions.Annotate | PdfPermissions.FillForms | PdfPermissions.Assemble | PdfPermissions.PrintHighQuality)]
    public void An_s3_document_has_the_permissions_of_revision_2_and_an_s4_document_those_of_revision_3(string subFilter, PdfPermissions expected)
    {
        // Bits 3, 6, 9, 11 and 12. Revision 2 knows bits 3 to 6 only: assembling follows modifying (clear), high-quality printing
        // follows printing.
        byte[] file = new PublicKeyTestFile
        {
            Encryption = $"<< /Filter /Adobe.PubSec /SubFilter /{subFilter} /V 2 /Length 128 /Recipients {{0}} >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0x00000D24, _reader)],
            Cipher = TestCipher.Rc4,
            KeyLength = 16,
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(expected, document.Permissions);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_s4_document_encrypted_with_rc4_opens_with_a_128_bit_sha_1_key()
    {
        byte[] file = new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s4 /V 2 /Length 128 /Recipients {0} >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
            Cipher = TestCipher.Rc4,
            KeyLength = 16,
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PublicKeyTestFile.Title, EncryptedCorpusTests.InfoTitle(document));
        Assert.Equal(new CosName("adbe.pkcs7.s4"), document.Security!.SubFilter);
        Assert.Equal(2, document.Security.Version);
        Assert.Equal(128, document.Security.KeyLength);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_s5_document_with_an_aes_128_crypt_filter_opens_with_a_128_bit_sha_1_key()
    {
        byte[] file = new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 4 /CF << /DefaultCryptFilter << /CFM /AESV2 /Length 128 /Recipients {0} >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
            Cipher = TestCipher.AesV2,
            KeyLength = 16,
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PublicKeyTestFile.Title, EncryptedCorpusTests.InfoTitle(document));
        Assert.Equal(128, document.Security!.KeyLength);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_key_digests_every_recipient_list_and_the_permissions_come_from_the_list_that_names_the_reader()
    {
        byte[] seed = [.. Enumerable.Repeat((byte)0x5A, 20)];
        byte[] file = new PublicKeyTestFile
        {
            Encryption = S5Aes256,
            Seed = seed,
            Recipients =
            [
                PublicKeyTestFile.Envelope(seed, 0xFFFFFFFE, _other),
                PublicKeyTestFile.Envelope(seed, 0xFFFFF0C4, _reader),
            ],
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PdfPermissions.Print, document.Permissions);
    }

    [Fact]
    public void A_reader_in_two_lists_has_the_permissions_of_the_first()
    {
        byte[] file = S5File(
            PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFF0D4, _other, _reader),
            PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFE, _reader));

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Extract, document.Permissions);
    }

    [Fact]
    public void A_recipient_named_by_subject_key_identifier_is_found_among_several_certificates()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, SubjectIdentifierType.SubjectKeyIdentifier, _reader));
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(_other.RawData);

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(new X509Certificate2Collection(new[] { publicOnly, _reader })));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
    }

    [Fact]
    public void A_certificate_that_is_not_a_recipient_is_reported_with_the_recipients()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader));

        var exception = Assert.Throws<PdfCertificateException>(() => new PdfEngine().Open(file, new PdfCertificateCredentials(_other)));

        Assert.Equal(PdfCertificateFailure.NoMatchingRecipient, exception.Failure);
        SubjectIdentifier recipient = Assert.Single(exception.Recipients);
        var issuerSerial = Assert.IsType<X509IssuerSerial>(recipient.Value);
        Assert.Equal(_reader.SerialNumber, issuerSerial.SerialNumber, ignoreCase: true);
        Assert.Contains("CN=Broadside Reader", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_encrypted_for_certificates_needs_a_certificate()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader));

        var withoutCredentials = Assert.Throws<PdfCertificateException>(() => PdfDocument.Open(file));
        var withPassword = Assert.Throws<PdfCertificateException>(() => new PdfEngine().Open(file, new PdfPassword("secret")));

        Assert.Equal(PdfCertificateFailure.Required, withoutCredentials.Failure);
        Assert.Equal(PdfCertificateFailure.Required, withPassword.Failure);
        Assert.Single(withoutCredentials.Recipients);
    }

    [Fact]
    public void A_recipient_certificate_without_its_private_key_cannot_open_the_document()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader));
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(_reader.RawData);

        var exception = Assert.Throws<PdfCertificateException>(() => new PdfEngine().Open(file, new PdfCertificateCredentials(publicOnly)));

        Assert.Equal(PdfCertificateFailure.PrivateKeyUnavailable, exception.Failure);
    }

    [Fact]
    public void Metadata_left_in_plaintext_by_the_crypt_filter_adds_the_marker_to_the_key()
    {
        const string xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/><?xpacket end=\"w\"?>";
        byte[] file = new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 /EncryptMetadata false /Recipients {0} >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
            MetadataInPlaintext = true,
            CatalogEntries = "/Metadata 7 0 R",
            ExtraObjects = [(_, _) => PublicKeyTestFile.Stream("/Type /Metadata /Subtype /XML", Encoding.UTF8.GetBytes(xmp))],
        }.Build();

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));
        var metadata = (CosStream)document.Resolve(document.Catalog[new CosName("Metadata")]);

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.False(document.Security!.EncryptsMetadata);
        Assert.Equal(xmp, Encoding.UTF8.GetString(document.DecodeStream(metadata).Span));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_stream_whose_crypt_filter_has_its_own_recipients_decrypts_with_that_filter_s_key()
    {
        byte[] seed = [.. Enumerable.Repeat((byte)0xC3, 20)];
        byte[] restricted = PublicKeyTestFile.Envelope(seed, permissions: null, _other, _reader);
        byte[] key = PublicKeyTestFile.DeriveKey(seed, [restricted], metadataInPlaintext: false, keyLength: 32);

        using PdfDocument document = new PdfEngine().Open(RestrictedFile(restricted, key), new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal(RestrictedText, Encoding.Latin1.GetString(document.DecodeStream(RestrictedStream(document)).Span));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_stream_whose_crypt_filter_does_not_name_the_reader_stays_encrypted()
    {
        byte[] seed = [.. Enumerable.Repeat((byte)0xC3, 20)];
        byte[] restricted = PublicKeyTestFile.Envelope(seed, permissions: null, _other);
        byte[] key = PublicKeyTestFile.DeriveKey(seed, [restricted], metadataInPlaintext: false, keyLength: 32);

        using PdfDocument document = new PdfEngine().Open(RestrictedFile(restricted, key), new PdfCertificateCredentials(_reader));
        CosStream stream = RestrictedStream(document);

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.True(document.DecodeStream(stream).Span.SequenceEqual(stream.EncodedData.Span));
        Assert.Contains(document.Diagnostics, d => d.Code == "CryptFilterNotAuthorized");
        // A filter the reader is not a recipient of is legal (§7.6.6): Information only, no CryptFilterUnsupported error.
        Assert.DoesNotContain(document.Diagnostics, d => d.Severity > DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_recipient_string_padded_with_zeros_is_read_with_a_diagnostic_and_digested_whole()
    {
        byte[] padded = [.. PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader), 0, 0, 0, 0];

        using PdfDocument document = new PdfEngine().Open(S5File(padded), new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal("PublicKeyRecipientInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_recipient_string_that_is_not_cms_is_skipped_with_a_diagnostic()
    {
        byte[] file = S5File([0x30, 0x03, 0x02, 0x01, 0x00], PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader));

        using PdfDocument document = new PdfEngine().Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
        Assert.Equal("PublicKeyRecipientInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_well_formed_document_opens_in_strict_mode()
    {
        byte[] file = S5File(PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader));

        using PdfDocument document = new PdfEngine(new PdfOptions().UseStrict()).Open(file, new PdfCertificateCredentials(_reader));

        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(document));
    }

    [Fact]
    public void A_document_with_no_recipients_cannot_be_authenticated()
    {
        byte[] file = new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter /Unused {0} >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
        }.Build();

        var exception = Assert.Throws<DiagnosticException>(() => new PdfEngine().Open(file, new PdfCertificateCredentials(_reader)));

        Assert.Equal("EncryptDictionaryInvalid", exception.Diagnostic.Code);
    }

    private static CosStream RestrictedStream(PdfDocument document) =>
        Assert.IsType<CosStream>(document.Resolve(document.Catalog[new CosName("Restricted")]));

    private static byte[] S5File(params byte[][] recipients) =>
        new PublicKeyTestFile { Encryption = S5Aes256, Recipients = recipients }.Build();

    /// <summary>Object 7 names the crypt filter /Restricted, whose recipients (one CMS string, Table 27) have a seed of their own.</summary>
    private byte[] RestrictedFile(byte[] restricted, byte[] restrictedKey) =>
        new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 /Recipients {0} >> /Restricted << /CFM /AESV3 /Length 256 /Recipients " + PublicKeyTestFile.Hex(restricted) + " >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFFFFC, _reader)],
            CatalogEntries = "/Restricted 7 0 R",
            ExtraObjects =
            [
                (_, number) => PublicKeyTestFile.Stream(
                    "/Filter /Crypt /DecodeParms << /Type /CryptFilterDecodeParms /Name /Restricted >>",
                    PublicKeyTestFile.Encrypt(TestCipher.AesV3, restrictedKey, number, Encoding.Latin1.GetBytes(RestrictedText))),
            ],
        }.Build();
}
