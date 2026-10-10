using System.Buffers;
using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Text;
using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>Verifies the integrity MAC of an encrypted document (ISO/TS 32004).</summary>
/// <remarks>
/// <para>
/// ISO/TS 32004 §5.2.3 (the trailer's <c>AuthCode</c> dictionary), §6.3-6.6 (the PDF MAC token: a CMS AuthenticatedData with one
/// password recipient, the key derived with pdfMacWrapKdf, wrapped with AES-256 key wrap, HMAC-SHA-256 over the authenticated
/// attributes) and Annex B (validation). Only the newest revision's <c>AuthCode</c> counts (B.1). A standalone token is checked
/// against the byte range it states, which must cover the whole file except the token itself (B.2.2); a token attached to a
/// signature is checked against the signature's byte range and signature value (B.2.3).
/// </para>
/// <para>
/// Outcomes are diagnostics: a token that is malformed is <c>IntegrityCodeInvalid</c>, one that does not match the file is
/// <c>IntegrityCodeMismatch</c>, one whose byte range stops short of the end of the file is <c>IntegrityCodeIncomplete</c>, a
/// missing token the permissions require is <c>IntegrityCodeMissing</c>, and one that cannot be checked here (a SHA-3 digest the
/// platform lacks) is <c>IntegrityCodeNotVerified</c>. Strict mode throws on any of them. The file is digested in windows, so a large
/// file is never held in memory for this.
/// </para>
/// </remarks>
internal static class IntegrityVerifier
{
    private const string AuthenticatedDataOid = "1.2.840.113549.1.9.16.1.2";
    private const string IntegrityInfoOid = "1.0.32004.1.0";
    private const string WrapKdfOid = "1.0.32004.1.1";
    private const string MacDataAttributeOid = "1.0.32004.1.2";
    private const string Aes256WrapOid = "2.16.840.1.101.3.4.1.45";
    private const string HmacSha256Oid = "1.2.840.113549.2.9";
    private const string ContentTypeAttributeOid = "1.2.840.113549.1.9.3";
    private const string MessageDigestAttributeOid = "1.2.840.113549.1.9.4";

    /// <summary>The digest algorithms of ISO/TS 32004 Table 8: object identifier, name, and whether this platform implements it.</summary>
    private static readonly (string Oid, HashAlgorithmName Name, Func<bool> IsSupported)[] Digests =
    [
        ("2.16.840.1.101.3.4.2.1", HashAlgorithmName.SHA256, static () => true),
        ("2.16.840.1.101.3.4.2.2", HashAlgorithmName.SHA384, static () => true),
        ("2.16.840.1.101.3.4.2.3", HashAlgorithmName.SHA512, static () => true),
        ("2.16.840.1.101.3.4.2.8", HashAlgorithmName.SHA3_256, static () => SHA3_256.IsSupported),
        ("2.16.840.1.101.3.4.2.9", HashAlgorithmName.SHA3_384, static () => SHA3_384.IsSupported),
        ("2.16.840.1.101.3.4.2.10", HashAlgorithmName.SHA3_512, static () => SHA3_512.IsSupported),
    ];

    private static readonly CosName AuthCode = new("AuthCode");
    private static readonly CosName MacLocation = new("MACLocation");
    private static readonly CosName Standalone = new("Standalone");
    private static readonly CosName AttachedToSig = new("AttachedToSig");
    private static readonly CosName SigObjRef = new("SigObjRef");
    private static readonly CosName KdfSalt = new("KDFSalt");

