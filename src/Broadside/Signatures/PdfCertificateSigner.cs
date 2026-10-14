using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// The managed default signer: signs with the private key of a certificate through the platform's cryptography (RSA with
/// RSASSA-PKCS1-v1_5).
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.3.3 and Table 260 (RSA keys up to 4096 bits, PDF 1.5); RFC 8017 §8.2; ADR 0011. The certificate is not copied:
/// keep it alive, and undisposed, until signing is done. ECDSA and RSA-PSS keys arrive with PAdES signing (issue #194).
/// </remarks>
public sealed class PdfCertificateSigner : IPdfSigner
{
    /// <summary>Initializes a new instance of the <see cref="PdfCertificateSigner"/> class.</summary>
    /// <param name="certificate">The signer's certificate, with an RSA private key.</param>
    /// <param name="certificateChain">The other certificates of the chain to embed, from the issuer upward; <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException"><paramref name="certificate"/> has no private key.</exception>
    /// <exception cref="NotSupportedException"><paramref name="certificate"/>'s key is not an RSA key.</exception>
    /// <remarks>ISO 32000-2 §12.8.3.3.</remarks>
    public PdfCertificateSigner(X509Certificate2 certificate, IEnumerable<X509Certificate2>? certificateChain = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The certificate has no private key to sign with; use an IPdfSigner for a key held elsewhere.", nameof(certificate));
        }

        CmsSignedData.RequireSupportedKey(certificate);
        Certificate = certificate;
        CertificateChain = certificateChain is null ? [] : [.. certificateChain];
    }

    /// <inheritdoc/>
    public X509Certificate2 Certificate { get; }

    /// <inheritdoc/>
    public IReadOnlyList<X509Certificate2> CertificateChain { get; }

    /// <inheritdoc/>
    public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm)
    {
        using RSA key = Certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The certificate's RSA private key is not available.");
        return key.SignHash(digest.ToArray(), digestAlgorithm, RSASignaturePadding.Pkcs1);
    }
}
