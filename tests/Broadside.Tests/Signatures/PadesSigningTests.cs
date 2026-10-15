using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// PAdES baseline B-B signatures (ISO 32000-2 §12.8.3.4, ETSI EN 319 142-1) and the signature algorithms beyond RSA with SHA-2:
/// ECDSA on the NIST curves and RSA-PSS (ISO/TS 32002), SHA-3 digests (ISO/TS 32001), and the developer extensions those declare.
/// Every signature is verified by OpenSSL's CMS verification over the byte ranges and, where poppler supports the algorithm, by
/// <c>pdfsig</c>; the certificates are made in the test.
/// </summary>
public sealed class PadesSigningTests
{
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string SigningTimeOid = "1.2.840.113549.1.9.5";

    [Fact]
    public void A_pades_signature_names_the_cades_sub_filter_and_verifies_with_pdfsig_and_openssl()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=PAdES signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached));

        string output = Pdfsig.Run(signed);
        Assert.Equal(1, Pdfsig.ValidSignatures(output));
        Assert.Contains("Signature Type: ETSI.CAdES.detached", output, StringComparison.Ordinal);
        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        using PdfDocument reopened = PdfDocument.Open(signed);
        CosDictionary dictionary = Assert.IsType<Broadside.Forms.PdfSignatureField>(reopened.AcroForm!.FindField("Signature1")).SignatureDictionary!;
        Assert.Equal("ETSI.CAdES.detached", ((CosName)dictionary[new CosName("SubFilter")]).Value);
        Assert.False(dictionary.ContainsKey(new CosName("Cert")));
        Assert.True(dictionary.ContainsKey(new CosName("M")));
    }

    [Theory]
    [InlineData(PdfSignatureSubFilter.EtsiCadesDetached)]
    [InlineData(PdfSignatureSubFilter.AdobePkcs7Detached)]
    public void The_signed_attributes_hold_the_signing_certificate_and_no_signing_time(PdfSignatureSubFilter subFilter)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=PAdES signer");
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithSubFilter(subFilter));

        SignerInfo signer = DecodeSigner(SignedFile.Last(signed));
        string[] attributes = [.. signer.SignedAttributes.Cast<CryptographicAttributeObject>().Select(attribute => attribute.Oid.Value!)];
        Assert.Contains(SigningCertificateV2Oid, attributes);
        Assert.DoesNotContain(SigningTimeOid, attributes);

        // RFC 5035 §5.4: ESSCertIDv2 with the default hash algorithm (SHA-256, so absent), the certificate's hash, and the issuer
        // and serial number of the certificate.
        byte[] value = signer.SignedAttributes.Cast<CryptographicAttributeObject>().Single(attribute => attribute.Oid.Value == SigningCertificateV2Oid).Values[0].RawData;
        var signingCertificate = new System.Formats.Asn1.AsnReader(value, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
        var certificates = signingCertificate.ReadSequence();
        var certificateId = certificates.ReadSequence();
        Assert.Equal(SHA256.HashData(certificate.RawData), certificateId.ReadOctetString());
        var issuerSerial = certificateId.ReadSequence();
        var names = issuerSerial.ReadSequence();
        var directoryName = names.ReadSequence(new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 4));
        Assert.Equal(certificate.IssuerName.RawData, directoryName.ReadEncodedValue().ToArray());
        Assert.Equal(certificate.SerialNumberBytes.ToArray(), issuerSerial.ReadIntegerBytes().ToArray());
        Assert.False(certificates.HasData);
    }

    /// <summary>ISO/TS 32002 Table 3: each NIST curve with its SHA-2 and SHA-3 digest, as curve name, digest name and the OIDs CMS writes.</summary>
    public static TheoryData<string, string, string, string> EcdsaCombinations => new()
    {
        { "P-256", "SHA256", "2.16.840.1.101.3.4.2.1", "1.2.840.10045.4.3.2" },
        { "P-384", "SHA384", "2.16.840.1.101.3.4.2.2", "1.2.840.10045.4.3.3" },
        { "P-521", "SHA512", "2.16.840.1.101.3.4.2.3", "1.2.840.10045.4.3.4" },
        { "P-256", "SHA3-256", "2.16.840.1.101.3.4.2.8", "2.16.840.1.101.3.4.3.10" },
        { "P-384", "SHA3-384", "2.16.840.1.101.3.4.2.9", "2.16.840.1.101.3.4.3.11" },
        { "P-521", "SHA3-512", "2.16.840.1.101.3.4.2.10", "2.16.840.1.101.3.4.3.12" },
    };

    [Theory]
    [MemberData(nameof(EcdsaCombinations))]
    public void An_ecdsa_pades_signature_verifies_with_openssl_and_pdfsig_where_poppler_supports_it(string curve, string digest, string digestOid, string signatureOid)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.CreateFromFriendlyName(NistCurveName(curve)));
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(
            document,
            new PdfCertificateSigner(certificate),
            new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached).WithDigestAlgorithm(new HashAlgorithmName(digest)));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        SignerInfo signer = DecodeSigner(signature);
        Assert.Equal(digestOid, signer.DigestAlgorithm.Value);
        Assert.Equal(signatureOid, signer.SignatureAlgorithm.Value);
        // poppler 26.10 (NSS) reports SHA-3 signatures as an "Unknown Validation Failure": OpenSSL alone verifies those.
        if (!digest.StartsWith("SHA3-", StringComparison.Ordinal))
        {
            Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        }
    }

    [Theory]
    [InlineData("SHA256")]
    [InlineData("SHA384")]
    [InlineData("SHA512")]
    [InlineData("SHA3-256")]
    [InlineData("SHA3-384")]
    [InlineData("SHA3-512")]
    public void An_rsa_pss_pades_signature_verifies_with_openssl_and_pdfsig_where_poppler_supports_it(string digest)
    {
        // The platform's RSA signs SHA-3 digests on Windows and Linux only; RSASSA-PSS needs the digest itself for MGF1.
        Assert.SkipWhen(digest.StartsWith("SHA3-", StringComparison.Ordinal) && !SHA3_256.IsSupported, "The platform's RSA has no SHA-3.");
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=RSA-PSS signer", 3072);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(
            document,
            new PdfCertificateSigner(certificate, rsaSignaturePadding: RSASignaturePadding.Pss),
            new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached).WithDigestAlgorithm(new HashAlgorithmName(digest)));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
        Assert.Equal("1.2.840.113549.1.1.10", DecodeSigner(signature).SignatureAlgorithm.Value);
        if (!digest.StartsWith("SHA3-", StringComparison.Ordinal))
        {
            Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
        }
    }

    [Theory]
    [InlineData("SHA3-256")]
    [InlineData("SHA3-384")]
    [InlineData("SHA3-512")]
    public void An_rsa_pkcs1_signature_with_a_sha3_digest_verifies_with_openssl(string digest)
    {
        // A signer that does the RSA operation itself, so the SHA-3 DigestInfo is signed on every platform.
        using X509Certificate2 withKey = TestCertificates.SelfSigned("CN=RSA SHA-3 signer");
        using RSA key = withKey.GetRSAPrivateKey()!;
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new RawRsaSigner(publicOnly, key), new PdfSignatureOptions().WithDigestAlgorithm(new HashAlgorithmName(digest)));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), publicOnly);
        Assert.Equal("1.2.840.113549.1.1.1", DecodeSigner(signature).SignatureAlgorithm.Value);
    }

    [Fact]
    public void An_rsa_pss_certificate_key_is_signed_with_rsa_pss_through_an_external_signer()
    {
        using X509Certificate2 authority = TestCertificates.SelfSigned("CN=Broadside test authority");
        using var key = RSA.Create(2048);
        using X509Certificate2 certificate = TestCertificates.WithKeyAlgorithm("CN=RSASSA-PSS key", "1.2.840.113549.1.1.10", key.ExportRSAPublicKey(), authority);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new PssKeySigner(certificate, key), new PdfSignatureOptions().WithSubFilter(PdfSignatureSubFilter.EtsiCadesDetached));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), authority);
        Assert.Equal("1.2.840.113549.1.1.10", DecodeSigner(signature).SignatureAlgorithm.Value);
    }

    [Fact]
    public void An_ecdsa_value_a_token_returns_as_r_and_s_is_written_as_der()
    {
        using X509Certificate2 withKey = TestCertificates.SelfSignedEcdsa("CN=Token signer", ECCurve.NamedCurves.nistP384);
        using ECDsa key = withKey.GetECDsaPrivateKey()!;
        using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new P1363Signer(publicOnly, key));

        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), publicOnly);
        Assert.Equal("2.16.840.1.101.3.4.2.2", DecodeSigner(signature).DigestAlgorithm.Value);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    [Theory]
    [InlineData("RSA", "SHA256", "1.7", "")]
    [InlineData("RSA", "SHA3-256", "2.0", "32001")]
    [InlineData("P-256", "SHA256", "2.0", "32002")]
    [InlineData("P-384", "SHA3-384", "2.0", "32001 32002")]
    public void The_iso_ts_32001_and_32002_extensions_are_declared_when_the_signature_uses_them(string key, string digest, string version, string levels)
    {
        using X509Certificate2 certificate = key == "RSA"
            ? TestCertificates.SelfSigned("CN=RSA signer")
            : TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.CreateFromFriendlyName(NistCurveName(key)));
        using RSA? rsa = certificate.GetRSAPrivateKey();
        IPdfSigner signer = rsa is null ? new PdfCertificateSigner(certificate) : new RawRsaSigner(certificate, rsa);
        if (digest == "SHA256" && rsa is not null)
        {
            signer = new PdfCertificateSigner(certificate);
        }

        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, signer, new PdfSignatureOptions().WithDigestAlgorithm(new HashAlgorithmName(digest)));

        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(version, reopened.Version.ToString());
        PdfDeveloperExtension[] iso = [.. reopened.Extensions.Where(extension => extension.Prefix.Value == "ISO_").OrderBy(extension => extension.ExtensionLevel)];
        Assert.Equal(levels, string.Join(' ', iso.Select(extension => extension.ExtensionLevel)));
        foreach (PdfDeveloperExtension extension in iso)
        {
            Assert.Equal(new PdfVersion(2, 0), extension.BaseVersion);
            Assert.Equal(":2022", extension.ExtensionRevision);
            Assert.Equal(extension.ExtensionLevel == 32001 ? "https://www.iso.org/standard/45874.html" : "https://www.iso.org/standard/45875.html", extension.Url?.ToString());
        }

        // The declarations are in the signed update, so the signature covers them.
        SignedFile signature = SignedFile.Last(signed);
        OpenSsl.VerifyCms(signature.Cms, signature.CoveredBytes(), certificate);
    }

    [Theory]
    [InlineData("SHA256", PdfSignatureSubFilter.AdobePkcs7Detached, "1.6")]
    [InlineData("SHA384", PdfSignatureSubFilter.AdobePkcs7Detached, "1.7")]
    [InlineData("SHA512", PdfSignatureSubFilter.AdobePkcs7Detached, "1.7")]
    [InlineData("SHA256", PdfSignatureSubFilter.EtsiCadesDetached, "2.0")]
    public void Signing_a_pdf_1_4_document_raises_it_to_what_table_260_gives_the_signature(string digest, PdfSignatureSubFilter subFilter, string version)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSigned("CN=RSA signer");
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithSubFilter(subFilter).WithDigestAlgorithm(new HashAlgorithmName(digest)));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
        using PdfDocument reopened = PdfDocument.Open(signed);
        Assert.Equal(version, reopened.Version.ToString());
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    [Fact]
    public void Strict_mode_refuses_an_ecdsa_signature_of_a_pdf_1_7_document_before_anything_changes()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.NamedCurves.nistP256);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        Diagnostics.DiagnosticException exception = Assert.Throws<Diagnostics.DiagnosticException>(() => document.Sign(output, new PdfCertificateSigner(certificate)));

        Assert.Contains("ECDSA", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
        Assert.Null(document.AcroForm);
        Assert.Empty(document.Extensions);
        Assert.False(document.Catalog.ContainsKey(new CosName("Version")));
    }

    [Fact]
    public void A_signer_that_fails_leaves_no_extension_declared_and_the_next_save_raises_nothing()
    {
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.NamedCurves.nistP256);
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        string before = CosSnapshot.Of(document);
        using var output = new MemoryStream();

        Assert.Throws<CryptographicException>(() => document.Sign(output, new FailingSigner(certificate), new PdfSignatureOptions().WithDigestAlgorithm(new HashAlgorithmName("SHA3-256"))));

        Assert.Equal(0, output.Length);
        Assert.Equal(before, CosSnapshot.Of(document));
        Assert.Empty(document.Extensions);
        Assert.False(document.Catalog.ContainsKey(new CosName("Version")));
        using var saved = new MemoryStream();
        document.Save(saved, new PdfSaveOptions().WithIncrementalUpdate(true));
        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
        Assert.Empty(reopened.Extensions);
        Assert.Null(reopened.AcroForm);
    }

    [Theory]
    [InlineData("P-256", "SHA384")]
    [InlineData("P-384", "SHA256")]
    [InlineData("P-521", "SHA3-256")]
    public void An_ecdsa_curve_refuses_a_digest_iso_ts_32002_does_not_pair_with_it(string curve, string digest)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.CreateFromFriendlyName(NistCurveName(curve)));
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();

        Assert.Throws<NotSupportedException>(() => document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithDigestAlgorithm(new HashAlgorithmName(digest))));

        Assert.Equal(0, output.Length);
        Assert.Null(document.AcroForm);
    }

    [Theory]
    [InlineData("P-256", "2.16.840.1.101.3.4.2.1")]
    [InlineData("P-384", "2.16.840.1.101.3.4.2.2")]
    [InlineData("P-521", "2.16.840.1.101.3.4.2.3")]
    public void Without_a_digest_algorithm_an_ecdsa_key_signs_with_the_sha_2_digest_of_its_curve(string curve, string digestOid)
    {
        using X509Certificate2 certificate = TestCertificates.SelfSignedEcdsa("CN=ECDSA signer", ECCurve.CreateFromFriendlyName(NistCurveName(curve)));
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));

        byte[] signed = Sign(document, new PdfCertificateSigner(certificate));

        Assert.Equal(digestOid, DecodeSigner(SignedFile.Last(signed)).DigestAlgorithm.Value);
        Assert.Equal(1, Pdfsig.ValidSignatures(Pdfsig.Run(signed)));
    }

    private static string NistCurveName(string curve) => curve switch
    {
        "P-256" => "nistP256",
        "P-384" => "nistP384",
        _ => "nistP521",
    };

    private static SignerInfo DecodeSigner(SignedFile signature)
    {
        var cms = new SignedCms(new ContentInfo(signature.CoveredBytes()), detached: true);
        cms.Decode(signature.Cms);
        return Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
    }

    private static byte[] Sign(PdfDocument document, IPdfSigner signer, PdfSignatureOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Sign(output, signer, options);
        return output.ToArray();
    }

    /// <summary>
    /// An RSA signer that does the PKCS #1 v1.5 operation itself (RFC 8017 §8.2.1, §9.2): the <c>DigestInfo</c> of any digest, padded
    /// and raised to the private exponent, so it signs SHA-3 digests where the platform's RSA cannot.
    /// </summary>
    private sealed class RawRsaSigner(X509Certificate2 certificate, RSA key) : IPdfSigner
    {
        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm)
        {
            string oid = digestAlgorithm.Name switch
            {
                "SHA3-256" => "2.16.840.1.101.3.4.2.8",
                "SHA3-384" => "2.16.840.1.101.3.4.2.9",
                "SHA3-512" => "2.16.840.1.101.3.4.2.10",
                _ => throw new NotSupportedException(digestAlgorithm.Name),
            };
            var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(oid);
                    writer.WriteNull();
                }

                writer.WriteOctetString(digest);
            }

            byte[] digestInfo = writer.Encode();
            RSAParameters parameters = key.ExportParameters(includePrivateParameters: true);
            int length = parameters.Modulus!.Length;
            byte[] encoded = new byte[length];
            encoded[1] = 0x01;
            encoded.AsSpan(2, length - digestInfo.Length - 3).Fill(0xFF);
            digestInfo.CopyTo(encoded, length - digestInfo.Length);
            var message = new System.Numerics.BigInteger(encoded, isUnsigned: true, isBigEndian: true);
            var modulus = new System.Numerics.BigInteger(parameters.Modulus, isUnsigned: true, isBigEndian: true);
            var exponent = new System.Numerics.BigInteger(parameters.D, isUnsigned: true, isBigEndian: true);
            byte[] value = System.Numerics.BigInteger.ModPow(message, exponent, modulus).ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] signature = new byte[length];
            value.CopyTo(signature, length - value.Length);
            return signature;
        }
    }

    /// <summary>A signer whose device refuses to sign.</summary>
    private sealed class FailingSigner(X509Certificate2 certificate) : IPdfSigner
    {
        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) => throw new CryptographicException("The token is locked.");
    }

    /// <summary>A signer for a certificate whose key is an <c>id-RSASSA-PSS</c> key, which the platform cannot attach a private key to.</summary>
    private sealed class PssKeySigner(X509Certificate2 certificate, RSA key) : IPdfSigner
    {
        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) =>
            key.SignHash(digest.ToArray(), digestAlgorithm, RSASignaturePadding.Pss);
    }

    /// <summary>An ECDSA signer returning <c>r</c> and <c>s</c> concatenated, as a PKCS #11 token does.</summary>
    private sealed class P1363Signer(X509Certificate2 certificate, ECDsa key) : IPdfSigner
    {
        public X509Certificate2 Certificate => certificate;

        public IReadOnlyList<X509Certificate2> CertificateChain => [];

        public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm) =>
            key.SignHash(digest.ToArray(), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
