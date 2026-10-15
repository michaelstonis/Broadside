using System.Formats.Asn1;

namespace Broadside.Signatures.Validation;

/// <summary>
/// The parts of a CMS <c>SignedData</c> (RFC 5652 §5) that signature validation reads: the encapsulated content, the certificates and
/// revocation information, and the first signer's identifier, algorithms, signed attributes, signature value and signature time-stamp
/// tokens. BER is accepted throughout, since signers before PDF 2.0 wrote indefinite lengths.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.3.3.1: "the value of Contents shall be a DER-encoded CMS binary data object containing the signature". Revocation
/// information comes from the <c>crls</c> field (RFC 5652 §10.2.1, with OCSP responses per RFC 5940 §4) and the
/// <c>adbe-revocationInfoArchival</c> signed attribute (§12.8.3.3.2); the ESS signing-certificate attributes from RFC 2634 §5.4 and
/// RFC 5035 §3; time-stamp tokens from the id-aa-signatureTimeStampToken unsigned attribute (RFC 3161 Appendix A). Other unsigned
/// attributes (a PDF MAC token per ISO/TS 32004 §6.5) are skipped. A malformed optional attribute is left out rather than failing the
/// whole object.
/// </remarks>
internal sealed class CmsSignature
{
    /// <summary>id-signedData (RFC 5652 §5.1).</summary>
    public const string SignedDataOid = "1.2.840.113549.1.7.2";

    /// <summary>id-data (RFC 5652 §4).</summary>
    public const string DataOid = "1.2.840.113549.1.7.1";

    /// <summary>id-ct-TSTInfo (RFC 3161 §2.4.2).</summary>
    public const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    /// <summary>id-ri-ocsp-response (RFC 5940 §4): an OCSP response as other revocation information.</summary>
    public const string OcspRevocationInfoOid = "1.3.6.1.5.5.7.16.2";

    private const string ContentTypeAttribute = "1.2.840.113549.1.9.3";
    private const string MessageDigestAttribute = "1.2.840.113549.1.9.4";
    private const string SigningCertificateAttribute = "1.2.840.113549.1.9.16.2.12";
    private const string SigningCertificateV2Attribute = "1.2.840.113549.1.9.16.2.47";
    private const string RevocationInfoArchivalAttribute = "1.2.840.113583.1.1.8";
    private const string SignatureTimeStampAttribute = "1.2.840.113549.1.9.16.2.14";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string Sha1Oid = "1.3.14.3.2.26";

    private CmsSignature()
    {
    }

    /// <summary>Gets the encapsulated content type (<c>eContentType</c>).</summary>
    public string ContentType { get; private init; } = DataOid;

    /// <summary>Gets the encapsulated content, or <see langword="null"/> for a detached signature.</summary>
    public byte[]? Content { get; private init; }

    /// <summary>Gets the encodings of the certificates the object carries (plain X.509 certificates only).</summary>
    public IReadOnlyList<byte[]> Certificates { get; private init; } = [];

    /// <summary>Gets the signer's issuer name, for an issuer-and-serial-number identifier.</summary>
    public ReadOnlyMemory<byte> IssuerName { get; private init; }

    /// <summary>Gets the signer's serial number (big-endian, leading zeros removed), for an issuer-and-serial-number identifier.</summary>
    public ReadOnlyMemory<byte> SerialNumber { get; private init; }

    /// <summary>Gets the signer's subject key identifier, for a <c>[0]</c> identifier.</summary>
    public ReadOnlyMemory<byte> SubjectKeyIdentifier { get; private init; }

    /// <summary>Gets the signer's digest algorithm.</summary>
    public AlgorithmIdentifier DigestAlgorithm { get; private init; }

    /// <summary>Gets the signer's signature algorithm.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; private init; }

    /// <summary>
    /// Gets the signed attributes as RFC 5652 §5.4 hashes them (the <c>[0]</c> tag replaced by <c>SET OF</c>), or <see langword="null"/>
    /// when there are none.
    /// </summary>
    public byte[]? SignedAttributes { get; private init; }

    /// <summary>Gets the values of the message-digest attribute (RFC 5652 §11.2), one per value.</summary>
    public IReadOnlyList<byte[]> MessageDigests { get; private init; } = [];

    /// <summary>Gets the values of the content-type attribute (RFC 5652 §11.1), one per value.</summary>
    public IReadOnlyList<string> ContentTypes { get; private init; } = [];

