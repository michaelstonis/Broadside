using System.Security.Cryptography.X509Certificates;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Tests.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Saving encrypted for certificate recipients with the public-key security handler. ISO 32000-2 §7.6.5.</summary>
public sealed class PublicKeyEncryptedSaveTests
{
    private static readonly CosName Root = new("Root");

    [Fact]
    public void A_document_encrypted_for_two_recipients_opens_with_each_certificate_and_reports_its_permissions()
    {
        using X509Certificate2 editor = PublicKeyTestFile.Certificate("Broadside Editor");
        using X509Certificate2 viewer = PublicKeyTestFile.Certificate("Broadside Viewer");
        using PdfDocument source = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        var encryption = new PdfPublicKeyEncryptionOptions()
            .AddRecipient(editor, PdfPermissions.All)
            .AddRecipient(viewer, PdfPermissions.Print | PdfPermissions.Extract);

        byte[] saved = Save(source, new PdfSaveOptions().WithPublicKeyEncryption(encryption));

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
            Assert.False(reopened.Security.RequiresIntegrityCode);
            AssertSameDocument(source, reopened);
            Assert.Empty(reopened.Diagnostics);
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