    /// <summary>Checks the document's PDF MAC token, if it has or needs one.</summary>
    /// <param name="source">The file.</param>
    /// <param name="loader">The loader, decryption installed.</param>
    /// <param name="version">The encryption dictionary's <c>V</c>.</param>
    /// <param name="rawPermissions">The permissions word (<c>P</c>); bit 13 clear requires a token.</param>
    /// <param name="fileKey">The file encryption key; empty when the document opened without authentication (not checkable).</param>
    /// <param name="diagnostics">Where to report the outcome.</param>
    /// <returns>The outcome.</returns>
    public static PdfIntegrityStatus Verify(PdfSource source, ObjectLoader loader, int version, int rawPermissions, ReadOnlySpan<byte> fileKey, DiagnosticSink diagnostics)
    {
        CosDictionary trailer = loader.CrossReference.Sections[0].Trailer;
        bool required = version >= 5 && (rawPermissions & (1 << 12)) == 0;
        if (!trailer.TryGetValue(AuthCode, out CosObject? entry))
        {
            if (!required)
            {
                return PdfIntegrityStatus.None;
            }

            diagnostics.Report(
                DiagnosticCodes.IntegrityCodeMissing,
                DiagnosticSeverity.Warning,
                "Permission bit 13 is clear, so a PDF MAC token is required (ISO/TS 32004 Table 3), but the trailer has no AuthCode dictionary.");
            return PdfIntegrityStatus.Missing;
        }

        if (fileKey.IsEmpty)
        {
            diagnostics.Report(
                DiagnosticCodes.IntegrityCodeNotVerified,
                DiagnosticSeverity.Information,
                "The document opened without its user password, so the PDF MAC token cannot be checked without the file encryption key.");
            return PdfIntegrityStatus.NotVerified;
        }

        var verifier = new Verification(source, loader, diagnostics);
        return verifier.Run(entry, version, fileKey);
    }

    /// <summary>Parses a PDF MAC token and checks its structure (§6.3) without any key: the parser the fuzz target drives.</summary>
    /// <param name="der">The DER-encoded token.</param>
    /// <param name="problem">Why the token is invalid, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the token is structurally valid.</returns>
    internal static bool TryParseToken(byte[] der, out string? problem)
    {
        try
        {
            problem = MacToken.Parse(der).Problem;
        }
        catch (AsnContentException exception)
        {
            problem = exception.Message;
        }
        catch (CryptographicException exception)
        {
            problem = exception.Message;
        }

        return problem is null;
    }

    /// <summary>The checks of one document, with its diagnostics.</summary>
    private sealed class Verification(PdfSource source, ObjectLoader loader, DiagnosticSink diagnostics)
    {
        public PdfIntegrityStatus Run(CosObject entry, int version, ReadOnlySpan<byte> fileKey)
        {
            if (loader.Resolve(entry) is not CosDictionary authCode)
            {
                return Invalid("The trailer's AuthCode entry shall be a dictionary.");
            }

            if (version < 5)
            {
                return Invalid(string.Create(CultureInfo.InvariantCulture, $"An AuthCode dictionary requires an encryption dictionary with V 5 or later (ISO/TS 32004 Table 5); this one has V {version}."));
            }

            if (Encryption() is not { } encryption
                || loader.Resolve(encryption.TryGetValue(KdfSalt, out CosObject? salt) ? salt : null) is not CosString { Bytes.Length: 32 } kdfSalt)
            {
                return Invalid("The encryption dictionary shall have a 32-byte KDFSalt string when the document has a PDF MAC token (ISO/TS 32004 Table 2).");
            }

            CosObject location = loader.Resolve(authCode.TryGetValue(MacLocation, out CosObject? named) ? named : null);
            if (Standalone.Equals(location))
            {
                return VerifyStandalone(authCode, fileKey, kdfSalt.Bytes);
            }

            if (AttachedToSig.Equals(location))
            {
                return VerifyAttached(authCode, fileKey, kdfSalt.Bytes);
            }

            if (location is CosName other)
            {
                diagnostics.Report(
                    DiagnosticCodes.IntegrityCodeNotVerified,
                    DiagnosticSeverity.Information,
                    $"The PDF MAC token's location /{other.Value} is not one ISO/TS 32004 defines; the token is not checked.");
                return PdfIntegrityStatus.NotVerified;
            }

            return Invalid("The AuthCode dictionary has no MACLocation, which indicates an invalid MAC (ISO/TS 32004 B.2.1).");
        }

        private CosDictionary? Encryption() =>
            loader.Resolve(loader.CrossReference.Trailer.TryGetValue(KnownNames.Encrypt, out CosObject? encrypt) ? encrypt : null) as CosDictionary;

