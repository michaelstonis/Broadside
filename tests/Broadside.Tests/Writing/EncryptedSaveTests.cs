using System.Security.Cryptography.X509Certificates;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.Tests.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving with the standard security handler: new encryption, re-saving an encrypted document, removing encryption. ISO 32000-2 §7.6.</summary>
public sealed class EncryptedSaveTests
{
    private static readonly CosName Root = new("Root");

    [Fact]
    public void A_document_saved_with_passwords_opens_with_the_user_password_and_with_the_owner_password()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));

        using PdfDocument user = new PdfEngine().Open(saved, new PdfPassword("user"));
        using PdfDocument owner = new PdfEngine().Open(saved, new PdfPassword("owner"));
        Assert.Equal(PdfAccessLevel.User, user.Security!.Access);
        Assert.Equal(PdfAccessLevel.Owner, owner.Security!.Access);
        Assert.Equal(6, user.Security.Revision);
        Assert.Equal(256, user.Security.KeyLength);
        AssertSameDocument(source, user);
        Assert.Empty(user.Diagnostics);
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(saved));
    }

    public static TheoryData<PdfEncryptionAlgorithm, PdfCrossReferenceLayout, int> AlgorithmsAndLayouts
    {
        get
        {
            var data = new TheoryData<PdfEncryptionAlgorithm, PdfCrossReferenceLayout, int>();
            foreach ((PdfEncryptionAlgorithm algorithm, int revision) in new[] { (PdfEncryptionAlgorithm.Aes256, 6), (PdfEncryptionAlgorithm.Aes128, 4), (PdfEncryptionAlgorithm.Rc4, 3) })
            {
                foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
                {
                    data.Add(algorithm, layout, revision);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AlgorithmsAndLayouts))]
    public void Each_revision_reopens_with_both_passwords_and_passes_qpdf_check_with_them(PdfEncryptionAlgorithm algorithm, PdfCrossReferenceLayout layout, int revision)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("object-stream.pdf"));
        var options = new PdfSaveOptions()
            .WithCrossReferenceLayout(layout)
            .WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm).WithPermissions(PdfPermissions.Print | PdfPermissions.Extract));

        byte[] saved = Save(source, options);

        foreach ((string password, PdfAccessLevel access) in new[] { ("user", PdfAccessLevel.User), ("owner", PdfAccessLevel.Owner) })
        {
            using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword(password));
            Assert.Equal(access, reopened.Security!.Access);
            Assert.Equal(revision, reopened.Security.Revision);
            Assert.Equal(access == PdfAccessLevel.Owner ? PdfPermissions.All : PdfPermissions.Print | PdfPermissions.Extract, reopened.Permissions);
            AssertSameDocument(source, reopened);
            Assert.Empty(reopened.Diagnostics);

            (int exitCode, string output) = ExternalTool.Run("qpdf", saved, $"--password={password}", "--check");
            Assert.True(exitCode == 0, output);
            Assert.Contains("No syntax or stream encoding errors found", output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Table)]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void Re_saving_an_encrypted_document_re_encrypts_changed_and_unchanged_objects_and_never_copies_ciphertext(PdfCrossReferenceLayout layout)
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        using PdfDocument source = PdfDocument.Open(original, new PdfOptions().WithPassword("owner"));
        string content = EncryptedCorpusTests.ContentText(source);
        source.Pages[0].Dictionary[new CosName("PieceInfo")] = new CosString("Changed page"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout));

        // The unchanged content stream's ciphertext, and the source bytes of every object, appear nowhere in the output.
        ReadOnlySpan<byte> text = saved;
        Assert.True(text.IndexOf(StreamData(original, "4 0 obj")) < 0, "The unchanged content stream's ciphertext was copied.");
        Assert.True(text.IndexOf("Changed page"u8) < 0, "The changed page's new string was written unencrypted.");
        Assert.True(text.IndexOf("AES-256"u8) < 0, "The content was written unencrypted.");

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(6, reopened.Security!.Revision);
        Assert.Equal(content, EncryptedCorpusTests.ContentText(reopened));
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[new CosName("PieceInfo")]).DecodeText());
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--password=owner", "--check");
        Assert.True(exitCode == 0, output);
    }

    /// <summary>Every encrypted corpus file that opens cleanly, with each way of saving it.</summary>
    public static TheoryData<string, EncryptedSaveMode> EncryptedFilesAndModes
    {
        get
        {
            var data = new TheoryData<string, EncryptedSaveMode>();
            foreach (string file in Corpus.WellFormedFileNames.Concat(Corpus.PasswordProtectedFileNames).Where(name => name.StartsWith("encrypted-", StringComparison.Ordinal)))
            {
                foreach (EncryptedSaveMode mode in Enum.GetValues<EncryptedSaveMode>())
                {
                    data.Add(file, mode);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EncryptedFilesAndModes))]
    public void An_encrypted_file_re_saves_with_its_key_with_a_new_key_or_unencrypted(string fileName, EncryptedSaveMode mode)
    {
        string password = fileName == "encrypted-empty-owner-password.pdf" ? string.Empty : "owner";
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().WithPassword(password));
        PdfSaveOptions options = mode switch
        {
            EncryptedSaveMode.Keep => new PdfSaveOptions(),
            EncryptedSaveMode.Replace => new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "new owner")),
            _ => new PdfSaveOptions().WithRemoveEncryption(true),
        };
        byte[] saved = Save(source, options);

        string reopenWith = mode switch { EncryptedSaveMode.Keep => password, EncryptedSaveMode.Replace => "user", _ => "anything" };
        using PdfDocument reopened = PdfDocument.Open(saved, new PdfOptions().WithPassword(reopenWith));
        Assert.Equal(mode != EncryptedSaveMode.Remove, reopened.IsEncrypted);
        if (fileName == "encrypted-crypt-filters.pdf" && mode != EncryptedSaveMode.Keep)
        {
            // The streams that named a crypt filter lose their Crypt filter (ISO 32000-2 §7.4.10); their data is the same.
            Assert.Equal(DecodedContents(source), DecodedContents(reopened));
        }
        else
        {
            AssertSameDocument(source, reopened);
        }

        Assert.Empty(reopened.Diagnostics);
        if (!(mode == EncryptedSaveMode.Keep && fileName == "encrypted-aes-gcm.pdf"))
        {
            // qpdf 12 does not read revision 7 (ISO/TS 32003), and rejects the owner password of a 40-bit revision 3 file whose O
            // follows Algorithm 3 as written (tests/Corpus/README.md); that file is checked with its empty user password.
            string qpdfPassword = mode == EncryptedSaveMode.Keep && fileName == "encrypted-rc4-40-r3.pdf" ? string.Empty : reopenWith;
            (int exitCode, string output) = ExternalTool.Run("qpdf", saved, $"--password={qpdfPassword}", "--check");
            Assert.True(exitCode == 0, output);
        }
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Aes256)]
    [InlineData(PdfEncryptionAlgorithm.Aes128)]
    public void Without_metadata_encryption_the_metadata_stream_is_readable_without_a_password(PdfEncryptionAlgorithm algorithm)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));
        CosStream metadata = (CosStream)source.Resolve(source.Catalog[new CosName("Metadata")]);
        byte[] packet = metadata.EncodedData.ToArray();
        var encryption = new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm).WithEncryptMetadata(false);

        byte[] saved = Save(source, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.AllStreams).WithEncryption(encryption));

        Assert.True(((ReadOnlySpan<byte>)saved).IndexOf(packet) >= 0, "The metadata stream is not in the file as plaintext.");
        Assert.True(((ReadOnlySpan<byte>)saved).IndexOf("(Broadside)"u8) < 0, "A string outside the metadata stream was written unencrypted.");
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.False(reopened.Security!.EncryptsMetadata);
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--password=user", "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void By_default_the_metadata_stream_is_encrypted()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));

        Assert.True(((ReadOnlySpan<byte>)saved).IndexOf("<?xpacket"u8) < 0);
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.True(reopened.Security!.EncryptsMetadata);
        AssertSameDocument(source, reopened);
    }

    [Fact]
    public void Rc4_cannot_leave_metadata_unencrypted()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));
        using var output = new MemoryStream();
        var encryption = new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Rc4).WithEncryptMetadata(false);

        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithEncryption(encryption)));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4)]
    [InlineData(PdfEncryptionAlgorithm.Aes128)]
    public void A_deprecated_algorithm_records_a_diagnostic(PdfEncryptionAlgorithm algorithm)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm)));

        Diagnostic diagnostic = Assert.Single(source.Diagnostics);
        Assert.Equal("EncryptionDeprecated", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(algorithm == PdfEncryptionAlgorithm.Rc4 ? "RC4" : "AES-128", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_saving_an_rc4_document_with_its_key_records_the_diagnostic()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-rc4-128.pdf"));

        Save(source, new PdfSaveOptions());

        Assert.Equal("EncryptionDeprecated", Assert.Single(source.Diagnostics).Code);
    }

    [Fact]
    public void Aes256_records_no_deprecation_only_the_raise_to_pdf_2_0()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));

        // empty-page.pdf is PDF 1.7; AES-256 (V 5) is PDF 2.0 (#141).
        Diagnostic raised = Assert.Single(source.Diagnostics);
        Assert.Equal("VersionRaised", raised.Code);
        Assert.Equal(DiagnosticSeverity.Information, raised.Severity);
    }

    [Fact]
    public void Strict_mode_refuses_rc4_before_anything_is_written()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();
        var encryption = new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Rc4);

        Assert.Throws<DiagnosticException>(() => source.Save(output, new PdfSaveOptions().WithEncryption(encryption)));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Without_an_owner_password_the_user_password_grants_only_the_permissions()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("secret", null).WithPermissions(PdfPermissions.Print)));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("secret"));
        Assert.Equal(PdfAccessLevel.User, reopened.Security!.Access);
        Assert.Equal(PdfPermissions.Print, reopened.Permissions);
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(saved));
    }

    [Fact]
    public void An_empty_user_password_opens_without_a_password_with_the_permissions()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions(null, "owner").WithPermissions(PdfPermissions.Print | PdfPermissions.PrintHighQuality)));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(PdfAccessLevel.User, reopened.Security!.Access);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.PrintHighQuality, reopened.Permissions);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--show-encryption");
        Assert.True(exitCode == 0, output);
        Assert.Contains("print high resolution: allowed", output, StringComparison.Ordinal);
        Assert.Contains("modify anything: not allowed", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Aes256, "pässwort")]
    [InlineData(PdfEncryptionAlgorithm.Aes256, "密码")]
    [InlineData(PdfEncryptionAlgorithm.Rc4, "café")]
    public void Non_ascii_passwords_are_encoded_as_the_revision_requires(PdfEncryptionAlgorithm algorithm, string password)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions(password, "owner").WithAlgorithm(algorithm)));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword(password));
        Assert.Equal(PdfAccessLevel.User, reopened.Security!.Access);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, $"--password={password}", "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void A_password_pdf_doc_encoding_cannot_represent_is_refused_for_a_deprecated_algorithm()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using var output = new MemoryStream();
        var encryption = new PdfEncryptionOptions("密码", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Aes128);

        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithEncryption(encryption)));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Setting_and_removing_encryption_at_once_is_refused()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));
        using var output = new MemoryStream();
        PdfSaveOptions options = new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")).WithRemoveEncryption(true);

        Assert.Throws<ArgumentException>(() => source.Save(output, options));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_document_opened_without_the_key_to_its_embedded_file_re_saves_only_with_its_own_encryption()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-embedded-file-open.pdf"));
        using var refused = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => source.Save(refused, new PdfSaveOptions().WithRemoveEncryption(true)));
        Assert.Throws<NotSupportedException>(() => source.Save(refused, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner"))));
        Assert.Equal(0, refused.Length);

        // Kept, the embedded file is written as the ciphertext it was read as, and the user password still decrypts it.
        byte[] saved = Save(source, new PdfSaveOptions());
        using PdfDocument withPassword = new PdfEngine().Open(saved, new PdfPassword("pässwort"));
        using PdfDocument original = new PdfEngine().Open(Corpus.Path("encrypted-embedded-file-open.pdf"), new PdfPassword("pässwort"));
        AssertSameDocument(original, withPassword);
    }

    [Fact]
    public void An_encrypted_document_saves_incrementally_with_its_key_appending_only_encrypted_changes()
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        using PdfDocument document = PdfDocument.Open(original, new PdfOptions().WithPassword("owner"));
        document.Pages[0].Dictionary[new CosName("PieceInfo")] = new CosString("Changed page"u8);

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
        Assert.True(saved.AsSpan(original.Length).IndexOf("Changed page"u8) < 0, "The update holds the new string unencrypted.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[new CosName("PieceInfo")]).DecodeText());
        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256 (R6)) Tj ET", EncryptedCorpusTests.ContentText(reopened));
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--password=owner", "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void An_incremental_update_keeps_the_documents_encryption()
    {
        using PdfDocument encrypted = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => encrypted.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithRemoveEncryption(true)));
        Assert.Throws<NotSupportedException>(() => encrypted.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithEncryption(new PdfEncryptionOptions("a", "b"))));
        Assert.Throws<NotSupportedException>(() => plain.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithEncryption(new PdfEncryptionOptions("a", "b"))));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_dangling_reference_does_not_come_to_reach_the_new_encryption_dictionary()
    {
        // Object 3 does not exist, so the page tree has no page; the new encryption dictionary must not take number 3 (fuzz finding).
        byte[] file = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
            + "xref\n0 3\n0000000000 65535 f\r\n0000000009 00000 n\r\n0000000058 00000 n\r\ntrailer\n<< /Size 3 /Root 1 0 R >>\nstartxref\n111\n%%EOF\n");
        using PdfDocument source = PdfDocument.Open(file);
        int pages = source.Pages.Count;

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions(null, "owner")));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(pages, reopened.Pages.Count);
        Assert.NotEqual(3, ((CosReference)reopened.Trailer[new CosName("Encrypt")]).ObjectNumber);
    }

    [Theory]
    [InlineData("encrypted-aes-256.pdf", PdfEncryptionAlgorithm.Aes256)]
    [InlineData("encrypted-aes-gcm.pdf", PdfEncryptionAlgorithm.Aes128)]
    [InlineData("encrypted-rc4-128.pdf", PdfEncryptionAlgorithm.Rc4)]
    public void The_managed_cryptography_encrypts_what_the_platform_cryptography_decrypts(string fileName, PdfEncryptionAlgorithm algorithm)
    {
        // The browser has no BCL MD5, AES or AES-GCM (and macOS no SHA-3); the managed fallbacks write what the platform reads.
        byte[] kept;
        byte[] replaced;
        using (CryptographyBackend.ForceManaged())
        {
            using PdfDocument managed = new PdfEngine().Open(Corpus.Bytes(fileName), new PdfPassword("owner"));
            kept = Save(managed, new PdfSaveOptions());
            replaced = Save(managed, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm)));
        }

        using PdfDocument source = new PdfEngine().Open(Corpus.Bytes(fileName), new PdfPassword("owner"));
        using PdfDocument reopenedKept = new PdfEngine().Open(kept, new PdfPassword("owner"));
        using PdfDocument reopenedReplaced = new PdfEngine().Open(replaced, new PdfPassword("user"));
        AssertSameDocument(source, reopenedKept);
        AssertSameDocument(source, reopenedReplaced);
        Assert.Empty(reopenedKept.Diagnostics);
        Assert.Empty(reopenedReplaced.Diagnostics);
    }

    [Fact]
    public void A_public_key_encrypted_document_re_saves_with_its_key_or_with_a_password()
    {
        using X509Certificate2 reader = PublicKeyTestFile.Certificate("Broadside Reader");
        byte[] file = new PublicKeyTestFile
        {
            Encryption = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 /Recipients {0} >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
            Recipients = [PublicKeyTestFile.Envelope(PublicKeyTestFile.DefaultSeed, 0xFFFFF0E4, reader)],
        }.Build();
        using PdfDocument source = new PdfEngine().Open(file, new PdfCertificateCredentials(reader));

        byte[] kept = Save(source, new PdfSaveOptions());
        byte[] replaced = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));

        using PdfDocument withCertificate = new PdfEngine().Open(kept, new PdfCertificateCredentials(reader));
        Assert.Equal(new CosName("Adobe.PubSec"), withCertificate.Security!.Handler);
        Assert.Equal(PublicKeyTestFile.Content, EncryptedCorpusTests.ContentText(withCertificate));
        Assert.Equal(PublicKeyTestFile.Title, EncryptedCorpusTests.InfoTitle(withCertificate));
        Assert.Empty(withCertificate.Diagnostics);
        using PdfDocument withPassword = new PdfEngine().Open(replaced, new PdfPassword("user"));
        Assert.Equal(PublicKeyTestFile.Title, EncryptedCorpusTests.InfoTitle(withPassword));
        AssertSameDocument(source, withPassword);
    }

    /// <summary>The decoded content of every page.</summary>
    private static string[] DecodedContents(PdfDocument document) =>
        [.. document.Pages.Select(page => document.Resolve(page.Dictionary[new CosName("Contents")]) switch
        {
            CosArray parts => string.Concat(parts.Select(part => System.Text.Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(part)).Span))),
            CosStream stream => System.Text.Encoding.Latin1.GetString(document.DecodeStream(stream).Span),
            _ => string.Empty,
        })];

    /// <summary>The raw bytes between <c>stream</c> and <c>endstream</c> of the object that starts with <paramref name="header"/>.</summary>
    private static byte[] StreamData(byte[] file, string header)
    {
        ReadOnlySpan<byte> bytes = file;
        int at = bytes.IndexOf(System.Text.Encoding.ASCII.GetBytes(header));
        int start = at + bytes[at..].IndexOf("stream"u8) + "stream".Length;
        start += bytes[start] == '\r' ? 2 : 1;
        int end = start + bytes[start..].IndexOf("endstream"u8);
        while (bytes[end - 1] is (byte)'\n' or (byte)'\r')
        {
            end--;
        }

        Assert.True(end - start >= 32, "Expected AES ciphertext.");
        return bytes[start..end].ToArray();
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

/// <summary>How a test saves an encrypted document: with its own key, with a new key, or unencrypted.</summary>
public enum EncryptedSaveMode
{
    Keep,
    Replace,
    Remove,
}
