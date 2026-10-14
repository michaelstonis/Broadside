using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving encrypted for certificate recipients with the public-key security handler. ISO 32000-2 §7.6.5.</summary>
public sealed class PublicKeyEncryptedSaveTests
{
    private static readonly CosName Root = new("Root");
    private static readonly CosName CF = new("CF");
    private static readonly CosName DefaultCryptFilter = new("DefaultCryptFilter");
    private static readonly CosName Recipients = new("Recipients");
    private static readonly CosName PieceInfo = new("PieceInfo");

    public static TheoryData<PdfCrossReferenceLayout> Layouts => new(Enum.GetValues<PdfCrossReferenceLayout>());

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_document_encrypted_for_two_recipients_opens_with_each_certificate_and_reports_its_permissions(PdfCrossReferenceLayout layout)
    {
        using X509Certificate2 editor = PublicKeyTestFile.Certificate("Broadside Editor");
        using X509Certificate2 viewer = PublicKeyTestFile.Certificate("Broadside Viewer");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("object-stream.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions()
            .AddRecipient(editor, PdfPermissions.All)
            .AddRecipient(viewer, PdfPermissions.Print | PdfPermissions.Extract);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithPublicKeyEncryption(encryption));

        using PdfDocument asEditor = new PdfEngine().Open(saved, new PdfCertificateCredentials(editor));
        using PdfDocument asViewer = new PdfEngine().Open(saved, new PdfCertificateCredentials(viewer));
        Assert.Equal(PdfPermissions.All, asEditor.Permissions);
        Assert.Equal(PdfAccessLevel.User, asEditor.Security!.Access);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Extract, asViewer.Permissions);
        Assert.Equal(PdfAccessLevel.User, asViewer.Security!.Access);
        foreach (PdfDocument reopened in new[] { asEditor, asViewer })
        {
            Assert.Equal(new CosName("Adobe.PubSec"), reopened.Security!.Handler);
            Assert.Equal(new CosName("adbe.pkcs7.s5"), reopened.Security.SubFilter);
            Assert.Equal(5, reopened.Security.Version);
            Assert.Equal(256, reopened.Security.KeyLength);
            Assert.Equal(PdfIntegrityStatus.None, reopened.Security.Integrity);
            Assert.False(reopened.Security.RequiresIntegrityCode);
            AssertSameDocument(source, reopened);
            Assert.Empty(reopened.Diagnostics);
        }
    }

    [Fact]
    public void A_certificate_not_in_the_recipient_list_fails_with_the_credentials_error()
    {
        using X509Certificate2 recipient = PublicKeyTestFile.Certificate("Broadside Recipient");
        using X509Certificate2 stranger = PublicKeyTestFile.Certificate("Broadside Stranger");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithPublicKeyEncryption(new PdfPublicKeyEncryptionOptions().AddRecipient(recipient, PdfPermissions.All)));

        PdfCertificateException wrong = Assert.Throws<PdfCertificateException>(() => new PdfEngine().Open(saved, new PdfCertificateCredentials(stranger)));
        Assert.Equal(PdfCertificateFailure.NoMatchingRecipient, wrong.Failure);
        X509IssuerSerial listed = Assert.IsType<X509IssuerSerial>(Assert.Single(wrong.Recipients).Value);
        Assert.Equal(recipient.Issuer, listed.IssuerName);
        PdfCertificateException none = Assert.Throws<PdfCertificateException>(() => PdfDocument.Open(saved));
        Assert.Equal(PdfCertificateFailure.Required, none.Failure);
    }

    [Fact]
    public void An_owner_recipient_has_owner_access_and_every_permission()
    {
        using X509Certificate2 owner = PublicKeyTestFile.Certificate("Broadside Owner");
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.None).AddOwner(owner);

        byte[] saved = Save(source, new PdfSaveOptions().WithPublicKeyEncryption(encryption));

        using PdfDocument asOwner = new PdfEngine().Open(saved, new PdfCertificateCredentials(owner));
        using PdfDocument asReader = new PdfEngine().Open(saved, new PdfCertificateCredentials(reader));
        Assert.Equal(PdfAccessLevel.Owner, asOwner.Security!.Access);
        Assert.Equal(PdfPermissions.All, asOwner.Permissions);
        Assert.Equal(PdfAccessLevel.User, asReader.Security!.Access);
        Assert.Equal(PdfPermissions.None, asReader.Permissions);
        Assert.Equal(PdfAccessLevel.Owner, encryption.Recipients[1].Access);
    }

    [Fact]
    public void Each_list_is_cms_enveloped_data_holding_one_seed_and_its_permissions_most_significant_byte_first()
    {
        using X509Certificate2 first = PublicKeyTestFile.Certificate("Broadside First");
        using X509Certificate2 second = PublicKeyTestFile.Certificate("Broadside Second");
        using X509Certificate2 third = PublicKeyTestFile.Certificate("Broadside Third");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions()
            .AddRecipient(first, PdfPermissions.Print | PdfPermissions.Extract)
            .AddOwner(second)
            .AddRecipient(third, PdfPermissions.Print | PdfPermissions.Extract);

        byte[] saved = Save(source, new PdfSaveOptions().WithPublicKeyEncryption(encryption));

        // Recipients with the same permissions share one CMS object (Table 27), in the order they were first added.
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfCertificateCredentials(first));
        CosDictionary filter = (CosDictionary)((CosDictionary)reopened.Security!.EncryptionDictionary[CF])[DefaultCryptFilter];
        Assert.Equal(new CosName("AESV3"), filter[new CosName("CFM")]);
        Assert.Equal(new CosInteger(256), filter[new CosName("Length")]);
        CosArray lists = (CosArray)filter[Recipients];
        Assert.Equal(2, lists.Count);
        byte[] firstList = ((CosString)lists[0]).Bytes.ToArray();
        byte[] secondList = ((CosString)lists[1]).Bytes.ToArray();

        // OpenSSL decrypts each list independently: a 20-byte seed, the same in every list, then the permissions word of Table 24
        // (bits 3 and 5 for Print and Extract, bit 2 for the owner) with bit 10 and the unused bits set.
        byte[] firstContent = OpenSslDecrypt(firstList, first);
        byte[] thirdContent = OpenSslDecrypt(firstList, third);
        byte[] secondContent = OpenSslDecrypt(secondList, second);
        Assert.Equal(24, firstContent.Length);
        Assert.Equal(firstContent, thirdContent);
        Assert.Equal(firstContent[..20], secondContent[..20]);
        Assert.Equal(0xFFFF_F2D4u, BinaryPrimitives.ReadUInt32BigEndian(firstContent.AsSpan(20)));
        Assert.Equal(0xFFFF_FFFEu, BinaryPrimitives.ReadUInt32BigEndian(secondContent.AsSpan(20)));
    }

    [Fact]
    public void Without_metadata_encryption_the_metadata_stream_is_readable_without_a_certificate()
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.All).WithEncryptMetadata(false);

        byte[] saved = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.AllStreams).WithPublicKeyEncryption(encryption));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfCertificateCredentials(reader));
        byte[] packet = reopened.Metadata!.Stream.EncodedData.ToArray();
        Assert.True(((ReadOnlySpan<byte>)packet).IndexOf("<?xpacket"u8) >= 0);
        Assert.True(((ReadOnlySpan<byte>)saved).IndexOf(packet) >= 0, "The metadata stream is not in the file as plaintext.");
        Assert.False(reopened.Security!.EncryptsMetadata);
        CosDictionary filter = (CosDictionary)((CosDictionary)reopened.Security.EncryptionDictionary[CF])[DefaultCryptFilter];
        Assert.Equal(CosBoolean.False, filter[new CosName("EncryptMetadata")]);
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_password_encrypted_document_is_saved_for_recipients_without_copying_its_ciphertext()
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));
        source.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithPublicKeyEncryption(new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.All)));

        Assert.True(saved.AsSpan().IndexOf("Changed page"u8) < 0, "A string was written unencrypted.");
        Assert.True(saved.AsSpan().IndexOf("/Standard"u8) < 0, "The password encryption dictionary was written.");
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfCertificateCredentials(reader));
        Assert.Equal(new CosName("Adobe.PubSec"), reopened.Security!.Handler);
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[PieceInfo]).DecodeText());
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void Integrity_protection_writes_a_mac_keyed_from_the_default_crypt_filter_that_verifies_and_survives_a_resave(PdfCrossReferenceLayout layout)
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.Print).WithIntegrityProtection(true);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout).WithPublicKeyEncryption(encryption));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfCertificateCredentials(reader));
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        Assert.True(reopened.Security.RequiresIntegrityCode);
        Assert.Equal(PdfPermissions.Print, reopened.Permissions);
        Assert.Equal(32, ((CosString)reopened.Security.EncryptionDictionary[new CosName("KDFSalt")]).Bytes.Length);
        Assert.Contains(reopened.Extensions, extension => extension.Prefix.Value == "ISO_" && extension.ExtensionLevel == 32004);
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);

        // Re-saved with its own key, the MAC is computed again from the same key and salt.
        byte[] resaved = Save(reopened, new PdfSaveOptions().WithCrossReferenceLayout(layout));
        using PdfDocument again = new PdfEngine().Open(resaved, new PdfCertificateCredentials(reader));
        Assert.Equal(PdfIntegrityStatus.Verified, again.Security!.Integrity);
        Assert.Empty(again.Diagnostics);

        // The header's binary comment line (§7.5.2): the file reads the same, only the MAC notices.
        Assert.Equal((byte)'%', saved[9]);
        saved[10] ^= 0x01;
        using PdfDocument tampered = new PdfEngine().Open(saved, new PdfCertificateCredentials(reader));
        Assert.Equal(PdfIntegrityStatus.Failed, tampered.Security!.Integrity);
        Assert.Equal("IntegrityCodeMismatch", Assert.Single(tampered.Diagnostics).Code);
    }

    [Fact]
    public void A_save_without_recipients_or_combined_with_other_encryption_options_is_refused_before_writing()
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var recipients = new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.All);
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(new PdfPublicKeyEncryptionOptions())));
        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(recipients).WithEncryption(new PdfEncryptionOptions("user", "owner"))));
        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(recipients).WithRemoveEncryption(true)));
        Assert.Throws<NotSupportedException>(() => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(recipients).WithIncrementalUpdate(true)));
        Assert.Throws<NotSupportedException>(() => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(recipients.WithIntegrityProtection(true)).WithLinearize(true)));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_certificate_without_an_rsa_key_is_refused_as_a_recipient()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Broadside EC", key, HashAlgorithmName.SHA256);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));

        Assert.Throws<ArgumentException>(() => new PdfPublicKeyEncryptionOptions().AddRecipient(certificate, PdfPermissions.All));
        Assert.Throws<ArgumentNullException>(() => new PdfPublicKeyEncryptionOptions().AddOwner(null!));
    }

    [Fact]
    public void Strict_mode_refuses_raising_a_document_below_pdf_2_0_without_writing()
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(
            () => source.Save(output, new PdfSaveOptions().WithPublicKeyEncryption(new PdfPublicKeyEncryptionOptions().AddRecipient(reader, PdfPermissions.All))));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("AES-256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    /// <summary>OpenSSL's decryption of one CMS enveloped-data object with the recipient's certificate and private key.</summary>
    private static byte[] OpenSslDecrypt(byte[] cms, X509Certificate2 recipient)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string certificatePath = Path.Combine(directory, "recipient.pem");
            string keyPath = Path.Combine(directory, "recipient.key");
            string outputPath = Path.Combine(directory, "content.bin");
            File.WriteAllText(certificatePath, recipient.ExportCertificatePem());
            using (RSA key = recipient.GetRSAPrivateKey()!)
            {
                File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            }

            (int exitCode, string output) = ExternalTool.Run(
                "openssl",
                cms,
                "cms", "-decrypt", "-binary", "-inform", "DER", "-recip", certificatePath, "-inkey", keyPath, "-out", outputPath, "-in");
            Assert.True(exitCode == 0, output);
            return File.ReadAllBytes(outputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>The catalog graphs are equal: every string and every stream's data (decrypted) the same.</summary>
    private static void AssertSameDocument(PdfDocument expected, PdfDocument actual) =>
        CosGraph.AssertIsomorphic(expected, expected.Trailer[Root], actual, actual.Trailer[Root]);
}
