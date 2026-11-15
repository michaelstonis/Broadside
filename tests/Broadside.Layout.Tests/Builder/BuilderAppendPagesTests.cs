using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Layout.Flow;
using Broadside.Signatures;
using Broadside.Structure;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// <c>AppendPages(doc =&gt; ...)</c> into opened documents (issue #397, B9): under a tagged document's existing <c>Document</c> element, into
/// an untagged one with a diagnostic, keeping the target's metadata, and through an incremental save that leaves a signature valid
/// (ISO 32000-2 §7.5.6, §12.8).
/// </summary>
public sealed class BuilderAppendPagesTests
{
    [Fact]
    public void Pages_appended_to_a_tagged_document_go_under_its_existing_Document_element()
    {
        byte[] contract;
        using (PdfDocument original = PdfDocument.Create(doc => doc.WithTitle("Contract").WithLanguage("en-GB").Content(c =>
        {
            c.Heading(1, "Contract");
            c.Paragraph("The parties agree.");
        })))
        {
            contract = Save(original);
        }

        using PdfDocument document = PdfDocument.Open(contract);
        document.AppendPages(doc => doc
            .WithTitle("Schedule")
            .WithLanguage("fr")
            .Content(c =>
            {
                c.Heading(1, "Schedule A: Equipment");
                c.Table(t =>
                {
                    t.Header(h =>
                    {
                        h.Cell("Item");
                        h.Cell("Qty");
                    });
                    t.Row("Flow meter", "2");
                });
            }));

        Assert.Equal(2, document.Pages.Count);
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Equal("Document(H1,P,H1,Table(THead(TR(TH(P),TH(P))),TBody(TR(TD(P),TD(P)))))", Shape(reopened.StructureTree!.Children));
        Assert.Equal("Contract", reopened.Properties.Title);
        Assert.Equal("en-GB", reopened.Language);
        Assert.Equal(["BuilderMetadataKept", "BuilderMetadataKept"], BuilderDiagnosticSink.Of(document).Select(diagnostic => diagnostic.Code));
        Assert.Contains("Schedule A: Equipment", reopened.Pages[1].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_appended_to_an_untagged_document_are_tagged_with_a_diagnostic()
    {
        byte[] plain;
        using (PdfDocument original = PdfDocument.Create(doc => doc.WithTitle("Plain").WithLanguage("en").WithTagging(false).Content(c => c.Paragraph("Old page."))))
        {
            plain = Save(original);
        }

        using PdfDocument document = PdfDocument.Open(plain);
        document.AppendPages(doc => doc.Content(c => c.Paragraph("New page.")));

        Assert.Contains(BuilderDiagnosticSink.Of(document), diagnostic => diagnostic.Code == "BuilderAppendedToUntagged");
        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Equal("Document(P)", Shape(reopened.StructureTree!.Children));
        Assert.Equal(2, reopened.Pages.Count);
    }

    [Fact]
    public void A_signed_document_stays_valid_after_appended_pages_are_saved_incrementally()
    {
        using X509Certificate2 certificate = SelfSigned();
        byte[] signed;
        using (PdfDocument original = PdfDocument.Create(doc => doc.WithTitle("Signed").WithLanguage("en").Content(c => c.Paragraph("Signed content."))))
        {
            using var reopenedOriginal = PdfDocument.Open(Save(original));
            using var output = new MemoryStream();
            reopenedOriginal.Sign(output, new PdfCertificateSigner(certificate));
            signed = output.ToArray();
        }

        byte[] updated;
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            document.AppendPages(doc => doc.Content(c => c.Paragraph("An appended schedule.")));
            using var output = new MemoryStream();
            document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true));
            updated = output.ToArray();
        }

        Assert.Equal(signed, updated.AsSpan(0, signed.Length).ToArray());
        using PdfDocument result = PdfDocument.Open(updated);
        Assert.Equal(2, result.Pages.Count);
        PdfSignatureValidation validation = Assert.Single(result.ValidateSignatures(new PdfSignatureValidationOptions().WithTrustAnchors(certificate)));
        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Broadside builder", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyCertSign, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
