namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics about encryption and security handlers.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004. Each code is reported under <see cref="DiagnosticArea.Security"/>; see <see cref="DiagnosticCatalog"/> for every area.</para>
/// </remarks>
public static class SecurityDiagnostics
{
    /// <summary>Gets the <c>UnencryptedWrapperInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6.7</remarks>
    public static DiagnosticCode UnencryptedWrapperInvalid { get; } = new(nameof(UnencryptedWrapperInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6.7");

    /// <summary>Gets the <c>EncryptedPayloadFilterUnknown</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6.7</remarks>
    public static DiagnosticCode EncryptedPayloadFilterUnknown { get; } = new(nameof(EncryptedPayloadFilterUnknown), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6.7");

    /// <summary>Gets the <c>EncryptDictionaryInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptDictionaryInvalid { get; } = new(nameof(EncryptDictionaryInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionIdMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionIdMissing { get; } = new(nameof(EncryptionIdMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionVersionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionVersionInvalid { get; } = new(nameof(EncryptionVersionInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionRevisionInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionRevisionInvalid { get; } = new(nameof(EncryptionRevisionInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionKeyLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionKeyLengthInvalid { get; } = new(nameof(EncryptionKeyLengthInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionEntryLengthInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionEntryLengthInvalid { get; } = new(nameof(EncryptionEntryLengthInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionPermissionsMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionPermissionsMismatch { get; } = new(nameof(EncryptionPermissionsMismatch), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionExtensionMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionExtensionMissing { get; } = new(nameof(EncryptionExtensionMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>OwnerPasswordKeyVariant</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode OwnerPasswordKeyVariant { get; } = new(nameof(OwnerPasswordKeyVariant), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>CryptFilterMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterMissing { get; } = new(nameof(CryptFilterMissing), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>CryptFilterMethodInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterMethodInvalid { get; } = new(nameof(CryptFilterMethodInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptedDataInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptedDataInvalid { get; } = new(nameof(EncryptedDataInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptedDataAuthenticationFailed</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptedDataAuthenticationFailed { get; } = new(nameof(EncryptedDataAuthenticationFailed), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>MetadataNotEncrypted</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode MetadataNotEncrypted { get; } = new(nameof(MetadataNotEncrypted), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>IntegrityCodeMissing</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeMissing { get; } = new(nameof(IntegrityCodeMissing), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>IntegrityCodeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeInvalid { get; } = new(nameof(IntegrityCodeInvalid), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>IntegrityCodeMismatch</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeMismatch { get; } = new(nameof(IntegrityCodeMismatch), DiagnosticArea.Security, DiagnosticSeverity.Error, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>IntegrityCodeIncomplete</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeIncomplete { get; } = new(nameof(IntegrityCodeIncomplete), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>IntegrityCodeNotVerified</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode IntegrityCodeNotVerified { get; } = new(nameof(IntegrityCodeNotVerified), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>PublicKeyRecipientInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode PublicKeyRecipientInvalid { get; } = new(nameof(PublicKeyRecipientInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>PublicKeyEnvelopeInvalid</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode PublicKeyEnvelopeInvalid { get; } = new(nameof(PublicKeyEnvelopeInvalid), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>CryptFilterNotAuthorized</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode CryptFilterNotAuthorized { get; } = new(nameof(CryptFilterNotAuthorized), DiagnosticArea.Security, DiagnosticSeverity.Information, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets the <c>EncryptionDeprecated</c> code.</summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004</remarks>
    public static DiagnosticCode EncryptionDeprecated { get; } = new(nameof(EncryptionDeprecated), DiagnosticArea.Security, DiagnosticSeverity.Warning, "ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        UnencryptedWrapperInvalid,
        EncryptedPayloadFilterUnknown,
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
