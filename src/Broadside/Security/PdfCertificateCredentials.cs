using System.Security.Cryptography.X509Certificates;

namespace Broadside.Security;

/// <summary>Certificates with their private keys, offered to open a document encrypted for certificate recipients.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.5.1: the reader scans the document's recipient lists for one of its certificates and decrypts the enveloped data
/// with that certificate's private key. Each certificate is matched by issuer and serial number, or by subject key identifier, the
/// way the CMS recipient info (RFC 5652 §6.2) names it; the first one that matches opens the document.
/// </para>
/// <para>
/// A certificate's private key is read through the certificate (<see cref="X509Certificate2.HasPrivateKey"/>), so a certificate from
/// a store, a PKCS #12 file, or one paired with its key by <c>CopyWithPrivateKey</c> all work. The engine never reads a certificate
/// store by itself, and does not dispose the certificates. Certificates cannot be used in a browser (WebAssembly), where the
/// platform has no X.509 or CMS support.
/// </para>
/// </remarks>
public sealed class PdfCertificateCredentials : PdfCredentials
{
    private readonly X509Certificate2[] _certificates;

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateCredentials"/> class with one certificate.</summary>
    /// <param name="certificate">A certificate with its private key.</param>
    public PdfCertificateCredentials(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _certificates = [certificate];
    }

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateCredentials"/> class with candidate certificates, tried in order.</summary>
    /// <param name="certificates">Certificates, such as an <see cref="X509Certificate2Collection"/>; those without a private key match but cannot decrypt.</param>
    public PdfCertificateCredentials(IEnumerable<X509Certificate2> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        _certificates = [.. certificates];
        if (Array.IndexOf(_certificates, null) >= 0)
        {
            throw new ArgumentException("The certificates cannot contain null.", nameof(certificates));
        }
    }

    /// <summary>Gets the certificates, in the order they are tried.</summary>
    public IReadOnlyList<X509Certificate2> Certificates => _certificates;

    /// <inheritdoc/>
    public override string ToString() => "PdfCertificateCredentials";
}
