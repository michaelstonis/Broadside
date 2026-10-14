using System.Formats.Asn1;
using System.Security.Cryptography;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>Composes the PDF MAC token of an encrypted file a save writes (ISO/TS 32004): the counterpart of <see cref="IntegrityVerifier"/>.</summary>
/// <remarks>
/// <para>
/// ISO/TS 32004 §6.2-§6.6. The token is a CMS ContentInfo of type id-ct-authData (RFC 5652 §9.1) whose AuthenticatedData has one
/// PasswordRecipientInfo (§6.3.3: key derivation pdfMacWrapKdf, key encryption AES-256 key wrap without padding, RFC 3394), the MAC
/// algorithm HMAC with SHA-256 (§6.3.5, Table 9), the digest algorithm SHA-256 (§6.3.4, Table 8), the encapsulated content a
/// PdfMacIntegrityInfo of version 0 holding the digest of the file's byte range (§6.2, §6.6.2; no signatureDigest: the token stands
/// alone in the trailer, §6.5.1), and the authenticated attributes content type, message digest and CMS algorithm protection
/// (§6.3.6, RFC 6211), with no unauthenticated attributes (§6.3.7).
/// </para>
/// <para>
/// The MAC key is 32 random bytes per token (§6.3.3: generated with a strong random number generator), wrapped with the key
/// encryption key HKDF-SHA-256 derives from the file encryption key, the document's <c>KDFSalt</c> and the info string
/// <c>PDFMAC</c> (§6.4). Every field has a fixed length, so <see cref="TokenLength"/> is known before the file is digested and the
/// space for the token can be reserved (Table 6, <c>MAC</c>: "Space for the MAC string shall be allocated before it is computed").
/// </para>
/// </remarks>
internal sealed class IntegrityCodeWriter
{
    private const string AuthenticatedDataOid = "1.2.840.113549.1.9.16.1.2";
    private const string IntegrityInfoOid = "1.0.32004.1.0";
    private const string WrapKdfOid = "1.0.32004.1.1";
    private const string Aes256WrapOid = "2.16.840.1.101.3.4.1.45";
    private const string HmacSha256Oid = "1.2.840.113549.2.9";
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string ContentTypeAttributeOid = "1.2.840.113549.1.9.3";
    private const string MessageDigestAttributeOid = "1.2.840.113549.1.9.4";
    private const string AlgorithmProtectionAttributeOid = "1.2.840.113549.1.9.52";
    private const int MacKeyLength = 32;

    private readonly byte[] _keyEncryptionKey;

