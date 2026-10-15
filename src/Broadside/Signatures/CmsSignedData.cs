using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures;

/// <summary>
/// Encodes the detached CMS <c>SignedData</c> of a byte range signature (ISO 32000-2 §12.8.3.3 and §12.8.3.4; RFC 5652 §5): one
/// signer identified by issuer and serial number, signed attributes holding the content type, the message digest and the ESS
/// signing certificate, and the signer's certificates. DER throughout, so the signed attributes are encoded exactly as they are hashed.
/// </summary>
/// <remarks>
/// <para>
/// Built here rather than with <c>SignedCms</c>, which hashes the content itself from one array: the byte ranges are hashed in
/// windows through <see cref="IncrementalDigest"/> (SHA-3 included where the platform lacks it), and the signature value comes from an
/// <see cref="IPdfSigner"/> that only sees a digest.
/// </para>
/// <para>
/// The signature algorithm follows the certificate's key: RSA (PKCS #1 v1.5 or RSASSA-PSS, as the signer says) or ECDSA on P-256,
/// P-384 or P-521 (ISO/TS 32002 §5.1.3, Table 3). The digest algorithm is SHA-256, SHA-384, SHA-512 (ISO 32000-2 Table 260) or
/// SHA3-256, SHA3-384, SHA3-512 (ISO/TS 32001 §5.1.4), the same for the message digest and the signed attributes (ISO/TS 32002 §5.1.4).
/// </para>
/// </remarks>
internal static class CmsSignedData
{
    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    private const string DataOid = "1.2.840.113549.1.7.1";
    private const string ContentTypeOid = "1.2.840.113549.1.9.3";
    private const string MessageDigestOid = "1.2.840.113549.1.9.4";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string RsaEncryptionOid = "1.2.840.113549.1.1.1";
    private const string RsaPssOid = "1.2.840.113549.1.1.10";
    private const string Mgf1Oid = "1.2.840.113549.1.1.8";
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string PdfMacDataOid = "1.0.32004.1.2";
    private const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";

    /// <summary>Gets the SHA3-256 digest algorithm (FIPS 202; ISO/TS 32001 §5.1.1).</summary>
    public static HashAlgorithmName Sha3_256 { get; } = new("SHA3-256");

    /// <summary>Gets the SHA3-384 digest algorithm (FIPS 202; ISO/TS 32001 §5.1.1).</summary>
    public static HashAlgorithmName Sha3_384 { get; } = new("SHA3-384");

    /// <summary>Gets the SHA3-512 digest algorithm (FIPS 202; ISO/TS 32001 §5.1.1).</summary>
    public static HashAlgorithmName Sha3_512 { get; } = new("SHA3-512");

    /// <summary>Throws for a digest algorithm byte range signing does not write (§12.8.3.1, Table 260; ISO/TS 32001 §5.1.4).</summary>
    public static void RequireSupportedDigest(HashAlgorithmName algorithm) => DigestOid(algorithm);

    /// <summary>Returns a value indicating whether <paramref name="algorithm"/> is one of the SHA-3 digests (ISO/TS 32001).</summary>
    public static bool IsSha3(HashAlgorithmName algorithm) => algorithm == Sha3_256 || algorithm == Sha3_384 || algorithm == Sha3_512;

    /// <summary>The object identifier of a digest algorithm (NIST CSOR; RFC 5754 §2 for SHA-2).</summary>
    public static string DigestOid(HashAlgorithmName algorithm) =>
        algorithm == HashAlgorithmName.SHA256 ? Sha256Oid
        : algorithm == HashAlgorithmName.SHA384 ? "2.16.840.1.101.3.4.2.2"
        : algorithm == HashAlgorithmName.SHA512 ? "2.16.840.1.101.3.4.2.3"
        : algorithm == Sha3_256 ? "2.16.840.1.101.3.4.2.8"
        : algorithm == Sha3_384 ? "2.16.840.1.101.3.4.2.9"
        : algorithm == Sha3_512 ? "2.16.840.1.101.3.4.2.10"
        : throw new NotSupportedException(
            $"The digest algorithm {algorithm.Name} is not supported for signing; use SHA-256, SHA-384, SHA-512, SHA3-256, SHA3-384 or SHA3-512.");

