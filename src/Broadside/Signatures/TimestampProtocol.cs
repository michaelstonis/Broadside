using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Broadside.Signatures;

/// <summary>
/// The RFC 3161 time-stamp protocol messages: the <c>TimeStampReq</c> a client sends (§2.4.1) and the <c>TimeStampResp</c> it gets
/// back (§2.4.2), whose token is checked against the request before it is used.
/// </summary>
/// <remarks>
/// RFC 3161 §2.4, as updated by RFC 5816 (ISO 32000-2 §12.8.3.3.1 and §12.8.5.2 require both). Encoded and decoded with
/// <c>System.Formats.Asn1</c>, which every platform has (the platform's <c>Rfc3161TimestampRequest</c> is not available on the
/// browser). The token's own signature is not verified here: that is validation's job (with its certificate path and revocation);
/// the client checks what RFC 3161 §2.4.2 asks the requester to check, that the token answers this request.
/// </remarks>
internal static class TimestampProtocol
{
    /// <summary>The content type of a TSTInfo (RFC 3161 §2.4.2, <c>id-ct-TSTInfo</c>).</summary>
    public const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    private const string SignedDataOid = "1.2.840.113549.1.7.2";

    private static readonly Asn1Tag ContextZero = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag ImplicitOne = new(TagClass.ContextSpecific, 1, isConstructed: true);