        private PdfIntegrityStatus VerifyStandalone(CosDictionary authCode, ReadOnlySpan<byte> fileKey, ReadOnlySpan<byte> kdfSalt)
        {
            if (!TryReadByteRange(authCode, out long[] range))
            {
                return Invalid("The AuthCode ByteRange shall be four nonnegative integers [0 L1 S L2] with S greater than L1, inside the file.");
            }

            long baseOffset = loader.Header.Offset;
            long gapStart = range[1];
            long gapEnd = range[2];
            if (!TryReadHexToken(baseOffset + gapStart, gapEnd - gapStart, out byte[]? token))
            {
                return Invalid("The bytes the AuthCode ByteRange leaves out shall be exactly the MAC string: '<', an even number of hexadecimal digits, '>'.");
            }

            return CheckCoverage(VerifyToken(token, fileKey, kdfSalt, range, signatureValue: null), range);
        }

        /// <summary>B.2.2.2 and B.2.3.2: a valid token whose byte range stops short of the end of the file leaves the rest unprotected.</summary>
        private PdfIntegrityStatus CheckCoverage(PdfIntegrityStatus status, long[] range)
        {
            if (status == PdfIntegrityStatus.Verified && range[2] + range[3] != source.Length - loader.Header.Offset)
            {
                diagnostics.Report(
                    DiagnosticCodes.IntegrityCodeIncomplete,
                    DiagnosticSeverity.Warning,
                    "The PDF MAC token is valid but its byte range does not reach the end of the file: what follows is not protected (a stale MAC after an update, or tampering; ISO/TS 32004 B.2.2.2, B.2.3.2).");
                return PdfIntegrityStatus.Failed;
            }

            return status;
        }

        private PdfIntegrityStatus VerifyAttached(CosDictionary authCode, ReadOnlySpan<byte> fileKey, ReadOnlySpan<byte> kdfSalt)
        {
            if (!authCode.TryGetValue(SigObjRef, out CosObject? reference)
                || loader.Resolve(reference) is not CosDictionary signature
                || loader.Resolve(signature.TryGetValue(KnownNames.Contents, out CosObject? contents) ? contents : null) is not CosString container)
            {
                return Invalid("The AuthCode SigObjRef shall refer to a signature dictionary with a Contents string (ISO/TS 32004 Table 6).");
            }

            if (!TryReadByteRange(signature, out long[] range))
            {
                return Invalid("The signature dictionary the PDF MAC token is attached to has no valid ByteRange.");
            }

            byte[]? token = null;
            byte[] signatureValue;
            try
            {
                var signed = new SignedCms();
                signed.Decode(WithoutPadding(container.Bytes));
                SignerInfo signer = signed.SignerInfos[0];
                signatureValue = signer.GetSignature();
                foreach (CryptographicAttributeObject attribute in signer.UnsignedAttributes)
                {
                    if (attribute.Oid.Value == MacDataAttributeOid)
                    {
                        if (token is not null || attribute.Values.Count != 1)
                        {
                            return Invalid("The signature shall carry exactly one pdfMacData attribute with exactly one value (ISO/TS 32004 §6.5.2).");
                        }

                        token = attribute.Values[0].RawData;
                    }
                }
            }
            catch (CryptographicException exception)
            {
                return Invalid($"The signature container the PDF MAC token is attached to cannot be decoded ({exception.Message}).");
            }
            catch (ArgumentOutOfRangeException)
            {
                return Invalid("The signature container the PDF MAC token is attached to has no signer.");
            }

            if (token is null)
            {
                return Invalid("The signature the AuthCode dictionary points to carries no pdfMacData attribute (ISO/TS 32004 §6.5.2).");
            }

            return CheckCoverage(VerifyToken(token, fileKey, kdfSalt, range, signatureValue), range);
        }

