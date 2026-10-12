using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// Produces the signature value of a document signature from a digest: the extension point signing goes through, so that the private
/// key can stay in a hardware security module, a smart card or a remote signing service.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.1 and §12.8.3.3; RFC 5652 §5.4 and §5.5; ADR 0010. The document computes the byte range digest itself, builds
/// the CMS signed attributes (content type and message digest), and asks the signer to sign the digest of their DER encoding, the
/// value RFC 5652 §5.4 says the signature is computed over. The signer never sees the document. It returns the raw signature value:
/// for an RSA key, the RSASSA-PKCS1-v1_5 signature (RFC 8017 §8.2) of the digest with the digest algorithm's
/// <c>DigestInfo</c>, which is what <see cref="RSA.SignHash(byte[], HashAlgorithmName, RSASignaturePadding)"/> with
/// <see cref="RSASignaturePadding.Pkcs1"/> returns. The document then builds the CMS <c>SignedData</c> around it with
/// <see cref="Certificate"/> and <see cref="CertificateChain"/>.
/// </para>
/// <para>
/// <see cref="PdfCertificateSigner"/> is the managed default, over a certificate whose private key the platform holds. A signer is
/// asked once per signature; a document may be signed from any thread but not from several at once (signing is a mutation).
/// </para>
/// </remarks>
public interface IPdfSigner
{
    /// <summary>Gets the signer's certificate, whose public key verifies the signature. Its private key is not needed.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.3.1: "At minimum the CMS object shall include the signer's X.509 signing certificate."</remarks>
    X509Certificate2 Certificate { get; }

    /// <summary>
    /// Gets the other certificates of the signer's chain to embed in the signature, from the issuer of <see cref="Certificate"/>
    /// upward; empty to embed only <see cref="Certificate"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.3.1: the handler "should capture the chain of certificates ... before signing".</remarks>
    IReadOnlyList<X509Certificate2> CertificateChain { get; }

    /// <summary>Signs <paramref name="digest"/>, the digest of the signed attributes, with the signer's private key.</summary>
    /// <param name="digest">The digest to sign.</param>
    /// <param name="digestAlgorithm">The algorithm <paramref name="digest"/> was computed with.</param>
    /// <returns>The signature value: for an RSA key, the RSASSA-PKCS1-v1_5 signature.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.3; RFC 5652 §5.5. Throw to abandon the signature; nothing is written then.</remarks>
    byte[] SignDigest(ReadOnlySpan<byte> digest, HashAlgorithmName digestAlgorithm);

    /// <summary>Signs <paramref name="digest"/> asynchronously, for a signer that waits on a device or a remote service.</summary>
    /// <param name="digest">The digest to sign.</param>
    /// <param name="digestAlgorithm">The algorithm <paramref name="digest"/> was computed with.</param>
    /// <param name="cancellationToken">Cancels the signing.</param>
    /// <returns>The signature value, as <see cref="SignDigest"/> returns it.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.8.3.3. <see cref="PdfDocument.SignAsync"/> calls this, <see cref="PdfDocument.Sign"/> calls
    /// <see cref="SignDigest"/>. The default implementation calls <see cref="SignDigest"/>.
    /// </remarks>
    ValueTask<byte[]> SignDigestAsync(ReadOnlyMemory<byte> digest, HashAlgorithmName digestAlgorithm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SignDigest(digest.Span, digestAlgorithm));
    }
}
