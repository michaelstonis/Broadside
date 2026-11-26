using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside;
using Broadside.Samples;
using Broadside.Security;

Sample.Start("Reading: passwords and certificates");

OpenWithoutAPassword();
OpenWithAPassword();
OpenWithAPasswordForEveryFile();
HandleAMissingOrWrongPassword();
OpenWithACertificate();
Sample.Finish();

static void OpenWithoutAPassword()
{
    #region empty-user-password
    using PdfDocument document = PdfDocument.Open("encrypted-aes-128.pdf"); // the user password is empty
    Console.WriteLine($"Encrypted: {document.IsEncrypted}, access: {document.Security?.Access}, may print: {document.Permissions.HasFlag(PdfPermissions.Print)}");
    #endregion
    Sample.Expect(document.IsEncrypted && document.Pages.Count == 1, "an encrypted file with an empty user password opens");
}

static void OpenWithAPassword()
{
    #region password
    var engine = new PdfEngine();
    using PdfDocument document = engine.Open("encrypted-user-password.pdf", new PdfPassword("pässwort"));
    Console.WriteLine($"{document.Properties.Title}: {document.Permissions}");
    #endregion
    Sample.Expect(document.Properties.Title == "Protected", "the decrypted title");
    Sample.Expect(document.Permissions == (PdfPermissions.Print | PdfPermissions.Extract), "the user's permissions");
}

static void OpenWithAPasswordForEveryFile()
{
    #region password-options
    PdfOptions options = new PdfOptions().WithPassword("pässwort");
    using PdfDocument document = PdfDocument.Open("encrypted-user-password.pdf", options);
    #endregion
    Sample.Expect(document.Security?.Access == PdfAccessLevel.User, "opened as the user");
}

static void HandleAMissingOrWrongPassword()
{
    #region password-failure
    try
    {
        using PdfDocument document = PdfDocument.Open("encrypted-user-password.pdf");
    }
    catch (PdfPasswordException ex) when (ex.Failure == PdfPasswordFailure.Required)
    {
        Console.WriteLine("This file needs a password.");
    }

    try
    {
        using PdfDocument document = new PdfEngine().Open("encrypted-user-password.pdf", new PdfPassword("wrong"));
    }
    catch (PdfPasswordException ex) when (ex.Failure == PdfPasswordFailure.Incorrect)
    {
        Console.WriteLine("That password does not open this file.");
    }
    #endregion
}

static void OpenWithACertificate()
{
    // A certificate with its private key. A real application loads its own, for instance from the certificate store or a .pfx file.
    using RSA key = RSA.Create(2048);
    var request = new CertificateRequest("CN=Broadside sample recipient", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    // A file encrypted for that certificate.
    using (PdfDocument created = PdfDocument.Create())
    {
        var encryption = new PdfPublicKeyEncryptionOptions().AddRecipient(certificate, PdfPermissions.Print);
        created.Save("for-recipient.pdf", new PdfSaveOptions().WithPublicKeyEncryption(encryption));
    }

    #region certificate
    var engine = new PdfEngine();
    try
    {
        using PdfDocument document = engine.Open("for-recipient.pdf", new PdfCertificateCredentials(certificate));
        Console.WriteLine($"Opened for {certificate.Subject}: {document.Permissions}");
    }
    catch (PdfCertificateException ex)
    {
        Console.WriteLine($"Not for this certificate ({ex.Failure}); recipients: {ex.Recipients.Count}");
    }
    #endregion

    using (PdfDocument document = engine.Open("for-recipient.pdf", new PdfCertificateCredentials(certificate)))
    {
        Sample.Expect(document.Permissions == PdfPermissions.Print, "the recipient's permissions");
    }

    bool required = false;
    try
    {
        PdfDocument.Open("for-recipient.pdf").Dispose();
    }
    catch (PdfCertificateException ex) when (ex.Failure == PdfCertificateFailure.Required)
    {
        required = true;
    }

    Sample.Expect(required, "a certificate is required");
}
