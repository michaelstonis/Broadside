using System.Formats.Asn1;

namespace Broadside.Signatures.Validation;

/// <summary>
/// The parts of a CMS <c>SignedData</c> (RFC 5652 §5) that signature validation reads: the encapsulated content, the certificates,
/// and the first signer's identifier, algorithms, signed attributes and signature value. BER is accepted throughout, since signers
/// before PDF 2.0 wrote indefinite lengths.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.3.3.1: "the value of Contents shall be a DER-encoded CMS binary data object containing the signature". Unsigned
/// attributes (a timestamp token, a PDF MAC token per ISO/TS 32004 §6.5) are skipped: they are not part of the signature value.
/// </remarks>
internal sealed class CmsSignature
{
    /// <summary>id-signedData (RFC 5652 §5.1).</summary>
    public const string SignedDataOid = "1.2.840.113549.1.7.2";

    /// <summary>id-data (RFC 5652 §4).</summary>
    public const string DataOid = "1.2.840.113549.1.7.1";

    /// <summary>id-ct-TSTInfo (RFC 3161 §2.4.2).</summary>
    public const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    private const string ContentTypeAttribute = "1.2.840.113549.1.9.3";
    private const string MessageDigestAttribute = "1.2.840.113549.1.9.4";

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

            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                _ = signedData.ReadEncodedValue();
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
                            default:
                                _ = values.ReadEncodedValue();
                                break;
                        }
                    }
                }
            }

            AlgorithmIdentifier signatureAlgorithm = AlgorithmIdentifier.Read(signer);
            byte[] signatureValue = signer.ReadOctetString();
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

    /// <summary>Reads the message imprint of a <c>TSTInfo</c> (RFC 3161 §2.4.2): its digest algorithm and the digest.</summary>
    public static bool TryReadMessageImprint(byte[] tstInfo, out AlgorithmIdentifier algorithm, out byte[] digest)
    {
        algorithm = default;
        digest = [];
        try
        {
            AsnReader info = new AsnReader(tstInfo, AsnEncodingRules.BER).ReadSequence();
            _ = info.ReadIntegerBytes();
            _ = info.ReadObjectIdentifier();
            AsnReader imprint = info.ReadSequence();
            algorithm = AlgorithmIdentifier.Read(imprint);
            digest = imprint.ReadOctetString();
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static ReadOnlyMemory<byte> TrimLeadingZeros(ReadOnlyMemory<byte> value)
    {
        int start = value.Span.IndexOfAnyExcept((byte)0);
        return start < 0 ? ReadOnlyMemory<byte>.Empty : value[start..];
    }
}
