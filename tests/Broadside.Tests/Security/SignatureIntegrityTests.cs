using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Security.Cryptography;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// A PDF MAC token attached to a signature as an unsigned attribute (ISO/TS 32004 §6.5.2, §6.6.3, B.2.3), built in the test: an
/// incremental update of <c>encrypted-aes-256.pdf</c> adds a <c>KDFSalt</c>, a signature dictionary signed with a throwaway
/// certificate, and the trailer's <c>AuthCode</c> pointing at it.
/// </summary>
public class SignatureIntegrityTests
{
    private const string IntegrityInfoOid = "1.0.32004.1.0";

    [Fact]
    public void A_mac_attached_to_a_signature_is_verified()
    {
        using PdfDocument document = PdfDocument.Open(SignedFile(tamperSignature: false));

        Assert.Equal(PdfIntegrityStatus.Verified, document.Security!.Integrity);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_mac_attached_to_a_different_signature_value_does_not_match()
    {
        using PdfDocument document = PdfDocument.Open(SignedFile(tamperSignature: true));

        Assert.Equal(PdfIntegrityStatus.Failed, document.Security!.Integrity);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("IntegrityCodeMismatch", diagnostic.Code);
        Assert.Contains("signatureDigest", diagnostic.Message, StringComparison.Ordinal);
    }

    private static byte[] SignedFile(bool tamperSignature)
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        string text = Encoding.Latin1.GetString(original);
        int start = text.IndexOf("6 0 obj\n", StringComparison.Ordinal) + "6 0 obj\n".Length;
        string encryption = text[start..text.IndexOf("\nendobj", start, StringComparison.Ordinal)];
        int idStart = text.IndexOf("/ID [", StringComparison.Ordinal);
        string id = text[idStart..(text.IndexOf(']', idStart) + 1)];
        byte[] salt = [.. Enumerable.Repeat((byte)0x11, 32)];
        string placeholder = "<" + new string('0', 2 * 4096) + ">";
        const string rangePlaceholder = "[0 0000000000 0000000000 0000000000]";

        byte[] file = TestPdf.AppendUpdate(
            original,
            $"/Size 10 /Root 1 0 R /Encrypt 6 0 R {id} /AuthCode << /MACLocation /AttachedToSig /SigObjRef 9 0 R >>",
            (1, 0, "<< /Type /Catalog /Pages 2 0 R /Extensions << /ISO_ [<< /Type /DeveloperExtensions /BaseVersion /2.0 /ExtensionLevel 32004 >>] >> >>"),
            (6, 0, encryption[..^2] + $" /KDFSalt <{Convert.ToHexString(salt)}> >>"),
            (9, 0, $"<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange {rangePlaceholder} /Contents {placeholder} >>"));

        string updated = Encoding.Latin1.GetString(file);
        int l1 = updated.IndexOf(placeholder, StringComparison.Ordinal);
        int s = l1 + placeholder.Length;
        string range = string.Create(CultureInfo.InvariantCulture, $"[0 {l1:D10} {s:D10} {file.Length - s:D10}]");
        updated = updated.Replace(rangePlaceholder, range, StringComparison.Ordinal);
        file = Encoding.Latin1.GetBytes(updated);
        byte[] covered = [.. file.AsSpan(0, l1), .. file.AsSpan(s)];

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Broadside test signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = new SignedCms(new ContentInfo(covered), detached: true);
        signed.ComputeSignature(new CmsSigner(certificate));
        byte[] signatureValue = signed.SignerInfos[0].GetSignature();
        if (tamperSignature)
        {
            signatureValue[0] ^= 0x01;
        }

        byte[] fileKey = SHA256.HashData("broadside-corpus:encrypted-aes-256:file-key:0"u8);
        byte[] token = Token(fileKey, salt, SHA256.HashData(covered), SHA256.HashData(signatureValue));
        signed.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData("1.0.32004.1.2", token));
        string contents = Convert.ToHexString(signed.Encode()).PadRight(2 * 4096, '0');
        Encoding.Latin1.GetBytes(contents).CopyTo(file, l1 + 1);
        return file;
    }

    /// <summary>ISO/TS 32004 §6.3: the AuthenticatedData token, written with the base class library's ASN.1 writer.</summary>
    private static byte[] Token(byte[] fileKey, byte[] salt, byte[] dataDigest, byte[] signatureDigest)
    {
        byte[] macKey = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];
        var infoWriter = new AsnWriter(AsnEncodingRules.DER);
        using (infoWriter.PushSequence())
        {
            infoWriter.WriteInteger(0);
            infoWriter.WriteOctetString(dataDigest);
            infoWriter.WriteOctetString(signatureDigest, new Asn1Tag(TagClass.ContextSpecific, 0));
        }

        byte[] info = infoWriter.Encode();
        byte[] contentType = Attribute("1.2.840.113549.1.9.3", writer => writer.WriteObjectIdentifier(IntegrityInfoOid));
        byte[] messageDigest = Attribute("1.2.840.113549.1.9.4", writer => writer.WriteOctetString(SHA256.HashData(info)));

        var setWriter = new AsnWriter(AsnEncodingRules.DER);
        using (setWriter.PushSetOf())
        {
            setWriter.WriteEncodedValue(contentType);
            setWriter.WriteEncodedValue(messageDigest);
        }

        byte[] attributesAsSet = setWriter.Encode();
        byte[] mac = HMACSHA256.HashData(macKey, attributesAsSet);
        byte[] keyEncryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey, 32, salt, "PDFMAC"u8.ToArray());

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier("1.2.840.113549.1.9.16.1.2");
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (writer.PushSequence())
            {
                writer.WriteInteger(0);
                using (writer.PushSetOf())
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true)))
                {
                    writer.WriteInteger(0);
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    {
                        writer.WriteObjectIdentifier("1.0.32004.1.1");
                    }

                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("2.16.840.1.101.3.4.1.45");
                    }

                    writer.WriteOctetString(AesKeyWrap.Wrap(keyEncryptionKey, macKey));
                }

                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier("1.2.840.113549.2.9");
                }

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
                {
                    writer.WriteObjectIdentifier("2.16.840.1.101.3.4.2.1");
                }

                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(IntegrityInfoOid);
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    {
                        writer.WriteOctetString(info);
                    }
                }

                byte[] implicitAttributes = [.. attributesAsSet];
                implicitAttributes[0] = 0xA2;
                writer.WriteEncodedValue(implicitAttributes);
                writer.WriteOctetString(mac);
            }
        }

        return writer.Encode();
    }

    private static byte[] Attribute(string type, Action<AsnWriter> value)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(type);
            using (writer.PushSetOf())
            {
                value(writer);
            }
        }

        return writer.Encode();
    }
}
