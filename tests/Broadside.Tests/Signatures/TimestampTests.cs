using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// RFC 3161 timestamps: a signature time-stamp token on a signature (PAdES B-T; ISO 32000-2 §12.8.3.3.1, §12.8.3.4.3; RFC 3161
/// Appendix A) and a document timestamp (§12.8.5). The authority is <see cref="TestTimestampAuthority"/>, inside the test process
/// behind the <see cref="HttpClient"/> the client is given: nothing goes to the network. Tokens are checked independently with the
/// platform PKCS package (<see cref="Rfc3161TimestampToken"/>) and OpenSSL's <c>ts -verify</c>.
/// </summary>
public sealed class TimestampTests
{
    private const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";
    private const string PdfMacDataOid = "1.0.32004.1.2";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_b_t_signature_carries_a_token_whose_imprint_is_the_digest_of_its_signature_value(bool asynchronous)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        PdfSignatureOptions options = new PdfSignatureOptions()
            .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached)
            .WithTimestampClient(new PdfTimestampClient(http, TestTimestampAuthority.Address));

        using var output = new MemoryStream();
        if (asynchronous)
        {
            await document.SignAsync(output, new PdfCertificateSigner(certificate), options, TestContext.Current.CancellationToken);
        }
        else
        {
            Sign(document, output, new PdfCertificateSigner(certificate), options);
        }

        byte[] signed = output.ToArray();
        SignedFile signature = SignedFile.Last(signed);
        (SignerInfo signer, byte[] token) = SignatureTimestamp(signature);
        AssertTimestampsSignatureValue(signer, token);

        // The timestamp is unsigned: the signature itself still verifies over the byte ranges.
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));

        Rfc3161TimestampRequest request = Assert.Single(authority.Requests);
        Assert.NotNull(request.GetNonce());
        Assert.True(request.RequestSignerCertificate);
        Assert.Equal("application/timestamp-query", Assert.Single(authority.ContentTypes));
    }

    [Theory]
    [InlineData("RSA", "SHA256", PdfSignatureSubFilter.AdobePkcs7Detached, "SHA256")]
    [InlineData("RSA-PSS", "SHA384", PdfSignatureSubFilter.EtsiCadesDetached, "SHA384")]
    [InlineData("P-256", "SHA256", PdfSignatureSubFilter.EtsiCadesDetached, "SHA512")]
    [InlineData("P-384", "SHA384", PdfSignatureSubFilter.EtsiCadesDetached, "SHA256")]
    [InlineData("P-521", "SHA512", PdfSignatureSubFilter.EtsiCadesDetached, "SHA256")]
    [InlineData("P-256", "SHA3-256", PdfSignatureSubFilter.EtsiCadesDetached, "SHA3-256")]
    public void Every_signing_algorithm_gets_a_token_over_the_signature_value_its_signer_info_holds(string key, string digest, PdfSignatureSubFilter subFilter, string imprintDigest)
    {
        // An ECDSA value the platform returns as r || s is time-stamped as the DER the SignerInfo holds (RFC 3161 Appendix A).
        using X509Certificate2 certificate = key.StartsWith("P-", StringComparison.Ordinal)
            ? TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.CreateFromFriendlyName("nist" + key.Replace("-", string.Empty, StringComparison.Ordinal)))
            : TestCertificates.SelfSigned("CN=RSA signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address).WithDigestAlgorithm(new HashAlgorithmName(imprintDigest));
        var signer = new PdfCertificateSigner(certificate, rsaSignaturePadding: key == "RSA-PSS" ? RSASignaturePadding.Pss : null);
        PdfSignatureOptions options = new PdfSignatureOptions()
            .WithDigestAlgorithm(new HashAlgorithmName(digest))
            .WithSubFilter(subFilter)
            .WithTimestampClient(client);

        byte[] signed = Sign(document, signer, options);

        SignedFile signature = SignedFile.Last(signed);
        (SignerInfo signerInfo, byte[] token) = SignatureTimestamp(signature);
        Assert.True(Rfc3161TimestampToken.TryDecode(token, out Rfc3161TimestampToken? decoded, out _));
        Assert.Equal(new HashAlgorithmName(imprintDigest).Name, HashName(decoded.TokenInfo.HashAlgorithmId.Value!));
        if (PlatformHash(imprintDigest, signerInfo.GetSignature()) is { } expected)
        {
            Assert.Equal(expected, decoded.TokenInfo.GetMessageHash().ToArray());
        }

        // OpenSSL hashes the signature value itself and compares it with the imprint, SHA-3 included.
        OpenSsl.VerifyTimestamp(token, signerInfo.GetSignature(), TestTimestampAuthority.Root);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_document_timestamp_verifies_with_openssl_over_its_byte_ranges(bool asynchronous)
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        byte[] file = Corpus.Bytes("empty-page.pdf");
        using PdfDocument document = PdfDocument.Open(file);
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);

        using var output = new MemoryStream();
        if (asynchronous)
        {
            await document.TimestampAsync(output, client, cancellationToken: TestContext.Current.CancellationToken);
        }
        else
        {
            Stamp(document, output, client);
        }

        byte[] stamped = output.ToArray();
        Assert.True(stamped.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        SignedFile timestamp = SignedFile.Last(stamped);
        Assert.Equal(0, timestamp.ByteRange[0]);
        Assert.Equal(stamped.Length, timestamp.ByteRange[2] + timestamp.ByteRange[3]);
        OpenSsl.VerifyTimestamp(timestamp.Cms, timestamp.CoveredBytes(), TestTimestampAuthority.Root);
        Assert.True(Rfc3161TimestampToken.TryDecode(timestamp.Cms, out Rfc3161TimestampToken? token, out _));
        Assert.True(token.VerifySignatureForData(timestamp.CoveredBytes(), out _));
        Assert.Equal(authority.Tokens.Single(), timestamp.Cms);

        // §12.8.5.2 and Table 255: a DocTimeStamp dictionary with ETSI.RFC3161 and none of the entries that sub-filter excludes.
        using PdfDocument reopened = PdfDocument.Open(stamped);
        PdfSignatureField field = Assert.Single(reopened.AcroForm!.TerminalFields.OfType<PdfSignatureField>());
        CosDictionary dictionary = field.SignatureDictionary!;
        Assert.Equal(new CosName("DocTimeStamp"), dictionary[new CosName("Type")]);
        Assert.Equal(new CosName("Adobe.PPKLite"), dictionary[new CosName("Filter")]);
        Assert.Equal(new CosName("ETSI.RFC3161"), dictionary[new CosName("SubFilter")]);
        string[] excluded = ["M", "Name", "Reason", "Location", "ContactInfo", "Cert", "Reference", "Changes"];
        Assert.DoesNotContain(excluded, key => dictionary.ContainsKey(new CosName(key)));
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Empty(reopened.Diagnostics);

        (int exitCode, string qpdf) = ExternalTool.Run("qpdf", stamped, "--check");
        Assert.True(exitCode == 0, qpdf);
    }

    [Fact]
    public void A_document_timestamp_after_a_signature_leaves_the_signature_valid()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached).WithTimestampClient(client));
        using var output = new MemoryStream();
        document.Timestamp(output, client);

        byte[] stamped = output.ToArray();
        Assert.True(stamped.AsSpan(0, signed.Length).SequenceEqual(signed));
        IReadOnlyList<SignedFile> all = SignedFile.All(stamped);
        Assert.Equal(2, all.Count);
        OpenSsl.VerifyCms(all[0].Cms, all[0].CoveredBytes(), certificate);
        OpenSsl.VerifyTimestamp(all[1].Cms, all[1].CoveredBytes(), TestTimestampAuthority.Root);
        Assert.Equal(2, authority.Requests.Count);
    }

    [Theory]
    [InlineData(TimestampFault.WrongNonce, "nonce")]
    [InlineData(TimestampFault.MissingNonce, "no nonce")]
    [InlineData(TimestampFault.WrongImprint, "message imprint")]
    [InlineData(TimestampFault.HttpError, "HTTP 503")]
    [InlineData(TimestampFault.Garbage, "not a valid TimeStampResp")]
    [InlineData(TimestampFault.Rejection, "refused the request")]
    public void An_authority_that_fails_throws_a_clear_exception_and_leaves_the_document_as_it_was(TimestampFault fault, string message)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority { Fault = fault };
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        PdfTimestampException signing = Assert.Throws<PdfTimestampException>(
            () => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithTimestampClient(client)));
        PdfTimestampException stamping = Assert.Throws<PdfTimestampException>(() => document.Timestamp(output, client));

        Assert.Contains(message, signing.Message, StringComparison.Ordinal);
        Assert.Contains(message, stamping.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);

        // Issue #306: the save inside signing and time-stamping dated the document (adding an Info dictionary), raised it to PDF 2.0
        // for the document timestamp and laid its metadata out for 2.0 before the authority was asked; all of it is undone.
        Assert.Equal(before, CosSnapshot.Of(document));
        Assert.Equal(2, authority.Requests.Count);
    }

    [Fact]
    public async Task An_authority_that_fails_asynchronously_leaves_every_object_of_the_document_as_it_was()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority { Fault = TimestampFault.Rejection };
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<PdfTimestampException>(
            () => document.SignAsync(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithTimestampClient(client), cancellationToken));
        Assert.Equal(before, CosSnapshot.Of(document));
        await Assert.ThrowsAsync<PdfTimestampException>(() => document.TimestampAsync(output, client, cancellationToken: cancellationToken));
        Assert.Equal(before, CosSnapshot.Of(document));

        Assert.Equal(0, output.Length);
        Assert.Equal(2, authority.Requests.Count);
    }

    [Fact]
    public async Task A_refusal_reports_the_authority_status_text_and_failure_info()
    {
        using var authority = new TestTimestampAuthority { Fault = TimestampFault.Rejection };
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        PdfTimestampException exception = await Assert.ThrowsAsync<PdfTimestampException>(
            () => document.TimestampAsync(output, new PdfTimestampClient(http, TestTimestampAuthority.Address), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, exception.AuthorityStatus);
        Assert.Equal(1 << 5, exception.FailureInfo);
        Assert.Contains("rejection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unsupported request", exception.Message, StringComparison.Ordinal);
        Assert.Contains("badDataFormat", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_token_under_another_policy_than_the_requested_one_is_refused()
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        PdfTimestampException exception = Assert.Throws<PdfTimestampException>(
            () => document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address).WithPolicy("1.3.6.1.4.1.32473.9.9")));

        Assert.Contains("policy", exception.Message, StringComparison.Ordinal);
        Assert.Equal("1.3.6.1.4.1.32473.9.9", Assert.Single(authority.Requests).RequestedPolicyId?.Value);
    }

    [Fact]
    public void A_token_longer_than_the_reserved_room_is_refused_and_nothing_is_written()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority { Padding = 6000 };
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address).WithTokenSizeEstimate(4096);
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        InvalidOperationException stamping = Assert.Throws<InvalidOperationException>(() => document.Timestamp(output, client));
        InvalidOperationException signing = Assert.Throws<InvalidOperationException>(
            () => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithTimestampClient(client)));

        Assert.Contains("TokenSizeEstimate", stamping.Message, StringComparison.Ordinal);
        Assert.Contains("TokenSizeEstimate", signing.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));

        // With the room raised, the same authority's token fits.
        client.TokenSizeEstimate = 16384;
        document.Timestamp(output, client);
        SignedFile timestamp = SignedFile.Last(output.ToArray());
        OpenSsl.VerifyTimestamp(timestamp.Cms, timestamp.CoveredBytes(), TestTimestampAuthority.Root);
    }

    [Fact]
    public void A_document_timestamp_of_a_protected_document_carries_its_integrity_mac()
    {
        // ISO/TS 32004 §6.5.2: in a signed revision the MAC is an unsigned attribute of the document time stamp signature.
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("encrypted-mac.pdf"), new PdfOptions().WithPassword("owner"));
        using var output = new MemoryStream();

        document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address));

        byte[] stamped = output.ToArray();
        using PdfDocument reopened = PdfDocument.Open(stamped);
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        var authCode = (CosDictionary)reopened.Trailer[new CosName("AuthCode")];
        Assert.Equal(new CosName("AttachedToSig"), authCode[new CosName("MACLocation")]);
        Assert.Empty(reopened.Diagnostics);

        SignedFile timestamp = SignedFile.Last(stamped);
        var cms = new SignedCms();
        cms.Decode(timestamp.Cms);
        Assert.Single(cms.SignerInfos[0].UnsignedAttributes.Cast<CryptographicAttributeObject>(), attribute => attribute.Oid.Value == PdfMacDataOid);

        // The MAC is unsigned: the authority's token still verifies over the byte ranges.
        OpenSsl.VerifyTimestamp(timestamp.Cms, timestamp.CoveredBytes(), TestTimestampAuthority.Root);
    }

    [Fact]
    public void A_b_t_signature_of_a_protected_document_carries_both_the_mac_and_the_token()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("encrypted-mac.pdf"), new PdfOptions().WithPassword("owner"));
        PdfSignatureOptions options = new PdfSignatureOptions().WithTimestampClient(new PdfTimestampClient(http, TestTimestampAuthority.Address));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), options);

        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(PdfIntegrityStatus.Verified, reopened.Security!.Integrity);
        SignedFile signature = SignedFile.Last(signed);
        (SignerInfo signer, byte[] token) = SignatureTimestamp(signature);
        Assert.Single(signer.UnsignedAttributes.Cast<CryptographicAttributeObject>(), attribute => attribute.Oid.Value == PdfMacDataOid);
        AssertTimestampsSignatureValue(signer, token);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
    }

    [Fact]
    public void Strict_mode_refuses_a_document_timestamp_of_a_pdf_1_7_document_before_asking_the_authority()
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"), new PdfOptions().UseStrict());
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Diagnostics.DiagnosticException exception = Assert.Throws<Diagnostics.DiagnosticException>(
            () => document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address)));

        Assert.Contains("document timestamp", exception.Message, StringComparison.Ordinal);
        Assert.Empty(authority.Requests);
        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_failed_document_timestamp_leaves_the_version_and_the_next_save_raises_nothing()
    {
        using var authority = new TestTimestampAuthority { Fault = TimestampFault.HttpError };
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<PdfTimestampException>(() => document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address)));

        using var saved = new MemoryStream();
        document.Save(saved, new PdfSaveOptions().WithIncrementalUpdate(true));
        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
        Assert.Null(reopened.AcroForm);
    }

    [Fact]
    public void The_client_validates_its_settings()
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        Assert.Throws<ArgumentException>(() => new PdfTimestampClient(http, new Uri("/tsr", UriKind.Relative)));
        var client = new PdfTimestampClient(http, TestTimestampAuthority.Address);
        Assert.Equal(HashAlgorithmName.SHA256, client.DigestAlgorithm);
        Assert.Equal(8192, client.TokenSizeEstimate);
        Assert.Null(client.Policy);
        Assert.Throws<NotSupportedException>(() => client.WithDigestAlgorithm(HashAlgorithmName.MD5));
        Assert.Throws<ArgumentException>(() => client.WithPolicy("not an oid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.WithTokenSizeEstimate(0));
    }

    private static void Stamp(PdfDocument document, Stream output, PdfTimestampClient client) => document.Timestamp(output, client);

    private static void Sign(PdfDocument document, Stream output, IPdfSigner signer, PdfSignatureOptions options) => document.Sign(output, signer, options);

    private static byte[] Sign(PdfDocument document, IPdfSigner signer, PdfSignatureOptions options)
    {
        using var output = new MemoryStream();
        document.Sign(output, signer, options);
        return output.ToArray();
    }

    /// <summary>The signer of the signature and the single value of its signature time-stamp attribute.</summary>
    private static (SignerInfo Signer, byte[] Token) SignatureTimestamp(SignedFile signature)
    {
        var cms = new SignedCms();
        cms.Decode(signature.Cms);
        SignerInfo signer = Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
        CryptographicAttributeObject attribute = Assert.Single(signer.UnsignedAttributes.Cast<CryptographicAttributeObject>(), item => item.Oid.Value == SignatureTimeStampOid);
        return (signer, Assert.Single(attribute.Values.Cast<AsnEncodedData>()).RawData);
    }

    /// <summary>RFC 3161 Appendix A: the token's imprint is the hash of the SignerInfo's signature value, checked three ways.</summary>
    private static void AssertTimestampsSignatureValue(SignerInfo signer, byte[] token)
    {
        Assert.True(Rfc3161TimestampToken.TryDecode(token, out Rfc3161TimestampToken? decoded, out _));
        Assert.Equal(SHA256.HashData(signer.GetSignature()), decoded.TokenInfo.GetMessageHash().ToArray());
        Assert.True(decoded.VerifySignatureForSignerInfo(signer, out X509Certificate2? unit));
        using (unit)
        {
            Assert.Equal(TestTimestampAuthority.Unit.Thumbprint, unit!.Thumbprint);
        }
        OpenSsl.VerifyTimestamp(token, signer.GetSignature(), TestTimestampAuthority.Root);
    }

    /// <summary>The platform's hash of <paramref name="data"/>, or <see langword="null"/> where it has no SHA-3 (macOS).</summary>
    private static byte[]? PlatformHash(string algorithm, byte[] data) => algorithm switch
    {
        "SHA256" => SHA256.HashData(data),
        "SHA384" => SHA384.HashData(data),
        "SHA512" => SHA512.HashData(data),
        _ => SHA3_256.IsSupported ? SHA3_256.HashData(data) : null,
    };

    private static string HashName(string oid) => oid switch
    {
        "2.16.840.1.101.3.4.2.1" => "SHA256",
        "2.16.840.1.101.3.4.2.2" => "SHA384",
        "2.16.840.1.101.3.4.2.3" => "SHA512",
        "2.16.840.1.101.3.4.2.8" => "SHA3-256",
        _ => oid,
    };
}
