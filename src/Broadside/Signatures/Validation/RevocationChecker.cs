using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>
/// The revocation data one validation may use: decoded OCSP responses and CRLs from the options, the document security store and the
/// signature being validated, and the fetcher when the caller opted in to online checking.
/// </summary>
/// <remarks>ISO 32000-2 §12.8.3.3.2, §12.8.3.4.6 and §12.8.4.3; RFC 6960; RFC 5280 §5.</remarks>
internal sealed class RevocationSources
{
    private RevocationSources(IReadOnlyList<OcspResponse> ocsp, IReadOnlyList<RevocationList> crls, RevocationFetcher? fetcher, bool required)
    {
        Ocsp = ocsp;
        Crls = crls;
        Fetcher = fetcher;
        Required = required;
    }

    /// <summary>Gets the OCSP responses that decoded.</summary>
    public IReadOnlyList<OcspResponse> Ocsp { get; }

    /// <summary>Gets the CRLs that decoded.</summary>
    public IReadOnlyList<RevocationList> Crls { get; }

    /// <summary>Gets the fetcher, or <see langword="null"/> when nothing may be fetched.</summary>
    public RevocationFetcher? Fetcher { get; }

    /// <summary>Gets a value indicating whether a certificate without an established status is a problem.</summary>
    public bool Required { get; }

    /// <summary>The sources from encoded responses and lists; what does not decode is left out.</summary>
    public static RevocationSources Create(IEnumerable<byte[]> ocsp, IEnumerable<byte[]> crls, RevocationFetcher? fetcher, bool required) =>
        new([.. DecodeOcsp(ocsp)], [.. DecodeCrls(crls)], fetcher, required);

    /// <summary>These sources and the encoded responses and lists <paramref name="ocsp"/> and <paramref name="crls"/>.</summary>
    public RevocationSources Including(IEnumerable<byte[]> ocsp, IEnumerable<byte[]> crls) =>
        new([.. Ocsp, .. DecodeOcsp(ocsp)], [.. Crls, .. DecodeCrls(crls)], Fetcher, Required);

    private static IEnumerable<OcspResponse> DecodeOcsp(IEnumerable<byte[]> encoded)
    {
        foreach (byte[] response in encoded)
        {
            if (OcspResponse.TryDecode(response, out OcspResponse? decoded))
            {
                yield return decoded!;
            }
        }
    }

    private static IEnumerable<RevocationList> DecodeCrls(IEnumerable<byte[]> encoded)
    {
        foreach (byte[] list in encoded)
        {
            if (RevocationList.TryDecode(list, out RevocationList? decoded))
            {
                yield return decoded!;
            }
        }
    }
}

