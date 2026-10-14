using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// Encodes the detached CMS <c>SignedData</c> of a byte range signature (ISO 32000-2 §12.8.3.3, <c>adbe.pkcs7.detached</c>; RFC 5652
/// §5): one signer identified by issuer and serial number, signed attributes holding the content type and the message digest, and the
/// signer's certificates. DER throughout, so the signed attributes are encoded exactly as they are hashed.
/// </summary>
/// <remarks>
/// Built here rather than with <c>SignedCms</c>, which hashes the content itself from one array: the byte ranges are hashed in
/// windows through <c>IncrementalDigest</c>, and the signature value comes from an <see cref="IPdfSigner"/> that only sees a digest.
/// </remarks>
internal static class CmsSignedData
{
    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    private const string DataOid = "1.2.840.113549.1.7.1";
    private const string ContentTypeOid = "1.2.840.113549.1.9.3";
    private const string MessageDigestOid = "1.2.840.113549.1.9.4";
    private const string RsaEncryptionOid = "1.2.840.113549.1.1.1";
    private const string PdfMacDataOid = "1.0.32004.1.2";

    /// <summary>Throws for a digest algorithm byte range signing does not write (§12.8.3.1, Table 260).</summary>
    public static void RequireSupportedDigest(HashAlgorithmName algorithm) => DigestOid(algorithm);

    /// <summary>Throws for a certificate whose key the CMS signer info cannot describe.</summary>
    public static void RequireSupportedKey(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (certificate.PublicKey.Oid.Value != RsaEncryptionOid)
        {
            throw new NotSupportedException(
                $"Signing with a {certificate.PublicKey.Oid.FriendlyName ?? certificate.PublicKey.Oid.Value} key is not supported yet; RSA keys are.");
        }
    }

    /// <summary>The object identifier of a digest algorithm (NIST CSOR, RFC 5754 §2).</summary>
    public static string DigestOid(HashAlgorithmName algorithm)
    {
        if (algorithm == HashAlgorithmName.SHA256)
        {
            return "2.16.840.1.101.3.4.2.1";
        }

        if (algorithm == HashAlgorithmName.SHA384)
        {
            return "2.16.840.1.101.3.4.2.2";
        }

        if (algorithm == HashAlgorithmName.SHA512)
        {
            return "2.16.840.1.101.3.4.2.3";
        }

        throw new NotSupportedException($"The digest algorithm {algorithm.Name} is not supported for signing; use SHA-256, SHA-384 or SHA-512.");
    }

    /// <summary>
    /// The DER encoding of the signed attributes as RFC 5652 §5.4 hashes them, with the universal <c>SET OF</c> tag: the content type
    /// (<c>id-data</c>, §11.1) and the message digest of the byte ranges (§11.2).
    /// </summary>
    public static byte[] SignedAttributes(ReadOnlySpan<byte> messageDigest)
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
        }

        return writer.Encode();
    }

    /// <summary>The DER-encoded <c>ContentInfo</c> holding the detached <c>SignedData</c> (RFC 5652 §3, §5.1 to §5.3).</summary>
    /// <param name="digestAlgorithm">The digest algorithm of the message digest and the signed attributes.</param>
    /// <param name="signedAttributes">The signed attributes as <see cref="SignedAttributes"/> encodes them.</param>
    /// <param name="signature">The signature value over the signed attributes' digest.</param>
    /// <param name="certificate">The signer's certificate.</param>
    /// <param name="chain">The other certificates to embed.</param>
    /// <param name="macToken">
    /// A PDF MAC token to carry as the signer's <c>pdfMacData</c> unsigned attribute (ISO/TS 32004 §6.5.2), or <see langword="null"/>.
    /// </param>
    public static byte[] Encode(
        HashAlgorithmName digestAlgorithm,
        byte[] signedAttributes,
        byte[] signature,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        byte[]? macToken = null)
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
                    WriteSignerInfo(writer, digestOid, signedAttributes, signature, certificate, macToken);
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// How many bytes to reserve for the encoded signature: the certificates, a signature value as long as the key's modulus, the
    /// issuer name, and room for the rest of the structure (and for an unsigned attribute a later revision may add).
    /// </summary>
    public static int EstimateLength(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> chain)
    {
        int certificates = certificate.RawData.Length;
        foreach (X509Certificate2 other in chain)
        {
            certificates += other.RawData.Length;
        }

        using RSA? key = certificate.GetRSAPublicKey();
        int signature = key is null ? 512 : (key.KeySize + 7) / 8;
        return certificates + certificate.IssuerName.RawData.Length + signature + 1024;
    }

    private static void WriteSignerInfo(AsnWriter writer, string digestOid, byte[] signedAttributes, byte[] signature, X509Certificate2 certificate, byte[]? macToken)
    {
        writer.WriteInteger(1);
        using (writer.PushSequence())
        {
            writer.WriteEncodedValue(certificate.IssuerName.RawData);
            writer.WriteInteger(certificate.SerialNumberBytes.Span);
        }

        WriteAlgorithm(writer, digestOid, withNullParameters: false);

        // The signed attributes go in with the implicit [0] tag instead of SET OF; the content octets are the ones hashed (§5.4).
        byte[] tagged = (byte[])signedAttributes.Clone();
        tagged[0] = 0xA0;
        writer.WriteEncodedValue(tagged);

        WriteAlgorithm(writer, RsaEncryptionOid, withNullParameters: true);
        writer.WriteOctetString(signature);
        if (macToken is not null)
        {
            // unsignedAttrs [1] IMPLICIT SET OF Attribute (RFC 5652 §5.3): id-attr-pdfMacData with exactly one value, the token.
            using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(PdfMacDataOid);
                using (writer.PushSetOf())
                {
                    writer.WriteEncodedValue(macToken);
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
