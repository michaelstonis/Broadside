using System.Security.Cryptography.X509Certificates;
using Broadside.Signatures;
using Broadside.TestSupport;
using static Broadside.Tests.Signatures.RevocationTestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Time-stamp tokens during signature validation through <see cref="PdfDocument.ValidateSignatures"/>: a signature time-stamp token
/// (RFC 3161 Appendix A) is verified as a CMS signature whose imprint is the signature value, its unit's signing-certificate attribute,
/// extended key usage, path and revocation are checked at the current time, and a valid one moves the signer's validation to its time
/// (ISO 32000-2 §12.8.3.4.5 b) and §12.8.3.4.8). Tokens come from <see cref="TestTimestampAuthority"/>, inside the test process.
/// </summary>
public sealed class TimestampValidationTests
{
    [Fact]
    public void An_expired_certificate_with_a_valid_earlier_timestamp_validates_at_the_timestamp_time()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside short-lived signer", notAfter: DateTimeOffset.UtcNow.AddHours(1));
        using var authority = new TestTimestampAuthority();
        byte[] signed = Signed(signer, [root], after: cms => AddTimestamp(cms, authority));
        DateTimeOffset later = DateTimeOffset.UtcNow.AddDays(2);

        PdfSignatureValidation untimed = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root).WithValidationTime(later));
        PdfSignatureValidation timed = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root, TestTimestampAuthority.Root).WithValidationTime(later));

        // Without trusting the authority the token proves nothing: the signer is checked now, after it expired.
        Assert.Equal(PdfSignatureStatus.Indeterminate, untimed.Status);
        Assert.Equal([PdfSignatureProblemKind.CertificateExpired], untimed.Problems.Select(static problem => problem.Kind));
        Assert.Equal(later, untimed.ValidationTime);
        Assert.Equal(PdfSignatureStatus.Indeterminate, untimed.Timestamp?.Status);
        Assert.Contains(untimed.Timestamp!.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ChainIncomplete);

        Assert.Equal(PdfSignatureStatus.Valid, timed.Status);
        Assert.Empty(timed.Problems);
        PdfTimestampValidation timestamp = Assert.IsType<PdfTimestampValidation>(timed.Timestamp);
        Assert.Equal(PdfSignatureStatus.Valid, timestamp.Status);
        Assert.Equal(timestamp.Time, timed.ValidationTime);
        Assert.InRange(timestamp.Time, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow);
        Assert.Equal(TestTimestampAuthority.Unit.Thumbprint, timestamp.SignerCertificate?.Thumbprint);
        Assert.Equal([TestTimestampAuthority.Unit.Thumbprint, TestTimestampAuthority.Root.Thumbprint], timestamp.CertificatePath.Select(static certificate => certificate.Thumbprint));
        Assert.Equal(TestTimestampAuthority.Policy, timestamp.Policy);
    }

    [Fact]
    public void A_broadside_b_t_signature_reports_its_verified_timestamp()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside B-T signer");
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();
        document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions()
            .WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached)
            .WithTimestampClient(new PdfTimestampClient(http, TestTimestampAuthority.Address)));

        PdfSignatureValidation validation = ValidateSingle(output.ToArray(), new PdfSignatureValidationOptions().WithTrustAnchors(certificate, TestTimestampAuthority.Root).WithValidationTime(DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal(PdfSignatureStatus.Valid, validation.Timestamp?.Status);
        Assert.Equal(validation.Timestamp!.Time, validation.ValidationTime);
    }

    [Fact]
    public void A_changed_token_is_invalid_and_its_time_is_not_used()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside short-lived signer", notAfter: DateTimeOffset.UtcNow.AddHours(1));
        using var authority = new TestTimestampAuthority();

        // The token's signer info ends with its signature value.
        byte[] signed = Signed(signer, [root], after: cms => AddTimestamp(cms, authority, token =>
        {
            token[^1] ^= 0x01;
            return token;
        }));

        PdfSignatureValidation validation = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root, TestTimestampAuthority.Root).WithValidationTime(DateTimeOffset.UtcNow.AddDays(2)));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.CertificateExpired, PdfSignatureProblemKind.TimestampInvalid], validation.Problems.Select(static problem => problem.Kind).Order());
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Timestamp?.Status);
        Assert.Contains(validation.Timestamp!.Problems, static problem => problem.Kind == PdfSignatureProblemKind.SignatureValueInvalid);
    }

    [Fact]
    public void A_token_over_other_data_does_not_match_the_signature_value()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside signer");
        using var authority = new TestTimestampAuthority();
        byte[] signed = Signed(signer, [root], after: cms => cms.SignerInfos[0].AddUnsignedAttribute(
            new System.Security.Cryptography.AsnEncodedData(SignatureTimeStampOid, authority.Stamp([1, 2, 3]))));

        PdfSignatureValidation validation = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root, TestTimestampAuthority.Root));

        Assert.Equal([PdfSignatureProblemKind.TimestampInvalid], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal([PdfSignatureProblemKind.DigestMismatch], validation.Timestamp!.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_token_without_the_signing_certificate_attribute_is_invalid()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside signer");
        using var authority = new TestTimestampAuthority { OmitSigningCertificate = true };
        byte[] signed = Signed(signer, [root], after: cms => AddTimestamp(cms, authority));

        PdfSignatureValidation validation = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root, TestTimestampAuthority.Root));

        Assert.Equal([PdfSignatureProblemKind.TimestampInvalid], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal([PdfSignatureProblemKind.SigningCertificateMismatch], validation.Timestamp!.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_unit_without_the_time_stamping_usage_gives_no_time()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside short-lived signer", notAfter: DateTimeOffset.UtcNow.AddHours(1));
        using X509Certificate2 tsaRoot = ValidationCertificates.Authority("CN=Broadside unit root");
        using X509Certificate2 unit = TestTimestampAuthority.IssueUnit(tsaRoot, timeStamping: false);
        using var authority = new TestTimestampAuthority { Certificate = unit };
        byte[] signed = Signed(signer, [root], after: cms => AddTimestamp(cms, authority));

        PdfSignatureValidation validation = ValidateSingle(signed, new PdfSignatureValidationOptions().WithTrustAnchors(root, tsaRoot).WithValidationTime(DateTimeOffset.UtcNow.AddDays(2)));

        Assert.Equal([PdfSignatureProblemKind.CertificateExpired], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal([PdfSignatureProblemKind.KeyUsageNotPermitted], validation.Timestamp!.Problems.Select(static problem => problem.Kind));
    }

    [Theory]
    [InlineData(X509RevocationReason.KeyCompromise, false)]
    [InlineData(X509RevocationReason.Superseded, true)]
    public void A_unit_revoked_after_its_token_keeps_the_time_unless_its_key_was_compromised(X509RevocationReason reason, bool used)
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside timestamped root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside short-lived signer", notAfter: DateTimeOffset.UtcNow.AddHours(1));
        using X509Certificate2 tsaRoot = ValidationCertificates.Authority("CN=Broadside unit root");
        using X509Certificate2 unit = TestTimestampAuthority.IssueUnit(tsaRoot);
        using var authority = new TestTimestampAuthority { Certificate = unit };
        using var unitServices = new TestRevocationAuthority(tsaRoot);
        byte[] signed = Signed(signer, [root], after: cms => AddTimestamp(cms, authority));

        // §12.8.3.4.8: the unit's path is checked at the current time; it was revoked a minute after the token.
        unitServices.Revoke(unit, DateTimeOffset.UtcNow.AddMinutes(1), reason);
        PdfSignatureValidation validation = ValidateSingle(signed, new PdfSignatureValidationOptions()
            .WithTrustAnchors(root, tsaRoot)
            .WithValidationTime(DateTimeOffset.UtcNow.AddDays(2))
            .WithCertificateRevocationLists(unitServices.Crl(DateTimeOffset.UtcNow.AddMinutes(2))));

        PdfCertificateRevocation revocation = Assert.Single(validation.Timestamp!.Revocations);
        Assert.Equal((PdfRevocationStatus.Revoked, reason), (revocation.Status, revocation.Reason));
        Assert.Equal(used ? PdfSignatureStatus.Valid : PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal(used ? [] : [PdfSignatureProblemKind.CertificateRevoked], validation.Timestamp.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_document_timestamp_reports_its_time_and_unit()
    {
        using var authority = new TestTimestampAuthority();
        using var http = new HttpClient(authority, disposeHandler: false);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();
        document.Timestamp(output, new PdfTimestampClient(http, TestTimestampAuthority.Address));

        PdfSignatureValidation validation = ValidateSingle(output.ToArray(), new PdfSignatureValidationOptions().WithTrustAnchors(TestTimestampAuthority.Root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal("ETSI.RFC3161", validation.SubFilter);
        PdfTimestampValidation timestamp = Assert.IsType<PdfTimestampValidation>(validation.Timestamp);
        Assert.Equal(PdfSignatureStatus.Valid, timestamp.Status);
        Assert.InRange(timestamp.Time, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow);
        Assert.Equal(TestTimestampAuthority.Unit.Thumbprint, timestamp.SignerCertificate?.Thumbprint);
    }

    [Fact]
    public void A_signing_certificate_attribute_naming_another_certificate_makes_the_signature_invalid()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 signer = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside signer");
        using X509Certificate2 other = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside other signer");

        byte[] named = Signed(signer, [root], cms => cms.SignedAttributes.Add(SigningCertificateV2(signer)));
        byte[] misnamed = Signed(signer, [root], cms => cms.SignedAttributes.Add(SigningCertificateV2(other)));

        var options = new PdfSignatureValidationOptions().WithTrustAnchors(root);
        Assert.Equal(PdfSignatureStatus.Valid, ValidateSingle(named, options).Status);
        PdfSignatureValidation validation = ValidateSingle(misnamed, options);
        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.SigningCertificateMismatch], validation.Problems.Select(static problem => problem.Kind));
    }
}
