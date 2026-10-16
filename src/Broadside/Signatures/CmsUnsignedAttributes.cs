using System.Formats.Asn1;

namespace Broadside.Signatures;

/// <summary>
/// Reads the signature value of, and adds an unsigned attribute to, the first signer of an encoded CMS <c>SignedData</c>: a signature
/// time-stamp token on a document signature, or a PDF MAC token on a document time-stamp token.
/// </summary>
/// <remarks>
/// RFC 5652 §5.3: unsigned attributes (<c>unsignedAttrs [1] IMPLICIT SET OF Attribute</c>) are not covered by the signature, so a
/// signature or a time-stamp token stays valid when one is added. Every other element is copied as it was encoded, the encapsulated
/// content and the signed attributes included; the structure around them is written again in DER.
/// </remarks>
internal static class CmsUnsignedAttributes
{
    /// <summary><c>id-attr-pdfMacData</c> (ISO/TS 32004 §6.5.2).</summary>
    public const string PdfMacDataOid = "1.0.32004.1.2";

    private static readonly Asn1Tag ContextZero = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag ContextOne = new(TagClass.ContextSpecific, 1, isConstructed: true);

    /// <summary>The <c>signature</c> octets of the first <c>SignerInfo</c> (RFC 5652 §5.3), what a signature time-stamp's imprint hashes.</summary>
    /// <param name="contentInfo">The DER- or BER-encoded <c>ContentInfo</c> of a <c>SignedData</c>.</param>
    /// <returns>The signature value.</returns>
    public static byte[] SignatureValue(byte[] contentInfo)
    {
        AsnReader signer = FirstSigner(contentInfo);
        while (signer.HasData)
        {
            Asn1Tag tag = signer.PeekTag();
            if (tag.HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.OctetString)))
            {
                return signer.ReadOctetString();
            }

            signer.ReadEncodedValue();
        }

        throw new AsnContentException("The SignerInfo has no signature value.");
    }

    /// <summary>Returns <paramref name="contentInfo"/> with an attribute of one value added to its first signer's unsigned attributes.</summary>
    /// <param name="contentInfo">The encoded <c>ContentInfo</c> of a <c>SignedData</c>.</param>
    /// <param name="oid">The attribute type.</param>
    /// <param name="value">The attribute's single value, encoded.</param>
    /// <returns>The DER-encoded <c>ContentInfo</c>.</returns>
    public static byte[] Add(byte[] contentInfo, string oid, byte[] value)
    {
        var reader = new AsnReader(contentInfo, AsnEncodingRules.BER);
        AsnReader outer = reader.ReadSequence();
        string contentType = outer.ReadObjectIdentifier();
        AsnReader signedData = outer.ReadSequence(ContextZero).ReadSequence();

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(contentType);
            using (writer.PushSequence(ContextZero))
            using (writer.PushSequence())
            {
                while (signedData.HasData)
                {
                    if (!signedData.PeekTag().HasSameClassAndValue(Asn1Tag.SetOf) || IsDigestAlgorithms(signedData))
                    {
                        writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                        continue;
                    }

                    AsnReader signers = signedData.ReadSetOf();
                    using (writer.PushSetOf())
                    {
                        WriteSigner(writer, signers.ReadSequence(), oid, value);
                        while (signers.HasData)
                        {
                            writer.WriteEncodedValue(signers.ReadEncodedValue().Span);
                        }
                    }
                }
            }
        }

        return writer.Encode();

        // SignedData has two SETs at its top level: digestAlgorithms (second, after version) and signerInfos (last).
        static bool IsDigestAlgorithms(AsnReader signedData)
        {
            AsnReader probe = signedData.Clone();
            probe.ReadEncodedValue();
            return probe.HasData;
        }
    }

    private static void WriteSigner(AsnWriter writer, AsnReader signer, string oid, byte[] value)
    {
        using (writer.PushSequence())
        {
            bool written = false;
            while (signer.HasData)
            {
                if (signer.PeekTag().HasSameClassAndValue(ContextOne))
                {
                    AsnReader existing = signer.ReadSetOf(ContextOne);
                    WriteUnsigned(writer, existing, oid, value);
                    written = true;
                    continue;
                }

                writer.WriteEncodedValue(signer.ReadEncodedValue().Span);
            }

            if (!written)
            {
                WriteUnsigned(writer, null, oid, value);
            }
        }
    }

    private static void WriteUnsigned(AsnWriter writer, AsnReader? existing, string oid, byte[] value)
    {
        using (writer.PushSetOf(ContextOne))
        {
            while (existing is { HasData: true })
            {
                writer.WriteEncodedValue(existing.ReadEncodedValue().Span);
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

    private static AsnReader FirstSigner(byte[] contentInfo)
    {
        var reader = new AsnReader(contentInfo, AsnEncodingRules.BER);
        AsnReader outer = reader.ReadSequence();
        outer.ReadObjectIdentifier();
        AsnReader signedData = outer.ReadSequence(ContextZero).ReadSequence();
        AsnReader? last = null;
        while (signedData.HasData)
        {
            if (signedData.PeekTag().HasSameClassAndValue(Asn1Tag.SetOf))
            {
                last = signedData.ReadSetOf(skipSortOrderValidation: true);
            }
            else
            {
                signedData.ReadEncodedValue();
            }
        }

        return (last ?? throw new AsnContentException("The SignedData has no signer infos.")).ReadSequence();
    }
}
