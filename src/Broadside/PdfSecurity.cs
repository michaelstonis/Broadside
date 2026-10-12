using Broadside.Objects;

namespace Broadside;

/// <summary>How an encrypted document is protected and what the credential that opened it allows.</summary>
/// <remarks>
/// ISO 32000-2 §7.6 (Table 20 and Table 21); ISO/TS 32003 (version 6, revision 7); ISO/TS 32004 (integrity MAC). Read once, when the
/// document is opened; <see cref="EncryptionDictionary"/> is the live encryption dictionary.
/// </remarks>
public sealed class PdfSecurity
{
    internal PdfSecurity(
        CosDictionary encryptionDictionary,
        CosName handler,
        CosName? subFilter,
        int version,
        int? revision,
        int keyLength,
        PdfAccessLevel access,
        PdfPermissions permissions,
        int rawPermissions,
        bool encryptsMetadata,
        PdfIntegrityStatus integrity)
    {
        EncryptionDictionary = encryptionDictionary;
        Handler = handler;
        SubFilter = subFilter;
        Version = version;
        Revision = revision;
        KeyLength = keyLength;
        Access = access;
        Permissions = permissions;
        RawPermissions = rawPermissions;
        EncryptsMetadata = encryptsMetadata;
        Integrity = integrity;
    }

    /// <summary>Gets the encryption dictionary (the trailer's <c>Encrypt</c> entry), whose strings are never encrypted.</summary>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20.</remarks>
    public CosDictionary EncryptionDictionary { get; }

    /// <summary>Gets the name of the security handler that opened the document, such as <c>Standard</c>.</summary>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20, <c>Filter</c>; a handler may also be chosen by <c>SubFilter</c>.</remarks>
    public CosName Handler { get; }

    /// <summary>Gets the encryption dictionary's <c>SubFilter</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20.</remarks>
    public CosName? SubFilter { get; }

    /// <summary>Gets the algorithm code <c>V</c>: 1 to 4 (deprecated), 5 (AES-256) or 6 (AES-GCM, ISO/TS 32003).</summary>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20; ISO/TS 32003 §5.1, Table 2.</remarks>
    public int Version { get; }

    /// <summary>Gets the standard security handler's revision <c>R</c> (2 to 7), or <see langword="null"/> for another handler.</summary>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 21; ISO/TS 32003 §5.1, Table 3.</remarks>
    public int? Revision { get; }

    /// <summary>Gets the length of the file encryption key, in bits.</summary>
    /// <remarks>ISO 32000-2 §7.6.2: 40 to 128 bits for <c>V</c> 1 to 4, 256 bits for 5 and 6.</remarks>
    public int KeyLength { get; }

    /// <summary>Gets which credential opened the document.</summary>
    /// <remarks>ISO 32000-2 §7.6.4.1.</remarks>
    public PdfAccessLevel Access { get; }

    /// <summary>Gets the permissions that apply: <see cref="PdfPermissions.All"/> for the owner, the granted ones for a user.</summary>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 22.</remarks>
    public PdfPermissions Permissions { get; }

    /// <summary>Gets the user access permissions word as the document states it (the <c>P</c> entry, or the value in <c>Perms</c>).</summary>
    /// <remarks>ISO 32000-2 §7.6.4.2: an unsigned 32-bit quantity stored as a signed integer; bit 1 is the low-order bit.</remarks>
    public int RawPermissions { get; }

    /// <summary>Gets a value indicating whether the document-level metadata stream is encrypted (<c>EncryptMetadata</c>).</summary>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 21: meaningful for <c>V</c> 4 and later, default <see langword="true"/>.</remarks>
    public bool EncryptsMetadata { get; }

    /// <summary>Gets a value indicating whether the permissions require a PDF MAC token in every revision (bit 13 clear).</summary>
    /// <remarks>ISO/TS 32004 §5.1.2, Table 3; meaningful for <c>V</c> 5 and later.</remarks>
    public bool RequiresIntegrityCode => Version >= 5 && (RawPermissions & (1 << 12)) == 0;

    /// <summary>Gets the outcome of checking the document's integrity MAC when it was opened.</summary>
    /// <remarks>ISO/TS 32004, Annex B. A failure is also recorded as a diagnostic, and throws in strict mode.</remarks>
    public PdfIntegrityStatus Integrity { get; }
}
