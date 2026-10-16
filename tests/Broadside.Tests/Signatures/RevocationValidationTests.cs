using System.Security.Cryptography.X509Certificates;
using Broadside.Objects;
using Broadside.Signatures;
using static Broadside.Tests.Signatures.RevocationTestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Revocation checking during signature validation through <see cref="PdfDocument.ValidateSignatures"/>: OCSP responses (RFC 6960) and
/// CRLs (RFC 5280 §5) from the signature (ISO 32000-2 §12.8.3.3.2, RFC 5652 §10.2.1), the document security store (§12.8.4.3) and the
/// options, and fetched through an injected client only when the caller opts in. The authority, its OCSP responder and its CRL are
/// <see cref="TestRevocationAuthority"/>, inside the test process: nothing goes to the network.
/// </summary>
public sealed class RevocationValidationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddMinutes(5);

    [Fact]
    public void A_revoked_signer_reports_revoked_from_a_crl_the_caller_gives()
    {
        using Hierarchy pki = new();
        pki.Authority.Revoke(pki.Signer, DateTimeOffset.UtcNow.AddHours(-1));
        byte[] signed = Signed(pki.Signer, [pki.Root]);

        PdfSignatureValidation validation = ValidateSingle(signed, pki.Options().WithCertificateRevocationLists(pki.Authority.Crl()));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.CertificateRevoked], validation.Problems.Select(static problem => problem.Kind));
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal(pki.Signer.Thumbprint, revocation.Certificate.Thumbprint);
        Assert.Equal(PdfRevocationStatus.Revoked, revocation.Status);
        Assert.Equal(PdfRevocationSource.Crl, revocation.Source);
        Assert.Equal(X509RevocationReason.KeyCompromise, revocation.Reason);
        Assert.False(revocation.Fetched);
    }

    [Fact]
    public void A_good_signer_with_an_embedded_ocsp_response_reports_good_offline()
    {
        using Hierarchy pki = new();
        using var network = new TestRevocationAuthority(pki.Root);
        using var http = new HttpClient(network, disposeHandler: false);
        byte[] signed = Signed(pki.Signer, [pki.Root], signer => signer.SignedAttributes.Add(RevocationInfoArchival([pki.Authority.Ocsp(pki.Signer)], [])));

        PdfSignatureValidation validation = ValidateSingle(signed, pki.Options().WithRequireRevocationStatus(true));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal(PdfRevocationStatus.Good, revocation.Status);
        Assert.Equal(PdfRevocationSource.Ocsp, revocation.Source);
        Assert.False(revocation.Fetched);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public void A_revoked_signer_reports_revoked_from_an_embedded_ocsp_response()
    {
        using Hierarchy pki = new();
        pki.Authority.Revoke(pki.Signer, DateTimeOffset.UtcNow.AddHours(-1), X509RevocationReason.Superseded);
        byte[] signed = Signed(pki.Signer, [pki.Root], signer => signer.SignedAttributes.Add(RevocationInfoArchival([pki.Authority.Ocsp(pki.Signer)], [])));

        PdfSignatureValidation validation = ValidateSingle(signed, pki.Options());

        Assert.Equal([PdfSignatureProblemKind.CertificateRevoked], validation.Problems.Select(static problem => problem.Kind));
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal((PdfRevocationStatus.Revoked, PdfRevocationSource.Ocsp, X509RevocationReason.Superseded), (revocation.Status, revocation.Source, revocation.Reason));
    }

    [Theory]
    [InlineData("crl")]
    [InlineData("ocsp")]
    public void Revocation_information_in_the_cms_crls_field_is_used(string kind)
    {
        using Hierarchy pki = new();
        pki.Authority.Revoke(pki.Signer, DateTimeOffset.UtcNow.AddHours(-1));
        byte[] signed = Signed(pki.Signer, [pki.Root], edit: cms => WithRevocationChoices(cms, kind == "crl" ? pki.Authority.Crl() : null, kind == "ocsp" ? pki.Authority.Ocsp(pki.Signer) : null));

        PdfSignatureValidation validation = ValidateSingle(signed, pki.Options());

        Assert.Equal([PdfSignatureProblemKind.CertificateRevoked], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal(kind == "crl" ? PdfRevocationSource.Crl : PdfRevocationSource.Ocsp, Assert.Single(validation.Revocations).Source);
    }

    [Fact]
    public void The_document_security_store_supplies_the_intermediate_and_the_revocation_data()
    {
        using Hierarchy pki = new(intermediate: true);
        using var network = new TestRevocationAuthority(pki.Root);
        byte[] signed = Signed(pki.Signer, []);

        // ISO 32000-2 §12.8.4.3: the store is added in a later incremental update.
        byte[] stored;
        using (PdfDocument document = PdfDocument.Open(signed))
        {
            var dss = new CosDictionary
            {
                [new CosName("Certs")] = new CosArray([document.AddObject(Stream(pki.Intermediate!.RawData))]),
                [new CosName("OCSPs")] = new CosArray([document.AddObject(Stream(pki.Authority.Ocsp(pki.Signer))), document.AddObject(Stream(network.Ocsp(pki.Intermediate!)))]),
            };
            document.Catalog[new CosName("DSS")] = document.AddObject(dss);
            using var output = new MemoryStream();
            document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true));
            stored = output.ToArray();
        }

        PdfSignatureValidation before = ValidateSingle(signed, pki.Options());
        PdfSignatureValidation after = ValidateSingle(stored, pki.Options().WithRequireRevocationStatus(true));

        Assert.Contains(before.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ChainIncomplete);
        Assert.Equal(PdfSignatureStatus.Valid, after.Status);
        Assert.False(after.CoversWholeFile);
        Assert.Equal([pki.Signer.Thumbprint, pki.Intermediate!.Thumbprint, pki.Root.Thumbprint], after.CertificatePath.Select(static certificate => certificate.Thumbprint));
        Assert.Equal([PdfRevocationStatus.Good, PdfRevocationStatus.Good], after.Revocations.Select(static revocation => revocation.Status));
    }

    [Fact]
    public void Without_revocation_data_the_status_is_not_checked_and_requiring_it_makes_the_signature_indeterminate()
    {
        using Hierarchy pki = new();
        byte[] signed = Signed(pki.Signer, [pki.Root]);

        PdfSignatureValidation lenient = ValidateSingle(signed, pki.Options());
        PdfSignatureValidation required = ValidateSingle(signed, pki.Options().WithRequireRevocationStatus(true));

        Assert.Equal(PdfSignatureStatus.Valid, lenient.Status);
        Assert.Equal(PdfRevocationStatus.NotChecked, Assert.Single(lenient.Revocations).Status);
        Assert.Equal(PdfSignatureStatus.Indeterminate, required.Status);
        Assert.Equal([PdfSignatureProblemKind.RevocationUnknown], required.Problems.Select(static problem => problem.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_request_is_sent_unless_online_checking_is_enabled(bool asynchronous)
    {
        using Hierarchy pki = new(revocationAddresses: true);
        using var network = new TestRevocationAuthority(pki.Root);
        using var http = new HttpClient(network, disposeHandler: false);
        byte[] signed = Signed(pki.Signer, [pki.Root]);
        using PdfDocument document = PdfDocument.Open(signed);

        IReadOnlyList<PdfSignatureValidation> offline = asynchronous
            ? await document.ValidateSignaturesAsync(pki.Options(), TestContext.Current.CancellationToken)
            : Validate(document, pki.Options());
        Assert.Empty(network.Requests);
        Assert.Equal(PdfRevocationStatus.NotChecked, Assert.Single(Assert.Single(offline).Revocations).Status);

        PdfSignatureValidationOptions online = pki.Options().UseOnlineRevocation(http).WithRequireRevocationStatus(true);
        PdfSignatureValidation validation = Assert.Single(asynchronous
            ? await document.ValidateSignaturesAsync(online, TestContext.Current.CancellationToken)
            : Validate(document, online));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal((PdfRevocationStatus.Good, PdfRevocationSource.Ocsp, true), (revocation.Status, revocation.Source, revocation.Fetched));
        Assert.Equal([$"POST {TestRevocationAuthority.OcspAddress}"], network.Requests);
    }

    [Fact]
    public void Online_checking_falls_back_to_the_crl_and_finds_a_revocation()
    {
        using Hierarchy pki = new(revocationAddresses: true);
        using var network = new TestRevocationAuthority(pki.Root) { OcspOffline = true };
        network.Revoke(pki.Signer, DateTimeOffset.UtcNow.AddDays(-1), X509RevocationReason.AffiliationChanged);
        using var http = new HttpClient(network, disposeHandler: false);
        byte[] signed = Signed(pki.Signer, [pki.Root]);

        PdfSignatureValidation validation = ValidateSingle(signed, pki.Options().UseOnlineRevocation(http));

        Assert.Equal([PdfSignatureProblemKind.CertificateRevoked], validation.Problems.Select(static problem => problem.Kind));
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal((PdfRevocationStatus.Revoked, PdfRevocationSource.Crl, true), (revocation.Status, revocation.Source, revocation.Fetched));
        Assert.Equal([$"POST {TestRevocationAuthority.OcspAddress}", $"GET {TestRevocationAuthority.CrlAddress}"], network.Requests);
    }

    [Theory]
    [InlineData(true, false, PdfRevocationStatus.Good)]
    [InlineData(false, false, PdfRevocationStatus.NotChecked)]
    [InlineData(true, true, PdfRevocationStatus.NotChecked)]
    public void A_delegated_responder_counts_only_with_the_ocsp_signing_usage_and_a_good_signature(bool ocspSigning, bool corrupt, PdfRevocationStatus expected)
    {
        using Hierarchy pki = new();
        using X509Certificate2 responder = pki.Authority.IssueResponder(ocspSigning);
        byte[] response = pki.Authority.Ocsp(pki.Signer, responder: responder, corruptSignature: corrupt);

        PdfSignatureValidation validation = ValidateSingle(Signed(pki.Signer, [pki.Root]), pki.Options().WithOcspResponses(response));

        Assert.Equal(expected, Assert.Single(validation.Revocations).Status);
    }

    [Fact]
    public void A_response_signed_by_another_authority_is_not_used()
    {
        using Hierarchy pki = new();
        using Hierarchy other = new();
        other.Authority.Revoke(pki.Signer, DateTimeOffset.UtcNow.AddHours(-1));

        PdfSignatureValidation validation = ValidateSingle(Signed(pki.Signer, [pki.Root]), pki.Options().WithCertificateRevocationLists(other.Authority.Crl()).WithOcspResponses(other.Authority.Ocsp(pki.Signer)));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal(PdfRevocationStatus.NotChecked, Assert.Single(validation.Revocations).Status);
    }

    [Fact]
    public void A_response_whose_next_update_passed_before_the_validation_time_does_not_establish_the_status()
    {
        using Hierarchy pki = new();
        byte[] stale = pki.Authority.Ocsp(pki.Signer, producedAt: DateTimeOffset.UtcNow.AddDays(-3), nextUpdate: DateTimeOffset.UtcNow.AddDays(-2));
        byte[] staleCrl = pki.Authority.Crl(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-2));

        PdfSignatureValidation validation = ValidateSingle(Signed(pki.Signer, [pki.Root]), pki.Options().WithOcspResponses(stale).WithCertificateRevocationLists(staleCrl).WithRequireRevocationStatus(true));

        Assert.Equal([PdfSignatureProblemKind.RevocationUnknown], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_responder_that_does_not_know_the_certificate_is_reported()
    {
        using Hierarchy pki = new();
        pki.Authority.AnswerUnknown = true;

        PdfSignatureValidation validation = ValidateSingle(Signed(pki.Signer, [pki.Root]), pki.Options().WithOcspResponses(pki.Authority.Ocsp(pki.Signer)));

        Assert.Equal([PdfSignatureProblemKind.RevocationUnknown], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal(PdfRevocationStatus.Unknown, Assert.Single(validation.Revocations).Status);
    }

    [Fact]
    public void A_revocation_after_the_validation_time_is_shown_but_is_not_a_problem()
    {
        using Hierarchy pki = new();
        pki.Authority.Revoke(pki.Signer, Now.AddHours(1), X509RevocationReason.CessationOfOperation);

        PdfSignatureValidation validation = ValidateSingle(Signed(pki.Signer, [pki.Root]), pki.Options().WithCertificateRevocationLists(pki.Authority.Crl()));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        PdfCertificateRevocation revocation = Assert.Single(validation.Revocations);
        Assert.Equal(PdfRevocationStatus.Revoked, revocation.Status);
        Assert.Equal(Now.AddHours(1).ToUnixTimeSeconds(), revocation.RevocationTime?.ToUnixTimeSeconds());
    }

    private static CosStream Stream(byte[] data) => new(new CosDictionary(), data);

    private static IReadOnlyList<PdfSignatureValidation> Validate(PdfDocument document, PdfSignatureValidationOptions options) => document.ValidateSignatures(options);

    /// <summary>A root authority (with its revocation services), an optional intermediate, and a signer certificate.</summary>
    private sealed class Hierarchy : IDisposable
    {
        public Hierarchy(bool intermediate = false, bool revocationAddresses = false)
        {
            Root = ValidationCertificates.Authority("CN=Broadside revocation root");
            Intermediate = intermediate ? ValidationCertificates.IssueAuthority(Root, "CN=Broadside revocation intermediate") : null;
            X509Certificate2 issuer = Intermediate ?? Root;
            Authority = new TestRevocationAuthority(issuer);
            Signer = ValidationCertificates.IssueRsaSigner(issuer, "CN=Broadside revocation signer", revocationAddresses: revocationAddresses);
        }

        public X509Certificate2 Root { get; }

        public X509Certificate2? Intermediate { get; }

        public X509Certificate2 Signer { get; }

        /// <summary>Gets the services of the signer's issuer.</summary>
        public TestRevocationAuthority Authority { get; }

        public PdfSignatureValidationOptions Options() => new PdfSignatureValidationOptions().WithTrustAnchors(Root).WithValidationTime(Now);

        public void Dispose()
        {
            Authority.Dispose();
            Signer.Dispose();
            Intermediate?.Dispose();
            Root.Dispose();
        }
    }
}