/// <summary>
/// Checks the revocation of each certificate of a built certificate path with the CRLs and OCSP responses its issuers signed, at the
/// time the path is validated at, and reports revoked and unknown certificates.
/// </summary>
/// <remarks>
/// <para>
/// RFC 5280 §6.1.3 (a)(3) and §6.3 (CRL validation: issuer, signature with a key allowed to sign CRLs, scope, completeness); RFC 6960
/// §2.2, §3.2 and §4.2.2.2 (OCSP: the response names the certificate by its issuer's name and key hashes and serial; it is signed by the
/// issuer, or by a responder whose certificate the issuer issued with the OCSP-signing extended key usage). The trust anchor is not
/// checked (RFC 5280 §6.1.1 (d)); neither is a delegated responder's own revocation.
/// </para>
/// <para>
/// When data applies (ISO 32000-2 §12.8.3.4.7 and §12.8.3.4.8): it must have been produced before the signer's (or time-stamping
/// unit's) certificate expired; a "good" status then establishes the status at time T when it was produced at or after T
/// (§12.8.3.4.8: producedAt, for a CRL its issue date, after the time in the past), or when T lies between its thisUpdate and its
/// nextUpdate (§12.8.3.4.7 for CRLs at the current time; RFC 6960 §4.2.2.1). Five minutes of clock skew are allowed. A revoked entry
/// applies whenever it was produced: the certificate counts as revoked at T when its revocation time is at or before T.
/// </para>
/// </remarks>
internal static class RevocationChecker
{
    private const string OcspSigningUsage = "1.3.6.1.5.5.7.3.9";

    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Checks every certificate of <paramref name="path"/> before the trust anchor at <paramref name="time"/> and adds a problem for each
    /// revoked or unknown one (and, when required, each whose status is not established).
    /// </summary>
    /// <param name="path">The path, signer first.</param>
    /// <param name="anchorIndex">The trust anchor's index, or -1.</param>
    /// <param name="time">The time the path is validated at (T).</param>
    /// <param name="now">The current time of the validation (data produced after it is not used).</param>
    /// <param name="tokenTime">
    /// For a time-stamping unit's path, checked at the current time, the time its token asserts: a certificate revoked after it for a
    /// reason other than a key compromise (or none) does not make the token unusable (ISO 32000-2 §12.8.3.4.8).
    /// </param>
    /// <param name="sources">The revocation data.</param>
    /// <param name="problems">Where problems go.</param>
    /// <param name="asynchronous">Whether a fetch may complete asynchronously.</param>
    /// <param name="cancellationToken">Cancels a fetch.</param>
    public static async ValueTask<IReadOnlyList<PdfCertificateRevocation>> CheckAsync(
        IReadOnlyList<X509Certificate2> path,
        int anchorIndex,
        DateTimeOffset time,
        DateTimeOffset now,
        DateTimeOffset? tokenTime,
        RevocationSources sources,
        List<PdfSignatureProblem> problems,
        bool asynchronous,
        CancellationToken cancellationToken)
    {
        var results = new List<PdfCertificateRevocation>();
        DateTimeOffset expiry = new(path[0].NotAfter.ToUniversalTime(), TimeSpan.Zero);
        for (int index = 0; index < path.Count; index++)
        {
            if (index == anchorIndex)
            {
                break;
            }

            X509Certificate2 certificate = path[index];
            X509Certificate2? issuer = index + 1 < path.Count ? path[index + 1] : null;
            if (issuer is null)
            {
                if (!CertificatePath.IsSelfIssued(certificate))
                {
                    // The issuer was not found: nothing can be authenticated (ChainIncomplete is already reported).
                    results.Add(new(certificate, PdfRevocationStatus.NotChecked, PdfRevocationSource.None, null, null, fetched: false));
                }

                break;
            }

            var statements = new List<Statement>();
            Collect(certificate, issuer, sources.Ocsp, sources.Crls, fetched: false, statements);
            Outcome outcome = Evaluate(statements, time, now, expiry);
            if (outcome.Status == PdfRevocationStatus.NotChecked && sources.Fetcher is { } fetcher)
            {
                byte[]? ocsp = await fetcher.FetchOcspAsync(certificate, issuer, asynchronous, cancellationToken).ConfigureAwait(false);
                if (ocsp is not null && OcspResponse.TryDecode(ocsp, out OcspResponse? response))
                {
                    Collect(certificate, issuer, [response!], [], fetched: true, statements);
                    outcome = Evaluate(statements, time, now, expiry);
                }

                if (outcome.Status == PdfRevocationStatus.NotChecked)
                {
                    List<byte[]> crls = await fetcher.FetchCrlsAsync(certificate, asynchronous, cancellationToken).ConfigureAwait(false);
                    var lists = new List<RevocationList>();
                    foreach (byte[] crl in crls)
                    {
                        if (RevocationList.TryDecode(crl, out RevocationList? list))
                        {
                            lists.Add(list!);
                        }
                    }

                    Collect(certificate, issuer, [], lists, fetched: true, statements);
                    outcome = Evaluate(statements, time, now, expiry);
                }
            }

            results.Add(new(certificate, outcome.Status, outcome.Statement?.Source ?? PdfRevocationSource.None, outcome.Statement?.RevocationTime, outcome.Statement?.Reason, outcome.Statement?.Fetched ?? false, outcome.Statement?.Evidence ?? default));
            Report(certificate, outcome, time, tokenTime, sources.Required, problems);
        }

        return results;
    }

    private static void Report(X509Certificate2 certificate, Outcome outcome, DateTimeOffset time, DateTimeOffset? tokenTime, bool required, List<PdfSignatureProblem> problems)
    {
        switch (outcome.Status)
        {
            case PdfRevocationStatus.Revoked:
                Statement statement = outcome.Statement!;
                DateTimeOffset revoked = statement.RevocationTime!.Value;
                bool counts = revoked <= time;
                if (counts && tokenTime is { } token && statement.Reason is not (null or X509RevocationReason.Unspecified or X509RevocationReason.KeyCompromise or X509RevocationReason.CACompromise))
                {
                    // §12.8.3.4.8: the token's time stays reliable when the unit was revoked after it for another reason.
                    counts = revoked <= token;
                }

                if (counts)
                {
                    string reason = statement.Reason is { } value ? value.ToString() : "no reason given";
                    problems.Add(new(PdfSignatureProblemKind.CertificateRevoked, string.Create(CultureInfo.InvariantCulture, $"The certificate '{certificate.Subject}' was revoked on {revoked:u} ({reason}), at or before the validation time {time:u}; the {(statement.Source == PdfRevocationSource.Ocsp ? "OCSP response" : "CRL")} says so.")));
                }

                break;
            case PdfRevocationStatus.Unknown:
                problems.Add(new(PdfSignatureProblemKind.RevocationUnknown, $"The OCSP responder does not know the certificate '{certificate.Subject}'."));
                break;
            case PdfRevocationStatus.NotChecked when required:
                problems.Add(new(PdfSignatureProblemKind.RevocationUnknown, string.Create(CultureInfo.InvariantCulture, $"No CRL or OCSP response its issuer signed establishes the revocation status of '{certificate.Subject}' at {time:u}.")));
                break;
        }
    }