    /// <summary>
    /// Reads what the signer info needs to know of the certificate's public key, or throws for a key it cannot describe: RSA
    /// (<c>rsaEncryption</c> or <c>id-RSASSA-PSS</c>, RFC 4055 §1.2) or ECDSA on a named NIST curve (ISO/TS 32002 §5.1.3, Table 3).
    /// </summary>
    /// <exception cref="NotSupportedException">The key is of another type, or on another curve, or the curve is not named.</exception>
    public static SigningKey ReadKey(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        PublicKey key = certificate.PublicKey;
        switch (key.Oid.Value)
        {
            case RsaEncryptionOid:
                {
                    using RSA rsa = certificate.GetRSAPublicKey() ?? throw new NotSupportedException("The certificate's RSA public key cannot be read.");
                    return new SigningKey(SigningKeyType.Rsa, rsa.KeySize, null);
                }

            case RsaPssOid:
                {
                    // The platform does not load an id-RSASSA-PSS key from a certificate; the key itself is an RSAPublicKey (RFC 4055 §1.2).
                    using RSA rsa = RSA.Create();
                    rsa.ImportRSAPublicKey(key.EncodedKeyValue.RawData, out _);
                    return new SigningKey(SigningKeyType.RsaPss, rsa.KeySize, null);
                }

            case EcPublicKeyOid:
                {
                    string curve = NamedCurve(key.EncodedParameters?.RawData) ?? "an unnamed curve";
                    return curve switch
                    {
                        "1.2.840.10045.3.1.7" => new SigningKey(SigningKeyType.Ecdsa, 256, "P-256"),
                        "1.3.132.0.34" => new SigningKey(SigningKeyType.Ecdsa, 384, "P-384"),
                        "1.3.132.0.35" => new SigningKey(SigningKeyType.Ecdsa, 521, "P-521"),
                        _ => throw new NotSupportedException(
                            $"Signing with an ECDSA key on {curve} is not supported; the named curves P-256, P-384 and P-521 are (ISO/TS 32002 Table 3, RFC 5480 §2.1.1)."),
                    };
                }

            default:
                throw new NotSupportedException(
                    $"Signing with a {key.Oid.FriendlyName ?? key.Oid.Value} key is not supported; RSA, RSA-PSS and ECDSA keys on P-256, P-384 and P-521 are.");
        }

        static string? NamedCurve(byte[]? parameters)
        {
            if (parameters is null)
            {
                return null;
            }

            try
            {
                var reader = new AsnReader(parameters, AsnEncodingRules.DER);
                return reader.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier) ? reader.ReadObjectIdentifier() : null;
            }
            catch (AsnContentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The digest algorithm a signature with <paramref name="key"/> uses when the options name none: the one ISO/TS 32002 Table 3
    /// pairs with an ECDSA curve (SHA-384 for P-384, SHA-512 for P-521), SHA-256 otherwise.
    /// </summary>
    public static HashAlgorithmName DefaultDigest(SigningKey key) => key.Curve switch
    {
        "P-384" => HashAlgorithmName.SHA384,
        "P-521" => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.SHA256,
    };

    /// <summary>
    /// Throws when <paramref name="digest"/> may not be used with <paramref name="key"/>: an ECDSA curve takes only the SHA-2 and
    /// SHA-3 digests of its size (ISO/TS 32002 §5.1.3, Table 3); an RSA-PSS key only RSASSA-PSS signatures (RFC 4055 §1.2).
    /// </summary>
    public static void RequireCompatible(SigningKey key, HashAlgorithmName digest, RSASignaturePadding padding)
    {
        RequireSupportedDigest(digest);
        if (key.Type == SigningKeyType.Ecdsa)
        {
            HashAlgorithmName sha2 = DefaultDigest(key);
            HashAlgorithmName sha3 = key.Size switch { 384 => Sha3_384, 521 => Sha3_512, _ => Sha3_256 };
            if (digest != sha2 && digest != sha3)
            {
                throw new NotSupportedException(
                    $"An ECDSA signature on {key.Curve} uses {sha2.Name} or {sha3.Name} (ISO/TS 32002 Table 3), not {digest.Name}.");
            }
        }
        else if (key.Type == SigningKeyType.RsaPss && padding != RSASignaturePadding.Pss)
        {
            throw new NotSupportedException("The certificate's key is an RSASSA-PSS key (id-RSASSA-PSS), so the signature must be RSASSA-PSS.");
        }
    }

    /// <summary>
    /// The DER encoding of the signed attributes as RFC 5652 §5.4 hashes them, with the universal <c>SET OF</c> tag: the content type
    /// (<c>id-data</c>, §11.1), the message digest of the byte ranges (§11.2) and the ESS signing-certificate-v2 attribute naming
    /// <paramref name="certificate"/> (RFC 5035 §3; ISO 32000-2 §12.8.3.4.3 f). No signing time: the signature dictionary's <c>M</c>
    /// carries it (§12.8.3.4.2).
    /// </summary>
    public static byte[] SignedAttributes(ReadOnlySpan<byte> messageDigest, HashAlgorithmName digestAlgorithm, X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSetOf())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(ContentTypeOid);
                using (writer.PushSetOf())
                {
                    writer.WriteObjectIdentifier(DataOid);
                }
            }

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(MessageDigestOid);
                using (writer.PushSetOf())
                {
                    writer.WriteOctetString(messageDigest);
                }
            }

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(SigningCertificateV2Oid);
                using (writer.PushSetOf())
                {
                    WriteSigningCertificateV2(writer, digestAlgorithm, certificate);
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Returns the signature value as CMS carries it: an ECDSA value the signer returned in the IEEE P1363 form (<c>r</c> and
    /// <c>s</c> concatenated, what a PKCS #11 token returns) becomes the DER <c>ECDSA-Sig-Value</c> RFC 5753 §7.2 requires (ISO/TS
    /// 32002 §5.1.3 NOTE 2); anything else is returned as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">An ECDSA value in neither form.</exception>
    public static byte[] NormalizeSignature(SigningKey key, byte[] signature)
    {
        if (key.Type != SigningKeyType.Ecdsa || IsDerEcdsaSignature(signature))
        {
            return signature;
        }

        int coordinate = (key.Size + 7) / 8;
        if (signature.Length != 2 * coordinate)
        {
            throw new InvalidOperationException(
                $"The signer returned a {signature.Length}-byte ECDSA signature value for a {key.Curve} key, neither a DER ECDSA-Sig-Value nor {2 * coordinate} bytes of r and s.");
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteIntegerUnsigned(signature.AsSpan(0, coordinate));
            writer.WriteIntegerUnsigned(signature.AsSpan(coordinate));
        }

        return writer.Encode();

        static bool IsDerEcdsaSignature(byte[] value)
        {
            try
            {
                var reader = new AsnReader(value, AsnEncodingRules.DER);
                AsnReader sequence = reader.ReadSequence();
                sequence.ReadIntegerBytes();
                sequence.ReadIntegerBytes();
                return !sequence.HasData && !reader.HasData;
            }
            catch (AsnContentException)
            {
                return false;
            }
        }
    }

    /// <summary>The DER-encoded <c>ContentInfo</c> holding the detached <c>SignedData</c> (RFC 5652 §3, §5.1 to §5.3).</summary>
    /// <param name="digestAlgorithm">The digest algorithm of the message digest and the signed attributes.</param>
    /// <param name="key">The signer's key, as <see cref="ReadKey"/> read it.</param>
    /// <param name="padding">For an RSA key, the signature scheme.</param>
    /// <param name="signedAttributes">The signed attributes as <see cref="SignedAttributes"/> encodes them.</param>
    /// <param name="signature">The signature value over the signed attributes' digest, as <see cref="NormalizeSignature"/> returns it.</param>
    /// <param name="certificate">The signer's certificate.</param>
    /// <param name="chain">The other certificates to embed.</param>
    /// <param name="macToken">
    /// A PDF MAC token to carry as the signer's <c>pdfMacData</c> unsigned attribute (ISO/TS 32004 §6.5.2), or <see langword="null"/>.
    /// </param>
    /// <param name="timestampToken">
    /// An RFC 3161 time-stamp token over <paramref name="signature"/> to carry as the signer's <c>id-aa-signatureTimeStampToken</c>
    /// unsigned attribute (RFC 3161 Appendix A; ISO 32000-2 §12.8.3.3.1, §12.8.3.4.3 b), PAdES B-T), or <see langword="null"/>.
    /// </param>
    public static byte[] Encode(
        HashAlgorithmName digestAlgorithm,
        SigningKey key,
        RSASignaturePadding padding,
        byte[] signedAttributes,
        byte[] signature,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        byte[]? macToken = null,
        byte[]? timestampToken = null)
    {
        string digestOid = DigestOid(digestAlgorithm);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(SignedDataOid);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (writer.PushSequence())
            {
                // SignedData version 1: issuer-and-serial signer, id-data content, no attribute certificates (§5.1).
                writer.WriteInteger(1);
                using (writer.PushSetOf())
                {
                    WriteAlgorithm(writer, digestOid, withNullParameters: false);
                }

                // Detached: the encapsulated content info names id-data and holds no content (§5.2).
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(DataOid);
                }

                using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    writer.WriteEncodedValue(certificate.RawData);
                    foreach (X509Certificate2 other in chain)
                    {
                        if (!other.RawDataMemory.Span.SequenceEqual(certificate.RawDataMemory.Span))
                        {
                            writer.WriteEncodedValue(other.RawData);
                        }
                    }
                }

                using (writer.PushSetOf())
                using (writer.PushSequence())
                {
                    WriteSignerInfo(writer, digestAlgorithm, key, padding, signedAttributes, signature, certificate, macToken, timestampToken);
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// How many bytes to reserve for the encoded signature: the certificates, the longest signature value the key makes, the issuer
    /// name (twice: the signer identifier and the signing certificate attribute), and room for the rest of the structure (and for an
    /// unsigned attribute a later revision may add), plus <paramref name="timestampTokenSize"/> and its attribute for a signature
    /// time-stamp token.
    /// </summary>
    public static int EstimateLength(SigningKey key, X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain, int timestampTokenSize = 0)
    {
        int certificates = certificate.RawData.Length;
        foreach (X509Certificate2 other in chain)
        {
            certificates += other.RawData.Length;
        }

        // An ECDSA-Sig-Value is a sequence of two integers of up to the coordinate size plus a sign byte (RFC 5753 §7.2).
        int signature = key.Type == SigningKeyType.Ecdsa ? (2 * (((key.Size + 7) / 8) + 3)) + 4 : (key.Size + 7) / 8;
        // The attribute around a token: its SEQUENCE, the OID, the SET, and the [1] set when it is the only unsigned attribute.
        int timestamp = timestampTokenSize > 0 ? timestampTokenSize + 32 : 0;
        return certificates + (2 * certificate.IssuerName.RawData.Length) + signature + 1280 + timestamp;
    }

    /// <summary>
    /// <c>SigningCertificateV2</c> with one <c>ESSCertIDv2</c>: the certificate's hash with the signature's digest algorithm (absent
    /// when SHA-256, the default) and the issuer and serial number (RFC 5035 §4, §5.4; ETSI EN 319 122-1 §5.2.2).
    /// </summary>
    private static void WriteSigningCertificateV2(AsnWriter writer, HashAlgorithmName digestAlgorithm, X509Certificate2 certificate)
    {
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            if (digestAlgorithm != HashAlgorithmName.SHA256)
            {
                WriteAlgorithm(writer, DigestOid(digestAlgorithm), withNullParameters: false);
            }

            writer.WriteOctetString(IncrementalDigest.HashData(digestAlgorithm, certificate.RawData));
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true)))
                {
                    writer.WriteEncodedValue(certificate.IssuerName.RawData);
                }

                writer.WriteInteger(certificate.SerialNumberBytes.Span);
            }
        }
    }

    private static void WriteSignerInfo(
        AsnWriter writer,
        HashAlgorithmName digestAlgorithm,
        SigningKey key,
        RSASignaturePadding padding,
        byte[] signedAttributes,
        byte[] signature,
        X509Certificate2 certificate,
        byte[]? macToken,
        byte[]? timestampToken)
    {
        writer.WriteInteger(1);
        using (writer.PushSequence())
        {
            writer.WriteEncodedValue(certificate.IssuerName.RawData);
            writer.WriteInteger(certificate.SerialNumberBytes.Span);
        }

        WriteAlgorithm(writer, DigestOid(digestAlgorithm), withNullParameters: false);

        // The signed attributes go in with the implicit [0] tag instead of SET OF; the content octets are the ones hashed (§5.4).
        byte[] tagged = (byte[])signedAttributes.Clone();
        tagged[0] = 0xA0;
        writer.WriteEncodedValue(tagged);

        WriteSignatureAlgorithm(writer, digestAlgorithm, key, padding);
        writer.WriteOctetString(signature);
        if (macToken is null && timestampToken is null)
        {
            return;
        }

        // unsignedAttrs [1] IMPLICIT SET OF Attribute (RFC 5652 §5.3), each attribute with exactly one value: id-attr-pdfMacData,
        // the PDF MAC token, and id-aa-signatureTimeStampToken, the time-stamp token over the signature value (RFC 3161 Appendix A).
        using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
        {
            WriteAttribute(writer, PdfMacDataOid, macToken);
            WriteAttribute(writer, SignatureTimeStampOid, timestampToken);
        }

        static void WriteAttribute(AsnWriter writer, string oid, byte[]? value)
        {
            if (value is null)
            {
                return;
            }

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(oid);
                using (writer.PushSetOf())
                {
                    writer.WriteEncodedValue(value);
                }
            }
        }
    }

    /// <summary>
    /// The signer info's <c>signatureAlgorithm</c>: <c>rsaEncryption</c> for RSA PKCS #1 v1.5 (RFC 3370 §3.2), <c>id-RSASSA-PSS</c>
    /// with its parameters (RFC 4056 §2), or the ECDSA algorithm with the digest (RFC 5753 §2.1.1, RFC 5758 §3.2; NIST CSOR for
    /// SHA-3).
    /// </summary>
    private static void WriteSignatureAlgorithm(AsnWriter writer, HashAlgorithmName digestAlgorithm, SigningKey key, RSASignaturePadding padding)
    {
        if (key.Type == SigningKeyType.Ecdsa)
        {
            string oid = digestAlgorithm == HashAlgorithmName.SHA256 ? "1.2.840.10045.4.3.2"
                : digestAlgorithm == HashAlgorithmName.SHA384 ? "1.2.840.10045.4.3.3"
                : digestAlgorithm == HashAlgorithmName.SHA512 ? "1.2.840.10045.4.3.4"
                : digestAlgorithm == Sha3_256 ? "2.16.840.1.101.3.4.3.10"
                : digestAlgorithm == Sha3_384 ? "2.16.840.1.101.3.4.3.11"
                : "2.16.840.1.101.3.4.3.12";
            WriteAlgorithm(writer, oid, withNullParameters: false);
            return;
        }

        if (padding != RSASignaturePadding.Pss)
        {
            WriteAlgorithm(writer, RsaEncryptionOid, withNullParameters: true);
            return;
        }

        // RSASSA-PSS-params (RFC 4055 §3.1): the digest, MGF1 with the same digest, and a salt as long as the digest, which is what
        // the platform's RSASignaturePadding.Pss signs with; the trailer field is the default.
        string digestOid = DigestOid(digestAlgorithm);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(RsaPssOid);
            using (writer.PushSequence())
            {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    WriteAlgorithm(writer, digestOid, withNullParameters: false);
                }

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Mgf1Oid);
                    WriteAlgorithm(writer, digestOid, withNullParameters: false);
                }

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true)))
                {
                    writer.WriteInteger(IncrementalDigest.HashData(digestAlgorithm, []).Length);
                }
            }
        }
    }

    private static void WriteAlgorithm(AsnWriter writer, string oid, bool withNullParameters)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(oid);
            if (withNullParameters)
            {
                writer.WriteNull();
            }
        }
    }
}

/// <summary>The kinds of signing key the CMS encoder describes.</summary>
internal enum SigningKeyType
{
    /// <summary>An RSA key (<c>rsaEncryption</c>), signing with PKCS #1 v1.5 or RSASSA-PSS.</summary>
    Rsa,

    /// <summary>An RSA key restricted to RSASSA-PSS (<c>id-RSASSA-PSS</c>, RFC 4055 §1.2).</summary>
    RsaPss,

    /// <summary>An elliptic curve key, signing with ECDSA (RFC 5480).</summary>
    Ecdsa,
}

/// <summary>What the CMS encoder knows of the signer's public key.</summary>
/// <param name="Type">The kind of key.</param>
/// <param name="Size">The modulus or field size in bits.</param>
/// <param name="Curve">The NIST name of an ECDSA key's curve, or <see langword="null"/>.</param>
internal readonly record struct SigningKey(SigningKeyType Type, int Size, string? Curve);
