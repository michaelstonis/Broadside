using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside;
using Broadside.Samples;
using Broadside.Signatures;

Sample.Start("Signatures");

using var services = new InProcessServices();
using var http = new HttpClient(services, disposeHandler: false); // in an application: the HttpClient your app already uses
X509Certificate2 certificate = services.Signer;
X509Certificate2 root = services.Root;

SignBaseline(certificate, root);
SignWithTimestamp(certificate, root, http);
AddLongTermValidation(root, http);
Archive(http);
await SignExternally(certificate, root);
await SignDeferred(certificate, root);
Validate(root, services.Requests);
RemoveSignatures();
Sample.Finish();

static void SignBaseline(X509Certificate2 certificate, X509Certificate2 root)
{
    #region sign
    using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
    var signer = new PdfCertificateSigner(certificate, [root]); // the certificate with its private key, and its chain
    PdfSignatureOptions options = new PdfSignatureOptions()
        .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached) // PAdES; AdobePkcs7Detached for readers older than PDF 2.0
        .WithReason("Approved")
        .WithLocation("London")
        .WithAppearance(new PdfSignatureAppearance(pageIndex: 0, new PdfRectangle(72, 600, 272, 650)).WithText("Approved by Ada Lovelace"));

    using FileStream output = File.Create("signed-b-b.pdf");
    document.Sign(output, signer, options); // writes the document and the signature as an incremental update
    #endregion
}

static void SignWithTimestamp(X509Certificate2 certificate, X509Certificate2 root, HttpClient http)
{
    #region timestamp
    using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
    var timestamps = new PdfTimestampClient(http, InProcessServices.TimestampAddress); // the client is injected, never created
    PdfSignatureOptions options = new PdfSignatureOptions()
        .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached)
        .WithTimestampClient(timestamps); // PAdES B-T: a trusted time on the signature

    using FileStream output = File.Create("signed-b-t.pdf");
    document.Sign(output, new PdfCertificateSigner(certificate, [root]), options);
    #endregion
}

static void AddLongTermValidation(X509Certificate2 root, HttpClient http)
{
    #region ltv
    // PAdES B-LT: the certificates and revocation data every signature needs, in a document security store, so the file can be
    // validated later without the network. Revocation data is fetched through the client given, and only through it.
    using PdfDocument document = PdfDocument.Open("signed-b-t.pdf");
    PdfSignatureValidationOptions options = new PdfSignatureValidationOptions()
        .WithTrustAnchors(root)
        .UseOnlineRevocation(http);

    using FileStream output = File.Create("signed-b-lt.pdf");
    document.AddValidationData(output, options);
    #endregion
}

static void Archive(HttpClient http)
{
    #region archive
    // PAdES B-LTA: a document timestamp over everything, the validation data included. Repeat it before the timestamp's own
    // certificate expires to keep the file verifiable.
    using PdfDocument document = PdfDocument.Open("signed-b-lt.pdf");
    using FileStream output = File.Create("signed-b-lta.pdf");
    document.Timestamp(output, new PdfTimestampClient(http, InProcessServices.TimestampAddress));
    #endregion
}

static async Task SignExternally(X509Certificate2 certificate, X509Certificate2 root)
{
    // The private key stays with the "remote service"; the signer only ever sees the certificate's public part.
    using RSA remoteKey = certificate.GetRSAPrivateKey()!;
    using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);

    #region external-signer
    using PdfDocument document = await PdfDocument.OpenAsync("text-standard14.pdf");
    var signer = new RemoteSigner(publicOnly, [root], remoteKey);
    await using FileStream output = File.Create("signed-external.pdf");
    await document.SignAsync(output, signer, new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached));
    #endregion
}

static async Task SignDeferred(X509Certificate2 certificate, X509Certificate2 root)
{
    // The key is with a remote signing service that answers minutes or days later, in another process.
    using RSA remoteKey = certificate.GetRSAPrivateKey()!;
    using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
    PrepareDeferred(publicOnly, root);
    await CompleteDeferred(remoteKey);
}

static void PrepareDeferred(X509Certificate2 publicOnly, X509Certificate2 root)
{
    #region deferred-prepare
    // Step 1: reserve the signature and keep the prepared file and the state; neither holds a private key.
    using (PdfDocument document = PdfDocument.Open("text-standard14.pdf"))
    using (FileStream prepared = File.Create("signed-deferred.pdf"))
    using (FileStream state = File.Create("signed-deferred.state"))
    {
        PdfPreparedSignature signature = document.PrepareSignature(prepared, publicOnly, [root]);
        signature.Save(state);
        // Send signature.DataToSign (with signature.DigestAlgorithm) to the signing service.
    }
    #endregion
}