        /// <summary>Validates the token's CMS structure (§6.3, B.3) and its binding to the file (B.2).</summary>
        private PdfIntegrityStatus VerifyToken(byte[] token, ReadOnlySpan<byte> fileKey, ReadOnlySpan<byte> kdfSalt, long[] range, byte[]? signatureValue)
        {
            MacToken parsed;
            try
            {
                parsed = MacToken.Parse(token);
            }
            catch (AsnContentException exception)
            {
                return Invalid($"The PDF MAC token is not a valid DER-encoded AuthenticatedData ({exception.Message}).");
            }
            catch (CryptographicException exception)
            {
                return Invalid($"The PDF MAC token is not a valid DER-encoded AuthenticatedData ({exception.Message}).");
            }

            if (parsed.Problem is { } problem)
            {
                return Invalid(problem);
            }

            if (!TryCreateDigest(parsed.DigestAlgorithm, out HashAlgorithmName digestName))
            {
                diagnostics.Report(
                    DiagnosticCodes.IntegrityCodeNotVerified,
                    DiagnosticSeverity.Information,
                    $"The PDF MAC token's digest algorithm {parsed.DigestAlgorithm} is not available on this platform; the document's integrity is not checked.");
                return PdfIntegrityStatus.NotVerified;
            }

            // §6.4: KEK = HKDF-SHA-256(file encryption key, KDFSalt, "PDFMAC"); then unwrap the MAC key (RFC 3394).
            byte[] keyEncryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, fileKey.ToArray(), 32, kdfSalt.ToArray(), "PDFMAC"u8.ToArray());
            byte[]? macKey = AesKeyWrap.Unwrap(keyEncryptionKey, parsed.EncryptedKey);
            if (macKey is null)
            {
                return Mismatch("The PDF MAC token's key does not unwrap with the document's key: the token was not made for this document's key, or was changed.");
            }

            // RFC 5652 §9.2: the MAC covers the DER of the authenticated attributes with the SET OF tag.
            byte[] attributes = parsed.AuthenticatedAttributes.ToArray();
            attributes[0] = 0x31;
            byte[] mac = HMACSHA256.HashData(macKey, attributes);
            if (!CryptographicOperations.FixedTimeEquals(mac, parsed.Mac))
            {
                return Mismatch("The PDF MAC token's MAC does not match its authenticated attributes: the token was changed.");
            }

            if (!CryptographicOperations.FixedTimeEquals(Hash(digestName, parsed.Content), parsed.MessageDigest))
            {
                return Mismatch("The PDF MAC token's messageDigest attribute does not match its content.");
            }

            if (signatureValue is null && parsed.SignatureDigest is not null)
            {
                return Invalid("A standalone PDF MAC token shall not have a signatureDigest (ISO/TS 32004 §6.6.2).");
            }

            if (signatureValue is not null
                && (parsed.SignatureDigest is null || !CryptographicOperations.FixedTimeEquals(Hash(digestName, signatureValue), parsed.SignatureDigest)))
            {
                return Mismatch("The PDF MAC token's signatureDigest does not match the signature it is attached to (ISO/TS 32004 §6.6.3).");
            }

            if (!CryptographicOperations.FixedTimeEquals(DigestRange(digestName, range), parsed.DataDigest))
            {
                return Mismatch("The file's bytes do not match the PDF MAC token's dataDigest: the document was changed after the MAC was computed.");
            }

            return PdfIntegrityStatus.Verified;
        }

        private bool TryReadByteRange(CosDictionary dictionary, out long[] range)
        {
            range = new long[4];
            if (loader.Resolve(dictionary.TryGetValue(KnownNames.ByteRange, out CosObject? entry) ? entry : null) is not CosArray { Count: 4 } array)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if (loader.Resolve(array[i]) is not CosInteger { Value: >= 0 } value)
                {
                    return false;
                }

                range[i] = value.Value;
            }

