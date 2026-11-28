---
title: "Signatures"
---

# Signatures

This guide signs documents at each PAdES baseline level, adds timestamps and long-term validation data, signs with a key held elsewhere, and validates signatures. The code comes from the [`Signatures`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/Signatures) sample; CI builds and runs it on every change, checks its files with `qpdf --check`, and verifies the signed ones with poppler's `pdfsig`.

**Nothing in the library goes to the network on its own.** Every option that needs a time-stamping authority or revocation data takes an `HttpClient` you supply, so your application decides about proxies, timeouts, retries and logging. The sample's certification and time-stamping authorities run inside its own process ([`InProcessServices.cs`](https://github.com/michaelstonis/Broadside/blob/main/samples/guides/Signatures/InProcessServices.cs)), behind the `HttpClient` it passes, so it never touches the network either.

| Feature | Minimum PDF version |
|---|---|
| A signature field and a byte-range signature (`adbe.pkcs7.detached`) | 1.3 |
| An RSA key over 1024 bits, a certification signature, a field lock | 1.5 |
| A SHA-256 digest, a signature time-stamp | 1.6 |
| A PAdES signature (`ETSI.CAdES.detached`), an ECDSA key, a document timestamp, a document security store | 2.0 |

A signature is written as an incremental update, after the document's bytes: signing never changes what is already in the file, so earlier signatures stay valid.

## Signing (PAdES B-B)

`Sign` writes the document and its signature to a stream. `PdfCertificateSigner` signs with a certificate's private key (RSA or ECDSA), and embeds the chain you give it. `PdfSignatureSubFilter.EtsiCadesDetached` makes a PAdES signature; `AdobePkcs7Detached`, the default, suits readers older than PDF 2.0. `WithAppearance` draws the signature on a page; without it the signature is invisible.

<!-- snippet: samples/guides/Signatures/Program.cs#sign -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
var signer = new PdfCertificateSigner(certificate, [root]); // the certificate with its private key, and its chain
PdfSignatureOptions options = new PdfSignatureOptions()
    .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached) // PAdES; AdobePkcs7Detached for readers older than PDF 2.0
    .WithReason("Approved")
    .WithLocation("London")
    .WithAppearance(new PdfSignatureAppearance(pageIndex: 0, new PdfRectangle(72, 600, 272, 650)).WithText("Approved by Ada Lovelace"));

using FileStream output = File.Create("signed-b-b.pdf");
document.Sign(output, signer, options); // writes the document and the signature as an incremental update
```
<!-- endsnippet -->

`WithCertification` makes a certification signature, which says what later changes are allowed, and `WithFieldLock` locks form fields when the signature is applied.

## Timestamps (PAdES B-T)

A time-stamp from a trusted authority proves the signature existed at that time, which a validator uses instead of the signer's own clock. `PdfTimestampClient` speaks RFC 3161 through the `HttpClient` you give it:

<!-- snippet: samples/guides/Signatures/Program.cs#timestamp -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf");
var timestamps = new PdfTimestampClient(http, InProcessServices.TimestampAddress); // the client is injected, never created
PdfSignatureOptions options = new PdfSignatureOptions()
    .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached)
    .WithTimestampClient(timestamps); // PAdES B-T: a trusted time on the signature

using FileStream output = File.Create("signed-b-t.pdf");
document.Sign(output, new PdfCertificateSigner(certificate, [root]), options);
```
<!-- endsnippet -->

## Long-term validation (PAdES B-LT)

A signature can only be checked while its certificates and their revocation status can be found. `AddValidationData` gathers them now (the chains, CRLs and OCSP responses for the signer and the time-stamp authority) and stores them in the document's security store in an incremental update, so the file can be validated later without the network. Revocation data comes from the responses you pass in `WithOcspResponses` and `WithCertificateRevocationLists`, and is fetched only when `UseOnlineRevocation` gives a client:

<!-- snippet: samples/guides/Signatures/Program.cs#ltv -->
```csharp
// PAdES B-LT: the certificates and revocation data every signature needs, in a document security store, so the file can be
// validated later without the network. Revocation data is fetched through the client given, and only through it.
using PdfDocument document = PdfDocument.Open("signed-b-t.pdf");
PdfSignatureValidationOptions options = new PdfSignatureValidationOptions()
    .WithTrustAnchors(root)
    .UseOnlineRevocation(http);

using FileStream output = File.Create("signed-b-lt.pdf");
document.AddValidationData(output, options);
```
<!-- endsnippet -->

## Archiving (PAdES B-LTA)

A document timestamp covers the whole file, validation data included, and keeps it verifiable after the signer's certificate expires. Add another, with new validation data, before the last timestamp's certificate expires:

<!-- snippet: samples/guides/Signatures/Program.cs#archive -->
```csharp
// PAdES B-LTA: a document timestamp over everything, the validation data included. Repeat it before the timestamp's own
// certificate expires to keep the file verifiable.
using PdfDocument document = PdfDocument.Open("signed-b-lt.pdf");
using FileStream output = File.Create("signed-b-lta.pdf");
document.Timestamp(output, new PdfTimestampClient(http, InProcessServices.TimestampAddress));
```
<!-- endsnippet -->

## External signers

When the private key is in a hardware token, a smart card, a cloud key service or a signing server, implement `IPdfSigner`. The library computes the digest of what is signed and calls `SignDigest` (from `Sign`) or `SignDigestAsync` (from `SignAsync`); you send the digest to the key and return the raw signature value. The private key never has to be in the process:

<!-- snippet: samples/guides/Signatures/Program.cs#external-signer-class -->
```csharp
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
```
<!-- endsnippet -->

