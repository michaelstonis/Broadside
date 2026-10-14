using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.Tests.Security;
using Broadside.Tests.Signatures;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving with AES-GCM (ISO/TS 32003) and with an integrity MAC (ISO/TS 32004): new encryption, an encrypted document re-saved with
/// its own key, and incremental updates, whose MAC is computed again over the whole file.
/// </summary>
public sealed class IntegrityProtectedSaveTests
{
    private static readonly CosName Root = new("Root");
    private static readonly CosName PieceInfo = new("PieceInfo");

    public static TheoryData<PdfEncryptionAlgorithm, PdfCrossReferenceLayout> ProtectedAlgorithmsAndLayouts
    {
        get
        {
            var data = new TheoryData<PdfEncryptionAlgorithm, PdfCrossReferenceLayout>();
            foreach (PdfEncryptionAlgorithm algorithm in new[] { PdfEncryptionAlgorithm.Aes256, PdfEncryptionAlgorithm.Aes256Gcm })
            {
                foreach (PdfCrossReferenceLayout layout in Enum.GetValues<PdfCrossReferenceLayout>())
                {
                    data.Add(algorithm, layout);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ProtectedAlgorithmsAndLayouts))]
    public void New_encryption_with_integrity_protection_reopens_with_the_mac_verified(PdfEncryptionAlgorithm algorithm, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("object-stream.pdf"));
        var options = new PdfSaveOptions()
            .WithCrossReferenceLayout(layout)
            .WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm).WithIntegrityProtection(true));

        byte[] saved = Save(source, options);

        foreach (string password in new[] { "user", "owner" })
        {
            using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword(password));
            Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
            Assert.True(reopened.Security.RequiresIntegrityCode);
            Assert.Equal(algorithm == PdfEncryptionAlgorithm.Aes256Gcm ? [32003, 32004] : [32004], IsoExtensionLevels(reopened));
            AssertSameDocument(source, reopened);
            Assert.Empty(reopened.Diagnostics);
        }

        if (algorithm == PdfEncryptionAlgorithm.Aes256)
        {
            // qpdf ignores the AuthCode entry and reads AES-256; it reads neither revision 7 nor the MAC (tests/Corpus/README.md).
            (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--password=user", "--check");
            Assert.True(exitCode == 0, output);
        }
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Aes256, PdfCrossReferenceLayout.Table)]
    [InlineData(PdfEncryptionAlgorithm.Aes256Gcm, PdfCrossReferenceLayout.StreamWithObjectStreams)]
    public void Changing_one_byte_of_a_protected_save_fails_verification(PdfEncryptionAlgorithm algorithm, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        byte[] saved = Save(source, new PdfSaveOptions()
            .WithCrossReferenceLayout(layout)
            .WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm).WithIntegrityProtection(true)));

        // The header's binary comment line (ISO 32000-2 §7.5.2): the file reads the same, only the MAC notices.
        Assert.Equal((byte)'%', saved[9]);
        saved[10] ^= 0x01;

        using PdfDocument tampered = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(PdfIntegrityStatus.Failed, tampered.Security!.Integrity);
        Diagnostic mismatch = Assert.Single(tampered.Diagnostics);
        Assert.Equal("IntegrityCodeMismatch", mismatch.Code);
        Assert.Equal(DiagnosticSeverity.Error, mismatch.Severity);
    }

    [Fact]
    public void New_aes_gcm_encryption_writes_version_6_revision_7_and_declares_iso_ts_32003()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        source.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Aes256Gcm)));

        Assert.True(saved.AsSpan().IndexOf("Changed page"u8) < 0, "A string was written unencrypted.");
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(6, reopened.Security!.Version);
        Assert.Equal(7, reopened.Security.Revision);
        Assert.Equal(256, reopened.Security.KeyLength);
        Assert.Equal(PdfIntegrityStatus.None, reopened.Security.Integrity);
        Assert.False(reopened.Security.RequiresIntegrityCode);
        Assert.Equal(new CosName("AESV4"), ((CosDictionary)((CosDictionary)reopened.Security.EncryptionDictionary[new CosName("CF")])[new CosName("StdCF")])[new CosName("CFM")]);
        Assert.Equal([32003], IsoExtensionLevels(reopened));
        PdfDeveloperExtension declared = Assert.Single(reopened.Extensions);
        Assert.Equal(":2023", declared.ExtensionRevision);
        Assert.Equal(new Uri("https://www.iso.org/standard/45876.html"), declared.Url);
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[PieceInfo]).DecodeText());
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void The_iso_ts_32004_declaration_is_the_one_the_specification_gives()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", null).WithIntegrityProtection(true)));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        PdfDeveloperExtension declared = Assert.Single(reopened.Extensions);
        Assert.Equal("ISO_", declared.Prefix.Value);
        Assert.Equal(32004, declared.ExtensionLevel);
        Assert.Equal(new PdfVersion(2, 0), declared.BaseVersion);
        Assert.Equal(":2024", declared.ExtensionRevision);
        Assert.Equal(new Uri("https://www.iso.org/standard/45877.html"), declared.Url);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Aes128)]
    [InlineData(PdfEncryptionAlgorithm.Rc4)]
    public void Integrity_protection_needs_aes_256_or_aes_gcm(PdfEncryptionAlgorithm algorithm)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => source.Save(output, new PdfSaveOptions().WithEncryption(
            new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm).WithIntegrityProtection(true))));

        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_linearized_save_is_refused_rather_than_written_without_its_mac()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => source.Save(output, new PdfSaveOptions().WithLinearize(true).WithEncryption(
            new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Aes256Gcm).WithIntegrityProtection(true))));

        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("encrypted-mac.pdf", PdfCrossReferenceLayout.Table)]
    [InlineData("encrypted-mac.pdf", PdfCrossReferenceLayout.StreamWithObjectStreams)]
    [InlineData("encrypted-mac-sha3.pdf", PdfCrossReferenceLayout.Stream)]
    public void A_protected_document_re_saves_with_its_key_and_a_new_mac(string fileName, PdfCrossReferenceLayout layout)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().WithPassword("owner"));
        source.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);

        byte[] saved = Save(source, new PdfSaveOptions().WithCrossReferenceLayout(layout));

        Assert.True(saved.AsSpan().IndexOf("Changed page"u8) < 0, "A string was written unencrypted.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        Assert.True(reopened.Security.RequiresIntegrityCode);
        Assert.Equal([32004], IsoExtensionLevels(reopened));
        AssertSameDocument(source, reopened);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--password=owner", "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void An_incremental_update_of_a_protected_document_appends_a_mac_over_the_whole_file()
    {
        byte[] original = Corpus.Bytes("encrypted-mac.pdf");
        using PdfDocument document = PdfDocument.Open(original, new PdfOptions().WithPassword("owner"));
        document.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);

        byte[] first = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        document.Pages[0].Dictionary[PieceInfo] = new CosString("Changed again"u8);
        byte[] second = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(first.AsSpan(0, original.Length).SequenceEqual(original));
        Assert.True(second.AsSpan(0, first.Length).SequenceEqual(first));
        Assert.True(second.AsSpan(original.Length).IndexOf("Changed"u8) < 0, "An update holds a string unencrypted.");
        foreach ((byte[] file, string expected, int revisions) in new[] { (first, "Changed page", 2), (second, "Changed again", 3) })
        {
            using PdfDocument reopened = PdfDocument.Open(file);
            Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
            Assert.Equal(revisions, reopened.Revisions.Count);
            Assert.Equal(expected, ((CosString)reopened.Pages[0].Dictionary[PieceInfo]).DecodeText());
            Assert.Equal("BT /F1 24 Tf 72 700 Td (Integrity MAC (R6)) Tj ET", EncryptedCorpusTests.ContentText(reopened));
            Assert.Empty(reopened.Diagnostics);
        }

        (int exitCode, string output) = ExternalTool.Run("qpdf", second, "--password=owner", "--check");
        Assert.True(exitCode == 0, output);

        // The new MAC covers the original revision too: the comment encrypted-mac-tampered.pdf changes, changed in the update.
        byte[] tampered = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(second).Replace("target: original", "target: Original", StringComparison.Ordinal));
        Assert.NotEqual(second, tampered);
        using PdfDocument changed = PdfDocument.Open(tampered);
        Assert.Equal(PdfIntegrityStatus.Failed, changed.Security!.Integrity);
        Assert.Equal("IntegrityCodeMismatch", Assert.Single(changed.Diagnostics).Code);
    }

    [Fact]
    public void An_incremental_save_of_an_aes_gcm_document_appends_only_encrypted_changes()
    {
        byte[] original = Corpus.Bytes("encrypted-aes-gcm.pdf");
        using PdfDocument document = PdfDocument.Open(original, new PdfOptions().WithPassword("owner"));
        document.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);
        CosReference added = document.AddObject(new CosStream(new CosDictionary(), "Added stream data"u8.ToArray()));
        document.Pages[0].Dictionary[new CosName("Added")] = added;

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
        ReadOnlySpan<byte> update = saved.AsSpan(original.Length);
        Assert.True(update.IndexOf("Changed page"u8) < 0, "The update holds the changed string unencrypted.");
        Assert.True(update.IndexOf("Added stream data"u8) < 0, "The update holds the added stream unencrypted.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(7, reopened.Security!.Revision);
        Assert.Equal(2, reopened.Revisions.Count);
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[PieceInfo]).DecodeText());
        var stream = (CosStream)reopened.Resolve(reopened.Pages[0].Dictionary[new CosName("Added")]);
        Assert.Equal("Added stream data", Encoding.ASCII.GetString(reopened.DecodeStream(stream).Span));
        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256-GCM (R7)) Tj ET", EncryptedCorpusTests.ContentText(reopened));
        Assert.Equal([32003], IsoExtensionLevels(reopened));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void An_incremental_save_of_a_document_saved_with_aes_gcm_and_a_mac_keeps_both()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        byte[] saved = Save(source, new PdfSaveOptions().WithEncryption(
            new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Aes256Gcm).WithIntegrityProtection(true)));
        using PdfDocument document = new PdfEngine().Open(saved, new PdfPassword("owner"));
        document.Pages[0].Dictionary[PieceInfo] = new CosString("Changed page"u8);

        byte[] updated = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        using PdfDocument reopened = new PdfEngine().Open(updated, new PdfPassword("user"));
        Assert.Equal(7, reopened.Security!.Revision);
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security.Integrity);
        Assert.Equal("Changed page", ((CosString)reopened.Pages[0].Dictionary[PieceInfo]).DecodeText());
        Assert.Equal([32003, 32004], IsoExtensionLevels(reopened));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void The_managed_cryptography_writes_aes_gcm_and_a_mac_the_platform_reads()
    {
        // The browser has no BCL AES-GCM: the managed fallback encrypts what the platform decrypts, under the MAC.
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        byte[] saved;
        using (CryptographyBackend.ForceManaged())
        {
            using PdfDocument managed = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
            saved = Save(managed, new PdfSaveOptions().WithEncryption(
                new PdfEncryptionOptions("user", "owner").WithAlgorithm(PdfEncryptionAlgorithm.Aes256Gcm).WithIntegrityProtection(true)));
        }

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        Assert.Equal(EncryptedCorpusTests.ContentText(source), EncryptedCorpusTests.ContentText(reopened));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Signing_a_protected_document_attaches_its_new_mac_to_the_signature()
    {
        // ISO/TS 32004 §6.5.2: in a signed revision the token is an unsigned attribute of the signature, which covers the file.
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("encrypted-mac.pdf"), new PdfOptions().WithPassword("owner"));
        using var output = new MemoryStream();

        document.Sign(output, new PdfCertificateSigner(certificate));

        byte[] signed = output.ToArray();
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        var authCode = (CosDictionary)reopened.Trailer[new CosName("AuthCode")];
        Assert.Equal(new CosName("AttachedToSig"), authCode[new CosName("MACLocation")]);
        Assert.False(authCode.ContainsKey(new CosName("MAC")));
        Assert.Empty(reopened.Diagnostics);
        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);

        // The token binds the signature value too: a changed byte anywhere in the covered ranges fails both.
        byte[] tampered = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(signed).Replace("target: original", "target: Original", StringComparison.Ordinal));
        using PdfDocument changed = PdfDocument.Open(tampered);
        Assert.Equal(PdfIntegrityStatus.Failed, changed.Security!.Integrity);
    }

    private static int[] IsoExtensionLevels(PdfDocument document) =>
        [.. document.Extensions.Where(extension => extension.Prefix.Value == "ISO_").Select(extension => extension.ExtensionLevel ?? 0)];

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