            long length = source.Length - loader.Header.Offset;
            return range[0] == 0 && range[2] > range[1] && range[1] <= length && range[2] <= length && range[3] <= length - range[2];
        }

        private bool TryReadHexToken(long offset, long length, out byte[] token)
        {
            token = [];
            if (length < 2 || length > 1 << 20)
            {
                return false;
            }

            byte[] region = new byte[length];
            if (source.Read(offset, region) != region.Length || region[0] != (byte)'<' || region[^1] != (byte)'>' || (region.Length - 2) % 2 != 0)
            {
                return false;
            }

            byte[] decoded = new byte[(region.Length - 2) / 2];
            if (Convert.FromHexString(Encoding.Latin1.GetString(region, 1, region.Length - 2), decoded, out _, out int written) != OperationStatus.Done
                || written != decoded.Length)
            {
                return false;
            }

            token = decoded;
            return true;
        }

        private byte[] DigestRange(HashAlgorithmName name, long[] range)
        {
            using var hash = IncrementalHash.CreateHash(name);
            long baseOffset = loader.Header.Offset;
            byte[] buffer = new byte[64 * 1024];
            for (int part = 0; part < 4; part += 2)
            {
                long position = baseOffset + range[part];
                long remaining = range[part + 1];
                while (remaining > 0)
                {
                    int read = source.Read(position, buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)));
                    if (read <= 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer, 0, read);
                    position += read;
                    remaining -= read;
                }
            }

            return hash.GetHashAndReset();
        }

        private PdfIntegrityStatus Invalid(string message)
        {
            diagnostics.Report(DiagnosticCodes.IntegrityCodeInvalid, DiagnosticSeverity.Error, message);
            return PdfIntegrityStatus.Failed;
        }

        private PdfIntegrityStatus Mismatch(string message)
        {
            diagnostics.Report(DiagnosticCodes.IntegrityCodeMismatch, DiagnosticSeverity.Error, message);
            return PdfIntegrityStatus.Failed;
        }

        private static byte[] WithoutPadding(ReadOnlySpan<byte> container)
        {
            // Signature containers are padded with zeros to fill their reserved space (ISO 32000-2 §12.8.3.3.1). The padding is
            // whatever follows the container's outer DER element: trimming trailing zero bytes instead would also cut a container
            // whose own last byte is zero (the token's MAC ends it, so one in 256). An unreadable length is left to the decoder.
            try
            {
                AsnDecoder.ReadEncodedValue(container, AsnEncodingRules.BER, out _, out _, out int consumed);
                return container[..consumed].ToArray();
            }
            catch (AsnContentException)
            {
                return container.ToArray();
            }
        }

        private static byte[] Hash(HashAlgorithmName name, ReadOnlySpan<byte> data)
        {
            using var hash = IncrementalHash.CreateHash(name);
            hash.AppendData(data);
            return hash.GetHashAndReset();
        }

        /// <summary>Finds the digest algorithm <paramref name="oid"/> names in <see cref="Digests"/>, when this platform has it.</summary>
        private static bool TryCreateDigest(string oid, out HashAlgorithmName name)
        {
            foreach ((string known, HashAlgorithmName algorithm, Func<bool> isSupported) in Digests)
            {
                if (known == oid)
                {
                    name = algorithm;
                    return isSupported();
                }
            }

            name = default;
            return false;
        }
    }

    /// <summary>The parts of a PDF MAC token the validation needs (RFC 5652 §9.1, ISO/TS 32004 §6.2-6.3).</summary>
    private sealed class MacToken
    {
        public string? Problem { get; private set; }

        public string DigestAlgorithm { get; private set; } = string.Empty;

        public byte[] EncryptedKey { get; private set; } = [];

        public ReadOnlyMemory<byte> AuthenticatedAttributes { get; private set; }

        public byte[] Mac { get; private set; } = [];

        public byte[] Content { get; private set; } = [];

        public byte[] MessageDigest { get; private set; } = [];

        public byte[] DataDigest { get; private set; } = [];

        public byte[]? SignatureDigest { get; private set; }

        public static MacToken Parse(byte[] der)
        {
            var token = new MacToken();
            var contentInfo = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            if (contentInfo.ReadObjectIdentifier() != AuthenticatedDataOid)
            {
                return token.Fail("The PDF MAC token's content type shall be id-ct-authData (ISO/TS 32004 §6.3.1).");
            }

            AsnReader data = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
            _ = data.ReadInteger();
            if (data.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                data.ReadEncodedValue(); // originatorInfo
            }

            if (token.ReadRecipient(data.ReadSetOf()) is { } recipientProblem)
            {
                return token.Fail(recipientProblem);
            }

            if (ReadAlgorithm(data) != HmacSha256Oid)
            {
                return token.Fail("The PDF MAC token's MAC algorithm shall be HMAC with SHA-256 (ISO/TS 32004 Table 9).");
            }

            if (!data.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                return token.Fail("The PDF MAC token shall name its digest algorithm (ISO/TS 32004 §6.3.4).");
            }

            token.DigestAlgorithm = ReadAlgorithm(data, new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));

            AsnReader encapsulated = data.ReadSequence();
            if (encapsulated.ReadObjectIdentifier() != IntegrityInfoOid)
            {
                return token.Fail("The PDF MAC token's encapsulated content type shall be id-ct-pdfMacIntegrityInfo (ISO/TS 32004 §6.3.2).");
            }

            token.Content = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadOctetString();
            if (token.ReadIntegrityInfo() is { } infoProblem)
            {
                return token.Fail(infoProblem);
            }

            var attributesTag = new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true);
            if (!data.PeekTag().HasSameClassAndValue(attributesTag))
            {
                return token.Fail("The PDF MAC token shall have authenticated attributes (ISO/TS 32004 §6.3.6.1).");
            }

            token.AuthenticatedAttributes = data.ReadEncodedValue();
            if (token.ReadAttributes(attributesTag) is { } attributeProblem)
            {
                return token.Fail(attributeProblem);
            }

            token.Mac = data.ReadOctetString();
            if (data.HasData)
            {
                return token.Fail("The PDF MAC token shall have no unauthenticated attributes (ISO/TS 32004 §6.3.7).");
            }

            return token;
        }

        private static string ReadAlgorithm(AsnReader reader, Asn1Tag? tag = null)
        {
            AsnReader algorithm = tag is { } implicitTag ? reader.ReadSequence(implicitTag) : reader.ReadSequence();
            return algorithm.ReadObjectIdentifier();
        }

        private string? ReadRecipient(AsnReader recipients)
        {
            var passwordRecipient = new Asn1Tag(TagClass.ContextSpecific, 3, isConstructed: true);
            if (!recipients.HasData || !recipients.PeekTag().HasSameClassAndValue(passwordRecipient))
            {
                return "The PDF MAC token shall have exactly one recipient, of type PasswordRecipientInfo (ISO/TS 32004 §6.3.3).";
            }

            AsnReader recipient = recipients.ReadSequence(passwordRecipient);
            if (recipients.HasData)
            {
                return "The PDF MAC token shall have exactly one recipient (ISO/TS 32004 §6.3.3).";
            }

            _ = recipient.ReadInteger();
            var kdfTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            if (!recipient.PeekTag().HasSameClassAndValue(kdfTag) || ReadAlgorithm(recipient, kdfTag) != WrapKdfOid)
            {
                return "The PDF MAC token's key derivation algorithm shall be pdfMacWrapKdf (ISO/TS 32004 §6.4).";
            }

            if (ReadAlgorithm(recipient) != Aes256WrapOid)
            {
                return "The PDF MAC token's key encryption algorithm shall be AES-256 key wrap without padding (ISO/TS 32004 Table 7).";
            }

            EncryptedKey = recipient.ReadOctetString();
            return null;
        }

        private string? ReadIntegrityInfo()
        {
            AsnReader info = new AsnReader(Content, AsnEncodingRules.DER).ReadSequence();
            if (info.ReadInteger() != 0)
            {
                return "The PdfMacIntegrityInfo version shall be 0 (ISO/TS 32004 §6.6.1).";
            }

            DataDigest = info.ReadOctetString();
            if (info.HasData)
            {
                SignatureDigest = info.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
            }

            return null;
        }

        private string? ReadAttributes(Asn1Tag tag)
        {
            AsnReader attributes = new AsnReader(AuthenticatedAttributes, AsnEncodingRules.DER).ReadSetOf(tag);
            int contentTypes = 0;
            int digests = 0;
            while (attributes.HasData)
            {
                AsnReader attribute = attributes.ReadSequence();
                string type = attribute.ReadObjectIdentifier();
                AsnReader values = attribute.ReadSetOf();
                switch (type)
                {
                    case ContentTypeAttributeOid:
                        contentTypes++;
                        if (values.ReadObjectIdentifier() != IntegrityInfoOid || values.HasData)
                        {
                            return "The PDF MAC token's content-type attribute shall be id-ct-pdfMacIntegrityInfo (ISO/TS 32004 §6.3.6.2).";
                        }

                        break;
                    case MessageDigestAttributeOid:
                        digests++;
                        MessageDigest = values.ReadOctetString();
                        if (values.HasData)
                        {
                            return "The PDF MAC token's message-digest attribute shall have exactly one value (ISO/TS 32004 §6.3.6.3).";
                        }

                        break;
                    default:
                        break;
                }
            }

            return contentTypes == 1 && digests == 1
                ? null
                : "The PDF MAC token shall have exactly one content-type and one message-digest attribute (ISO/TS 32004 §6.3.6).";
        }

        private MacToken Fail(string problem)
        {
            Problem = problem;
            return this;
        }
    }
}
