using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// The managed default signer: signs with the private key of a certificate through the platform's cryptography, RSA (PKCS #1 v1.5
/// or RSASSA-PSS) or ECDSA on P-256, P-384 or P-521.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.3.3 and Table 260 (RSA keys up to 4096 bits, PDF 1.5; ECDSA, PDF 2.0); ISO/TS 32002 §5.1.3 (curves); RFC 8017
/// §8.1 and §8.2; ADR 0011. The certificate is not copied: keep it alive, and undisposed, until signing is done.
/// </para>
/// <para>
/// ECDSA signs every digest on every platform. RSA signs SHA-3 digests only where the platform's RSA has SHA-3 (Windows and Linux,
/// not macOS, iOS or the browser); elsewhere <see cref="SignDigest"/> throws <see cref="NotSupportedException"/>, and an
/// <see cref="IPdfSigner"/> over a key that can is the way. The platform cannot load a private key for an <c>id-RSASSA-PSS</c>
/// certificate key either; sign such a certificate through an <see cref="IPdfSigner"/>.
/// </para>
/// </remarks>
public sealed class PdfCertificateSigner : IPdfSigner
{
    private readonly SigningKey _key;

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateSigner"/> class.</summary>
    /// <param name="certificate">The signer's certificate, with an RSA or ECDSA private key.</param>
    /// <param name="certificateChain">The other certificates of the chain to embed, from the issuer upward; <see langword="null"/> for none.</param>
    /// <param name="rsaSignaturePadding">
    /// For an RSA key, <see cref="RSASignaturePadding.Pkcs1"/> (when <see langword="null"/>, the default) or
    /// <see cref="RSASignaturePadding.Pss"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="certificate"/> has no private key.</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="certificate"/>'s key is neither an RSA key nor an ECDSA key on P-256, P-384 or P-521, or is an
    /// <c>id-RSASSA-PSS</c> key, whose private key the platform does not load.
    /// </exception>
    /// <remarks>ISO 32000-2 §12.8.3.3; ISO/TS 32002 §5.1.3; RFC 4056 §2.</remarks>
    public PdfCertificateSigner(
        X509Certificate2 certificate,
        IEnumerable<X509Certificate2>? certificateChain = null,
        RSASignaturePadding? rsaSignaturePadding = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The certificate has no private key to sign with; use an IPdfSigner for a key held elsewhere.", nameof(certificate));
        }

        _key = CmsSignedData.ReadKey(certificate);
        if (_key.Type == SigningKeyType.RsaPss)
        {
            throw new NotSupportedException(
                "The platform does not load the private key of an id-RSASSA-PSS certificate key; sign through an IPdfSigner that holds the RSA key.");
        }

        Certificate = certificate;
        CertificateChain = certificateChain is null ? [] : [.. certificateChain];
        RsaSignaturePadding = rsaSignaturePadding ?? RSASignaturePadding.Pkcs1;
    }

    /// <inheritdoc/>
    public X509Certificate2 Certificate { get; }

    /// <inheritdoc/>
    public IReadOnlyList<X509Certificate2> CertificateChain { get; }

    /// <inheritdoc/>
    public RSASignaturePadding RsaSignaturePadding { get; }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">An RSA key and a SHA-3 digest on a platform whose RSA has no SHA-3.</exception>
    public byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm)
    {
        if (_key.Type == SigningKeyType.Ecdsa)
        {
            using ECDsa ecdsa = Certificate.GetECDsaPrivateKey()
                ?? throw new InvalidOperationException("The certificate's ECDSA private key is not available.");
            return ecdsa.SignHash(digest.ToArray(), DSASignatureFormat.Rfc3279DerSequence);
        }

        using RSA key = Certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The certificate's RSA private key is not available.");
        try
        {
            return key.SignHash(digest.ToArray(), digestAlgorithm, RsaSignaturePadding);
        }
        catch (CryptographicException exception) when (CmsSignedData.IsSha3(digestAlgorithm))
        {
            throw new NotSupportedException(
                $"The platform's RSA cannot sign a {digestAlgorithm.Name} digest; use an ECDSA key, a SHA-2 digest, or an IPdfSigner whose RSA key can.",
                exception);
        }
    }
}
