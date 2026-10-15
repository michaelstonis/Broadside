using System.Formats.Asn1;
using Broadside.Signatures;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>timestamp-response</c> target (issue #195): the input is an RFC 3161 <c>TimeStampResp</c> read by
/// <see cref="TimestampProtocol.ReadResponse"/> against a fixed request (SHA-256 imprint of 32 bytes 0x5A, nonce 0x4142434445464748,
/// no policy, certificates requested). The only documented outcome besides a token is <see cref="PdfTimestampException"/>; anything
/// else escaping is a finding. Invariant: a returned token is the encoding of one value inside the input, so no longer than it.
/// </summary>
/// <remarks>
/// RFC 3161 §2.4.2; ISO 32000-2 §12.8.3.3.1 and §12.8.5. The smoke runner feeds whole corpus files, which are no responses: an input
/// starting with <c>%PDF-</c> is turned into <see cref="ValidResponse"/> with up to 16 bytes changed at positions the file's bytes
/// choose, so its mutations corrupt a response that answers the request. <c>--seeds</c> writes <see cref="ValidResponse"/> itself.
/// </remarks>
internal static class TimestampResponseTarget
{
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    private static readonly byte[] Nonce = [0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48];

    private static readonly byte[] Imprint = [.. Enumerable.Repeat((byte)0x5A, 32)];

    private static readonly Lazy<byte[]> Valid = new(ValidResponse);

    public static void Target(ReadOnlySpan<byte> data)
    {
        byte[] response = data.StartsWith("%PDF-"u8) ? Perturb(data) : data.ToArray();
        byte[] token;
        try
        {
            token = TimestampProtocol.ReadResponse(response, Sha256Oid, Imprint, Nonce, policy: null, requestedCertificates: true);
        }
        catch (PdfTimestampException) when (!response.AsSpan().SequenceEqual(Valid.Value))
        {
            return;
        }

        if (token.Length == 0 || token.Length > response.Length)
        {
            throw new InvalidOperationException($"A {token.Length}-byte token from a {response.Length}-byte response.");
        }
    }

    /// <summary>
    /// A granted response that answers the target's request: a token whose SignedData has the authority's certificates (an empty
    /// set), one signer (an empty SignerInfo: the client does not verify the signature) and a TSTInfo with the request's imprint and
    /// nonce, an accuracy, ordering false and a TSA name.
    /// </summary>
    public static byte[] ValidResponse()
    {
        var info = new AsnWriter(AsnEncodingRules.DER);
        using (info.PushSequence())
        {
            info.WriteInteger(1);
            info.WriteObjectIdentifier("1.3.6.1.4.1.32473.1.1");
            using (info.PushSequence())
            {
                using (info.PushSequence())
                {
                    info.WriteObjectIdentifier(Sha256Oid);
                }

                info.WriteOctetString(Imprint);
            }

            info.WriteInteger(7);
            info.WriteGeneralizedTime(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
            using (info.PushSequence())
            {
                info.WriteInteger(1);
            }

            info.WriteBoolean(false);
            info.WriteIntegerUnsigned(Nonce);
            using (info.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            {
                info.WriteCharacterString(UniversalTagNumber.IA5String, "tsa", new Asn1Tag(TagClass.ContextSpecific, 1));
            }
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(0);
            }

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                using (writer.PushSequence())
                {
                    writer.WriteInteger(3);
                    using (writer.PushSetOf())
                    {
                    }

                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier(TimestampProtocol.TstInfoOid);
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                        {
                            writer.WriteOctetString(info.Encode());
                        }
                    }

                    using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    {
                    }

                    using (writer.PushSetOf())
                    using (writer.PushSequence())
                    {
                        writer.WriteInteger(1);
                    }
                }
            }
        }

        return writer.Encode();
    }

    private static byte[] Perturb(ReadOnlySpan<byte> file)
    {
        byte[] response = ValidResponse();
        ReadOnlySpan<byte> choices = file.Length > 8 ? file[8..] : [];
        for (int index = 0; index + 1 < choices.Length && index < 32; index += 2)
        {
            response[choices[index] % response.Length] ^= choices[index + 1];
        }

        return response;
    }
}