    /// <summary>
    /// The outcome of the statements: revoked when any says so; else good when one applies at <paramref name="time"/>; else unknown when
    /// a responder says so; else not checked.
    /// </summary>
    private static Outcome Evaluate(List<Statement> statements, DateTimeOffset time, DateTimeOffset now, DateTimeOffset expiry)
    {
        Statement? revoked = null;
        Statement? good = null;
        Statement? unknown = null;
        foreach (Statement statement in statements)
        {
            if (statement.Produced > now + Skew)
            {
                continue;
            }

            switch (statement.Status)
            {
                case PdfRevocationStatus.Revoked:
                    if (revoked is null || statement.RevocationTime < revoked.RevocationTime)
                    {
                        revoked = statement;
                    }

                    break;
                case PdfRevocationStatus.Good when good is null && Applies(statement, time, expiry):
                    good = statement;
                    break;
                case PdfRevocationStatus.Unknown when Applies(statement, time, expiry):
                    unknown ??= statement;
                    break;
            }
        }

        return revoked is not null ? new(PdfRevocationStatus.Revoked, revoked)
            : good is not null ? new(PdfRevocationStatus.Good, good)
            : unknown is not null ? new(PdfRevocationStatus.Unknown, unknown)
            : new(PdfRevocationStatus.NotChecked, null);
    }

    /// <summary>Whether a statement establishes the status at <paramref name="time"/> (ISO 32000-2 §12.8.3.4.7, §12.8.3.4.8).</summary>
    private static bool Applies(Statement statement, DateTimeOffset time, DateTimeOffset expiry) =>
        statement.Produced <= expiry + Skew
        && (statement.Produced >= time - Skew
            || (statement.ThisUpdate <= time + Skew && time <= (statement.NextUpdate ?? statement.ThisUpdate) + Skew));

