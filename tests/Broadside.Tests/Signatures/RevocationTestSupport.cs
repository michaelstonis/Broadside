using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Broadside.Signatures;
using Broadside.TestSupport;

namespace Broadside.Tests.Signatures;

/// <summary>
/// Signed files whose CMS object a test builds with the platform PKCS package (<see cref="SignedCms"/>), with revocation information
/// and time-stamp tokens in it, over a Broadside signature whose <c>Contents</c> leaves room for them.
/// </summary>
internal static class RevocationTestSupport
{
    /// <summary>adbe-revocationInfoArchival (ISO 32000-2 §12.8.3.3.2).</summary>
    public const string RevocationInfoArchivalOid = "1.2.840.113583.1.1.8";

    /// <summary>id-aa-signatureTimeStampToken (RFC 3161 Appendix A).</summary>
    public const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";

    private static readonly Lazy<byte[]> RoomyTemplate = new(CreateTemplate);

    /// <summary>
    /// A file signed so its <c>Contents</c> holds about 9 KB, whose CMS object is replaced by what <paramref name="sign"/> makes from a
    /// detached <see cref="SignedCms"/> over the covered bytes: it calls <see cref="SignedCms.ComputeSignature(CmsSigner, bool)"/> and may
    /// add unsigned attributes before the object is encoded; <paramref name="edit"/> may then change the encoding.
    /// </summary>
    public static byte[] Signed(X509Certificate2 signer, IEnumerable<X509Certificate2> chain, Action<CmsSigner>? configure = null, Action<SignedCms>? after = null, Func<byte[], byte[]>? edit = null)
    {
        return SignedFileEditor.ReplaceSignature(RoomyTemplate.Value, covered =>
        {
            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer) { IncludeOption = X509IncludeOption.EndCertOnly };
            foreach (X509Certificate2 certificate in chain)
            {
                cmsSigner.Certificates.Add(certificate);
            }

            configure?.Invoke(cmsSigner);
            cms.ComputeSignature(cmsSigner, silent: true);
            after?.Invoke(cms);
            byte[] encoded = cms.Encode();
            return edit is null ? encoded : edit(encoded);
        });
    }

    /// <summary>The adbe-revocationInfoArchival signed attribute holding <paramref name="ocsps"/> and <paramref name="crls"/>.</summary>
    public static AsnEncodedData RevocationInfoArchival(IEnumerable<byte[]> ocsps, IEnumerable<byte[]> crls)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            byte[][] lists = [.. crls];
            if (lists.Length > 0)
            {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                using (writer.PushSequence())
                {
                    foreach (byte[] crl in lists)
                    {
                        writer.WriteEncodedValue(crl);
                    }
                }
            }

            byte[][] responses = [.. ocsps];
            if (responses.Length > 0)
            {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
                using (writer.PushSequence())
                {
                    foreach (byte[] response in responses)
                    {
                        writer.WriteEncodedValue(response);
                    }
                }
            }
        }

        return new AsnEncodedData(RevocationInfoArchivalOid, writer.Encode());
    }

    /// <summary>
    /// Adds a <c>crls</c> field (RFC 5652 §10.2.1) to a CMS <c>SignedData</c> after its certificates: <paramref name="crl"/> as a
    /// CertificateList and <paramref name="ocsp"/> as other revocation information (RFC 5940 §4). The field is not signed.
    /// </summary>
    public static byte[] WithRevocationChoices(byte[] cms, byte[]? crl, byte[]? ocsp)
    {
        AsnReader contentInfo = new AsnReader(cms, AsnEncodingRules.BER).ReadSequence();
        string type = contentInfo.ReadObjectIdentifier();
        AsnReader signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(type);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (writer.PushSequence())
            {
                writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                if (signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                }

                using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    if (crl is not null)
                    {
                        writer.WriteEncodedValue(crl);
                    }

                    if (ocsp is not null)
                    {
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
                        {
                            writer.WriteObjectIdentifier("1.3.6.1.5.5.7.16.2");
                            writer.WriteEncodedValue(ocsp);
                        }
                    }
                }

                while (signedData.HasData)
                {
                    writer.WriteEncodedValue(signedData.ReadEncodedValue().Span);
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>An ESS signing-certificate-v2 signed attribute naming <paramref name="certificate"/> (RFC 5035 §3).</summary>
    public static AsnEncodedData SigningCertificateV2(X509Certificate2 certificate) =>
        new("1.2.840.113549.1.9.16.2.47", TestTimestampAuthority.SigningCertificateV2(certificate));

    /// <summary>Adds a signature time-stamp token from <paramref name="authority"/> over the signer's signature value.</summary>
    public static void AddTimestamp(SignedCms cms, TestTimestampAuthority authority, Func<byte[], byte[]>? editToken = null)
    {
        SignerInfo signer = cms.SignerInfos[0];
        byte[] token = authority.Stamp(signer.GetSignature());
        signer.AddUnsignedAttribute(new AsnEncodedData(SignatureTimeStampOid, editToken is null ? token : editToken(token)));
    }

    /// <summary>Validates the single signature of <paramref name="file"/>.</summary>
    public static PdfSignatureValidation ValidateSingle(byte[] file, PdfSignatureValidationOptions options)
    {
        using PdfDocument document = PdfDocument.Open(file);
        return Assert.Single(document.ValidateSignatures(options));
    }

    private static byte[] CreateTemplate()
    {
        // A 4096-bit signer with a chain of four more makes Broadside reserve room for a CMS object about that size.
        X509Certificate2 placeholder = TestCertificates.SelfSigned("CN=Broadside placeholder signer with a long name to reserve room", keySize: 4096);
        X509Certificate2[] chain = [.. Enumerable.Range(0, 4).Select(index => TestCertificates.SelfSigned($"CN=Broadside placeholder chain certificate {index}", keySize: 4096))];
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"));
        using var output = new MemoryStream();
        document.Sign(output, new PdfCertificateSigner(placeholder, chain));
        return output.ToArray();
    }
}
