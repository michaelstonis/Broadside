using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// The encrypted corpus files open with the empty user password and with the owner password <c>owner</c>, and their strings and
/// content streams decrypt to what <c>tests/Corpus/generate.py</c> encrypted. ISO 32000-2 §7.6; ISO/TS 32003.
/// </summary>
public class EncryptedCorpusTests
{
    public static TheoryData<string, string> EncryptedFiles => new()
    {
        { "encrypted-rc4-40.pdf", "RC4 40-bit (R2)" },
        { "encrypted-rc4-128.pdf", "RC4 128-bit (R3)" },
        { "encrypted-rc4-40-r3.pdf", "RC4 40-bit (R3)" },
        { "encrypted-aes-128.pdf", "AES-128 (R4)" },
        { "encrypted-aes-256.pdf", "AES-256 (R6)" },
        { "encrypted-aes-gcm.pdf", "AES-256-GCM (R7)" },
        { "encrypted-mac.pdf", "Integrity MAC (R6)" },
    };

    public static TheoryData<string, int, int, int> EncryptionParameters => new()
    {
        { "encrypted-rc4-40.pdf", 1, 2, 40 },
        { "encrypted-rc4-128.pdf", 2, 3, 128 },
        { "encrypted-rc4-40-r3.pdf", 2, 3, 40 },
        { "encrypted-aes-128.pdf", 4, 4, 128 },
        { "encrypted-aes-256.pdf", 5, 6, 256 },
        { "encrypted-aes-gcm.pdf", 6, 7, 256 },
    };

    [Theory]
    [MemberData(nameof(EncryptedFiles))]
    public void An_encrypted_file_opens_with_the_empty_user_password_and_its_content_stream_decrypts(string fileName, string text)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", ContentText(document));
        Assert.True(document.IsEncrypted);
        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(EncryptedFiles))]
    public void An_encrypted_file_opens_with_the_owner_password_and_gives_owner_access(string fileName, string text)
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Bytes(fileName), new PdfPassword("owner"));

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", ContentText(document));
        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
        Assert.Equal(PdfPermissions.All, document.Permissions);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(EncryptedFiles))]
    public void An_encrypted_file_opens_in_strict_mode(string fileName, string text)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().UseStrict());

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", ContentText(document));
    }

    [Theory]
    [MemberData(nameof(EncryptedFiles))]
    public void The_managed_cryptography_decrypts_like_the_platform_cryptography(string fileName, string text)
    {
        using IDisposable managed = CryptographyBackend.ForceManaged();
        using PdfDocument document = new PdfEngine().Open(Corpus.Bytes(fileName), new PdfPassword("owner"));

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", ContentText(document));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(EncryptionParameters))]
    public void The_security_describes_the_algorithm_revision_and_key_length(string fileName, int version, int revision, int keyLength)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        PdfSecurity security = document.Security!;

        Assert.Equal(new CosName("Standard"), security.Handler);
        Assert.Null(security.SubFilter);
        Assert.Equal(version, security.Version);
        Assert.Equal(revision, security.Revision);
        Assert.Equal(keyLength, security.KeyLength);
        Assert.Equal(-4, security.RawPermissions);
        Assert.Equal(PdfPermissions.All, security.Permissions);
        Assert.True(security.EncryptsMetadata);
        Assert.False(security.RequiresIntegrityCode);
        Assert.Equal(PdfIntegrityStatus.None, security.Integrity);
    }

    [Fact]
    public void Strings_are_decrypted_with_the_key_of_the_object_that_holds_them()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-gcm.pdf"));

        Assert.Equal("AES-GCM", InfoTitle(document));
        var extension = (CosDictionary)((CosArray)((CosDictionary)document.Catalog[new CosName("Extensions")])[new CosName("ISO_")])[0];
        Assert.Equal(":2023", ((CosString)extension[new CosName("ExtensionRevision")]).DecodeText());
        Assert.Equal("https://www.iso.org/standard/45876.html", ((CosString)extension[new CosName("URL")]).DecodeText());
    }

    [Fact]
    public void Decrypting_marks_nothing_dirty()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-gcm.pdf"));

        _ = ContentText(document);
        _ = InfoTitle(document);

        Assert.False(document.Catalog.IsDirty);
        Assert.False(document.Resolve(document.Trailer[new CosName("Info")]).IsDirty);
        Assert.False(document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]).IsDirty);
    }

    [Fact]
    public void The_encryption_dictionary_and_the_file_identifier_are_not_decrypted()
    {
        byte[] file = Corpus.Bytes("encrypted-aes-256.pdf");
        using PdfDocument document = PdfDocument.Open(file);

        var owner = (CosString)document.Security!.EncryptionDictionary[new CosName("O")];
        Assert.Contains(Convert.ToHexStringLower(owner.Bytes), Encoding.Latin1.GetString(file), StringComparison.Ordinal);
        var id = (CosString)((CosArray)document.Trailer[new CosName("ID")])[0];
        Assert.Equal(16, id.Bytes.Length);
        Assert.Contains(Convert.ToHexStringLower(id.Bytes), Encoding.Latin1.GetString(file), StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrent_readers_see_the_same_decrypted_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-aes-128.pdf"));
        string[] texts = new string[32];

        Parallel.For(0, texts.Length, index => texts[index] = ContentText(document));

        Assert.All(texts, text => Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-128 (R4)) Tj ET", text));
    }

    [Fact]
    public void An_unencrypted_document_permits_everything()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));

        Assert.False(document.IsEncrypted);
        Assert.Null(document.Security);
        Assert.Equal(PdfPermissions.All, document.Permissions);
    }

    [Fact]
    public void A_document_rebuilt_because_its_root_is_unusable_is_still_decrypted()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-256.pdf")).Replace("/Root 1 0 R", "/Root 9 0 R", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256 (R6)) Tj ET", ContentText(document));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "RootMissing");
    }

    [Fact]
    public void A_document_whose_cross_reference_is_unreadable_is_rebuilt_and_decrypted()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-gcm.pdf")).Replace("startxref", "startxrex", StringComparison.Ordinal);

        using PdfDocument document = new PdfEngine().Open(Encoding.Latin1.GetBytes(text), new PdfPassword("owner"));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256-GCM (R7)) Tj ET", ContentText(document));
        Assert.Equal("AES-GCM", InfoTitle(document));
        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
    }

    [Fact]
    public void A_revision_7_document_without_the_32003_extension_is_read_with_a_diagnostic()
    {
        string text = Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-gcm.pdf")).Replace("/ExtensionLevel 32003", "/ExtensionLevel 32009", StringComparison.Ordinal);

        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.Equal("BT /F1 24 Tf 72 700 Td (AES-256-GCM (R7)) Tj ET", ContentText(document));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("EncryptionExtensionMissing", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    internal static CosStream ContentStream(PdfDocument document) =>
        Assert.IsType<CosStream>(document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]));

    internal static string ContentText(PdfDocument document) => Encoding.Latin1.GetString(document.DecodeStream(ContentStream(document)).Span);

    internal static string InfoTitle(PdfDocument document) =>
        ((CosString)((CosDictionary)document.Resolve(document.Trailer[new CosName("Info")]))[new CosName("Title")]).DecodeText();
}