    /// <summary>Initializes a new instance of the <see cref="IntegrityCodeWriter"/> class.</summary>
    /// <param name="fileKey">
    /// The file encryption key (§6.4: the key the security handler encrypts the document with; for a public-key handler with
    /// <c>adbe.pkcs7.s5</c>, the <c>DefaultCryptFilter</c>'s), the input key material the reader derives the same key from.
    /// </param>
    /// <param name="kdfSalt">The encryption dictionary's 32-byte <c>KDFSalt</c> (§5.1.1, Table 2).</param>
    public IntegrityCodeWriter(ReadOnlySpan<byte> fileKey, ReadOnlySpan<byte> kdfSalt)
    {
        _keyEncryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey.ToArray(), 32, kdfSalt.ToArray(), "PDFMAC"u8.ToArray());
        TokenLength = Compose(new byte[DigestLength], new byte[MacKeyLength]).Length;
    }

    /// <summary>Gets the length of the DER-encoded token in bytes, the same for every token this writer composes.</summary>
    public int TokenLength { get; }

    /// <summary>Gets the length of the file digest <see cref="CreateToken"/> takes.</summary>
    public static int DigestLength => 32;

    /// <summary>Creates the digest the file's byte range is hashed with (§6.3.4: the token's digest algorithm, SHA-256).</summary>
    /// <returns>A new digest.</returns>
    public static IncrementalDigest CreateDigest() => IncrementalDigest.Create(HashAlgorithmName.SHA256);

    /// <summary>Composes a token for a file whose byte range digests to <paramref name="dataDigest"/>, under a new random MAC key.</summary>
    /// <param name="dataDigest">The SHA-256 digest of the bytes the <c>AuthCode</c> byte range covers (§6.6.2).</param>
    /// <returns>The DER-encoded token, <see cref="TokenLength"/> bytes.</returns>
    public byte[] CreateToken(ReadOnlySpan<byte> dataDigest)
    {
        byte[] macKey = RandomNumberGenerator.GetBytes(MacKeyLength);
        try
        {
            return Compose(dataDigest, macKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    private byte[] Compose(ReadOnlySpan<byte> dataDigest, byte[] macKey)
    {
        // §6.2: PdfMacIntegrityInfo ::= SEQUENCE { version INTEGER (0), dataDigest OCTET STRING }.
        var info = new AsnWriter(AsnEncodingRules.DER);
        using (info.PushSequence())
        {
            info.WriteInteger(0);
            info.WriteOctetString(dataDigest);
        }

        byte[] content = info.Encode();

        // §6.3.6: the authenticated attributes, as the SET OF the MAC covers (RFC 5652 §9.2); DER sorts them.
        var attributes = new AsnWriter(AsnEncodingRules.DER);
        using (attributes.PushSetOf())
        {
            using (attributes.PushSequence())
            {
                attributes.WriteObjectIdentifier(ContentTypeAttributeOid);
                using (attributes.PushSetOf())
                {
                    attributes.WriteObjectIdentifier(IntegrityInfoOid);
                }
            }

            using (attributes.PushSequence())
            {
                attributes.WriteObjectIdentifier(MessageDigestAttributeOid);
                using (attributes.PushSetOf())
                {
                    attributes.WriteOctetString(IncrementalDigest.HashData(HashAlgorithmName.SHA256, content));
                }
            }

            using (attributes.PushSequence())
            {
                // RFC 6211: CMSAlgorithmProtection ::= SEQUENCE { digestAlgorithm, macAlgorithm [2] IMPLICIT AlgorithmIdentifier }.
                attributes.WriteObjectIdentifier(AlgorithmProtectionAttributeOid);
                using (attributes.PushSetOf())
                using (attributes.PushSequence())
                {
                    WriteAlgorithm(attributes, Sha256Oid);
                    WriteAlgorithm(attributes, HmacSha256Oid, new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true));
                }
            }
        }

        byte[] attributeSet = attributes.Encode();
        byte[] mac = HMACSHA256.HashData(macKey, attributeSet);
        byte[] authenticatedAttributes = (byte[])attributeSet.Clone();
        authenticatedAttributes[0] = 0xA2; // authAttrs [2] IMPLICIT, constructed.

        var token = new AsnWriter(AsnEncodingRules.DER);
        using (token.PushSequence())
        {
            token.WriteObjectIdentifier(AuthenticatedDataOid);
            using (token.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (token.PushSequence())
            {
                token.WriteInteger(0);
                using (token.PushSetOf())
                using (token.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true)))
                {
                    // RFC 5652 §6.2.4 PasswordRecipientInfo: version 0, keyDerivationAlgorithm [0], keyEncryptionAlgorithm, encryptedKey.
                    token.WriteInteger(0);
                    WriteAlgorithm(token, WrapKdfOid, new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                    WriteAlgorithm(token, Aes256WrapOid);
                    token.WriteOctetString(AesKeyWrap.Wrap(_keyEncryptionKey, macKey));
                }

                WriteAlgorithm(token, HmacSha256Oid);
                WriteAlgorithm(token, Sha256Oid, new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
                using (token.PushSequence())
                {
                    token.WriteObjectIdentifier(IntegrityInfoOid);
                    using (token.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    {
                        token.WriteOctetString(content);
                    }
                }

                token.WriteEncodedValue(authenticatedAttributes);
                token.WriteOctetString(mac);
            }
        }

        return token.Encode();
    }

    /// <summary>An AlgorithmIdentifier with no parameters (§6.4: pdfMacWrapKdf has none; RFC 3394 and RFC 5754 omit them).</summary>
    private static void WriteAlgorithm(AsnWriter writer, string oid, Asn1Tag? tag = null)
    {
        using (writer.PushSequence(tag))
        {
            writer.WriteObjectIdentifier(oid);
        }
    }
}