static async Task CompleteDeferred(RSA remoteKey)
{
    #region deferred-complete
    // Step 2, later and anywhere: open the state, and write the value the service returned into the prepared file in place.
    PdfPreparedSignature pending;
    using (FileStream state = File.OpenRead("signed-deferred.state"))
    {
        pending = PdfPreparedSignature.Open(state);
    }

    byte[] signatureValue = remoteKey.SignHash(pending.DataToSign.ToArray(), pending.DigestAlgorithm, pending.RsaSignaturePadding!);
    await using FileStream file = new("signed-deferred.pdf", FileMode.Open, FileAccess.ReadWrite);
    await PdfDocument.CompleteSignatureAsync(file, pending, signatureValue);
    #endregion
}

static void Validate(X509Certificate2 root, int requests)
{
    #region validate
    using PdfDocument document = PdfDocument.Open("signed-b-lta.pdf");
    PdfSignatureValidationOptions options = new PdfSignatureValidationOptions()
        .WithTrustAnchors(root)                 // no path is trusted until you name the anchors you trust
        .WithRequireRevocationStatus(true);     // the store in the file must answer for every certificate; nothing is fetched

    foreach (PdfSignatureValidation signature in document.ValidateSignatures(options))
    {
        Console.WriteLine($"{signature.FieldName}: {signature.Status}, signed by {signature.SignerCertificate?.Subject}");
        Console.WriteLine($"  timestamp: {signature.Timestamp?.Time:u} ({signature.Timestamp?.Status})");
        foreach (PdfSignatureProblem problem in signature.Problems)
        {
            Console.WriteLine($"  problem: {problem.Message}");
        }
    }
    #endregion

    IReadOnlyList<PdfSignatureValidation> results = document.ValidateSignatures(options);
    Sample.Expect(results.Count == 2, "the signature and the document timestamp");
    Sample.Expect(results.All(result => result.Status == PdfSignatureStatus.Valid), string.Join("; ", results.SelectMany(result => result.Problems)));
    Sample.Expect(requests > 0, "the in-process services answered");
    foreach (string file in new[] { "signed-b-b.pdf", "signed-external.pdf", "signed-deferred.pdf" })
    {
        using PdfDocument signed = PdfDocument.Open(file);
        Sample.Expect(signed.ValidateSignatures(new PdfSignatureValidationOptions().WithTrustAnchors(root))[0].Status == PdfSignatureStatus.Valid, file);
    }
}

static void RemoveSignatures()
{
    #region remove
    using PdfDocument document = PdfDocument.Open("signed-b-lta.pdf");
    PdfSignatureRemoval removal = document.RemoveSignatures(); // keeps the emptied fields; WithRemoveFields(true) removes them

    // A full rewrite without unreachable objects: an incremental save would keep the signatures in the earlier revision.
    using FileStream output = File.Create("unsigned.pdf");
    document.Save(output, removal.CreateSaveOptions());
    #endregion

    Sample.Expect(removal.SignaturesRemoved == 2, "the signature and the document timestamp");
    output.Close();
    using PdfDocument unsigned = PdfDocument.Open("unsigned.pdf");
    Sample.Expect(unsigned.ValidateSignatures().Count == 0, "no signature is left");
}

#region external-signer-class
/// <summary>A signer whose private key is somewhere else: a hardware token, a cloud key service or a signing server.</summary>
internal sealed class RemoteSigner(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain, RSA remoteKey) : IPdfSigner
{
    public X509Certificate2 Certificate => certificate;

    public IReadOnlyList<X509Certificate2> CertificateChain => chain;

    // Called by Sign. Send the digest to the service and return the raw signature value it computes.
    public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) =>
        remoteKey.SignHash(digest.ToArray(), digestAlgorithm, RSASignaturePadding.Pkcs1);

    // Called by SignAsync: await the service here instead of blocking a thread.
    public async ValueTask<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, HashAlgorithmName digestAlgorithm, CancellationToken cancellationToken)
    {
        await Task.Yield(); // stands in for the round trip to the service
        return SignDigest(digest.Span, digestAlgorithm);
    }
}
#endregion
