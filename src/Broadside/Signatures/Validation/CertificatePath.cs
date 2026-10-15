using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>
/// Builds a signer's certificate path to a trust anchor in managed code and checks it: each certificate's signature with its issuer's
/// key, validity periods at the validation time, the issuers' basic constraints, key usage and path length, and the signer's key usage.
/// </summary>
/// <remarks>
/// RFC 5280 §6.1 (basic path validation), the subset that needs no revocation data and no policy processing: §6.1.3 (a)(1) signature,
/// (a)(2) validity, (a)(4) issuer name; §6.1.4 (k) basic constraints, (l) and (m) path length, (n) key usage; §4.2.1.3 for the
/// signer's key usage. Built here rather than with <see cref="X509Chain"/> so the result is the same on every platform, works
/// in the browser where the platform chain engine does not, never downloads an issuer, and uses the same algorithm table as the CMS
/// signature (<see cref="SignatureAlgorithms"/>). Revocation and timestamps arrive with issue #197.
/// </remarks>
internal static class CertificatePath
{
    /// <summary>The longest path built; a longer one is reported as incomplete.</summary>
    public const int MaxLength = 16;

    /// <summary>
    /// Builds the path from <paramref name="signer"/> through <paramref name="pool"/> to one of <paramref name="anchors"/>, checks it at
    /// <paramref name="time"/>, and adds a problem for each check that fails.
    /// </summary>
    /// <returns>The path, signer first, as far as it was built.</returns>
    public static IReadOnlyList<X509Certificate2> Build(
        X509Certificate2 signer,
        IReadOnlyList<X509Certificate2> pool,
        IReadOnlyList<X509Certificate2> anchors,
        DateTimeOffset time,
        List<PdfSignatureProblem> problems)
    {
        var path = new List<X509Certificate2> { signer };
        X509Certificate2 current = signer;
        int anchorIndex = -1;
        while (true)
        {
            if (IsAnchor(current, anchors))
            {
                anchorIndex = path.Count - 1;
                break;
            }

            if (path.Count >= MaxLength)
            {
                problems.Add(new(PdfSignatureProblemKind.ChainIncomplete, $"No trust anchor within {MaxLength} certificates of the signer."));
                break;
            }

            if (FindIssuer(current, anchors, path, problems) is { } anchor)
            {
                path.Add(anchor);
                anchorIndex = path.Count - 1;
                break;
            }

            if (FindIssuer(current, pool, path, problems) is { } issuer)
            {
                path.Add(issuer);
                current = issuer;
                continue;
            }

            problems.Add(IsSelfSigned(current)
                ? new(PdfSignatureProblemKind.ChainUntrusted, $"The chain ends at the self-signed certificate '{current.Subject}', which is not a trust anchor.")
                : new(PdfSignatureProblemKind.ChainIncomplete, $"The issuer '{current.Issuer}' of '{current.Subject}' is not among the signature's, the extra or the trusted certificates."));
            break;
        }

        Check(path, anchorIndex, time, problems);
        return path;
    }

