namespace Broadside;

/// <summary>How a save encrypts the document with the standard security handler: passwords, permissions, algorithm and metadata.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.4 (standard security handler). Opening the saved file with the user password grants <see cref="Permissions"/>;
/// opening it with the owner password grants every permission (§7.6.4.1). Without an owner password the user password serves as
/// both (Algorithm 3 step a), so no password grants more than <see cref="Permissions"/>. An empty user password opens the document
/// without asking for one, with those permissions.
/// </para>
/// <para>
/// The default algorithm, <see cref="PdfEncryptionAlgorithm.Aes256"/> (revision 6), takes passwords of any Unicode text, prepared
/// with SASLprep (RFC 4013) and encoded as UTF-8, of which the first 127 bytes count (§7.6.4.3.3). The deprecated algorithms take
/// passwords PDFDocEncoding can represent, of which the first 32 bytes count (§7.6.4.3.2).
/// </para>
/// <para>
/// Every string and stream is encrypted except those §7.6.2 exempts: the encryption dictionary, the trailer's file identifiers,
/// cross-reference streams, the <c>Contents</c> of signature dictionaries, and, when <see cref="EncryptMetadata"/> is
/// <see langword="false"/>, metadata streams.
/// </para>
/// </remarks>
public sealed class PdfEncryptionOptions
{
    private PdfEncryptionAlgorithm _algorithm = PdfEncryptionAlgorithm.Aes256;

    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionOptions"/> class.</summary>
    /// <param name="userPassword">The password that opens the document with <see cref="Permissions"/>; <see langword="null"/> or empty opens it without a password.</param>
    /// <param name="ownerPassword">The password that opens the document with every permission; <see langword="null"/> or empty for the user password.</param>
    /// <remarks>ISO 32000-2 §7.6.4.1, §7.6.4.4.2 (Algorithm 3 step a).</remarks>
    public PdfEncryptionOptions(string? userPassword, string? ownerPassword)
    {
        UserPassword = userPassword ?? string.Empty;
        OwnerPassword = string.IsNullOrEmpty(ownerPassword) ? UserPassword : ownerPassword;
    }

    /// <summary>Gets the user password; empty when the document opens without a password.</summary>
    /// <remarks>ISO 32000-2 §7.6.4.1.</remarks>
    public string UserPassword { get; }

    /// <summary>Gets the owner password, which is the user password when none was given.</summary>
    /// <remarks>ISO 32000-2 §7.6.4.1, §7.6.4.4.2 (Algorithm 3 step a).</remarks>
    public string OwnerPassword { get; }

    /// <summary>
    /// Gets or sets the algorithm. The default is <see cref="PdfEncryptionAlgorithm.Aes256"/>; the others are deprecated in PDF 2.0.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20; §7.6.4.2, Table 21.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined algorithm.</exception>
    public PdfEncryptionAlgorithm Algorithm
    {
        get => _algorithm;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not an encryption algorithm.");
            }

            _algorithm = value;
        }
    }

    /// <summary>Gets or sets what opening the document with the user password permits. The default is <see cref="PdfPermissions.All"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.4.2, Table 22: written as the <c>P</c> entry, with the reserved bits set and bit 10 set (PDF 2.0 writers shall
    /// set it), and bit 13 set, so no integrity MAC is required (ISO/TS 32004 §5.1.2). Values outside <see cref="PdfPermissions.All"/>
    /// are ignored. Readers are expected to respect the permissions; nothing in PDF enforces them (§7.6.4.1).
    /// </remarks>
    public PdfPermissions Permissions { get; set; } = PdfPermissions.All;

    /// <summary>
    /// Gets or sets a value indicating whether metadata streams are encrypted. The default is <see langword="true"/>; with
    /// <see langword="false"/>, a metadata stream stays readable to tools that index files without the password.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.4.2, Table 21 (<c>EncryptMetadata</c>), §7.6.4.3.2 step f and §7.6.4.4.9 (Algorithm 10). Meaningful for
    /// <c>V</c> 4 and later, so <see cref="PdfEncryptionAlgorithm.Rc4"/> (<c>V</c> 2) always encrypts metadata and a save that combines
    /// it with <see langword="false"/> is refused. Unencrypted metadata streams are also left uncompressed.
    /// </remarks>
    public bool EncryptMetadata { get; set; } = true;

    /// <summary>Sets <see cref="Algorithm"/>.</summary>
    /// <param name="algorithm">The algorithm.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="algorithm"/> is not a defined algorithm.</exception>
    /// <remarks>ISO 32000-2 §7.6.2, Table 20.</remarks>
    public PdfEncryptionOptions WithAlgorithm(PdfEncryptionAlgorithm algorithm)
    {
        Algorithm = algorithm;
        return this;
    }

    /// <summary>Sets <see cref="Permissions"/>.</summary>
    /// <param name="permissions">What the user password permits.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 22.</remarks>
    public PdfEncryptionOptions WithPermissions(PdfPermissions permissions)
    {
        Permissions = permissions;
        return this;
    }

    /// <summary>Sets <see cref="EncryptMetadata"/>.</summary>
    /// <param name="encryptMetadata">Whether metadata streams are encrypted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 21.</remarks>
    public PdfEncryptionOptions WithEncryptMetadata(bool encryptMetadata)
    {
        EncryptMetadata = encryptMetadata;
        return this;
    }
}
