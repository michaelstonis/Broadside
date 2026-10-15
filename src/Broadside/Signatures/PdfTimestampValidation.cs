using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Signatures;

/// <summary>
/// The validation of an RFC 3161 time-stamp token: its signature, its message imprint, the time-stamping unit's signing-certificate
/// attribute, extended key usage and certificate path (checked, with its revocation, at the time validation runs), and the time it
/// asserts.
/// </summary>
/// <remarks>
/// RFC 3161 §2.3 (the unit's certificate), §2.4.1 and RFC 5816 (the ESS signing-certificate attribute), §2.4.2 (TSTInfo) and Appendix A
/// (the signature time-stamp attribute); ISO 32000-2 §12.8.3.4.5 c) and §12.8.3.4.8 (the unit's path and revocation at the current
/// time; the rules for a revoked unit).
/// </remarks>
public sealed class PdfTimestampValidation
{
    internal PdfTimestampValidation(
        DateTimeOffset time,
        string? policy,
        HashAlgorithmName? digestAlgorithm,
        X509Certificate2? signerCertificate,
        IReadOnlyList<X509Certificate2> certificatePath,
        IReadOnlyList<PdfCertificateRevocation> revocations,
        IReadOnlyList<PdfSignatureProblem> problems)
    {
        Time = time;
        Policy = policy;
        DigestAlgorithm = digestAlgorithm;
        SignerCertificate = signerCertificate;
        CertificatePath = certificatePath;
        Revocations = revocations;
        Problems = problems;
        Status = PdfSignatureValidation.StatusOf(problems);
    }

    /// <summary>
    /// Gets the outcome: <see cref="PdfSignatureStatus.Valid"/> when the token checks out and its unit's chain reaches a trust anchor;
    /// only then is <see cref="Time"/> used as the signature's validation time.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.3.4.8.</remarks>
    public PdfSignatureStatus Status { get; }

    /// <summary>Gets the reasons the token is not valid; empty when it is.</summary>
    /// <remarks>RFC 3161 §2.4.2.</remarks>
    public IReadOnlyList<PdfSignatureProblem> Problems { get; }

    /// <summary>Gets the time the token asserts (<c>genTime</c>); <see cref="DateTimeOffset.MinValue"/> when it could not be read.</summary>
    /// <remarks>RFC 3161 §2.4.2.</remarks>
    public DateTimeOffset Time { get; }

    /// <summary>Gets the policy the token was issued under, as a dotted object identifier, or <see langword="null"/> when unreadable.</summary>
    /// <remarks>RFC 3161 §2.4.2.</remarks>
    public string? Policy { get; }

    /// <summary>Gets the digest algorithm of the token's message imprint, or <see langword="null"/> when it was not read.</summary>
    /// <remarks>RFC 3161 §2.4.2 (messageImprint).</remarks>
    public HashAlgorithmName? DigestAlgorithm { get; }

    /// <summary>Gets the time-stamping unit's certificate, or <see langword="null"/> when the token does not carry it.</summary>
    /// <remarks>RFC 3161 §2.3.</remarks>
    public X509Certificate2? SignerCertificate { get; }

    /// <summary>Gets the unit's certificate path that was built, unit first; empty when there is no unit certificate.</summary>
    /// <remarks>RFC 5280 §6.1; ISO 32000-2 §12.8.3.4.5 c).</remarks>
    public IReadOnlyList<X509Certificate2> CertificatePath { get; }

    /// <summary>Gets the revocation status of each certificate of <see cref="CertificatePath"/> but the trust anchor, at the current time.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.4.8.</remarks>
    public IReadOnlyList<PdfCertificateRevocation> Revocations { get; }
}
