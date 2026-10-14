using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Validating the signatures of a document through <see cref="PdfDocument.ValidateSignatures"/>: byte range coverage, the digest of
/// the signed bytes, the CMS signature value, and the signer's chain to the trust anchors the options give. ISO 32000-2 §12.8.1,
/// §12.8.3. Every certificate is made in the test with <see cref="CertificateRequest"/>; nothing is fetched. CMS objects other than
/// Broadside's own come from the platform PKCS package (<see cref="SignedCms"/>), an independent encoder.
/// </summary>
public sealed class SignatureValidationTests
{
    private static readonly DateTimeOffset SigningTime = new(2026, 3, 14, 15, 9, 26, TimeSpan.Zero);

    [Fact]
    public void An_own_signature_whose_chain_reaches_a_trusted_authority_is_valid()
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = TestCertificates.Chain("CN=Broadside validated signer");
        using (authority)
        using (leaf)
        {
            byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [authority]));

            PdfSignatureValidation validation = ValidateSingle(signed, Trusting(authority));

            Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
            Assert.Empty(validation.Problems);
            Assert.Equal("Signature1", validation.FieldName);
            Assert.Equal("adbe.pkcs7.detached", validation.SubFilter);
            Assert.Equal(HashAlgorithmName.SHA256, validation.DigestAlgorithm);
            Assert.True(validation.CoversWholeFile);
            Assert.Equal(4, validation.ByteRange.Count);
            Assert.Equal(leaf.Thumbprint, validation.SignerCertificate?.Thumbprint);
            Assert.Equal([leaf.Thumbprint, authority.Thumbprint], validation.CertificatePath.Select(static certificate => certificate.Thumbprint));
        }
    }

    [Fact]
    public void A_file_changed_after_signing_reports_the_digest_mismatch_and_is_invalid()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithSigningTime(SigningTime).WithReason("Approved"));
        Assert.Equal(PdfSignatureStatus.Valid, ValidateSingle(signed, Trusting(certificate)).Status);

        // The reason is inside the signed bytes; one letter changes.
        byte[] changed = (byte[])signed.Clone();
        SignedFileEditor.Replace(changed, "(Approved)", "(Approvex)");

        PdfSignatureValidation validation = ValidateSingle(changed, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        PdfSignatureProblem problem = Assert.Single(validation.Problems);
        Assert.Equal(PdfSignatureProblemKind.DigestMismatch, problem.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_changed_signature_value_does_not_verify(bool managed)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        SignedFile signature = SignedFile.Last(signed);
        byte[] cms = (byte[])signature.Cms.Clone();
        cms[^1] ^= 0x01; // The signer info ends with its signature value.
        SignedFileEditor.WriteContents(signed, signature.ByteRange, cms);

        using IDisposable? scope = managed ? CryptographyBackend.ForceManaged() : null;
        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.SignatureValueInvalid], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_byte_range_that_leaves_a_second_gap_is_reported()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        long[] range = SignedFile.Last(signed).ByteRange;

        // Bytes 100 to 110 of the original file are left out besides the signature value.
        byte[] gapped = SignedFileEditor.RewriteByteRange(signed, 0, 100, 110, range[1] - 110, range[2], range[3]);

        PdfSignatureValidation validation = ValidateSingle(gapped, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        PdfSignatureProblem gap = Assert.Single(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ByteRangeGap);
        Assert.Contains("100 to 110", gap.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ContentsOutsideGap);
    }

    [Fact]
    public void A_byte_range_that_does_not_start_at_the_first_byte_is_reported()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        long[] range = SignedFile.Last(signed).ByteRange;

        byte[] gapped = SignedFileEditor.RewriteByteRange(signed, 9, range[1] - 9, range[2], range[3]);

        PdfSignatureValidation validation = ValidateSingle(gapped, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Contains(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ByteRangeGap);
    }

    [Fact]
    public void A_byte_range_whose_gap_is_not_the_signature_value_is_reported()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        long[] range = SignedFile.Last(signed).ByteRange;

        // The gap moves one byte left: it now holds the space before '<' and leaves the '>' signed.
        byte[] shifted = SignedFileEditor.RewriteByteRange(signed, 0, range[1] - 1, range[2] - 1, range[3] + 1);

        PdfSignatureValidation validation = ValidateSingle(shifted, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Contains(validation.Problems, static problem => problem.Kind == PdfSignatureProblemKind.ContentsOutsideGap);
    }

    [Theory]
    [InlineData("[0 10 5 20]")]
    [InlineData("[0 10 20]")]
    [InlineData("[0 -1 20 5]")]
    [InlineData("[0 10 20 999999999]")]
    public void A_byte_range_that_is_not_pairs_within_the_file_is_invalid(string byteRange)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        long[] values = [.. byteRange.Trim('[', ']').Split(' ').Select(long.Parse)];

        PdfSignatureValidation validation = ValidateSingle(SignedFileEditor.RewriteByteRange(signed, values), Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.ByteRangeInvalid], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_self_signed_signer_that_is_not_trusted_is_indeterminate_and_its_signature_still_checks_out()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));

        PdfSignatureValidation validation = ValidateSingle(signed);

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.ChainUntrusted], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal([certificate.Thumbprint], validation.CertificatePath.Select(static item => item.Thumbprint));
    }

    [Fact]
    public void A_chain_the_signature_does_not_carry_is_incomplete_until_its_authority_is_given()
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = TestCertificates.Chain("CN=Broadside chained signer");
        using (authority)
        using (leaf)
        {
            byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf));

            PdfSignatureValidation untrusted = ValidateSingle(signed);
            PdfSignatureValidation extra = ValidateSingle(signed, new PdfSignatureValidationOptions().WithExtraCertificates(authority));
            PdfSignatureValidation trusted = ValidateSingle(signed, Trusting(authority));

            Assert.Equal([PdfSignatureProblemKind.ChainIncomplete], untrusted.Problems.Select(static problem => problem.Kind));
            Assert.Equal(PdfSignatureStatus.Indeterminate, untrusted.Status);
            Assert.Equal([PdfSignatureProblemKind.ChainUntrusted], extra.Problems.Select(static problem => problem.Kind));
            Assert.Equal(2, extra.CertificatePath.Count);
            Assert.Equal(PdfSignatureStatus.Valid, trusted.Status);
        }
    }

    [Fact]
    public void A_certificate_outside_its_validity_period_at_the_validation_time_is_reported()
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = TestCertificates.Chain("CN=Broadside chained signer");
        using (authority)
        using (leaf)
        {
            byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [authority]));

            PdfSignatureValidation late = ValidateSingle(signed, Trusting(authority).WithValidationTime(DateTimeOffset.UtcNow.AddYears(5)));
            PdfSignatureValidation early = ValidateSingle(signed, Trusting(authority).WithValidationTime(DateTimeOffset.UtcNow.AddDays(-30)));

            // The anchor is trusted as given (RFC 5280 §6.1.1 (d)); only the signer's own certificate is checked here.
            Assert.Equal([PdfSignatureProblemKind.CertificateExpired], late.Problems.Select(static problem => problem.Kind));
            Assert.Equal(PdfSignatureStatus.Indeterminate, late.Status);
            Assert.Equal([PdfSignatureProblemKind.CertificateNotYetValid], early.Problems.Select(static problem => problem.Kind));
        }
    }

    [Fact]
    public void An_issuer_that_is_not_a_certification_authority_is_reported()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 impostor = ValidationCertificates.IssueAuthority(root, "CN=Broadside end entity", certificateAuthority: false);
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(impostor, "CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [impostor, root]));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.IssuerNotCertificateAuthority], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal(3, validation.CertificatePath.Count);
    }

    [Fact]
    public void A_chain_longer_than_an_issuers_path_length_constraint_is_reported()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 constrained = ValidationCertificates.IssueAuthority(root, "CN=Broadside constrained authority", pathLength: 0);
        using X509Certificate2 below = ValidationCertificates.IssueAuthority(constrained, "CN=Broadside authority below");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(below, "CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [below, constrained]));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal([PdfSignatureProblemKind.PathLengthExceeded], validation.Problems.Select(static problem => problem.Kind));
        Assert.Equal(4, validation.CertificatePath.Count);
    }

    [Fact]
    public void A_signer_certificate_whose_key_usage_excludes_signing_is_reported()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside encryption key", X509KeyUsageFlags.KeyEncipherment);
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal([PdfSignatureProblemKind.KeyUsageNotPermitted], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void Two_signatures_validate_in_signing_order_and_only_the_last_covers_the_whole_file()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 first = ValidationCertificates.IssueRsaSigner(root, "CN=First signer");
        using X509Certificate2 second = ValidationCertificates.IssueRsaSigner(root, "CN=Second signer");
        byte[] once = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(first), new PdfSignatureOptions().WithFieldName("Author"));
        byte[] twice;
        using (PdfDocument document = PdfDocument.Open(once))
        {
            twice = Sign(document, new PdfCertificateSigner(second), new PdfSignatureOptions().WithFieldName("Approver").WithDigestAlgorithm(HashAlgorithmName.SHA384));
        }

        using PdfDocument signed = PdfDocument.Open(twice);
        IReadOnlyList<PdfSignatureValidation> validations = signed.ValidateSignatures(Trusting(root));

        Assert.Equal(["Author", "Approver"], validations.Select(static validation => validation.FieldName));
        Assert.All(validations, static validation => Assert.Equal(PdfSignatureStatus.Valid, validation.Status));
        Assert.Equal([false, true], validations.Select(static validation => validation.CoversWholeFile));
        Assert.Equal([HashAlgorithmName.SHA256, HashAlgorithmName.SHA384], validations.Select(static validation => validation.DigestAlgorithm!.Value));
    }

    [Fact]
    public void A_document_without_signatures_has_no_validations()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));

        Assert.Empty(document.ValidateSignatures());
    }

    [Theory]
    [InlineData("SHA256", false)]
    [InlineData("SHA384", false)]
    [InlineData("SHA512", false)]
    [InlineData("SHA512", true)]
    public void Each_digest_validates_with_the_platform_and_the_managed_rsa_verifier(string digest, bool managed)
    {
        (X509Certificate2 authority, X509Certificate2 leaf) = TestCertificates.Chain("CN=Broadside chained signer");
        using (authority)
        using (leaf)
        {
            byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [authority]), new PdfSignatureOptions().WithDigestAlgorithm(new HashAlgorithmName(digest)));

            using IDisposable? scope = managed ? CryptographyBackend.ForceManaged() : null;
            PdfSignatureValidation validation = ValidateSingle(signed, Trusting(authority));

            Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
            Assert.Equal(digest, validation.DigestAlgorithm?.Name);
        }
    }

    [Theory]
    [InlineData("nistP256", "SHA256")]
    [InlineData("nistP384", "SHA384")]
    [InlineData("nistP521", "SHA512")]
    public void An_ecdsa_signature_from_the_platform_pkcs_package_validates(string curve, string digest)
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueEcdsaSigner(root, "CN=Broadside ECDSA signer", ECCurve.CreateFromFriendlyName(curve));
        byte[] signed = ReplaceWithPlatformCms(leaf, root, signer => signer.DigestAlgorithm = DigestOid(digest));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal(digest, validation.DigestAlgorithm?.Name);
        Assert.Equal(leaf.Thumbprint, validation.SignerCertificate?.Thumbprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_rsa_pss_signature_from_the_platform_pkcs_package_validates(bool managed)
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside PSS signer");
        using RSA key = leaf.GetRSAPrivateKey()!;
        byte[] signed = ReplaceWithPlatformCms(leaf, root, null, (certificate, _) => new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate, key, RSASignaturePadding.Pss));

        using IDisposable? scope = managed ? CryptographyBackend.ForceManaged() : null;
        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
    }

    [Fact]
    public void A_sha1_signature_identified_by_subject_key_identifier_validates()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside legacy signer");
        byte[] signed = ReplaceWithPlatformCms(
            leaf,
            root,
            signer => signer.DigestAlgorithm = DigestOid("SHA1"),
            (certificate, _) => new CmsSigner(SubjectIdentifierType.SubjectKeyIdentifier, certificate));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal(HashAlgorithmName.SHA1, validation.DigestAlgorithm);
    }

    [Fact]
    public void A_signature_without_signed_attributes_signs_the_byte_range_digest_itself()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside bare signer");
        byte[] signed = ReplaceWithPlatformCms(leaf, root, null);
        var decoded = new SignedCms();
        decoded.Decode(SignedFile.Last(signed).Cms);
        Assert.Empty(decoded.SignerInfos[0].SignedAttributes);

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
    }

    [Fact]
    public void A_signature_that_does_not_carry_its_signer_certificate_is_reported()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside signer");
        byte[] signed = ReplaceWithPlatformCms(leaf, root, signer => signer.IncludeOption = X509IncludeOption.None);

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.SignerCertificateMissing], validation.Problems.Select(static problem => problem.Kind));
        Assert.Null(validation.SignerCertificate);
    }

    [Fact]
    public void An_ecdsa_authority_and_an_rsa_pss_signed_certificate_are_verified_on_the_way_to_the_anchor()
    {
        using X509Certificate2 root = ValidationCertificates.EcdsaAuthority("CN=Broadside ECDSA root", ECCurve.NamedCurves.nistP384, HashAlgorithmName.SHA384);
        using X509Certificate2 intermediate = ValidationCertificates.IssueAuthority(root, "CN=Broadside RSA authority");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(intermediate, "CN=Broadside signer", issuerPadding: RSASignaturePadding.Pss);
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(leaf, [intermediate]));

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal([leaf.Thumbprint, intermediate.Thumbprint, root.Thumbprint], validation.CertificatePath.Select(static item => item.Thumbprint));
    }

    [Fact]
    public void A_deprecated_pkcs7_sha1_signature_validates_its_encapsulated_digest()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 leaf = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside legacy signer");
        byte[] template = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(Placeholder()));

        byte[] signed = SignedFileEditor.ReplaceSignature(
            template,
#pragma warning disable CA5350 // adbe.pkcs7.sha1 encapsulates a SHA-1 digest by definition (§12.8.3.3.1); validation must still read it.
            covered => SignedFileEditor.PlatformCms(new ContentInfo(SHA1.HashData(covered)), Signer(leaf), detached: false),
#pragma warning restore CA5350
            "adbe.pkcs7.sha1");
        byte[] tampered = (byte[])signed.Clone();
        tampered[7] ^= 0x01; // The header's version digit, signed and harmless to reading.

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(root));
        PdfSignatureValidation changed = ValidateSingle(tampered, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal("adbe.pkcs7.sha1", validation.SubFilter);
        Assert.Equal([PdfSignatureProblemKind.DigestMismatch], changed.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_document_timestamp_validates_its_message_imprint()
    {
        using X509Certificate2 root = ValidationCertificates.Authority("CN=Broadside root");
        using X509Certificate2 authority = ValidationCertificates.IssueRsaSigner(root, "CN=Broadside timestamp authority");
        byte[] template = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(Placeholder()));

        byte[] stamped = SignedFileEditor.ReplaceSignature(
            template,
            covered => SignedFileEditor.PlatformCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), TstInfo(SHA256.HashData(covered))), Signer(authority), detached: false),
            "ETSI.RFC3161");
        byte[] tampered = (byte[])stamped.Clone();
        tampered[7] ^= 0x01; // The header's version digit, signed and harmless to reading.

        PdfSignatureValidation validation = ValidateSingle(stamped, Trusting(root));
        PdfSignatureValidation changed = ValidateSingle(tampered, Trusting(root));

        Assert.Equal(PdfSignatureStatus.Valid, validation.Status);
        Assert.Equal("ETSI.RFC3161", validation.SubFilter);
        Assert.Equal([PdfSignatureProblemKind.DigestMismatch], changed.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void A_pkcs1_signature_is_reported_as_unsupported()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        SignedFileEditor.Replace(signed, "/SubFilter /adbe.pkcs7.detached", "/SubFilter /adbe.x509.rsa_sha1");

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Indeterminate, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.SubFilterUnsupported], validation.Problems.Select(static problem => problem.Kind));
    }

    [Fact]
    public void Contents_that_are_not_a_cms_object_are_invalid()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=Broadside signer");
        byte[] signed = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(certificate));
        SignedFile signature = SignedFile.Last(signed);
        SignedFileEditor.WriteContents(signed, signature.ByteRange, [0x30, 0x03, 0x02, 0x01, 0x01]);

        PdfSignatureValidation validation = ValidateSingle(signed, Trusting(certificate));

        Assert.Equal(PdfSignatureStatus.Invalid, validation.Status);
        Assert.Equal([PdfSignatureProblemKind.SignatureEncodingInvalid], validation.Problems.Select(static problem => problem.Kind));
    }

    internal static byte[] SignCorpusFile(string fileName, IPdfSigner signer, PdfSignatureOptions? options = null)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(fileName));
        return Sign(document, signer, options);
    }

    internal static byte[] Sign(PdfDocument document, IPdfSigner signer, PdfSignatureOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Sign(output, signer, options);
        return output.ToArray();
    }

    internal static PdfSignatureValidationOptions Trusting(params X509Certificate2[] anchors) => new PdfSignatureValidationOptions().WithTrustAnchors(anchors);

    internal static PdfSignatureValidation ValidateSingle(byte[] file, PdfSignatureValidationOptions? options = null)
    {
        using PdfDocument document = PdfDocument.Open(file);
        return Assert.Single(document.ValidateSignatures(options));
    }

    /// <summary>A file signed with a large key, so its <c>Contents</c> has room for any CMS object a test puts there instead.</summary>
    private static byte[] ReplaceWithPlatformCms(
        X509Certificate2 leaf,
        X509Certificate2 root,
        Action<CmsSigner>? configure,
        Func<X509Certificate2, X509Certificate2, CmsSigner>? create = null)
    {
        byte[] template = SignCorpusFile("empty-page.pdf", new PdfCertificateSigner(Placeholder()));
        return SignedFileEditor.ReplaceSignature(template, covered =>
        {
            CmsSigner signer = create?.Invoke(leaf, root) ?? Signer(leaf);
            configure?.Invoke(signer);
            return SignedFileEditor.PlatformCms(new ContentInfo(covered), signer);
        });
    }

    private static CmsSigner Signer(X509Certificate2 certificate) => new(SubjectIdentifierType.IssuerAndSerialNumber, certificate) { IncludeOption = X509IncludeOption.EndCertOnly };

    private static X509Certificate2 Placeholder() => PlaceholderCertificate.Value;

    private static readonly Lazy<X509Certificate2> PlaceholderCertificate = new(() => TestCertificates.SelfSigned("CN=Broadside placeholder signer with a long name to reserve room", keySize: 4096));

    private static Oid DigestOid(string digest) => digest switch
    {
        "SHA1" => new Oid("1.3.14.3.2.26"),
        "SHA256" => new Oid("2.16.840.1.101.3.4.2.1"),
        "SHA384" => new Oid("2.16.840.1.101.3.4.2.2"),
        _ => new Oid("2.16.840.1.101.3.4.2.3"),
    };

    /// <summary>A minimal <c>TSTInfo</c> (RFC 3161 §2.4.2) whose message imprint is <paramref name="digest"/> under SHA-256.</summary>
    private static byte[] TstInfo(byte[] digest)
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            writer.WriteObjectIdentifier("1.3.6.1.4.1.99999.1");
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                }

                writer.WriteOctetString(digest);
            }

            writer.WriteInteger(42);
            writer.WriteGeneralizedTime(SigningTime);
        }

        return writer.Encode();
    }
}
