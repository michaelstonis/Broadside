using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// What signature validation trusts: the certificates a signer's chain must reach, other certificates that may complete a chain, and
/// the time at which certificates must be valid.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 ("the signer's certificate shall be determined and verified by the signature handler to match with any of the
/// validation parameters") and §12.8.3.3.1 ("the policy of how to establish trusted identity lists ... is up to the validation
/// signature handler"); RFC 5280 §6.1 for the trust anchors and the validation time. Nothing is trusted by default and nothing is
/// fetched: the operating system's certificate store is not consulted and no certificate is downloaded. Revocation is not checked
/// here.
/// </remarks>
public sealed class PdfSignatureValidationOptions
{
    private IReadOnlyList<X509Certificate2> _trustAnchors = [];
    private IReadOnlyList<X509Certificate2> _extraCertificates = [];

    /// <summary>
    /// Gets or sets the trust anchors: a signer's certificate chain is trusted when it reaches one of them, whether a root or an
    /// intermediate. Empty by default, so no chain is trusted until anchors are given.
    /// </summary>
    /// <remarks>RFC 5280 §6.1.1 (d): an anchor's own validity and constraints are not checked; it is trusted as given.</remarks>
    public IReadOnlyList<X509Certificate2> TrustAnchors
    {
        get => _trustAnchors;
        set => _trustAnchors = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets other certificates that may complete a chain the signature does not carry in full (intermediates from a
    /// document security store or a local cache). They are not trusted. Empty by default.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.3.1 (the chain in the CMS object) and §12.8.4.3 (the document security store).</remarks>
    public IReadOnlyList<X509Certificate2> ExtraCertificates
    {
        get => _extraCertificates;
        set => _extraCertificates = [.. value ?? throw new ArgumentNullException(nameof(value))];
    }

    /// <summary>
    /// Gets or sets the time at which every certificate of a chain must be within its validity period; <see langword="null"/> (the
    /// default) for the time validation runs.
    /// </summary>
    /// <remarks>RFC 5280 §6.1.1 (b) and §4.1.2.5. A verified timestamp setting this time arrives with issue #197.</remarks>
    public DateTimeOffset? ValidationTime { get; set; }

    /// <summary>Sets <see cref="TrustAnchors"/>.</summary>
    /// <param name="trustAnchors">The certificates to trust.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.3.1; RFC 5280 §6.1.1 (d).</remarks>
    public PdfSignatureValidationOptions WithTrustAnchors(params IEnumerable<X509Certificate2> trustAnchors)
    {
        TrustAnchors = [.. trustAnchors];
        return this;
    }

    /// <summary>Sets <see cref="ExtraCertificates"/>.</summary>
    /// <param name="extraCertificates">Certificates that may complete a chain.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.3.3.1.</remarks>
    public PdfSignatureValidationOptions WithExtraCertificates(params IEnumerable<X509Certificate2> extraCertificates)
    {
        ExtraCertificates = [.. extraCertificates];
        return this;
    }

    /// <summary>Sets <see cref="ValidationTime"/>.</summary>
    /// <param name="validationTime">The time, or <see langword="null"/> for the time validation runs.</param>
    /// <returns>These options.</returns>
    /// <remarks>RFC 5280 §6.1.1 (b).</remarks>
    public PdfSignatureValidationOptions WithValidationTime(DateTimeOffset? validationTime)
    {
        ValidationTime = validationTime;
        return this;
    }
}