    /// <summary>Gets the signature value.</summary>
    public byte[] Signature { get; private init; } = [];

    /// <summary>Gets the DER encodings of the certificate revocation lists the object carries (crls field, adbe attribute).</summary>
    public IReadOnlyList<byte[]> Crls { get; private init; } = [];

    /// <summary>Gets the encodings of the OCSP responses the object carries (crls field per RFC 5940, adbe attribute).</summary>
    public IReadOnlyList<byte[]> OcspResponses { get; private init; } = [];

    /// <summary>
    /// Gets the first certificate identifier of the signer's ESS signing-certificate(-v2) attribute, the one that names the signing
    /// certificate (RFC 5035 §3), or <see langword="null"/> when there is none.
    /// </summary>
    public EssCertificateId? SigningCertificate { get; private init; }

    /// <summary>Gets the encodings of the signer's signature time-stamp tokens (RFC 3161 Appendix A), each a <c>ContentInfo</c>.</summary>
    public IReadOnlyList<byte[]> TimestampTokens { get; private init; } = [];

    /// <summary>Gets how many signer infos the object holds; only the first is validated.</summary>
    public int SignerCount { get; private init; }

    /// <summary>Decodes a <c>ContentInfo</c> holding <c>SignedData</c>, ignoring trailing zero padding (§12.8.3.3.1).</summary>
    /// <param name="encoded">The bytes of the <c>Contents</c> string.</param>
    /// <param name="signature">The decoded object.</param>
    /// <param name="error">Why it could not be decoded.</param>
    public static bool TryDecode(ReadOnlyMemory<byte> encoded, out CmsSignature signature, out string error)
    {
        signature = new CmsSignature();
        try
        {
            var outer = new AsnReader(encoded, AsnEncodingRules.BER);
            AsnReader contentInfo = outer.ReadSequence();
            string contentType = contentInfo.ReadObjectIdentifier();
            if (contentType != SignedDataOid)
            {
                error = $"The CMS object holds {contentType}, not signed data.";
                return false;
            }

            AsnReader signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            _ = signedData.ReadIntegerBytes();
            _ = signedData.ReadSetOf(skipSortOrderValidation: true);

            AsnReader encapsulated = signedData.ReadSequence();
            string encapsulatedType = encapsulated.ReadObjectIdentifier();
            byte[]? content = null;
            if (encapsulated.HasData)
            {
                content = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadOctetString();
            }

            var certificates = new List<byte[]>();
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                AsnReader set = signedData.ReadSetOf(skipSortOrderValidation: true, new Asn1Tag(TagClass.ContextSpecific, 0));
                while (set.HasData)
                {
                    Asn1Tag tag = set.PeekTag();
                    ReadOnlyMemory<byte> value = set.ReadEncodedValue();
                    if (tag.HasSameClassAndValue(Asn1Tag.Sequence))
                    {
                        certificates.Add(value.ToArray());
                    }
                }
            }

            var crls = new List<byte[]>();
            var ocsps = new List<byte[]>();
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                ReadRevocationChoices(signedData.ReadEncodedValue(), crls, ocsps);
            }

            AsnReader signerInfos = signedData.ReadSetOf(skipSortOrderValidation: true);
            if (!signerInfos.HasData)
            {
                error = "The CMS object has no signer information.";
                return false;
            }

            AsnReader signer = signerInfos.ReadSequence();
            int signerCount = 1;
            while (signerInfos.HasData)
            {
                _ = signerInfos.ReadEncodedValue();
                signerCount++;
            }

            _ = signer.ReadIntegerBytes();
            ReadOnlyMemory<byte> issuer = default;
            ReadOnlyMemory<byte> serial = default;
            ReadOnlyMemory<byte> keyIdentifier = default;
            if (signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                keyIdentifier = signer.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
            }
            else
            {
                AsnReader issuerAndSerial = signer.ReadSequence();
                issuer = issuerAndSerial.ReadEncodedValue();
                serial = TrimLeadingZeros(issuerAndSerial.ReadIntegerBytes());
            }