    /// <summary>The statements about <paramref name="certificate"/> that <paramref name="issuer"/>, or a responder it delegated to, signed.</summary>
    private static void Collect(
        X509Certificate2 certificate,
        X509Certificate2 issuer,
        IReadOnlyList<OcspResponse> responses,
        IReadOnlyList<RevocationList> lists,
        bool fetched,
        List<Statement> statements)
    {
        ReadOnlySpan<byte> serial = OcspResponse.TrimLeadingZeros(certificate.SerialNumberBytes).Span;
        foreach (RevocationList list in lists)
        {
            if (!Covers(list, certificate, issuer))
            {
                continue;
            }

            if (list.TryFind(serial, out DateTimeOffset date, out X509RevocationReason? reason))
            {
                if (reason != X509RevocationReason.RemoveFromCrl)
                {
                    statements.Add(new(PdfRevocationStatus.Revoked, PdfRevocationSource.Crl, list.ThisUpdate, list.ThisUpdate, list.NextUpdate, date, reason, fetched, list.Encoded));
                }
            }
            else if (list.Complete)
            {
                statements.Add(new(PdfRevocationStatus.Good, PdfRevocationSource.Crl, list.ThisUpdate, list.ThisUpdate, list.NextUpdate, null, null, fetched, list.Encoded));
            }
        }

        foreach (OcspResponse response in responses)
        {
            bool? authentic = null;
            foreach (SingleOcspResponse single in response.Responses)
            {
                if (!Names(single, certificate, issuer, serial))
                {
                    continue;
                }

                authentic ??= Authenticate(response, issuer);
                if (authentic != true)
                {
                    break;
                }

                statements.Add(new(single.Status, PdfRevocationSource.Ocsp, response.ProducedAt, single.ThisUpdate, single.NextUpdate, single.RevocationTime, single.Reason, fetched, response.Encoded));
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="list"/> is <paramref name="issuer"/>'s list for <paramref name="certificate"/>: issued under its name,
    /// signed with its key, which may sign CRLs, and scoped to certificates like this one (RFC 5280 §6.3.3 (b), (f), (g)).
    /// </summary>
    private static bool Covers(RevocationList list, X509Certificate2 certificate, X509Certificate2 issuer)
    {
        if (!CertificatePath.NamesMatch(list.Issuer, certificate.IssuerName) || !CertificatePath.NamesMatch(list.Issuer, issuer.SubjectName))
        {
            return false;
        }

        bool authority = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault() is { CertificateAuthority: true };
        if ((list.OnlyUserCertificates && authority) || (list.OnlyAuthorityCertificates && !authority))
        {
            return false;
        }

        if (issuer.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } usage && (usage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0)
        {
            return false;
        }

        return CertificatePath.VerifySigned(list.SignedData.Span, list.SignatureAlgorithm, list.Signature, issuer.PublicKey) == SignatureCheck.Valid;
    }

    /// <summary>Whether the <c>CertID</c> of <paramref name="single"/> names <paramref name="certificate"/> (RFC 6960 §4.1.1).</summary>
    private static bool Names(SingleOcspResponse single, X509Certificate2 certificate, X509Certificate2 issuer, ReadOnlySpan<byte> serial)
    {
        if (!single.SerialNumber.Span.SequenceEqual(serial)
            || !SignatureAlgorithms.TryGetDigest(single.HashAlgorithm.Oid, out HashAlgorithmName algorithm)
            || !IncrementalDigest.IsSupported(algorithm))
        {
            return false;
        }

        return IncrementalDigest.HashData(algorithm, certificate.IssuerName.RawData).AsSpan().SequenceEqual(single.IssuerNameHash)
            && IncrementalDigest.HashData(algorithm, issuer.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(single.IssuerKeyHash);
    }

    /// <summary>
    /// Whether <paramref name="response"/> is signed by <paramref name="issuer"/>, or by a responder certificate it carries that
    /// <paramref name="issuer"/> issued for OCSP signing and that was valid when the response was produced (RFC 6960 §4.2.2.2).
    /// </summary>
    private static bool Authenticate(OcspResponse response, X509Certificate2 issuer)
    {
        if (IsResponder(response, issuer) && Verifies(response, issuer))
        {
            return true;
        }

        foreach (byte[] encoded in response.Certificates)
        {
            X509Certificate2 responder;
            try
            {
                responder = X509CertificateLoader.LoadCertificate(encoded);
            }
            catch (CryptographicException)
            {
                continue;
            }

            using (responder)
            {
                DateTime produced = response.ProducedAt.UtcDateTime;
                if (IsResponder(response, responder)
                    && CertificatePath.NamesMatch(responder.IssuerName, issuer.SubjectName)
                    && produced >= responder.NotBefore.ToUniversalTime() - Skew
                    && produced <= responder.NotAfter.ToUniversalTime() + Skew
                    && responder.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(static usage => usage.EnhancedKeyUsages.Cast<Oid>().Any(static oid => oid.Value == OcspSigningUsage))
                    && CertificatePath.VerifyIssuedBy(responder, issuer) == SignatureCheck.Valid
                    && Verifies(response, responder))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Verifies(OcspResponse response, X509Certificate2 signer) =>
        CertificatePath.VerifySigned(response.SignedData.Span, response.SignatureAlgorithm, response.Signature, signer.PublicKey) == SignatureCheck.Valid;

    /// <summary>Whether the response's responder identifier names <paramref name="certificate"/>: by name, or by the SHA-1 of its key.</summary>
    private static bool IsResponder(OcspResponse response, X509Certificate2 certificate)
    {
        if (!response.ResponderName.IsEmpty)
        {
            try
            {
                return CertificatePath.NamesMatch(new X500DistinguishedName(response.ResponderName.Span), certificate.SubjectName);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        return IncrementalDigest.HashData(HashAlgorithmName.SHA1, certificate.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(response.ResponderKeyHash.Span);
    }

    /// <summary>What one CRL or OCSP response says about one certificate.</summary>
    private sealed record Statement(
        PdfRevocationStatus Status,
        PdfRevocationSource Source,
        DateTimeOffset Produced,
        DateTimeOffset ThisUpdate,
        DateTimeOffset? NextUpdate,
        DateTimeOffset? RevocationTime,
        X509RevocationReason? Reason,
        bool Fetched,
        ReadOnlyMemory<byte> Evidence);

    private readonly record struct Outcome(PdfRevocationStatus Status, Statement? Statement);
}