    /// <summary>A new nonce: 8 random bytes, a positive INTEGER in its minimal encoding (RFC 3161 §2.4.1: "a large random number").</summary>
    /// <returns>The nonce's big-endian bytes.</returns>
    public static byte[] CreateNonce()
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(8);
        nonce[0] = (byte)((nonce[0] & 0x7F) | 0x40);
        return nonce;
    }

    /// <summary>The DER-encoded <c>TimeStampReq</c> (RFC 3161 §2.4.1), version 1.</summary>
    /// <param name="digestOid">The hash algorithm of <paramref name="imprint"/>.</param>
    /// <param name="imprint">The hash of the data to time-stamp.</param>
    /// <param name="nonce">The nonce, as <see cref="CreateNonce"/> makes it.</param>
    /// <param name="policy">The policy to ask for, or <see langword="null"/> for the authority's default.</param>
    /// <param name="requestCertificates">Whether to ask for the authority's certificate in the token (<c>certReq</c>).</param>
    /// <returns>The request.</returns>
    public static byte[] EncodeRequest(string digestOid, ReadOnlySpan<byte> imprint, ReadOnlySpan<byte> nonce, string? policy, bool requestCertificates)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    // RFC 5754 §2: the SHA-2 (and FIPS 202 SHA-3) parameters are absent.
                    writer.WriteObjectIdentifier(digestOid);
                }

                writer.WriteOctetString(imprint);
            }

            if (policy is not null)
            {
                writer.WriteObjectIdentifier(policy);
            }

            writer.WriteIntegerUnsigned(nonce);
            if (requestCertificates)
            {
                writer.WriteBoolean(true);
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Reads a <c>TimeStampResp</c> (RFC 3161 §2.4.2) and returns its token, after checking that the authority granted the request
    /// and that the token answers it: a CMS <c>SignedData</c> with one signer, encapsulating a TSTInfo with the request's message
    /// imprint, nonce and (when one was asked for) policy, with the authority's certificate when it was asked for.
    /// </summary>
    /// <param name="response">The response's bytes.</param>
    /// <param name="digestOid">The request's hash algorithm.</param>
    /// <param name="imprint">The request's hash.</param>
    /// <param name="nonce">The request's nonce.</param>
    /// <param name="policy">The request's policy, or <see langword="null"/>.</param>
    /// <param name="requestedCertificates">Whether the request asked for the authority's certificate.</param>
    /// <returns>The DER-encoded <c>TimeStampToken</c>, a CMS <c>ContentInfo</c>.</returns>
    /// <exception cref="PdfTimestampException">The authority refused, or the response is malformed or does not answer the request.</exception>
    public static byte[] ReadResponse(
        ReadOnlySpan<byte> response,
        string digestOid,
        ReadOnlySpan<byte> imprint,
        ReadOnlySpan<byte> nonce,
        string? policy,
        bool requestedCertificates)
    {
        byte[] token;
        TokenContents contents;
        try
        {
            var reader = new AsnReader(response.ToArray(), AsnEncodingRules.BER);
            AsnReader message = reader.ReadSequence();
            ReadStatus(message.ReadSequence());
            if (!message.HasData)
            {
                throw new PdfTimestampException("The time-stamp authority granted the request but returned no time-stamp token (RFC 3161 §2.4.2).");
            }

            token = message.ReadEncodedValue().ToArray();
            contents = ReadToken(token);
        }
        catch (AsnContentException exception)
        {
            throw Malformed(exception);
        }
        catch (CryptographicException exception)
        {
            throw Malformed(exception);
        }

        if (contents.ImprintAlgorithm != digestOid)
        {
            throw new PdfTimestampException(
                $"The time-stamp token's message imprint uses the hash algorithm {contents.ImprintAlgorithm}, not the requested {digestOid} (RFC 3161 §2.4.2).");
        }

        if (!contents.Imprint.AsSpan().SequenceEqual(imprint))
        {
            throw new PdfTimestampException("The time-stamp token's message imprint is not the hash that was sent: the token time-stamps other data (RFC 3161 §2.4.2).");
        }

        if (contents.Nonce is not { } answered)
        {
            throw new PdfTimestampException("The time-stamp token has no nonce, but the request had one: it cannot be told apart from a replayed response (RFC 3161 §2.4.2).");
        }

        if (answered != new BigInteger(nonce, isUnsigned: true, isBigEndian: true))
        {
            throw new PdfTimestampException("The time-stamp token's nonce is not the request's: the response answers another request (RFC 3161 §2.4.2).");
        }

        if (policy is not null && contents.Policy != policy)
        {
            throw new PdfTimestampException($"The time-stamp token was issued under the policy {contents.Policy}, not the requested {policy} (RFC 3161 §2.4.2).");
        }

        if (requestedCertificates && !contents.HasCertificates)
        {
            throw new PdfTimestampException("The time-stamp token does not hold the authority's certificate, which the request asked for (RFC 3161 §2.4.1, certReq).");
        }

        return token;
    }

    /// <summary>Throws unless <c>PKIStatusInfo</c> grants the request (granted or grantedWithMods).</summary>
    private static void ReadStatus(AsnReader info)
    {
        if (!info.TryReadInt32(out int status))
        {
            throw new PdfTimestampException("The time-stamp authority answered with a status that is not a PKIStatus (RFC 3161 §2.4.2).");
        }

        if (status is 0 or 1)
        {
            return;
        }

        string? text = null;
        int? failure = null;
        while (info.HasData)
        {
            Asn1Tag tag = info.PeekTag();
            if (tag.HasSameClassAndValue(Asn1Tag.Sequence))
            {
                AsnReader strings = info.ReadSequence();
                var joined = new StringBuilder();
                while (strings.HasData)
                {
                    joined.Append(joined.Length > 0 ? "; " : string.Empty).Append(strings.ReadCharacterString(UniversalTagNumber.UTF8String));
                }

                text = joined.ToString();
            }
            else if (tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.BitString)))
            {
                byte[] bits = info.ReadBitString(out _);
                int value = 0;
                for (int bit = 0; bit < Math.Min(bits.Length * 8, 31); bit++)
                {
                    if ((bits[bit / 8] & (0x80 >> (bit % 8))) != 0)
                    {
                        value |= 1 << bit;
                    }
                }

                failure = value;
            }
            else
            {
                info.ReadEncodedValue();
            }
        }

        var message = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"The time-stamp authority refused the request: status {StatusName(status)} ({status})"));
        if (text is not null)
        {
            message.Append(CultureInfo.InvariantCulture, $", \"{text}\"");
        }

        if (failure is { } bitsSet && FailureNames(bitsSet) is { Length: > 0 } names)
        {
            message.Append(CultureInfo.InvariantCulture, $", failure {names}");
        }

        throw new PdfTimestampException(message.Append(" (RFC 3161 §2.4.2).").ToString(), status, failure);
    }

    /// <summary>Reads what the checks need from a <c>TimeStampToken</c>: <c>ContentInfo</c> of <c>SignedData</c> of a TSTInfo.</summary>
    private static TokenContents ReadToken(byte[] token)
    {
        var reader = new AsnReader(token, AsnEncodingRules.BER);
        AsnReader contentInfo = reader.ReadSequence();
        if (contentInfo.ReadObjectIdentifier() != SignedDataOid)
        {
            throw new PdfTimestampException("The time-stamp token is not a CMS SignedData (RFC 3161 §2.4.2).");
        }

        AsnReader signedData = contentInfo.ReadSequence(ContextZero).ReadSequence();
        signedData.ReadEncodedValue(); // version
        signedData.ReadSetOf(); // digestAlgorithms
        AsnReader encapsulated = signedData.ReadSequence();
        string contentType = encapsulated.ReadObjectIdentifier();
        if (contentType != TstInfoOid)
        {
            throw new PdfTimestampException($"The time-stamp token encapsulates {contentType}, not a TSTInfo (RFC 3161 §2.4.2, id-ct-TSTInfo).");
        }

        if (!encapsulated.HasData)
        {
            throw new PdfTimestampException("The time-stamp token encapsulates no TSTInfo (RFC 3161 §2.4.2).");
        }

        byte[] tstInfo = encapsulated.ReadSequence(ContextZero).ReadOctetString();
        bool hasCertificates = false;
        while (signedData.HasData && !signedData.PeekTag().HasSameClassAndValue(Asn1Tag.SetOf))
        {
            Asn1Tag tag = signedData.PeekTag();
            hasCertificates |= tag.HasSameClassAndValue(ContextZero);
            signedData.ReadEncodedValue();
        }

        AsnReader signerInfos = signedData.ReadSetOf();
        signerInfos.ReadEncodedValue();
        if (signerInfos.HasData)
        {
            throw new PdfTimestampException("The time-stamp token has more than one signer; RFC 3161 §2.4.2 allows only the authority's.");
        }

        return ReadTstInfo(tstInfo, hasCertificates);
    }

    /// <summary>Reads a TSTInfo (RFC 3161 §2.4.2): its policy, message imprint and nonce.</summary>
    private static TokenContents ReadTstInfo(byte[] encoded, bool hasCertificates)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        AsnReader info = reader.ReadSequence();
        info.ReadEncodedValue(); // version
        string policy = info.ReadObjectIdentifier();
        AsnReader messageImprint = info.ReadSequence();
        string algorithm = messageImprint.ReadSequence().ReadObjectIdentifier();
        byte[] imprint = messageImprint.ReadOctetString();
        info.ReadEncodedValue(); // serialNumber
        info.ReadGeneralizedTime();
        BigInteger? nonce = null;
        while (info.HasData)
        {
            Asn1Tag tag = info.PeekTag();
            if (tag.HasSameClassAndValue(Asn1Tag.Integer))
            {
                nonce = info.ReadInteger();
            }
            else if (tag.HasSameClassAndValue(ImplicitOne) || tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                break; // extensions, the last field
            }
            else
            {
                info.ReadEncodedValue(); // accuracy, ordering, tsa
            }
        }

        return new TokenContents(policy, algorithm, imprint, nonce, hasCertificates);
    }

    private static PdfTimestampException Malformed(Exception exception) =>
        new($"The time-stamp authority's response is not a valid TimeStampResp (RFC 3161 §2.4.2): {exception.Message}", exception);

    private static string StatusName(int status) => status switch
    {
        2 => "rejection",
        3 => "waiting",
        4 => "revocationWarning",
        5 => "revocationNotification",
        _ => "unknown",
    };

    private static string FailureNames(int bits)
    {
        (int Bit, string Name)[] known =
        [
            (0, "badAlg"), (2, "badRequest"), (5, "badDataFormat"), (14, "timeNotAvailable"), (15, "unacceptedPolicy"),
            (16, "unacceptedExtension"), (17, "addInfoNotAvailable"), (25, "systemFailure"),
        ];
        return string.Join(", ", known.Where(entry => (bits & (1 << entry.Bit)) != 0).Select(entry => entry.Name));
    }

    /// <summary>What <see cref="ReadResponse"/> checks in a token.</summary>
    private sealed record TokenContents(string Policy, string ImprintAlgorithm, byte[] Imprint, BigInteger? Nonce, bool HasCertificates);
}
