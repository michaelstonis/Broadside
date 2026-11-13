namespace Broadside.SampleDocuments;

/// <summary>
/// Encrypted copies of other samples, one for each security handler setup a reader meets: RC4 and AES-128 with an owner password
/// only (they open without a prompt and restrict what the reader may do), AES-256 with a user password (opening needs it),
/// AES-256-GCM with integrity protection (PDF 2.0), and public-key encryption to the client's test certificate. TEST ONLY: the
/// passwords are published in MANIFEST.md and protect nothing.
/// </summary>
internal static class Encrypted
{
    /// <summary>The user (open) password of the sample that needs one (test only).</summary>
    public const string UserPassword = "broadside-user";

    /// <summary>The owner (permissions) password of every password-encrypted sample (test only).</summary>
    public const string OwnerPassword = "broadside-owner";

    public static void Write(GeneratorContext context)
    {
        Encrypt(context, "paper.pdf", "paper-rc4.pdf", new PdfEncryptionOptions(null, OwnerPassword)
            .WithAlgorithm(PdfEncryptionAlgorithm.Rc4)
            .WithPermissions(PdfPermissions.Extract | PdfPermissions.Annotate | PdfPermissions.FillForms));
        Encrypt(context, "textbook.pdf", "textbook-aes128.pdf", new PdfEncryptionOptions(null, OwnerPassword)
            .WithAlgorithm(PdfEncryptionAlgorithm.Aes128)
            .WithPermissions(PdfPermissions.Print | PdfPermissions.PrintHighQuality | PdfPermissions.Annotate));
        Encrypt(context, "invoice-conservative.pdf", "invoice-conservative-aes256.pdf", new PdfEncryptionOptions(UserPassword, OwnerPassword)
            .WithAlgorithm(PdfEncryptionAlgorithm.Aes256)
            .WithPermissions(PdfPermissions.Print | PdfPermissions.PrintHighQuality | PdfPermissions.FillForms));
        Encrypt(context, "dictionary.pdf", "dictionary-aes256-gcm.pdf", new PdfEncryptionOptions(null, OwnerPassword)
            .WithAlgorithm(PdfEncryptionAlgorithm.Aes256Gcm)
            .WithIntegrityProtection(true)
            .WithPermissions(PdfPermissions.All));

        TestPki pki = TestPki.Load(context);
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("invoice-colourful.pdf")));
        document.Save(context.OutputPath("invoice-colourful-certificate.pdf"), new PdfSaveOptions()
            .WithUpdateModificationDate(false)
            .WithPublicKeyEncryption(new PdfPublicKeyEncryptionOptions()
                .AddRecipient(pki.Client, PdfPermissions.Print | PdfPermissions.PrintHighQuality)
                .AddOwner(pki.Provider)));
    }

    private static void Encrypt(GeneratorContext context, string source, string target, PdfEncryptionOptions encryption)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath(source)));
        document.Save(context.OutputPath(target), new PdfSaveOptions().WithUpdateModificationDate(false).WithEncryption(encryption));
    }
}
