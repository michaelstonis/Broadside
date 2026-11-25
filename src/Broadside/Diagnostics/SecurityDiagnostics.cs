namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about encryption and security handlers.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004. Each code is reported under <see cref="DiagnosticArea.Security"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class SecurityDiagnostics
{
    /// <summary>Gets the <c>UnencryptedWrapperInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6.7</remarks>
    public static DiagnosticCode UnencryptedWrapperInvalid { get; } = new(nameof(UnencryptedWrapperInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6.7",
        "An unencrypted wrapper document deviates from the required wrapper structure. The embedded payload is read anyway.");

    /// <summary>Gets the <c>EncryptedPayloadFilterUnknown</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6.7</remarks>
    public static DiagnosticCode EncryptedPayloadFilterUnknown { get; } = new(nameof(EncryptedPayloadFilterUnknown), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6.7",
        "The encrypted payload of a wrapper needs a cryptographic filter that no available security handler provides. The wrapper is read, and the payload opens only if its own encryption dictionary names a handler that is available.");

    /// <summary>Gets the <c>EncryptedPayloadNotEncrypted</c> code: an unencrypted wrapper is written around a payload that is not encrypted.</summary>
    /// <remarks>ISO 32000-2 §7.6.7</remarks>
    public static DiagnosticCode EncryptedPayloadNotEncrypted { get; } = new(nameof(EncryptedPayloadNotEncrypted), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6.7",
        "The payload of an unencrypted wrapper is not an encrypted PDF file as the wrapper structure requires. The payload is embedded anyway.");

    /// <summary>Gets the <c>EncryptDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptDictionaryInvalid { get; } = new(nameof(EncryptDictionaryInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The trailer Encrypt entry or the encryption dictionary is malformed, for example it is not a dictionary or lacks a Filter entry. The document is read as not encrypted, or the file structure is rebuilt.");

    /// <summary>Gets the <c>EncryptionIdMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionIdMissing { get; } = new(nameof(EncryptionIdMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The trailer of an encrypted document has no ID array of two strings. An empty identifier is used.");

    /// <summary>Gets the <c>EncryptionVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionVersionInvalid { get; } = new(nameof(EncryptionVersionInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The encryption version entry V is not a usable published value. It is read as the closest supported algorithm or by its key length.");

    /// <summary>Gets the <c>EncryptionRevisionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionRevisionInvalid { get; } = new(nameof(EncryptionRevisionInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The standard security handler uses a revision that should not appear in a conforming file, such as the deprecated revision 5. It is read as that revision.");

    /// <summary>Gets the <c>EncryptionKeyLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionKeyLengthInvalid { get; } = new(nameof(EncryptionKeyLengthInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The key length of the encryption dictionary or a crypt filter is missing, out of range, or inconsistent with the key. A usable length is derived, for example by reading the value as a byte count or using 128 bits.");

    /// <summary>Gets the <c>EncryptionEntryLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionEntryLengthInvalid { get; } = new(nameof(EncryptionEntryLengthInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "An encryption dictionary entry has a different length than required. Only the required number of leading bytes is read, and a short entry is used as it is.");

    /// <summary>Gets the <c>EncryptionPermissionsMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionPermissionsMismatch { get; } = new(nameof(EncryptionPermissionsMismatch), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The encrypted Perms entry does not decrypt to a valid permissions block or disagrees with the P entry or EncryptMetadata. The value from Perms is used when it is valid, otherwise the P entry is used.");

    /// <summary>Gets the <c>EncryptionExtensionMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionExtensionMissing { get; } = new(nameof(EncryptionExtensionMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The document uses an ISO technical specification encryption level but the catalog Extensions dictionary does not declare that extension level. The document is read anyway.");

    /// <summary>Gets the <c>OwnerPasswordKeyVariant</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode OwnerPasswordKeyVariant { get; } = new(nameof(OwnerPasswordKeyVariant), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The owner password entry was computed with a known variant of the key algorithm that does not rehash the whole digest. The owner password is accepted.");

    /// <summary>Gets the <c>CryptFilterMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterMissing { get; } = new(nameof(CryptFilterMissing), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "An encryption dictionary entry names a crypt filter that the CF dictionary does not define. The data using that filter is left as it is.");

    /// <summary>Gets the <c>CryptFilterMethodInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterMethodInvalid { get; } = new(nameof(CryptFilterMethodInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "A crypt filter uses a method that does not fit, such as None or AES with a mismatched key size or version. The data is left as it is, or the method is read as the likely intended one.");

    /// <summary>Gets the <c>EncryptedDataInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptedDataInvalid { get; } = new(nameof(EncryptedDataInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "Encrypted data is malformed and cannot be decrypted as expected, for example it has an invalid length or padding. The problem is recorded and the affected data is read as far as possible.");

    /// <summary>Gets the <c>EncryptedDataAuthenticationFailed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptedDataAuthenticationFailed { get; } = new(nameof(EncryptedDataAuthenticationFailed), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The AES-GCM authentication tag of encrypted data does not match, so the data was changed or damaged after encryption. The data decrypts to nothing.");

    /// <summary>Gets the <c>MetadataNotEncrypted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode MetadataNotEncrypted { get; } = new(nameof(MetadataNotEncrypted), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The metadata stream is plaintext XMP although EncryptMetadata is true. The stream is read as it is.");

    /// <summary>Gets the <c>IntegrityCodeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeMissing { get; } = new(nameof(IntegrityCodeMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The permissions require a PDF MAC integrity token but the trailer has no AuthCode dictionary. Integrity cannot be confirmed.");

    /// <summary>Gets the <c>IntegrityCodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeInvalid { get; } = new(nameof(IntegrityCodeInvalid), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The PDF MAC integrity token is malformed or structurally invalid. The integrity check fails.");

    /// <summary>Gets the <c>IntegrityCodeMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeMismatch { get; } = new(nameof(IntegrityCodeMismatch), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The PDF MAC integrity token does not match the computed value, so the document was changed or damaged. The integrity check fails.");

    /// <summary>Gets the <c>IntegrityCodeIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeIncomplete { get; } = new(nameof(IntegrityCodeIncomplete), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The PDF MAC token is valid but its byte range does not reach the end of the file. The data after the range is not protected, as after an update or tampering.");

    /// <summary>Gets the <c>IntegrityCodeNotVerified</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeNotVerified { get; } = new(nameof(IntegrityCodeNotVerified), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The PDF MAC integrity token cannot be checked, for example because the document opened without the user password or the token uses an unsupported location or algorithm. The integrity of the document is not verified.");

    /// <summary>Gets the <c>PublicKeyRecipientInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode PublicKeyRecipientInvalid { get; } = new(nameof(PublicKeyRecipientInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "An entry of the Recipients array is not a string, is not a CMS enveloped-data object, or has trailing bytes. The entry or the extra bytes are ignored.");

    /// <summary>Gets the <c>PublicKeyEnvelopeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode PublicKeyEnvelopeInvalid { get; } = new(nameof(PublicKeyEnvelopeInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "The enveloped data of a recipient does not hold the required seed and permissions bytes. The streams that use it stay encrypted.");

    /// <summary>Gets the <c>CryptFilterNotAuthorized</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterNotAuthorized { get; } = new(nameof(CryptFilterNotAuthorized), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "Data is encrypted with a crypt filter that the supplied credentials do not unlock, for example no certificate names a recipient. The data stays encrypted.");

    /// <summary>Gets the <c>EncryptionDeprecated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionDeprecated { get; } = new(nameof(EncryptionDeprecated), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004",
        "A saved file is encrypted with an algorithm that is deprecated in PDF 2.0, such as RC4, which offers little protection. AES-256 is the PDF 2.0 algorithm.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        UnencryptedWrapperInvalid,
        EncryptedPayloadFilterUnknown,
        EncryptedPayloadNotEncrypted,
        EncryptDictionaryInvalid,
        EncryptionIdMissing,
        EncryptionVersionInvalid,
        EncryptionRevisionInvalid,
        EncryptionKeyLengthInvalid,
        EncryptionEntryLengthInvalid,
        EncryptionPermissionsMismatch,
        EncryptionExtensionMissing,
        OwnerPasswordKeyVariant,
        CryptFilterMissing,
        CryptFilterMethodInvalid,
        EncryptedDataInvalid,
        EncryptedDataAuthenticationFailed,
        MetadataNotEncrypted,
        IntegrityCodeMissing,
        IntegrityCodeInvalid,
        IntegrityCodeMismatch,
        IntegrityCodeIncomplete,
        IntegrityCodeNotVerified,
        PublicKeyRecipientInvalid,
        PublicKeyEnvelopeInvalid,
        CryptFilterNotAuthorized,
        EncryptionDeprecated,
    ];
}
