using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Security;

/// <summary>
/// The public-key security handler: documents encrypted for certificate recipients, SubFilters <c>adbe.pkcs7.s3</c>,
/// <c>adbe.pkcs7.s4</c> and <c>adbe.pkcs7.s5</c>. Registered by default.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.5. Each <c>Recipients</c> string is a CMS enveloped-data object (RFC 5652) listing recipients with equal access;
/// the reader's certificates (<see cref="PdfCertificateCredentials"/>) are matched against them by issuer and serial number or subject
/// key identifier, and the first matching list's enveloped data, decrypted with the certificate's private key, holds a 20-byte seed and
/// 4 bytes of permissions, most significant byte first (§7.6.5.3). The file encryption key is the first <c>n</c>/8 bytes of the SHA-1
/// (up to 128 bits) or SHA-256 (256 bits) digest of the seed, every string of the <c>Recipients</c> array in order, and four 0xFF
/// bytes when the document-level metadata is left in plaintext.
/// </para>
/// <para>
/// Where the recipients are: <c>adbe.pkcs7.s3</c> and <c>s4</c> list them in the encryption dictionary (Table 23); <c>adbe.pkcs7.s5</c>
/// in the crypt filter <c>StmF</c> (else <c>StrF</c>) names (Table 27). Other crypt filters with their own <c>Recipients</c> (a single
/// string when streams name them in their <c>Crypt</c> decode parameters) get keys of their own, from a 20-byte seed without
/// permissions; one whose recipients do not include the reader is not authorized, and the streams that use it stay encrypted. The key
/// length comes from the crypt filter's method (<c>AESV2</c> 128 bits, <c>AESV3</c> 256) or its <c>Length</c>, in bits (Table 25),
/// else from the encryption dictionary's <c>Length</c> (default 40); <c>V</c> 5 and later always use 256 bits.
/// </para>
/// <para>
/// Permissions (Table 24): <c>adbe.pkcs7.s3</c> has the meanings of the standard handler's revision 2, the others those of revision 3
/// (<see cref="StandardSecurityHandler.UserPermissions"/>); bit 2 permits changing the encryption and enables every permission, which
/// gives <see cref="PdfAccessLevel.Owner"/> access. The integrity MAC of ISO/TS 32004 §6.4 uses the file encryption key, the key of
/// the crypt filter <c>DefaultCryptFilter</c> in an <c>adbe.pkcs7.s5</c> document.
/// </para>
/// <para>
/// Certificates and CMS are not available in a browser (WebAssembly): there the handler throws
/// <see cref="PdfEncryptionNotSupportedException"/> with <see cref="PdfEncryptionNotSupportedReason.Platform"/>, as it does for a
/// key-agreement recipient or a content-encryption algorithm (RC4, DES, RC2) the platform's CMS implementation cannot decrypt.
/// </para>
/// </remarks>
public sealed class PublicKeySecurityHandler : ISecurityHandler
{
    private static readonly CosName PubSec = new("Adobe.PubSec");
    private static readonly CosName S3 = new("adbe.pkcs7.s3");
    private static readonly CosName S4 = new("adbe.pkcs7.s4");
    private static readonly CosName S5 = new("adbe.pkcs7.s5");
    private static readonly CosName RecipientsName = new("Recipients");

    /// <summary>Permission bit 2 (Table 24): change the encryption, and every other permission.</summary>
    private const int ChangeEncryption = 1 << 1;

    /// <summary>Gets <c>Adobe.PubSec</c>; documents naming another public-key handler open through <see cref="SubFilters"/>.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.2: Entrust.PPKEF, Adobe.PPKLite and Adobe.PubSec are examples of public-key handler names.</remarks>
    public CosName Filter => PubSec;

    /// <summary>Gets <c>adbe.pkcs7.s3</c>, <c>adbe.pkcs7.s4</c> and <c>adbe.pkcs7.s5</c>.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.2.</remarks>
    public IReadOnlyCollection<CosName> SubFilters => [S3, S4, S5];