            AlgorithmIdentifier digestAlgorithm = AlgorithmIdentifier.Read(signer);
            byte[]? signedAttributes = null;
            var digests = new List<byte[]>();
            var types = new List<string>();
            EssCertificateId? signingCertificate = null;
            EssCertificateId? signingCertificateV1 = null;
            if (signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                signedAttributes = signer.PeekEncodedValue().ToArray();
                signedAttributes[0] = 0x31;
                AsnReader attributes = signer.ReadSetOf(skipSortOrderValidation: true, new Asn1Tag(TagClass.ContextSpecific, 0));
                while (attributes.HasData)
                {
                    AsnReader attribute = attributes.ReadSequence();
                    string type = attribute.ReadObjectIdentifier();
                    AsnReader values = attribute.ReadSetOf(skipSortOrderValidation: true);
                    while (values.HasData)
                    {
                        switch (type)
                        {
                            case MessageDigestAttribute:
                                digests.Add(values.ReadOctetString());
                                break;
                            case ContentTypeAttribute:
                                types.Add(values.ReadObjectIdentifier());
                                break;
                            case SigningCertificateV2Attribute:
                                signingCertificate ??= ReadEssCertificateId(values.ReadEncodedValue(), version2: true);
                                break;
                            case SigningCertificateAttribute:
                                signingCertificateV1 ??= ReadEssCertificateId(values.ReadEncodedValue(), version2: false);
                                break;
                            case RevocationInfoArchivalAttribute:
                                ReadRevocationInfoArchival(values.ReadEncodedValue(), crls, ocsps);
                                break;
                            default:
                                _ = values.ReadEncodedValue();
                                break;
                        }
                    }
                }
            }

            AlgorithmIdentifier signatureAlgorithm = AlgorithmIdentifier.Read(signer);
            byte[] signatureValue = signer.ReadOctetString();
            var tokens = new List<byte[]>();
            if (signer.HasData && signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                AsnReader unsigned = signer.ReadSetOf(skipSortOrderValidation: true, new Asn1Tag(TagClass.ContextSpecific, 1));
                while (unsigned.HasData)
                {
                    AsnReader attribute = unsigned.ReadSequence();
                    string type = attribute.ReadObjectIdentifier();
                    AsnReader values = attribute.ReadSetOf(skipSortOrderValidation: true);
                    while (values.HasData)
                    {
                        ReadOnlyMemory<byte> value = values.ReadEncodedValue();
                        if (type == SignatureTimeStampAttribute)
                        {
                            tokens.Add(value.ToArray());
                        }
                    }
                }
            }

