using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures.Validation;

/// <summary>
/// A decoded OCSP response (RFC 6960 §4.2.1): the signed <c>ResponseData</c> with its responder, production time and single responses,
/// the signature over it, the certificates it carries and its nonce. Only successful basic responses decode.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1 (ASN.1, explicit tags: <c>good</c>, <c>revoked</c> and <c>unknown</c> are the implicitly tagged exceptions) and
/// §4.4.1 (nonce, RFC 8954 §2.1). A bare <c>BasicOCSPResponse</c> is accepted too, since some document security stores hold one.
/// </remarks>
internal sealed class OcspResponse
{
    /// <summary>id-pkix-ocsp-basic (RFC 6960 §4.2.1).</summary>
    public const string BasicResponseOid = "1.3.6.1.5.5.7.48.1.1";

    /// <summary>id-pkix-ocsp-nonce (RFC 6960 §4.4.1).</summary>
    public const string NonceOid = "1.3.6.1.5.5.7.48.1.2";

    private static readonly Asn1Tag Context0 = new(TagClass.ContextSpecific, 0);
    private static readonly Asn1Tag Context1 = new(TagClass.ContextSpecific, 1);
    private static readonly Asn1Tag Context2 = new(TagClass.ContextSpecific, 2);

    private OcspResponse()
    {
    }

    /// <summary>Gets the DER encoding of <c>tbsResponseData</c>, what the signature covers.</summary>
    public ReadOnlyMemory<byte> SignedData { get; private init; }

    /// <summary>Gets the signature algorithm.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; private init; }

    /// <summary>Gets the signature value.</summary>
    public byte[] Signature { get; private init; } = [];

    /// <summary>Gets the responder's name (<c>byName</c>), or empty.</summary>
    public ReadOnlyMemory<byte> ResponderName { get; private init; }

    /// <summary>Gets the SHA-1 hash of the responder's public key (<c>byKey</c>), or empty.</summary>
    public ReadOnlyMemory<byte> ResponderKeyHash { get; private init; }

    /// <summary>Gets when the response was signed.</summary>
    public DateTimeOffset ProducedAt { get; private init; }

    /// <summary>Gets the single responses.</summary>
    public IReadOnlyList<SingleOcspResponse> Responses { get; private init; } = [];

    /// <summary>Gets the encodings of the certificates the response carries (a delegated responder's).</summary>
    public IReadOnlyList<byte[]> Certificates { get; private init; } = [];

    /// <summary>Gets the value of the nonce extension, or <see langword="null"/> when there is none.</summary>
    public byte[]? Nonce { get; private init; }

    /// <summary>Gets the DER <c>OCSPResponse</c> (a bare <c>BasicOCSPResponse</c> wrapped in one), as a document security store holds it (ISO 32000-2 §12.8.4.3, Table 261).</summary>
    public ReadOnlyMemory<byte> Encoded { get; private set; }

    /// <summary>Decodes an <c>OCSPResponse</c> whose status is successful and whose type is basic, or a <c>BasicOCSPResponse</c>.</summary>
    /// <param name="encoded">The encoding.</param>
    /// <param name="response">The response.</param>
    /// <returns>Whether it decoded.</returns>
    public static bool TryDecode(ReadOnlyMemory<byte> encoded, out OcspResponse? response)
    {
        response = null;
        try
        {
            AsnReader outer = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
            bool wrapped = outer.PeekTag().HasSameClassAndValue(Asn1Tag.Enumerated);
            ReadOnlyMemory<byte> basicEncoding = encoded;
            if (wrapped)
            {
                // OCSPResponseStatus successful (0) is the only one with response bytes.
                if (ReadEnumerated(outer) != 0 || !outer.HasData)
                {
                    return false;
                }

                AsnReader bytes = outer.ReadSequence(Context0).ReadSequence();
                if (bytes.ReadObjectIdentifier() != BasicResponseOid)
                {
                    return false;
                }

                basicEncoding = bytes.ReadOctetString();
                outer = new AsnReader(basicEncoding, AsnEncodingRules.BER).ReadSequence();
            }

            response = ReadBasic(outer);
            response.Encoded = wrapped ? encoded : Wrap(basicEncoding.Span);
            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    /// <summary>An <c>OCSPResponse</c> with status successful around <paramref name="basic"/> (RFC 6960 §4.2.1).</summary>
    private static byte[] Wrap(ReadOnlySpan<byte> basic)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteEncodedValue([0x0A, 0x01, 0x00]);
            using (writer.PushSequence(Context0))
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(BasicResponseOid);
                writer.WriteOctetString(basic);
            }
        }