    private static void Check(List<X509Certificate2> path, int anchorIndex, DateTimeOffset time, List<PdfSignatureProblem> problems)
    {
        DateTime utc = time.UtcDateTime;
        int intermediates = 0;
        for (int index = 0; index < path.Count; index++)
        {
            X509Certificate2 certificate = path[index];
            bool anchor = index == anchorIndex;
            if (anchor && index > 0)
            {
                // RFC 5280 §6.1.1 (d): a trust anchor is an input to validation, trusted as given.
                break;
            }

            if (utc > certificate.NotAfter.ToUniversalTime())
            {
                problems.Add(new(PdfSignatureProblemKind.CertificateExpired, string.Create(CultureInfo.InvariantCulture, $"The certificate '{certificate.Subject}' expired on {certificate.NotAfter.ToUniversalTime():u}.")));
            }
            else if (utc < certificate.NotBefore.ToUniversalTime())
            {
                problems.Add(new(PdfSignatureProblemKind.CertificateNotYetValid, string.Create(CultureInfo.InvariantCulture, $"The certificate '{certificate.Subject}' is not valid before {certificate.NotBefore.ToUniversalTime():u}.")));
            }

            X509KeyUsageExtension? usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (index == 0)
            {
                if (usage is not null && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
                {
                    problems.Add(new(PdfSignatureProblemKind.KeyUsageNotPermitted, $"The signer certificate '{certificate.Subject}' allows neither digital signatures nor non-repudiation ({usage.KeyUsages})."));
                }

                continue;
            }

            X509BasicConstraintsExtension? constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (constraints is not { CertificateAuthority: true })
            {
                problems.Add(new(PdfSignatureProblemKind.IssuerNotCertificateAuthority, $"The issuer '{certificate.Subject}' is not a certification authority."));
            }
            else if (usage is not null && (usage.KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0)
            {
                problems.Add(new(PdfSignatureProblemKind.IssuerNotCertificateAuthority, $"The issuer '{certificate.Subject}' may not sign certificates ({usage.KeyUsages})."));
            }
            else if (constraints.HasPathLengthConstraint && intermediates > constraints.PathLengthConstraint)
            {
                problems.Add(new(PdfSignatureProblemKind.PathLengthExceeded, string.Create(CultureInfo.InvariantCulture, $"The issuer '{certificate.Subject}' allows {constraints.PathLengthConstraint} certification authorities below it; the path has {intermediates}.")));
            }

            if (!IsSelfIssued(certificate))
            {
                intermediates++;
            }
        }
    }

    private static bool IsAnchor(X509Certificate2 certificate, IReadOnlyList<X509Certificate2> anchors)
    {
        foreach (X509Certificate2 anchor in anchors)
        {
            if (anchor.RawDataMemory.Span.SequenceEqual(certificate.RawDataMemory.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static X509Certificate2? FindIssuer(X509Certificate2 subject, IReadOnlyList<X509Certificate2> candidates, List<X509Certificate2> path, List<PdfSignatureProblem> problems)
    {
        foreach (X509Certificate2 candidate in candidates)
        {
            if (path.Exists(member => member.RawDataMemory.Span.SequenceEqual(candidate.RawDataMemory.Span)) || !NamesMatch(subject.IssuerName, candidate.SubjectName) || !KeyIdentifiersMatch(subject, candidate))
            {
                continue;
            }

            switch (VerifyIssuedBy(subject, candidate))
            {
                case SignatureCheck.Valid:
                    return candidate;
                case SignatureCheck.Unsupported:
                    problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The signature of '{subject.Subject}' by '{candidate.Subject}' uses an algorithm that cannot be verified here."));
                    return candidate;
            }
        }

        return null;
    }

    private static bool IsSelfIssued(X509Certificate2 certificate) => NamesMatch(certificate.IssuerName, certificate.SubjectName);

    private static bool IsSelfSigned(X509Certificate2 certificate) => IsSelfIssued(certificate) && VerifyIssuedBy(certificate, certificate) == SignatureCheck.Valid;

    /// <summary>Whether two names are the same: the same encoding, or the same decoded text (RFC 5280 §7.1 allows more; this suffices for signers).</summary>
    private static bool NamesMatch(X500DistinguishedName first, X500DistinguishedName second) =>
        first.RawData.AsSpan().SequenceEqual(second.RawData)
        || (first.Name.Length > 0 && string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The subject's authority key identifier and the candidate's subject key identifier agree, or one is absent (RFC 5280 §4.2.1.1).</summary>
    private static bool KeyIdentifiersMatch(X509Certificate2 subject, X509Certificate2 candidate)
    {
        ReadOnlyMemory<byte>? authority = subject.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().FirstOrDefault()?.KeyIdentifier;
        ReadOnlyMemory<byte>? key = candidate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault()?.SubjectKeyIdentifierBytes;
        return authority is not { } a || key is not { } k || a.Span.SequenceEqual(k.Span);
    }

    /// <summary>Verifies the signature on <paramref name="subject"/> with <paramref name="issuer"/>'s key (RFC 5280 §4.1.1.2, §4.1.1.3).</summary>
    private static SignatureCheck VerifyIssuedBy(X509Certificate2 subject, X509Certificate2 issuer)
    {
        ReadOnlyMemory<byte> toBeSigned;
        AlgorithmIdentifier algorithm;
        byte[] signature;
        try
        {
            AsnReader certificate = new AsnReader(subject.RawDataMemory, AsnEncodingRules.BER).ReadSequence();
            toBeSigned = certificate.ReadEncodedValue();
            algorithm = AlgorithmIdentifier.Read(certificate);
            signature = certificate.ReadBitString(out int unused);
            if (unused != 0)
            {
                return SignatureCheck.Invalid;
            }
        }
        catch (AsnContentException)
        {
            return SignatureCheck.Invalid;
        }

        if (!SignatureAlgorithms.TryGetSignatureDigest(algorithm, null, out HashAlgorithmName digestAlgorithm))
        {
            return SignatureCheck.Unsupported;
        }

        byte[] digest = IncrementalDigest.HashData(digestAlgorithm, toBeSigned.Span);
        return SignatureAlgorithms.Verify(algorithm, digestAlgorithm, issuer.PublicKey, digest, signature);
    }
}