            signature = new CmsSignature
            {
                ContentType = encapsulatedType,
                Content = content,
                Certificates = certificates,
                IssuerName = issuer,
                SerialNumber = serial,
                SubjectKeyIdentifier = keyIdentifier,
                DigestAlgorithm = digestAlgorithm,
                SignatureAlgorithm = signatureAlgorithm,
                SignedAttributes = signedAttributes,
                MessageDigests = digests,
                ContentTypes = types,
                Signature = signatureValue,
                SignerCount = signerCount,
                Crls = crls,
                OcspResponses = ocsps,
                SigningCertificate = signingCertificate ?? signingCertificateV1,
                TimestampTokens = tokens,
            };
            error = string.Empty;
            return true;
        }
        catch (AsnContentException exception)
        {
            error = $"The CMS object cannot be decoded: {exception.Message}";
            return false;
        }
    }

    /// <summary>Reads a <c>TSTInfo</c> (RFC 3161 §2.4.2): its policy, message imprint and time.</summary>
    public static bool TryReadTstInfo(byte[] tstInfo, out TstInfo info)
    {
        info = default;
        try
        {
            AsnReader sequence = new AsnReader(tstInfo, AsnEncodingRules.BER).ReadSequence();
            _ = sequence.ReadIntegerBytes();
            string policy = sequence.ReadObjectIdentifier();
            AsnReader imprint = sequence.ReadSequence();
            AlgorithmIdentifier algorithm = AlgorithmIdentifier.Read(imprint);
            byte[] digest = imprint.ReadOctetString();
            _ = sequence.ReadIntegerBytes();
            DateTimeOffset time = sequence.ReadGeneralizedTime();
            info = new TstInfo(policy, algorithm, digest, time);
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <c>RevocationInfoChoices</c> (RFC 5652 §10.2.1): CRLs, and OCSP responses as <c>other</c> revocation information
    /// (RFC 5940 §4); other formats are skipped.
    /// </summary>
    private static void ReadRevocationChoices(ReadOnlyMemory<byte> encoded, List<byte[]> crls, List<byte[]> ocsps)
    {
        try
        {
            AsnReader set = new AsnReader(encoded, AsnEncodingRules.BER).ReadSetOf(skipSortOrderValidation: true, new Asn1Tag(TagClass.ContextSpecific, 1));
            while (set.HasData)
            {
                Asn1Tag tag = set.PeekTag();
                if (tag.HasSameClassAndValue(Asn1Tag.Sequence))
                {
                    crls.Add(set.ReadEncodedValue().ToArray());
                }
                else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    AsnReader other = set.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1));
                    if (other.ReadObjectIdentifier() == OcspRevocationInfoOid)
                    {
                        ocsps.Add(other.ReadEncodedValue().ToArray());
                    }
                }
                else
                {
                    _ = set.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException)
        {
            // Revocation information that cannot be read is not used.
        }
    }

    /// <summary>Reads <c>RevocationInfoArchival</c> (ISO 32000-2 §12.8.3.3.2): its CRLs and OCSP responses.</summary>
    private static void ReadRevocationInfoArchival(ReadOnlyMemory<byte> encoded, List<byte[]> crls, List<byte[]> ocsps)
    {
        try
        {
            AsnReader archival = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
            while (archival.HasData)
            {
                Asn1Tag tag = archival.PeekTag();
                List<byte[]>? target = tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)) ? crls
                    : tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)) ? ocsps
                    : null;
                if (target is null)
                {
                    _ = archival.ReadEncodedValue();
                    continue;
                }

                AsnReader items = archival.ReadSequence(tag).ReadSequence();
                while (items.HasData)
                {
                    target.Add(items.ReadEncodedValue().ToArray());
                }
            }
        }
        catch (AsnContentException)
        {
            // Revocation information that cannot be read is not used.
        }
    }

    /// <summary>The first <c>ESSCertID</c> (RFC 2634 §5.4.1) or <c>ESSCertIDv2</c> (RFC 5035 §4) of a signing-certificate attribute.</summary>
    private static EssCertificateId ReadEssCertificateId(ReadOnlyMemory<byte> encoded, bool version2)
    {
        try
        {
            AsnReader certificates = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence().ReadSequence();
            AsnReader identifier = certificates.ReadSequence();
            string algorithm = Sha1Oid;
            if (version2)
            {
                algorithm = identifier.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence) ? AlgorithmIdentifier.Read(identifier).Oid : Sha256Oid;
            }

            byte[] hash = identifier.ReadOctetString();
            ReadOnlyMemory<byte> serial = default;
            if (identifier.HasData)
            {
                AsnReader issuerSerial = identifier.ReadSequence();
                _ = issuerSerial.ReadEncodedValue();
                serial = TrimLeadingZeros(issuerSerial.ReadIntegerBytes());
            }

            return new EssCertificateId(algorithm, hash, serial);
        }
        catch (AsnContentException)
        {
            return new EssCertificateId(string.Empty, [], default);
        }
    }

    private static ReadOnlyMemory<byte> TrimLeadingZeros(ReadOnlyMemory<byte> value)
    {
        int start = value.Span.IndexOfAnyExcept((byte)0);
        return start < 0 ? ReadOnlyMemory<byte>.Empty : value[start..];
    }
}

/// <summary>A certificate identifier of an ESS signing-certificate attribute: the hash of the certificate and, optionally, its serial.</summary>
/// <param name="HashAlgorithm">The hash algorithm's object identifier; empty when the attribute could not be read.</param>
/// <param name="Hash">The hash of the whole certificate.</param>
/// <param name="SerialNumber">The serial number (leading zeros removed), or empty when absent.</param>
internal readonly record struct EssCertificateId(string HashAlgorithm, byte[] Hash, ReadOnlyMemory<byte> SerialNumber);

/// <summary>The parts of a <c>TSTInfo</c> (RFC 3161 §2.4.2) validation uses.</summary>
/// <param name="Policy">The TSA policy.</param>
/// <param name="ImprintAlgorithm">The message imprint's hash algorithm.</param>
/// <param name="Imprint">The message imprint's hash.</param>
/// <param name="Time">The time the token asserts (genTime).</param>
internal readonly record struct TstInfo(string Policy, AlgorithmIdentifier ImprintAlgorithm, byte[] Imprint, DateTimeOffset Time);