    /// <inheritdoc/>
    /// <exception cref="PdfCertificateException">No certificate given is a recipient with a usable private key.</exception>
    public SecurityHandlerResult Authenticate(SecurityHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (OperatingSystem.IsBrowser())
        {
            throw new PdfEncryptionNotSupportedException(
                PdfEncryptionNotSupportedReason.Platform,
                "The document is encrypted for certificate recipients; certificates and CMS are not available in a browser.");
        }

        CosDictionary encryption = context.EncryptionDictionary;
        CosName? subFilter = context.Resolve(Get(encryption, KnownNames.SubFilter)) as CosName;
        int version = context.Resolve(Get(encryption, KnownNames.V)) is CosInteger { Value: >= 0 and <= int.MaxValue } v ? (int)v.Value : 0;
        if (version == 0)
        {
            context.Report(
                DiagnosticCodes.EncryptionVersionInvalid,
                DiagnosticSeverity.Warning,
                "The public-key encryption dictionary has no usable V entry; it is read by its key length (RC4 below 256 bits, AES-256 at 256).");
        }

        IReadOnlyList<X509Certificate2> certificates = (context.Credentials as PdfCertificateCredentials)?.Certificates ?? [];
        CosDictionary? filters = context.Resolve(Get(encryption, KnownNames.CF)) as CosDictionary;

        // The document-level recipients: the encryption dictionary's (s3, s4), else those of the crypt filter StmF or StrF names (s5).
        CosName? documentFilter = null;
        CosDictionary? documentFilterDictionary = null;
        CosObject recipientsEntry = context.Resolve(Get(encryption, RecipientsName));
        if (recipientsEntry is CosNull)
        {
            foreach (CosName key in (ReadOnlySpan<CosName>)[KnownNames.StmF, KnownNames.StrF])
            {
                if (context.Resolve(Get(encryption, key)) is CosName name
                    && !name.Equals(FilterNames.Identity)
                    && context.Resolve(Get(filters, name)) is CosDictionary filter
                    && context.Resolve(Get(filter, RecipientsName)) is not CosNull)
                {
                    documentFilter = name;
                    documentFilterDictionary = filter;
                    recipientsEntry = context.Resolve(Get(filter, RecipientsName));
                    break;
                }
            }

            if (documentFilter is null)
            {
                throw context.Fail("The public-key encryption dictionary has no Recipients, neither its own (Table 23) nor in the crypt filter StmF or StrF names (Table 27).");
            }
        }
        else if (version >= 4 && context.Resolve(Get(encryption, KnownNames.StmF)) is CosName stmF && context.Resolve(Get(filters, stmF)) is CosDictionary stmFilter)
        {
            documentFilterDictionary = stmFilter;
        }

        if (subFilter is not null && subFilter.Equals(S5) != (documentFilter is not null))
        {
            context.Report(
                DiagnosticCodes.EncryptDictionaryInvalid,
                DiagnosticSeverity.Warning,
                $"With SubFilter /{subFilter.Value} the Recipients shall be in {(subFilter.Equals(S5) ? "the crypt filter dictionary (Table 27)" : "the encryption dictionary (Table 23)")}; they are read from where they are.");
        }

        byte[][] recipients = ReadRecipients(context, recipientsEntry, "the document");
        bool encryptMetadata = context.Resolve(Get(documentFilterDictionary, KnownNames.EncryptMetadata)) is CosBoolean filterFlag
            ? filterFlag.Value
            : context.Resolve(Get(encryption, KnownNames.EncryptMetadata)) is not CosBoolean { Value: false };
        int keyLength = KeyLength(context, encryption, documentFilterDictionary, version, documentFilter is null ? "the encryption dictionary" : $"the crypt filter /{documentFilter.Value}");

        Envelope envelope = Open(context, recipients, certificates, "the document")
            ?? throw new PdfCertificateException(
                certificates.Count == 0 ? PdfCertificateFailure.Required : PdfCertificateFailure.NoMatchingRecipient,
                RecipientIdentifiers(recipients),
                innerException: null);

        if (envelope.Content.Length < 20)
        {
            throw context.Fail(string.Create(CultureInfo.InvariantCulture, $"The enveloped data of the document's recipient list shall hold a 20-byte seed and 4 bytes of permissions (§7.6.5.3); it has {envelope.Content.Length} bytes."));
        }

        int rawPermissions = 0;
        if (envelope.Content.Length >= 24)
        {
            rawPermissions = BinaryPrimitives.ReadInt32BigEndian(envelope.Content.AsSpan(20, 4));
        }

        if (envelope.Content.Length != 24)
        {
            context.Report(
                DiagnosticCodes.PublicKeyEnvelopeInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The enveloped data of a document-level recipient list shall be a 20-byte seed and 4 bytes of permissions (§7.6.5.3); it has {envelope.Content.Length} bytes{(envelope.Content.Length < 24 ? ", so no permission is granted" : ", the rest is ignored")}."));
        }

        byte[] fileKey = DeriveKey(envelope.Content.AsSpan(0, 20), recipients, appendMetadataMarker: !encryptMetadata, keyLength);
        Dictionary<CosName, ReadOnlyMemory<byte>>? filterKeys = ReadFilterKeys(context, filters, documentFilter, fileKey, certificates, version);

        bool owner = (rawPermissions & ChangeEncryption) != 0;
        PdfPermissions permissions = owner
            ? PdfPermissions.All
            : StandardSecurityHandler.UserPermissions(rawPermissions, S3.Equals(subFilter) ? 2 : 3);
        return new SecurityHandlerResult(fileKey, owner ? PdfAccessLevel.Owner : PdfAccessLevel.User, permissions, rawPermissions)
        {
            CryptFilterKeys = filterKeys,
        };
    }

    /// <summary>Keys for every other crypt filter with recipients of its own (Table 27); an empty key marks one the reader is not a recipient of.</summary>
    private static Dictionary<CosName, ReadOnlyMemory<byte>>? ReadFilterKeys(
        SecurityHandlerContext context,
        CosDictionary? filters,
        CosName? documentFilter,
        byte[] fileKey,
        IReadOnlyList<X509Certificate2> certificates,
        int version)
    {
        if (filters is null)
        {
            return null;
        }

        var keys = new Dictionary<CosName, ReadOnlyMemory<byte>>();
        if (documentFilter is not null)
        {
            keys[documentFilter] = fileKey;
        }

        for (int index = 0; index < filters.Count; index++)
        {
            (CosName name, CosObject value) = filters.GetAt(index);
            if (name.Equals(documentFilter)
                || context.Resolve(value) is not CosDictionary filter
                || context.Resolve(Get(filter, RecipientsName)) is not { } entry
                || entry is CosNull)
            {
                continue;
            }

            string label = $"the crypt filter /{name.Value}";
            byte[][] recipients = ReadRecipients(context, entry, label);
            Envelope? envelope = Open(context, recipients, certificates, label);
            if (envelope is null)
            {
                context.Report(
                    DiagnosticCodes.CryptFilterNotAuthorized,
                    DiagnosticSeverity.Information,
                    $"None of the certificates given is a recipient of {label}; the streams that use it stay encrypted (§7.6.6).");
                keys[name] = ReadOnlyMemory<byte>.Empty;
                continue;
            }

            if (envelope.Content.Length < 20)
            {
                context.Report(
                    DiagnosticCodes.PublicKeyEnvelopeInvalid,
                    DiagnosticSeverity.Error,
                    string.Create(CultureInfo.InvariantCulture, $"The enveloped data of {label} shall hold a 20-byte seed (Table 27); it has {envelope.Content.Length} bytes, so the streams that use it stay encrypted."));
                keys[name] = ReadOnlyMemory<byte>.Empty;
                continue;
            }

            int keyLength = KeyLength(context, null, filter, version, label);
            keys[name] = DeriveKey(envelope.Content.AsSpan(0, 20), recipients, appendMetadataMarker: false, keyLength);
        }

        return keys.Count == 0 ? null : keys;
    }

    /// <summary>§7.6.5.3 steps a-d.</summary>
    private static byte[] DeriveKey(ReadOnlySpan<byte> seed, byte[][] recipients, bool appendMetadataMarker, int keyLength)
    {
        using var hash = IncrementalHash.CreateHash(keyLength > 20 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
        hash.AppendData(seed);
        foreach (byte[] recipient in recipients)
        {
            hash.AppendData(recipient);
        }

        if (appendMetadataMarker)
        {
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        return hash.GetHashAndReset()[..keyLength];
    }

    /// <summary>The key length in bytes: from the crypt filter's method or Length (bits), else the dictionary's Length (Table 25, Table 20).</summary>
    private static int KeyLength(SecurityHandlerContext context, CosDictionary? encryption, CosDictionary? filter, int version, string label)
    {
        if (version >= 5)
        {
            return 32;
        }

        CosName? method = context.Resolve(Get(filter, KnownNames.CFM)) as CosName;
        if (KnownNames.AESV3.Equals(method))
        {
            return 32;
        }

        if (KnownNames.AESV2.Equals(method))
        {
            return 16;
        }

        CosObject length = context.Resolve(Get(filter, KnownNames.Length));
        if (length is CosNull)
        {
            length = context.Resolve(Get(encryption, KnownNames.Length));
        }

        if (length is not CosInteger { Value: var bits })
        {
            return filter is null ? 5 : 16; // Table 20: Length defaults to 40 bits; a crypt filter's Length is required, 128 bits is what writers use.
        }

        if (bits is >= 5 and <= 16)
        {
            context.Report(
                DiagnosticCodes.EncryptionKeyLengthInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The Length of {label} is {bits}; public-key handlers give it in bits (Table 25), so it is read as a number of bytes."));
            bits *= 8;
        }

        long clamped = Math.Clamp(bits / 8 * 8, 40, 128);
        if (clamped != bits)
        {
            context.Report(
                DiagnosticCodes.EncryptionKeyLengthInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The Length of {label} is {bits} bits; a SHA-1 derived key is a multiple of 8 from 40 to 128 bits (§7.6.5.3), so {clamped} bits are used."));
        }

        return (int)(clamped / 8);
    }

    /// <summary>The bytes of each CMS object of a Recipients entry: an array of strings, or a single string (Table 27).</summary>
    private static byte[][] ReadRecipients(SecurityHandlerContext context, CosObject entry, string label)
    {
        List<byte[]> recipients = [];
        IEnumerable<CosObject> items = entry is CosArray array ? array : [entry];
        foreach (CosObject item in items)
        {
            if (context.Resolve(item) is CosString text)
            {
                recipients.Add(text.Bytes.ToArray());
            }
            else
            {
                context.Report(
                    DiagnosticCodes.PublicKeyRecipientInvalid,
                    DiagnosticSeverity.Warning,
                    $"An entry of the Recipients of {label} is not a string; it is ignored.");
            }
        }

        if (recipients.Count == 0)
        {
            throw context.Fail($"The Recipients of {label} hold no CMS object.");
        }

        return [.. recipients];
    }

    /// <summary>Finds the first recipient list naming one of the certificates and decrypts its enveloped data; null when none names one.</summary>
    private static Envelope? Open(SecurityHandlerContext context, byte[][] recipients, IReadOnlyList<X509Certificate2> certificates, string label)
    {
        if (certificates.Count == 0)
        {
            return null;
        }

        X509Certificate2? withoutKey = null;
        foreach (byte[] recipient in recipients)
        {
            EnvelopedCms? cms = Decode(context, recipient, label);
            if (cms is null)
            {
                continue;
            }

            foreach (RecipientInfo info in cms.RecipientInfos)
            {
                foreach (X509Certificate2 certificate in certificates)
                {
                    if (!Matches(info.RecipientIdentifier, certificate))
                    {
                        continue;
                    }

                    if (!certificate.HasPrivateKey)
                    {
                        withoutKey ??= certificate;
                        continue;
                    }

                    return new Envelope(Decrypt(cms, info, certificate, recipients));
                }
            }
        }

        if (withoutKey is not null)
        {
            throw new PdfCertificateException(
                PdfCertificateFailure.PrivateKeyUnavailable,
                RecipientIdentifiers(recipients),
                new CryptographicException($"The certificate '{withoutKey.Subject}' is a recipient of {label} but has no private key."));
        }

        return null;
    }

    private static byte[] Decrypt(EnvelopedCms cms, RecipientInfo info, X509Certificate2 certificate, byte[][] recipients)
    {
        try
        {
            cms.Decrypt(info, new X509Certificate2Collection(certificate));
            return cms.ContentInfo.Content;
        }
        catch (PlatformNotSupportedException exception)
        {
            throw Unsupported(cms, info, exception);
        }
        catch (CryptographicException exception)
        {
            if (info.Type == RecipientInfoType.KeyAgreement || IsLegacyCipher(cms.ContentEncryptionAlgorithm.Oid.Value))
            {
                throw Unsupported(cms, info, exception);
            }

            throw new PdfCertificateException(PdfCertificateFailure.PrivateKeyUnavailable, RecipientIdentifiers(recipients), exception);
        }
    }

    private static PdfEncryptionNotSupportedException Unsupported(EnvelopedCms cms, RecipientInfo info, Exception exception) =>
        new(
            PdfEncryptionNotSupportedReason.Platform,
            $"The enveloped data ({(info.Type == RecipientInfoType.KeyAgreement ? "a key-agreement recipient, " : string.Empty)}content encrypted with {cms.ContentEncryptionAlgorithm.Oid.FriendlyName ?? cms.ContentEncryptionAlgorithm.Oid.Value}) cannot be decrypted on this platform: {exception.Message}");

    /// <summary>RC4, DES and RC2 (§7.6.5.3, deprecated), which CMS implementations drop first.</summary>
    private static bool IsLegacyCipher(string? oid) => oid is "1.2.840.113549.3.4" or "1.3.14.3.2.7" or "1.2.840.113549.3.2";

    /// <summary>Decodes one CMS object; a string with bytes after the DER encoding (zero padding) is read up to its end.</summary>
    private static EnvelopedCms? Decode(SecurityHandlerContext context, byte[] recipient, string label)
    {
        try
        {
            AsnDecoder.ReadEncodedValue(recipient, AsnEncodingRules.BER, out _, out _, out int consumed);
            if (consumed != recipient.Length)
            {
                bool padding = recipient.AsSpan(consumed).IndexOfAnyExcept((byte)0) < 0;
                context.Report(
                    DiagnosticCodes.PublicKeyRecipientInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"A CMS object in the Recipients of {label} is followed by {recipient.Length - consumed} {(padding ? "zero bytes" : "other bytes")}; they are ignored (but still digested into the key)."));
            }

            var cms = new EnvelopedCms();
            cms.Decode(recipient.AsSpan(0, consumed));
            return cms;
        }
        catch (Exception exception) when (exception is CryptographicException or AsnContentException)
        {
            context.Report(
                DiagnosticCodes.PublicKeyRecipientInvalid,
                DiagnosticSeverity.Warning,
                $"A string in the Recipients of {label} is not a CMS enveloped-data object (RFC 5652); it is skipped: {exception.Message}");
            return null;
        }
    }

    /// <summary>RFC 5652 §6.2: a recipient is named by issuer and serial number, or by subject key identifier.</summary>
    private static bool Matches(SubjectIdentifier identifier, X509Certificate2 certificate)
    {
        switch (identifier.Value)
        {
            case X509IssuerSerial issuerSerial:
                return string.Equals(issuerSerial.IssuerName, certificate.Issuer, StringComparison.OrdinalIgnoreCase)
                    && SameSerialNumber(issuerSerial.SerialNumber, certificate.SerialNumber);
            case string keyIdentifier:
                foreach (X509Extension extension in certificate.Extensions)
                {
                    if (extension is X509SubjectKeyIdentifierExtension { SubjectKeyIdentifier: { } own })
                    {
                        return string.Equals(own, keyIdentifier, StringComparison.OrdinalIgnoreCase);
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private static bool SameSerialNumber(string left, string right) =>
        string.Equals(left.TrimStart('0'), right.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The recipients of every list that decodes, for the error message.</summary>
    private static SubjectIdentifier[] RecipientIdentifiers(byte[][] recipients)
    {
        List<SubjectIdentifier> identifiers = [];
        foreach (byte[] recipient in recipients)
        {
            try
            {
                AsnDecoder.ReadEncodedValue(recipient, AsnEncodingRules.BER, out _, out _, out int consumed);
                var cms = new EnvelopedCms();
                cms.Decode(recipient.AsSpan(0, consumed));
                foreach (RecipientInfo info in cms.RecipientInfos)
                {
                    identifiers.Add(info.RecipientIdentifier);
                }
            }
            catch (Exception exception) when (exception is CryptographicException or AsnContentException)
            {
                // Already reported while scanning.
            }
        }

        return [.. identifiers];
    }

    private static CosObject? Get(CosDictionary? dictionary, CosName key) =>
        dictionary is not null && dictionary.TryGetValue(key, out CosObject? value) ? value : null;

    /// <summary>The decrypted enveloped data of the recipient list that names the reader.</summary>
    private sealed record Envelope(byte[] Content);
}