<!-- snippet: samples/guides/Signatures/Program.cs#external-signer -->
```csharp
using PdfDocument document = await PdfDocument.OpenAsync("text-standard14.pdf");
var signer = new RemoteSigner(publicOnly, [root], remoteKey);
await using FileStream output = File.Create("signed-external.pdf");
await document.SignAsync(output, signer, new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached));
```
<!-- endsnippet -->

For an RSA key, `RsaSignaturePadding` chooses PKCS #1 v1.5 (the default) or PSS; an ECDSA signer returns the DER-encoded signature value.

## Deferred signing

Some signing services answer minutes or days later, in another process: a qualified signature creation device, a cloud signing service, a smart card in the user's browser, an approval workflow. `PrepareSignature` does everything `Sign` does before it asks the signer: it places the field, draws the appearance, writes the prepared file with the signature's `Contents` zero-filled, and returns a `PdfPreparedSignature`. Send its `DataToSign` (the digest of the CMS signed attributes, what `IPdfSigner.SignDigest` receives) to the service, and save its state with `Save`. The state is a small, versioned, opaque blob with the certificates and the byte ranges' digest; it holds no private key and none of the document.

<!-- snippet: samples/guides/Signatures/Program.cs#deferred-prepare -->
```csharp
// Step 1: reserve the signature and keep the prepared file and the state; neither holds a private key.
using (PdfDocument document = PdfDocument.Open("text-standard14.pdf"))
using (FileStream prepared = File.Create("signed-deferred.pdf"))
using (FileStream state = File.Create("signed-deferred.state"))
{
    PdfPreparedSignature signature = document.PrepareSignature(prepared, publicOnly, [root]);
    signature.Save(state);
    // Send signature.DataToSign (with signature.DigestAlgorithm) to the signing service.
}
```
<!-- endsnippet -->

When the value comes back, `Open` the state and call `CompleteSignature` (or `CompleteSignatureAsync`) on the prepared file, opened for reading and writing. It hashes the byte ranges again and refuses a file that changed after preparation or was already completed, checks the value against the certificate, builds the CMS object, and writes it in place. Nothing is written unless every check passes.

<!-- snippet: samples/guides/Signatures/Program.cs#deferred-complete -->
```csharp
// Step 2, later and anywhere: open the state, and write the value the service returned into the prepared file in place.
PdfPreparedSignature pending;
using (FileStream state = File.OpenRead("signed-deferred.state"))
{
    pending = PdfPreparedSignature.Open(state);
}

byte[] signatureValue = remoteKey.SignHash(pending.DataToSign.ToArray(), pending.DigestAlgorithm, pending.RsaSignaturePadding!);
await using FileStream file = new("signed-deferred.pdf", FileMode.Open, FileAccess.ReadWrite);
await PdfDocument.CompleteSignatureAsync(file, pending, signatureValue);
```
<!-- endsnippet -->

To time-stamp the signature (PAdES B-T), set `PdfSignatureOptions.TimestampClient` when preparing, which reserves the room, and pass the client to `CompleteSignature`, which asks the authority. A service that returns a whole CMS `SignedData` instead of a signature value goes through `CompleteSignatureWithCms`, which checks that the object signs the prepared byte ranges' digest with the prepared certificate. A document protected by an ISO/TS 32004 integrity MAC cannot be signed in two steps; sign it with `Sign`.

## Validation

`ValidateSignatures` checks every signature and document timestamp: the digest over the signed bytes, the signature value, the certificate path to a trust anchor you choose, revocation, the time-stamp, and every change made after the signature (each listed in `Modifications`, permitted or not). There are no trust anchors by default, so no certificate path is trusted until you pass the anchors you trust.

<!-- snippet: samples/guides/Signatures/Program.cs#validate -->
```csharp
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
```
<!-- endsnippet -->

`Status` is `Valid`, `Invalid` (the file was changed or the signature is wrong) or `Indeterminate` (it could not be decided, for example without a trusted path or revocation status), and `Problems` says why. `ValidateSignaturesAsync` does the same without blocking while it fetches revocation data through `UseOnlineRevocation`.

## Removing signatures

To reuse a signed contract as a template, strip a test signature, or remove a certification so a form can be edited again, `RemoveSignatures` clears every signed field: the field's value goes, its widgets get the empty appearance of a new signature field, the catalog's certification (`Perms` `DocMDP`) goes with its signature, and the form's `SigFlags` is recomputed. The fields stay, ready to be signed again, unless `WithRemoveFields(true)` removes them with their widgets. Document timestamps are removed too unless `WithIncludeDocumentTimestamps(false)` keeps them, and the document security store (`DSS`) goes once no signature is left. For one field, call `PdfSignatureField.ClearSignature` or `PdfAcroForm.RemoveField`.

<!-- snippet: samples/guides/Signatures/Program.cs#remove -->
```csharp
using PdfDocument document = PdfDocument.Open("signed-b-lta.pdf");
PdfSignatureRemoval removal = document.RemoveSignatures(); // keeps the emptied fields; WithRemoveFields(true) removes them

// A full rewrite without unreachable objects: an incremental save would keep the signatures in the earlier revision.
using FileStream output = File.Create("unsigned.pdf");
document.Save(output, removal.CreateSaveOptions());
```
<!-- endsnippet -->

Removing a signature does not remove its bytes from the file you opened. An incremental save appends to that file, so the earlier revision still holds every signature and a validator can still find them; save with `CreateSaveOptions`, a full rewrite that leaves out the objects nothing reaches any more. A certification that permits no changes does not prevent removal: it is your copy, and modification detection is a validation concern.

Conformance: [ISO 32000-2 §12.8 (digital signatures), §12.7.5.5 (signature fields)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md), [ISO/TS 32001 (SHA-3)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-ts-32001.md) and [ISO/TS 32002 (ECDSA)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-ts-32002.md).
