using Broadside.Diagnostics;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Passwords: the default user password, user and owner passwords, their encodings by revision, and the distinct errors for a
/// missing or wrong password. ISO 32000-2 §7.6.4.1-7.6.4.4.
/// </summary>
public class PasswordTests
{
    public static TheoryData<string, string, string> ProtectedContent => new()
    {
        { "encrypted-user-password.pdf", "p\u00e4sswort", "User password (R6)" },
        { "encrypted-rc4-user-password.pdf", "café", "User password (R3)" },
    };

    [Theory]
    [MemberData(nameof(Corpus.PasswordProtectedFiles), MemberType = typeof(Corpus))]
    public void A_document_with_a_user_password_needs_a_password(string fileName, string userPassword)
    {
        Assert.NotEmpty(userPassword);

        var exception = Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(Corpus.Path(fileName)));

        Assert.Equal(PdfPasswordFailure.Required, exception.Failure);
    }

    [Theory]
    [MemberData(nameof(Corpus.PasswordProtectedFiles), MemberType = typeof(Corpus))]
    public void A_wrong_password_is_reported_as_incorrect(string fileName, string userPassword)
    {
        var exception = Assert.Throws<PdfPasswordException>(() => new PdfEngine().Open(Corpus.Path(fileName), new PdfPassword(userPassword + "x")));

        Assert.Equal(PdfPasswordFailure.Incorrect, exception.Failure);
    }

    [Theory]
    [MemberData(nameof(ProtectedContent))]
    public void The_user_password_opens_the_document_with_user_access(string fileName, string userPassword, string text)
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path(fileName), new PdfPassword(userPassword));

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", EncryptedCorpusTests.ContentText(document));
        Assert.Equal("Protected", EncryptedCorpusTests.InfoTitle(document));
        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(ProtectedContent))]
    public void The_owner_password_opens_the_document_with_owner_access(string fileName, string userPassword, string text)
    {
        Assert.NotEmpty(userPassword);
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName), new PdfOptions().WithPassword("owner"));

        Assert.Equal($"BT /F1 24 Tf 72 700 Td ({text}) Tj ET", EncryptedCorpusTests.ContentText(document));
        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
        Assert.Equal(PdfPermissions.All, document.Permissions);
    }

    [Fact]
    public void User_access_has_the_permissions_the_document_grants()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("encrypted-user-password.pdf"), new PdfPassword("p\u00e4sswort"));

        Assert.Equal(-3372, document.Security!.RawPermissions);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Extract, document.Permissions);
    }

    [Fact]
    public void A_revision_6_password_maps_soft_hyphens_to_nothing()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("encrypted-user-password.pdf"), new PdfPassword("p\u00e4ss\u00adwort"));

        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
    }

    [Fact]
    public void A_legacy_password_can_be_given_as_bytes_in_a_code_page()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("encrypted-rc4-user-password.pdf"), new PdfPassword([0x63, 0x61, 0x66, 0xE9]));

        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
    }

    [Fact]
    public void A_wrong_password_for_a_document_without_a_user_password_is_still_incorrect()
    {
        var exception = Assert.Throws<PdfPasswordException>(
            () => new PdfEngine().Open(Corpus.Path("encrypted-aes-256.pdf"), new PdfPassword("not the owner")));

        Assert.Equal(PdfPasswordFailure.Incorrect, exception.Failure);
    }

    [Fact]
    public void The_password_given_to_open_overrides_the_one_on_the_options()
    {
        var engine = new PdfEngine(new PdfOptions().WithPassword("wrong"));

        using PdfDocument document = engine.Open(Corpus.Path("encrypted-user-password.pdf"), new PdfPassword("owner"));

        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
    }

    [Fact]
    public void A_password_is_ignored_for_an_unencrypted_document()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("empty-page.pdf"), new PdfPassword("anything"));

        Assert.False(document.IsEncrypted);
    }

    [Fact]
    public void An_owner_password_computed_by_rehashing_only_the_key_length_is_accepted_with_a_diagnostic()
    {
        using PdfDocument document = new PdfEngine().Open(Corpus.Path("encrypted-owner-key-variant.pdf"), new PdfPassword("owner"));

        Assert.Equal(PdfAccessLevel.Owner, document.Security!.Access);
        Assert.Equal("BT /F1 24 Tf 72 700 Td (RC4 40-bit (R3), qpdf owner key) Tj ET", EncryptedCorpusTests.ContentText(document));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("OwnerPasswordKeyVariant", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void The_owner_key_variant_throws_in_strict_mode()
    {
        var engine = new PdfEngine(new PdfOptions().UseStrict());

        var exception = Assert.Throws<DiagnosticException>(() => engine.Open(Corpus.Path("encrypted-owner-key-variant.pdf"), new PdfPassword("owner")));

        Assert.Equal("OwnerPasswordKeyVariant", exception.Diagnostic.Code);
    }

    [Fact]
    public void The_owner_key_variant_opens_with_the_empty_user_password_without_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("encrypted-owner-key-variant.pdf"));

        Assert.Equal(PdfAccessLevel.User, document.Security!.Access);
        Assert.Empty(document.Diagnostics);
    }
}
