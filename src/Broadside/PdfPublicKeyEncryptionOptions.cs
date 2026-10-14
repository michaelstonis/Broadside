using System.Security.Cryptography.X509Certificates;

namespace Broadside;

/// <summary>
/// How a save encrypts the document for certificate recipients with the public-key security handler: who the recipients are, what
/// each may do, and whether metadata is encrypted.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.5. The saved file names the handler <c>Adobe.PubSec</c> with SubFilter <c>adbe.pkcs7.s5</c>: <c>V</c> 5, and a
/// crypt filter <c>DefaultCryptFilter</c> (method <c>AESV3</c>, 256 bits) that <c>StmF</c> and <c>StrF</c> name, which holds the
/// <c>Recipients</c> (Table 27). Recipients with the same permissions share one CMS enveloped-data object (RFC 5652), in the order
/// they were first added; its content is a random 20-byte seed followed by their permissions (§7.6.5.3), and its content-encryption
/// key is encrypted for each of them with RSA (PKCS #1 v1.5) and named by issuer and serial number. The file encryption key is the
/// SHA-256 digest of the seed and every CMS object in order (and four 0xFF bytes when metadata is left in plaintext), and strings and
/// streams are encrypted with AES-256 (§7.6.3.3, Algorithm 1.A). AES-256 is a PDF 2.0 feature, so the header is raised to 2.0 (in
/// strict mode, a document below 2.0 refuses the save).
/// </para>
/// <para>
/// Opening the saved file with a recipient's certificate and private key (<see cref="Security.PdfCertificateCredentials"/>) grants
/// that recipient's permissions; a recipient added with <see cref="AddOwner"/> has owner access. Every string and stream is encrypted
/// except those §7.6.2 exempts, as with <see cref="PdfEncryptionOptions"/>. Certificates and CMS are not available in a browser
/// (WebAssembly), where such a save throws <see cref="Security.PdfEncryptionNotSupportedException"/>.
/// </para>
/// </remarks>
public sealed class PdfPublicKeyEncryptionOptions
{
    private readonly List<PdfEncryptionRecipient> _recipients = [];

    /// <summary>Gets the recipients, in the order they were added.</summary>
    /// <remarks>ISO 32000-2 §7.6.5.2, Table 23 and Table 27 (<c>Recipients</c>).</remarks>
    public IReadOnlyList<PdfEncryptionRecipient> Recipients => _recipients;

    /// <summary>
    /// Gets or sets a value indicating whether metadata streams are encrypted. The default is <see langword="true"/>; with
    /// <see langword="false"/>, a metadata stream stays readable to tools that index files without a certificate.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.5.3 step c, Table 27 (<c>EncryptMetadata</c> in the crypt filter <c>StmF</c> names). Unencrypted metadata
    /// streams are also left uncompressed.
    /// </remarks>
    public bool EncryptMetadata { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the saved file carries an integrity MAC (a PDF MAC token) that every later revision
    /// must carry as well. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// ISO/TS 32004 §5.1 and §6: every recipient's permissions clear bit 13 (Table 4), the encryption dictionary gets a random 32-byte
    /// <c>KDFSalt</c>, and the trailer's <c>AuthCode</c> holds a MAC over the whole file keyed from the <c>DefaultCryptFilter</c> key
    /// (§6.4). The catalog declares the ISO/TS 32004 extension. A linearized save cannot carry the MAC and is refused.
    /// </remarks>
    public bool IntegrityProtection { get; set; }

    /// <summary>Sets <see cref="IntegrityProtection"/>.</summary>
    /// <param name="integrityProtection">Whether the saved file carries an integrity MAC.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO/TS 32004 §5.1, §6.</remarks>
    public PdfPublicKeyEncryptionOptions WithIntegrityProtection(bool integrityProtection)
    {
        IntegrityProtection = integrityProtection;
        return this;
    }

    /// <summary>Adds a recipient who opens the document with <paramref name="permissions"/>.</summary>
    /// <param name="certificate">
    /// The recipient's X.509 certificate, whose RSA public key the enveloped data's key is encrypted with. Only its public part is used;
    /// it must not be disposed before the save.
    /// </param>
    /// <param name="permissions">What the recipient may do; values outside <see cref="PdfPermissions.All"/> are ignored.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The certificate's public key is not an RSA key.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.6.5.2, Table 24: written with bit 10 set (PDF 2.0 writers shall set it) and the bits Table 24 does not use set.
    /// A certificate added more than once gets the permissions of the first list that names it (Table 23).
    /// </remarks>
    public PdfPublicKeyEncryptionOptions AddRecipient(X509Certificate2 certificate, PdfPermissions permissions)
    {
        _recipients.Add(new PdfEncryptionRecipient(certificate, permissions & PdfPermissions.All, PdfAccessLevel.User));
        return this;
    }

    /// <summary>Adds a recipient who opens the document with owner access: every permission, including changing the encryption.</summary>
    /// <param name="certificate">The recipient's X.509 certificate with an RSA public key; it must not be disposed before the save.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The certificate's public key is not an RSA key.</exception>
    /// <remarks>ISO 32000-2 §7.6.5.2, Table 24, bit 2: permits change of encryption and enables all other permissions.</remarks>
    public PdfPublicKeyEncryptionOptions AddOwner(X509Certificate2 certificate)
    {
        _recipients.Add(new PdfEncryptionRecipient(certificate, PdfPermissions.All, PdfAccessLevel.Owner));
        return this;
    }

    /// <summary>Sets <see cref="EncryptMetadata"/>.</summary>
    /// <param name="encryptMetadata">Whether metadata streams are encrypted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.5.3 step c, Table 27.</remarks>
    public PdfPublicKeyEncryptionOptions WithEncryptMetadata(bool encryptMetadata)
    {
        EncryptMetadata = encryptMetadata;
        return this;
    }
}