        return writer.Encode();
    }

    private static OcspResponse ReadBasic(AsnReader basic)
    {
        ReadOnlyMemory<byte> signed = basic.PeekEncodedValue();
        AsnReader data = basic.ReadSequence();
        AlgorithmIdentifier algorithm = AlgorithmIdentifier.Read(basic);
        byte[] signature = basic.ReadBitString(out _);
        var certificates = new List<byte[]>();
        if (basic.HasData && basic.PeekTag().HasSameClassAndValue(Context0))
        {
            AsnReader list = basic.ReadSequence(Context0).ReadSequence();
            while (list.HasData)
            {
                certificates.Add(list.ReadEncodedValue().ToArray());
            }
        }

        if (data.PeekTag().HasSameClassAndValue(Context0))
        {
            _ = data.ReadEncodedValue();
        }

        ReadOnlyMemory<byte> name = default;
        ReadOnlyMemory<byte> keyHash = default;
        Asn1Tag responder = data.PeekTag();
        if (responder.HasSameClassAndValue(Context1))
        {
            name = data.ReadSequence(Context1).ReadEncodedValue();
        }
        else
        {
            keyHash = data.ReadSequence(Context2).ReadOctetString();
        }

        DateTimeOffset producedAt = data.ReadGeneralizedTime();
        var responses = new List<SingleOcspResponse>();
        AsnReader list2 = data.ReadSequence();
        while (list2.HasData)
        {
            responses.Add(ReadSingle(list2.ReadSequence()));
        }

        byte[]? nonce = null;
        if (data.HasData && data.PeekTag().HasSameClassAndValue(Context1))
        {
            AsnReader extensions = data.ReadSequence(Context1).ReadSequence();
            while (extensions.HasData)
            {
                AsnReader extension = extensions.ReadSequence();
                string id = extension.ReadObjectIdentifier();
                if (extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
                {
                    _ = extension.ReadBoolean();
                }

                byte[] value = extension.ReadOctetString();
                if (id == NonceOid)
                {
                    nonce = value;
                }
            }
        }

        return new OcspResponse
        {
            SignedData = signed,
            SignatureAlgorithm = algorithm,
            Signature = signature,
            ResponderName = name,
            ResponderKeyHash = keyHash,
            ProducedAt = producedAt,
            Responses = responses,
            Certificates = certificates,
            Nonce = nonce,
        };
    }

    private static SingleOcspResponse ReadSingle(AsnReader single)
    {
        AsnReader id = single.ReadSequence();
        AlgorithmIdentifier hash = AlgorithmIdentifier.Read(id);
        byte[] nameHash = id.ReadOctetString();
        byte[] keyHash = id.ReadOctetString();
        ReadOnlyMemory<byte> serial = TrimLeadingZeros(id.ReadIntegerBytes());

        Asn1Tag tag = single.PeekTag();
        PdfRevocationStatus status;
        DateTimeOffset? revocationTime = null;
        X509RevocationReason? reason = null;
        if (tag.HasSameClassAndValue(Context0))
        {
            single.ReadNull(Context0);
            status = PdfRevocationStatus.Good;
        }
        else if (tag.HasSameClassAndValue(Context1))
        {
            AsnReader revoked = single.ReadSequence(Context1);
            revocationTime = revoked.ReadGeneralizedTime();
            if (revoked.HasData && revoked.PeekTag().HasSameClassAndValue(Context0))
            {
                AsnReader value = revoked.ReadSequence(Context0);
                reason = ReadEnumerated(value) is { } code ? (X509RevocationReason)code : null;
            }

            status = PdfRevocationStatus.Revoked;
        }
        else
        {
            _ = single.ReadEncodedValue();
            status = PdfRevocationStatus.Unknown;
        }

        DateTimeOffset thisUpdate = single.ReadGeneralizedTime();
        DateTimeOffset? nextUpdate = null;
        if (single.HasData && single.PeekTag().HasSameClassAndValue(Context0))
        {
            nextUpdate = single.ReadSequence(Context0).ReadGeneralizedTime();
        }

        return new SingleOcspResponse(hash, nameHash, keyHash, serial, status, revocationTime, reason, thisUpdate, nextUpdate);
    }

    /// <summary>Reads a small ENUMERATED value, or <see langword="null"/> when it does not fit in 31 bits.</summary>
    internal static int? ReadEnumerated(AsnReader reader)
    {
        ReadOnlySpan<byte> bytes = reader.ReadEnumeratedBytes().Span;
        if (bytes.Length is 0 or > 4 || (bytes[0] & 0x80) != 0)
        {
            return null;
        }

        int value = 0;
        foreach (byte item in bytes)
        {
            value = (value << 8) | item;
        }

        return value;
    }

    internal static ReadOnlyMemory<byte> TrimLeadingZeros(ReadOnlyMemory<byte> value)
    {
        int start = value.Span.IndexOfAnyExcept((byte)0);
        return start < 0 ? ReadOnlyMemory<byte>.Empty : value[start..];
    }
}

/// <summary>One <c>SingleResponse</c> of an OCSP response (RFC 6960 §4.2.1): which certificate, its status and the update times.</summary>
/// <param name="HashAlgorithm">The <c>CertID</c> hash algorithm.</param>
/// <param name="IssuerNameHash">The hash of the issuer's distinguished name.</param>
/// <param name="IssuerKeyHash">The hash of the issuer's public key.</param>
/// <param name="SerialNumber">The certificate's serial number, leading zeros removed.</param>
/// <param name="Status">Good, revoked or unknown.</param>
/// <param name="RevocationTime">When it was revoked.</param>
/// <param name="Reason">Why, when given.</param>
/// <param name="ThisUpdate">The time the status is known correct at.</param>
/// <param name="NextUpdate">The time newer information will be available at, if given.</param>
internal sealed record SingleOcspResponse(
    AlgorithmIdentifier HashAlgorithm,
    byte[] IssuerNameHash,
    byte[] IssuerKeyHash,
    ReadOnlyMemory<byte> SerialNumber,
    PdfRevocationStatus Status,
    DateTimeOffset? RevocationTime,
    X509RevocationReason? Reason,
    DateTimeOffset ThisUpdate,
    DateTimeOffset? NextUpdate);
